using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RoomAgent.Tests
{
    internal class FakeLogger : ILogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception ex = null) { }
    }

    internal class FakeIdleMonitor : IIdleMonitor
    {
        public TimeSpan CurrentIdle { get; set; }
        public int ResetCount { get; private set; }

        public void Sample() { }
        public TimeSpan GetIdleDuration() { return CurrentIdle; }
        public void Reset() { ResetCount++; CurrentIdle = TimeSpan.Zero; }
    }

    internal class FakeControllerMonitor : IControllerMonitor
    {
        public readonly List<string> NextNewlyConnected = new List<string>();

        public IList<string> PollNewlyConnectedControllers()
        {
            List<string> result = new List<string>(NextNewlyConnected);
            NextNewlyConnected.Clear();
            return result;
        }
    }

    internal class FakeSystemActions : ISystemActions
    {
        public readonly List<string> Calls = new List<string>();
        public bool VerifyResult = true;
        public bool? VerifyResultAfterSwitch = null;
        public bool VerifyMonitorOnlyResult = true;
        public bool SetAudioResult = false;
        public bool MonitorAvailableResult = true;
        public bool PendingConsoleEnvironmentRevert = false;

        public void SwitchToFreshVirtualDesktop() { Calls.Add("vdesktop:new"); }
        public void RevertVirtualDesktop() { Calls.Add("vdesktop:revert"); PendingConsoleEnvironmentRevert = false; }
        public void HideTaskbar() { Calls.Add("taskbar:hide"); }
        public void ShowTaskbar() { Calls.Add("taskbar:show"); }
        public void HideDesktopIcons() { Calls.Add("icons:hide"); }
        public void ShowDesktopIcons() { Calls.Add("icons:show"); }
        public void EnterXboxMode() { Calls.Add("xbox:enter"); }
        public void ExitXboxMode() { Calls.Add("xbox:exit"); }
        public void MoveCursorToLowerRightCorner() { Calls.Add("cursor:lower-right"); }
        public bool HasPendingConsoleEnvironmentRevert() { return PendingConsoleEnvironmentRevert; }
        public void MinimizeAllWindows() { Calls.Add("minimize-all"); }
        public void RestoreMinimizedWindows() { Calls.Add("minimize-all:restore"); }
        public void SaveWindowPositions() { Calls.Add("windows:save"); }
        public void RestoreWindowPositions() { Calls.Add("windows:restore"); }
        public readonly List<DesktopShortcutInfo> Shortcuts = new List<DesktopShortcutInfo>();
        public readonly List<OpenWindowInfo> OpenWindows = new List<OpenWindowInfo>();
        public IList<DesktopShortcutInfo> GetDesktopShortcuts() { return Shortcuts; }
        public bool LaunchDesktopShortcut(string pathOrName, out string launchedName)
        {
            Calls.Add("shortcut:launch:" + pathOrName);
            launchedName = pathOrName;
            return Shortcuts.Exists(s => s.Path == pathOrName || s.Name == pathOrName);
        }
        public IList<OpenWindowInfo> GetOpenWindows() { return OpenWindows; }
        public bool FocusWindow(long hwnd)
        {
            Calls.Add("window:focus:" + hwnd);
            return OpenWindows.Exists(w => w.Hwnd == hwnd);
        }
        public bool MaximizeWindow(long hwnd)
        {
            Calls.Add("window:maximize:" + hwnd);
            return OpenWindows.Exists(w => w.Hwnd == hwnd);
        }
        public bool CloseWindow(long hwnd)
        {
            Calls.Add("window:close:" + hwnd);
            return OpenWindows.RemoveAll(w => w.Hwnd == hwnd) > 0;
        }
        public bool OpenTaskView()
        {
            Calls.Add("task-view");
            return true;
        }
        public bool OsdEnabled = true;
        public string OsdMode = "afterburner";
        public bool? GetOsdEnabled() { return OsdEnabled; }
        public bool TrySetOsdEnabled(bool enabled, out bool currentState)
        {
            Calls.Add("osd:" + (enabled ? "on" : "off"));
            OsdEnabled = enabled;
            OsdMode = enabled ? "afterburner" : "off";
            currentState = OsdEnabled;
            return true;
        }
        public string GetOsdMode() { return OsdMode; }
        public bool TrySetOsdMode(string mode, out string currentMode, out bool currentEnabled)
        {
            Calls.Add("osd-mode:" + mode);
            OsdMode = mode;
            OsdEnabled = mode != "off";
            currentMode = OsdMode;
            currentEnabled = OsdEnabled;
            return true;
        }
        public bool IsMonitorAvailable(string expectedMonitor) { return MonitorAvailableResult; }
        public void SwitchDisplay(EnvironmentConfig env)
        {
            Calls.Add(string.Format("display:{0}@{1}x{2}:{3}:hdr={4}", env.Monitor, env.Width, env.Height, env.RefreshRate, env.Hdr));
            if (VerifyResultAfterSwitch.HasValue)
            {
                VerifyResult = VerifyResultAfterSwitch.Value;
            }
        }
        public bool VerifyOutputDevices(EnvironmentConfig env, bool checkAudio, out string details)
        {
            Calls.Add("verify:" + env.Monitor + "|" + (checkAudio ? env.Audio : ""));
            if (!checkAudio)
            {
                details = VerifyMonitorOnlyResult ? "ok" : "monitor-mismatch";
                return VerifyMonitorOnlyResult;
            }
            details = VerifyResult ? "ok" : "mismatch";
            return VerifyResult;
        }
        public bool TrySetDefaultAudioPlaybackDevice(string expectedAudio, out string matchedDevice)
        {
            Calls.Add("audio:set:" + expectedAudio);
            matchedDevice = SetAudioResult ? expectedAudio : "";
            if (SetAudioResult)
            {
                VerifyResult = true;
            }
            return SetAudioResult;
        }
        public void OpenSteamBigPicture() { Calls.Add("steam:open"); }
        public void CloseSteamBigPicture(string closeMode) { Calls.Add("steam:close:" + closeMode); }
        public void CloseSteamWindow() { Calls.Add("steam:close-window"); }
        public void CloseApplications(string[] appNames) { Calls.Add("close-apps:" + string.Join(",", appNames)); }
        public void LaunchApp(string target) { Calls.Add("app:launch:" + target); }
        public void CloseWindowWhenReady(string processName, string exactTitle = null) { Calls.Add("close-window-when-ready:" + processName + (string.IsNullOrEmpty(exactTitle) ? "" : ":" + exactTitle)); }
        public void StartHueSync() { Calls.Add("huesync:start"); }
        public void StopHueSync() { Calls.Add("huesync:stop"); }
        public bool StreamingActive = false;
        public string StreamingDisplay = "Sunshine Dis";
        public string StreamingReason = "virtual-display";
        public StreamingSessionInfo GetStreamingSessionInfo()
        {
            return new StreamingSessionInfo
            {
                Active = StreamingActive,
                DisplayName = StreamingActive ? StreamingDisplay : "",
                Reason = StreamingActive ? StreamingReason : ""
            };
        }
        public bool StopStreamingSession()
        {
            Calls.Add("streaming:stop");
            StreamingActive = false;
            return true;
        }
        public void SleepComputer() { Calls.Add("sleep"); }
        public void ShutdownComputer() { Calls.Add("shutdown"); }
    }

    internal static class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;

        private static int Main()
        {
            Run("LivingRoomTransitionOrder", TestLivingRoomTransitionOrder);
            Run("OfficeTransitionOrder", TestOfficeTransitionOrder);
            Run("WakeGuardRevertsWithoutApi", TestWakeGuardRevertsWithoutApi);
            Run("WakeGuardCancelledByApi", TestWakeGuardCancelledByApi);
            Run("PreSleepRevertsToOffice", TestPreSleepRevertsToOffice);
            Run("ShutdownRevertsToOffice", TestShutdownRevertsToOffice);
            Run("ShutdownProceedsWhenOfficeMonitorUnavailable", TestShutdownProceedsWhenOfficeMonitorUnavailable);
            Run("IdleTimeoutRevertsToOffice", TestIdleTimeoutRevertsToOffice);
            Run("ValidationRetriesAndFailsOnMismatch", TestValidationRetriesAndFailsOnMismatch);
            Run("AudioFallbackFixesAudioMismatch", TestAudioFallbackFixesAudioMismatch);
            Run("PreFlightMonitorCheckBlocksUnavailableTarget", TestPreFlightMonitorCheckBlocksUnavailableTarget);
            Run("XboxReceiverMonitorEdgeTriggers", TestXboxReceiverMonitorEdgeTriggers);
            Run("ControllerTriggerSwitchesToLivingRoom", TestControllerTriggerSwitchesToLivingRoom);
            Run("ControllerTriggerRespectsDisabledFlag", TestControllerTriggerRespectsDisabledFlag);
            Run("ControllerTriggerRespectsDisabledProfileId", TestControllerTriggerRespectsDisabledProfileId);
            Run("LivingRoomClosesConfiguredApps", TestLivingRoomClosesConfiguredApps);
            Run("OfficeClosesConfiguredApps", TestOfficeClosesConfiguredApps);
            Run("UnavailableMonitorDoesNotCloseApps", TestUnavailableMonitorDoesNotCloseApps);
            Run("ConfigLoadDefaultsAndReadsCloseApps", TestConfigLoadDefaultsAndReadsCloseApps);
            Run("HueSyncDisabledByDefaultDoesNotToggle", TestHueSyncDisabledByDefaultDoesNotToggle);
            Run("HueSyncEnabledStopsInLivingRoomAndStartsInOffice", TestHueSyncEnabledStopsInLivingRoomAndStartsInOffice);
            Run("ConfigLoadDefaultsAndReadsHueSync", TestConfigLoadDefaultsAndReadsHueSync);
            Run("LivingRoomValidationFailureRevertsToOfficeDisplay", TestLivingRoomValidationFailureRevertsToOfficeDisplay);
            Run("OfficeModeReappliesWhenOutputDevicesMismatch", TestOfficeModeReappliesWhenOutputDevicesMismatch);
            Run("ConsoleEnvironmentDisabledSkipsDesktopActions", TestConsoleEnvironmentDisabledSkipsDesktopActions);
            Run("XboxConsoleEnvironmentModeTriggersAndRevertsXboxMode", TestXboxConsoleEnvironmentModeTriggersAndRevertsXboxMode);
            Run("XboxConsoleEnvironmentModeDisabledSkipsXboxActions", TestXboxConsoleEnvironmentModeDisabledSkipsXboxActions);
            Run("ConfigLoadDefaultsAndReadsConsoleEnvironmentMode", TestConfigLoadDefaultsAndReadsConsoleEnvironmentMode);
            Run("MinimizeAllConsoleEnvironmentModeMinimizesAndSkipsRevert", TestMinimizeAllConsoleEnvironmentModeMinimizesAndSkipsRevert);
            Run("SwitchConsoleEnvironmentModeWhileInLivingRoom", TestSwitchConsoleEnvironmentModeWhileInLivingRoom);
            Run("SwitchConsoleEnvironmentModeWhileInOffice", TestSwitchConsoleEnvironmentModeWhileInOffice);
            Run("PerRoomConsoleEnvironmentModeConfigDefaultsAndTransitions", TestPerRoomConsoleEnvironmentModeConfigDefaultsAndTransitions);
            Run("ShortcutsAndWindowsControllerActions", TestShortcutsAndWindowsControllerActions);
            Run("BuiltInShortcutsAlwaysPresent", TestBuiltInShortcutsAlwaysPresent);
            Run("ReconcileStartupStateRevertsLeftoverConsoleEnvironmentInOfficeMode", TestReconcileStartupStateRevertsLeftoverConsoleEnvironmentInOfficeMode);
            Run("ReconcileStartupStateSkipsWhenNoPendingState", TestReconcileStartupStateSkipsWhenNoPendingState);
            Run("SetConsoleEnvironmentModeOffRevertsPendingStateEvenWhenAlreadyOff", TestSetConsoleEnvironmentModeOffRevertsPendingStateEvenWhenAlreadyOff);
            Run("OnSessionEndingRevertsToOfficeAndForcesConsoleEnvironmentRevertOnError", TestOnSessionEndingRevertsToOfficeAndForcesConsoleEnvironmentRevertOnError);
            Run("WindowsSystemActionsLoadsPersistedConsoleEnvironmentState", TestWindowsSystemActionsLoadsPersistedConsoleEnvironmentState);
            Run("WindowsSystemActionsRecordsAndRecoversWallpaperHistory", TestWindowsSystemActionsRecordsAndRecoversWallpaperHistory);
            Run("SetConsoleEnvironmentModePersistsToConfigAndIsRemembered", TestSetConsoleEnvironmentModePersistsToConfigAndIsRemembered);
            Run("StopStreamingAndSwitchToOffice", TestStopStreamingAndSwitchToOffice);
            Run("StopStreamingAndSwitchToLivingRoomWhenUnderlyingWasLivingRoom", TestStopStreamingAndSwitchToLivingRoomWhenUnderlyingWasLivingRoom);
            Run("IdleTimeoutAndControllerTriggerSkippedWhileStreamingActive", TestIdleTimeoutAndControllerTriggerSkippedWhileStreamingActive);
            Run("CustomThirdProfileAndDefaultRevert", TestCustomThirdProfileAndDefaultRevert);
            Run("ModularIntegrationCanBeDisabledGlobally", TestModularIntegrationCanBeDisabledGlobally);
            Run("ProfilesConfigSaveAndReloadRoundTrip", TestProfilesConfigSaveAndReloadRoundTrip);
            Run("CloakedWindowFilteringForOpenWindows", TestCloakedWindowFilteringForOpenWindows);
            Run("RunIntegrationsOnConsoleEnvironmentSwitch", TestRunIntegrationsOnConsoleEnvironmentSwitch);
            Run("DefaultPortAndConfigReloadEvent", TestDefaultPortAndConfigReloadEvent);
            Run("NetworkAddressHelperFormatting", TestNetworkAddressHelperFormatting);
            Run("AppDiscoveryNormalizationMergeAndFilter", TestAppDiscoveryNormalizationMergeAndFilter);
            Run("ShortcutIconOmitsOverlay", TestShortcutIconOmitsOverlay);
            Run("WindowPositionsSavedAndRestoredAcrossConsoleEnvironmentModes", TestWindowPositionsSavedAndRestoredAcrossConsoleEnvironmentModes);
            Run("WindowsSystemActionsPersistsSavedWindowPlacements", TestWindowsSystemActionsPersistsSavedWindowPlacements);
            Run("AppLauncherMixedEntriesAndSerialization", TestAppLauncherMixedEntriesAndSerialization);
            Run("SteamHeroResolverSteamAndNonSteam", TestSteamHeroResolverSteamAndNonSteam);
            Run("AuthServicePasswordAndSignedTokens", TestAuthServicePasswordAndSignedTokens);
            Run("StreamingConsoleEnvironmentAutoDetectStartAndEnd", TestStreamingConsoleEnvironmentAutoDetectStartAndEnd);
            Run("StreamingConsoleEnvironmentApiStartOverrideAndNoDoubleTrigger", TestStreamingConsoleEnvironmentApiStartOverrideAndNoDoubleTrigger);
            Run("StreamingConsoleEnvironmentRevertDisabledKeepsMode", TestStreamingConsoleEnvironmentRevertDisabledKeepsMode);
            Run("StreamingConsoleEnvironmentInheritAndStopStreamingRevert", TestStreamingConsoleEnvironmentInheritAndStopStreamingRevert);
            Run("UpdateServiceVersioningAndManifest", TestUpdateServiceVersioningAndManifest);
            Run("ConsoleEnvironmentMovesCursorToLowerRightCorner", TestConsoleEnvironmentMovesCursorToLowerRightCorner);
            Run("RemoteInputProcessorParsesAndClampsPayloads", TestRemoteInputProcessorParsesAndClampsPayloads);
            Run("OsdModeMutuallyExclusiveTransitions", TestOsdModeMutuallyExclusiveTransitions);
            Run("PendingSwitchExecutesWhenMonitorBecomesAvailable", TestPendingSwitchExecutesWhenMonitorBecomesAvailable);
            Run("PendingDisplayReadyWaitsForHdrAndCompletes", TestPendingDisplayReadyWaitsForHdrAndCompletes);
            Run("CancelPendingSwitchStopsWaiting", TestCancelPendingSwitchStopsWaiting);

            Console.WriteLine(string.Format("Tests complete: {0} passed, {1} failed", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static AgentConfig CreateTestConfig()
        {
            return new AgentConfig
            {
                BearerToken = "test-token",
                Office = new EnvironmentConfig
                {
                    Monitor = "Odyssey G80SD",
                    Audio = "Sound Blaster Play! 3",
                    Width = 3840,
                    Height = 2160,
                    RefreshRate = 240,
                    Hdr = true
                },
                LivingRoom = new EnvironmentConfig
                {
                    Monitor = "LG TV SSCR2",
                    Audio = "LG TV SSCR2",
                    Width = 3840,
                    Height = 2160,
                    RefreshRate = 120,
                    Hdr = true,
                    Integrations = new Dictionary<string, Dictionary<string, object>>
                    {
                        { "steam", new Dictionary<string, object> { { "bigPicture", true } } }
                    }
                },
                VerifyRetryCount = 1,
                SteamCloseMode = "exit-bigpicture",
                DisplaySettleDelayMs = 10,
                WakeGuardSeconds = 1,
                IdleRevertMinutes = 30,
                IdleSleepAfterRevert = false,
                ConsoleEnvironmentOnLivingRoom = true
            };
        }

        private static void TestLivingRoomTransitionOrder()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected LivingRoom mode");
                Assert(actions.Calls.Count == 8, "Expected 8 actions, got " + actions.Calls.Count);
                Assert(actions.Calls[0] == "windows:save", "Expected save window positions first");
                Assert(actions.Calls[1] == "vdesktop:new", "Expected fresh virtual desktop second");
                Assert(actions.Calls[2] == "icons:hide", "Expected hide desktop icons third");
                Assert(actions.Calls[3] == "display:LG TV SSCR2@3840x2160:120:hdr=True", "Expected display switch fourth");
                Assert(actions.Calls[4] == "verify:LG TV SSCR2|LG TV SSCR2", "Expected verify fifth");
                Assert(actions.Calls[5] == "taskbar:hide", "Expected hide taskbar sixth");
                Assert(actions.Calls[6] == "cursor:lower-right", "Expected cursor move to lower-right seventh");
                Assert(actions.Calls[7] == "steam:open", "Expected Steam Big Picture eighth");
            }
        }

        private static void TestOfficeTransitionOrder()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                actions.Calls.Clear();

                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office mode");
                Assert(actions.Calls.Count == 8, "Expected 8 actions, got " + actions.Calls.Count);
                Assert(actions.Calls[0] == "steam:close:exit-bigpicture", "Expected Steam close first");
                Assert(actions.Calls[1] == "display:Odyssey G80SD@3840x2160:240:hdr=True", "Expected Office display second");
                Assert(actions.Calls[2] == "verify:Odyssey G80SD|Sound Blaster Play! 3", "Expected verify third");
                Assert(actions.Calls[3] == "steam:close-window", "Expected Steam window close fourth");
                Assert(actions.Calls[4] == "taskbar:show", "Expected show taskbar fifth");
                Assert(actions.Calls[5] == "icons:show", "Expected show desktop icons sixth");
                Assert(actions.Calls[6] == "vdesktop:revert", "Expected revert virtual desktop seventh");
                Assert(actions.Calls[7] == "windows:restore", "Expected restore window positions eighth");
            }
        }

        private static void TestWakeGuardRevertsWithoutApi()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.WakeGuardSeconds = 1;
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                ctrl.OnPowerResume();
                Thread.Sleep(1500);
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected wake-guard to revert to Office");
            }
        }

        private static void TestWakeGuardCancelledByApi()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.WakeGuardSeconds = 1;
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                ctrl.OnPowerResume();
                Thread.Sleep(250);
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                Thread.Sleep(1200);
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected mode to stay LivingRoom after API refresh");
            }
        }

        private static void TestPreSleepRevertsToOffice()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                ctrl.OnPowerSuspend();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected suspend to revert to Office");
            }
        }

        private static void TestShutdownRevertsToOffice()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                actions.Calls.Clear();

                ctrl.ShutdownAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected shutdown to revert to Office");
                Assert(actions.Calls.Contains("shutdown"), "Expected ShutdownComputer to be called");
            }
        }

        private static void TestShutdownProceedsWhenOfficeMonitorUnavailable()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            actions.VerifyResult = false;
            actions.MonitorAvailableResult = false;
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.ShutdownAsync().GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("shutdown"), "Expected ShutdownComputer to be called even when Office monitor is unavailable");
            }
        }

        private static void TestIdleTimeoutRevertsToOffice()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.IdleRevertMinutes = 30;
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();

                idle.CurrentIdle = TimeSpan.FromMinutes(10);
                ctrl.CheckIdleAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Should stay LivingRoom below idle threshold");

                idle.CurrentIdle = TimeSpan.FromMinutes(31);
                ctrl.CheckIdleAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Should revert to Office when idle >= 30m");
            }
        }

        private static void TestValidationRetriesAndFailsOnMismatch()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.VerifyRetryCount = 1;
            FakeSystemActions actions = new FakeSystemActions
            {
                VerifyResult = false,
                VerifyMonitorOnlyResult = false
            };
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                bool threw = false;
                try
                {
                    ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }

                Assert(threw, "Expected validation failure to throw InvalidOperationException");
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected mode to remain Office on validation failure");

                int displayCalls = 0;
                for (int i = 0; i < actions.Calls.Count; i++)
                {
                    if (actions.Calls[i] == "display:LG TV SSCR2@3840x2160:120:hdr=True") displayCalls++;
                }
                Assert(displayCalls == 2, "Expected initial display call plus 1 retry, got " + displayCalls);
            }
        }

        private static void TestAudioFallbackFixesAudioMismatch()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.VerifyRetryCount = 1;
            FakeSystemActions actions = new FakeSystemActions
            {
                VerifyResult = false,
                VerifyMonitorOnlyResult = true,
                SetAudioResult = true
            };
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected LivingRoom mode after direct audio fallback");
                Assert(actions.Calls.Contains("audio:set:LG TV SSCR2"), "Expected direct audio fallback to be called");
            }
        }

        private static void TestPreFlightMonitorCheckBlocksUnavailableTarget()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions { MonitorAvailableResult = false };
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();

                Assert(ctrl.PendingProfileId == "living-room", "Expected pending profile to be living-room");
                Assert(ctrl.PendingSwitchReason == "waiting-for-display", "Expected waiting-for-display reason");
                Assert(actions.Calls.Count == 0, "Expected no windows minimized or display switched while waiting");
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected mode to remain Office while waiting");
            }
        }

        private static void TestXboxReceiverMonitorEdgeTriggers()
        {
            List<string> liveIds = new List<string> { @"USB\VID_045E&PID_0B12&IG_00\00&00&EXISTING" };
            XboxReceiverControllerMonitor monitor = new XboxReceiverControllerMonitor(() => liveIds);

            IList<string> first = monitor.PollNewlyConnectedControllers();
            Assert(first.Count == 0, "Expected startup-connected controller not to trigger");

            liveIds.Add(@"USB\VID_045E&PID_0B12&IG_00\01&00&SECOND");
            IList<string> second = monitor.PollNewlyConnectedControllers();
            Assert(second.Count == 1 && second[0] == @"USB\VID_045E&PID_0B12&IG_00\01&00&SECOND", "Expected newly connected controller to trigger");

            IList<string> steady = monitor.PollNewlyConnectedControllers();
            Assert(steady.Count == 0, "Expected already-connected controller not to re-trigger");

            liveIds.Remove(@"USB\VID_045E&PID_0B12&IG_00\01&00&SECOND");
            IList<string> afterDisconnect = monitor.PollNewlyConnectedControllers();
            Assert(afterDisconnect.Count == 0, "Expected disconnect not to trigger");

            liveIds.Add(@"USB\VID_045E&PID_0B12&IG_00\01&00&SECOND");
            IList<string> reconnected = monitor.PollNewlyConnectedControllers();
            Assert(reconnected.Count == 1, "Expected reconnected controller to trigger again");
        }

        private static void TestControllerTriggerSwitchesToLivingRoom()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            FakeControllerMonitor controllers = new FakeControllerMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, controllers, new FakeLogger()))
            {
                ctrl.CheckControllerTriggerAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office mode when no controller connected");

                controllers.NextNewlyConnected.Add(@"USB\VID_045E&PID_0B12&IG_00\00&00&00001D403389ED7E");
                ctrl.CheckControllerTriggerAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected LivingRoom mode when receiver controller connects");
                Assert(idle.ResetCount == 1, "Expected idle timer reset on LivingRoom switch");

                controllers.NextNewlyConnected.Add(@"USB\VID_045E&PID_0B12&IG_00\01&00&0000FABC4A8AED7E");
                ctrl.CheckControllerTriggerAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected mode to stay LivingRoom");
                Assert(idle.ResetCount == 2, "Expected idle timer reset when second controller connects in LivingRoom mode");
            }
        }

        private static void TestControllerTriggerRespectsDisabledFlag()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.XboxReceiverTriggerEnabled = false;
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            FakeControllerMonitor controllers = new FakeControllerMonitor();
            controllers.NextNewlyConnected.Add(@"USB\VID_045E&PID_0B12&IG_00\00&00&00001D403389ED7E");

            using (RoomController ctrl = new RoomController(cfg, actions, idle, controllers, new FakeLogger()))
            {
                ctrl.CheckControllerTriggerAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office mode when XboxReceiverTriggerEnabled is false");
                Assert(actions.Calls.Count == 0, "Expected no system actions when trigger is disabled");
            }
        }

        private static void TestControllerTriggerRespectsDisabledProfileId()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.ControllerTriggerProfileId = AgentConfig.DisabledControllerTriggerId;
            cfg.ApplyDefaultsAndNormalize();
            Assert(!cfg.XboxReceiverTriggerEnabled, "Expected XboxReceiverTriggerEnabled to be false when ControllerTriggerProfileId is disabled");
            Assert(cfg.GetControllerTriggerProfile() == null, "Expected GetControllerTriggerProfile() to return null when disabled");

            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            FakeControllerMonitor controllers = new FakeControllerMonitor();
            controllers.NextNewlyConnected.Add(@"USB\VID_045E&PID_0B12&IG_00\00&00&00001D403389ED7E");

            using (RoomController ctrl = new RoomController(cfg, actions, idle, controllers, new FakeLogger()))
            {
                ctrl.CheckControllerTriggerAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office mode when ControllerTriggerProfileId is disabled");
                Assert(actions.Calls.Count == 0, "Expected no system actions when trigger profile is disabled");
            }
        }

        private static void TestLivingRoomClosesConfiguredApps()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.LivingRoom.CloseApps = new[] { "Telegram" };
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected LivingRoom mode");
                Assert(actions.Calls.Count == 9, "Expected 9 actions, got " + actions.Calls.Count);
                Assert(actions.Calls[0] == "close-apps:Telegram", "Expected CloseApplications first");
                Assert(actions.Calls[1] == "windows:save", "Expected save window positions second");
                Assert(actions.Calls[2] == "vdesktop:new", "Expected fresh virtual desktop third");
            }
        }

        private static void TestOfficeClosesConfiguredApps()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.Office.CloseApps = new[] { "Discord", "Spotify" };
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                actions.Calls.Clear();

                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office mode");
                Assert(actions.Calls.Count == 9, "Expected 9 actions, got " + actions.Calls.Count);
                Assert(actions.Calls[0] == "close-apps:Discord,Spotify", "Expected CloseApplications first");
                Assert(actions.Calls[1] == "steam:close:exit-bigpicture", "Expected Steam close second");
                Assert(actions.Calls[8] == "windows:restore", "Expected restore window positions last");
            }
        }

        private static void TestUnavailableMonitorDoesNotCloseApps()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.LivingRoom.CloseApps = new[] { "Telegram" };
            FakeSystemActions actions = new FakeSystemActions { MonitorAvailableResult = false };
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();

                Assert(ctrl.PendingProfileId == "living-room", "Expected pending living-room switch");
                Assert(actions.Calls.Count == 0, "Expected no apps closed while monitor is unavailable");
            }
        }

        private static void TestConfigLoadDefaultsAndReadsCloseApps()
        {
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempPath, @"{
                    ""Office"": { ""Monitor"": ""M1"" },
                    ""LivingRoom"": { ""Monitor"": ""M2"", ""CloseApps"": [""Telegram"", ""Discord.exe""] }
                }");

                AgentConfig loaded = AgentConfig.Load(tempPath);
                Assert(loaded.Office.CloseApps != null && loaded.Office.CloseApps.Length == 0, "Expected Office.CloseApps to default to empty array");
                Assert(loaded.LivingRoom.CloseApps != null && loaded.LivingRoom.CloseApps.Length == 2, "Expected LivingRoom.CloseApps to have 2 entries");
                Assert(loaded.LivingRoom.CloseApps[0] == "Telegram", "Expected first app Telegram");
                Assert(loaded.LivingRoom.CloseApps[1] == "Discord.exe", "Expected second app Discord.exe");
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }

        private static void TestHueSyncDisabledByDefaultDoesNotToggle()
        {
            AgentConfig cfg = CreateTestConfig();
            Assert(!cfg.Office.HueSync && !cfg.LivingRoom.HueSync, "Expected HueSync to default to false for both rooms");
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();

                Assert(!actions.Calls.Contains("huesync:stop"), "Did not expect huesync:stop when disabled");
                Assert(!actions.Calls.Contains("huesync:start"), "Did not expect huesync:start when disabled");
            }
        }

        private static void TestHueSyncEnabledStopsInLivingRoomAndStartsInOffice()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.Office.HueSync = true;
            cfg.LivingRoom.HueSync = false;
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("huesync:stop"), "Expected huesync:stop in LivingRoom mode");
                Assert(!actions.Calls.Contains("huesync:start"), "Did not expect huesync:start in LivingRoom mode");

                actions.Calls.Clear();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();
                int displayIdx = actions.Calls.IndexOf("display:Odyssey G80SD@3840x2160:240:hdr=True");
                int hueStartIdx = actions.Calls.IndexOf("huesync:start");
                Assert(hueStartIdx >= 0, "Expected huesync:start in Office mode");
                Assert(displayIdx >= 0 && hueStartIdx > displayIdx, "Expected huesync:start after Office display switch");
                Assert(!actions.Calls.Contains("huesync:stop"), "Did not expect huesync:stop in Office mode");
            }

            cfg = CreateTestConfig();
            cfg.Office.HueSync = false;
            cfg.LivingRoom.HueSync = true;
            actions = new FakeSystemActions();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                int displayIdx = actions.Calls.IndexOf("display:LG TV SSCR2@3840x2160:120:hdr=True");
                int hueStartIdx = actions.Calls.IndexOf("huesync:start");
                Assert(hueStartIdx >= 0, "Expected huesync:start in LivingRoom mode when LivingRoom.HueSync is true");
                Assert(displayIdx >= 0 && hueStartIdx > displayIdx, "Expected huesync:start after LivingRoom display switch");
                Assert(!actions.Calls.Contains("huesync:stop"), "Did not expect huesync:stop in LivingRoom mode");

                actions.Calls.Clear();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("huesync:stop"), "Expected huesync:stop in Office mode when Office.HueSync is false");
                Assert(!actions.Calls.Contains("huesync:start"), "Did not expect huesync:start in Office mode");
            }
        }

        private static void TestConfigLoadDefaultsAndReadsHueSync()
        {
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempPath, @"{
                    ""Office"": { ""Monitor"": ""M1"" },
                    ""LivingRoom"": { ""Monitor"": ""M2"" }
                }");

                AgentConfig defaults = AgentConfig.Load(tempPath);
                Assert(!defaults.Office.HueSync, "Expected Office.HueSync to default to false");
                Assert(!defaults.LivingRoom.HueSync, "Expected LivingRoom.HueSync to default to false");
                Assert(defaults.HueSyncPath == @"C:\Program Files\Hue Sync\HueSync.exe", "Expected default HueSyncPath");

                System.IO.File.WriteAllText(tempPath, @"{
                    ""Office"": { ""Monitor"": ""M1"", ""HueSync"": true },
                    ""LivingRoom"": { ""Monitor"": ""M2"", ""HueSync"": false },
                    ""HueSyncPath"": ""D:\\Tools\\HueSync.exe""
                }");

                AgentConfig custom = AgentConfig.Load(tempPath);
                Assert(custom.Office.HueSync, "Expected Office.HueSync to be true");
                Assert(!custom.LivingRoom.HueSync, "Expected LivingRoom.HueSync to be false");
                Assert(custom.HueSyncPath == @"D:\Tools\HueSync.exe", "Expected custom HueSyncPath");
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }

        private static void TestLivingRoomValidationFailureRevertsToOfficeDisplay()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.VerifyRetryCount = 0;
            cfg.Office.HueSync = true;
            cfg.LivingRoom.HueSync = false;
            FakeSystemActions actions = new FakeSystemActions
            {
                VerifyResult = false,
                VerifyMonitorOnlyResult = false
            };
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                bool threw = false;
                try
                {
                    ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }

                Assert(threw, "Expected validation failure to throw");
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected mode to remain Office");
                Assert(actions.Calls.Contains("display:Odyssey G80SD@3840x2160:240:hdr=True"), "Expected rollback to Office display");
                Assert(actions.Calls.Contains("taskbar:show"), "Expected rollback to show taskbar");
                Assert(actions.Calls.Contains("icons:show"), "Expected rollback to show desktop icons");
                Assert(actions.Calls.Contains("vdesktop:revert"), "Expected rollback to revert virtual desktop");
                Assert(actions.Calls.Contains("huesync:start"), "Expected rollback to restart Hue Sync for Office");
            }
        }

        private static void TestConsoleEnvironmentDisabledSkipsDesktopActions()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.ConsoleEnvironmentOnLivingRoom = false;
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();

                Assert(!actions.Calls.Contains("vdesktop:new"), "Did not expect vdesktop:new when disabled");
                Assert(!actions.Calls.Contains("vdesktop:revert"), "Did not expect vdesktop:revert when disabled");
                Assert(!actions.Calls.Contains("taskbar:hide"), "Did not expect taskbar:hide when disabled");
                Assert(!actions.Calls.Contains("taskbar:show"), "Did not expect taskbar:show when disabled");
                Assert(!actions.Calls.Contains("icons:hide"), "Did not expect icons:hide when disabled");
                Assert(!actions.Calls.Contains("icons:show"), "Did not expect icons:show when disabled");
            }
        }

        private static void TestOfficeModeReappliesWhenOutputDevicesMismatch()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.Office.HueSync = true;
            FakeSystemActions actions = new FakeSystemActions
            {
                VerifyResult = false,
                VerifyMonitorOnlyResult = false
            };
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                actions.VerifyResult = false;
                // First VerifyOutputDevices in SwitchToOfficeAsync fails, then subsequent verification passes
                bool threw = false;
                try
                {
                    ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }

                Assert(threw, "Expected SwitchToOfficeAsync to attempt display switch when VerifyOutputDevices fails");
                Assert(actions.Calls.Contains("display:Odyssey G80SD@3840x2160:240:hdr=True"), "Expected Office display switch to be attempted");
            }
        }

        private static void TestXboxConsoleEnvironmentModeTriggersAndRevertsXboxMode()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.ConsoleEnvironmentOnLivingRoom = true;
            cfg.ConsoleEnvironmentMode = "xbox";
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();

                Assert(actions.Calls.Contains("xbox:enter"), "Expected xbox:enter when ConsoleEnvironmentMode is xbox");
                Assert(!actions.Calls.Contains("vdesktop:new"), "Did not expect vdesktop:new in xbox mode");
                Assert(!actions.Calls.Contains("icons:hide"), "Did not expect icons:hide in xbox mode");
                Assert(!actions.Calls.Contains("taskbar:hide"), "Did not expect taskbar:hide in xbox mode");

                int displayIdx = actions.Calls.IndexOf("display:LG TV SSCR2@3840x2160:120:hdr=True");
                int xboxEnterIdx = actions.Calls.IndexOf("xbox:enter");
                int steamOpenIdx = actions.Calls.IndexOf("steam:open");
                Assert(displayIdx >= 0 && xboxEnterIdx > displayIdx, "Expected xbox:enter after display switch");
                Assert(steamOpenIdx > xboxEnterIdx, "Expected steam:open after xbox:enter");

                actions.Calls.Clear();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();

                Assert(actions.Calls.Contains("xbox:exit"), "Expected xbox:exit when reverting from xbox mode");
                Assert(!actions.Calls.Contains("taskbar:show"), "Did not expect taskbar:show in xbox mode");
                Assert(!actions.Calls.Contains("icons:show"), "Did not expect icons:show in xbox mode");
                Assert(!actions.Calls.Contains("vdesktop:revert"), "Did not expect vdesktop:revert in xbox mode");
            }
        }

        private static void TestXboxConsoleEnvironmentModeDisabledSkipsXboxActions()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.ConsoleEnvironmentOnLivingRoom = false;
            cfg.ConsoleEnvironmentMode = "xbox";
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();

                Assert(!actions.Calls.Contains("xbox:enter"), "Did not expect xbox:enter when ConsoleEnvironmentOnLivingRoom is false");
                Assert(!actions.Calls.Contains("xbox:exit"), "Did not expect xbox:exit when ConsoleEnvironmentOnLivingRoom is false");
            }
        }

        private static void TestConfigLoadDefaultsAndReadsConsoleEnvironmentMode()
        {
            string tempDefault = System.IO.Path.GetTempFileName();
            string tempXbox = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempDefault, "{}");
                AgentConfig defaultCfg = AgentConfig.Load(tempDefault);
                Assert(defaultCfg.ConsoleEnvironmentMode == "virtual-desktop", "Expected default ConsoleEnvironmentMode to be virtual-desktop");
                Assert(!defaultCfg.IsXboxConsoleEnvironmentMode(), "Expected default IsXboxConsoleEnvironmentMode to be false");
                Assert(defaultCfg.LivingRoomDesktopColorEnabled, "Expected default LivingRoomDesktopColorEnabled to be true");
                Assert(defaultCfg.LivingRoomDesktopColor == "#000000", "Expected default LivingRoomDesktopColor to be #000000");

                System.IO.File.WriteAllText(tempXbox, "{\"ConsoleEnvironmentMode\":\"xbox\",\"LivingRoomDesktopColorEnabled\":false,\"LivingRoomDesktopColor\":\"#000000\"}");
                AgentConfig xboxCfg = AgentConfig.Load(tempXbox);
                Assert(xboxCfg.ConsoleEnvironmentMode == "xbox", "Expected loaded ConsoleEnvironmentMode to be xbox");
                Assert(xboxCfg.IsXboxConsoleEnvironmentMode(), "Expected IsXboxConsoleEnvironmentMode to be true");
                Assert(!xboxCfg.LivingRoomDesktopColorEnabled, "Expected loaded LivingRoomDesktopColorEnabled to be false");
                Assert(xboxCfg.LivingRoomDesktopColor == "#000000", "Expected loaded LivingRoomDesktopColor to be #000000");
            }
            finally
            {
                System.IO.File.Delete(tempDefault);
                System.IO.File.Delete(tempXbox);
            }
        }

        private static void TestMinimizeAllConsoleEnvironmentModeMinimizesAndSkipsRevert()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.ConsoleEnvironmentOnLivingRoom = true;
            cfg.ConsoleEnvironmentMode = "minimize-all";
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();

                Assert(actions.Calls.Contains("minimize-all"), "Expected minimize-all when ConsoleEnvironmentMode is minimize-all");
                Assert(!actions.Calls.Contains("vdesktop:new"), "Did not expect vdesktop:new in minimize-all mode");
                Assert(!actions.Calls.Contains("icons:hide"), "Did not expect icons:hide in minimize-all mode");
                Assert(!actions.Calls.Contains("taskbar:hide"), "Did not expect taskbar:hide in minimize-all mode");
                Assert(!actions.Calls.Contains("xbox:enter"), "Did not expect xbox:enter in minimize-all mode");

                int minIdx = actions.Calls.IndexOf("minimize-all");
                int displayIdx = actions.Calls.IndexOf("display:LG TV SSCR2@3840x2160:120:hdr=True");
                Assert(minIdx >= 0 && displayIdx > minIdx, "Expected minimize-all before display switch");

                actions.Calls.Clear();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();

                Assert(actions.Calls.Contains("minimize-all:restore"), "Expected minimize-all:restore when leaving minimize-all mode");
                Assert(!actions.Calls.Contains("xbox:exit"), "Did not expect xbox:exit in minimize-all mode");
                Assert(!actions.Calls.Contains("taskbar:show"), "Did not expect taskbar:show in minimize-all mode");
                Assert(!actions.Calls.Contains("icons:show"), "Did not expect icons:show in minimize-all mode");
                Assert(!actions.Calls.Contains("vdesktop:revert"), "Did not expect vdesktop:revert in minimize-all mode");

                actions.Calls.Clear();
                ctrl.SetConsoleEnvironmentModeAsync("minimize-all").GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("minimize-all"), "Expected minimize-all when switching from default to minimize-all");

                actions.Calls.Clear();
                ctrl.SetConsoleEnvironmentModeAsync("off").GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("minimize-all:restore"), "Expected minimize-all:restore when switching from minimize-all back to default");
            }
        }

        private static void TestSwitchConsoleEnvironmentModeWhileInLivingRoom()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.ConsoleEnvironmentOnLivingRoom = true;
            cfg.ConsoleEnvironmentMode = "virtual-desktop";
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                actions.Calls.Clear();

                ctrl.SwitchToLivingRoomAsync(true, "xbox").GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("vdesktop:revert"), "Expected previous virtual desktop to revert");
                Assert(actions.Calls.Contains("xbox:enter"), "Expected xbox:enter after switching console environment mode");

                actions.Calls.Clear();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("xbox:exit"), "Expected xbox:exit when reverting to Office");
            }
        }

        private static void TestSwitchConsoleEnvironmentModeWhileInOffice()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected initial Office mode");
                Assert(ctrl.ActiveConsoleEnvironmentMode == "off", "Expected initial Office console environment mode to be off");

                ctrl.SetConsoleEnvironmentModeAsync("virtual-desktop").GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected to remain in Office mode");
                Assert(ctrl.ActiveConsoleEnvironmentMode == "virtual-desktop", "Expected active mode to be virtual-desktop");
                Assert(actions.Calls.Contains("vdesktop:new"), "Expected vdesktop:new in Office mode");
                Assert(actions.Calls.Contains("taskbar:hide"), "Expected taskbar:hide in Office mode");
                Assert(!actions.Calls.Contains("display:LG TV SSCR2@3840x2160:120:hdr=True"), "Did not expect LivingRoom display switch");

                actions.Calls.Clear();
                ctrl.SetConsoleEnvironmentModeAsync("xbox").GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected to remain in Office mode");
                Assert(ctrl.ActiveConsoleEnvironmentMode == "xbox", "Expected active mode to be xbox");
                Assert(actions.Calls.Contains("vdesktop:revert"), "Expected virtual desktop to revert");
                Assert(actions.Calls.Contains("xbox:enter"), "Expected xbox:enter in Office mode");

                actions.Calls.Clear();
                ctrl.SetConsoleEnvironmentModeAsync("minimize-all").GetAwaiter().GetResult();
                Assert(ctrl.ActiveConsoleEnvironmentMode == "minimize-all", "Expected active mode to be minimize-all");
                Assert(actions.Calls.Contains("xbox:exit"), "Expected xbox:exit");
                Assert(actions.Calls.Contains("minimize-all"), "Expected minimize-all");

                actions.Calls.Clear();
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected LivingRoom mode");
                Assert(ctrl.ActiveConsoleEnvironmentMode == "virtual-desktop", "Expected LivingRoom configured mode virtual-desktop");
                Assert(actions.Calls.Contains("vdesktop:new"), "Expected vdesktop:new on LivingRoom switch");

                actions.Calls.Clear();
                ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office mode");
                Assert(ctrl.ActiveConsoleEnvironmentMode == "minimize-all", "Expected Office remembered mode minimize-all");
                Assert(actions.Calls.Contains("minimize-all"), "Expected minimize-all on Office switch");
            }
        }

        private static void TestPerRoomConsoleEnvironmentModeConfigDefaultsAndTransitions()
        {
            string tempPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempPath, @"{
                    ""Office"": { ""Monitor"": ""M1"", ""ConsoleEnvironmentMode"": ""minimize-all"" },
                    ""LivingRoom"": { ""Monitor"": ""M2"", ""ConsoleEnvironmentMode"": ""xbox"" }
                }");

                AgentConfig cfg = AgentConfig.Load(tempPath);
                Assert(cfg.GetRoomConsoleEnvironmentMode(RoomMode.Office) == "minimize-all", "Expected Office ConsoleEnvironmentMode minimize-all");
                Assert(cfg.GetRoomConsoleEnvironmentMode(RoomMode.LivingRoom) == "xbox", "Expected LivingRoom ConsoleEnvironmentMode xbox");

                FakeSystemActions actions = new FakeSystemActions();
                FakeIdleMonitor idle = new FakeIdleMonitor();
                using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
                {
                    Assert(ctrl.ActiveConsoleEnvironmentMode == "minimize-all", "Expected initial ActiveConsoleEnvironmentMode minimize-all");
                    ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                    Assert(ctrl.ActiveConsoleEnvironmentMode == "xbox", "Expected LivingRoom ActiveConsoleEnvironmentMode xbox");
                    Assert(actions.Calls.Contains("xbox:enter"), "Expected xbox:enter for LivingRoom");

                    actions.Calls.Clear();
                    ctrl.SwitchToOfficeAsync("api").GetAwaiter().GetResult();
                    Assert(ctrl.ActiveConsoleEnvironmentMode == "minimize-all", "Expected Office ActiveConsoleEnvironmentMode minimize-all");
                    Assert(actions.Calls.Contains("xbox:exit"), "Expected xbox:exit when leaving LivingRoom");
                    Assert(actions.Calls.Contains("minimize-all"), "Expected minimize-all when entering Office");
                }
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }

        private static void TestShortcutsAndWindowsControllerActions()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            actions.Shortcuts.Add(new DesktopShortcutInfo { Name = "Steam", Path = @"C:\Desktop\Steam.lnk", Extension = "lnk", Source = "user" });
            actions.OpenWindows.Add(new OpenWindowInfo { Hwnd = 101, Pid = 10, ProcessName = "notepad", Title = "Notes" });
            actions.OpenWindows.Add(new OpenWindowInfo { Hwnd = 202, Pid = 20, ProcessName = "chrome", Title = "Browser" });
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                string launched;
                Assert(ctrl.LaunchDesktopShortcut("Steam", out launched), "Expected shortcut launch to succeed");
                Assert(launched == "Steam", "Expected launched name Steam");
                Assert(!ctrl.LaunchDesktopShortcut("Missing", out launched), "Expected missing shortcut to return false");

                Assert(ctrl.FocusWindow(101), "Expected FocusWindow 101 to succeed");
                Assert(actions.Calls.Contains("window:focus:101"), "Expected hwnd 101 focused");
                Assert(!ctrl.FocusWindow(999), "Expected FocusWindow 999 to return false");

                Assert(ctrl.MaximizeWindow(101), "Expected MaximizeWindow 101 to succeed");
                Assert(actions.Calls.Contains("window:maximize:101"), "Expected hwnd 101 maximized");
                Assert(!ctrl.MaximizeWindow(999), "Expected MaximizeWindow 999 to return false");

                Assert(ctrl.CloseAllOpenWindows() == 2, "Expected 2 windows closed");
                Assert(actions.Calls.Contains("window:close:101"), "Expected hwnd 101 closed");
                Assert(actions.Calls.Contains("window:close:202"), "Expected hwnd 202 closed");

                Assert(ctrl.OpenTaskView(), "Expected OpenTaskView to succeed");
                Assert(actions.Calls.Contains("task-view"), "Expected task-view call");
            }
        }

        private static void TestBuiltInShortcutsAlwaysPresent()
        {
            AgentConfig cfg = CreateTestConfig();
            WindowsSystemActions actions = new WindowsSystemActions(cfg, new FakeLogger());
            IList<DesktopShortcutInfo> shortcuts = actions.GetDesktopShortcuts();

            bool hasSteamBigPicture = false;
            bool hasXboxApp = false;
            foreach (DesktopShortcutInfo s in shortcuts)
            {
                if (string.Equals(s.Name, WindowsSystemActions.SteamBigPictureShortcutName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Name, "Steam Big Picture", StringComparison.OrdinalIgnoreCase))
                {
                    hasSteamBigPicture = true;
                }
                if (string.Equals(s.Name, WindowsSystemActions.XboxAppShortcutName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Name, "Xbox", StringComparison.OrdinalIgnoreCase))
                {
                    hasXboxApp = true;
                }
            }

            Assert(hasSteamBigPicture, "Expected Steam Big Picture Mode shortcut to always be present");
            Assert(hasXboxApp, "Expected Xbox App shortcut to always be present");

            List<DesktopShortcutInfo> existingDesktop = new List<DesktopShortcutInfo>
            {
                new DesktopShortcutInfo { Name = "Steam Big Picture", Path = @"C:\Desktop\Steam Big Picture.lnk", Extension = "lnk", Source = "user" },
                new DesktopShortcutInfo { Name = "Xbox", Path = @"C:\Desktop\Xbox.lnk", Extension = "lnk", Source = "user" }
            };
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\Desktop\Steam Big Picture.lnk",
                @"C:\Desktop\Xbox.lnk"
            };
            actions.AddBuiltInShortcuts(existingDesktop, seen);
            Assert(existingDesktop.Count == 2, "Expected built-in shortcuts to deduplicate against existing desktop shortcuts");

            string tempShortcutsDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cerberus-shortcuts-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tempShortcutsDir);
            try
            {
                string customUrl = System.IO.Path.Combine(tempShortcutsDir, "Custom Game.url");
                System.IO.File.WriteAllText(customUrl, "[InternetShortcut]\r\nURL=steam://rungameid/12345\r\n");

                AgentConfig customCfg = CreateTestConfig();
                customCfg.ShortcutsDirectory = tempShortcutsDir;
                customCfg.ApplyDefaultsAndNormalize();
                Assert(customCfg.ShortcutsDirectory == tempShortcutsDir, "Expected custom ShortcutsDirectory to be preserved");

                WindowsSystemActions customActions = new WindowsSystemActions(customCfg, new FakeLogger());
                IList<DesktopShortcutInfo> customShortcuts = customActions.GetDesktopShortcuts();
                bool hasCustom = false;
                bool customHasBuiltInSteam = false;
                bool customHasBuiltInXbox = false;
                foreach (DesktopShortcutInfo s in customShortcuts)
                {
                    if (s.Name == "Custom Game" && s.Source == "custom") hasCustom = true;
                    if (s.Name == WindowsSystemActions.SteamBigPictureShortcutName) customHasBuiltInSteam = true;
                    if (s.Name == WindowsSystemActions.XboxAppShortcutName) customHasBuiltInXbox = true;
                }
                Assert(hasCustom, "Expected custom shortcut folder entry to appear");
                Assert(customHasBuiltInSteam && customHasBuiltInXbox, "Expected built-in shortcuts to still appear with custom folder");
            }
            finally
            {
                if (System.IO.Directory.Exists(tempShortcutsDir))
                {
                    System.IO.Directory.Delete(tempShortcutsDir, true);
                }
            }
        }

        private static void TestReconcileStartupStateRevertsLeftoverConsoleEnvironmentInOfficeMode()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            actions.PendingConsoleEnvironmentRevert = true;
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.ReconcileStartupStateAsync().GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("xbox:exit"), "Expected xbox:exit on startup reconciliation");
                Assert(actions.Calls.Contains("taskbar:show"), "Expected taskbar:show on startup reconciliation");
                Assert(actions.Calls.Contains("icons:show"), "Expected icons:show on startup reconciliation");
                Assert(actions.Calls.Contains("vdesktop:revert"), "Expected vdesktop:revert on startup reconciliation");
                Assert(!actions.HasPendingConsoleEnvironmentRevert(), "Expected pending state cleared");
                Assert(ctrl.ActiveConsoleEnvironmentMode == "off", "Expected ActiveConsoleEnvironmentMode to be off");
            }
        }

        private static void TestReconcileStartupStateSkipsWhenNoPendingState()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            actions.PendingConsoleEnvironmentRevert = false;
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.ReconcileStartupStateAsync().GetAwaiter().GetResult();
                Assert(actions.Calls.Count == 0, "Expected no revert calls when no pending state");
            }
        }

        private static void TestSetConsoleEnvironmentModeOffRevertsPendingStateEvenWhenAlreadyOff()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            actions.PendingConsoleEnvironmentRevert = true;
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SetConsoleEnvironmentModeAsync("off").GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("taskbar:show"), "Expected taskbar:show");
                Assert(actions.Calls.Contains("icons:show"), "Expected icons:show");
                Assert(actions.Calls.Contains("vdesktop:revert"), "Expected vdesktop:revert");
            }
        }

        private static void TestOnSessionEndingRevertsToOfficeAndForcesConsoleEnvironmentRevertOnError()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                actions.Calls.Clear();
                actions.MonitorAvailableResult = false; // Force SwitchToOfficeAsync to throw during session ending
                actions.PendingConsoleEnvironmentRevert = true;

                ctrl.OnSessionEnding("SystemShutdown");
                Assert(actions.Calls.Contains("taskbar:show"), "Expected forced taskbar:show on session ending error");
                Assert(actions.Calls.Contains("icons:show"), "Expected forced icons:show on session ending error");
                Assert(actions.Calls.Contains("vdesktop:revert"), "Expected forced vdesktop:revert on session ending error");
            }
        }

        private static void TestWindowsSystemActionsLoadsPersistedConsoleEnvironmentState()
        {
            string tempStatePath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempStatePath, @"{
                    ""CreatedVirtualDesktop"": true,
                    ""CreatedVirtualDesktopId"": ""8fd54f72-d874-46f7-8eb3-e874b650bbcf"",
                    ""TaskbarHiddenByUs"": true,
                    ""DesktopIconsHiddenByUs"": true,
                    ""PreviousWallpaperPath"": ""C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg"",
                    ""WallpaperChangedByUs"": true,
                    ""XboxModeEnteredByUs"": false
                }");

                AgentConfig cfg = CreateTestConfig();
                WindowsSystemActions actions = new WindowsSystemActions(cfg, new FakeLogger(), tempStatePath);
                Assert(actions.HasPendingConsoleEnvironmentRevert(), "Expected persisted state to be detected on construction");
            }
            finally
            {
                if (System.IO.File.Exists(tempStatePath))
                {
                    System.IO.File.Delete(tempStatePath);
                }
            }
        }

        private static void TestWindowsSystemActionsRecordsAndRecoversWallpaperHistory()
        {
            string tempStatePath = System.IO.Path.GetTempFileName();
            string tempHistoryPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.Delete(tempStatePath);
                System.IO.File.Delete(tempHistoryPath);

                AgentConfig cfg = CreateTestConfig();
                WindowsSystemActions actions = new WindowsSystemActions(cfg, new FakeLogger(), tempStatePath, tempHistoryPath);

                actions.RecordWallpaperHistory(@"C:\Wallpapers\first.jpg");
                actions.RecordWallpaperHistory(@"C:\Wallpapers\second.jpg");
                actions.RecordWallpaperHistory(@"C:\Users\TestUser\AppData\Local\Temp\room-agent-bg.bmp");

                Assert(actions.GetLastKnownWallpaperFromHistory() == @"C:\Wallpapers\second.jpg",
                    "Expected last known wallpaper to be second.jpg and ignore room-agent-bg.bmp");
                Assert(System.IO.File.Exists(tempHistoryPath), "Expected wallpaper history file to be created");

                WindowsSystemActions reloaded = new WindowsSystemActions(cfg, new FakeLogger(), tempStatePath, tempHistoryPath);
                Assert(reloaded.GetLastKnownWallpaperFromHistory() == @"C:\Wallpapers\second.jpg",
                    "Expected reloaded instance to recover second.jpg from history");
            }
            finally
            {
                if (System.IO.File.Exists(tempStatePath))
                {
                    System.IO.File.Delete(tempStatePath);
                }
                if (System.IO.File.Exists(tempHistoryPath))
                {
                    System.IO.File.Delete(tempHistoryPath);
                }
            }
        }

        private static void TestSetConsoleEnvironmentModePersistsToConfigAndIsRemembered()
        {
            string tempConfigPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempConfigPath, @"{
  ""Port"": 8765,
  ""Office"": {
    ""Monitor"": ""Odyssey G80SD"",
    ""ConsoleEnvironmentMode"": ""off""
  },
  ""LivingRoom"": {
    ""Monitor"": ""LG TV SSCR2"",
    ""ConsoleEnvironmentMode"": ""virtual-desktop""
  },
  ""ConsoleEnvironmentMode"": ""virtual-desktop""
}");

                AgentConfig cfg = AgentConfig.Load(tempConfigPath);
                FakeSystemActions actions = new FakeSystemActions();
                FakeIdleMonitor idle = new FakeIdleMonitor();

                using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
                {
                    Assert(ctrl.ActiveConsoleEnvironmentMode == "off", "Expected initial Office mode off");
                    ctrl.SetConsoleEnvironmentModeAsync("virtual-desktop").GetAwaiter().GetResult();
                    Assert(cfg.GetRoomConsoleEnvironmentMode(RoomMode.Office) == "virtual-desktop",
                        "Expected Office config ConsoleEnvironmentMode to be updated to virtual-desktop");
                }

                AgentConfig reloaded = AgentConfig.Load(tempConfigPath);
                Assert(reloaded.GetRoomConsoleEnvironmentMode(RoomMode.Office) == "virtual-desktop",
                    "Expected reloaded Office ConsoleEnvironmentMode to be remembered as virtual-desktop");
                Assert(reloaded.GetRoomConsoleEnvironmentMode(RoomMode.LivingRoom) == "virtual-desktop",
                    "Expected LivingRoom ConsoleEnvironmentMode to remain virtual-desktop");

                using (RoomController ctrl2 = new RoomController(reloaded, new FakeSystemActions(), new FakeIdleMonitor(), new FakeLogger()))
                {
                    Assert(ctrl2.ActiveConsoleEnvironmentMode == "virtual-desktop",
                        "Expected new controller to remember virtual-desktop in Office mode");
                    ctrl2.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                    ctrl2.SetConsoleEnvironmentModeAsync("xbox").GetAwaiter().GetResult();
                }

                AgentConfig reloaded2 = AgentConfig.Load(tempConfigPath);
                Assert(reloaded2.GetRoomConsoleEnvironmentMode(RoomMode.Office) == "virtual-desktop",
                    "Expected Office ConsoleEnvironmentMode to remain virtual-desktop");
                Assert(reloaded2.GetRoomConsoleEnvironmentMode(RoomMode.LivingRoom) == "xbox",
                    "Expected LivingRoom ConsoleEnvironmentMode to be updated and remembered as xbox");
            }
            finally
            {
                if (System.IO.File.Exists(tempConfigPath))
                {
                    System.IO.File.Delete(tempConfigPath);
                }
            }
        }

        private static void TestStopStreamingAndSwitchToOffice()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions
            {
                StreamingActive = true,
                VerifyResult = false,
                VerifyResultAfterSwitch = true
            };
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                Assert(ctrl.GetStreamingSessionInfo().Active, "Expected streaming session to be active");
                ctrl.StopStreamingAsync("office").GetAwaiter().GetResult();

                Assert(!ctrl.GetStreamingSessionInfo().Active, "Expected streaming session to be stopped");
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office mode after stopping streaming");
                int stopIdx = actions.Calls.IndexOf("streaming:stop");
                int displayIdx = actions.Calls.FindIndex(c => c.StartsWith("display:Odyssey G80SD"));
                Assert(stopIdx >= 0, "Expected streaming:stop call");
                Assert(displayIdx > stopIdx, "Expected Office display switch after streaming:stop");
            }
        }

        private static void TestStopStreamingAndSwitchToLivingRoomWhenUnderlyingWasLivingRoom()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                actions.Calls.Clear();

                // Simulate external Moonlight/Apollo stream taking over the display while in LivingRoom mode
                actions.StreamingActive = true;
                actions.VerifyResult = false;
                actions.VerifyResultAfterSwitch = true;

                ctrl.StopStreamingAsync("living-room").GetAwaiter().GetResult();

                Assert(!ctrl.GetStreamingSessionInfo().Active, "Expected streaming session to be stopped");
                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected LivingRoom mode");
                int stopIdx = actions.Calls.IndexOf("streaming:stop");
                int displayIdx = actions.Calls.FindIndex(c => c.StartsWith("display:LG TV SSCR2"));
                Assert(stopIdx >= 0, "Expected streaming:stop call");
                Assert(displayIdx > stopIdx, "Expected LivingRoom display to be re-applied after streaming:stop");
            }
        }

        private static void TestIdleTimeoutAndControllerTriggerSkippedWhileStreamingActive()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.IdleRevertMinutes = 1;
            cfg.XboxReceiverTriggerEnabled = true;
            FakeSystemActions actions = new FakeSystemActions { StreamingActive = true };
            FakeIdleMonitor idle = new FakeIdleMonitor { CurrentIdle = TimeSpan.FromMinutes(10) };
            FakeControllerMonitor controllerMonitor = new FakeControllerMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, controllerMonitor, new FakeLogger()))
            {
                controllerMonitor.NextNewlyConnected.Add("USB\\VID_045E&PID_02FE\\1");
                ctrl.CheckControllerTriggerAsync().GetAwaiter().GetResult();
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected controller trigger to be skipped while streaming");
                Assert(actions.Calls.Count == 0, "Expected no display switches while streaming");

                ctrl.CheckIdleAsync().GetAwaiter().GetResult();
                Assert(actions.Calls.Count == 0, "Expected idle check to be skipped while streaming");
            }
        }

        private static void TestCustomThirdProfileAndDefaultRevert()
        {
            AgentConfig cfg = new AgentConfig();
            cfg.IdleRevertMinutes = 5;
            cfg.Profiles = new List<EnvironmentConfig>
            {
                new EnvironmentConfig
                {
                    Id = "desk",
                    Name = "Desk",
                    IsDefault = true,
                    Monitor = "DeskMonitor",
                    Audio = "DeskAudio",
                    Width = 2560,
                    Height = 1440,
                    RefreshRate = 144,
                    ConsoleEnvironmentMode = "off"
                },
                new EnvironmentConfig
                {
                    Id = "projector",
                    Name = "Projector",
                    IsDefault = false,
                    Monitor = "Epson",
                    Audio = "AVR",
                    Width = 3840,
                    Height = 2160,
                    RefreshRate = 60,
                    ConsoleEnvironmentMode = "minimize-all",
                    Integrations = new Dictionary<string, Dictionary<string, object>>
                    {
                        { "steam", new Dictionary<string, object> { { "bigPicture", true } } }
                    }
                }
            };
            cfg.ControllerTriggerProfileId = "projector";
            cfg.ApplyDefaultsAndNormalize();

            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor { CurrentIdle = TimeSpan.FromMinutes(10) };
            FakeControllerMonitor controllerMonitor = new FakeControllerMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, controllerMonitor, new FakeLogger()))
            {
                Assert(ctrl.ActiveProfileId == "desk", "Expected initial profile to be default 'desk'");

                controllerMonitor.NextNewlyConnected.Add("USB\\VID_045E&PID_02FE\\1");
                ctrl.CheckControllerTriggerAsync().GetAwaiter().GetResult();

                Assert(ctrl.ActiveProfileId == "projector", "Expected controller trigger to switch to 'projector'");
                Assert(actions.Calls.Contains("display:Epson@3840x2160:60:hdr=True"), "Expected projector display switch");
                Assert(actions.Calls.Contains("steam:open"), "Expected Steam Big Picture on projector profile");

                idle.CurrentIdle = TimeSpan.FromMinutes(10);
                ctrl.CheckIdleAsync().GetAwaiter().GetResult();
                Assert(ctrl.ActiveProfileId == "desk", "Expected idle revert to return to default 'desk' profile");
                Assert(actions.Calls.Contains("display:DeskMonitor@2560x1440:144:hdr=True"), "Expected desk display switch on revert");
            }
        }

        private static void TestModularIntegrationCanBeDisabledGlobally()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.Integrations["steam"] = new Dictionary<string, object> { { "enabled", false } };
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.SwitchToProfileAsync("living-room", true).GetAwaiter().GetResult();
                Assert(!actions.Calls.Contains("steam:open"), "Expected Steam Big Picture to be skipped when steam integration is disabled");

                ctrl.SwitchToProfileAsync("office", true).GetAwaiter().GetResult();
                Assert(!actions.Calls.Contains("steam:close:exit-bigpicture"), "Expected Steam close to be skipped when steam integration is disabled");
            }
        }

        private static void TestProfilesConfigSaveAndReloadRoundTrip()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "roomagent_profiles_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                AgentConfig cfg = new AgentConfig();
                cfg.Profiles.Add(new EnvironmentConfig
                {
                    Id = "sim-rig",
                    Name = "Sim Rig",
                    Monitor = "TripleScreen",
                    Audio = "SimAudio",
                    Width = 5760,
                    Height = 1080,
                    RefreshRate = 120,
                    ConsoleEnvironmentMode = "off"
                });
                cfg.ApplyDefaultsAndNormalize();
                cfg.Save(tempPath);

                AgentConfig reloaded = AgentConfig.Load(tempPath);
                Assert(reloaded.Profiles.Count == 3, "Expected 3 profiles after reload, got " + reloaded.Profiles.Count);
                EnvironmentConfig sim = reloaded.GetProfile("sim-rig");
                Assert(sim != null, "Expected sim-rig profile after reload");
                Assert(sim.Monitor == "TripleScreen", "Expected TripleScreen monitor");
                Assert(sim.Width == 5760 && sim.RefreshRate == 120, "Expected resolution/refresh preserved");
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }

        private static void TestCloakedWindowFilteringForOpenWindows()
        {
            Assert(!NativeInterop.ShouldSkipCloakedWindow(0, false), "Uncloaked window should not be skipped");
            Assert(!NativeInterop.ShouldSkipCloakedWindow(0, true), "Uncloaked window should not be skipped with includeShellCloaked");

            Assert(NativeInterop.ShouldSkipCloakedWindow(NativeInterop.DWM_CLOAKED_SHELL, false),
                "Shell-cloaked window should be skipped when includeShellCloaked is false");
            Assert(!NativeInterop.ShouldSkipCloakedWindow(NativeInterop.DWM_CLOAKED_SHELL, true),
                "Shell-cloaked window (e.g. Xbox mode / inactive virtual desktop) should be included when includeShellCloaked is true");

            Assert(NativeInterop.ShouldSkipCloakedWindow(NativeInterop.DWM_CLOAKED_APP, false),
                "App-cloaked window should be skipped");
            Assert(NativeInterop.ShouldSkipCloakedWindow(NativeInterop.DWM_CLOAKED_APP, true),
                "App-cloaked window should still be skipped when includeShellCloaked is true");

            Assert(NativeInterop.ShouldSkipCloakedWindow(NativeInterop.DWM_CLOAKED_INHERITED, true),
                "Inherited-cloaked window should still be skipped when includeShellCloaked is true");
            Assert(NativeInterop.ShouldSkipCloakedWindow(NativeInterop.DWM_CLOAKED_APP | NativeInterop.DWM_CLOAKED_SHELL, true),
                "App+Shell cloaked window should still be skipped when includeShellCloaked is true");
        }

        private static void TestRunIntegrationsOnConsoleEnvironmentSwitch()
        {
            AgentConfig cfgDisabled = CreateTestConfig();
            cfgDisabled.RunIntegrationsOnConsoleEnvironmentSwitch = false;
            cfgDisabled.Office.Integrations = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "appLauncher",
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "launch", new[] { "steam://open/bigpicture" } }
                    }
                }
            };
            FakeSystemActions actionsDisabled = new FakeSystemActions();
            using (RoomController ctrl = new RoomController(cfgDisabled, actionsDisabled, new FakeIdleMonitor(), new FakeLogger()))
            {
                ctrl.SetConsoleEnvironmentModeAsync("xbox").GetAwaiter().GetResult();
                Assert(actionsDisabled.Calls.Contains("xbox:enter"), "Expected xbox:enter");
                Assert(!actionsDisabled.Calls.Contains("app:launch:steam://open/bigpicture"), "Did not expect app:launch when RunIntegrationsOnConsoleEnvironmentSwitch is false");

                actionsDisabled.Calls.Clear();
                ctrl.SetConsoleEnvironmentModeAsync("off").GetAwaiter().GetResult();
                Assert(actionsDisabled.Calls.Contains("xbox:exit"), "Expected xbox:exit");
                Assert(!actionsDisabled.Calls.Contains("steam:close:exit-bigpicture"), "Did not expect steam:close when RunIntegrationsOnConsoleEnvironmentSwitch is false");
            }

            AgentConfig cfgEnabled = CreateTestConfig();
            cfgEnabled.RunIntegrationsOnConsoleEnvironmentSwitch = true;
            cfgEnabled.Office.Integrations = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "appLauncher",
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "launch", new[] { "steam://open/bigpicture" } }
                    }
                }
            };
            FakeSystemActions actionsEnabled = new FakeSystemActions();
            using (RoomController ctrl = new RoomController(cfgEnabled, actionsEnabled, new FakeIdleMonitor(), new FakeLogger()))
            {
                ctrl.SetConsoleEnvironmentModeAsync("xbox").GetAwaiter().GetResult();
                int xboxIdx = actionsEnabled.Calls.IndexOf("xbox:enter");
                int appLaunchIdx = actionsEnabled.Calls.IndexOf("app:launch:steam://open/bigpicture");
                Assert(xboxIdx >= 0 && appLaunchIdx > xboxIdx, "Expected app:launch after xbox:enter when RunIntegrationsOnConsoleEnvironmentSwitch is true");

                actionsEnabled.Calls.Clear();
                ctrl.SetConsoleEnvironmentModeAsync("off").GetAwaiter().GetResult();
                Assert(actionsEnabled.Calls.Contains("xbox:exit"), "Expected xbox:exit");
                Assert(actionsEnabled.Calls.Contains("steam:close:exit-bigpicture"), "Expected steam:close when returning to off");
                Assert(actionsEnabled.Calls.Contains("steam:close-window"), "Expected steam:close-window when returning to off");
            }
        }

        private static void TestDefaultPortAndConfigReloadEvent()
        {
            AgentConfig defaults = new AgentConfig();
            Assert(defaults.Port == 8090, "Expected default Port to be 8090");

            AgentConfig outOfRange = new AgentConfig { Port = 70000 };
            outOfRange.ApplyDefaultsAndNormalize();
            Assert(outOfRange.Port == 8090, "Expected out-of-range Port to normalize to 8090");

            string tempConfigPath = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempConfigPath, @"{ ""Port"": 8090 }");
                AgentConfig cfg = AgentConfig.Load(tempConfigPath);
                Assert(cfg.Port == 8090, "Expected loaded Port 8090");

                using (RoomController ctrl = new RoomController(cfg, new FakeSystemActions(), new FakeIdleMonitor(), new FakeLogger()))
                {
                    int reloadEvents = 0;
                    ctrl.ConfigReloaded += delegate { reloadEvents++; };

                    System.IO.File.WriteAllText(tempConfigPath, @"{ ""Port"": 8095 }");
                    string rawJson = System.IO.File.ReadAllText(tempConfigPath);
                    AgentConfig reloaded = AgentConfig.Load(tempConfigPath);
                    ctrl.ReloadConfigAsync(reloaded, rawJson, save: false).GetAwaiter().GetResult();

                    Assert(cfg.Port == 8095, "Expected live config Port to update to 8095");
                    Assert(reloadEvents == 1, "Expected ConfigReloaded event to fire once");
                }
            }
            finally
            {
                if (System.IO.File.Exists(tempConfigPath))
                {
                    System.IO.File.Delete(tempConfigPath);
                }
            }
        }

        private static void TestNetworkAddressHelperFormatting()
        {
            WebAccessAddresses withDomain = NetworkAddressHelper.BuildWebAccessAddresses(
                8090,
                "gaming-pc",
                "home.arpa",
                new[] { "192.168.1.50", "10.0.0.5", "192.168.1.50" });
            Assert(withDomain.PrimaryDnsUrl == "http://gaming-pc:8090", "Expected primary DNS URL");
            Assert(withDomain.DnsSummary == "http://gaming-pc:8090, http://gaming-pc.home.arpa:8090", "Expected DNS summary with FQDN");
            Assert(withDomain.PrimaryIpUrl == "http://192.168.1.50:8090", "Expected primary IP URL");
            Assert(withDomain.IpSummary == "http://192.168.1.50:8090, http://10.0.0.5:8090", "Expected deduplicated IP summary");

            WebAccessAddresses port80 = NetworkAddressHelper.BuildWebAccessAddresses(
                80,
                "gaming-pc.lan",
                "lan",
                new[] { "192.168.1.50" });
            Assert(port80.PrimaryDnsUrl == "http://gaming-pc.lan", "Expected port 80 omitted from DNS URL");
            Assert(port80.DnsSummary == "http://gaming-pc.lan", "Expected no duplicate domain suffix");
            Assert(port80.PrimaryIpUrl == "http://192.168.1.50", "Expected port 80 omitted from IP URL");

            WebAccessAddresses fallback = NetworkAddressHelper.BuildWebAccessAddresses(0, "", "", null);
            Assert(fallback.PrimaryDnsUrl == "http://localhost:8090", "Expected localhost fallback");
            Assert(fallback.PrimaryIpUrl == "http://127.0.0.1:8090", "Expected loopback IP fallback");

            Assert(NetworkAddressHelper.FormatMacAddress(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }) == "AA-BB-CC-DD-EE-FF",
                "Expected uppercase hyphenated MAC address");
            Assert(NetworkAddressHelper.FormatMacAddress(new byte[6]) == "",
                "Expected all-zero MAC address rejected");
            Assert(NetworkAddressHelper.ComputeBroadcastIp(
                System.Net.IPAddress.Parse("192.168.1.42"),
                System.Net.IPAddress.Parse("255.255.255.0")) == "192.168.1.255",
                "Expected /24 directed broadcast IP");
            Assert(NetworkAddressHelper.ComputeBroadcastIp(
                System.Net.IPAddress.Parse("10.0.12.34"),
                System.Net.IPAddress.Parse("255.255.252.0")) == "10.0.15.255",
                "Expected /22 directed broadcast IP");
        }

        private static void TestAppDiscoveryNormalizationMergeAndFilter()
        {
            Assert(AppDiscovery.NormalizeProcessName("  \"C:\\Apps\\Discord.exe\" ") == "Discord", "Expected exe path normalized to process name");
            Assert(AppDiscovery.NormalizeProcessName("Telegram.exe") == "Telegram", "Expected .exe suffix stripped");
            Assert(AppDiscovery.NormalizeProcessName("Spotify") == "Spotify", "Expected plain process name preserved");
            Assert(AppDiscovery.NormalizeProcessName("   ") == "", "Expected whitespace normalized to empty string");

            List<DiscoveredApp> running = new List<DiscoveredApp>
            {
                new DiscoveredApp { ProcessName = "Discord.exe", DisplayName = "Discord", IsRunning = true, Detail = "Friends" },
                new DiscoveredApp { ProcessName = "notepad", DisplayName = "Notepad", IsRunning = true, Detail = "notes.txt" }
            };
            List<DiscoveredApp> installed = new List<DiscoveredApp>
            {
                new DiscoveredApp { ProcessName = "discord", DisplayName = "Discord Desktop", ExecutablePath = @"C:\Apps\Discord\Discord.exe", IsInstalled = true },
                new DiscoveredApp { ProcessName = "Spotify", DisplayName = "Spotify Music", ExecutablePath = @"C:\Apps\Spotify\Spotify.exe", IsInstalled = true }
            };

            List<DiscoveredApp> merged = AppDiscovery.MergeApps(running, installed);
            Assert(merged.Count == 3, "Expected 3 deduplicated apps after merge");
            Assert(merged[0].IsRunning && merged[1].IsRunning && !merged[2].IsRunning, "Expected running apps sorted first");

            DiscoveredApp discord = merged.Find(a => string.Equals(a.ProcessName, "Discord", StringComparison.OrdinalIgnoreCase));
            Assert(discord != null && discord.IsRunning && discord.IsInstalled, "Expected Discord marked both running and installed");
            Assert(discord.SourceLabel == "Running & Installed", "Expected combined source label");
            Assert(discord.ExecutablePath == @"C:\Apps\Discord\Discord.exe", "Expected ExecutablePath preserved from installed entry when running entry lacked one");

            List<DiscoveredApp> runningOnly = AppDiscovery.FilterApps(merged, "", "Running");
            Assert(runningOnly.Count == 2, "Expected 2 running apps");

            List<DiscoveredApp> installedOnly = AppDiscovery.FilterApps(merged, "", "Installed");
            Assert(installedOnly.Count == 2, "Expected 2 installed apps");

            List<DiscoveredApp> queryFiltered = AppDiscovery.FilterApps(merged, "notes", "All");
            Assert(queryFiltered.Count == 1 && queryFiltered[0].ProcessName == "notepad", "Expected detail query match for notepad");
        }

        private static void TestShortcutIconOmitsOverlay()
        {
            string notepadPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            Assert(System.IO.File.Exists(notepadPath), "Expected notepad.exe to exist");

            string lnkPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "room-agent-shortcut-icon-test-" + Guid.NewGuid().ToString("N") + ".lnk");
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                Assert(shellType != null, "Expected WScript.Shell COM type");
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { lnkPath });
                shortcut.GetType().InvokeMember(
                    "TargetPath",
                    System.Reflection.BindingFlags.SetProperty,
                    null,
                    shortcut,
                    new object[] { notepadPath });
                shortcut.GetType().InvokeMember(
                    "Save",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shortcut,
                    null);

                string exeIcon = NativeInterop.ExtractShellFileIconDataUrl(notepadPath);
                string shortcutIcon = NativeInterop.ExtractShortcutIconDataUrl(lnkPath);
                Assert(!string.IsNullOrEmpty(exeIcon) && exeIcon.StartsWith("data:image/png;base64,", StringComparison.Ordinal), "Expected PNG data URL for exe icon");
                Assert(shortcutIcon == exeIcon, "Expected .lnk shortcut icon to match target exe icon without shortcut overlay");
            }
            finally
            {
                if (shortcut != null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                }
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                }
                try
                {
                    if (System.IO.File.Exists(lnkPath)) System.IO.File.Delete(lnkPath);
                }
                catch { }
            }
        }

        private static void TestWindowPositionsSavedAndRestoredAcrossConsoleEnvironmentModes()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();

            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                // Leaving normal desktop mode ("off") -> "virtual-desktop" saves positions once.
                ctrl.SetConsoleEnvironmentModeAsync("virtual-desktop").GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("windows:save"), "Expected windows:save when leaving normal desktop mode");
                Assert(!actions.Calls.Contains("windows:restore"), "Did not expect windows:restore when entering virtual-desktop");
                actions.Calls.Clear();

                // Switching between non-normal modes ("virtual-desktop" -> "xbox") must not overwrite or restore yet.
                ctrl.SetConsoleEnvironmentModeAsync("xbox").GetAwaiter().GetResult();
                Assert(!actions.Calls.Contains("windows:save"), "Did not expect windows:save when already outside normal desktop mode");
                Assert(!actions.Calls.Contains("windows:restore"), "Did not expect windows:restore when switching to xbox mode");
                actions.Calls.Clear();

                // Returning to normal desktop mode ("off") restores saved window positions after console environment revert.
                ctrl.SetConsoleEnvironmentModeAsync("off").GetAwaiter().GetResult();
                int xboxExitIdx = actions.Calls.IndexOf("xbox:exit");
                int restoreIdx = actions.Calls.IndexOf("windows:restore");
                Assert(xboxExitIdx >= 0, "Expected xbox:exit when returning to off");
                Assert(restoreIdx > xboxExitIdx, "Expected windows:restore after xbox:exit");
                Assert(!actions.Calls.Contains("windows:save"), "Did not expect windows:save when returning to normal desktop mode");
            }
        }

        private static void TestWindowsSystemActionsPersistsSavedWindowPlacements()
        {
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RoomAgentPlacementTest_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tempDir);
            string statePath = System.IO.Path.Combine(tempDir, "console-environment-state.json");
            try
            {
                System.IO.File.WriteAllText(
                    statePath,
                    "{\"SavedWindowPlacements\":[{\"Hwnd\":123456,\"Pid\":999,\"ShowCmd\":1,\"NormalLeft\":10,\"NormalTop\":20,\"NormalRight\":810,\"NormalBottom\":620,\"HasWindowRect\":true,\"RectLeft\":10,\"RectTop\":20,\"RectRight\":810,\"RectBottom\":620}]}");

                AgentConfig cfg = CreateTestConfig();
                WindowsSystemActions actions = new WindowsSystemActions(cfg, new FakeLogger(), statePath);
                Assert(actions.HasPendingConsoleEnvironmentRevert(), "Expected persisted SavedWindowPlacements to mark pending console environment revert");

                actions.RestoreWindowPositions();
                Assert(!actions.HasPendingConsoleEnvironmentRevert(), "Expected RestoreWindowPositions to clear pending saved placements");
                Assert(!System.IO.File.Exists(statePath), "Expected state file to be deleted once all pending state is cleared");
            }
            finally
            {
                try
                {
                    if (System.IO.Directory.Exists(tempDir))
                    {
                        System.IO.Directory.Delete(tempDir, true);
                    }
                }
                catch { }
            }
        }

        private static void TestAppLauncherMixedEntriesAndSerialization()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "roomagent_applauncher_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                EnvironmentConfig profile = new EnvironmentConfig
                {
                    Id = "office",
                    Name = "Office",
                    IsDefault = true,
                    Monitor = "M1"
                };

                LaunchAppEntry[] initial = new[]
                {
                    new LaunchAppEntry { Path = "steam://open/bigpicture", CloseWindowWhenReady = false },
                    new LaunchAppEntry { Path = @"C:\Program Files\Hue Sync\HueSync.exe", CloseWindowWhenReady = true, WindowTitle = "Philips Hue Sync" }
                };

                object[] serialized = AppLauncherIntegration.SerializeLaunchEntries(initial);
                Assert(serialized.Length == 2, "Expected 2 serialized entries");
                Assert(serialized[0] is string && (string)serialized[0] == "steam://open/bigpicture", "Expected plain string for simple launch entry");
                Assert(serialized[1] is Dictionary<string, object>, "Expected object dictionary for closeWindowWhenReady entry");

                profile.Integrations["appLauncher"] = new Dictionary<string, object>
                {
                    { "launch", serialized }
                };

                AgentConfig cfg = new AgentConfig
                {
                    Profiles = new List<EnvironmentConfig> { profile }
                };
                cfg.ApplyDefaultsAndNormalize();
                cfg.Save(tempPath);

                AgentConfig reloaded = AgentConfig.Load(tempPath);
                EnvironmentConfig reloadedProfile = reloaded.GetProfile("office");
                LaunchAppEntry[] parsed = AppLauncherIntegration.GetLaunchEntries(reloadedProfile);

                Assert(parsed.Length == 2, "Expected 2 parsed entries after reload, got " + parsed.Length);
                Assert(parsed[0].Path == "steam://open/bigpicture" && !parsed[0].CloseWindowWhenReady, "Expected first entry to round-trip as plain launch");
                Assert(parsed[1].Path == @"C:\Program Files\Hue Sync\HueSync.exe", "Expected second entry path to round-trip");
                Assert(parsed[1].CloseWindowWhenReady, "Expected second entry CloseWindowWhenReady to be true");
                Assert(parsed[1].WindowTitle == "Philips Hue Sync", "Expected second entry WindowTitle to round-trip");
            }
            finally
            {
                try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); } catch { }
            }
        }

        private static byte[] BuildTestShortcutsVdf(uint appId, string appName, string exe, string startDir)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                void WriteCString(string s)
                {
                    byte[] b = System.Text.Encoding.UTF8.GetBytes(s ?? "");
                    ms.Write(b, 0, b.Length);
                    ms.WriteByte(0x00);
                }

                ms.WriteByte(0x00); WriteCString("shortcuts");
                ms.WriteByte(0x00); WriteCString("0");

                ms.WriteByte(0x02); WriteCString("appid");
                byte[] idBytes = BitConverter.GetBytes(unchecked((int)appId));
                ms.Write(idBytes, 0, 4);

                ms.WriteByte(0x01); WriteCString("AppName"); WriteCString(appName);
                ms.WriteByte(0x01); WriteCString("Exe"); WriteCString(exe);
                ms.WriteByte(0x01); WriteCString("StartDir"); WriteCString(startDir);

                ms.WriteByte(0x08); // end entry 0
                ms.WriteByte(0x08); // end shortcuts
                ms.WriteByte(0x08); // end root
                return ms.ToArray();
            }
        }

        private static void TestSteamHeroResolverSteamAndNonSteam()
        {
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cerberus_steamhero_" + Guid.NewGuid().ToString("N"));
            try
            {
                string steamRoot = System.IO.Path.Combine(tempDir, "Steam");
                string secondLib = System.IO.Path.Combine(tempDir, "D_SteamLibrary");
                string steamApps = System.IO.Path.Combine(steamRoot, "steamapps");
                string secondSteamApps = System.IO.Path.Combine(secondLib, "steamapps");
                string gameDir = System.IO.Path.Combine(secondSteamApps, "common", "TestSteamGame", "Binaries");
                string libCacheAppDir = System.IO.Path.Combine(steamRoot, "appcache", "librarycache", "123456");
                string userConfigDir = System.IO.Path.Combine(steamRoot, "userdata", "22856567", "config");
                string gridDir = System.IO.Path.Combine(userConfigDir, "grid");

                System.IO.Directory.CreateDirectory(steamApps);
                System.IO.Directory.CreateDirectory(gameDir);
                System.IO.Directory.CreateDirectory(libCacheAppDir);
                System.IO.Directory.CreateDirectory(gridDir);

                string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steamRoot.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + secondLib.Replace("\\", "\\\\") + "\"\n\t}\n}\n";
                System.IO.File.WriteAllText(System.IO.Path.Combine(steamApps, "libraryfolders.vdf"), vdf);

                string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"123456\"\n\t\"name\"\t\t\"Test Steam Game\"\n\t\"installdir\"\t\t\"TestSteamGame\"\n}\n";
                System.IO.File.WriteAllText(System.IO.Path.Combine(secondSteamApps, "appmanifest_123456.acf"), acf);

                byte[] steamHeroBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
                string steamHeroPath = System.IO.Path.Combine(libCacheAppDir, "library_hero.jpg");
                System.IO.File.WriteAllBytes(steamHeroPath, steamHeroBytes);

                // Non-Steam shortcut with unsigned 32-bit appid (> int.MaxValue)
                uint nonSteamAppId = 2157451464u;
                string nonSteamDir = System.IO.Path.Combine(tempDir, "Games", "MyNonSteam");
                System.IO.Directory.CreateDirectory(nonSteamDir);
                string nonSteamExe = System.IO.Path.Combine(nonSteamDir, "MyGame.exe");
                string nonSteamChildExe = System.IO.Path.Combine(nonSteamDir, "MyGame-Win64-Shipping.exe");
                byte[] shortcutsBytes = BuildTestShortcutsVdf(nonSteamAppId, "My Non-Steam Game", "\"" + nonSteamExe + "\"", "\"" + nonSteamDir + "\"");
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(userConfigDir, "shortcuts.vdf"), shortcutsBytes);

                byte[] nonSteamHeroBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
                string nonSteamHeroPath = System.IO.Path.Combine(gridDir, nonSteamAppId + "_hero.png");
                System.IO.File.WriteAllBytes(nonSteamHeroPath, nonSteamHeroBytes);

                SteamHeroResolver resolver = new SteamHeroResolver(null, steamRoot);

                // 1. Official Steam game matched by installdir in secondary library
                string steamExe = System.IO.Path.Combine(gameDir, "Game.exe");
                string resolvedSteamUrl = resolver.ResolveHeroUrl(steamExe, "Game");
                Assert(!string.IsNullOrEmpty(resolvedSteamUrl) && resolvedSteamUrl.StartsWith("/api/frame-app-hero?key="), "Expected Steam hero URL");
                string steamKey = Uri.UnescapeDataString(resolvedSteamUrl.Substring("/api/frame-app-hero?key=".Length));
                byte[] outBytes;
                string outType;
                Assert(resolver.TryGetHeroImage(steamKey, out outBytes, out outType), "Expected TryGetHeroImage to succeed for Steam game");
                Assert(outType == "image/jpeg" && outBytes.Length == steamHeroBytes.Length, "Expected JPEG bytes for Steam hero");

                // 2. Non-Steam game matched by exact Exe
                string resolvedNonSteamUrl = resolver.ResolveHeroUrl(nonSteamExe, "MyGame");
                Assert(!string.IsNullOrEmpty(resolvedNonSteamUrl), "Expected non-Steam hero URL by exact Exe");
                string nonSteamKey = Uri.UnescapeDataString(resolvedNonSteamUrl.Substring("/api/frame-app-hero?key=".Length));
                Assert(resolver.TryGetHeroImage(nonSteamKey, out outBytes, out outType), "Expected TryGetHeroImage to succeed for non-Steam game");
                Assert(outType == "image/png" && outBytes.Length == nonSteamHeroBytes.Length, "Expected PNG bytes for non-Steam hero");

                // 3. Non-Steam game matched by StartDir when child exe differs from shortcut Exe
                string resolvedChildPath = resolver.ResolveHeroFilePath(nonSteamChildExe, "UnrealGame");
                Assert(string.Equals(resolvedChildPath, nonSteamHeroPath, StringComparison.OrdinalIgnoreCase), "Expected non-Steam hero by StartDir match");

                // 4. Custom grid hero overrides librarycache for official Steam game
                string customSteamHeroPath = System.IO.Path.Combine(gridDir, "123456_hero.png");
                System.IO.File.WriteAllBytes(customSteamHeroPath, nonSteamHeroBytes);
                string resolvedCustomSteam = resolver.ResolveHeroFilePath(steamExe, "Game");
                Assert(string.Equals(resolvedCustomSteam, customSteamHeroPath, StringComparison.OrdinalIgnoreCase), "Expected custom grid hero to override librarycache");
            }
            finally
            {
                try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void TestAuthServicePasswordAndSignedTokens()
        {
            AgentConfig cfg = new AgentConfig();
            Assert(!AuthService.IsAuthEnabled(cfg), "Auth should be disabled by default");
            Assert(AuthService.VerifyToken(cfg, ""), "VerifyToken should pass when auth is disabled");

            AuthService.SetPassword(cfg, "s3cret-pass");
            Assert(AuthService.IsAuthEnabled(cfg), "Auth should be enabled after SetPassword");
            Assert(AuthService.VerifyPassword(cfg, "s3cret-pass"), "Correct password should verify");
            Assert(!AuthService.VerifyPassword(cfg, "wrong-pass"), "Wrong password should fail");
            Assert(!AuthService.VerifyPassword(cfg, ""), "Empty password should fail");

            string token = AuthService.IssueToken(cfg);
            Assert(!string.IsNullOrWhiteSpace(token), "Issued token should not be empty");
            Assert(AuthService.VerifyToken(cfg, token), "Issued token should verify");
            Assert(!AuthService.VerifyToken(cfg, token + "x"), "Tampered token should fail");
            Assert(!AuthService.VerifyToken(cfg, ""), "Empty token should fail when auth is enabled");

            AuthService.RotateTokenSecret(cfg);
            Assert(!AuthService.VerifyToken(cfg, token), "Rotating secret should invalidate old tokens");
            string newToken = AuthService.IssueToken(cfg);
            Assert(AuthService.VerifyToken(cfg, newToken), "Token issued after rotation should verify");

            AuthService auth = new AuthService();
            int retryAfter;
            for (int i = 0; i < 5; i++)
            {
                Assert(auth.IsLoginAllowed("192.168.1.50", out retryAfter), "Login should be allowed before max failures");
                auth.RecordLoginFailure("192.168.1.50");
            }
            Assert(!auth.IsLoginAllowed("192.168.1.50", out retryAfter) && retryAfter > 0, "Login should be locked out after 5 failures");
            auth.ResetLoginFailures("192.168.1.50");
            Assert(auth.IsLoginAllowed("192.168.1.50", out retryAfter), "ResetLoginFailures should clear lockout");

            AuthService.ClearPassword(cfg);
            Assert(!AuthService.IsAuthEnabled(cfg), "ClearPassword should disable auth");
        }

        private static void TestStreamingConsoleEnvironmentAutoDetectStartAndEnd()
        {
            AgentConfig cfg = new AgentConfig
            {
                DisplaySettleDelayMs = 0,
                StreamingConsoleEnvironmentMode = "virtual-desktop",
                StreamingAutoDetectEnabled = true,
                StreamingRevertConsoleEnvironmentOnEnd = true
            };
            cfg.Office.ConsoleEnvironmentMode = "off";
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController controller = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                Assert(controller.ActiveConsoleEnvironmentMode == "off", "Should start in off mode");

                // Not streaming yet -> no-op
                controller.CheckStreamingStateAsync().GetAwaiter().GetResult();
                Assert(actions.Calls.Count == 0, "No actions expected when not streaming");

                // Stream starts -> enters virtual-desktop without stopping stream
                actions.StreamingActive = true;
                controller.CheckStreamingStateAsync().GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "virtual-desktop", "Expected virtual-desktop on stream start");
                Assert(actions.Calls.Contains("windows:save"), "Expected window positions saved");
                Assert(actions.Calls.Contains("vdesktop:new"), "Expected virtual desktop created");
                Assert(actions.Calls.Contains("icons:hide"), "Expected desktop icons hidden");
                Assert(actions.Calls.Contains("taskbar:hide"), "Expected taskbar hidden");
                Assert(!actions.Calls.Contains("streaming:stop"), "Must not stop stream when entering streaming console environment");

                // Subsequent poll while still streaming -> no-op
                int callsBeforeSecondPoll = actions.Calls.Count;
                controller.CheckStreamingStateAsync().GetAwaiter().GetResult();
                Assert(actions.Calls.Count == callsBeforeSecondPoll, "Repeated poll while streaming should be a no-op");

                // Stream ends -> reverts back to office's off mode
                actions.StreamingActive = false;
                controller.CheckStreamingStateAsync().GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "off", "Expected revert to off when stream ends");
                Assert(actions.Calls.Contains("taskbar:show"), "Expected taskbar restored");
                Assert(actions.Calls.Contains("icons:show"), "Expected icons restored");
                Assert(actions.Calls.Contains("vdesktop:revert"), "Expected virtual desktop reverted");
                Assert(actions.Calls.Contains("windows:restore"), "Expected window positions restored");
            }
        }

        private static void TestStreamingConsoleEnvironmentApiStartOverrideAndNoDoubleTrigger()
        {
            AgentConfig cfg = new AgentConfig
            {
                DisplaySettleDelayMs = 0,
                StreamingConsoleEnvironmentMode = "virtual-desktop",
                StreamingAutoDetectEnabled = true,
                StreamingRevertConsoleEnvironmentOnEnd = true
            };
            cfg.Office.ConsoleEnvironmentMode = "off";
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController controller = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                controller.StartStreamingEnvironmentAsync("xbox", "api").GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "xbox", "Expected API override to enter xbox mode");
                Assert(actions.Calls.Contains("xbox:enter"), "Expected xbox:enter");

                // Poller sees stream active afterwards -> should not overwrite xbox with virtual-desktop
                actions.StreamingActive = true;
                int countAfterApi = actions.Calls.Count;
                controller.CheckStreamingStateAsync().GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "xbox", "Poller should not overwrite explicit API override");
                Assert(actions.Calls.Count == countAfterApi, "Poller should not re-trigger after API start");

                controller.EndStreamingEnvironmentAsync("api").GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "off", "Expected end hook to revert back to off");
                Assert(actions.Calls.Contains("xbox:exit"), "Expected xbox:exit");
                Assert(actions.Calls.Contains("windows:restore"), "Expected windows:restore");
            }
        }

        private static void TestStreamingConsoleEnvironmentRevertDisabledKeepsMode()
        {
            AgentConfig cfg = new AgentConfig
            {
                DisplaySettleDelayMs = 0,
                StreamingConsoleEnvironmentMode = "minimize-all",
                StreamingAutoDetectEnabled = true,
                StreamingRevertConsoleEnvironmentOnEnd = false
            };
            cfg.Office.ConsoleEnvironmentMode = "off";
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController controller = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                actions.StreamingActive = true;
                controller.CheckStreamingStateAsync().GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "minimize-all", "Expected minimize-all on stream start");

                actions.StreamingActive = false;
                controller.CheckStreamingStateAsync().GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "minimize-all", "Expected minimize-all to remain when revert-on-end is false");
                Assert(!actions.Calls.Contains("minimize-all:restore"), "Should not restore minimized windows when revert-on-end is false");
            }
        }

        private static void TestStreamingConsoleEnvironmentInheritAndStopStreamingRevert()
        {
            Assert(AgentConfig.NormalizeStreamingConsoleEnvironmentMode("inherit") == "", "Expected inherit to normalize to empty");
            Assert(AgentConfig.NormalizeStreamingConsoleEnvironmentMode("unchanged") == "", "Expected unchanged to normalize to empty");
            Assert(AgentConfig.NormalizeStreamingConsoleEnvironmentMode("xbox") == "xbox", "Expected xbox to normalize to xbox");

            AgentConfig cfg = new AgentConfig
            {
                DisplaySettleDelayMs = 0,
                StreamingConsoleEnvironmentMode = "inherit",
                StreamingAutoDetectEnabled = true,
                StreamingRevertConsoleEnvironmentOnEnd = true
            };
            cfg.Office.ConsoleEnvironmentMode = "off";
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController controller = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                actions.StreamingActive = true;
                controller.CheckStreamingStateAsync().GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "off", "Inherit should leave ActiveConsoleEnvironmentMode unchanged");

                // Explicit API override followed by StopStreamingAsync should stop stream and revert console environment
                controller.StartStreamingEnvironmentAsync("virtual-desktop", "api").GetAwaiter().GetResult();
                Assert(controller.ActiveConsoleEnvironmentMode == "virtual-desktop", "Expected virtual-desktop override");
                controller.StopStreamingAsync().GetAwaiter().GetResult();
                Assert(actions.Calls.Contains("streaming:stop"), "Expected StopStreamingAsync to stop stream");
                Assert(controller.ActiveConsoleEnvironmentMode == "off", "Expected StopStreamingAsync to revert console override");
            }
        }

        private static void TestUpdateServiceVersioningAndManifest()
        {
            Assert(AgentConfig.NormalizeUpdateChannel(null) == "auto", "Null update channel should normalize to auto");
            Assert(AgentConfig.NormalizeUpdateChannel("ALPHA") == "alpha", "ALPHA should normalize to alpha");
            Assert(AgentConfig.NormalizeUpdateChannel("unknown") == "auto", "Unknown channel should normalize to auto");

            Assert(UpdateService.ResolveEffectiveChannel("auto", "1.0.0-alpha.20261128-abc1234") == "alpha", "Auto should resolve alpha");
            Assert(UpdateService.ResolveEffectiveChannel("auto", "1.0.0-beta.1") == "beta", "Auto should resolve beta");
            Assert(UpdateService.ResolveEffectiveChannel("auto", "1.0.0") == "stable", "Auto should resolve stable");
            Assert(UpdateService.ResolveEffectiveChannel("stable", "1.0.0-alpha.20261128-abc1234") == "stable", "Explicit channel should override auto");

            Assert(UpdateService.CompareVersions("1.0.1", "1.0.0") > 0, "1.0.1 > 1.0.0");
            Assert(UpdateService.CompareVersions("1.0.0", "1.0.0-beta.1") > 0, "Stable > beta");
            Assert(UpdateService.CompareVersions("1.0.0-beta.2", "1.0.0-alpha.20261128-abc1234") > 0, "Beta > alpha");
            Assert(UpdateService.CompareVersions("1.0.0-alpha.20261129-0000001", "1.0.0-alpha.20261128-fffffff") > 0, "Newer alpha date > older alpha date");

            Assert(!UpdateService.IsUpdateAvailable("1.0.0", "1.0.0", "stable"), "Same version should not be an update");
            Assert(UpdateService.IsUpdateAvailable("1.0.0-alpha.20261128-fffffff", "1.0.0-alpha.20261128-0000001", "alpha"), "Same-day alpha SHA change should be detected as update");
            Assert(!UpdateService.IsUpdateAvailable("1.0.1", "1.0.0", "stable"), "Older stable version should not be an update");

            string manifestJson = @"{
                ""schemaVersion"": 1,
                ""channels"": {
                    ""alpha"": {
                        ""version"": ""1.0.0-alpha.20261129-1234567"",
                        ""url"": ""https://example.com/CerberusServiceSetup.exe"",
                        ""sha256"": ""abcd"",
                        ""size"": 1024
                    }
                }
            }";
            var channels = UpdateService.ParseManifest(manifestJson);
            Assert(channels.ContainsKey("alpha"), "Manifest should contain alpha channel");
            Assert(channels["alpha"].Version == "1.0.0-alpha.20261129-1234567", "Manifest alpha version mismatch");
            Assert(channels["alpha"].Sha256 == "abcd", "Manifest alpha sha256 mismatch");
        }

        private static void TestConsoleEnvironmentMovesCursorToLowerRightCorner()
        {
            string[] activeModes = new[] { "virtual-desktop", "xbox", "minimize-all" };
            for (int i = 0; i < activeModes.Length; i++)
            {
                string mode = activeModes[i];
                AgentConfig cfg = CreateTestConfig();
                cfg.LivingRoom.ConsoleEnvironmentMode = mode;
                FakeSystemActions actions = new FakeSystemActions();
                FakeIdleMonitor idle = new FakeIdleMonitor();
                using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
                {
                    ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                    Assert(actions.Calls.Contains("cursor:lower-right"), "Expected cursor move when entering " + mode);
                    int displayIdx = actions.Calls.IndexOf("display:LG TV SSCR2@3840x2160:120:hdr=True");
                    int cursorIdx = actions.Calls.IndexOf("cursor:lower-right");
                    Assert(cursorIdx > displayIdx, "Expected cursor move after display switch for " + mode);

                    actions.Calls.Clear();
                    ctrl.SetConsoleEnvironmentModeAsync("off").GetAwaiter().GetResult();
                    Assert(!actions.Calls.Contains("cursor:lower-right"), "Did not expect cursor move when setting console environment off");

                    actions.Calls.Clear();
                    ctrl.SetConsoleEnvironmentModeAsync(mode).GetAwaiter().GetResult();
                    Assert(actions.Calls.Contains("cursor:lower-right"), "Expected cursor move when switching console environment in-place to " + mode);
                }
            }
        }

        private sealed class FakeRemoteInputSender : IRemoteInputSender
        {
            public readonly List<string> Events = new List<string>();
            public bool MoveMouseRelative(int dx, int dy) { Events.Add("move:" + dx + "," + dy); return true; }
            public bool SendMouseButton(string button, string action) { Events.Add("btn:" + button + ":" + action); return true; }
            public bool SendMouseScroll(int deltaX, int deltaY) { Events.Add("scroll:" + deltaX + "," + deltaY); return true; }
            public bool SendKey(ushort vk, string action) { Events.Add("key:" + vk + ":" + action); return true; }
            public bool SendKeyCombo(ushort[] vks) { Events.Add("combo:" + string.Join("+", vks)); return true; }
            public bool SendUnicodeText(string text) { Events.Add("text:" + text); return true; }
        }

        private static void TestRemoteInputProcessorParsesAndClampsPayloads()
        {
            FakeRemoteInputSender sender = new FakeRemoteInputSender();
            string err;

            Assert(RemoteInputProcessor.ProcessJsonMessage("{\"t\":\"move\",\"dx\":5000,\"dy\":-5000}", sender, out err), "Expected move to succeed");
            Assert(sender.Events[0] == "move:2000,-2000", "Expected clamped move delta");

            Assert(RemoteInputProcessor.ProcessJsonMessage("{\"t\":\"btn\",\"b\":\"right\",\"a\":\"click\"}", sender, out err), "Expected btn to succeed");
            Assert(sender.Events[1] == "btn:right:click", "Expected right click");

            Assert(RemoteInputProcessor.ProcessJsonMessage("{\"t\":\"scroll\",\"dx\":0,\"dy\":-120}", sender, out err), "Expected scroll to succeed");
            Assert(sender.Events[2] == "scroll:0,-120", "Expected scroll delta");

            Assert(RemoteInputProcessor.ProcessJsonMessage("{\"t\":\"key\",\"vk\":13,\"a\":\"press\"}", sender, out err), "Expected Enter key to succeed");
            Assert(sender.Events[3] == "key:13:press", "Expected VK_RETURN");

            Assert(RemoteInputProcessor.ProcessJsonMessage("{\"t\":\"combo\",\"vks\":[91,71]}", sender, out err), "Expected Win+G combo to succeed");
            Assert(sender.Events[4] == "combo:91+71", "Expected Win+G vks");

            Assert(RemoteInputProcessor.ProcessJsonMessage("{\"t\":\"text\",\"s\":\"abc\"}", sender, out err), "Expected text to succeed");
            Assert(sender.Events[5] == "text:abc", "Expected text payload");

            Assert(!RemoteInputProcessor.ProcessJsonMessage("{\"t\":\"btn\",\"b\":\"invalid\"}", sender, out err), "Expected invalid button to fail");
            Assert(err == "invalid_button", "Expected invalid_button error");
        }

        private static void TestOsdModeMutuallyExclusiveTransitions()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions();
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                string mode;
                bool enabled;
                Assert(ctrl.TrySetOsdMode("radeon", out mode, out enabled), "Expected setting radeon mode to succeed");
                Assert(mode == "radeon" && enabled, "Expected radeon mode active");
                Assert(ctrl.GetOsdMode() == "radeon", "Expected GetOsdMode to return radeon");

                Assert(ctrl.TrySetOsdMode("afterburner", out mode, out enabled), "Expected switching to afterburner to succeed");
                Assert(mode == "afterburner" && enabled, "Expected afterburner mode active");

                Assert(ctrl.TrySetOsdMode("off", out mode, out enabled), "Expected switching to off to succeed");
                Assert(mode == "off" && !enabled, "Expected osd disabled when off");
            }
        }

        private static void TestPendingSwitchExecutesWhenMonitorBecomesAvailable()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions { MonitorAvailableResult = false };
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.PendingSwitchPollIntervalMs = 20;
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();

                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office while monitor is offline");
                Assert(ctrl.PendingProfileId == "living-room", "Expected pending living-room");

                actions.MonitorAvailableResult = true;
                DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                while (ctrl.CurrentMode != RoomMode.LivingRoom && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(25);
                }

                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected automatic switch to LivingRoom once monitor came online");
                Assert(string.IsNullOrEmpty(ctrl.PendingProfileId), "Expected PendingProfileId to clear after completion");
            }
        }

        private static void TestPendingDisplayReadyWaitsForHdrAndCompletes()
        {
            AgentConfig cfg = CreateTestConfig();
            cfg.VerifyRetryCount = 0;
            FakeSystemActions actions = new FakeSystemActions
            {
                MonitorAvailableResult = true,
                VerifyMonitorOnlyResult = true,
                VerifyResult = false
            };
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.PendingSwitchPollIntervalMs = 20;
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();

                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected Office until HDR/audio validation passes");
                Assert(ctrl.PendingProfileId == "living-room", "Expected pending living-room");
                Assert(ctrl.PendingSwitchReason == "waiting-for-display-ready", "Expected waiting-for-display-ready reason");
                Assert(!actions.Calls.Contains("display:Odyssey G80SD@3840x2160:240:hdr=True"), "Expected no revert to Office display while waiting for TV HDR");

                actions.VerifyResult = true;
                DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                while (ctrl.CurrentMode != RoomMode.LivingRoom && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(25);
                }

                Assert(ctrl.CurrentMode == RoomMode.LivingRoom, "Expected LivingRoom once TV HDR/audio became ready");
                Assert(string.IsNullOrEmpty(ctrl.PendingProfileId), "Expected PendingProfileId to clear");
            }
        }

        private static void TestCancelPendingSwitchStopsWaiting()
        {
            AgentConfig cfg = CreateTestConfig();
            FakeSystemActions actions = new FakeSystemActions { MonitorAvailableResult = false };
            FakeIdleMonitor idle = new FakeIdleMonitor();
            using (RoomController ctrl = new RoomController(cfg, actions, idle, new FakeLogger()))
            {
                ctrl.PendingSwitchPollIntervalMs = 20;
                ctrl.SwitchToLivingRoomAsync(true).GetAwaiter().GetResult();
                Assert(ctrl.PendingProfileId == "living-room", "Expected pending living-room");

                ctrl.CancelPendingSwitch();
                Assert(string.IsNullOrEmpty(ctrl.PendingProfileId), "Expected pending switch to be cleared");

                actions.MonitorAvailableResult = true;
                Thread.Sleep(120);
                Assert(ctrl.CurrentMode == RoomMode.Office, "Expected cancelled pending switch not to execute");
            }
        }
    }
}
