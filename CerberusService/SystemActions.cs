using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace RoomAgent
{
    public class DesktopShortcutInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Extension { get; set; }
        public string Source { get; set; }
        public string IconDataUrl { get; set; }
    }

    public class OpenWindowInfo
    {
        public long Hwnd { get; set; }
        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string Title { get; set; }
        public string IconDataUrl { get; set; }
    }

    public sealed class SavedWindowPlacement
    {
        public long Hwnd { get; set; }
        public int Pid { get; set; }
        public uint Flags { get; set; }
        public uint ShowCmd { get; set; }
        public int MinX { get; set; }
        public int MinY { get; set; }
        public int MaxX { get; set; }
        public int MaxY { get; set; }
        public int NormalLeft { get; set; }
        public int NormalTop { get; set; }
        public int NormalRight { get; set; }
        public int NormalBottom { get; set; }
        public bool HasWindowRect { get; set; }
        public int RectLeft { get; set; }
        public int RectTop { get; set; }
        public int RectRight { get; set; }
        public int RectBottom { get; set; }
    }

    public class StreamingSessionInfo
    {
        public bool Active { get; set; }
        public string DisplayName { get; set; }
        public string Reason { get; set; }
    }

    public interface ISystemActions
    {
        void SwitchToFreshVirtualDesktop();
        void RevertVirtualDesktop();
        void HideTaskbar();
        void ShowTaskbar();
        void HideDesktopIcons();
        void ShowDesktopIcons();
        void EnterXboxMode();
        void ExitXboxMode();
        bool HasPendingConsoleEnvironmentRevert();
        void MinimizeAllWindows();
        void RestoreMinimizedWindows();
        void SaveWindowPositions();
        void RestoreWindowPositions();
        IList<DesktopShortcutInfo> GetDesktopShortcuts();
        bool LaunchDesktopShortcut(string pathOrName, out string launchedName);
        IList<OpenWindowInfo> GetOpenWindows();
        bool FocusWindow(long hwnd);
        bool MaximizeWindow(long hwnd);
        bool CloseWindow(long hwnd);
        bool OpenTaskView();
        bool? GetOsdEnabled();
        bool TrySetOsdEnabled(bool enabled, out bool currentState);
        bool IsMonitorAvailable(string expectedMonitor);
        void SwitchDisplay(EnvironmentConfig env);
        bool VerifyOutputDevices(EnvironmentConfig env, bool checkAudio, out string details);
        bool TrySetDefaultAudioPlaybackDevice(string expectedAudio, out string matchedDevice);
        void OpenSteamBigPicture();
        void CloseSteamBigPicture(string closeMode);
        void CloseSteamWindow();
        void CloseApplications(string[] appNames);
        void LaunchApp(string target);
        void CloseWindowWhenReady(string processName, string exactTitle = null);
        void StartHueSync();
        void StopHueSync();
        StreamingSessionInfo GetStreamingSessionInfo();
        bool StopStreamingSession();
        void SleepComputer();
        void ShutdownComputer();
    }

    public class WindowsSystemActions : ISystemActions
    {
        private sealed class ConsoleEnvironmentState
        {
            public bool CreatedVirtualDesktop { get; set; }
            public string CreatedVirtualDesktopId { get; set; }
            public bool TaskbarHiddenByUs { get; set; }
            public bool DesktopIconsHiddenByUs { get; set; }
            public string PreviousWallpaperPath { get; set; }
            public bool WallpaperChangedByUs { get; set; }
            public bool XboxModeEnteredByUs { get; set; }
            public long[] MinimizedWindowHandles { get; set; }
            public SavedWindowPlacement[] SavedWindowPlacements { get; set; }
        }

        private sealed class WallpaperHistoryEntry
        {
            public string TimestampUtc { get; set; }
            public string WallpaperPath { get; set; }
        }

        private const int MaxWallpaperHistoryEntries = 50;

        private readonly AgentConfig _config;
        private readonly ILogger _logger;
        private readonly string _stateFilePath;
        private readonly string _wallpaperHistoryFilePath;
        private readonly object _stateLock = new object();
        private bool _createdVirtualDesktop;
        private Guid _createdVirtualDesktopId;
        private bool _taskbarHiddenByUs;
        private bool _desktopIconsHiddenByUs;
        private string _previousWallpaperPath;
        private bool _wallpaperChangedByUs;
        private bool _xboxModeEnteredByUs;
        private List<long> _minimizedWindowHandles = new List<long>();
        private List<SavedWindowPlacement> _savedWindowPlacements = new List<SavedWindowPlacement>();

        public WindowsSystemActions(AgentConfig config, ILogger logger)
            : this(
                config,
                logger,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console-environment-state.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallpaper-history.json"))
        {
        }

        public WindowsSystemActions(AgentConfig config, ILogger logger, string stateFilePath, string wallpaperHistoryFilePath = null)
        {
            _config = config;
            _logger = logger;
            _stateFilePath = stateFilePath;
            _wallpaperHistoryFilePath = !string.IsNullOrWhiteSpace(wallpaperHistoryFilePath)
                ? wallpaperHistoryFilePath
                : (!string.IsNullOrWhiteSpace(stateFilePath)
                    ? Path.Combine(Path.GetDirectoryName(stateFilePath) ?? AppDomain.CurrentDomain.BaseDirectory, "wallpaper-history.json")
                    : null);
            LoadPersistedState();
        }

        public bool HasPendingConsoleEnvironmentRevert()
        {
            return _createdVirtualDesktop ||
                   _taskbarHiddenByUs ||
                   _desktopIconsHiddenByUs ||
                   _wallpaperChangedByUs ||
                   _xboxModeEnteredByUs ||
                   (_minimizedWindowHandles != null && _minimizedWindowHandles.Count > 0) ||
                   (_savedWindowPlacements != null && _savedWindowPlacements.Count > 0);
        }

        public void SwitchToFreshVirtualDesktop()
        {
            Guid newId;
            bool ok = NativeInterop.SwitchToNewVirtualDesktop(out newId);
            if (ok)
            {
                _createdVirtualDesktop = true;
                _createdVirtualDesktopId = newId;
                _logger.Info("Switched to fresh virtual desktop (" + (newId != Guid.Empty ? newId.ToString() : "unknown-id") + ").");

                if (_config.LivingRoomDesktopColorEnabled && !string.IsNullOrWhiteSpace(_config.LivingRoomDesktopColor))
                {
                    string currentWallpaper = NativeInterop.GetCurrentDesktopWallpaper();
                    if (!IsAgentSolidColorWallpaper(currentWallpaper))
                    {
                        _previousWallpaperPath = currentWallpaper;
                        RecordWallpaperHistory(currentWallpaper);
                        _logger.Info("Recorded previous desktop wallpaper: " + (string.IsNullOrEmpty(currentWallpaper) ? "<none>" : currentWallpaper));
                    }
                    else if (string.IsNullOrWhiteSpace(_previousWallpaperPath) || IsAgentSolidColorWallpaper(_previousWallpaperPath))
                    {
                        string historyWallpaper = GetLastKnownWallpaperFromHistory();
                        if (!string.IsNullOrWhiteSpace(historyWallpaper))
                        {
                            _previousWallpaperPath = historyWallpaper;
                            _logger.Info("Recovered previous desktop wallpaper from history: " + historyWallpaper);
                        }
                    }

                    bool bgOk = NativeInterop.SetCurrentDesktopSolidColorWallpaper(_config.LivingRoomDesktopColor);
                    _wallpaperChangedByUs = bgOk;
                    _logger.Info("Set virtual desktop background color (" + _config.LivingRoomDesktopColor + ") -> " + bgOk);
                }

                SavePersistedState();
            }
            else
            {
                _logger.Warn("Failed to switch to a fresh virtual desktop.");
            }
        }

        public void RevertVirtualDesktop()
        {
            if (!_createdVirtualDesktop && !_wallpaperChangedByUs)
            {
                return;
            }

            if (_createdVirtualDesktop)
            {
                bool ok = NativeInterop.CloseCurrentVirtualDesktop(_createdVirtualDesktopId);
                _logger.Info("RevertVirtualDesktop -> " + ok);
                _createdVirtualDesktop = false;
                _createdVirtualDesktopId = Guid.Empty;
            }

            if (_wallpaperChangedByUs)
            {
                string currentWallpaper = NativeInterop.GetCurrentDesktopWallpaper();
                if (!string.Equals(currentWallpaper, _previousWallpaperPath, StringComparison.OrdinalIgnoreCase))
                {
                    bool wpOk = NativeInterop.SetDesktopWallpaper(_previousWallpaperPath);
                    _logger.Info("Restored original desktop wallpaper -> " + wpOk);
                }
                _wallpaperChangedByUs = false;
                _previousWallpaperPath = null;
            }

            SavePersistedState();
        }

        public void HideTaskbar()
        {
            if (NativeInterop.IsTaskbarAutoHide())
            {
                _taskbarHiddenByUs = false;
                SavePersistedState();
                _logger.Info("Taskbar auto-hide is already enabled.");
                return;
            }

            bool ok = NativeInterop.SetTaskbarAutoHide(true);
            if (ok)
            {
                _taskbarHiddenByUs = true;
                SavePersistedState();
                _logger.Info("HideTaskbar (auto-hide) -> True");
            }
            else
            {
                _logger.Warn("HideTaskbar (auto-hide) -> False");
            }
        }

        public void ShowTaskbar()
        {
            if (!_taskbarHiddenByUs)
            {
                return;
            }

            bool ok = NativeInterop.SetTaskbarAutoHide(false);
            if (ok)
            {
                _taskbarHiddenByUs = false;
                SavePersistedState();
            }
            _logger.Info("ShowTaskbar (auto-hide off) -> " + ok);
        }

        public void HideDesktopIcons()
        {
            bool currentlyVisible;
            if (!NativeInterop.AreDesktopIconsVisible(out currentlyVisible))
            {
                _logger.Warn("HideDesktopIcons -> desktop list view not found.");
                return;
            }

            if (!currentlyVisible)
            {
                _desktopIconsHiddenByUs = false;
                SavePersistedState();
                _logger.Info("Desktop icons are already hidden.");
                return;
            }

            bool ok = NativeInterop.SetDesktopIconsVisible(false);
            if (ok)
            {
                _desktopIconsHiddenByUs = true;
                SavePersistedState();
                _logger.Info("HideDesktopIcons -> True");
            }
            else
            {
                _logger.Warn("HideDesktopIcons -> False");
            }
        }

        public void ShowDesktopIcons()
        {
            if (!_desktopIconsHiddenByUs)
            {
                return;
            }

            bool ok = NativeInterop.SetDesktopIconsVisible(true);
            if (ok)
            {
                _desktopIconsHiddenByUs = false;
                SavePersistedState();
            }
            _logger.Info("ShowDesktopIcons -> " + ok);
        }

        public void EnterXboxMode()
        {
            if (_xboxModeEnteredByUs)
            {
                _logger.Info("Xbox mode was already entered by us.");
                return;
            }

            long previousForegroundHwnd = NativeInterop.GetForegroundWindowHandle();
            List<long> openWindows = NativeInterop.GetNonMinimizedWindowHandles();
            bool ok = NativeInterop.ToggleXboxMode();
            if (ok)
            {
                _xboxModeEnteredByUs = true;
                TrackMinimizedWindowHandles(openWindows);
                SavePersistedState();
                _logger.Info("EnterXboxMode (Win+F11) -> True (tracked " + (openWindows != null ? openWindows.Count : 0) + " windows)");
                WaitForXboxModeReady(previousForegroundHwnd, TimeSpan.FromSeconds(5));
            }
            else
            {
                _logger.Warn("EnterXboxMode (Win+F11) -> False");
            }
        }

        public void ExitXboxMode()
        {
            if (!_xboxModeEnteredByUs)
            {
                return;
            }

            bool ok = NativeInterop.ToggleXboxMode();
            _xboxModeEnteredByUs = false;
            SavePersistedState();
            _logger.Info("ExitXboxMode (Win+F11) -> " + ok);
            Thread.Sleep(250);
            CloseApplications(new[] { "XboxPcApp", "XboxPcAppCE" });
            // Give Windows time to finish transitioning out of Xbox mode before restoring windows.
            Thread.Sleep(500);
            RestoreMinimizedWindows();
        }

        private void LoadPersistedState()
        {
            if (string.IsNullOrWhiteSpace(_stateFilePath))
            {
                return;
            }

            lock (_stateLock)
            {
                try
                {
                    if (!File.Exists(_stateFilePath))
                    {
                        return;
                    }

                    string json = File.ReadAllText(_stateFilePath);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return;
                    }

                    ConsoleEnvironmentState state = System.Text.Json.JsonSerializer.Deserialize<ConsoleEnvironmentState>(json);
                    if (state == null)
                    {
                        return;
                    }

                    _createdVirtualDesktop = state.CreatedVirtualDesktop;
                    Guid parsedId;
                    _createdVirtualDesktopId = Guid.TryParse(state.CreatedVirtualDesktopId, out parsedId)
                        ? parsedId
                        : Guid.Empty;
                    _taskbarHiddenByUs = state.TaskbarHiddenByUs;
                    _desktopIconsHiddenByUs = state.DesktopIconsHiddenByUs;
                    _previousWallpaperPath = state.PreviousWallpaperPath;
                    _wallpaperChangedByUs = state.WallpaperChangedByUs;
                    _xboxModeEnteredByUs = state.XboxModeEnteredByUs;
                    _minimizedWindowHandles = state.MinimizedWindowHandles != null
                        ? new List<long>(state.MinimizedWindowHandles)
                        : new List<long>();
                    _savedWindowPlacements = state.SavedWindowPlacements != null
                        ? new List<SavedWindowPlacement>(state.SavedWindowPlacements)
                        : new List<SavedWindowPlacement>();

                    if (HasPendingConsoleEnvironmentRevert())
                    {
                        _logger.Info("Loaded persisted console environment state from " + _stateFilePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to load persisted console environment state: " + ex.Message);
                }
            }
        }

        private void SavePersistedState()
        {
            if (string.IsNullOrWhiteSpace(_stateFilePath))
            {
                return;
            }

            lock (_stateLock)
            {
                try
                {
                    if (!HasPendingConsoleEnvironmentRevert())
                    {
                        if (File.Exists(_stateFilePath))
                        {
                            File.Delete(_stateFilePath);
                        }
                        return;
                    }

                    ConsoleEnvironmentState state = new ConsoleEnvironmentState
                    {
                        CreatedVirtualDesktop = _createdVirtualDesktop,
                        CreatedVirtualDesktopId = _createdVirtualDesktopId != Guid.Empty ? _createdVirtualDesktopId.ToString() : "",
                        TaskbarHiddenByUs = _taskbarHiddenByUs,
                        DesktopIconsHiddenByUs = _desktopIconsHiddenByUs,
                        PreviousWallpaperPath = _previousWallpaperPath,
                        WallpaperChangedByUs = _wallpaperChangedByUs,
                        XboxModeEnteredByUs = _xboxModeEnteredByUs,
                        MinimizedWindowHandles = _minimizedWindowHandles.ToArray(),
                        SavedWindowPlacements = _savedWindowPlacements.ToArray()
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(state);
                    File.WriteAllText(_stateFilePath, json);
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to persist console environment state: " + ex.Message);
                }
            }
        }

        internal static bool IsAgentSolidColorWallpaper(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return string.Equals(Path.GetFileName(path.Trim()), "room-agent-bg.bmp", StringComparison.OrdinalIgnoreCase);
        }

        internal void RecordWallpaperHistory(string wallpaperPath)
        {
            if (string.IsNullOrWhiteSpace(_wallpaperHistoryFilePath) ||
                string.IsNullOrWhiteSpace(wallpaperPath) ||
                IsAgentSolidColorWallpaper(wallpaperPath))
            {
                return;
            }

            lock (_stateLock)
            {
                try
                {
                    List<WallpaperHistoryEntry> entries = LoadWallpaperHistoryUnsafe();
                    if (entries.Count > 0 &&
                        string.Equals(entries[entries.Count - 1].WallpaperPath, wallpaperPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    entries.Add(new WallpaperHistoryEntry
                    {
                        TimestampUtc = DateTime.UtcNow.ToString("o"),
                        WallpaperPath = wallpaperPath
                    });

                    if (entries.Count > MaxWallpaperHistoryEntries)
                    {
                        entries.RemoveRange(0, entries.Count - MaxWallpaperHistoryEntries);
                    }

                    File.WriteAllText(_wallpaperHistoryFilePath, System.Text.Json.JsonSerializer.Serialize(entries));
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to record wallpaper history: " + ex.Message);
                }
            }
        }

        internal string GetLastKnownWallpaperFromHistory()
        {
            if (string.IsNullOrWhiteSpace(_wallpaperHistoryFilePath))
            {
                return null;
            }

            lock (_stateLock)
            {
                try
                {
                    List<WallpaperHistoryEntry> entries = LoadWallpaperHistoryUnsafe();
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        string path = entries[i].WallpaperPath;
                        if (!string.IsNullOrWhiteSpace(path) && !IsAgentSolidColorWallpaper(path))
                        {
                            return path;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to read wallpaper history: " + ex.Message);
                }
                return null;
            }
        }

        private List<WallpaperHistoryEntry> LoadWallpaperHistoryUnsafe()
        {
            if (!File.Exists(_wallpaperHistoryFilePath))
            {
                return new List<WallpaperHistoryEntry>();
            }

            string json = File.ReadAllText(_wallpaperHistoryFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<WallpaperHistoryEntry>();
            }

            List<WallpaperHistoryEntry> entries = System.Text.Json.JsonSerializer.Deserialize<List<WallpaperHistoryEntry>>(json);
            return entries ?? new List<WallpaperHistoryEntry>();
        }

        public void MinimizeAllWindows()
        {
            List<long> toMinimize = NativeInterop.GetNonMinimizedWindowHandles();
            bool ok = NativeInterop.MinimizeAllWindows();
            if (ok)
            {
                TrackMinimizedWindowHandles(toMinimize);
                SavePersistedState();
            }
            _logger.Info("MinimizeAllWindows -> " + ok + " (tracked " + (toMinimize != null ? toMinimize.Count : 0) + " windows)");
        }

        private void TrackMinimizedWindowHandles(IList<long> handles)
        {
            if (handles == null || handles.Count == 0)
            {
                return;
            }

            lock (_stateLock)
            {
                if (_minimizedWindowHandles.Count == 0)
                {
                    _minimizedWindowHandles = new List<long>(handles);
                }
                else
                {
                    foreach (long hwnd in handles)
                    {
                        if (!_minimizedWindowHandles.Contains(hwnd))
                        {
                            _minimizedWindowHandles.Add(hwnd);
                        }
                    }
                }
            }
        }

        public void RestoreMinimizedWindows()
        {
            List<long> toRestore;
            lock (_stateLock)
            {
                if (_minimizedWindowHandles == null || _minimizedWindowHandles.Count == 0)
                {
                    return;
                }
                toRestore = new List<long>(_minimizedWindowHandles);
                _minimizedWindowHandles.Clear();
            }

            int restored = NativeInterop.RestoreWindows(toRestore);
            SavePersistedState();
            _logger.Info("RestoreMinimizedWindows -> restored " + restored + " of " + toRestore.Count + " windows");
        }

        public void SaveWindowPositions()
        {
            List<SavedWindowPlacement> captured = NativeInterop.CaptureOpenWindowPlacements();
            lock (_stateLock)
            {
                _savedWindowPlacements = captured ?? new List<SavedWindowPlacement>();
            }
            SavePersistedState();
            _logger.Info("SaveWindowPositions -> captured " + (captured != null ? captured.Count : 0) + " windows");
        }

        public void RestoreWindowPositions()
        {
            List<SavedWindowPlacement> toRestore;
            lock (_stateLock)
            {
                if (_savedWindowPlacements == null || _savedWindowPlacements.Count == 0)
                {
                    return;
                }
                toRestore = new List<SavedWindowPlacement>(_savedWindowPlacements);
                _savedWindowPlacements.Clear();
            }

            int restored = NativeInterop.RestoreWindowPlacements(toRestore);
            SavePersistedState();
            _logger.Info("RestoreWindowPositions -> restored " + restored + " of " + toRestore.Count + " windows");
        }

        internal const string SteamBigPictureShortcutPath = "cerberus://shortcut/steam-big-picture";
        internal const string SteamBigPictureShortcutName = "Steam Big Picture Mode";
        internal const string XboxAppShortcutPath = "cerberus://shortcut/xbox-app";
        internal const string XboxAppShortcutName = "Xbox App";

        public IList<DesktopShortcutInfo> GetDesktopShortcuts()
        {
            List<DesktopShortcutInfo> results = new List<DesktopShortcutInfo>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string customDir = _config != null ? _config.ShortcutsDirectory : null;
            if (!string.IsNullOrWhiteSpace(customDir))
            {
                string resolvedDir = Environment.ExpandEnvironmentVariables(customDir.Trim());
                AddShortcutsFromDirectory(resolvedDir, "custom", results, seenPaths);
            }
            else
            {
                AddShortcutsFromDirectory(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "user", results, seenPaths);
                AddShortcutsFromDirectory(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "public", results, seenPaths);
            }

            AddBuiltInShortcuts(results, seenPaths);

            results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return results;
        }

        internal void AddBuiltInShortcuts(List<DesktopShortcutInfo> results, HashSet<string> seenPaths)
        {
            if (!HasShortcutNamed(results, SteamBigPictureShortcutName, "Steam Big Picture") &&
                seenPaths.Add(SteamBigPictureShortcutPath))
            {
                results.Add(new DesktopShortcutInfo
                {
                    Name = SteamBigPictureShortcutName,
                    Path = SteamBigPictureShortcutPath,
                    Extension = "uri",
                    Source = "builtin",
                    IconDataUrl = TryGetSteamIconDataUrl()
                });
            }

            if (!HasShortcutNamed(results, XboxAppShortcutName, "Xbox") &&
                seenPaths.Add(XboxAppShortcutPath))
            {
                results.Add(new DesktopShortcutInfo
                {
                    Name = XboxAppShortcutName,
                    Path = XboxAppShortcutPath,
                    Extension = "app",
                    Source = "builtin",
                    IconDataUrl = TryGetXboxAppIconDataUrl()
                });
            }
        }

        private static bool HasShortcutNamed(List<DesktopShortcutInfo> results, params string[] names)
        {
            foreach (DesktopShortcutInfo item in results)
            {
                foreach (string name in names)
                {
                    if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private string TryGetSteamIconDataUrl()
        {
            try
            {
                string steamExe = _config != null ? _config.SteamPath : null;
                if (string.IsNullOrWhiteSpace(steamExe) || !File.Exists(steamExe))
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        if (key != null)
                        {
                            object exeVal = key.GetValue("SteamExe");
                            if (exeVal != null && File.Exists(exeVal.ToString()))
                            {
                                steamExe = exeVal.ToString();
                            }
                            else
                            {
                                object pathVal = key.GetValue("SteamPath");
                                if (pathVal != null)
                                {
                                    string candidate = Path.Combine(pathVal.ToString(), "steam.exe");
                                    if (File.Exists(candidate))
                                    {
                                        steamExe = candidate;
                                    }
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(steamExe) && File.Exists(steamExe))
                {
                    return NativeInterop.ExtractShellFileIconDataUrl(steamExe);
                }
            }
            catch
            {
            }
            return null;
        }

        private static string TryGetXboxAppIconDataUrl()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("XboxPcApp");
                try
                {
                    foreach (Process proc in procs)
                    {
                        try
                        {
                            string exePath = proc.MainModule != null ? proc.MainModule.FileName : null;
                            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                            {
                                return NativeInterop.ExtractShellFileIconDataUrl(exePath);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                finally
                {
                    foreach (Process proc in procs)
                    {
                        proc.Dispose();
                    }
                }

                using (RegistryKey packagesKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                {
                    if (packagesKey != null)
                    {
                        foreach (string subKeyName in packagesKey.GetSubKeyNames())
                        {
                            if (!subKeyName.StartsWith("Microsoft.GamingApp_", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            using (RegistryKey pkgKey = packagesKey.OpenSubKey(subKeyName))
                            {
                                object rootObj = pkgKey != null ? pkgKey.GetValue("PackageRootFolder") : null;
                                if (rootObj != null)
                                {
                                    string exePath = Path.Combine(rootObj.ToString(), "XboxPcApp.exe");
                                    if (File.Exists(exePath))
                                    {
                                        return NativeInterop.ExtractShellFileIconDataUrl(exePath);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static void AddShortcutsFromDirectory(string dir, string source, List<DesktopShortcutInfo> results, HashSet<string> seenPaths)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch
            {
                return;
            }

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file);
                if (!string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    FileAttributes attrs = File.GetAttributes(file);
                    if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (!seenPaths.Add(file))
                {
                    continue;
                }

                results.Add(new DesktopShortcutInfo
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    Extension = ext.TrimStart('.').ToLowerInvariant(),
                    Source = source,
                    IconDataUrl = NativeInterop.ExtractShortcutIconDataUrl(file)
                });
            }
        }

        public bool LaunchDesktopShortcut(string pathOrName, out string launchedName)
        {
            launchedName = null;
            if (string.IsNullOrWhiteSpace(pathOrName))
            {
                return false;
            }

            string query = pathOrName.Trim();
            IList<DesktopShortcutInfo> shortcuts = GetDesktopShortcuts();
            DesktopShortcutInfo match = null;

            foreach (DesktopShortcutInfo s in shortcuts)
            {
                if (string.Equals(s.Path, query, StringComparison.OrdinalIgnoreCase))
                {
                    match = s;
                    break;
                }
            }

            if (match == null)
            {
                foreach (DesktopShortcutInfo s in shortcuts)
                {
                    if (string.Equals(s.Name, query, StringComparison.OrdinalIgnoreCase))
                    {
                        match = s;
                        break;
                    }
                }
            }

            if (match == null)
            {
                return false;
            }

            if (string.Equals(match.Path, SteamBigPictureShortcutPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("Launching built-in shortcut \"" + match.Name + "\"");
                OpenSteamBigPicture();
                launchedName = match.Name;
                return true;
            }

            if (string.Equals(match.Path, XboxAppShortcutPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("Launching built-in shortcut \"" + match.Name + "\"");
                OpenXboxApp();
                launchedName = match.Name;
                return true;
            }

            if (!File.Exists(match.Path))
            {
                return false;
            }

            _logger.Info("Launching desktop shortcut \"" + match.Name + "\" (" + match.Path + ")");
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = match.Path,
                WorkingDirectory = Path.GetDirectoryName(match.Path),
                UseShellExecute = true
            };
            Process.Start(psi);
            if (string.Equals(match.Name, XboxAppShortcutName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(match.Name, "Xbox", StringComparison.OrdinalIgnoreCase))
            {
                MaximizeXboxAppWindow(TimeSpan.FromSeconds(8));
            }
            launchedName = match.Name;
            return true;
        }

        public IList<OpenWindowInfo> GetOpenWindows()
        {
            return NativeInterop.EnumerateOpenWindows(includeShellCloaked: true);
        }

        public bool FocusWindow(long hwnd)
        {
            bool ok = NativeInterop.FocusWindowByHandle(hwnd);
            _logger.Info("FocusWindow (hwnd=" + hwnd + ") -> " + ok);
            return ok;
        }

        public bool MaximizeWindow(long hwnd)
        {
            bool ok = NativeInterop.MaximizeWindowByHandle(hwnd);
            _logger.Info("MaximizeWindow (hwnd=" + hwnd + ") -> " + ok);
            return ok;
        }

        public bool CloseWindow(long hwnd)
        {
            bool ok = NativeInterop.CloseWindowByHandle(hwnd);
            _logger.Info("CloseWindow (hwnd=" + hwnd + ") -> " + ok);
            return ok;
        }

        public bool OpenTaskView()
        {
            bool ok = NativeInterop.OpenTaskView();
            _logger.Info("OpenTaskView (Win+Tab) -> " + ok);
            return ok;
        }

        public bool? GetOsdEnabled()
        {
            return NativeInterop.GetRtssOsdEnabled();
        }

        public bool TrySetOsdEnabled(bool enabled, out bool currentState)
        {
            bool ok = NativeInterop.TrySetRtssOsdEnabled(enabled, out currentState);
            _logger.Info("TrySetOsdEnabled (" + enabled + ") -> ok=" + ok + ", state=" + currentState);
            return ok;
        }

        public bool IsMonitorAvailable(string expectedMonitor)
        {
            return NativeInterop.IsMonitorAvailable(expectedMonitor);
        }

        public void SwitchDisplay(EnvironmentConfig env)
        {
            _logger.Info(string.Format("Switching display directly to \"{0}\" ({1}x{2}@{3}Hz, hdr={4})",
                env.Monitor, env.Width, env.Height, env.RefreshRate,
                env.Hdr.HasValue ? env.Hdr.Value.ToString() : "unchanged"));
            NativeInterop.SwitchToSingleMonitor(env.Monitor, env.Width, env.Height, env.RefreshRate, env.Hdr);
        }

        public bool VerifyOutputDevices(EnvironmentConfig env, bool checkAudio, out string details)
        {
            string[] monitors = NativeInterop.GetActiveMonitorNames();
            string audio = NativeInterop.GetDefaultAudioPlaybackDeviceName();
            string monitorList = monitors.Length > 0 ? string.Join(", ", monitors) : "(none)";

            bool monitorOk = true;
            if (!string.IsNullOrWhiteSpace(env.Monitor))
            {
                monitorOk = false;
                for (int i = 0; i < monitors.Length; i++)
                {
                    if (monitors[i].IndexOf(env.Monitor, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        monitorOk = true;
                        break;
                    }
                }
            }

            bool hdrOk = true;
            string hdrStatus = "n/a";
            if (env.Hdr.HasValue && monitorOk)
            {
                bool supported, enabled;
                if (NativeInterop.GetActiveMonitorHdrState(env.Monitor, out supported, out enabled))
                {
                    hdrStatus = supported ? (enabled ? "on" : "off") : "unsupported";
                    if (supported && enabled != env.Hdr.Value)
                    {
                        hdrOk = false;
                    }
                }
                else
                {
                    hdrStatus = "unknown";
                }
            }

            bool audioOk = true;
            if (checkAudio && !string.IsNullOrWhiteSpace(env.Audio))
            {
                audioOk = !string.IsNullOrEmpty(audio) &&
                    audio.IndexOf(env.Audio, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            details = string.Format("monitors=[{0}] (expected \"{1}\"), hdr={2} (expected {3}), audio=\"{4}\" (expected \"{5}\")",
                monitorList, env.Monitor ?? "", hdrStatus, env.Hdr.HasValue ? (env.Hdr.Value ? "on" : "off") : "any",
                audio, checkAudio ? (env.Audio ?? "") : "skip");
            return monitorOk && hdrOk && audioOk;
        }

        public bool TrySetDefaultAudioPlaybackDevice(string expectedAudio, out string matchedDevice)
        {
            bool ok = NativeInterop.SetDefaultAudioPlaybackDeviceByName(expectedAudio, out matchedDevice);
            if (ok)
            {
                _logger.Info("Set default audio playback device directly to \"" + matchedDevice + "\"");
            }
            else
            {
                _logger.Warn("Could not set default audio playback device matching \"" + (expectedAudio ?? "") + "\"");
            }
            return ok;
        }

        public void OpenXboxApp()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = @"shell:AppsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to launch Xbox app via AppsFolder (" + ex.Message + "); falling back to xbox://");
                LaunchUri("xbox://");
            }

            MaximizeXboxAppWindow(TimeSpan.FromSeconds(8));
        }

        private void MaximizeXboxAppWindow(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                List<OpenWindowInfo> windows = NativeInterop.EnumerateOpenWindows(includeShellCloaked: false);
                for (int i = 0; i < windows.Count; i++)
                {
                    OpenWindowInfo w = windows[i];
                    if (IsXboxAppWindow(w))
                    {
                        bool maximized = NativeInterop.MaximizeWindowByHandle(w.Hwnd);
                        _logger.Info("Maximized Xbox App window (hwnd=" + w.Hwnd + ") -> " + maximized);
                        return;
                    }
                }

                Thread.Sleep(150);
            }

            _logger.Info("Xbox App window not detected within timeout; continuing.");
        }

        public void OpenSteamBigPicture()
        {
            bool steamRunning = IsSteamRunning();
            if (steamRunning)
            {
                _logger.Info("Steam is already running; opening Big Picture via steam://open/bigpicture");
                LaunchUri("steam://open/bigpicture");
                FocusSteamBigPictureWindow(TimeSpan.FromSeconds(5));
                return;
            }

            if (File.Exists(_config.SteamPath))
            {
                _logger.Info("Steam is not running; launching \"" + _config.SteamPath + "\" -bigpicture");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _config.SteamPath,
                    Arguments = "-bigpicture",
                    UseShellExecute = true
                };
                Process.Start(psi);
                FocusSteamBigPictureWindow(TimeSpan.FromSeconds(10));
            }
            else
            {
                _logger.Info("SteamPath not found; falling back to steam://open/bigpicture");
                LaunchUri("steam://open/bigpicture");
                FocusSteamBigPictureWindow(TimeSpan.FromSeconds(8));
            }
        }

        private void WaitForXboxModeReady(long previousForegroundHwnd, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                List<OpenWindowInfo> windows = NativeInterop.EnumerateOpenWindows(includeShellCloaked: false);
                bool hasXboxWindow = false;
                for (int i = 0; i < windows.Count; i++)
                {
                    OpenWindowInfo w = windows[i];
                    if (IsXboxAppWindow(w))
                    {
                        hasXboxWindow = true;
                        break;
                    }
                }

                long currentFg = NativeInterop.GetForegroundWindowHandle();
                if (hasXboxWindow && (previousForegroundHwnd == 0 || currentFg != previousForegroundHwnd))
                {
                    // Allow the Xbox Full Screen Experience transition/activation to settle before launching Steam.
                    Thread.Sleep(400);
                    _logger.Info("Xbox mode window detected; transition ready.");
                    return;
                }

                Thread.Sleep(150);
            }

            _logger.Info("Timed out waiting for Xbox mode window; continuing.");
        }

        private static bool IsXboxAppWindow(OpenWindowInfo window)
        {
            if (window == null)
            {
                return false;
            }

            if (string.Equals(window.ProcessName, "XboxPcApp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(window.ProcessName, "XboxPcAppCE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(window.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(window.Title, "Xbox", StringComparison.OrdinalIgnoreCase);
        }

        private void FocusSteamBigPictureWindow(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                List<OpenWindowInfo> windows = NativeInterop.EnumerateOpenWindows(includeShellCloaked: false);
                for (int i = 0; i < windows.Count; i++)
                {
                    OpenWindowInfo w = windows[i];
                    if (IsSteamBigPictureWindow(w))
                    {
                        bool focused = NativeInterop.FocusWindowByHandle(w.Hwnd);
                        _logger.Info("Focused Steam Big Picture window (hwnd=" + w.Hwnd + ") -> " + focused);
                        return;
                    }
                }

                Thread.Sleep(200);
            }

            _logger.Info("Steam Big Picture window not detected within timeout; continuing.");
        }

        private static bool IsSteamBigPictureWindow(OpenWindowInfo window)
        {
            if (window == null || string.IsNullOrEmpty(window.Title))
            {
                return false;
            }

            bool isSteamProcess = string.Equals(window.ProcessName, "steamwebhelper", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(window.ProcessName, "steam", StringComparison.OrdinalIgnoreCase);
            return isSteamProcess && window.Title.IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void CloseSteamBigPicture(string closeMode)
        {
            if (!IsSteamRunning())
            {
                _logger.Info("Steam is not running; skipping Big Picture close.");
                return;
            }

            if (string.Equals(closeMode, "shutdown", StringComparison.OrdinalIgnoreCase) && File.Exists(_config.SteamPath))
            {
                _logger.Info("Shutting down Steam via -shutdown");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _config.SteamPath,
                    Arguments = "-shutdown",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            else
            {
                _logger.Info("Closing Steam Big Picture via steam://close/bigpicture");
                LaunchUri("steam://close/bigpicture");
            }

            Thread.Sleep(500);
        }

        public void CloseSteamWindow()
        {
            if (!IsSteamRunning())
            {
                return;
            }

            for (int attempt = 0; attempt < 12; attempt++)
            {
                int closed = NativeInterop.CloseSteamMainWindow();
                if (closed > 0)
                {
                    _logger.Info("Closed Steam main window (count=" + closed + "); Steam remains in background.");
                    return;
                }
                Thread.Sleep(250);
            }

            _logger.Info("No visible Steam main window found to close.");
        }

        public void CloseApplications(string[] appNames)
        {
            if (appNames == null || appNames.Length == 0)
            {
                return;
            }

            foreach (string rawName in appNames)
            {
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }

                string processName = Path.GetFileNameWithoutExtension(rawName.Trim());
                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                Process[] procs = Process.GetProcessesByName(processName);
                if (procs == null || procs.Length == 0)
                {
                    _logger.Info("Application \"" + processName + "\" is not running.");
                    continue;
                }

                foreach (Process proc in procs)
                {
                    try
                    {
                        int pid = proc.Id;
                        bool exitedGracefully = false;

                        if (proc.MainWindowHandle != IntPtr.Zero && proc.CloseMainWindow())
                        {
                            exitedGracefully = proc.WaitForExit(1000);
                        }

                        if (exitedGracefully || proc.HasExited)
                        {
                            _logger.Info("Closed application \"" + processName + "\" (pid=" + pid + ").");
                        }
                        else
                        {
                            proc.Kill();
                            proc.WaitForExit(2000);
                            _logger.Info("Force-closed application \"" + processName + "\" (pid=" + pid + ").");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("Failed to close application \"" + processName + "\": " + ex.Message);
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
        }

        public void StartHueSync()
        {
            string processName = GetHueSyncProcessName();
            if (IsProcessRunning(processName))
            {
                _logger.Info("Hue Sync is already running.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.HueSyncPath) || !File.Exists(_config.HueSyncPath))
            {
                _logger.Warn("Hue Sync executable not found at \"" + (_config.HueSyncPath ?? "") + "\".");
                return;
            }

            _logger.Info("Starting Hue Sync from \"" + _config.HueSyncPath + "\"");
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _config.HueSyncPath,
                WorkingDirectory = Path.GetDirectoryName(_config.HueSyncPath),
                UseShellExecute = true
            };
            Process.Start(psi);

            CloseWindowWhenReady(processName, "Philips Hue Sync");
        }

        public void CloseWindowWhenReady(string processName, string exactTitle = null)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return;
            }
            Task.Run(() => CloseProcessWindowWhenReady(processName, exactTitle));
        }

        private void CloseProcessWindowWhenReady(string processName, string exactTitle)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                Thread.Sleep(250);
                if (!IsProcessRunning(processName))
                {
                    return;
                }
                int closed = NativeInterop.CloseProcessWindows(processName, exactTitle);
                if (closed > 0)
                {
                    _logger.Info("Closed window for \"" + processName + "\" (count=" + closed + ").");
                    return;
                }
            }

            _logger.Info("No visible window found to close for \"" + processName + "\".");
        }

        public void StopHueSync()
        {
            CloseApplications(new[] { GetHueSyncProcessName() });
        }

        public StreamingSessionInfo GetStreamingSessionInfo()
        {
            bool hasNetworkStream = IsStreamingNetworkSessionActive();
            if (!hasNetworkStream)
            {
                return new StreamingSessionInfo
                {
                    Active = false,
                    DisplayName = "",
                    Reason = ""
                };
            }

            string matchedVirtualDisplay = FindActiveVirtualDisplayName();
            bool hasVirtualDisplay = !string.IsNullOrEmpty(matchedVirtualDisplay);

            return new StreamingSessionInfo
            {
                Active = true,
                DisplayName = matchedVirtualDisplay ?? "",
                Reason = hasVirtualDisplay ? "virtual-display+network-stream" : "network-stream"
            };
        }

        public bool StopStreamingSession()
        {
            bool handledViaService = TryRestartStreamingService();
            if (!handledViaService)
            {
                CloseApplications(_config.StreamingProcessNames);
            }

            for (int i = 0; i < 20; i++)
            {
                StreamingSessionInfo info = GetStreamingSessionInfo();
                if (info == null || !info.Active)
                {
                    _logger.Info("Streaming session terminated cleanly.");
                    return true;
                }
                Thread.Sleep(250);
            }

            _logger.Warn("Streaming session still appears active after termination attempt.");
            return false;
        }

        private string FindActiveVirtualDisplayName()
        {
            string[] patterns = _config.StreamingVirtualDisplayPatterns;
            if (patterns == null || patterns.Length == 0)
            {
                return null;
            }

            NativeInterop.ActiveDisplayInfo[] displays = NativeInterop.GetActiveDisplayInfos();
            if (displays == null || displays.Length == 0)
            {
                return null;
            }

            foreach (NativeInterop.ActiveDisplayInfo display in displays)
            {
                string friendly = display.FriendlyName ?? "";
                string devPath = display.DevicePath ?? "";
                foreach (string pattern in patterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        continue;
                    }
                    if ((!string.IsNullOrEmpty(friendly) && friendly.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(devPath) && devPath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return !string.IsNullOrWhiteSpace(friendly) ? friendly : pattern;
                    }
                }
            }

            return null;
        }

        private bool IsStreamingNetworkSessionActive()
        {
            if (!IsAnyStreamingProcessRunning())
            {
                return false;
            }

            try
            {
                IPGlobalProperties ipProps = IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] udpListeners = ipProps.GetActiveUdpListeners();
                if (udpListeners != null)
                {
                    foreach (IPEndPoint ep in udpListeners)
                    {
                        if (ep.Port == 47998 || ep.Port == 47999 || ep.Port == 48000 || ep.Port == 48002)
                        {
                            return true;
                        }
                    }
                }

                TcpConnectionInformation[] tcpConns = ipProps.GetActiveTcpConnections();
                if (tcpConns != null)
                {
                    foreach (TcpConnectionInformation conn in tcpConns)
                    {
                        if (conn.State == TcpState.Established && conn.LocalEndPoint != null && conn.LocalEndPoint.Port == 48010)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to inspect network sockets for streaming session: " + ex.Message);
            }

            return false;
        }

        private bool IsAnyStreamingProcessRunning()
        {
            string[] procNames = _config.StreamingProcessNames;
            if (procNames == null || procNames.Length == 0)
            {
                return false;
            }

            foreach (string rawName in procNames)
            {
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }
                string name = Path.GetFileNameWithoutExtension(rawName.Trim());
                if (!string.IsNullOrWhiteSpace(name) && IsProcessRunning(name))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryRestartStreamingService()
        {
            string[] serviceNames = _config.StreamingServiceNames;
            if (serviceNames == null || serviceNames.Length == 0)
            {
                return false;
            }

            foreach (string serviceName in serviceNames)
            {
                if (string.IsNullOrWhiteSpace(serviceName))
                {
                    continue;
                }

                try
                {
                    using (ServiceController sc = new ServiceController(serviceName.Trim()))
                    {
                        ServiceControllerStatus status = sc.Status;
                        if (status == ServiceControllerStatus.Stopped || status == ServiceControllerStatus.StopPending)
                        {
                            continue;
                        }

                        _logger.Info("Stopping streaming service \"" + sc.ServiceName + "\" to end active session...");
                        if (sc.CanStop)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        }

                        WaitForStreamingProcessesToExit(TimeSpan.FromSeconds(5));

                        _logger.Info("Restarting streaming service \"" + sc.ServiceName + "\"...");
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Service not installed on this machine
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to restart streaming service \"" + serviceName + "\": " + ex.Message);
                }
            }

            return false;
        }

        private void WaitForStreamingProcessesToExit(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!IsAnyStreamingProcessRunning())
                {
                    return;
                }
                Thread.Sleep(200);
            }
        }

        public void SleepComputer()
        {
            _logger.Info("Putting computer into S3 sleep.");
            bool ok = NativeInterop.SuspendToSleep();
            if (!ok)
            {
                _logger.Warn("SetSuspendState returned false.");
            }
        }

        public void ShutdownComputer()
        {
            _logger.Info("Shutting down computer.");
            ProcessStartInfo psi = new ProcessStartInfo("shutdown.exe", "/s /t 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }

        private string GetHueSyncProcessName()
        {
            if (!string.IsNullOrWhiteSpace(_config.HueSyncPath))
            {
                string name = Path.GetFileNameWithoutExtension(_config.HueSyncPath.Trim());
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            return "HueSync";
        }

        private static bool IsSteamRunning()
        {
            return IsProcessRunning("steam");
        }

        private static bool IsProcessRunning(string processName)
        {
            Process[] procs = Process.GetProcessesByName(processName);
            bool running = procs != null && procs.Length > 0;
            if (procs != null)
            {
                foreach (Process p in procs)
                {
                    p.Dispose();
                }
            }
            return running;
        }

        private static void LaunchUri(string uri)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        public void LaunchApp(string target)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            };
            if (!target.Contains("://"))
            {
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    psi.WorkingDirectory = dir;
                }
            }

            Process.Start(psi);
        }
    }
}
