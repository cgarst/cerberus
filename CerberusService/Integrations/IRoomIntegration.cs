using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoomAgent
{
    public class IntegrationContext
    {
        public AgentConfig Config { get; private set; }
        public ISystemActions Actions { get; private set; }
        public ILogger Logger { get; private set; }

        public IntegrationContext(AgentConfig config, ISystemActions actions, ILogger logger)
        {
            Config = config;
            Actions = actions;
            Logger = logger;
        }
    }

    public interface IRoomIntegration
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsEnabled(AgentConfig config);
        void OnBeforeProfileSwitch(EnvironmentConfig targetProfile, EnvironmentConfig previousProfile, IntegrationContext context);
        void OnAfterProfileEntered(EnvironmentConfig activeProfile, EnvironmentConfig previousProfile, IntegrationContext context);
        void OnProfileSwitchFailed(EnvironmentConfig targetProfile, EnvironmentConfig fallbackProfile, IntegrationContext context);
    }

    public static class IntegrationConfigHelper
    {
        public static bool GetGlobalBool(AgentConfig config, string integrationId, string key, bool defaultValue)
        {
            if (config == null || config.Integrations == null)
            {
                return defaultValue;
            }
            return GetDictBool(config.Integrations, integrationId, key, defaultValue);
        }

        public static string GetGlobalString(AgentConfig config, string integrationId, string key, string defaultValue)
        {
            if (config == null || config.Integrations == null)
            {
                return defaultValue;
            }
            return GetDictString(config.Integrations, integrationId, key, defaultValue);
        }

        public static bool GetProfileBool(EnvironmentConfig profile, string integrationId, string key, bool defaultValue)
        {
            if (profile == null || profile.Integrations == null)
            {
                return defaultValue;
            }
            return GetDictBool(profile.Integrations, integrationId, key, defaultValue);
        }

        public static bool HasProfileSetting(EnvironmentConfig profile, string integrationId, string key)
        {
            if (profile == null || profile.Integrations == null)
            {
                return false;
            }
            Dictionary<string, object> settings;
            if (!TryGetCaseInsensitive(profile.Integrations, integrationId, out settings) || settings == null)
            {
                return false;
            }
            object unused;
            return TryGetCaseInsensitiveValue(settings, key, out unused);
        }

        public static string GetProfileString(EnvironmentConfig profile, string integrationId, string key, string defaultValue)
        {
            if (profile == null || profile.Integrations == null)
            {
                return defaultValue;
            }
            return GetDictString(profile.Integrations, integrationId, key, defaultValue);
        }

        public static string[] GetProfileStringArray(EnvironmentConfig profile, string integrationId, string key)
        {
            if (profile == null || profile.Integrations == null)
            {
                return new string[0];
            }
            Dictionary<string, object> settings;
            if (!TryGetCaseInsensitive(profile.Integrations, integrationId, out settings) || settings == null)
            {
                return new string[0];
            }
            object raw;
            if (!TryGetCaseInsensitiveValue(settings, key, out raw) || raw == null)
            {
                return new string[0];
            }
            if (raw is JsonElement jeArr)
            {
                if (jeArr.ValueKind == JsonValueKind.Array)
                {
                    List<string> list = new List<string>();
                    foreach (JsonElement item in jeArr.EnumerateArray())
                    {
                        string s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            list.Add(s.Trim());
                        }
                    }
                    return list.ToArray();
                }
                return new string[0];
            }
            string[] asArray = raw as string[];
            if (asArray != null)
            {
                return asArray;
            }
            System.Collections.IEnumerable seq = raw as System.Collections.IEnumerable;
            if (seq != null && !(raw is string))
            {
                List<string> list = new List<string>();
                foreach (object item in seq)
                {
                    if (item != null)
                    {
                        string s = item.ToString().Trim();
                        if (!string.IsNullOrEmpty(s))
                        {
                            list.Add(s);
                        }
                    }
                }
                return list.ToArray();
            }
            return new string[0];
        }

        private static bool GetDictBool(Dictionary<string, Dictionary<string, object>> root, string section, string key, bool defaultValue)
        {
            Dictionary<string, object> settings;
            if (!TryGetCaseInsensitive(root, section, out settings) || settings == null)
            {
                return defaultValue;
            }
            object raw;
            if (!TryGetCaseInsensitiveValue(settings, key, out raw) || raw == null)
            {
                return defaultValue;
            }
            if (raw is JsonElement jeBool)
            {
                if (jeBool.ValueKind == JsonValueKind.True) return true;
                if (jeBool.ValueKind == JsonValueKind.False) return false;
                if (jeBool.ValueKind == JsonValueKind.String && bool.TryParse(jeBool.GetString(), out bool jb)) return jb;
                return defaultValue;
            }
            if (raw is bool)
            {
                return (bool)raw;
            }
            bool parsed;
            if (bool.TryParse(raw.ToString(), out parsed))
            {
                return parsed;
            }
            return defaultValue;
        }

        private static string GetDictString(Dictionary<string, Dictionary<string, object>> root, string section, string key, string defaultValue)
        {
            Dictionary<string, object> settings;
            if (!TryGetCaseInsensitive(root, section, out settings) || settings == null)
            {
                return defaultValue;
            }
            object raw;
            if (!TryGetCaseInsensitiveValue(settings, key, out raw) || raw == null)
            {
                return defaultValue;
            }
            string s = raw is JsonElement jeStr
                ? (jeStr.ValueKind == JsonValueKind.String ? jeStr.GetString() : jeStr.ToString())
                : raw.ToString();
            return string.IsNullOrWhiteSpace(s) ? defaultValue : s;
        }

        private static bool TryGetCaseInsensitive(Dictionary<string, Dictionary<string, object>> dict, string key, out Dictionary<string, object> value)
        {
            if (dict.TryGetValue(key, out value))
            {
                return true;
            }
            foreach (KeyValuePair<string, Dictionary<string, object>> kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        private static bool TryGetCaseInsensitiveValue(Dictionary<string, object> dict, string key, out object value)
        {
            if (dict.TryGetValue(key, out value))
            {
                return true;
            }
            foreach (KeyValuePair<string, object> kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }
    }

    public class IntegrationManager
    {
        private readonly List<IRoomIntegration> _integrations = new List<IRoomIntegration>();

        public IntegrationManager(IEnumerable<IRoomIntegration> integrations)
        {
            if (integrations != null)
            {
                foreach (IRoomIntegration integration in integrations)
                {
                    if (integration != null)
                    {
                        _integrations.Add(integration);
                    }
                }
            }
        }

        public static IntegrationManager CreateDefault()
        {
            return new IntegrationManager(new IRoomIntegration[]
            {
                new SteamIntegration(),
                new HueSyncIntegration(),
                new AppLauncherIntegration()
            });
        }

        public IList<IRoomIntegration> Integrations
        {
            get { return _integrations.AsReadOnly(); }
        }

        public void OnBeforeProfileSwitch(EnvironmentConfig targetProfile, EnvironmentConfig previousProfile, IntegrationContext context)
        {
            for (int i = 0; i < _integrations.Count; i++)
            {
                IRoomIntegration integration = _integrations[i];
                if (!integration.IsEnabled(context.Config))
                {
                    continue;
                }
                try
                {
                    integration.OnBeforeProfileSwitch(targetProfile, previousProfile, context);
                }
                catch (Exception ex)
                {
                    context.Logger.Error("Integration '" + integration.Id + "' failed in OnBeforeProfileSwitch", ex);
                }
            }
        }

        public void OnAfterProfileEntered(EnvironmentConfig activeProfile, EnvironmentConfig previousProfile, IntegrationContext context)
        {
            for (int i = 0; i < _integrations.Count; i++)
            {
                IRoomIntegration integration = _integrations[i];
                if (!integration.IsEnabled(context.Config))
                {
                    continue;
                }
                try
                {
                    integration.OnAfterProfileEntered(activeProfile, previousProfile, context);
                }
                catch (Exception ex)
                {
                    context.Logger.Error("Integration '" + integration.Id + "' failed in OnAfterProfileEntered", ex);
                }
            }
        }

        public void OnProfileSwitchFailed(EnvironmentConfig targetProfile, EnvironmentConfig fallbackProfile, IntegrationContext context)
        {
            for (int i = 0; i < _integrations.Count; i++)
            {
                IRoomIntegration integration = _integrations[i];
                if (!integration.IsEnabled(context.Config))
                {
                    continue;
                }
                try
                {
                    integration.OnProfileSwitchFailed(targetProfile, fallbackProfile, context);
                }
                catch (Exception ex)
                {
                    context.Logger.Error("Integration '" + integration.Id + "' failed in OnProfileSwitchFailed", ex);
                }
            }
        }
    }
}
