using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace RoomAgent
{
    public class LaunchAppEntry
    {
        public string Path { get; set; }
        public bool CloseWindowWhenReady { get; set; }
        public string WindowTitle { get; set; }

        public override string ToString()
        {
            if (CloseWindowWhenReady)
            {
                return string.IsNullOrWhiteSpace(WindowTitle)
                    ? (Path ?? "") + "  [close window]"
                    : (Path ?? "") + "  [close window: " + WindowTitle + "]";
            }
            return Path ?? "";
        }
    }

    public class AppLauncherIntegration : IRoomIntegration
    {
        public string Id { get { return "appLauncher"; } }
        public string DisplayName { get { return "App Launcher"; } }

        public bool IsEnabled(AgentConfig config)
        {
            return IntegrationConfigHelper.GetGlobalBool(config, Id, "enabled", true);
        }

        public void OnBeforeProfileSwitch(EnvironmentConfig targetProfile, EnvironmentConfig previousProfile, IntegrationContext context)
        {
            // App closing is handled via profile.CloseApps.
        }

        public void OnAfterProfileEntered(EnvironmentConfig activeProfile, EnvironmentConfig previousProfile, IntegrationContext context)
        {
            if (activeProfile == null)
            {
                return;
            }

            LaunchAppEntry[] apps = GetLaunchEntries(activeProfile);
            if (apps.Length == 0)
            {
                return;
            }

            for (int i = 0; i < apps.Length; i++)
            {
                LaunchAppEntry entry = apps[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
                {
                    continue;
                }

                string target = entry.Path.Trim();
                try
                {
                    if (IsExecutableAlreadyRunning(target))
                    {
                        context.Logger.Info("AppLauncher: '" + target + "' is already running; skipping launch.");
                        continue;
                    }

                    context.Actions.LaunchApp(target);
                    context.Logger.Info("AppLauncher: started '" + target + "'.");

                    if (entry.CloseWindowWhenReady && !target.Contains("://"))
                    {
                        string processName = Path.GetFileNameWithoutExtension(target);
                        if (!string.IsNullOrWhiteSpace(processName))
                        {
                            context.Actions.CloseWindowWhenReady(processName, entry.WindowTitle);
                        }
                    }
                }
                catch (Exception ex)
                {
                    context.Logger.Warn("AppLauncher: failed to launch '" + target + "': " + ex.Message);
                }
            }
        }

        public void OnProfileSwitchFailed(EnvironmentConfig targetProfile, EnvironmentConfig fallbackProfile, IntegrationContext context)
        {
        }

        public static LaunchAppEntry[] GetLaunchEntries(EnvironmentConfig profile)
        {
            if (profile == null || profile.Integrations == null)
            {
                return new LaunchAppEntry[0];
            }

            Dictionary<string, object> settings;
            if (!TryGetCaseInsensitive(profile.Integrations, "appLauncher", out settings) || settings == null)
            {
                return new LaunchAppEntry[0];
            }

            object raw;
            if (!TryGetCaseInsensitiveValue(settings, "launch", out raw) || raw == null)
            {
                return new LaunchAppEntry[0];
            }

            List<LaunchAppEntry> list = new List<LaunchAppEntry>();
            if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in je.EnumerateArray())
                {
                    LaunchAppEntry parsed = ParseJsonElement(item);
                    if (parsed != null)
                    {
                        list.Add(parsed);
                    }
                }
                return list.ToArray();
            }

            if (raw is LaunchAppEntry[] typedArr)
            {
                return typedArr;
            }

            if (raw is System.Collections.IEnumerable seq && !(raw is string))
            {
                foreach (object item in seq)
                {
                    LaunchAppEntry parsed = ParseObject(item);
                    if (parsed != null)
                    {
                        list.Add(parsed);
                    }
                }
            }

            return list.ToArray();
        }

        public static object[] SerializeLaunchEntries(IEnumerable<LaunchAppEntry> entries)
        {
            List<object> result = new List<object>();
            if (entries == null)
            {
                return result.ToArray();
            }

            foreach (LaunchAppEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
                {
                    continue;
                }

                string path = entry.Path.Trim();
                if (!entry.CloseWindowWhenReady && string.IsNullOrWhiteSpace(entry.WindowTitle))
                {
                    result.Add(path);
                }
                else
                {
                    Dictionary<string, object> obj = new Dictionary<string, object>
                    {
                        { "path", path },
                        { "closeWindowWhenReady", entry.CloseWindowWhenReady }
                    };
                    if (!string.IsNullOrWhiteSpace(entry.WindowTitle))
                    {
                        obj["windowTitle"] = entry.WindowTitle.Trim();
                    }
                    result.Add(obj);
                }
            }

            return result.ToArray();
        }

        private static LaunchAppEntry ParseJsonElement(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string s = (item.GetString() ?? "").Trim();
                return string.IsNullOrEmpty(s) ? null : new LaunchAppEntry { Path = s };
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                string path = null;
                bool closeWhenReady = false;
                string windowTitle = null;

                foreach (JsonProperty prop in item.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "path", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        path = (prop.Value.GetString() ?? "").Trim();
                    }
                    else if (string.Equals(prop.Name, "closeWindowWhenReady", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.True) closeWhenReady = true;
                        else if (prop.Value.ValueKind == JsonValueKind.False) closeWhenReady = false;
                        else if (prop.Value.ValueKind == JsonValueKind.String && bool.TryParse(prop.Value.GetString(), out bool b)) closeWhenReady = b;
                    }
                    else if (string.Equals(prop.Name, "windowTitle", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        windowTitle = (prop.Value.GetString() ?? "").Trim();
                    }
                }

                if (string.IsNullOrEmpty(path))
                {
                    return null;
                }

                return new LaunchAppEntry
                {
                    Path = path,
                    CloseWindowWhenReady = closeWhenReady,
                    WindowTitle = windowTitle
                };
            }

            return null;
        }

        private static LaunchAppEntry ParseObject(object item)
        {
            if (item == null)
            {
                return null;
            }
            if (item is LaunchAppEntry entry)
            {
                return string.IsNullOrWhiteSpace(entry.Path) ? null : entry;
            }
            if (item is JsonElement je)
            {
                return ParseJsonElement(je);
            }
            if (item is IDictionary<string, object> dict)
            {
                string path = null;
                bool closeWhenReady = false;
                string windowTitle = null;
                foreach (KeyValuePair<string, object> kvp in dict)
                {
                    if (string.Equals(kvp.Key, "path", StringComparison.OrdinalIgnoreCase) && kvp.Value != null)
                    {
                        path = kvp.Value.ToString().Trim();
                    }
                    else if (string.Equals(kvp.Key, "closeWindowWhenReady", StringComparison.OrdinalIgnoreCase) && kvp.Value != null)
                    {
                        bool.TryParse(kvp.Value.ToString(), out closeWhenReady);
                    }
                    else if (string.Equals(kvp.Key, "windowTitle", StringComparison.OrdinalIgnoreCase) && kvp.Value != null)
                    {
                        windowTitle = kvp.Value.ToString().Trim();
                    }
                }
                return string.IsNullOrEmpty(path) ? null : new LaunchAppEntry { Path = path, CloseWindowWhenReady = closeWhenReady, WindowTitle = windowTitle };
            }
            string s = item.ToString().Trim();
            return string.IsNullOrEmpty(s) ? null : new LaunchAppEntry { Path = s };
        }

        private static bool TryGetCaseInsensitive(Dictionary<string, Dictionary<string, object>> dict, string key, out Dictionary<string, object> value)
        {
            if (dict.TryGetValue(key, out value)) return true;
            foreach (KeyValuePair<string, Dictionary<string, object>> kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)) { value = kvp.Value; return true; }
            }
            value = null;
            return false;
        }

        private static bool TryGetCaseInsensitiveValue(Dictionary<string, object> dict, string key, out object value)
        {
            if (dict.TryGetValue(key, out value)) return true;
            foreach (KeyValuePair<string, object> kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)) { value = kvp.Value; return true; }
            }
            value = null;
            return false;
        }

        private static bool IsExecutableAlreadyRunning(string target)
        {
            if (target.Contains("://"))
            {
                return false;
            }
            string fileName = Path.GetFileNameWithoutExtension(target);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }
            Process[] procs = Process.GetProcessesByName(fileName);
            bool running = procs != null && procs.Length > 0;
            if (procs != null)
            {
                for (int i = 0; i < procs.Length; i++)
                {
                    procs[i].Dispose();
                }
            }
            return running;
        }
    }
}
