import AsyncStorage from '@react-native-async-storage/async-storage';
import { Platform } from 'react-native';
import { PCConnectionConfig } from '../api/models';

const STORAGE_KEYS = {
  pcHost: 'pc_host',
  pcPort: 'pc_port',
  pcMac: 'pc_mac',
  broadcastIp: 'broadcast_ip',
  bearerToken: 'bearer_token',
  wakeTimeoutSeconds: 'wake_timeout_seconds',
  demoMode: 'demo_mode',
} as const;

export interface PersistedSettings extends PCConnectionConfig {
  isDemoMode: boolean;
}

export function isDemoQueryEnabled(): boolean {
  if (Platform.OS === 'web' && typeof window !== 'undefined') {
    try {
      const params = new URLSearchParams(window.location.search);
      const val = params.get('demo');
      return val === '1' || val === 'true';
    } catch {
      return false;
    }
  }
  return false;
}

export function isDemoStreamingEnabled(): boolean {
  if (Platform.OS === 'web' && typeof window !== 'undefined') {
    try {
      const params = new URLSearchParams(window.location.search);
      const val = params.get('streaming');
      return val === '1' || val === 'true';
    } catch {
      return false;
    }
  }
  return false;
}

export function getDemoInitialMode(): string {
  if (Platform.OS === 'web' && typeof window !== 'undefined') {
    try {
      const params = new URLSearchParams(window.location.search);
      const val = params.get('mode')?.trim();
      if (val) return val;
    } catch {
      // Ignore invalid URL
    }
  }
  return 'living-room';
}

export function isDemoIdleEnabled(): boolean {
  if (Platform.OS === 'web' && typeof window !== 'undefined') {
    try {
      const params = new URLSearchParams(window.location.search);
      const val = params.get('idle');
      return val === '1' || val === 'true';
    } catch {
      return false;
    }
  }
  return false;
}

export function getDefaultSettings(): PersistedSettings {
  let defaultHost = '';
  let defaultPort = 8090;

  if (Platform.OS === 'web' && typeof window !== 'undefined') {
    const loc = window.location;
    if (loc?.hostname) {
      defaultHost = loc.hostname;
      if (loc.port) {
        const parsed = Number(loc.port);
        if (Number.isFinite(parsed) && parsed > 0) {
          defaultPort = parsed;
        }
      } else if (loc.protocol === 'https:') {
        defaultPort = 443;
      } else if (loc.protocol === 'http:') {
        defaultPort = 80;
      }
    }
  }

  return {
    pcHost: defaultHost,
    pcPort: defaultPort,
    pcMac: '',
    broadcastIp: '255.255.255.255',
    bearerToken: '',
    wakeTimeoutSeconds: 45,
    isDemoMode: isDemoQueryEnabled(),
  };
}

export async function loadSettings(): Promise<PersistedSettings> {
  const defaults = getDefaultSettings();
  try {
    const entries = await AsyncStorage.multiGet([
      STORAGE_KEYS.pcHost,
      STORAGE_KEYS.pcPort,
      STORAGE_KEYS.pcMac,
      STORAGE_KEYS.broadcastIp,
      STORAGE_KEYS.bearerToken,
      STORAGE_KEYS.wakeTimeoutSeconds,
      STORAGE_KEYS.demoMode,
    ]);
    const map = new Map(entries);

    const savedHost = map.get(STORAGE_KEYS.pcHost);
    const savedPort = Number(map.get(STORAGE_KEYS.pcPort));
    const savedMac = map.get(STORAGE_KEYS.pcMac);
    const savedBroadcast = map.get(STORAGE_KEYS.broadcastIp);
    const savedToken = map.get(STORAGE_KEYS.bearerToken);
    const savedTimeout = Number(map.get(STORAGE_KEYS.wakeTimeoutSeconds));
    const savedDemo = map.get(STORAGE_KEYS.demoMode);

    return {
      pcHost: savedHost && savedHost.trim().length > 0 ? savedHost : defaults.pcHost,
      pcPort:
        Number.isFinite(savedPort) && savedPort > 0 && savedPort <= 65535
          ? savedPort
          : defaults.pcPort,
      pcMac: savedMac ?? defaults.pcMac,
      broadcastIp: savedBroadcast ?? defaults.broadcastIp,
      bearerToken: savedToken ?? defaults.bearerToken,
      wakeTimeoutSeconds:
        Number.isFinite(savedTimeout) && savedTimeout >= 5 && savedTimeout <= 180
          ? savedTimeout
          : defaults.wakeTimeoutSeconds,
      isDemoMode: isDemoQueryEnabled() || savedDemo === 'true',
    };
  } catch {
    return defaults;
  }
}

export async function saveSetting<K extends keyof PersistedSettings>(
  key: K,
  value: PersistedSettings[K]
): Promise<void> {
  const storageKeyMap: Record<keyof PersistedSettings, string> = {
    pcHost: STORAGE_KEYS.pcHost,
    pcPort: STORAGE_KEYS.pcPort,
    pcMac: STORAGE_KEYS.pcMac,
    broadcastIp: STORAGE_KEYS.broadcastIp,
    bearerToken: STORAGE_KEYS.bearerToken,
    wakeTimeoutSeconds: STORAGE_KEYS.wakeTimeoutSeconds,
    isDemoMode: STORAGE_KEYS.demoMode,
  };

  try {
    await AsyncStorage.setItem(storageKeyMap[key], String(value));
  } catch {
    // Ignore storage write errors
  }
}
