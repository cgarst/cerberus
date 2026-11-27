using System;

namespace RoomAgent
{
    public class SteamIntegration : IRoomIntegration
    {
        public string Id { get { return "steam"; } }
        public string DisplayName { get { return "Steam Big Picture"; } }

        public bool IsEnabled(AgentConfig config)
        {
            return IntegrationConfigHelper.GetGlobalBool(config, Id, "enabled", true);
        }

        public void OnBeforeProfileSwitch(EnvironmentConfig targetProfile, EnvironmentConfig previousProfile, IntegrationContext context)
        {
            bool targetWantsBigPicture = WantsBigPicture(targetProfile);
            if (!targetWantsBigPicture && ShouldCloseOnEnter(targetProfile, previousProfile))
            {
                string closeMode = IntegrationConfigHelper.GetGlobalString(context.Config, Id, "closeMode", context.Config.SteamCloseMode);
                context.Actions.CloseSteamBigPicture(closeMode);
            }
        }

        public void OnAfterProfileEntered(EnvironmentConfig activeProfile, EnvironmentConfig previousProfile, IntegrationContext context)
        {
            if (WantsBigPicture(activeProfile))
            {
                context.Actions.OpenSteamBigPicture();
            }
            else if (ShouldCloseOnEnter(activeProfile, previousProfile))
            {
                context.Actions.CloseSteamWindow();
            }
        }

        public void OnProfileSwitchFailed(EnvironmentConfig targetProfile, EnvironmentConfig fallbackProfile, IntegrationContext context)
        {
            // No-op: Steam Big Picture is only launched after display switch succeeds.
        }

        private bool WantsBigPicture(EnvironmentConfig profile)
        {
            if (profile == null)
            {
                return false;
            }
            if (IntegrationConfigHelper.HasProfileSetting(profile, Id, "bigPicture"))
            {
                return IntegrationConfigHelper.GetProfileBool(profile, Id, "bigPicture", false);
            }
            string mode = IntegrationConfigHelper.GetProfileString(profile, Id, "mode", "");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return string.Equals(mode, "big-picture", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private bool ShouldCloseOnEnter(EnvironmentConfig targetProfile, EnvironmentConfig previousProfile)
        {
            if (targetProfile == null)
            {
                return false;
            }
            if (IntegrationConfigHelper.HasProfileSetting(targetProfile, Id, "closeBigPicture"))
            {
                return IntegrationConfigHelper.GetProfileBool(targetProfile, Id, "closeBigPicture", false);
            }
            if (WantsBigPicture(previousProfile))
            {
                return true;
            }
            return targetProfile.IsDefault || string.Equals(targetProfile.Id, "office", StringComparison.OrdinalIgnoreCase);
        }
    }
}
