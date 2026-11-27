using System;

namespace RoomAgent
{
    public class HueSyncIntegration : IRoomIntegration
    {
        public string Id { get { return "hueSync"; } }
        public string DisplayName { get { return "Philips Hue Sync"; } }

        public bool IsEnabled(AgentConfig config)
        {
            return IntegrationConfigHelper.GetGlobalBool(config, Id, "enabled", true);
        }

        public void OnBeforeProfileSwitch(EnvironmentConfig targetProfile, EnvironmentConfig previousProfile, IntegrationContext context)
        {
            if (!WantsHueSync(targetProfile) && AnyProfileUsesHueSync(context.Config))
            {
                context.Actions.StopHueSync();
            }
        }

        public void OnAfterProfileEntered(EnvironmentConfig activeProfile, EnvironmentConfig previousProfile, IntegrationContext context)
        {
            if (WantsHueSync(activeProfile))
            {
                context.Actions.StartHueSync();
            }
        }

        public void OnProfileSwitchFailed(EnvironmentConfig targetProfile, EnvironmentConfig fallbackProfile, IntegrationContext context)
        {
            if (WantsHueSync(fallbackProfile))
            {
                context.Actions.StartHueSync();
            }
        }

        private bool WantsHueSync(EnvironmentConfig profile)
        {
            if (profile == null)
            {
                return false;
            }
            if (IntegrationConfigHelper.HasProfileSetting(profile, Id, "enabled"))
            {
                return IntegrationConfigHelper.GetProfileBool(profile, Id, "enabled", false);
            }
            return profile.HueSync;
        }

        private bool AnyProfileUsesHueSync(AgentConfig config)
        {
            if (config == null)
            {
                return false;
            }
            if (config.Profiles != null)
            {
                for (int i = 0; i < config.Profiles.Count; i++)
                {
                    if (WantsHueSync(config.Profiles[i]))
                    {
                        return true;
                    }
                }
            }
            return (config.Office != null && config.Office.HueSync) ||
                   (config.LivingRoom != null && config.LivingRoom.HueSync);
        }
    }
}
