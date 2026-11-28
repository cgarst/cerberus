import { Platform } from 'react-native';

export type ConsoleEnvironmentMode = 'off' | 'minimize-all' | 'virtual-desktop' | 'xbox';

export const CONSOLE_ENVIRONMENT_MODES: {
  id: ConsoleEnvironmentMode;
  shortLabel: string;
  fullLabel: string;
}[] = [
  { id: 'off', shortLabel: 'Off', fullLabel: 'Off' },
  { id: 'minimize-all', shortLabel: 'Minimized', fullLabel: 'Minimize All' },
  { id: 'virtual-desktop', shortLabel: 'Virtual', fullLabel: 'Virtual Desktop' },
  { id: 'xbox', shortLabel: 'Xbox', fullLabel: 'Xbox Mode' },
];

export type OsdMode = 'off' | 'afterburner';

export const OSD_MODES: {
  id: OsdMode;
  shortLabel: string;
  fullLabel: string;
}[] = [
  { id: 'off', shortLabel: 'Off', fullLabel: 'Off' },
  { id: 'afterburner', shortLabel: 'Afterburner', fullLabel: 'Afterburner' },
];

export type RoomMode = 'office' | 'living-room' | 'streaming';

export interface ProfileSummary {
  id: string;
  name: string;
  isDefault?: boolean;
  available?: boolean;
  consoleEnvironmentMode?: string | null;
}

export interface SystemMetrics {
  cpuName?: string | null;
  cpuPercent?: number | null;
  cpuTempC?: number | null;
  ramName?: string | null;
  ramPercent?: number | null;
  ramUsedGb?: number | null;
  ramTotalGb?: number | null;
  gpuName?: string | null;
  gpuPercent?: number | null;
  gpuTempC?: number | null;
  gpuHotspotTempC?: number | null;
  gpuVramUsedGb?: number | null;
  gpuVramTotalGb?: number | null;
  fps?: number | null;
  frametimeMs?: number | null;
  fpsHistory?: (number | null)[] | null;
  frametimeHistoryMs?: (number | null)[] | null;
  frameAppName?: string | null;
  frameAppIcon?: string | null;
  frameAppHeroUrl?: string | null;
  osdEnabled?: boolean | null;
}

export interface HealthResponse {
  status?: string | null;
  version?: string | null;
  authEnabled?: boolean | null;
  mode?: string | null;
  underlyingMode?: string | null;
  activeProfileId?: string | null;
  activeProfileName?: string | null;
  defaultProfileId?: string | null;
  profiles?: ProfileSummary[] | null;
  streamingActive?: boolean | null;
  streamingDisplay?: string | null;
  streamingReason?: string | null;
  idleSeconds?: number | null;
  officeAvailable?: boolean | null;
  livingRoomAvailable?: boolean | null;
  audioDevice?: string | null;
  consoleEnvironmentOnLivingRoom?: boolean | null;
  consoleEnvironmentMode?: string | null;
  officeConsoleEnvironmentMode?: string | null;
  livingRoomConsoleEnvironmentMode?: string | null;
  wolMac?: string | null;
  wolBroadcastIp?: string | null;
  metrics?: SystemMetrics | null;
}

export interface DesktopShortcut {
  name: string;
  path: string;
  extension?: string | null;
  source?: string | null;
  icon?: string | null;
}

export interface OpenWindow {
  hwnd: number;
  pid?: number | null;
  process?: string | null;
  title?: string | null;
  icon?: string | null;
}

export interface ShortcutsResponse {
  shortcuts?: DesktopShortcut[] | null;
}

export interface WindowsResponse {
  windows?: OpenWindow[] | null;
}

export interface OsdResponse {
  status?: string | null;
  osdEnabled?: boolean | null;
}

export interface UpdateStatusResponse {
  status?: string | null;
  Status?: string | null;
  currentVersion?: string | null;
  CurrentVersion?: string | null;
  downloadProgressPercent?: number | null;
  DownloadProgressPercent?: number | null;
  errorMessage?: string | null;
  ErrorMessage?: string | null;
}

export interface ApiErrorResponse {
  error?: string | null;
}

export interface PCConnectionConfig {
  pcHost: string;
  pcPort: number;
  pcMac: string;
  broadcastIp: string;
  bearerToken: string;
  wakeTimeoutSeconds: number;
}

export function getBaseUrl(config: PCConnectionConfig): string | null {
  if (Platform.OS === 'web' && typeof window !== 'undefined') {
    const win = window as Window & { __PC_HOSTED__?: boolean };
    const loc = window.location;
    if (win.__PC_HOSTED__ && loc?.origin) {
      const locPort = Number(loc.port) || (loc.protocol === 'https:' ? 443 : 80);
      if (
        config.pcHost.trim() === loc.hostname &&
        config.pcPort === locPort
      ) {
        return loc.origin;
      }
    }
  }

  const trimmedHost = config.pcHost.trim();
  if (!trimmedHost || config.pcPort <= 0 || config.pcPort > 65535) {
    return null;
  }
  return `http://${trimmedHost}:${config.pcPort}`;
}

export function getWsBaseUrl(config: PCConnectionConfig): string | null {
  const baseUrl = getBaseUrl(config);
  if (!baseUrl) return null;
  if (baseUrl.startsWith('https://')) {
    return `wss://${baseUrl.slice('https://'.length)}`;
  }
  if (baseUrl.startsWith('http://')) {
    return `ws://${baseUrl.slice('http://'.length)}`;
  }
  return baseUrl;
}

export function normalizeHostInput(
  rawHost: string,
  fallbackPort: number
): { host: string; port: number } {
  let cleaned = rawHost.trim();
  let port = fallbackPort;

  cleaned = cleaned.replace(/^https?:\/\//i, '');
  const slashIdx = cleaned.indexOf('/');
  if (slashIdx !== -1) {
    cleaned = cleaned.slice(0, slashIdx);
  }

  const colonIdx = cleaned.lastIndexOf(':');
  if (colonIdx > 0 && !cleaned.includes(']')) {
    const maybePort = Number(cleaned.slice(colonIdx + 1));
    if (Number.isInteger(maybePort) && maybePort > 0 && maybePort <= 65535) {
      port = maybePort;
      cleaned = cleaned.slice(0, colonIdx);
    }
  }

  return { host: cleaned.trim(), port };
}
