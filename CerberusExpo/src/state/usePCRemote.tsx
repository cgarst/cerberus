import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { AppState, AppStateStatus, Platform } from 'react-native';
import {
  ConsoleEnvironmentMode,
  DesktopShortcut,
  HealthResponse,
  OpenWindow,
  OsdMode,
  PCConnectionConfig,
  ProfileSummary,
} from '../api/models';
import { RoomAgentClient, RoomAgentClientError } from '../api/roomAgentClient';
import {
  buildDemoHealth,
  buildDemoSeed,
  DEMO_ACTIVE_GAME,
  DEMO_INITIAL_WINDOWS,
  DEMO_SHORTCUTS,
} from './demoData';
import {
  getDefaultSettings,
  getDemoInitialMode,
  isDemoIdleEnabled,
  isDemoStreamingEnabled,
  loadSettings,
  PersistedSettings,
  saveSetting,
} from './storage';
import { sendWakeOnLanPacket } from '../utils/wakeOnLan';
import {
  getClientVersion,
  isServerVersionOlder,
  normalizeVersionString,
} from '../utils/version';

export interface ServerUpdateAdvisory {
  clientVersion: string;
  serverVersion: string | null;
  message: string;
  canInstallUpdate: boolean;
}

interface PCRemoteContextValue {
  settingsLoaded: boolean;
  pcHost: string;
  pcPort: number;
  pcMac: string;
  broadcastIp: string;
  bearerToken: string;
  wakeTimeoutSeconds: number;
  isDemoMode: boolean;
  updateSetting: <K extends keyof PersistedSettings>(
    key: K,
    value: PersistedSettings[K]
  ) => void;

  pcOnline: boolean;
  hasCompletedInitialCheck: boolean;
  health: HealthResponse | null;
  shortcuts: DesktopShortcut[];
  windows: OpenWindow[];
  busy: boolean;
  wakeInProgress: boolean;
  wakeTargetMode: string;
  lastError: string | null;
  clearError: () => void;
  serverUpdateAdvisory: ServerUpdateAdvisory | null;
  dismissServerUpdateAdvisory: () => void;
  serverUpdating: boolean;
  serverUpdateProgress: string | null;
  updateServerNow: () => Promise<void>;
  needsHostSetup: boolean;
  authRequired: boolean;
  authError: string | null;
  authSubmitting: boolean;
  connectHost: (
    host: string,
    port: number
  ) => Promise<{ ok: boolean; authEnabled?: boolean; error?: string }>;
  login: (password: string) => Promise<boolean>;
  signOut: () => void;

  profiles: ProfileSummary[];
  defaultProfile: ProfileSummary;
  activeProfile: ProfileSummary;
  nonDefaultProfiles: ProfileSummary[];
  isStreaming: boolean;
  isLivingRoom: boolean;
  currentConsoleEnvironmentMode: ConsoleEnvironmentMode;
  statusChipText: string;

  refreshNow: () => Promise<void>;
  refreshStatus: () => Promise<void>;
  switchMode: (mode: string) => Promise<void>;
  cancelPendingSwitch: () => Promise<void>;
  switchWakeTarget: (mode: string) => void;
  cancelWake: () => void;
  wakeOnly: () => Promise<void>;
  stopStreaming: (targetMode: string) => Promise<void>;
  setConsoleEnvironmentMode: (mode: ConsoleEnvironmentMode) => Promise<void>;
  setOsdMode: (next: OsdMode) => Promise<void>;
  sleepPC: () => Promise<void>;
  shutdownPC: () => Promise<void>;
  fetchShortcuts: () => Promise<void>;
  launchShortcut: (shortcut: DesktopShortcut) => Promise<void>;
  fetchWindows: () => Promise<void>;
  focusWindow: (win: OpenWindow) => Promise<void>;
  closeWindow: (win: OpenWindow) => Promise<void>;
  closeAllWindows: () => Promise<void>;
  openTaskView: () => Promise<void>;
}

