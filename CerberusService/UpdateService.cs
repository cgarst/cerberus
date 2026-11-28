using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RoomAgent
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UpdateStatus
    {
        Idle,
        Checking,
        UpToDate,
        UpdateAvailable,
        Downloading,
        Installing,
        Error
    }

    public sealed class UpdateReleaseInfo
    {
        public string Channel { get; set; }
        public string Version { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
        public string PublishedAt { get; set; }
        public string ReleaseUrl { get; set; }
        public string Notes { get; set; }
    }

    public sealed class UpdateStateSnapshot
    {
        public UpdateStatus Status { get; set; }
        public string CurrentVersion { get; set; }
        public string ConfiguredChannel { get; set; }
        public string EffectiveChannel { get; set; }
        public UpdateReleaseInfo AvailableUpdate { get; set; }
        public string LastCheckedAt { get; set; }
        public string ErrorMessage { get; set; }
        public int DownloadProgressPercent { get; set; }
    }

    public sealed class UpdateService : IDisposable
    {
        public const string DefaultManifestUrl = "https://raw.githubusercontent.com/cgarst/cerberus/updates/update.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex AlphaDatePrefixRegex = new Regex(
            @"^\d+\.\d+\.\d+-alpha\.(\d{8})-[a-fA-F0-9]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly AgentConfig _config;
        private readonly ILogger _logger;
        private readonly string _manifestUrl;
        private readonly Action _requestShutdown;
        private readonly HttpClient _httpClient;
        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);

        private UpdateStatus _status = UpdateStatus.Idle;
        private UpdateReleaseInfo _availableUpdate;
        private string _lastCheckedAt;
        private string _errorMessage;
        private int _downloadProgressPercent;
        private bool _disposed;

        public event EventHandler<UpdateStateSnapshot> StateChanged;

        public UpdateService(
            AgentConfig config,
            ILogger logger,
            Action requestShutdown = null,
            string manifestUrl = null)
        {
            _config = config ?? throw new ArgumentNullException("config");
            _logger = logger;
            _requestShutdown = requestShutdown;
            _manifestUrl = string.IsNullOrWhiteSpace(manifestUrl) ? DefaultManifestUrl : manifestUrl;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CerberusService/" + GetCurrentVersion());
        }

        public static string GetCurrentVersion()
        {
            Assembly asm = typeof(UpdateService).Assembly;
            AssemblyInformationalVersionAttribute infoAttr =
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (infoAttr != null && !string.IsNullOrWhiteSpace(infoAttr.InformationalVersion))
            {
                return NormalizeVersionString(infoAttr.InformationalVersion);
            }

            Version v = asm.GetName().Version;
            if (v != null)
            {
                return string.Format("{0}.{1}.{2}", v.Major, v.Minor, Math.Max(0, v.Build));
            }
            return "1.0.0";
        }

        public static string NormalizeVersionString(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return "";
            string trimmed = version.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1);
            }
            int plusIdx = trimmed.IndexOf('+');
            if (plusIdx >= 0)
            {
                trimmed = trimmed.Substring(0, plusIdx);
            }
            return trimmed.Trim();
        }

        public static string ResolveEffectiveChannel(string configuredChannel, string currentVersion)
        {
            string normalized = AgentConfig.NormalizeUpdateChannel(configuredChannel);
            if (!string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            string ver = NormalizeVersionString(currentVersion);
            if (ver.IndexOf("-alpha", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "alpha";
            }
            if (ver.IndexOf('-') >= 0)
            {
                return "beta";
            }
            return "stable";
        }

        public static bool IsUpdateAvailable(string currentVersion, string candidateVersion, string effectiveChannel)
        {
            string current = NormalizeVersionString(currentVersion);
            string candidate = NormalizeVersionString(candidateVersion);
            if (string.IsNullOrEmpty(candidate)) return false;
            if (string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrEmpty(current)) return true;

            int cmp = CompareVersions(candidate, current);
            if (cmp > 0) return true;
            if (cmp < 0)
            {
                if (string.Equals(effectiveChannel, "alpha", StringComparison.OrdinalIgnoreCase))
                {
                    Match mCur = AlphaDatePrefixRegex.Match(current);
                    Match mCand = AlphaDatePrefixRegex.Match(candidate);
                    if (mCur.Success && mCand.Success &&
                        string.Equals(mCur.Groups[1].Value, mCand.Groups[1].Value, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }

            return false;
        }

        public static int CompareVersions(string left, string right)
        {
            ParsedSemVer a = ParsedSemVer.Parse(left);
            ParsedSemVer b = ParsedSemVer.Parse(right);

            int c = a.Major.CompareTo(b.Major);
            if (c != 0) return c;
            c = a.Minor.CompareTo(b.Minor);
            if (c != 0) return c;
            c = a.Patch.CompareTo(b.Patch);
            if (c != 0) return c;

            bool aHasPre = a.Prerelease.Length > 0;
            bool bHasPre = b.Prerelease.Length > 0;
            if (!aHasPre && !bHasPre) return 0;
            if (!aHasPre) return 1;
            if (!bHasPre) return -1;

            int len = Math.Min(a.Prerelease.Length, b.Prerelease.Length);
            for (int i = 0; i < len; i++)
            {
                string pa = a.Prerelease[i];
                string pb = b.Prerelease[i];
                bool aNum = long.TryParse(pa, out long na);
                bool bNum = long.TryParse(pb, out long nb);
                if (aNum && bNum)
                {
                    c = na.CompareTo(nb);
                    if (c != 0) return c;
                }
                else if (aNum != bNum)
                {
                    return aNum ? -1 : 1;
                }
                else
                {
                    c = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                }
            }

            return a.Prerelease.Length.CompareTo(b.Prerelease.Length);
        }

        public static Dictionary<string, UpdateReleaseInfo> ParseManifest(string json)
        {
            Dictionary<string, UpdateReleaseInfo> result =
                new Dictionary<string, UpdateReleaseInfo>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return result;

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("channels", out JsonElement channelsEl) ||
                    channelsEl.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

                foreach (JsonProperty prop in channelsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    UpdateReleaseInfo info = JsonSerializer.Deserialize<UpdateReleaseInfo>(prop.Value.GetRawText(), JsonOptions);
                    if (info == null) continue;
                    info.Channel = prop.Name.ToLowerInvariant();
                    result[prop.Name] = info;
                }
            }

            return result;
        }

        public UpdateStateSnapshot GetSnapshot()
        {
            string current = GetCurrentVersion();
            string configured = AgentConfig.NormalizeUpdateChannel(_config.UpdateChannel);
            string effective = ResolveEffectiveChannel(configured, current);

            lock (_stateLock)
            {
                return new UpdateStateSnapshot
                {
                    Status = _status,
                    CurrentVersion = current,
                    ConfiguredChannel = configured,
                    EffectiveChannel = effective,
                    AvailableUpdate = _availableUpdate,
                    LastCheckedAt = _lastCheckedAt,
                    ErrorMessage = _errorMessage,
                    DownloadProgressPercent = _downloadProgressPercent
                };
            }
        }

        public async Task<UpdateStateSnapshot> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            if (_disposed) return GetSnapshot();

            string current = GetCurrentVersion();
            string configured = AgentConfig.NormalizeUpdateChannel(_config.UpdateChannel);
            string effective = ResolveEffectiveChannel(configured, current);
            if (string.Equals(effective, "off", StringComparison.OrdinalIgnoreCase))
            {
                UpdateState(UpdateStatus.Idle, null, null, 0);
                return GetSnapshot();
            }

            await _operationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                UpdateState(UpdateStatus.Checking, null, null, 0);

                using (CancellationTokenSource checkCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    checkCts.CancelAfter(TimeSpan.FromSeconds(20));
                    string manifestJson = await _httpClient.GetStringAsync(_manifestUrl, checkCts.Token).ConfigureAwait(false);
                    Dictionary<string, UpdateReleaseInfo> channels = ParseManifest(manifestJson);

                    lock (_stateLock)
                    {
                        _lastCheckedAt = DateTime.UtcNow.ToString("o");
                    }

                    if (!channels.TryGetValue(effective, out UpdateReleaseInfo candidate) ||
                        candidate == null ||
                        string.IsNullOrWhiteSpace(candidate.Url) ||
                        string.IsNullOrWhiteSpace(candidate.Version))
                    {
                        UpdateState(UpdateStatus.UpToDate, null, null, 0);
                        return GetSnapshot();
                    }

                    if (IsUpdateAvailable(current, candidate.Version, effective))
                    {
                        if (_logger != null)
                        {
                            _logger.Info(string.Format(
                                "Update available on channel '{0}': {1} -> {2}",
                                effective, current, candidate.Version));
                        }
                        UpdateState(UpdateStatus.UpdateAvailable, candidate, null, 0);
                    }
                    else
                    {
                        UpdateState(UpdateStatus.UpToDate, null, null, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Warn("Update check failed: " + ex.Message);
                UpdateState(UpdateStatus.Error, null, ex.Message, 0);
            }
            finally
            {
                _operationLock.Release();
            }

            return GetSnapshot();
        }

        public async Task<bool> DownloadAndInstallAsync(CancellationToken ct = default)
        {
            if (_disposed) return false;

            await _operationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                UpdateReleaseInfo target;
                lock (_stateLock)
                {
                    target = _availableUpdate;
                }

                if (target == null || string.IsNullOrWhiteSpace(target.Url))
                {
                    return false;
                }

                if (!Uri.TryCreate(target.Url, UriKind.Absolute, out Uri downloadUri) ||
                    !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Update URL must use HTTPS.");
                }

                UpdateState(UpdateStatus.Downloading, target, null, 0);

                string tempDir = Path.Combine(Path.GetTempPath(), "CerberusServiceUpdate");
                Directory.CreateDirectory(tempDir);
                string fileName = "CerberusServiceSetup-" + SanitizeFilePart(target.Version) + "-win-x64.exe";
                string installerPath = Path.Combine(tempDir, fileName);

                using (HttpResponseMessage response = await _httpClient
                    .GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = response.Content.Headers.ContentLength ?? target.Size;

                    using (Stream remoteStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    using (FileStream localStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        long totalRead = 0;
                        int lastPercent = -1;
                        int read;
                        while ((read = await remoteStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await localStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            totalRead += read;
                            if (totalBytes > 0)
                            {
                                int percent = (int)Math.Min(100, (totalRead * 100L) / totalBytes);
                                if (percent != lastPercent && (percent % 5 == 0 || percent == 100))
                                {
                                    lastPercent = percent;
                                    UpdateState(UpdateStatus.Downloading, target, null, percent);
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(target.Sha256))
                {
                    string actualHash = ComputeSha256(installerPath);
                    if (!string.Equals(actualHash, target.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(installerPath); } catch { }
                        throw new InvalidOperationException("Downloaded installer SHA-256 verification failed.");
                    }
                }

                UpdateState(UpdateStatus.Installing, target, null, 100);
                if (_logger != null)
                {
                    _logger.Info("Launching CerberusService installer for version " + target.Version + ": " + installerPath);
                }

                bool elevated = IsProcessElevated();
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CERBERUSAUTOUPDATE",
                    UseShellExecute = !elevated,
                    Verb = elevated ? "" : "runas"
                };

                Process.Start(psi);
                _requestShutdown?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Update install failed", ex);
                UpdateReleaseInfo currentTarget;
                lock (_stateLock)
                {
                    currentTarget = _availableUpdate;
                }
                UpdateState(UpdateStatus.Error, currentTarget, ex.Message, 0);
                return false;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private void UpdateState(UpdateStatus status, UpdateReleaseInfo availableUpdate, string errorMessage, int progressPercent)
        {
            UpdateStateSnapshot snapshot;
            lock (_stateLock)
            {
                _status = status;
                _availableUpdate = availableUpdate;
                _errorMessage = errorMessage;
                _downloadProgressPercent = progressPercent;
            }

            snapshot = GetSnapshot();
            StateChanged?.Invoke(this, snapshot);
        }

        private static string ComputeSha256(string filePath)
        {
            using (FileStream fs = File.OpenRead(filePath))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string SanitizeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "update";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value;
        }

        private static bool IsProcessElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private sealed class ParsedSemVer
        {
            public int Major;
            public int Minor;
            public int Patch;
            public string[] Prerelease = Array.Empty<string>();

            public static ParsedSemVer Parse(string raw)
            {
                string norm = NormalizeVersionString(raw);
                ParsedSemVer result = new ParsedSemVer();
                if (string.IsNullOrEmpty(norm)) return result;

                int dashIdx = norm.IndexOf('-');
                string core = dashIdx >= 0 ? norm.Substring(0, dashIdx) : norm;
                string pre = dashIdx >= 0 ? norm.Substring(dashIdx + 1) : "";

                string[] coreParts = core.Split('.');
                if (coreParts.Length > 0) int.TryParse(coreParts[0], out result.Major);
                if (coreParts.Length > 1) int.TryParse(coreParts[1], out result.Minor);
                if (coreParts.Length > 2) int.TryParse(coreParts[2], out result.Patch);

                if (!string.IsNullOrWhiteSpace(pre))
                {
                    result.Prerelease = pre.Split('.', StringSplitOptions.RemoveEmptyEntries);
                }
                return result;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
            _operationLock.Dispose();
        }
    }
}
