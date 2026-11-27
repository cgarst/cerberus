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
  authRequired: boolean;
  authError: string | null;
  authSubmitting: boolean;
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
  switchWakeTarget: (mode: string) => void;
  cancelWake: () => void;
  wakeOnly: () => Promise<void>;
  stopStreaming: (targetMode: string) => Promise<void>;
  setConsoleEnvironmentMode: (mode: ConsoleEnvironmentMode) => Promise<void>;
  toggleOsd: (next: boolean) => Promise<void>;
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

  const demoTickRef = useRef(0);
  const demoOsdRef = useRef(true);
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
      setSettings((prev) => {
        const next = { ...prev, [key]: value };
        settingsRef.current = next;
        return next;
      });
      void saveSetting(key, value);
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
        osdEnabled: demoOsdRef.current,
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
    try {
      const list = await new RoomAgentClient(getConfig()).fetchWindows();
      setWindows(list);
    } catch {
      // Keep existing list on transient error
    }
  }, [getConfig]);

  const refreshStatus = useCallback(async () => {
    if (settingsRef.current.isDemoMode) return;
    const client = new RoomAgentClient(getConfig());
    try {
      const result = await client.fetchHealth(2000);
      setHasCompletedInitialCheck(true);
      setPcOnline(true);
      setAuthRequired(false);
      setAuthError(null);
      setHealth(result);
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
    } catch (err) {
      if (err instanceof RoomAgentClientError && err.kind === 'unauthorized') {
        setHasCompletedInitialCheck(true);
        setPcOnline(true);
        setHealth(null);
        setAuthRequired(true);
        if (settingsRef.current.bearerToken) {
          updateSetting('bearerToken', '');
        }
      } else if (err instanceof RoomAgentClientError && !err.isConnectivityError) {
        setHasCompletedInitialCheck(true);
        setPcOnline(true);
        setHealth(null);
        setLastError(err.message);
      } else {
        setHasCompletedInitialCheck(true);
        setPcOnline(false);
        setHealth(null);
        shortcutsLoadedRef.current = false;
        setShortcuts([]);
        setWindows([]);
      }
    }
  }, [fetchShortcuts, fetchWindows, getConfig, updateSetting]);

  const refreshNow = useCallback(async () => {
    if (settingsRef.current.isDemoMode) {
      applyDemoTick();
      return;
    }
    await refreshStatus();
    if (pcOnlineRef.current) {
      await Promise.all([fetchShortcuts(), fetchWindows()]);
    }
  }, [applyDemoTick, fetchShortcuts, fetchWindows, refreshStatus]);

  useEffect(() => {
    if (!settingsLoaded) return;

    if (settings.isDemoMode) {
      shortcutsLoadedRef.current = true;
      applyDemoTick();
      const id = setInterval(applyDemoTick, 1000);
      return () => clearInterval(id);
    }

    void refreshStatus();
    const healthTimer = setInterval(() => {
      void refreshStatus();
    }, 3000);

    const windowsTimer = setInterval(() => {
      if (pcOnlineRef.current && !busyRef.current) {
        void fetchWindows();
      }
    }, 6000);

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
    refreshStatus,
    fetchWindows,
  ]);

  useEffect(() => {
    const sub = AppState.addEventListener('change', (nextState: AppStateStatus) => {
      if (nextState === 'active' && settingsLoaded && !settingsRef.current.isDemoMode) {
        void refreshStatus().then(() => {
          if (pcOnlineRef.current && !busyRef.current) {
            void fetchWindows();
          }
        });
      }
    });
    return () => sub.remove();
  }, [settingsLoaded, refreshStatus, fetchWindows]);

  const performAction = useCallback(
    async (op: () => Promise<void>) => {
      setBusy(true);
      setLastError(null);
      try {
        await op();
      } catch (err) {
        setLastError(err instanceof Error ? err.message : String(err));
      } finally {
        setBusy(false);
        await refreshStatus();
      }
    },
    [refreshStatus]
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
          const client = new RoomAgentClient(getConfig());

          while (Date.now() < deadline && !wakeCancelledRef.current) {
            await sleep(1500);
            if (wakeCancelledRef.current) break;
            try {
              await client.fetchHealth(1500);
              await client.switchMode(wakeTargetRef.current);
              wakeActiveRef.current = false;
              setWakeInProgress(false);
              await refreshStatus();
              return;
            } catch {
              continue;
            }
          }

          if (!wakeCancelledRef.current) {
            setLastError('Timed out waiting for PC to wake up.');
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
      setBusy(true);
      setLastError(null);
      try {
        await new RoomAgentClient(getConfig()).switchMode(mode);
        setBusy(false);
        await refreshStatus();
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
    [getConfig, refreshStatus, startWakeAndSwitch]
  );

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
        demoModeRef.current = targetMode;
        demoConsoleEnvRef.current =
          targetMode !== 'office' ? 'virtual-desktop' : 'off';
        applyDemoTick();
        return;
      }
      await performAction(() =>
        new RoomAgentClient(getConfig()).stopStreaming(targetMode)
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

  const toggleOsd = useCallback(
    async (next: boolean) => {
      if (settingsRef.current.isDemoMode) {
        demoOsdRef.current = next;
        setHealth((prev) =>
          prev?.metrics
            ? { ...prev, metrics: { ...prev.metrics, osdEnabled: next } }
            : prev
        );
        return;
      }
      if (osdPendingRef.current) return;
      osdPendingRef.current = true;

      let previous: boolean | null | undefined;
      setHealth((prev) => {
        previous = prev?.metrics?.osdEnabled;
        if (!prev?.metrics) return prev;
        return { ...prev, metrics: { ...prev.metrics, osdEnabled: next } };
      });

      try {
        const confirmed = await new RoomAgentClient(getConfig()).setOsdEnabled(
          next
        );
        if (confirmed !== undefined && confirmed !== null) {
          setHealth((prev) =>
            prev?.metrics
              ? { ...prev, metrics: { ...prev.metrics, osdEnabled: confirmed } }
              : prev
          );
        }
      } catch (err) {
        setHealth((prev) =>
          prev?.metrics
            ? { ...prev, metrics: { ...prev.metrics, osdEnabled: previous } }
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
      await performAction(() =>
        new RoomAgentClient(getConfig()).launchShortcut(shortcut.path)
      );
    },
    [getConfig, performAction]
  );

  const focusWindow = useCallback(
    async (win: OpenWindow) => {
      if (settingsRef.current.isDemoMode) return;
      await performAction(() =>
        new RoomAgentClient(getConfig()).focusWindow(win.hwnd)
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
    await performAction(() =>
      new RoomAgentClient(getConfig()).openTaskView()
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
    authRequired,
    authError,
    authSubmitting,
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
    switchWakeTarget,
    cancelWake,
    wakeOnly,
    stopStreaming,
    setConsoleEnvironmentMode,
    toggleOsd,
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