const PCRemoteContext = createContext<PCRemoteContextValue | null>(null);

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function PCRemoteProvider({ children }: { children: React.ReactNode }) {
  const [settings, setSettings] = useState<PersistedSettings>(getDefaultSettings);
  const [settingsLoaded, setSettingsLoaded] = useState(false);

  const [pcOnline, setPcOnline] = useState(false);
  const [hasCompletedInitialCheck, setHasCompletedInitialCheck] = useState(false);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [shortcuts, setShortcuts] = useState<DesktopShortcut[]>([]);
  const [windows, setWindows] = useState<OpenWindow[]>([]);
  const [busy, setBusy] = useState(false);
  const [wakeInProgress, setWakeInProgress] = useState(false);
  const [wakeTargetMode, setWakeTargetMode] = useState<string>('living-room');
  const [lastError, setLastError] = useState<string | null>(null);
  const [authRequired, setAuthRequired] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [authSubmitting, setAuthSubmitting] = useState(false);
  const [serverVersion, setServerVersion] = useState<string | null>(null);
  const [legacyServerDetected, setLegacyServerDetected] = useState(false);
  const [updateAdvisoryDismissed, setUpdateAdvisoryDismissed] = useState(false);
  const [serverUpdating, setServerUpdating] = useState(false);
  const [serverUpdateProgress, setServerUpdateProgress] = useState<string | null>(
    null
  );

  const settingsRef = useRef(settings);
  settingsRef.current = settings;
  const pcOnlineRef = useRef(pcOnline);
  pcOnlineRef.current = pcOnline;
  const busyRef = useRef(busy);
  busyRef.current = busy;
  const wakeTargetRef = useRef(wakeTargetMode);
  wakeTargetRef.current = wakeTargetMode;

  const shortcutsLoadedRef = useRef(false);
  const osdPendingRef = useRef(false);
  const wakeCancelledRef = useRef(false);
  const wakeActiveRef = useRef(false);
  const statusInFlightRef = useRef<Promise<boolean> | null>(null);
  const windowsInFlightRef = useRef<Promise<void> | null>(null);
  const consecutiveFailuresRef = useRef(0);
  const updateProbeDoneRef = useRef(false);
  const updateProbeTargetRef = useRef('');
  const isAppActiveRef = useRef(
    AppState.currentState === 'active' || AppState.currentState === 'unknown'
  );

  const demoTickRef = useRef(0);
  const demoOsdRef = useRef<OsdMode>('afterburner');
  const demoInitialMode = getDemoInitialMode();
  const demoIdle = isDemoIdleEnabled();
  const demoModeRef = useRef<string>(demoInitialMode);
  const demoStreamingRef = useRef(isDemoStreamingEnabled());
  const demoConsoleEnvRef = useRef<ConsoleEnvironmentMode>(
    demoInitialMode === 'office' ? 'off' : 'virtual-desktop'
  );
  const demoActiveGameRef = useRef<string | null>(
    demoIdle ? null : DEMO_ACTIVE_GAME
  );
  const demoHistoryRef = useRef(buildDemoSeed());

  useEffect(() => {
    let mounted = true;
    loadSettings().then((loaded) => {
      if (!mounted) return;
      setSettings(loaded);
      settingsRef.current = loaded;
      setSettingsLoaded(true);
    });
    return () => {
      mounted = false;
    };
  }, []);

  const updateSetting = useCallback(
    <K extends keyof PersistedSettings>(key: K, value: PersistedSettings[K]) => {
      const prevValue = settingsRef.current[key];
      const hostOrPortChanged =
        (key === 'pcHost' || key === 'pcPort') && prevValue !== value;

      setSettings((prev) => {
        const next = { ...prev, [key]: value };
        if (hostOrPortChanged) {
          next.bearerToken = '';
        }
        settingsRef.current = next;
        return next;
      });
      void saveSetting(key, value);

      if (hostOrPortChanged) {
        void saveSetting('bearerToken', '');
        setLastError(null);
        setAuthRequired(false);
        setAuthError(null);
        setHealth(null);
        shortcutsLoadedRef.current = false;
      }
    },
    []
  );

  const getConfig = useCallback((): PCConnectionConfig => {
    const s = settingsRef.current;
    return {
      pcHost: s.pcHost,
      pcPort: s.pcPort,
      pcMac: s.pcMac,
      broadcastIp: s.broadcastIp,
      bearerToken: s.bearerToken,
      wakeTimeoutSeconds: s.wakeTimeoutSeconds,
    };
  }, []);

  const applyDemoTick = useCallback(() => {
    demoTickRef.current += 1;
    const t = demoTickRef.current;
    const nextFps =
      Math.round(
        (116 + Math.sin((60 + t) / 4) * 8 + (t % 19 === 0 ? -22 : 0)) * 10
      ) / 10;
    const nextFt =
      Math.round((1000 / nextFps + (t % 19 === 0 ? 6.8 : 0)) * 100) / 100;

    const hist = demoHistoryRef.current;
    hist.fps = [...hist.fps.slice(1), nextFps];
    hist.ft = [...hist.ft.slice(1), nextFt];

    setPcOnline(true);
    setHasCompletedInitialCheck(true);
    setShortcuts((prev) => (prev.length > 0 ? prev : DEMO_SHORTCUTS));
    setWindows((prev) => (prev.length > 0 ? prev : DEMO_INITIAL_WINDOWS));
    setHealth(
      buildDemoHealth({
        tick: t,
        mode: demoModeRef.current,
        streamingActive: demoStreamingRef.current,
        consoleEnvironmentMode: demoConsoleEnvRef.current,
        osdMode: demoOsdRef.current,
        activeGame: demoActiveGameRef.current,
        fpsHistory: hist.fps,
        ftHistory: hist.ft,
      })
    );
  }, []);

  const fetchShortcuts = useCallback(async () => {
    if (settingsRef.current.isDemoMode) return;
    try {
      const list = await new RoomAgentClient(getConfig()).fetchShortcuts();
      setShortcuts(list);
    } catch {
      // Keep existing list on transient error
    }
  }, [getConfig]);

  const fetchWindows = useCallback(async () => {
    if (settingsRef.current.isDemoMode) return;
    if (windowsInFlightRef.current) return windowsInFlightRef.current;
    const req = (async () => {
      try {
        const list = await new RoomAgentClient(getConfig()).fetchWindows();
        setWindows(list);
      } catch {
        // Keep existing list on transient error
      } finally {
        windowsInFlightRef.current = null;
      }
    })();
    windowsInFlightRef.current = req;
    return req;
  }, [getConfig]);

  const refreshStatusInternal = useCallback(async (): Promise<boolean> => {
    if (settingsRef.current.isDemoMode) return true;
    if (statusInFlightRef.current) return statusInFlightRef.current;

    const req = (async (): Promise<boolean> => {
      if (settingsRef.current.pcHost.trim().length === 0) {
        consecutiveFailuresRef.current = 0;
        pcOnlineRef.current = false;
        updateProbeDoneRef.current = false;
        setHasCompletedInitialCheck(true);
        setPcOnline(false);
        setHealth(null);
        setServerVersion(null);
        setLegacyServerDetected(false);
        setAuthRequired(false);
        setLastError(null);
        return false;
      }
      const targetKey = `${settingsRef.current.pcHost.trim()}:${settingsRef.current.pcPort}`;
      if (updateProbeTargetRef.current !== targetKey) {
        updateProbeTargetRef.current = targetKey;
        updateProbeDoneRef.current = false;
        setServerVersion(null);
        setLegacyServerDetected(false);
        setUpdateAdvisoryDismissed(false);
      }
      const client = new RoomAgentClient(getConfig());
      try {
        const result = await client.fetchHealth(2000);
        consecutiveFailuresRef.current = 0;
        pcOnlineRef.current = true;
        setHasCompletedInitialCheck(true);
        setPcOnline(true);
        setAuthRequired(false);
        setAuthError(null);
        setHealth(result);
        const healthVersion = result.version?.trim();
        if (healthVersion) {
          updateProbeDoneRef.current = true;
          setServerVersion(healthVersion);
          setLegacyServerDetected(false);
        } else if (!updateProbeDoneRef.current) {
          updateProbeDoneRef.current = true;
          void client
            .fetchUpdateStatus(3000)
            .then((upd) => {
              if (updateProbeTargetRef.current !== targetKey) return;
              const ver = (upd?.CurrentVersion ?? upd?.currentVersion)?.trim();
              if (ver) {
                setServerVersion(ver);
                setLegacyServerDetected(false);
              }
            })
            .catch((probeErr) => {
              if (updateProbeTargetRef.current !== targetKey) return;
              if (
                probeErr instanceof RoomAgentClientError &&
                probeErr.statusCode === 404
              ) {
                setLegacyServerDetected(true);
                setServerVersion(null);
              } else {
                updateProbeDoneRef.current = false;
              }
            });
        }
        const wolMac = result.wolMac?.trim();
        if (wolMac && settingsRef.current.pcMac.trim().length === 0) {
          updateSetting('pcMac', wolMac);
        }
        const wolBroadcastIp = result.wolBroadcastIp?.trim();
        const currentBroadcast = settingsRef.current.broadcastIp.trim();
        if (
          wolBroadcastIp &&
          (currentBroadcast.length === 0 || currentBroadcast === '255.255.255.255')
        ) {
          updateSetting('broadcastIp', wolBroadcastIp);
        }
        if (!shortcutsLoadedRef.current) {
          shortcutsLoadedRef.current = true;
          void fetchShortcuts();
          void fetchWindows();
        }
        return true;
      } catch (err) {
        if (err instanceof RoomAgentClientError && err.kind === 'unauthorized') {
          consecutiveFailuresRef.current = 0;
          pcOnlineRef.current = true;
          setHasCompletedInitialCheck(true);
          setPcOnline(true);
          setHealth(null);
          setAuthRequired(true);
          setLastError(null);
          if (settingsRef.current.bearerToken) {
            updateSetting('bearerToken', '');
          }
          return true;
        } else if (
          err instanceof RoomAgentClientError &&
          err.kind === 'invalid_configuration'
        ) {
          consecutiveFailuresRef.current = 0;
          pcOnlineRef.current = false;
          setHasCompletedInitialCheck(true);
          setPcOnline(false);
          setHealth(null);
          setLastError(err.message);
          return false;
        } else if (err instanceof RoomAgentClientError && !err.isConnectivityError) {
          consecutiveFailuresRef.current = 0;
          pcOnlineRef.current = true;
          setHasCompletedInitialCheck(true);
          setPcOnline(true);
          setHealth(null);
          setLastError(err.message);
          return true;
        } else {
          // Allow one transient timeout/blip when already online before wiping state.
          consecutiveFailuresRef.current += 1;
          if (!pcOnlineRef.current || consecutiveFailuresRef.current >= 2) {
            pcOnlineRef.current = false;
            updateProbeDoneRef.current = false;
            setHasCompletedInitialCheck(true);
            setPcOnline(false);
            setHealth(null);
            setServerVersion(null);
            setLegacyServerDetected(false);
            shortcutsLoadedRef.current = false;
            setShortcuts([]);
            setWindows([]);
          }
          return false;
        }
      } finally {
        statusInFlightRef.current = null;
      }
    })();

    statusInFlightRef.current = req;
    return req;
  }, [fetchShortcuts, fetchWindows, getConfig, updateSetting]);

  const refreshStatus = useCallback(async () => {
    await refreshStatusInternal();
  }, [refreshStatusInternal]);

  const refreshNow = useCallback(async () => {
    if (settingsRef.current.isDemoMode) {
      applyDemoTick();
      return;
    }
    const online = await refreshStatusInternal();
    if (online) {
      await Promise.all([fetchShortcuts(), fetchWindows()]);
    }
  }, [applyDemoTick, fetchShortcuts, fetchWindows, refreshStatusInternal]);

  useEffect(() => {
    if (!settingsLoaded) return;

    if (settings.isDemoMode) {
      shortcutsLoadedRef.current = true;
      applyDemoTick();
      const id = setInterval(applyDemoTick, 1000);
      return () => clearInterval(id);
    }

    void refreshStatusInternal();
    const healthTimer = setInterval(() => {
      if (isAppActiveRef.current) {
        void refreshStatusInternal();
      }
    }, 1500);

    const windowsTimer = setInterval(() => {
      if (isAppActiveRef.current && pcOnlineRef.current && !busyRef.current) {
        void fetchWindows();
      }
    }, 3000);

    return () => {
      clearInterval(healthTimer);
      clearInterval(windowsTimer);
    };
  }, [
    settingsLoaded,
    settings.isDemoMode,
    settings.pcHost,
    settings.pcPort,
    settings.bearerToken,
    applyDemoTick,
    refreshStatusInternal,
    fetchWindows,
  ]);

  useEffect(() => {
    const triggerActiveRefresh = () => {
      isAppActiveRef.current = true;
      if (settingsLoaded && !settingsRef.current.isDemoMode) {
        void refreshStatusInternal().then((online) => {
          if (online && !busyRef.current) {
            void fetchWindows();
          }
        });
      }
    };

    const sub = AppState.addEventListener('change', (nextState: AppStateStatus) => {
      if (nextState === 'active') {
        triggerActiveRefresh();
      } else {
        isAppActiveRef.current = false;
      }
    });

    if (Platform.OS === 'web' && typeof window !== 'undefined') {
      const onFocus = () => triggerActiveRefresh();
      const onVisibilityChange = () => {
        if (typeof document !== 'undefined' && !document.hidden) {
          triggerActiveRefresh();
        } else {
          isAppActiveRef.current = false;
        }
      };
      window.addEventListener('focus', onFocus);
      if (typeof document !== 'undefined') {
        document.addEventListener('visibilitychange', onVisibilityChange);
      }
      return () => {
        sub.remove();
        window.removeEventListener('focus', onFocus);
        if (typeof document !== 'undefined') {
          document.removeEventListener('visibilitychange', onVisibilityChange);
        }
      };
    }

    return () => sub.remove();
  }, [settingsLoaded, refreshStatusInternal, fetchWindows]);

  const performAction = useCallback(
    async (op: () => Promise<void>, options?: { refreshWindowsAfter?: boolean }) => {
      setBusy(true);
      setLastError(null);
      try {
        await op();
      } catch (err) {
        setLastError(err instanceof Error ? err.message : String(err));
      } finally {
        setBusy(false);
        const online = await refreshStatusInternal();
        if (options?.refreshWindowsAfter && online) {
          await sleep(400);
          await fetchWindows();
        }
      }
    },
    [fetchWindows, refreshStatusInternal]
  );

  const startWakeAndSwitch = useCallback(
    (targetMode: string) => {
      setWakeTargetMode(targetMode);
      wakeTargetRef.current = targetMode;
      setLastError(null);
      if (wakeActiveRef.current) return;

      if (Platform.OS === 'web') {
        setLastError('PC is offline. Waking a sleeping PC requires the mobile app.');
        return;
      }

      wakeActiveRef.current = true;
      wakeCancelledRef.current = false;
      setWakeInProgress(true);

      void (async () => {
        try {
          const s = settingsRef.current;
          await sendWakeOnLanPacket(s.pcMac, s.broadcastIp);
          const deadline =
            Date.now() + Math.max(10, s.wakeTimeoutSeconds) * 1000;

          let awake = false;
          while (Date.now() < deadline && !wakeCancelledRef.current) {
            await sleep(1500);
            if (wakeCancelledRef.current) break;
            if (pcOnlineRef.current) {
              awake = true;
              break;
            }
            try {
              await new RoomAgentClient(getConfig()).fetchHealth(2500);
              awake = true;
              break;
            } catch (err) {
              if (
                err instanceof RoomAgentClientError &&
                !err.isConnectivityError
              ) {
                awake = true;
                break;
              }
            }
          }

          if (wakeCancelledRef.current) return;

          if (!awake) {
            setLastError('Timed out waiting for PC to wake up.');
            return;
          }

          try {
            await new RoomAgentClient(getConfig()).switchMode(
              wakeTargetRef.current
            );
            wakeActiveRef.current = false;
            setWakeInProgress(false);
            await refreshStatus();
          } catch (err) {
            if (!wakeCancelledRef.current) {
              if (
                err instanceof RoomAgentClientError &&
                err.kind === 'unauthorized'
              ) {
                await refreshStatus();
              } else {
                setLastError(
                  err instanceof Error ? err.message : String(err)
                );
                await refreshStatus();
              }
            }
          }
        } catch (err) {
          if (!wakeCancelledRef.current) {
            const msg = err instanceof Error ? err.message : String(err);
            setLastError(`Wake failed: ${msg}`);
          }
        } finally {
          wakeActiveRef.current = false;
          setWakeInProgress(false);
        }
      })();
    },
    [getConfig, refreshStatus]
  );

  const switchMode = useCallback(
    async (mode: string) => {
      if (settingsRef.current.isDemoMode) {
        demoModeRef.current = mode;
        demoConsoleEnvRef.current =
          mode !== 'office' ? 'virtual-desktop' : 'off';
        applyDemoTick();
        return;
      }
      if (!pcOnlineRef.current) {
        startWakeAndSwitch(mode);
        return;
      }
      setBusy(true);
      setLastError(null);
      try {
        await new RoomAgentClient(getConfig()).switchMode(mode);
        setBusy(false);
        const online = await refreshStatusInternal();
        if (online) {
          void fetchWindows();
        }
      } catch (err) {
        setBusy(false);
        if (err instanceof RoomAgentClientError && err.isConnectivityError) {
          startWakeAndSwitch(mode);
        } else {
          setLastError(err instanceof Error ? err.message : String(err));
          await refreshStatus();
        }
      }
    },
    [fetchWindows, getConfig, refreshStatusInternal, startWakeAndSwitch]
  );

  const cancelPendingSwitch = useCallback(async () => {
    if (settingsRef.current.isDemoMode) return;
    await performAction(() =>
      new RoomAgentClient(getConfig()).cancelPendingSwitch()
    );
  }, [getConfig, performAction]);

  const switchWakeTarget = useCallback((mode: string) => {
    setWakeTargetMode(mode);
    wakeTargetRef.current = mode;
  }, []);

  const cancelWake = useCallback(() => {
    wakeCancelledRef.current = true;
    wakeActiveRef.current = false;
    setWakeInProgress(false);
  }, []);

  const wakeOnly = useCallback(async () => {
    setLastError(null);
    try {
      const s = settingsRef.current;
      await sendWakeOnLanPacket(s.pcMac, s.broadcastIp);
    } catch (err) {
      setLastError(err instanceof Error ? err.message : String(err));
    }
  }, []);

  const stopStreaming = useCallback(
    async (targetMode: string) => {
      if (settingsRef.current.isDemoMode) {
        demoStreamingRef.current = false;
        demoModeRef.current = targetMode || 'office';
        demoConsoleEnvRef.current =
          !targetMode || targetMode === 'office' ? 'off' : 'virtual-desktop';
        applyDemoTick();
        return;
      }
      await performAction(
        () => new RoomAgentClient(getConfig()).stopStreaming(targetMode),
        { refreshWindowsAfter: true }
      );
    },
    [getConfig, performAction]
  );

  const setConsoleEnvironmentMode = useCallback(
    async (mode: ConsoleEnvironmentMode) => {
      if (settingsRef.current.isDemoMode) {
        demoConsoleEnvRef.current = mode;
        applyDemoTick();
        return;
      }
      await performAction(() =>
        new RoomAgentClient(getConfig()).setConsoleEnvironmentMode(mode)
      );
    },
    [getConfig, performAction]
  );

  const setOsdMode = useCallback(
    async (next: OsdMode) => {
      if (settingsRef.current.isDemoMode) {
        demoOsdRef.current = next;
        setHealth((prev) =>
          prev?.metrics
            ? {
                ...prev,
                metrics: {
                  ...prev.metrics,
                  osdMode: next,
                  osdEnabled: next !== 'off',
                },
              }
            : prev
        );
        return;
      }
      if (osdPendingRef.current) return;
      osdPendingRef.current = true;

      let previousEnabled: boolean | null | undefined;
      let previousMode: OsdMode | null | undefined;
      setHealth((prev) => {
        previousEnabled = prev?.metrics?.osdEnabled;
        previousMode = prev?.metrics?.osdMode;
        if (!prev?.metrics) return prev;
        return {
          ...prev,
          metrics: {
            ...prev.metrics,
            osdMode: next,
            osdEnabled: next !== 'off',
          },
        };
      });

      try {
        const resp = await new RoomAgentClient(getConfig()).setOsdMode(next);
        const confirmedMode =
          resp.osdMode ??
          (resp.osdEnabled !== undefined && resp.osdEnabled !== null
            ? resp.osdEnabled
              ? next === 'off'
                ? 'afterburner'
                : next
              : 'off'
            : undefined);
        const confirmedEnabled =
          resp.osdEnabled ??
          (confirmedMode !== undefined ? confirmedMode !== 'off' : undefined);

        if (confirmedMode !== undefined || confirmedEnabled !== undefined) {
          setHealth((prev) =>
            prev?.metrics
              ? {
                  ...prev,
                  metrics: {
                    ...prev.metrics,
                    ...(confirmedMode !== undefined
                      ? { osdMode: confirmedMode }
                      : {}),
                    ...(confirmedEnabled !== undefined
                      ? { osdEnabled: confirmedEnabled }
                      : {}),
                  },
                }
              : prev
          );
        }
      } catch (err) {
        setHealth((prev) =>
          prev?.metrics
            ? {
                ...prev,
                metrics: {
                  ...prev.metrics,
                  osdMode: previousMode,
                  osdEnabled: previousEnabled,
                },
              }
            : prev
        );
        setLastError(err instanceof Error ? err.message : String(err));
      } finally {
        osdPendingRef.current = false;
      }
    },
    [getConfig]
  );

  const sleepPC = useCallback(async () => {
    if (settingsRef.current.isDemoMode) {
      setPcOnline(false);
      setHealth(null);
      return;
    }
    await performAction(() => new RoomAgentClient(getConfig()).sleepPC());
  }, [getConfig, performAction]);

  const shutdownPC = useCallback(async () => {
    if (settingsRef.current.isDemoMode) {
      setPcOnline(false);
      setHealth(null);
      return;
    }
    await performAction(() => new RoomAgentClient(getConfig()).shutdownPC());
  }, [getConfig, performAction]);

  const launchShortcut = useCallback(
    async (shortcut: DesktopShortcut) => {
      if (settingsRef.current.isDemoMode) {
        const entry = DEMO_SHORTCUTS.find((s) => s.path === shortcut.path);
        if (entry?.isGame) {
          demoActiveGameRef.current = entry.name;
          setWindows((prev) => {
            const nonGame = prev.filter(
              (w) =>
                !DEMO_SHORTCUTS.some(
                  (s) => s.isGame && s.processName === w.process
                )
            );
            return [
              {
                hwnd: 1001,
                pid: 18420,
                process: entry.processName ?? 'Game',
                title: entry.name,
                icon: entry.icon ?? null,
              },
              ...nonGame,
            ];
          });
          applyDemoTick();
        }
        return;
      }
      await performAction(
        () => new RoomAgentClient(getConfig()).launchShortcut(shortcut.path),
        { refreshWindowsAfter: true }
      );
    },
    [getConfig, performAction]
  );

  const focusWindow = useCallback(
    async (win: OpenWindow) => {
      if (settingsRef.current.isDemoMode) return;
      await performAction(
        () => new RoomAgentClient(getConfig()).focusWindow(win.hwnd),
        { refreshWindowsAfter: true }
      );
    },
    [getConfig, performAction]
  );

  const closeWindow = useCallback(
    async (win: OpenWindow) => {
      if (settingsRef.current.isDemoMode) {
        setWindows((prev) => prev.filter((w) => w.hwnd !== win.hwnd));
        if (win.title === demoActiveGameRef.current) {
          demoActiveGameRef.current = null;
          applyDemoTick();
        }
        return;
      }
      await performAction(async () => {
        await new RoomAgentClient(getConfig()).closeWindow(win.hwnd);
        await sleep(400);
        await fetchWindows();
      });
    },
    [fetchWindows, getConfig, performAction]
  );

  const closeAllWindows = useCallback(async () => {
    if (settingsRef.current.isDemoMode) {
      setWindows([]);
      return;
    }
    await performAction(async () => {
      await new RoomAgentClient(getConfig()).closeAllWindows();
      await sleep(400);
      await fetchWindows();
    });
  }, [fetchWindows, getConfig, performAction]);

  const openTaskView = useCallback(async () => {
    if (settingsRef.current.isDemoMode) return;
    await performAction(
      () => new RoomAgentClient(getConfig()).openTaskView(),
      { refreshWindowsAfter: true }
    );
  }, [getConfig, performAction]);

  const profiles: ProfileSummary[] = useMemo(() => {
    const raw = health?.profiles;
    if (raw && raw.length > 0) {
      return raw;
    }
    return [
      {
        id: 'office',
        name: 'Office',
        isDefault: true,
        available: health?.officeAvailable ?? true,
        consoleEnvironmentMode: health?.officeConsoleEnvironmentMode ?? 'off',
      },
      {
        id: 'living-room',
        name: 'Living Room',
        isDefault: false,
        available: health?.livingRoomAvailable ?? true,
        consoleEnvironmentMode:
          health?.livingRoomConsoleEnvironmentMode ?? 'virtual-desktop',
      },
    ];
  }, [health]);

  const defaultProfileId = health?.defaultProfileId;
  const defaultProfile: ProfileSummary = useMemo(() => {
    const byId = defaultProfileId
      ? profiles.find(
          (p) => p.id.toLowerCase() === defaultProfileId.toLowerCase()
        )
      : undefined;
    return byId ?? profiles.find((p) => p.isDefault) ?? profiles[0];
  }, [defaultProfileId, profiles]);

  const activeProfileIdRaw = health?.activeProfileId;
  const healthModeRaw = health?.mode;
  const underlyingModeRaw = health?.underlyingMode;
  const activeProfile: ProfileSummary = useMemo(() => {
    const activeId =
      activeProfileIdRaw ||
      (healthModeRaw && healthModeRaw !== 'streaming'
        ? healthModeRaw
        : underlyingModeRaw) ||
      defaultProfile.id;
    return (
      profiles.find((p) => p.id.toLowerCase() === activeId.toLowerCase()) ??
      defaultProfile
    );
  }, [
    activeProfileIdRaw,
    defaultProfile,
    healthModeRaw,
    profiles,
    underlyingModeRaw,
  ]);

  const nonDefaultProfiles = useMemo(
    () =>
      profiles.filter(
        (p) => p.id.toLowerCase() !== defaultProfile.id.toLowerCase()
      ),
    [defaultProfile.id, profiles]
  );

  const isStreaming =
    pcOnline && (Boolean(health?.streamingActive) || health?.mode === 'streaming');
  const isLivingRoom =
    pcOnline &&
    !isStreaming &&
    activeProfile.id.toLowerCase() !== defaultProfile.id.toLowerCase();

  const currentConsoleEnvironmentMode: ConsoleEnvironmentMode = useMemo(() => {
    const raw = health?.consoleEnvironmentMode;
    if (
      raw === 'off' ||
      raw === 'minimize-all' ||
      raw === 'virtual-desktop' ||
      raw === 'xbox'
    ) {
      return raw;
    }
    return 'off';
  }, [health?.consoleEnvironmentMode]);

  const statusChipText = useMemo(() => {
    if (wakeInProgress) return 'Waking…';
    if (!hasCompletedInitialCheck) return 'Checking…';
    if (pcOnline) {
      if (isStreaming) return 'Streaming';
      return activeProfile.name || health?.activeProfileName || 'Online';
    }
    return 'Asleep';
  }, [
    wakeInProgress,
    hasCompletedInitialCheck,
    pcOnline,
    isStreaming,
    activeProfile.name,
    health?.activeProfileName,
  ]);

  const clearError = useCallback(() => setLastError(null), []);
  const dismissServerUpdateAdvisory = useCallback(
    () => setUpdateAdvisoryDismissed(true),
    []
  );

  const serverUpdateAdvisory = useMemo<ServerUpdateAdvisory | null>(() => {
    if (settings.isDemoMode || !pcOnline || updateAdvisoryDismissed) {
      return null;
    }
    const clientVersion = getClientVersion();
    const isPasswordAuthed =
      Boolean(health?.authEnabled) && settings.bearerToken.trim().length > 0;
    if (legacyServerDetected) {
      return {
        clientVersion,
        serverVersion: null,
        canInstallUpdate: false,
        message: clientVersion
          ? `CerberusService on your PC is running an older version and requires an update (client v${clientVersion}).`
          : 'CerberusService on your PC is running an older version and requires an update.',
      };
    }
    if (
      serverVersion &&
      clientVersion &&
      isServerVersionOlder(serverVersion, clientVersion)
    ) {
      const normServer = normalizeVersionString(serverVersion);
      return {
        clientVersion,
        serverVersion: normServer,
        canInstallUpdate: isPasswordAuthed,
        message: `CerberusService on your PC (v${normServer}) is older than this client (v${clientVersion}) and requires an update.`,
      };
    }
    return null;
  }, [
    health?.authEnabled,
    legacyServerDetected,
    pcOnline,
    serverVersion,
    settings.bearerToken,
    settings.isDemoMode,
    updateAdvisoryDismissed,
  ]);

  const updateServerNow = useCallback(async () => {
    if (
      settingsRef.current.isDemoMode ||
      !pcOnlineRef.current ||
      !health?.authEnabled ||
      settingsRef.current.bearerToken.trim().length === 0 ||
      serverUpdating
    ) {
      return;
    }

    const client = new RoomAgentClient(getConfig());
    setServerUpdating(true);
    setServerUpdateProgress('Checking for update...');
    setLastError(null);

    try {
      const checkSnap = await client.checkForUpdates(15000);
      let status = (checkSnap.Status ?? checkSnap.status ?? '').toLowerCase();

      if (status === 'uptodate') {
        setLastError('No newer CerberusService release is published yet.');
        return;
      }

      if (status === 'error') {
        setLastError(
          checkSnap.ErrorMessage ??
            checkSnap.errorMessage ??
            'Failed to check for CerberusService updates.'
        );
        return;
      }

      if (status === 'updateavailable') {
        setServerUpdateProgress('Starting silent update...');
        await client.installUpdate(10000);
        status = 'downloading';
      }

      if (status === 'downloading' || status === 'installing') {
        for (let i = 0; i < 60; i++) {
          if (status === 'installing') {
            setServerUpdateProgress(
              'Installing update — CerberusService will restart...'
            );
            await sleep(2500);
            break;
          }
          await sleep(1000);
          try {
            const snap = await client.fetchUpdateStatus(3000);
            status = (snap.Status ?? snap.status ?? '').toLowerCase();
            if (status === 'downloading') {
              const pct =
                snap.DownloadProgressPercent ?? snap.downloadProgressPercent ?? 0;
              setServerUpdateProgress(`Downloading update (${pct}%)...`);
            } else if (status === 'installing') {
              setServerUpdateProgress(
                'Installing update — CerberusService will restart...'
              );
              await sleep(2500);
              break;
            } else if (status === 'error') {
              setLastError(
                snap.ErrorMessage ??
                  snap.errorMessage ??
                  'CerberusService update failed.'
              );
              break;
            } else if (status === 'uptodate' || status === 'idle') {
              break;
            }
          } catch (pollErr) {
            if (
              pollErr instanceof RoomAgentClientError &&
              pollErr.isConnectivityError
            ) {
              setServerUpdateProgress(
                'Installing update — CerberusService is restarting...'
              );
              await sleep(2000);
              break;
            }
            throw pollErr;
          }
        }
      }
    } catch (err) {
      setLastError(err instanceof Error ? err.message : String(err));
    } finally {
      setServerUpdating(false);
      setServerUpdateProgress(null);
    }
  }, [getConfig, health?.authEnabled, serverUpdating]);

  const needsHostSetup =
    settingsLoaded &&
    !settings.isDemoMode &&
    settings.pcHost.trim().length === 0;

  const connectHost = useCallback(
    async (
      host: string,
      port: number
    ): Promise<{ ok: boolean; authEnabled?: boolean; error?: string }> => {
      const trimmedHost = host.trim();
      if (!trimmedHost || !Number.isInteger(port) || port <= 0 || port > 65535) {
        return { ok: false, error: 'Enter a valid PC IP address or hostname and port.' };
      }

      const client = new RoomAgentClient({
        ...getConfig(),
        pcHost: trimmedHost,
        pcPort: port,
        bearerToken: '',
      });

      try {
        const status = await client.fetchAuthStatus(3500);
        updateSetting('pcHost', trimmedHost);
        updateSetting('pcPort', port);
        setLastError(null);
        setAuthError(null);
        setPcOnline(true);
        if (status.enabled) {
          setAuthRequired(true);
          return { ok: true, authEnabled: true };
        }
        setAuthRequired(false);
        return { ok: true, authEnabled: false };
      } catch (err) {
        const message =
          err instanceof Error ? err.message : String(err);
        return { ok: false, error: message };
      }
    },
    [getConfig, updateSetting]
  );

  const login = useCallback(
    async (password: string): Promise<boolean> => {
      setAuthSubmitting(true);
      setAuthError(null);
      try {
        const result = await new RoomAgentClient(getConfig()).login(password);
        updateSetting('bearerToken', result.token);
        setAuthRequired(false);
        setAuthError(null);
        setLastError(null);
        await refreshNow();
        return true;
      } catch (err) {
        setAuthError(err instanceof Error ? err.message : String(err));
        return false;
      } finally {
        setAuthSubmitting(false);
      }
    },
    [getConfig, refreshNow, updateSetting]
  );

  const signOut = useCallback(() => {
    updateSetting('bearerToken', '');
    setHealth(null);
    shortcutsLoadedRef.current = false;
    setShortcuts([]);
    setWindows([]);
    void refreshStatus();
  }, [refreshStatus, updateSetting]);

  const value: PCRemoteContextValue = {
    settingsLoaded,
    pcHost: settings.pcHost,
    pcPort: settings.pcPort,
    pcMac: settings.pcMac,
    broadcastIp: settings.broadcastIp,
    bearerToken: settings.bearerToken,
    wakeTimeoutSeconds: settings.wakeTimeoutSeconds,
    isDemoMode: settings.isDemoMode,
    updateSetting,
    pcOnline,
    hasCompletedInitialCheck,
    health,
    shortcuts,
    windows,
    busy,
    wakeInProgress,
    wakeTargetMode,
    lastError,
    clearError,
    serverUpdateAdvisory,
    dismissServerUpdateAdvisory,
    serverUpdating,
    serverUpdateProgress,
    updateServerNow,
    needsHostSetup,
    authRequired,
    authError,
    authSubmitting,
    connectHost,
    login,
    signOut,
    profiles,
    defaultProfile,
    activeProfile,
    nonDefaultProfiles,
    isStreaming,
    isLivingRoom,
    currentConsoleEnvironmentMode,
    statusChipText,
    refreshNow,
    refreshStatus,
    switchMode,
    cancelPendingSwitch,
    switchWakeTarget,
    cancelWake,
    wakeOnly,
    stopStreaming,
    setConsoleEnvironmentMode,
    setOsdMode,
    sleepPC,
    shutdownPC,
    fetchShortcuts,
    launchShortcut,
    fetchWindows,
    focusWindow,
    closeWindow,
    closeAllWindows,
    openTaskView,
  };

  return (
    <PCRemoteContext.Provider value={value}>
      {children}
    </PCRemoteContext.Provider>
  );
}

export function usePCRemote(): PCRemoteContextValue {
  const ctx = useContext(PCRemoteContext);
  if (!ctx) {
    throw new Error('usePCRemote must be used within PCRemoteProvider');
  }
  return ctx;
}
