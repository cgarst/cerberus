using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace RoomAgent
{
    public sealed class SteamHeroResolver
    {
        private static readonly string[] ImageExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp" };
        private static readonly string[] SteamCacheImageExtensions = new[] { ".jpg", ".png", ".jpeg", ".webp" };
        private static readonly TimeSpan IndexRefreshInterval = TimeSpan.FromSeconds(15);

        private readonly string _configuredSteamExePath;
        private readonly string _explicitSteamRootDir;
        private readonly object _lock = new object();

        private DateTime _lastIndexBuildUtc = DateTime.MinValue;
        private string _cachedSteamRoot;
        private List<SteamAppEntry> _steamApps = new List<SteamAppEntry>();
        private List<NonSteamShortcutEntry> _nonSteamShortcuts = new List<NonSteamShortcutEntry>();
        private List<string> _userGridDirs = new List<string>();
        private Dictionary<string, string> _userGridDirsByUserId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _servedPathsByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _keysByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private sealed class SteamAppEntry
        {
            public uint AppId;
            public string Name;
            public string InstallDir;
        }

        private sealed class NonSteamShortcutEntry
        {
            public uint AppId;
            public string UserId;
            public string AppName;
            public string ExePath;
            public string StartDir;
        }

        public SteamHeroResolver(string configuredSteamExePath = null, string explicitSteamRootDir = null)
        {
            _configuredSteamExePath = configuredSteamExePath;
            _explicitSteamRootDir = explicitSteamRootDir;
        }

        public string ResolveHeroUrl(string exePath, string appName)
        {
            string filePath = ResolveHeroFilePath(exePath, appName);
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            long ticks = 0;
            try
            {
                ticks = File.GetLastWriteTimeUtc(filePath).Ticks;
            }
            catch
            {
                return null;
            }

            string key;
            lock (_lock)
            {
                string cacheId = filePath + "|" + ticks;
                if (!_keysByPath.TryGetValue(cacheId, out key))
                {
                    key = ComputeStableKey(cacheId);
                    if (_servedPathsByKey.Count >= 128)
                    {
                        _servedPathsByKey.Clear();
                        _keysByPath.Clear();
                    }
                    _keysByPath[cacheId] = key;
                    _servedPathsByKey[key] = filePath;
                }
            }

            return "/api/frame-app-hero?key=" + Uri.EscapeDataString(key);
        }

        public bool TryGetHeroImage(string key, out byte[] bytes, out string contentType)
        {
            bytes = null;
            contentType = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string filePath;
            lock (_lock)
            {
                if (!_servedPathsByKey.TryGetValue(key, out filePath) || string.IsNullOrEmpty(filePath))
                {
                    return false;
                }
            }

            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }
                bytes = File.ReadAllBytes(filePath);
                contentType = GetImageContentType(filePath);
                return bytes != null && bytes.Length > 0;
            }
            catch
            {
                bytes = null;
                contentType = null;
                return false;
            }
        }

        public string ResolveHeroFilePath(string exePath, string appName)
        {
            string normalizedExe = NormalizePath(exePath);
            string normalizedAppName = NormalizeTitle(appName);

            if (string.IsNullOrEmpty(normalizedExe) && string.IsNullOrEmpty(normalizedAppName))
            {
                return null;
            }

            lock (_lock)
            {
                EnsureIndexFreshLocked();
                if (string.IsNullOrEmpty(_cachedSteamRoot))
                {
                    return null;
                }

                // 1. Check Non-Steam shortcuts first (exact exe, StartDir, or AppName match)
                NonSteamShortcutEntry shortcut = MatchNonSteamShortcutLocked(normalizedExe, normalizedAppName);
                if (shortcut != null)
                {
                    string shortcutHero = FindHeroFileLocked(shortcut.AppId, shortcut.UserId, isNonSteam: true);
                    if (!string.IsNullOrEmpty(shortcutHero))
                    {
                        return shortcutHero;
                    }
                }

                // 2. Check installed Steam apps (longest installdir prefix, then name)
                SteamAppEntry steamApp = MatchSteamAppLocked(normalizedExe, normalizedAppName);
                if (steamApp != null)
                {
                    string steamHero = FindHeroFileLocked(steamApp.AppId, null, isNonSteam: false);
                    if (!string.IsNullOrEmpty(steamHero))
                    {
                        return steamHero;
                    }
                }

                return null;
            }
        }

        private void EnsureIndexFreshLocked()
        {
            DateTime now = DateTime.UtcNow;
            if (_cachedSteamRoot != null && (now - _lastIndexBuildUtc) < IndexRefreshInterval)
            {
                return;
            }

            _lastIndexBuildUtc = now;
            _cachedSteamRoot = FindSteamRootDir();
            _steamApps = new List<SteamAppEntry>();
            _nonSteamShortcuts = new List<NonSteamShortcutEntry>();
            _userGridDirs = new List<string>();
            _userGridDirsByUserId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(_cachedSteamRoot))
            {
                return;
            }

            IndexUserdataLocked(_cachedSteamRoot);
            IndexSteamLibrariesLocked(_cachedSteamRoot);
        }

        private string FindSteamRootDir()
        {
            if (!string.IsNullOrWhiteSpace(_explicitSteamRootDir))
            {
                string explicitDir = NormalizePath(_explicitSteamRootDir);
                if (!string.IsNullOrEmpty(explicitDir) && Directory.Exists(explicitDir))
                {
                    return explicitDir;
                }
                return null;
            }

            if (!string.IsNullOrWhiteSpace(_configuredSteamExePath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(_configuredSteamExePath.Trim().Trim('"'));
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        return NormalizePath(dir);
                    }
                }
                catch
                {
                }
            }

            string[] regCandidates = new[]
            {
                ReadRegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                @"C:\Program Files (x86)\Steam"
            };

            for (int i = 0; i < regCandidates.Length; i++)
            {
                string candidate = NormalizePath(regCandidates[i]);
                if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string ReadRegistryValue(RegistryKey hive, string subKey, string valueName)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(subKey))
                {
                    return key != null ? key.GetValue(valueName) as string : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private void IndexUserdataLocked(string steamRoot)
        {
            string userdataDir = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdataDir))
            {
                return;
            }

            string[] userDirs;
            try
            {
                userDirs = Directory.GetDirectories(userdataDir);
            }
            catch
            {
                return;
            }

            for (int i = 0; i < userDirs.Length; i++)
            {
                string userDir = userDirs[i];
                string userId = Path.GetFileName(userDir);
                if (string.IsNullOrEmpty(userId))
                {
                    continue;
                }

                string configDir = Path.Combine(userDir, "config");
                string gridDir = Path.Combine(configDir, "grid");
                if (Directory.Exists(gridDir))
                {
                    _userGridDirs.Add(gridDir);
                    _userGridDirsByUserId[userId] = gridDir;
                }

                string shortcutsPath = Path.Combine(configDir, "shortcuts.vdf");
                if (File.Exists(shortcutsPath))
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(shortcutsPath);
                        List<NonSteamShortcutEntry> parsed = ParseShortcutsVdf(data, userId);
                        _nonSteamShortcuts.AddRange(parsed);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void IndexSteamLibrariesLocked(string steamRoot)
        {
            List<string> libraryRoots = new List<string>();
            HashSet<string> seenLibs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddLib(string dir)
            {
                string norm = NormalizePath(dir);
                if (!string.IsNullOrEmpty(norm) && Directory.Exists(norm) && seenLibs.Add(norm))
                {
                    libraryRoots.Add(norm);
                }
            }

            AddLib(steamRoot);

            string libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFoldersPath))
            {
                try
                {
                    string text = File.ReadAllText(libraryFoldersPath, Encoding.UTF8);
                    foreach (KeyValuePair<string, string> kv in EnumerateVdfKeyValues(text))
                    {
                        if (string.Equals(kv.Key, "path", StringComparison.OrdinalIgnoreCase))
                        {
                            AddLib(kv.Value);
                        }
                    }
                }
                catch
                {
                }
            }

            for (int i = 0; i < libraryRoots.Count; i++)
            {
                string steamAppsDir = Path.Combine(libraryRoots[i], "steamapps");
                if (!Directory.Exists(steamAppsDir))
                {
                    continue;
                }

                string[] manifests;
                try
                {
                    manifests = Directory.GetFiles(steamAppsDir, "appmanifest_*.acf");
                }
                catch
                {
                    continue;
                }

                for (int j = 0; j < manifests.Length; j++)
                {
                    try
                    {
                        string acfText = File.ReadAllText(manifests[j], Encoding.UTF8);
                        uint appId = 0;
                        string name = null;
                        string installDirName = null;

                        foreach (KeyValuePair<string, string> kv in EnumerateVdfKeyValues(acfText))
                        {
                            if (string.Equals(kv.Key, "appid", StringComparison.OrdinalIgnoreCase))
                            {
                                uint.TryParse(kv.Value, out appId);
                            }
                            else if (string.Equals(kv.Key, "name", StringComparison.OrdinalIgnoreCase))
                            {
                                name = kv.Value;
                            }
                            else if (string.Equals(kv.Key, "installdir", StringComparison.OrdinalIgnoreCase))
                            {
                                installDirName = kv.Value;
                            }
                        }

                        if (appId > 0 && !string.IsNullOrWhiteSpace(installDirName))
                        {
                            string fullInstallDir = NormalizePath(Path.Combine(steamAppsDir, "common", installDirName));
                            if (!string.IsNullOrEmpty(fullInstallDir))
                            {
                                _steamApps.Add(new SteamAppEntry
                                {
                                    AppId = appId,
                                    Name = name,
                                    InstallDir = fullInstallDir
                                });
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private NonSteamShortcutEntry MatchNonSteamShortcutLocked(string normalizedExe, string normalizedAppName)
        {
            if (_nonSteamShortcuts.Count == 0)
            {
                return null;
            }

            // 1. Exact Exe path match
            if (!string.IsNullOrEmpty(normalizedExe))
            {
                for (int i = 0; i < _nonSteamShortcuts.Count; i++)
                {
                    NonSteamShortcutEntry s = _nonSteamShortcuts[i];
                    if (!string.IsNullOrEmpty(s.ExePath) &&
                        string.Equals(s.ExePath, normalizedExe, StringComparison.OrdinalIgnoreCase))
                    {
                        return s;
                    }
                }

                // 2. StartDir prefix match (when shortcut launches a wrapper/launcher in the game folder)
                NonSteamShortcutEntry bestStartDirMatch = null;
                int bestStartDirLen = -1;
                for (int i = 0; i < _nonSteamShortcuts.Count; i++)
                {
                    NonSteamShortcutEntry s = _nonSteamShortcuts[i];
                    if (IsSpecificDirectory(s.StartDir) && IsPathInsideDirectory(normalizedExe, s.StartDir))
                    {
                        if (s.StartDir.Length > bestStartDirLen)
                        {
                            bestStartDirLen = s.StartDir.Length;
                            bestStartDirMatch = s;
                        }
                    }
                }
                if (bestStartDirMatch != null)
                {
                    return bestStartDirMatch;
                }
            }

            // 3. Fallback by AppName (if shortcut has hero art in grid)
            if (!string.IsNullOrEmpty(normalizedAppName))
            {
                for (int i = 0; i < _nonSteamShortcuts.Count; i++)
                {
                    NonSteamShortcutEntry s = _nonSteamShortcuts[i];
                    if (string.Equals(NormalizeTitle(s.AppName), normalizedAppName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(FindHeroFileLocked(s.AppId, s.UserId, isNonSteam: true)))
                        {
                            return s;
                        }
                    }
                }
            }

            return null;
        }

        private SteamAppEntry MatchSteamAppLocked(string normalizedExe, string normalizedAppName)
        {
            if (_steamApps.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(normalizedExe))
            {
                SteamAppEntry best = null;
                int bestLen = -1;
                for (int i = 0; i < _steamApps.Count; i++)
                {
                    SteamAppEntry app = _steamApps[i];
                    if (!string.IsNullOrEmpty(app.InstallDir) && IsPathInsideDirectory(normalizedExe, app.InstallDir))
                    {
                        if (app.InstallDir.Length > bestLen)
                        {
                            bestLen = app.InstallDir.Length;
                            best = app;
                        }
                    }
                }
                if (best != null)
                {
                    return best;
                }
            }

            if (!string.IsNullOrEmpty(normalizedAppName))
            {
                for (int i = 0; i < _steamApps.Count; i++)
                {
                    SteamAppEntry app = _steamApps[i];
                    if (string.Equals(NormalizeTitle(app.Name), normalizedAppName, StringComparison.OrdinalIgnoreCase))
                    {
                        return app;
                    }
                }
            }

            return null;
        }

        private string FindHeroFileLocked(uint appId, string preferredUserId, bool isNonSteam)
        {
            string appIdStr = appId.ToString();

            // 1. User-assigned custom hero art in userdata/<uid>/config/grid/<appid>_hero.*
            string customHero = FindInGridDirsLocked(appIdStr + "_hero", preferredUserId);
            if (!string.IsNullOrEmpty(customHero))
            {
                return customHero;
            }

            // 2. Official Steam librarycache hero art (only for Steam AppIDs)
            if (!isNonSteam)
            {
                string cacheHero = FindInLibraryCacheLocked(appIdStr, "library_hero");
                if (!string.IsNullOrEmpty(cacheHero))
                {
                    return cacheHero;
                }
            }

            // 3. Fallback to landscape custom grid capsule (<appid>.*)
            string customCapsule = FindInGridDirsLocked(appIdStr, preferredUserId);
            if (!string.IsNullOrEmpty(customCapsule))
            {
                return customCapsule;
            }

            // 4. Fallback to official Steam librarycache header (<appid>/header.*)
            if (!isNonSteam)
            {
                string cacheHeader = FindInLibraryCacheLocked(appIdStr, "header");
                if (!string.IsNullOrEmpty(cacheHeader))
                {
                    return cacheHeader;
                }
            }

            return null;
        }

        private string FindInGridDirsLocked(string baseFileName, string preferredUserId)
        {
            if (!string.IsNullOrEmpty(preferredUserId))
            {
                string preferredDir;
                if (_userGridDirsByUserId.TryGetValue(preferredUserId, out preferredDir))
                {
                    string hit = FindImageWithBaseName(preferredDir, baseFileName, ImageExtensions);
                    if (!string.IsNullOrEmpty(hit))
                    {
                        return hit;
                    }
                }
            }

            for (int i = 0; i < _userGridDirs.Count; i++)
            {
                string hit = FindImageWithBaseName(_userGridDirs[i], baseFileName, ImageExtensions);
                if (!string.IsNullOrEmpty(hit))
                {
                    return hit;
                }
            }

            return null;
        }

        private string FindInLibraryCacheLocked(string appIdStr, string assetName)
        {
            string libraryCacheDir = Path.Combine(_cachedSteamRoot, "appcache", "librarycache");
            if (!Directory.Exists(libraryCacheDir))
            {
                return null;
            }

            // Modern Steam layout: appcache/librarycache/<appid>/<assetName>.jpg
            string appDir = Path.Combine(libraryCacheDir, appIdStr);
            if (Directory.Exists(appDir))
            {
                string direct = FindImageWithBaseName(appDir, assetName, SteamCacheImageExtensions);
                if (!string.IsNullOrEmpty(direct))
                {
                    return direct;
                }

                // Hashed subfolder layout: appcache/librarycache/<appid>/<hash>/<assetName>.jpg
                try
                {
                    string[] subDirs = Directory.GetDirectories(appDir);
                    for (int i = 0; i < subDirs.Length; i++)
                    {
                        string nested = FindImageWithBaseName(subDirs[i], assetName, SteamCacheImageExtensions);
                        if (!string.IsNullOrEmpty(nested))
                        {
                            return nested;
                        }
                    }
                }
                catch
                {
                }
            }

            // Legacy Steam layout: appcache/librarycache/<appid>_<assetName>.jpg
            return FindImageWithBaseName(libraryCacheDir, appIdStr + "_" + assetName, SteamCacheImageExtensions);
        }

        private static string FindImageWithBaseName(string directory, string baseName, string[] extensions)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseName))
            {
                return null;
            }

            for (int i = 0; i < extensions.Length; i++)
            {
                string candidate = Path.Combine(directory, baseName + extensions[i]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static List<NonSteamShortcutEntry> ParseShortcutsVdf(byte[] data, string userId)
        {
            List<NonSteamShortcutEntry> results = new List<NonSteamShortcutEntry>();
            if (data == null || data.Length == 0)
            {
                return results;
            }

            int pos = 0;
            // Root should start with 0x00 "shortcuts" 0x00
            if (!TryReadMapHeader(data, ref pos, out string rootName))
            {
                return results;
            }

            while (pos < data.Length)
            {
                byte type = data[pos++];
                if (type == 0x08)
                {
                    break;
                }

                string entryKey;
                if (!TryReadCString(data, ref pos, out entryKey))
                {
                    break;
                }

                if (type == 0x00)
                {
                    NonSteamShortcutEntry entry = ReadShortcutMap(data, ref pos, userId);
                    if (entry != null)
                    {
                        results.Add(entry);
                    }
                }
                else
                {
                    if (!SkipBinaryVdfValue(type, data, ref pos))
                    {
                        break;
                    }
                }
            }

            return results;
        }

        private static NonSteamShortcutEntry ReadShortcutMap(byte[] data, ref int pos, string userId)
        {
            uint appId = 0;
            bool hasAppId = false;
            string appName = null;
            string rawExe = null;
            string rawStartDir = null;

            while (pos < data.Length)
            {
                byte type = data[pos++];
                if (type == 0x08)
                {
                    break;
                }

                string key;
                if (!TryReadCString(data, ref pos, out key))
                {
                    return null;
                }

                if (type == 0x01)
                {
                    string val;
                    if (!TryReadCString(data, ref pos, out val))
                    {
                        return null;
                    }

                    if (string.Equals(key, "AppName", StringComparison.OrdinalIgnoreCase))
                    {
                        appName = val;
                    }
                    else if (string.Equals(key, "Exe", StringComparison.OrdinalIgnoreCase))
                    {
                        rawExe = val;
                    }
                    else if (string.Equals(key, "StartDir", StringComparison.OrdinalIgnoreCase))
                    {
                        rawStartDir = val;
                    }
                }
                else if (type == 0x02)
                {
                    if (pos + 4 > data.Length)
                    {
                        return null;
                    }
                    int intVal = BitConverter.ToInt32(data, pos);
                    pos += 4;
                    if (string.Equals(key, "appid", StringComparison.OrdinalIgnoreCase))
                    {
                        appId = unchecked((uint)intVal);
                        hasAppId = true;
                    }
                }
                else
                {
                    if (!SkipBinaryVdfValue(type, data, ref pos))
                    {
                        return null;
                    }
                }
            }

            if (!hasAppId || appId == 0)
            {
                appId = ComputeLegacyShortcutAppId(rawExe ?? "", appName ?? "");
            }

            if (appId == 0 && string.IsNullOrWhiteSpace(appName) && string.IsNullOrWhiteSpace(rawExe))
            {
                return null;
            }

            return new NonSteamShortcutEntry
            {
                AppId = appId,
                UserId = userId,
                AppName = appName,
                ExePath = NormalizePath(ExtractExeFromCommand(rawExe)),
                StartDir = NormalizePath(rawStartDir)
            };
        }

        private static bool TryReadMapHeader(byte[] data, ref int pos, out string name)
        {
            name = null;
            if (pos >= data.Length || data[pos] != 0x00)
            {
                return false;
            }
            pos++;
            return TryReadCString(data, ref pos, out name);
        }

        private static bool SkipBinaryVdfValue(byte type, byte[] data, ref int pos)
        {
            switch (type)
            {
                case 0x00: // Nested map
                    while (pos < data.Length)
                    {
                        byte innerType = data[pos++];
                        if (innerType == 0x08)
                        {
                            return true;
                        }
                        string ignored;
                        if (!TryReadCString(data, ref pos, out ignored))
                        {
                            return false;
                        }
                        if (!SkipBinaryVdfValue(innerType, data, ref pos))
                        {
                            return false;
                        }
                    }
                    return false;
                case 0x01: // String
                    string s;
                    return TryReadCString(data, ref pos, out s);
                case 0x02: // Int32
                case 0x03: // Float32
                    if (pos + 4 > data.Length) return false;
                    pos += 4;
                    return true;
                case 0x07: // UInt64
                case 0x0A: // Int64
                    if (pos + 8 > data.Length) return false;
                    pos += 8;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadCString(byte[] data, ref int pos, out string value)
        {
            value = null;
            int start = pos;
            while (pos < data.Length && data[pos] != 0x00)
            {
                pos++;
            }
            if (pos >= data.Length)
            {
                return false;
            }
            value = Encoding.UTF8.GetString(data, start, pos - start);
            pos++;
            return true;
        }

        private static uint ComputeLegacyShortcutAppId(string rawExe, string appName)
        {
            byte[] input = Encoding.UTF8.GetBytes((rawExe ?? "") + (appName ?? ""));
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < input.Length; i++)
            {
                crc ^= input[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1u) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }
            crc ^= 0xFFFFFFFFu;
            return crc | 0x80000000u;
        }

        private static IEnumerable<KeyValuePair<string, string>> EnumerateVdfKeyValues(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield break;
            }

            int pos = 0;
            string pendingKey = null;
            while (pos < text.Length)
            {
                char c = text[pos];
                if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                {
                    while (pos < text.Length && text[pos] != '\n') pos++;
                    continue;
                }
                if (c == '{' || c == '}')
                {
                    pendingKey = null;
                    pos++;
                    continue;
                }
                if (c == '"')
                {
                    string token = ReadQuotedVdfToken(text, ref pos);
                    if (pendingKey == null)
                    {
                        pendingKey = token;
                    }
                    else
                    {
                        yield return new KeyValuePair<string, string>(pendingKey, token);
                        pendingKey = null;
                    }
                    continue;
                }
                pos++;
            }
        }

        private static string ReadQuotedVdfToken(string text, ref int pos)
        {
            pos++; // skip opening quote
            StringBuilder sb = new StringBuilder();
            while (pos < text.Length)
            {
                char c = text[pos++];
                if (c == '"')
                {
                    break;
                }
                if (c == '\\' && pos < text.Length)
                {
                    char next = text[pos++];
                    if (next == 'n') sb.Append('\n');
                    else if (next == 't') sb.Append('\t');
                    else sb.Append(next);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string ExtractExeFromCommand(string rawExe)
        {
            if (string.IsNullOrWhiteSpace(rawExe))
            {
                return null;
            }

            string trimmed = rawExe.Trim();
            if (trimmed.StartsWith("\""))
            {
                int endQuote = trimmed.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    return trimmed.Substring(1, endQuote - 1);
                }
            }
            return trimmed.Trim('"');
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                string cleaned = path.Trim().Trim('"').Replace('/', '\\');
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    return null;
                }
                return Path.GetFullPath(cleaned).TrimEnd('\\');
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSpecificDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                return false;
            }
            string root = Path.GetPathRoot(dir) ?? "";
            string trimmed = dir.TrimEnd('\\');
            return trimmed.Length > root.TrimEnd('\\').Length + 1;
        }

        private static bool IsPathInsideDirectory(string filePath, string dirPath)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(dirPath))
            {
                return false;
            }
            string dirWithSep = dirPath.EndsWith("\\", StringComparison.Ordinal) ? dirPath : dirPath + "\\";
            return filePath.StartsWith(dirWithSep, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "";
            }
            string s = title.Trim();
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - 4);
            }
            return s.Trim();
        }

        private static string ComputeStableKey(string value)
        {
            // FNV-1a 64-bit hex key (compact, URL-safe, no path exposure)
            ulong hash = 14695981039346656037UL;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        private static string GetImageContentType(string filePath)
        {
            string ext = (Path.GetExtension(filePath) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                default: return "application/octet-stream";
            }
        }
    }
}
