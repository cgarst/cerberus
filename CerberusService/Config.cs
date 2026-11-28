using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoomAgent
{
    public class EnvironmentConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public string Monitor { get; set; }
        public string Audio { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
        public bool? Hdr { get; set; }
        public bool HueSync { get; set; }
        public string ConsoleEnvironmentMode { get; set; }
        public string[] CloseApps { get; set; }
        public Dictionary<string, Dictionary<string, object>> Integrations { get; set; }

        public EnvironmentConfig()
        {
            Id = "";
            Name = "";
            IsDefault = false;
            Monitor = "";
            Audio = "";
            Hdr = true;
            HueSync = false;
            CloseApps = new string[0];
            Integrations = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public class AgentConfig
    {
        public const string DisabledControllerTriggerId = "disabled";

        public int Port { get; set; }
        public string BearerToken { get; set; }
        public string AuthPasswordHash { get; set; }
        public string AuthPasswordSalt { get; set; }
        public string AuthTokenSecret { get; set; }
        public List<EnvironmentConfig> Profiles { get; set; }
        public EnvironmentConfig Office { get; set; }
        public EnvironmentConfig LivingRoom { get; set; }
        public string ControllerTriggerProfileId { get; set; }
        public Dictionary<string, Dictionary<string, object>> Integrations { get; set; }
        public int VerifyRetryCount { get; set; }
        public string SteamPath { get; set; }
        public string SteamCloseMode { get; set; }
        public string ShortcutsDirectory { get; set; }
        public int DisplaySettleDelayMs { get; set; }
        public int WakeGuardSeconds { get; set; }
        public int IdleRevertMinutes { get; set; }
        public bool IdleSleepAfterRevert { get; set; }
        public bool ConsoleEnvironmentOnLivingRoom { get; set; }
        public string ConsoleEnvironmentMode { get; set; }
        public string StreamingConsoleEnvironmentMode { get; set; }
        public bool StreamingRevertConsoleEnvironmentOnEnd { get; set; }
        public bool StreamingAutoDetectEnabled { get; set; }
        public bool RunIntegrationsOnConsoleEnvironmentSwitch { get; set; }
        public bool LivingRoomDesktopColorEnabled { get; set; }
        public string LivingRoomDesktopColor { get; set; }
        public bool XboxReceiverTriggerEnabled { get; set; }
        public string[] XboxReceiverDevicePrefixes { get; set; }
        public int ControllerPollIntervalMs { get; set; }
        public string HueSyncPath { get; set; }
        public string[] StreamingVirtualDisplayPatterns { get; set; }
        public string[] StreamingServiceNames { get; set; }
        public string[] StreamingProcessNames { get; set; }
        public string LogDirectory { get; set; }
        public string UpdateChannel { get; set; }
        public bool CheckForUpdatesOnStartup { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string ConfigPath { get; set; }
        private List<EnvironmentConfig> _initialProfiles;

        public AgentConfig()
        {
            Port = 8090;
            BearerToken = "";
            AuthPasswordHash = "";
            AuthPasswordSalt = "";
            AuthTokenSecret = "";
            Office = new EnvironmentConfig
            {
                Id = "office",
                Name = "Office",
                IsDefault = true,
                Monitor = "Odyssey G80SD",
                Audio = "Sound Blaster Play! 3",
                Width = 3840,
                Height = 2160,
                RefreshRate = 240,
                Hdr = true,
                ConsoleEnvironmentMode = "off"
            };
            LivingRoom = new EnvironmentConfig
            {
                Id = "living-room",
                Name = "Living Room",
                IsDefault = false,
                Monitor = "LG TV SSCR2",
                Audio = "LG TV SSCR2",
                Width = 3840,
                Height = 2160,
                RefreshRate = 120,
                Hdr = true,
                ConsoleEnvironmentMode = ""
            };
            Profiles = new List<EnvironmentConfig> { Office, LivingRoom };
            _initialProfiles = Profiles;
            ControllerTriggerProfileId = "living-room";
            Integrations = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            VerifyRetryCount = 1;
            SteamPath = @"C:\Program Files (x86)\Steam\steam.exe";
            SteamCloseMode = "exit-bigpicture";
            ShortcutsDirectory = "";
            DisplaySettleDelayMs = 1500;
            WakeGuardSeconds = 15;
            IdleRevertMinutes = 45;
            IdleSleepAfterRevert = false;
            ConsoleEnvironmentOnLivingRoom = true;
            ConsoleEnvironmentMode = "virtual-desktop";
            StreamingConsoleEnvironmentMode = "";
            StreamingRevertConsoleEnvironmentOnEnd = true;
            StreamingAutoDetectEnabled = true;
            RunIntegrationsOnConsoleEnvironmentSwitch = false;
            LivingRoomDesktopColorEnabled = true;
            LivingRoomDesktopColor = "#000000";
            XboxReceiverTriggerEnabled = true;
            XboxReceiverDevicePrefixes = new[]
            {
                @"USB\VID_045E&PID_02FE",
                @"USB\VID_045E&PID_02E6"
            };
            ControllerPollIntervalMs = 1000;
            HueSyncPath = @"C:\Program Files\Hue Sync\HueSync.exe";
            StreamingVirtualDisplayPatterns = new[]
            {
                "Sunshine",
                "SudoVDA",
                "SudoMaker",
                "SDD4000",
                "Virtual Display",
                "Apollo"
            };
            StreamingServiceNames = new[]
            {
                "ApolloService",
                "SunshineService",
                "Apollo",
                "Sunshine"
            };
            StreamingProcessNames = new[]
            {
                "sunshine",
                "apollo"
            };
            LogDirectory = "logs";
            UpdateChannel = "auto";
            CheckForUpdatesOnStartup = true;
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions PrettyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static AgentConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Configuration file not found: " + path);
            }

            string json = File.ReadAllText(path);
            AgentConfig loaded = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions);
            if (loaded == null)
            {
                throw new InvalidOperationException("Failed to parse configuration file: " + path);
            }

            loaded.ApplyDefaultsAndNormalize(json);
            loaded.ConfigPath = path;
            return loaded;
        }

        public void ApplyDefaultsAndNormalize(string rawJson = null)
        {
            AgentConfig defaults = new AgentConfig();
            if (Port <= 0 || Port > 65535) Port = defaults.Port;
            if (string.IsNullOrWhiteSpace(SteamPath)) SteamPath = defaults.SteamPath;
            if (string.IsNullOrWhiteSpace(SteamCloseMode)) SteamCloseMode = defaults.SteamCloseMode;
            ShortcutsDirectory = ShortcutsDirectory == null ? "" : ShortcutsDirectory.Trim();
            if (DisplaySettleDelayMs < 0) DisplaySettleDelayMs = defaults.DisplaySettleDelayMs;
            if (WakeGuardSeconds < 0) WakeGuardSeconds = defaults.WakeGuardSeconds;
            if (IdleRevertMinutes < 0) IdleRevertMinutes = defaults.IdleRevertMinutes;
            if (string.IsNullOrWhiteSpace(LogDirectory)) LogDirectory = defaults.LogDirectory;
            UpdateChannel = NormalizeUpdateChannel(UpdateChannel);
            if (!string.IsNullOrEmpty(rawJson) && rawJson.IndexOf("\"CheckForUpdatesOnStartup\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                CheckForUpdatesOnStartup = defaults.CheckForUpdatesOnStartup;
            }
            if (BearerToken == null) BearerToken = "";
            if (AuthPasswordHash == null) AuthPasswordHash = "";
            if (AuthPasswordSalt == null) AuthPasswordSalt = "";
            if (AuthTokenSecret == null) AuthTokenSecret = "";
            if (VerifyRetryCount < 0) VerifyRetryCount = defaults.VerifyRetryCount;
            if (string.IsNullOrWhiteSpace(ConsoleEnvironmentMode)) ConsoleEnvironmentMode = defaults.ConsoleEnvironmentMode;
            StreamingConsoleEnvironmentMode = NormalizeStreamingConsoleEnvironmentMode(StreamingConsoleEnvironmentMode);
            if (!string.IsNullOrEmpty(rawJson) && rawJson.IndexOf("\"StreamingRevertConsoleEnvironmentOnEnd\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                StreamingRevertConsoleEnvironmentOnEnd = defaults.StreamingRevertConsoleEnvironmentOnEnd;
            }
            if (!string.IsNullOrEmpty(rawJson) && rawJson.IndexOf("\"StreamingAutoDetectEnabled\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                StreamingAutoDetectEnabled = defaults.StreamingAutoDetectEnabled;
            }
            if (XboxReceiverDevicePrefixes == null || XboxReceiverDevicePrefixes.Length == 0)
            {
                XboxReceiverDevicePrefixes = defaults.XboxReceiverDevicePrefixes;
            }
            if (ControllerPollIntervalMs <= 0) ControllerPollIntervalMs = defaults.ControllerPollIntervalMs;
            if (string.IsNullOrWhiteSpace(HueSyncPath)) HueSyncPath = defaults.HueSyncPath;
            if (StreamingVirtualDisplayPatterns == null || StreamingVirtualDisplayPatterns.Length == 0)
            {
                StreamingVirtualDisplayPatterns = defaults.StreamingVirtualDisplayPatterns;
            }
            if (StreamingServiceNames == null || StreamingServiceNames.Length == 0)
            {
                StreamingServiceNames = defaults.StreamingServiceNames;
            }
            if (StreamingProcessNames == null || StreamingProcessNames.Length == 0)
            {
                StreamingProcessNames = defaults.StreamingProcessNames;
            }
            if (Integrations == null)
            {
                Integrations = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            }

            bool hasProfilesInJson = !string.IsNullOrEmpty(rawJson) &&
                rawJson.IndexOf("\"Profiles\"", StringComparison.OrdinalIgnoreCase) >= 0;
            bool legacyJsonWithoutProfiles = !string.IsNullOrEmpty(rawJson) && !hasProfilesInJson && Office != null && LivingRoom != null;
            bool legacyPropsReplacedInMemory = string.IsNullOrEmpty(rawJson) &&
                object.ReferenceEquals(Profiles, _initialProfiles) &&
                Profiles != null && Profiles.Count == 2 &&
                ((Office != null && !object.ReferenceEquals(Office, Profiles[0])) ||
                 (LivingRoom != null && !object.ReferenceEquals(LivingRoom, Profiles[1])));

            if ((Profiles == null || Profiles.Count == 0) || legacyJsonWithoutProfiles || legacyPropsReplacedInMemory)
            {
                // Migrate legacy Office/LivingRoom structure into Profiles
                if (Office == null) Office = defaults.Office;
                if (LivingRoom == null) LivingRoom = defaults.LivingRoom;
                Office.Id = string.IsNullOrWhiteSpace(Office.Id) ? "office" : NormalizeProfileId(Office.Id);
                Office.Name = string.IsNullOrWhiteSpace(Office.Name) ? "Office" : Office.Name;
                LivingRoom.Id = string.IsNullOrWhiteSpace(LivingRoom.Id) ? "living-room" : NormalizeProfileId(LivingRoom.Id);
                LivingRoom.Name = string.IsNullOrWhiteSpace(LivingRoom.Name) ? "Living Room" : LivingRoom.Name;
                if (!Office.IsDefault && !LivingRoom.IsDefault)
                {
                    Office.IsDefault = true;
                }
                Profiles = new List<EnvironmentConfig> { Office, LivingRoom };
            _initialProfiles = Profiles;
            }

            bool anyDefault = false;
            HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Profiles.Count; i++)
            {
                EnvironmentConfig p = Profiles[i];
                if (p == null) continue;
                if (string.IsNullOrWhiteSpace(p.Name))
                {
                    p.Name = string.IsNullOrWhiteSpace(p.Id) ? ("Profile " + (i + 1)) : p.Id;
                }
                string candidateId = string.IsNullOrWhiteSpace(p.Id) ? NormalizeProfileId(p.Name) : NormalizeProfileId(p.Id);
                if (string.IsNullOrWhiteSpace(candidateId))
                {
                    candidateId = "profile-" + (i + 1);
                }
                while (seenIds.Contains(candidateId))
                {
                    candidateId = candidateId + "-" + (i + 1);
                }
                p.Id = candidateId;
                seenIds.Add(candidateId);

                if (p.Monitor == null) p.Monitor = "";
                if (p.Audio == null) p.Audio = "";
                if (p.CloseApps == null) p.CloseApps = new string[0];
                if (p.Integrations == null) p.Integrations = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(p.ConsoleEnvironmentMode))
                {
                    if (string.Equals(p.Id, "living-room", StringComparison.OrdinalIgnoreCase))
                    {
                        p.ConsoleEnvironmentMode = ConsoleEnvironmentOnLivingRoom ? NormalizeConsoleEnvironmentMode(ConsoleEnvironmentMode) : "off";
                    }
                    else
                    {
                        p.ConsoleEnvironmentMode = "off";
                    }
                }
                else
                {
                    p.ConsoleEnvironmentMode = NormalizeConsoleEnvironmentMode(p.ConsoleEnvironmentMode);
                }

                if (p.IsDefault)
                {
                    if (anyDefault)
                    {
                        p.IsDefault = false;
                    }
                    else
                    {
                        anyDefault = true;
                    }
                }
            }

            if (!anyDefault && Profiles.Count > 0)
            {
                EnvironmentConfig officeMatch = GetProfile("office");
                if (officeMatch != null)
                {
                    officeMatch.IsDefault = true;
                }
                else
                {
                    Profiles[0].IsDefault = true;
                }
            }

            if (!XboxReceiverTriggerEnabled)
            {
                ControllerTriggerProfileId = DisabledControllerTriggerId;
            }
            else if (string.IsNullOrWhiteSpace(ControllerTriggerProfileId))
            {
                EnvironmentConfig lr = GetProfile("living-room");
                ControllerTriggerProfileId = lr != null ? lr.Id : (Profiles.Count > 1 ? Profiles[1].Id : (Profiles.Count > 0 ? Profiles[0].Id : "living-room"));
            }
            else if (IsDisabledControllerTriggerValue(ControllerTriggerProfileId))
            {
                ControllerTriggerProfileId = DisabledControllerTriggerId;
                XboxReceiverTriggerEnabled = false;
            }

            // Keep legacy Office/LivingRoom properties synced for backward compatibility
            Office = GetProfile("office") ?? GetDefaultProfile() ?? defaults.Office;
            LivingRoom = GetProfile("living-room") ?? (Profiles.Count > 1 ? Profiles[1] : Office);
        }

        public static string NormalizeProfileId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            bool lastDash = false;
            string lower = value.Trim().ToLowerInvariant();
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastDash = false;
                }
                else if (!lastDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
            return sb.ToString().Trim('-');
        }

        public EnvironmentConfig GetProfile(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName) || Profiles == null)
            {
                return null;
            }
            string trimmed = idOrName.Trim();
            for (int i = 0; i < Profiles.Count; i++)
            {
                EnvironmentConfig p = Profiles[i];
                if (p != null && string.Equals(p.Id, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            for (int i = 0; i < Profiles.Count; i++)
            {
                EnvironmentConfig p = Profiles[i];
                if (p != null && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            string normalized = NormalizeProfileId(trimmed);
            for (int i = 0; i < Profiles.Count; i++)
            {
                EnvironmentConfig p = Profiles[i];
                if (p != null && string.Equals(p.Id, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            return null;
        }

        public EnvironmentConfig GetDefaultProfile()
        {
            if (Profiles != null)
            {
                for (int i = 0; i < Profiles.Count; i++)
                {
                    if (Profiles[i] != null && Profiles[i].IsDefault)
                    {
                        return Profiles[i];
                    }
                }
                if (Profiles.Count > 0)
                {
                    return Profiles[0];
                }
            }
            return Office;
        }

        public static bool IsDisabledControllerTriggerValue(string value)
        {
            return string.Equals(value, DisabledControllerTriggerId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
        }

        public EnvironmentConfig GetControllerTriggerProfile()
        {
            if (!XboxReceiverTriggerEnabled || IsDisabledControllerTriggerValue(ControllerTriggerProfileId))
            {
                return null;
            }
            return GetProfile(ControllerTriggerProfileId) ?? GetProfile("living-room");
        }

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(ConfigPath))
            {
                return;
            }
            Save(ConfigPath);
        }

        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            JsonObject root = JsonSerializer.SerializeToNode(this, PrettyJsonOptions) as JsonObject ?? new JsonObject();

            if (File.Exists(path))
            {
                string existingJson = File.ReadAllText(path);
                JsonObject existingRoot = JsonNode.Parse(existingJson) as JsonObject;
                if (existingRoot != null)
                {
                    foreach (KeyValuePair<string, JsonNode> kvp in existingRoot)
                    {
                        if (!root.ContainsKey(kvp.Key))
                        {
                            root[kvp.Key] = kvp.Value != null ? kvp.Value.DeepClone() : null;
                        }
                    }
                }
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string formatted = root.ToJsonString(PrettyJsonOptions) + Environment.NewLine;
            File.WriteAllText(path, formatted);
        }

        public void UpdateFrom(AgentConfig updated, string rawJson = null)
        {
            if (updated == null)
            {
                throw new ArgumentNullException("updated");
            }

            updated.ApplyDefaultsAndNormalize(rawJson);

            Port = updated.Port;
            if (!string.IsNullOrEmpty(updated.BearerToken))
            {
                BearerToken = updated.BearerToken;
            }
            AuthPasswordHash = updated.AuthPasswordHash ?? "";
            AuthPasswordSalt = updated.AuthPasswordSalt ?? "";
            AuthTokenSecret = updated.AuthTokenSecret ?? "";
            Profiles = updated.Profiles;
            Office = updated.Office;
            LivingRoom = updated.LivingRoom;
            ControllerTriggerProfileId = updated.ControllerTriggerProfileId;
            Integrations = updated.Integrations;
            VerifyRetryCount = updated.VerifyRetryCount;
            SteamPath = updated.SteamPath;
            SteamCloseMode = updated.SteamCloseMode;
            ShortcutsDirectory = updated.ShortcutsDirectory ?? "";
            DisplaySettleDelayMs = updated.DisplaySettleDelayMs;
            WakeGuardSeconds = updated.WakeGuardSeconds;
            IdleRevertMinutes = updated.IdleRevertMinutes;
            IdleSleepAfterRevert = updated.IdleSleepAfterRevert;
            ConsoleEnvironmentOnLivingRoom = updated.ConsoleEnvironmentOnLivingRoom;
            ConsoleEnvironmentMode = updated.ConsoleEnvironmentMode;
            StreamingConsoleEnvironmentMode = updated.StreamingConsoleEnvironmentMode;
            StreamingRevertConsoleEnvironmentOnEnd = updated.StreamingRevertConsoleEnvironmentOnEnd;
            StreamingAutoDetectEnabled = updated.StreamingAutoDetectEnabled;
            RunIntegrationsOnConsoleEnvironmentSwitch = updated.RunIntegrationsOnConsoleEnvironmentSwitch;
            LivingRoomDesktopColorEnabled = updated.LivingRoomDesktopColorEnabled;
            LivingRoomDesktopColor = updated.LivingRoomDesktopColor;
            XboxReceiverTriggerEnabled = updated.XboxReceiverTriggerEnabled;
            XboxReceiverDevicePrefixes = updated.XboxReceiverDevicePrefixes;
            ControllerPollIntervalMs = updated.ControllerPollIntervalMs;
            HueSyncPath = updated.HueSyncPath;
            StreamingVirtualDisplayPatterns = updated.StreamingVirtualDisplayPatterns;
            StreamingServiceNames = updated.StreamingServiceNames;
            StreamingProcessNames = updated.StreamingProcessNames;
            LogDirectory = updated.LogDirectory;
        }

        private static Dictionary<string, object> GetOrCreateChildDict(Dictionary<string, object> parent, string key)
        {
            object existing;
            if (parent.TryGetValue(key, out existing))
            {
                Dictionary<string, object> dict = existing as Dictionary<string, object>;
                if (dict != null)
                {
                    return dict;
                }
            }

            Dictionary<string, object> created = new Dictionary<string, object>();
            parent[key] = created;
            return created;
        }

        public bool IsXboxConsoleEnvironmentMode()
        {
            return string.Equals(NormalizeConsoleEnvironmentMode(ConsoleEnvironmentMode), "xbox", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsMinimizeAllConsoleEnvironmentMode()
        {
            return string.Equals(NormalizeConsoleEnvironmentMode(ConsoleEnvironmentMode), "minimize-all", StringComparison.OrdinalIgnoreCase);
        }

        public string GetNormalizedConsoleEnvironmentMode()
        {
            if (!ConsoleEnvironmentOnLivingRoom)
            {
                return "off";
            }
            return NormalizeConsoleEnvironmentMode(ConsoleEnvironmentMode);
        }

        public void SetRoomConsoleEnvironmentMode(string profileId, string mode)
        {
            string normalized = NormalizeConsoleEnvironmentMode(mode);
            EnvironmentConfig profile = GetProfile(profileId);
            if (profile != null)
            {
                profile.ConsoleEnvironmentMode = normalized;
            }
            if (string.Equals(profileId, "office", StringComparison.OrdinalIgnoreCase))
            {
                if (Office == null) Office = new EnvironmentConfig();
                Office.ConsoleEnvironmentMode = normalized;
            }
            else if (string.Equals(profileId, "living-room", StringComparison.OrdinalIgnoreCase))
            {
                if (LivingRoom == null) LivingRoom = new EnvironmentConfig();
                LivingRoom.ConsoleEnvironmentMode = normalized;
                ConsoleEnvironmentMode = normalized;
            }
        }

        public string GetRoomConsoleEnvironmentMode(string profileId)
        {
            EnvironmentConfig profile = GetProfile(profileId);
            if (profile != null && !string.IsNullOrWhiteSpace(profile.ConsoleEnvironmentMode))
            {
                return NormalizeConsoleEnvironmentMode(profile.ConsoleEnvironmentMode);
            }
            if (string.Equals(profileId, "living-room", StringComparison.OrdinalIgnoreCase))
            {
                return GetRoomConsoleEnvironmentMode(RoomMode.LivingRoom);
            }
            return "off";
        }

        public void SetRoomConsoleEnvironmentMode(RoomMode roomMode, string mode)
        {
            SetRoomConsoleEnvironmentMode(roomMode == RoomMode.Office ? "office" : "living-room", mode);
        }

        public string GetRoomConsoleEnvironmentMode(RoomMode roomMode)
        {
            if (roomMode == RoomMode.Office)
            {
                if (Office != null && !string.IsNullOrWhiteSpace(Office.ConsoleEnvironmentMode))
                {
                    return NormalizeConsoleEnvironmentMode(Office.ConsoleEnvironmentMode);
                }
                return "off";
            }

            if (LivingRoom != null && !string.IsNullOrWhiteSpace(LivingRoom.ConsoleEnvironmentMode))
            {
                return NormalizeConsoleEnvironmentMode(LivingRoom.ConsoleEnvironmentMode);
            }
            return GetNormalizedConsoleEnvironmentMode();
        }

        public static string NormalizeStreamingConsoleEnvironmentMode(string mode)
        {
            string trimmed = (mode ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed) ||
                string.Equals(trimmed, "inherit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "unchanged", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return NormalizeConsoleEnvironmentMode(trimmed);
        }

        public static string NormalizeUpdateChannel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "auto";
            switch (value.Trim().ToLowerInvariant())
            {
                case "stable":
                case "beta":
                case "alpha":
                case "off":
                    return value.Trim().ToLowerInvariant();
                default:
                    return "auto";
            }
        }

        public static string NormalizeConsoleEnvironmentMode(string mode)
        {
            string trimmed = (mode ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed) ||
                string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                return "off";
            }
            if (string.Equals(trimmed, "xbox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "xbox-mode", StringComparison.OrdinalIgnoreCase))
            {
                return "xbox";
            }
            if (string.Equals(trimmed, "minimize-all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "minimize", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "minimize_all", StringComparison.OrdinalIgnoreCase))
            {
                return "minimize-all";
            }
            return "virtual-desktop";
        }
    }
}
