using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RoomAgent
{
    public enum RoomMode
    {
        Office,
        LivingRoom
    }

    public class RoomController : IDisposable
    {
        private readonly AgentConfig _config;
        private readonly ISystemActions _actions;
        private readonly IIdleMonitor _idleMonitor;
        private readonly IControllerMonitor _controllerMonitor;
        private readonly IntegrationManager _integrationManager;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _wakeLock = new object();
        private readonly object _pendingLock = new object();

        private string _activeProfileId;
        private string _activeConsoleEnvironmentMode;
        private bool _wasStreamingActive;
        private bool _streamingConsoleOverrideActive;
        private string _preStreamingConsoleEnvironmentMode;
        private CancellationTokenSource _wakeGuardCts;
        private CancellationTokenSource _pendingSwitchCts;
        private string _pendingProfileId;
        private string _pendingConsoleEnvironmentMode;
        private string _pendingReason;

        public int PendingSwitchPollIntervalMs { get; set; } = 1500;

        public RoomController(AgentConfig config, ISystemActions actions, IIdleMonitor idleMonitor, ILogger logger)
            : this(config, actions, idleMonitor, null, null, logger)
        {
        }

        public RoomController(AgentConfig config, ISystemActions actions, IIdleMonitor idleMonitor, IControllerMonitor controllerMonitor, ILogger logger)
            : this(config, actions, idleMonitor, controllerMonitor, null, logger)
        {
        }

        public RoomController(AgentConfig config, ISystemActions actions, IIdleMonitor idleMonitor, IControllerMonitor controllerMonitor, IntegrationManager integrationManager, ILogger logger)
        {
            _config = config;
            _actions = actions;
            _idleMonitor = idleMonitor;
            _controllerMonitor = controllerMonitor;
            _integrationManager = integrationManager ?? IntegrationManager.CreateDefault();
            _logger = logger;
            _config.ApplyDefaultsAndNormalize();
            EnvironmentConfig defaultProfile = _config.GetDefaultProfile();
            _activeProfileId = defaultProfile != null ? defaultProfile.Id : "office";
        }

        public RoomMode CurrentMode
        {
            get
            {
                return string.Equals(_activeProfileId, "living-room", StringComparison.OrdinalIgnoreCase)
                    ? RoomMode.LivingRoom
                    : RoomMode.Office;
            }
        }

        public string ActiveProfileId
        {
            get { return _activeProfileId; }
        }

        public string PendingProfileId
        {
            get { lock (_pendingLock) { return _pendingProfileId; } }
        }

        public string PendingSwitchReason
        {
            get { lock (_pendingLock) { return _pendingReason; } }
        }

        public EnvironmentConfig ActiveProfile
        {
            get { return _config.GetProfile(_activeProfileId) ?? _config.GetDefaultProfile(); }
        }

        public string ActiveConsoleEnvironmentMode
        {
            get { return _activeConsoleEnvironmentMode ?? _config.GetRoomConsoleEnvironmentMode(_activeProfileId); }
        }

        public IList<EnvironmentConfig> GetProfiles()
        {
            return _config.Profiles != null ? _config.Profiles.AsReadOnly() : new List<EnvironmentConfig>().AsReadOnly();
        }

        public IList<IRoomIntegration> GetIntegrations()
        {
            return _integrationManager.Integrations;
        }

        public bool IsProfileMonitorAvailable(EnvironmentConfig profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Monitor))
            {
                return true;
            }
            return _actions.IsMonitorAvailable(profile.Monitor);
        }

        public int GetIdleSeconds()
        {
            return (int)Math.Max(0, _idleMonitor.GetIdleDuration().TotalSeconds);
        }

        public bool IsOfficeMonitorAvailable()
        {
            return IsProfileMonitorAvailable(_config.GetProfile("office") ?? _config.Office);
        }

        public bool IsLivingRoomMonitorAvailable()
        {
            return IsProfileMonitorAvailable(_config.GetProfile("living-room") ?? _config.LivingRoom);
        }

        public StreamingSessionInfo GetStreamingSessionInfo()
        {
            return _actions.GetStreamingSessionInfo() ?? new StreamingSessionInfo { Active = false, DisplayName = "", Reason = "" };
        }

        public async Task StopStreamingAsync(string targetMode = null, string consoleEnvironmentModeOverride = null)
        {
            string normalizedTarget = (targetMode ?? "").Trim();
            if (!string.IsNullOrEmpty(normalizedTarget))
            {
                EnvironmentConfig targetProfile = _config.GetProfile(normalizedTarget);
                if (targetProfile != null)
                {
                    await SwitchToProfileAsync(targetProfile.Id, true, consoleEnvironmentModeOverride, "streaming-exit").ConfigureAwait(false);
                    return;
                }
                if (string.Equals(normalizedTarget, "livingroom", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedTarget, "living-room", StringComparison.OrdinalIgnoreCase))
                {
                    await SwitchToLivingRoomAsync(true, consoleEnvironmentModeOverride).ConfigureAwait(false);
                    return;
                }
                if (string.Equals(normalizedTarget, "office", StringComparison.OrdinalIgnoreCase))
                {
                    await SwitchToOfficeAsync("streaming-exit", consoleEnvironmentModeOverride).ConfigureAwait(false);
                    return;
                }
            }

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopStreamingIfActiveLockedAsync("api-stop-streaming").ConfigureAwait(false);
                _wasStreamingActive = false;
                await RevertStreamingConsoleEnvironmentLockedAsync("api-stop-streaming").ConfigureAwait(false);
                _idleMonitor.Reset();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task StartStreamingEnvironmentAsync(string consoleEnvironmentModeOverride = null, string reason = "api")
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _wasStreamingActive = true;

                string targetMode = !string.IsNullOrWhiteSpace(consoleEnvironmentModeOverride)
                    ? AgentConfig.NormalizeConsoleEnvironmentMode(consoleEnvironmentModeOverride)
                    : AgentConfig.NormalizeStreamingConsoleEnvironmentMode(_config.StreamingConsoleEnvironmentMode);

                if (string.IsNullOrEmpty(targetMode))
                {
                    _logger.Info("GameStream started (reason=" + reason + "); StreamingConsoleEnvironmentMode is inherit/unchanged.");
                    _idleMonitor.Reset();
                    return;
                }

                string currentActive = ActiveConsoleEnvironmentMode;
                if (!_streamingConsoleOverrideActive)
                {
                    _preStreamingConsoleEnvironmentMode = currentActive;
                    _streamingConsoleOverrideActive = true;
                }

                if (string.Equals(currentActive, targetMode, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info("GameStream started (reason=" + reason + "); ConsoleEnvironmentMode is already " + targetMode + ".");
                    _idleMonitor.Reset();
                    return;
                }

                _logger.Info("GameStream started (reason=" + reason + "); switching ConsoleEnvironmentMode: " + currentActive + " -> " + targetMode);
                await SwitchActiveConsoleEnvironmentModeAsync(targetMode, currentActive).ConfigureAwait(false);
                ApplyIntegrationsForConsoleEnvironmentSwitch(currentActive, targetMode);
                _idleMonitor.Reset();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task EndStreamingEnvironmentAsync(string reason = "api")
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _wasStreamingActive = false;
                await RevertStreamingConsoleEnvironmentLockedAsync(reason).ConfigureAwait(false);
                _idleMonitor.Reset();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task CheckStreamingStateAsync()
        {
            if (!_config.StreamingAutoDetectEnabled)
            {
                return;
            }

            bool isStreaming = GetStreamingSessionInfo().Active;
            if (isStreaming == _wasStreamingActive)
            {
                return;
            }

            if (isStreaming)
            {
                await StartStreamingEnvironmentAsync(null, "auto-detect").ConfigureAwait(false);
            }
            else
            {
                await EndStreamingEnvironmentAsync("auto-detect").ConfigureAwait(false);
            }
        }

        private async Task RevertStreamingConsoleEnvironmentLockedAsync(string reason)
        {
            if (!_streamingConsoleOverrideActive)
            {
                return;
            }

            string restoreMode = _preStreamingConsoleEnvironmentMode ?? _config.GetRoomConsoleEnvironmentMode(_activeProfileId);
            _streamingConsoleOverrideActive = false;
            _preStreamingConsoleEnvironmentMode = null;

            if (!_config.StreamingRevertConsoleEnvironmentOnEnd)
            {
                _logger.Info("GameStream ended (reason=" + reason + "); StreamingRevertConsoleEnvironmentOnEnd is disabled, keeping " + ActiveConsoleEnvironmentMode + ".");
                return;
            }

            string currentActive = ActiveConsoleEnvironmentMode;
            if (string.Equals(currentActive, restoreMode, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("GameStream ended (reason=" + reason + "); ConsoleEnvironmentMode already matches " + restoreMode + ".");
                return;
            }

            _logger.Info("GameStream ended (reason=" + reason + "); reverting ConsoleEnvironmentMode: " + currentActive + " -> " + restoreMode);
            await SwitchActiveConsoleEnvironmentModeAsync(restoreMode, currentActive).ConfigureAwait(false);
            ApplyIntegrationsForConsoleEnvironmentSwitch(currentActive, restoreMode);
        }

        public IList<DesktopShortcutInfo> GetDesktopShortcuts()
        {
            return _actions.GetDesktopShortcuts();
        }

        public bool LaunchDesktopShortcut(string pathOrName, out string launchedName)
        {
            return _actions.LaunchDesktopShortcut(pathOrName, out launchedName);
        }

        public IList<OpenWindowInfo> GetOpenWindows()
        {
            return _actions.GetOpenWindows();
        }

        public bool FocusWindow(long hwnd)
        {
            return _actions.FocusWindow(hwnd);
        }

        public bool MaximizeWindow(long hwnd)
        {
            return _actions.MaximizeWindow(hwnd);
        }

        public bool CloseWindow(long hwnd)
        {
            return _actions.CloseWindow(hwnd);
        }

        public bool OpenTaskView()
        {
            return _actions.OpenTaskView();
        }

        public bool? GetOsdEnabled()
        {
            return _actions.GetOsdEnabled();
        }

        public bool TrySetOsdEnabled(bool enabled, out bool currentState)
        {
            return _actions.TrySetOsdEnabled(enabled, out currentState);
        }

        public string GetOsdMode()
        {
            return _actions.GetOsdMode();
        }

        public bool TrySetOsdMode(string mode, out string currentMode, out bool currentEnabled)
        {
            return _actions.TrySetOsdMode(mode, out currentMode, out currentEnabled);
        }

        public int CloseAllOpenWindows()
        {
            IList<OpenWindowInfo> windows = _actions.GetOpenWindows();
            int closed = 0;
            if (windows == null)
            {
                return 0;
            }

            foreach (OpenWindowInfo w in new List<OpenWindowInfo>(windows))
            {
                if (_actions.CloseWindow(w.Hwnd))
                {
                    closed++;
                }
            }
            return closed;
        }

        public async Task SetConsoleEnvironmentModeAsync(string consoleEnvironmentMode)
        {
            if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
            {
                throw new ArgumentException("Console environment mode is required.", "consoleEnvironmentMode");
            }

            string targetMode = AgentConfig.NormalizeConsoleEnvironmentMode(consoleEnvironmentMode);
            CancelPendingSwitch();

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                string currentActive = ActiveConsoleEnvironmentMode;
                if (!string.Equals(_config.GetRoomConsoleEnvironmentMode(_activeProfileId), targetMode, StringComparison.OrdinalIgnoreCase))
                {
                    _config.SetRoomConsoleEnvironmentMode(_activeProfileId, targetMode);
                    SaveConfigSafely();
                }

                if (string.Equals(currentActive, targetMode, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(targetMode, "off", StringComparison.OrdinalIgnoreCase) &&
                        _actions.HasPendingConsoleEnvironmentRevert())
                    {
                        _logger.Info("ConsoleEnvironmentMode is off, but pending console environment state was found; reverting...");
                        ForceRevertConsoleEnvironmentActions();
                        _idleMonitor.Reset();
                        return;
                    }

                    _logger.Info("ConsoleEnvironmentMode is already " + targetMode + " (profile=" + _activeProfileId + ").");
                    _idleMonitor.Reset();
                    return;
                }

                _logger.Info("Switching ConsoleEnvironmentMode while in " + _activeProfileId + " profile: " + currentActive + " -> " + targetMode);
                await SwitchActiveConsoleEnvironmentModeAsync(targetMode, currentActive).ConfigureAwait(false);
                ApplyIntegrationsForConsoleEnvironmentSwitch(currentActive, targetMode);
                _idleMonitor.Reset();
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task SwitchToLivingRoomAsync(bool fromApi, string consoleEnvironmentModeOverride = null)
        {
            return SwitchToProfileAsync("living-room", fromApi, consoleEnvironmentModeOverride, fromApi ? "api" : "controller");
        }

        public Task SwitchToOfficeAsync(string reason, string consoleEnvironmentModeOverride = null)
        {
            EnvironmentConfig officeOrDefault = _config.GetProfile("office") ?? _config.GetDefaultProfile();
            string targetId = officeOrDefault != null ? officeOrDefault.Id : "office";
            return SwitchToProfileAsync(targetId, true, consoleEnvironmentModeOverride, reason);
        }

        public async Task SwitchToProfileAsync(string profileId, bool fromApi, string consoleEnvironmentModeOverride = null, string reason = "api")
        {
            EnvironmentConfig targetProfile = _config.GetProfile(profileId);
            if (targetProfile == null)
            {
                throw new ArgumentException("Unknown profile: " + profileId, "profileId");
            }

            CancelWakeGuard();
            bool isPendingRetry = string.Equals(reason, "pending-ready", StringComparison.OrdinalIgnoreCase);
            if (!isPendingRetry)
            {
                CancelPendingSwitch();
            }

            bool allowPendingWait =
                !string.Equals(reason, "power-suspend", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reason, "session-ending", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reason, "api-sleep", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reason, "api-shutdown", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reason, "idle-timeout", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reason, "wake-guard-timeout", StringComparison.OrdinalIgnoreCase);

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopStreamingIfActiveLockedAsync("switching to " + targetProfile.Name).ConfigureAwait(false);
                _wasStreamingActive = false;
                _streamingConsoleOverrideActive = false;
                _preStreamingConsoleEnvironmentMode = null;
                string targetConsoleMode = ResolveTargetConsoleEnvironmentMode(targetProfile.Id, consoleEnvironmentModeOverride);
                EnvironmentConfig previousProfile = _config.GetProfile(_activeProfileId) ?? _config.GetDefaultProfile();

                if (string.Equals(_activeProfileId, targetProfile.Id, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(consoleEnvironmentModeOverride) &&
                        !string.Equals(ActiveConsoleEnvironmentMode, targetConsoleMode, StringComparison.OrdinalIgnoreCase))
                    {
                        string previousConsoleMode = ActiveConsoleEnvironmentMode;
                        _logger.Info("Switching ConsoleEnvironmentMode while in " + targetProfile.Name + " profile: " + previousConsoleMode + " -> " + targetConsoleMode);
                        await SwitchActiveConsoleEnvironmentModeAsync(targetConsoleMode).ConfigureAwait(false);
                        ApplyIntegrationsForConsoleEnvironmentSwitch(previousConsoleMode, targetConsoleMode);
                        _idleMonitor.Reset();
                        return;
                    }

                    string mismatchDetails;
                    if (_actions.VerifyOutputDevices(targetProfile, true, out mismatchDetails))
                    {
                        _logger.Info("Already in " + targetProfile.Name + " profile (fromApi=" + fromApi + ", reason=" + reason + ").");
                        _idleMonitor.Reset();
                        return;
                    }

                    _logger.Warn("Already in " + targetProfile.Name + " profile state, but output devices do not match (" + mismatchDetails + "); re-applying settings.");
                }

                _logger.Info("Switching to " + targetProfile.Name + " profile (id=" + targetProfile.Id + ", fromApi=" + fromApi + ", reason=" + reason + ", consoleEnvironment=" + targetConsoleMode + ")...");

                if (!IsProfileMonitorAvailable(targetProfile))
                {
                    if (allowPendingWait)
                    {
                        ArmPendingSwitch(targetProfile, targetConsoleMode, "waiting-for-display", fromApi);
                        return;
                    }
                    throw new InvalidOperationException("Target monitor \"" + targetProfile.Monitor + "\" is not available.");
                }

                if (targetProfile.CloseApps != null && targetProfile.CloseApps.Length > 0)
                {
                    _actions.CloseApplications(targetProfile.CloseApps);
                }

                IntegrationContext integrationCtx = new IntegrationContext(_config, _actions, _logger);
                _integrationManager.OnBeforeProfileSwitch(targetProfile, previousProfile, integrationCtx);

                bool isDefaultTarget = IsDefaultProfile(targetProfile.Id);
                if (!isDefaultTarget)
                {
                    string previousConsoleMode = ActiveConsoleEnvironmentMode;
                    if (IsNormalDesktopMode(previousConsoleMode) && !IsNormalDesktopMode(targetConsoleMode))
                    {
                        _actions.SaveWindowPositions();
                    }
                    RevertConsoleEnvironment();
                    await EnterConsoleEnvironmentPreDisplayAsync(targetConsoleMode).ConfigureAwait(false);
                }

                Exception switchError = null;
                try
                {
                    await ApplyDisplayAndAudioAsync(targetProfile).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    switchError = ex;
                }

                if (switchError != null)
                {
                    if (allowPendingWait && IsMonitorActiveInTopology(targetProfile))
                    {
                        _logger.Info(targetProfile.Name + " display is active in topology, but not ready yet (" + switchError.Message + "); waiting for display to come online.");
                        ArmPendingSwitch(targetProfile, targetConsoleMode, "waiting-for-display-ready", fromApi);
                        return;
                    }

                    EnvironmentConfig fallbackProfile = _config.GetDefaultProfile();
                    if (fallbackProfile != null && !string.Equals(fallbackProfile.Id, targetProfile.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warn(targetProfile.Name + " display/audio switch failed (" + switchError.Message + "); reverting to " + fallbackProfile.Name + "...");
                        _activeConsoleEnvironmentMode = targetConsoleMode;
                        await TryRevertToProfileDisplayAsync(fallbackProfile, integrationCtx, targetProfile).ConfigureAwait(false);
                    }
                    throw switchError;
                }

                ClearPendingSwitchIfMatches(targetProfile.Id);

                if (isDefaultTarget)
                {
                    _integrationManager.OnAfterProfileEntered(targetProfile, previousProfile, integrationCtx);
                    await SwitchActiveConsoleEnvironmentModeAsync(targetConsoleMode).ConfigureAwait(false);
                }
                else
                {
                    if (_config.DisplaySettleDelayMs > 0)
                    {
                        await Task.Delay(_config.DisplaySettleDelayMs).ConfigureAwait(false);
                    }
                    EnterConsoleEnvironmentPostDisplay(targetConsoleMode);
                    _activeConsoleEnvironmentMode = targetConsoleMode;
                    _integrationManager.OnAfterProfileEntered(targetProfile, previousProfile, integrationCtx);
                }

                _activeProfileId = targetProfile.Id;
                _idleMonitor.Reset();
                _logger.Info("Switched to " + targetProfile.Name + " profile.");
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task ApplyDisplayAndAudioAsync(EnvironmentConfig env)
        {
            int maxAttempts = 1 + Math.Max(0, _config.VerifyRetryCount);
            string lastDetails = "";

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                _actions.SwitchDisplay(env);

                if (string.IsNullOrWhiteSpace(env.Monitor) && string.IsNullOrWhiteSpace(env.Audio))
                {
                    return;
                }

                bool audioAttempted = false;
                for (int poll = 0; poll < 8; poll++)
                {
                    if (_actions.VerifyOutputDevices(env, true, out lastDetails))
                    {
                        _logger.Info("Verified output devices: " + lastDetails);
                        return;
                    }

                    if (!audioAttempted && !string.IsNullOrWhiteSpace(env.Audio))
                    {
                        string unused;
                        bool monitorReady = string.IsNullOrWhiteSpace(env.Monitor) ||
                            _actions.VerifyOutputDevices(env, false, out unused);
                        if (monitorReady)
                        {
                            string matchedAudio;
                            if (_actions.TrySetDefaultAudioPlaybackDevice(env.Audio, out matchedAudio))
                            {
                                audioAttempted = true;
                                if (_actions.VerifyOutputDevices(env, true, out lastDetails))
                                {
                                    _logger.Info("Verified output devices after setting audio: " + lastDetails);
                                    return;
                                }
                            }
                        }
                    }

                    await Task.Delay(250).ConfigureAwait(false);
                }

                if (attempt < maxAttempts)
                {
                    _logger.Warn("Output device mismatch (" + lastDetails + "); retrying display/audio switch...");
                }
            }

            throw new InvalidOperationException("Display/audio validation failed: " + lastDetails);
        }

        public async Task SleepAsync()
        {
            try
            {
                await SwitchToDefaultProfileAsync("api-sleep").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn("Sleep pre-switch to default profile failed (" + ex.Message + "); proceeding with sleep.");
            }
            _actions.SleepComputer();
        }

        public async Task ShutdownAsync()
        {
            try
            {
                await SwitchToDefaultProfileAsync("api-shutdown").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn("Shutdown pre-switch to default profile failed (" + ex.Message + "); proceeding with shutdown.");
            }
            _actions.ShutdownComputer();
        }

        public void OnPowerSuspend()
        {
            CancelWakeGuard();
            CancelPendingSwitch();
            if (!IsDefaultProfile(_activeProfileId))
            {
                _logger.Info("Power suspend detected while in " + _activeProfileId + " profile; reverting to default profile.");
                try
                {
                    SwitchToDefaultProfileAsync("power-suspend").GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error("Error reverting to default profile on power suspend", ex);
                }
            }
        }

        public void OnSessionEnding(string reason)
        {
            CancelWakeGuard();
            CancelPendingSwitch();
            if (!IsDefaultProfile(_activeProfileId))
            {
                _logger.Info("Session ending (" + (reason ?? "unknown") + ") while in " + _activeProfileId + " profile; reverting to default profile.");
                try
                {
                    SwitchToDefaultProfileAsync("session-ending").GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error("Error reverting to default profile on session ending; forcing console environment revert", ex);
                    ForceRevertConsoleEnvironmentActions();
                }
            }
            else if (_actions.HasPendingConsoleEnvironmentRevert())
            {
                _logger.Info("Session ending (" + (reason ?? "unknown") + ") with pending console environment state; reverting.");
                ForceRevertConsoleEnvironmentActions();
            }
        }

        public async Task ReconcileStartupStateAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                EnvironmentConfig defaultProfile = _config.GetDefaultProfile();
                string defaultConsoleMode = _config.GetRoomConsoleEnvironmentMode(defaultProfile != null ? defaultProfile.Id : "office");
                if (!string.Equals(defaultConsoleMode, "off", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!_actions.HasPendingConsoleEnvironmentRevert())
                {
                    return;
                }

                _logger.Info("Detected leftover console environment state on startup while in default profile; reverting to regular desktop mode...");

                for (int attempt = 1; attempt <= 10; attempt++)
                {
                    ForceRevertConsoleEnvironmentActions();
                    if (!_actions.HasPendingConsoleEnvironmentRevert())
                    {
                        _activeConsoleEnvironmentMode = "off";
                        _logger.Info("Startup console environment state reverted successfully.");
                        return;
                    }

                    if (attempt < 10)
                    {
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                }

                _logger.Warn("Startup console environment revert completed with some pending state remaining.");
            }
            finally
            {
                _gate.Release();
            }
        }

        public void OnPowerResume()
        {
            if (IsDefaultProfile(_activeProfileId) || _config.WakeGuardSeconds <= 0)
            {
                return;
            }

            _logger.Info(string.Format("Power resume detected in {0} profile; arming {1}s wake-guard.", _activeProfileId, _config.WakeGuardSeconds));
            CancellationTokenSource cts = new CancellationTokenSource();
            lock (_wakeLock)
            {
                if (_wakeGuardCts != null)
                {
                    _wakeGuardCts.Cancel();
                    _wakeGuardCts.Dispose();
                }
                _wakeGuardCts = cts;
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.WakeGuardSeconds), cts.Token).ConfigureAwait(false);
                    if (!cts.Token.IsCancellationRequested)
                    {
                        if (GetStreamingSessionInfo().Active)
                        {
                            _logger.Info("Wake-guard expired, but active streaming session is in progress; skipping revert to default profile.");
                            return;
                        }
                        _logger.Info("Wake-guard expired without API call; reverting to default profile.");
                        await SwitchToDefaultProfileAsync("wake-guard-timeout").ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.Error("Wake-guard error", ex);
                }
            });
        }

        public async Task CheckIdleAsync()
        {
            if (IsDefaultProfile(_activeProfileId) || _config.IdleRevertMinutes <= 0)
            {
                return;
            }

            if (GetStreamingSessionInfo().Active)
            {
                return;
            }

            TimeSpan idle = _idleMonitor.GetIdleDuration();
            if (idle >= TimeSpan.FromMinutes(_config.IdleRevertMinutes))
            {
                _logger.Info(string.Format("Idle threshold reached ({0:F1} min); reverting to default profile.", idle.TotalMinutes));
                await SwitchToDefaultProfileAsync("idle-timeout").ConfigureAwait(false);
                if (_config.IdleSleepAfterRevert)
                {
                    _actions.SleepComputer();
                }
            }
        }

        public async Task CheckControllerTriggerAsync()
        {
            if (!_config.XboxReceiverTriggerEnabled || _controllerMonitor == null)
            {
                return;
            }

            IList<string> newlyConnected = _controllerMonitor.PollNewlyConnectedControllers();
            if (newlyConnected == null || newlyConnected.Count == 0)
            {
                return;
            }

            string joined = string.Join(", ", newlyConnected);
            EnvironmentConfig triggerProfile = _config.GetControllerTriggerProfile();
            if (triggerProfile == null)
            {
                return;
            }

            if (GetStreamingSessionInfo().Active)
            {
                _logger.Info("Xbox receiver controller connected (" + joined + "); active streaming session in progress, skipping " + triggerProfile.Name + " switch.");
                _idleMonitor.Reset();
                return;
            }

            if (string.Equals(_activeProfileId, triggerProfile.Id, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("Xbox receiver controller connected (" + joined + "); already in " + triggerProfile.Name + " profile, resetting idle timer.");
                _idleMonitor.Reset();
                return;
            }

            _logger.Info("Xbox receiver controller connected (" + joined + "); switching to " + triggerProfile.Name + " profile.");
            await SwitchToProfileAsync(triggerProfile.Id, false, null, "controller").ConfigureAwait(false);
        }

        public event EventHandler ConfigReloaded;

        public async Task ReloadConfigAsync(AgentConfig updated, string rawJson = null, bool save = true)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _config.UpdateFrom(updated, rawJson);
                if (save)
                {
                    _config.Save();
                }
                if (_config.GetProfile(_activeProfileId) == null)
                {
                    EnvironmentConfig def = _config.GetDefaultProfile();
                    _activeProfileId = def != null ? def.Id : "office";
                }
            }
            finally
            {
                _gate.Release();
            }

            EventHandler handler = ConfigReloaded;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private Task SwitchToDefaultProfileAsync(string reason, string consoleEnvironmentModeOverride = null)
        {
            EnvironmentConfig def = _config.GetDefaultProfile();
            string targetId = def != null ? def.Id : "office";
            return SwitchToProfileAsync(targetId, true, consoleEnvironmentModeOverride, reason);
        }

        private bool IsDefaultProfile(string profileId)
        {
            EnvironmentConfig def = _config.GetDefaultProfile();
            if (def == null)
            {
                return string.Equals(profileId, "office", StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(profileId, def.Id, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> StopStreamingIfActiveLockedAsync(string reason)
        {
            StreamingSessionInfo info = GetStreamingSessionInfo();
            if (!info.Active)
            {
                return false;
            }

            _logger.Info(string.Format(
                "Active streaming session detected (display=\"{0}\", reason={1}); terminating cleanly before {2}...",
                info.DisplayName ?? "", info.Reason ?? "", reason));

            bool stopped = await Task.Run(() => _actions.StopStreamingSession()).ConfigureAwait(false);
            if (!stopped)
            {
                _logger.Warn("Streaming session stop reported incomplete cleanup; continuing with room transition.");
            }
            return stopped;
        }

        private async Task TryRevertToProfileDisplayAsync(EnvironmentConfig fallbackProfile, IntegrationContext integrationCtx, EnvironmentConfig failedProfile)
        {
            try
            {
                await ApplyDisplayAndAudioAsync(fallbackProfile).ConfigureAwait(false);
            }
            catch (Exception revertEx)
            {
                _logger.Error("Failed to revert display/audio back to " + fallbackProfile.Name, revertEx);
            }

            try
            {
                RevertConsoleEnvironment();
                string fallbackConsoleMode = _config.GetRoomConsoleEnvironmentMode(fallbackProfile.Id);
                if (IsNormalDesktopMode(fallbackConsoleMode))
                {
                    _actions.RestoreWindowPositions();
                }
            }
            catch (Exception consoleEx)
            {
                _logger.Error("Failed to revert console environment during " + fallbackProfile.Name + " revert", consoleEx);
            }

            _integrationManager.OnProfileSwitchFailed(failedProfile, fallbackProfile, integrationCtx);
        }

        private static bool IsNormalDesktopMode(string mode)
        {
            return string.IsNullOrEmpty(mode) || string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveTargetConsoleEnvironmentMode(string profileId, string consoleEnvironmentModeOverride)
        {
            if (!string.IsNullOrWhiteSpace(consoleEnvironmentModeOverride))
            {
                return AgentConfig.NormalizeConsoleEnvironmentMode(consoleEnvironmentModeOverride);
            }
            return _config.GetRoomConsoleEnvironmentMode(profileId);
        }

        private async Task SwitchActiveConsoleEnvironmentModeAsync(string targetConsoleMode, string previousConsoleModeOverride = null)
        {
            string previousConsoleMode = previousConsoleModeOverride ?? ActiveConsoleEnvironmentMode;
            if (IsNormalDesktopMode(previousConsoleMode) && !IsNormalDesktopMode(targetConsoleMode))
            {
                _actions.SaveWindowPositions();
            }

            RevertConsoleEnvironment();
            if (IsNormalDesktopMode(targetConsoleMode) && !IsNormalDesktopMode(previousConsoleMode))
            {
                _actions.RestoreWindowPositions();
            }

            await EnterConsoleEnvironmentPreDisplayAsync(targetConsoleMode).ConfigureAwait(false);
            EnterConsoleEnvironmentPostDisplay(targetConsoleMode);
            _activeConsoleEnvironmentMode = targetConsoleMode;
        }

        private void ApplyIntegrationsForConsoleEnvironmentSwitch(string previousConsoleMode, string targetConsoleMode)
        {
            if (!_config.RunIntegrationsOnConsoleEnvironmentSwitch)
            {
                return;
            }

            EnvironmentConfig activeProfile = _config.GetProfile(_activeProfileId) ?? _config.GetDefaultProfile();
            if (activeProfile == null)
            {
                return;
            }

            bool enteringActiveMode = !string.IsNullOrEmpty(targetConsoleMode) &&
                                      !string.Equals(targetConsoleMode, "off", StringComparison.OrdinalIgnoreCase);
            bool wasActiveMode = !string.IsNullOrEmpty(previousConsoleMode) &&
                                 !string.Equals(previousConsoleMode, "off", StringComparison.OrdinalIgnoreCase);

            if (enteringActiveMode)
            {
                IntegrationContext integrationCtx = new IntegrationContext(_config, _actions, _logger);
                for (int i = 0; i < _integrationManager.Integrations.Count; i++)
                {
                    IRoomIntegration integration = _integrationManager.Integrations[i];
                    if (!integration.IsEnabled(_config))
                    {
                        continue;
                    }
                    if (string.Equals(integration.Id, "appLauncher", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            integration.OnAfterProfileEntered(activeProfile, activeProfile, integrationCtx);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("Integration '" + integration.Id + "' failed during console-environment switch", ex);
                        }
                    }
                }

                if (IntegrationConfigHelper.GetGlobalBool(_config, "steam", "enabled", true) &&
                    (IntegrationConfigHelper.GetProfileBool(activeProfile, "steam", "bigPicture", false) ||
                     string.Equals(IntegrationConfigHelper.GetProfileString(activeProfile, "steam", "mode", ""), "big-picture", StringComparison.OrdinalIgnoreCase)))
                {
                    _actions.OpenSteamBigPicture();
                }
            }
            else if (wasActiveMode)
            {
                if (IntegrationConfigHelper.GetGlobalBool(_config, "steam", "enabled", true))
                {
                    string closeMode = IntegrationConfigHelper.GetGlobalString(_config, "steam", "closeMode", _config.SteamCloseMode);
                    _actions.CloseSteamBigPicture(closeMode);
                    _actions.CloseSteamWindow();
                }
            }
        }

        private async Task EnterConsoleEnvironmentPreDisplayAsync(string consoleMode)
        {
            if (string.Equals(consoleMode, "virtual-desktop", StringComparison.OrdinalIgnoreCase))
            {
                _actions.SwitchToFreshVirtualDesktop();
                _actions.HideDesktopIcons();
                await Task.Delay(250).ConfigureAwait(false);
            }
            else if (string.Equals(consoleMode, "minimize-all", StringComparison.OrdinalIgnoreCase))
            {
                _actions.MinimizeAllWindows();
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        private void EnterConsoleEnvironmentPostDisplay(string consoleMode)
        {
            if (string.Equals(consoleMode, "virtual-desktop", StringComparison.OrdinalIgnoreCase))
            {
                _actions.HideTaskbar();
            }
            else if (string.Equals(consoleMode, "xbox", StringComparison.OrdinalIgnoreCase))
            {
                _actions.EnterXboxMode();
            }

            if (!IsNormalDesktopMode(consoleMode))
            {
                _actions.MoveCursorToLowerRightCorner();
            }
        }

        private void RevertConsoleEnvironment()
        {
            string activeMode = _activeConsoleEnvironmentMode;
            _activeConsoleEnvironmentMode = null;

            if (string.IsNullOrEmpty(activeMode) ||
                string.Equals(activeMode, "off", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (string.Equals(activeMode, "xbox", StringComparison.OrdinalIgnoreCase))
            {
                _actions.ExitXboxMode();
            }
            else if (string.Equals(activeMode, "minimize-all", StringComparison.OrdinalIgnoreCase))
            {
                _actions.RestoreMinimizedWindows();
            }
            else
            {
                _actions.ShowTaskbar();
                _actions.ShowDesktopIcons();
                _actions.RevertVirtualDesktop();
            }
        }

        private void ForceRevertConsoleEnvironmentActions()
        {
            _actions.ExitXboxMode();
            _actions.RestoreMinimizedWindows();
            _actions.ShowTaskbar();
            _actions.ShowDesktopIcons();
            _actions.RevertVirtualDesktop();
            _actions.RestoreWindowPositions();
            _activeConsoleEnvironmentMode = null;
        }

        private void SaveConfigSafely()
        {
            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save configuration", ex);
            }
        }

        private bool IsMonitorActiveInTopology(EnvironmentConfig profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Monitor))
            {
                return true;
            }
            string details;
            if (_actions.VerifyOutputDevices(profile, false, out details))
            {
                return true;
            }
            if (string.IsNullOrEmpty(details))
            {
                return false;
            }
            int start = details.IndexOf("monitors=[", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return false;
            }
            start += "monitors=[".Length;
            int end = details.IndexOf("]", start, StringComparison.Ordinal);
            if (end < 0)
            {
                return false;
            }
            string activeMonitors = details.Substring(start, end - start);
            return !string.Equals(activeMonitors, "(none)", StringComparison.OrdinalIgnoreCase) &&
                   activeMonitors.IndexOf(profile.Monitor, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ArmPendingSwitch(EnvironmentConfig targetProfile, string targetConsoleMode, string pendingReason, bool fromApi)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            lock (_pendingLock)
            {
                if (_pendingSwitchCts != null)
                {
                    _pendingSwitchCts.Cancel();
                    _pendingSwitchCts.Dispose();
                }
                _pendingSwitchCts = cts;
                _pendingProfileId = targetProfile.Id;
                _pendingConsoleEnvironmentMode = targetConsoleMode;
                _pendingReason = pendingReason;
            }

            _logger.Info(string.Format("Armed pending switch to {0} (reason={1}, consoleEnvironment={2}); waiting for display to come online.",
                targetProfile.Name, pendingReason, targetConsoleMode ?? ""));

            Task.Run(async () =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        int pollMs = Math.Max(10, PendingSwitchPollIntervalMs);
                        await Task.Delay(pollMs, cts.Token).ConfigureAwait(false);
                        if (cts.Token.IsCancellationRequested)
                        {
                            break;
                        }

                        EnvironmentConfig latestProfile = _config.GetProfile(targetProfile.Id);
                        if (latestProfile == null)
                        {
                            ClearPendingSwitchIfCts(cts);
                            return;
                        }

                        bool ready = false;
                        if (string.Equals(pendingReason, "waiting-for-display", StringComparison.OrdinalIgnoreCase))
                        {
                            ready = IsProfileMonitorAvailable(latestProfile);
                        }
                        else
                        {
                            string unused;
                            if (_actions.VerifyOutputDevices(latestProfile, true, out unused))
                            {
                                ready = true;
                            }
                            else
                            {
                                if (!_actions.VerifyOutputDevices(latestProfile, false, out unused))
                                {
                                    _actions.SwitchDisplay(latestProfile);
                                }
                                string matchedAudio;
                                if (!string.IsNullOrWhiteSpace(latestProfile.Audio))
                                {
                                    _actions.TrySetDefaultAudioPlaybackDevice(latestProfile.Audio, out matchedAudio);
                                }
                                ready = _actions.VerifyOutputDevices(latestProfile, true, out unused);
                            }
                        }

                        if (!ready)
                        {
                            continue;
                        }

                        _logger.Info("Target display for " + latestProfile.Name + " is now ready; executing pending switch...");
                        await SwitchToProfileAsync(latestProfile.Id, fromApi, targetConsoleMode, "pending-ready").ConfigureAwait(false);

                        string stillPending;
                        lock (_pendingLock)
                        {
                            stillPending = _pendingSwitchCts == cts ? _pendingProfileId : null;
                        }
                        if (string.IsNullOrEmpty(stillPending))
                        {
                            return;
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.Error("Pending switch watcher error for " + targetProfile.Id, ex);
                    ClearPendingSwitchIfCts(cts);
                }
            });
        }

        private void ClearPendingSwitchIfCts(CancellationTokenSource cts)
        {
            lock (_pendingLock)
            {
                if (_pendingSwitchCts == cts)
                {
                    _pendingSwitchCts.Dispose();
                    _pendingSwitchCts = null;
                    _pendingProfileId = null;
                    _pendingConsoleEnvironmentMode = null;
                    _pendingReason = null;
                }
            }
        }

        private void ClearPendingSwitchIfMatches(string profileId)
        {
            lock (_pendingLock)
            {
                if (!string.IsNullOrEmpty(_pendingProfileId) &&
                    string.Equals(_pendingProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    if (_pendingSwitchCts != null)
                    {
                        _pendingSwitchCts.Cancel();
                        _pendingSwitchCts.Dispose();
                        _pendingSwitchCts = null;
                    }
                    _pendingProfileId = null;
                    _pendingConsoleEnvironmentMode = null;
                    _pendingReason = null;
                }
            }
        }

        public void CancelPendingSwitch()
        {
            bool hadPending = false;
            string prevProfile = null;
            lock (_pendingLock)
            {
                if (_pendingSwitchCts != null || !string.IsNullOrEmpty(_pendingProfileId))
                {
                    hadPending = true;
                    prevProfile = _pendingProfileId;
                }
                if (_pendingSwitchCts != null)
                {
                    _pendingSwitchCts.Cancel();
                    _pendingSwitchCts.Dispose();
                    _pendingSwitchCts = null;
                }
                _pendingProfileId = null;
                _pendingConsoleEnvironmentMode = null;
                _pendingReason = null;
            }
            if (hadPending)
            {
                _logger.Info("Cancelled pending switch to " + (prevProfile ?? "unknown") + ".");
            }
        }

        private void CancelWakeGuard()
        {
            lock (_wakeLock)
            {
                if (_wakeGuardCts != null)
                {
                    _wakeGuardCts.Cancel();
                    _wakeGuardCts.Dispose();
                    _wakeGuardCts = null;
                }
            }
        }

        public void Dispose()
        {
            CancelWakeGuard();
            CancelPendingSwitch();
            _gate.Dispose();
        }
    }
}
