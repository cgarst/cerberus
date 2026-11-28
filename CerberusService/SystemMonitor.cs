using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace RoomAgent
{
    public class SystemMetricsSnapshot
    {
        public string CpuName { get; set; }
        public double? CpuPercent { get; set; }
        public int? CpuTempC { get; set; }
        public string RamName { get; set; }
        public double? RamPercent { get; set; }
        public double? RamUsedGb { get; set; }
        public double? RamTotalGb { get; set; }
        public int? GpuPercent { get; set; }
        public int? GpuTempC { get; set; }
        public int? GpuHotspotTempC { get; set; }
        public double? GpuVramUsedGb { get; set; }
        public double? GpuVramTotalGb { get; set; }
        public string GpuName { get; set; }
        public double? Fps { get; set; }
        public double? FrametimeMs { get; set; }
        public double?[] FpsHistory { get; set; }
        public double?[] FrametimeHistoryMs { get; set; }
        public string FrameAppName { get; set; }
        public string FrameAppIcon { get; set; }
        public string FrameAppHeroUrl { get; set; }
        public bool? OsdEnabled { get; set; }
        public string OsdMode { get; set; }
        public string[] AvailableOsdModes { get; set; }
    }

    public interface ISystemMonitor : IDisposable
    {
        SystemMetricsSnapshot GetSnapshot();
        bool TryGetHeroImage(string key, out byte[] bytes, out string contentType);
    }

    public class WindowsSystemMonitor : ISystemMonitor
    {
        private const int SampleIntervalMs = 1000;
        private const int FrameHistoryCapacity = 60;

        // AMD ADL PMLog sensor IDs (adl_structures.h -> ADL_PMLOG_SENSORS)
        private const int AdlSensorTempEdge = 8;
        private const int AdlSensorTempHotspot = 9;
        private const int AdlSensorActivityGfx = 19;

        private readonly ILogger _logger;
        private readonly object _lock = new object();
        private readonly Timer _timer;

        private bool _disposed;
        private bool _hasPrevCpuTimes;
        private ulong _prevIdle;
        private ulong _prevKernel;
        private ulong _prevUser;

        private bool _adlUnavailable;
        private bool _adlWarned;
        private IntPtr _adlContext = IntPtr.Zero;
        private int _adlAdapterIndex = -1;
        private string _adlAdapterName;
        private double? _adlTotalVramGb;
        private readonly string _cpuName;
        private readonly string _ramName;
        private readonly double?[] _fpsRing = new double?[FrameHistoryCapacity];
        private readonly double?[] _frametimeRing = new double?[FrameHistoryCapacity];
        private int _ringIndex;
        private int _ringCount;
        private readonly Dictionary<string, FrameAppMeta> _appMetaCache = new Dictionary<string, FrameAppMeta>(StringComparer.OrdinalIgnoreCase);
        private readonly SteamHeroResolver _heroResolver;

        private struct FrameAppMeta
        {
            public string Name;
            public string Icon;
        }

        private SystemMetricsSnapshot _latest = new SystemMetricsSnapshot();

        // Keep delegate alive for the lifetime of ADL2_Main_Control_Create
        private static readonly AdlMainMallocCallback AdlMallocDelegate = AdlMalloc;

        public WindowsSystemMonitor(ILogger logger, AgentConfig config = null)
        {
            _logger = logger;
            _heroResolver = new SteamHeroResolver(config != null ? config.SteamPath : null);
            _cpuName = ReadCpuName();
            _ramName = ReadRamName();
            Sample();
            _timer = new Timer(OnTimerTick, null, SampleIntervalMs, SampleIntervalMs);
        }

        public bool TryGetHeroImage(string key, out byte[] bytes, out string contentType)
        {
            return _heroResolver.TryGetHeroImage(key, out bytes, out contentType);
        }

        public SystemMetricsSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                string liveOsdMode;
                string[] liveAvailableOsdModes;
                bool? liveOsdEnabled;
                NativeInterop.GetOsdState(out liveOsdMode, out liveAvailableOsdModes, out liveOsdEnabled);
                return new SystemMetricsSnapshot
                {
                    CpuName = _latest.CpuName,
                    CpuPercent = _latest.CpuPercent,
                    CpuTempC = _latest.CpuTempC,
                    RamName = _latest.RamName,
                    RamPercent = _latest.RamPercent,
                    RamUsedGb = _latest.RamUsedGb,
                    RamTotalGb = _latest.RamTotalGb,
                    GpuPercent = _latest.GpuPercent,
                    GpuTempC = _latest.GpuTempC,
                    GpuHotspotTempC = _latest.GpuHotspotTempC,
                    GpuVramUsedGb = _latest.GpuVramUsedGb,
                    GpuVramTotalGb = _latest.GpuVramTotalGb,
                    GpuName = _latest.GpuName,
                    Fps = _latest.Fps,
                    FrametimeMs = _latest.FrametimeMs,
                    FpsHistory = _latest.FpsHistory != null ? (double?[])_latest.FpsHistory.Clone() : null,
                    FrametimeHistoryMs = _latest.FrametimeHistoryMs != null ? (double?[])_latest.FrametimeHistoryMs.Clone() : null,
                    FrameAppName = _latest.FrameAppName,
                    FrameAppIcon = _latest.FrameAppIcon,
                    FrameAppHeroUrl = _latest.FrameAppHeroUrl,
                    OsdEnabled = liveOsdEnabled ?? _latest.OsdEnabled,
                    OsdMode = liveOsdMode ?? _latest.OsdMode,
                    AvailableOsdModes = liveAvailableOsdModes ?? _latest.AvailableOsdModes
                };
            }
        }

        private void OnTimerTick(object state)
        {
            Sample();
        }

        private void Sample()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                SystemMetricsSnapshot next = new SystemMetricsSnapshot
                {
                    CpuName = _cpuName,
                    CpuPercent = _latest.CpuPercent,
                    RamName = _ramName
                };

                double? cpu = ReadCpuPercent();
                if (cpu.HasValue)
                {
                    next.CpuPercent = cpu.Value;
                }

                int? cpuTempC;
                double? fps;
                double? frametimeMs;
                ReadAfterburnerMetrics(out cpuTempC, out fps, out frametimeMs);
                next.CpuTempC = cpuTempC;
                next.Fps = fps;
                next.FrametimeMs = frametimeMs;

                if (fps.HasValue || frametimeMs.HasValue)
                {
                    _fpsRing[_ringIndex] = fps;
                    _frametimeRing[_ringIndex] = frametimeMs;
                    _ringIndex = (_ringIndex + 1) % FrameHistoryCapacity;
                    if (_ringCount < FrameHistoryCapacity)
                    {
                        _ringCount++;
                    }
                    string appName;
                    string appIcon;
                    string appHeroUrl;
                    ReadActiveRtssApp(out appName, out appIcon, out appHeroUrl);
                    next.FrameAppName = appName;
                    next.FrameAppIcon = appIcon;
                    next.FrameAppHeroUrl = appHeroUrl;
                }
                else if (_ringCount > 0)
                {
                    Array.Clear(_fpsRing, 0, _fpsRing.Length);
                    Array.Clear(_frametimeRing, 0, _frametimeRing.Length);
                    _ringIndex = 0;
                    _ringCount = 0;
                }
                next.FpsHistory = BuildHistoryArray(_fpsRing);
                next.FrametimeHistoryMs = BuildHistoryArray(_frametimeRing);
                string osdMode;
                string[] availableOsdModes;
                bool? osdEnabled;
                NativeInterop.GetOsdState(out osdMode, out availableOsdModes, out osdEnabled);
                next.OsdEnabled = osdEnabled;
                next.OsdMode = osdMode;
                next.AvailableOsdModes = availableOsdModes;

                double? ramPct;
                double? ramUsedGb;
                double? ramTotalGb;
                if (ReadRamUsage(out ramPct, out ramUsedGb, out ramTotalGb))
                {
                    next.RamPercent = ramPct;
                    next.RamUsedGb = ramUsedGb;
                    next.RamTotalGb = ramTotalGb;
                }

                ReadAmdGpuTelemetry(next);
                _latest = next;
            }
        }

        private double? ReadCpuPercent()
        {
            FILETIME idleFt, kernelFt, userFt;
            if (!GetSystemTimes(out idleFt, out kernelFt, out userFt))
            {
                return null;
            }

            ulong idle = FileTimeToUInt64(idleFt);
            ulong kernel = FileTimeToUInt64(kernelFt);
            ulong user = FileTimeToUInt64(userFt);

            if (!_hasPrevCpuTimes)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _hasPrevCpuTimes = true;
                return null;
            }

            ulong idleDelta = idle - _prevIdle;
            ulong kernelDelta = kernel - _prevKernel;
            ulong userDelta = user - _prevUser;

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            ulong totalDelta = kernelDelta + userDelta;
            if (totalDelta == 0)
            {
                return null;
            }

            double busy = (double)(totalDelta - idleDelta) / totalDelta * 100.0;
            if (busy < 0.0) busy = 0.0;
            if (busy > 100.0) busy = 100.0;
            return Math.Round(busy, 1);
        }

        private double?[] BuildHistoryArray(double?[] ring)
        {
            double?[] result = new double?[_ringCount];
            int start = (_ringIndex - _ringCount + FrameHistoryCapacity) % FrameHistoryCapacity;
            for (int i = 0; i < _ringCount; i++)
            {
                result[i] = ring[(start + i) % FrameHistoryCapacity];
            }
            return result;
        }

        private static bool IsValidAfterburnerFrameMetric(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f && value < 10000f;
        }

        private static void ReadAfterburnerMetrics(out int? cpuTempC, out double? fps, out double? frametimeMs)
        {
            cpuTempC = null;
            fps = null;
            frametimeMs = null;

            const uint MahmSignature = 0x4D41484D; // "MAHM"
            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting("MAHMSharedMemory", MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor acc = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    MahmHeader header;
                    acc.Read(0, out header);
                    if (header.dwSignature != MahmSignature || header.dwNumEntries == 0 || header.dwNumEntries > 1024 || header.dwEntrySize == 0)
                    {
                        return;
                    }

                    int structSize = Marshal.SizeOf(typeof(MahmEntry));
                    int copySize = Math.Min(structSize, (int)header.dwEntrySize);
                    byte[] buf = new byte[structSize];
                    for (uint i = 0; i < header.dwNumEntries; i++)
                    {
                        long offset = header.dwHeaderSize + i * header.dwEntrySize;
                        acc.ReadArray(offset, buf, 0, copySize);
                        GCHandle handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                        try
                        {
                            MahmEntry entry = (MahmEntry)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(MahmEntry));
                            if (string.Equals(entry.szSrcName, "CPU temperature", StringComparison.OrdinalIgnoreCase))
                            {
                                if (entry.data > 0f && entry.data < 200f)
                                {
                                    cpuTempC = (int)Math.Round(entry.data);
                                }
                            }
                            else if (string.Equals(entry.szSrcName, "Framerate", StringComparison.OrdinalIgnoreCase))
                            {
                                if (IsValidAfterburnerFrameMetric(entry.data))
                                {
                                    fps = Math.Round(entry.data, 1);
                                }
                            }
                            else if (string.Equals(entry.szSrcName, "Frametime", StringComparison.OrdinalIgnoreCase))
                            {
                                if (IsValidAfterburnerFrameMetric(entry.data))
                                {
                                    frametimeMs = Math.Round(entry.data, 2);
                                }
                            }
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }
                }
            }
            catch
            {
                // MSI Afterburner not running or shared memory unavailable
            }
        }

        private void ReadActiveRtssApp(out string appName, out string appIcon, out string appHeroUrl)
        {
            appName = null;
            appIcon = null;
            appHeroUrl = null;

            const uint RtssSignature = 0x52545353; // "RTSS"
            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2", MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor acc = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    RtssHeader header;
                    acc.Read(0, out header);
                    if (header.dwSignature != RtssSignature || header.dwAppEntrySize == 0 || header.dwAppArrSize == 0 || header.dwAppArrSize > 512)
                    {
                        return;
                    }

                    int structSize = Marshal.SizeOf(typeof(RtssAppEntry));
                    int copySize = Math.Min(structSize, (int)header.dwAppEntrySize);
                    byte[] buf = new byte[structSize];

                    string bestExePath = null;
                    uint bestTime1 = 0;

                    for (uint i = 0; i < header.dwAppArrSize; i++)
                    {
                        long offset = (long)header.dwAppArrOffset + i * header.dwAppEntrySize;
                        acc.ReadArray(offset, buf, 0, copySize);
                        GCHandle handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                        try
                        {
                            RtssAppEntry entry = (RtssAppEntry)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(RtssAppEntry));
                            if (entry.dwProcessID == 0 || string.IsNullOrWhiteSpace(entry.szName))
                            {
                                continue;
                            }
                            if (entry.dwFrames == 0 && entry.dwFrameTime == 0)
                            {
                                continue;
                            }
                            if (bestExePath == null || entry.dwTime1 >= bestTime1)
                            {
                                bestTime1 = entry.dwTime1;
                                bestExePath = entry.szName.Trim();
                            }
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(bestExePath))
                    {
                        FrameAppMeta meta = GetOrResolveAppMeta(bestExePath);
                        appName = meta.Name;
                        appIcon = meta.Icon;
                        appHeroUrl = _heroResolver.ResolveHeroUrl(bestExePath, appName);
                    }
                }
            }
            catch
            {
                // RTSS not running or shared memory unavailable
            }
        }

        private FrameAppMeta GetOrResolveAppMeta(string exePath)
        {
            FrameAppMeta cached;
            if (_appMetaCache.TryGetValue(exePath, out cached))
            {
                return cached;
            }

            string name = Path.GetFileNameWithoutExtension(exePath);
            try
            {
                if (File.Exists(exePath))
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrWhiteSpace(fvi.FileDescription))
                    {
                        name = fvi.FileDescription.Trim();
                    }
                }
            }
            catch
            {
            }

            string icon = null;
            try
            {
                if (File.Exists(exePath))
                {
                    icon = NativeInterop.ExtractShellFileIconDataUrl(exePath);
                }
            }
            catch
            {
            }

            FrameAppMeta meta = new FrameAppMeta { Name = name, Icon = icon };
            if (_appMetaCache.Count >= 32)
            {
                _appMetaCache.Clear();
            }
            _appMetaCache[exePath] = meta;
            return meta;
        }

        private static bool ReadRamUsage(out double? percent, out double? usedGb, out double? totalGb)
        {
            percent = null;
            usedGb = null;
            totalGb = null;

            MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref mem) || mem.ullTotalPhys == 0)
            {
                return false;
            }

            const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
            ulong usedBytes = mem.ullTotalPhys - mem.ullAvailPhys;
            totalGb = Math.Round(mem.ullTotalPhys / bytesPerGb, 1);
            usedGb = Math.Round(usedBytes / bytesPerGb, 1);
            percent = Math.Round((double)usedBytes / mem.ullTotalPhys * 100.0, 1);
            return true;
        }

        private void ReadAmdGpuTelemetry(SystemMetricsSnapshot target)
        {
            if (_adlUnavailable)
            {
                return;
            }

            try
            {
                if (_adlContext == IntPtr.Zero && !InitAdl())
                {
                    return;
                }

                target.GpuName = _adlAdapterName;
                target.GpuVramTotalGb = _adlTotalVramGb;

                int vramUsedMb;
                if (ADL2_Adapter_DedicatedVRAMUsage_Get(_adlContext, _adlAdapterIndex, out vramUsedMb) == 0 && vramUsedMb >= 0)
                {
                    target.GpuVramUsedGb = Math.Round(vramUsedMb / 1024.0, 1);
                }

                AdlPmLogData pmData = new AdlPmLogData();
                pmData.size = Marshal.SizeOf(typeof(AdlPmLogData));
                pmData.sensors = new AdlSingleSensorData[256];

                int pmRc = ADL2_New_QueryPMLogData_Get(_adlContext, _adlAdapterIndex, ref pmData);
                if (pmRc == 0 && pmData.sensors != null)
                {
                    if (pmData.sensors[AdlSensorActivityGfx].supported != 0)
                    {
                        int pct = pmData.sensors[AdlSensorActivityGfx].value;
                        if (pct < 0) pct = 0;
                        if (pct > 100) pct = 100;
                        target.GpuPercent = pct;
                    }
                    if (pmData.sensors[AdlSensorTempEdge].supported != 0)
                    {
                        target.GpuTempC = pmData.sensors[AdlSensorTempEdge].value;
                    }
                    if (pmData.sensors[AdlSensorTempHotspot].supported != 0)
                    {
                        target.GpuHotspotTempC = pmData.sensors[AdlSensorTempHotspot].value;
                    }
                }
                else
                {
                    // Driver or display topology may have reset; re-init on next sample
                    DestroyAdl();
                }
            }
            catch (DllNotFoundException)
            {
                _adlUnavailable = true;
                if (!_adlWarned && _logger != null)
                {
                    _adlWarned = true;
                    _logger.Warn("atiadlxx.dll not found; AMD GPU telemetry disabled.");
                }
            }
            catch (EntryPointNotFoundException ex)
            {
                _adlUnavailable = true;
                if (!_adlWarned && _logger != null)
                {
                    _adlWarned = true;
                    _logger.Warn("AMD ADL2 entry point missing; GPU telemetry disabled: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                DestroyAdl();
                if (!_adlWarned && _logger != null)
                {
                    _adlWarned = true;
                    _logger.Warn("AMD ADL2 telemetry error: " + ex.Message);
                }
            }
        }

        private bool InitAdl()
        {
            IntPtr ctx;
            int rc = ADL2_Main_Control_Create(AdlMallocDelegate, 1, out ctx);
            if (rc != 0 || ctx == IntPtr.Zero)
            {
                return false;
            }

            int count;
            if (ADL2_Adapter_NumberOfAdapters_Get(ctx, out count) != 0 || count <= 0)
            {
                ADL2_Main_Control_Destroy(ctx);
                return false;
            }

            int structSize = Marshal.SizeOf(typeof(AdlAdapterInfo));
            IntPtr buffer = Marshal.AllocHGlobal(structSize * count);
            try
            {
                if (ADL2_Adapter_AdapterInfo_Get(ctx, buffer, structSize * count) != 0)
                {
                    ADL2_Main_Control_Destroy(ctx);
                    return false;
                }

                AdlAdapterInfo chosen = new AdlAdapterInfo();
                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    AdlAdapterInfo info = (AdlAdapterInfo)Marshal.PtrToStructure(IntPtr.Add(buffer, i * structSize), typeof(AdlAdapterInfo));
                    if (!found)
                    {
                        chosen = info;
                        found = true;
                    }
                    if (info.iExist != 0 && info.iPresent != 0)
                    {
                        chosen = info;
                        break;
                    }
                }

                if (!found)
                {
                    ADL2_Main_Control_Destroy(ctx);
                    return false;
                }

                _adlContext = ctx;
                _adlAdapterIndex = chosen.iAdapterIndex;
                _adlAdapterName = string.IsNullOrWhiteSpace(chosen.strAdapterName) ? "AMD GPU" : chosen.strAdapterName.Trim();

                AdlMemoryInfoX4 memInfo;
                if (ADL2_Adapter_MemoryInfoX4_Get(ctx, _adlAdapterIndex, out memInfo) == 0 && memInfo.iMemorySize > 0)
                {
                    _adlTotalVramGb = Math.Round(memInfo.iMemorySize / (1024.0 * 1024.0 * 1024.0), 1);
                }

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void DestroyAdl()
        {
            if (_adlContext != IntPtr.Zero)
            {
                try
                {
                    ADL2_Main_Control_Destroy(_adlContext);
                }
                catch
                {
                }
                _adlContext = IntPtr.Zero;
            }
            _adlAdapterIndex = -1;
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Dispose();
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                DestroyAdl();
            }
        }

        private static string ReadCpuName()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        string raw = key.GetValue("ProcessorNameString") as string;
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            string cleaned = Regex.Replace(raw.Trim(), @"\s+\d+-Core\s+Processor$", "", RegexOptions.IgnoreCase);
                            cleaned = Regex.Replace(cleaned, @"\s+Processor$", "", RegexOptions.IgnoreCase);
                            return cleaned.Trim();
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static string ReadRamName()
        {
            try
            {
                int stickCount = 0;
                ulong firstCapacity = 0;
                ulong totalCapacity = 0;
                bool uniformSize = true;
                string ddr = null;

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Capacity, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        ulong cap = 0;
                        object capObj = obj["Capacity"];
                        if (capObj != null) ulong.TryParse(capObj.ToString(), out cap);
                        if (cap > 0)
                        {
                            if (stickCount == 0) firstCapacity = cap;
                            else if (cap != firstCapacity) uniformSize = false;
                            totalCapacity += cap;
                            stickCount++;
                        }

                        if (ddr == null)
                        {
                            uint memType = 0;
                            object smbiosType = obj["SMBIOSMemoryType"];
                            if (smbiosType != null) uint.TryParse(smbiosType.ToString(), out memType);
                            if (memType == 34) ddr = "DDR5";
                            else if (memType == 26) ddr = "DDR4";
                            else if (memType == 24) ddr = "DDR3";
                        }
                    }
                }

                const ulong bytesPerGb = 1024UL * 1024UL * 1024UL;
                string sizeStr = null;
                if (stickCount > 0 && uniformSize && firstCapacity >= bytesPerGb)
                {
                    ulong perStickGb = firstCapacity / bytesPerGb;
                    sizeStr = stickCount + "x" + perStickGb + "GB";
                }
                else if (totalCapacity >= bytesPerGb)
                {
                    sizeStr = (totalCapacity / bytesPerGb) + "GB";
                }

                if (!string.IsNullOrEmpty(sizeStr) && !string.IsNullOrEmpty(ddr))
                {
                    return sizeStr + " " + ddr;
                }
                if (!string.IsNullOrEmpty(sizeStr))
                {
                    return sizeStr;
                }
                if (!string.IsNullOrEmpty(ddr))
                {
                    return ddr;
                }
            }
            catch
            {
            }
            return null;
        }

        private static ulong FileTimeToUInt64(FILETIME ft)
        {
            return ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
        }

        private static IntPtr AdlMalloc(int size)
        {
            return Marshal.AllocHGlobal(size);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AdlMainMallocCallback(int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct AdlAdapterInfo
        {
            public int iSize;
            public int iAdapterIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strUDID;
            public int iBusNumber;
            public int iDeviceNumber;
            public int iFunctionNumber;
            public int iVendorID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAdapterName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strDisplayName;
            public int iPresent;
            public int iExist;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strDriverPath;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strDriverPathExt;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strPNPString;
            public int iOSDisplayIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdlSingleSensorData
        {
            public int supported;
            public int value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdlPmLogData
        {
            public int size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public AdlSingleSensorData[] sensors;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct AdlMemoryInfoX4
        {
            public long iMemorySize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strMemoryType;
            public long iMemoryBandwidth;
            public long iHyperMemorySize;
            public long iInvisibleMemorySize;
            public long iVisibleMemorySize;
            public long iVramVendorRevId;
            public long iMemoryBandwidthX2;
            public long iMemoryBitRateX2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MahmHeader
        {
            public uint dwSignature;
            public uint dwVersion;
            public uint dwHeaderSize;
            public uint dwNumEntries;
            public uint dwEntrySize;
            public long time;
            public uint dwNumGpuEntries;
            public uint dwGpuEntrySize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct MahmEntry
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szSrcName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szSrcUnits;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szLocalizedSrcName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szLocalizedSrcUnits;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szRecommendedFormat;
            public float data;
            public float minLimit;
            public float maxLimit;
            public uint dwFlags;
            public uint dwGpu;
            public uint dwSrcId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RtssHeader
        {
            public uint dwSignature;
            public uint dwVersion;
            public uint dwAppEntrySize;
            public uint dwAppArrOffset;
            public uint dwAppArrSize;
            public uint dwOSDEntrySize;
            public uint dwOSDArrOffset;
            public uint dwOSDArrSize;
            public uint dwOSDFrame;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct RtssAppEntry
        {
            public uint dwProcessID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szName;
            public uint dwFlags;
            public uint dwTime0;
            public uint dwTime1;
            public uint dwFrames;
            public uint dwFrameTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Create(AdlMainMallocCallback callback, int iEnumConnectedAdapters, out IntPtr context);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Destroy(IntPtr context);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numAdapters);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int inputSize);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int iAdapterIndex, ref AdlPmLogData output);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Adapter_DedicatedVRAMUsage_Get(IntPtr context, int iAdapterIndex, out int iVRAMUsageInMB);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Adapter_MemoryInfoX4_Get(IntPtr context, int iAdapterIndex, out AdlMemoryInfoX4 info);
    }
}
