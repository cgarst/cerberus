import {
  ConsoleEnvironmentMode,
  DesktopShortcut,
  HealthResponse,
  OpenWindow,
  OsdMode,
} from '../api/models';

/** Build a crisp inline SVG badge data URL for demo shortcuts and windows. */
function badgeIcon(label: string, bg: string, fg = '#ffffff'): string {
  const safe = label
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">` +
    `<rect width="64" height="64" rx="14" fill="${bg}"/>` +
    `<rect x="1.5" y="1.5" width="61" height="61" rx="12.5" fill="none" stroke="rgba(255,255,255,0.18)" stroke-width="2"/>` +
    `<text x="32" y="39" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-size="22" font-weight="700" fill="${fg}">${safe}</text>` +
    `</svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

export interface DemoShortcutEntry extends DesktopShortcut {
  isGame: boolean;
  processName?: string;
}

export const DEMO_SYSTEM_SPECS = {
  pcName: 'VALKYRIE-PC',
  cpuName: 'Ryzen 7 7800X3D',
  ramName: '32 GB DDR5',
  ramTotalGb: 32,
  gpuName: 'Radeon RX 9070 XT',
  gpuVramTotalGb: 16,
  officeMonitor: 'Odyssey G80SD',
  officeAudio: 'Sound Blaster Play! 3',
  livingRoomMonitor: 'LG TV SSCR2',
  livingRoomAudio: 'LG TV SSCR2',
} as const;

export const DEMO_ACTIVE_GAME = 'The Blood of Dawnwalker';

export const DEMO_SHORTCUTS: DemoShortcutEntry[] = [
  {
    name: 'The Blood of Dawnwalker',
    path: 'C:\\Users\\Player\\Desktop\\The Blood of Dawnwalker.url',
    extension: '.url',
    source: 'User',
    icon: badgeIcon('BD', '#6b1d2f'),
    isGame: true,
    processName: 'Dawnwalker',
  },
  {
    name: 'Clair Obscur: Expedition 33',
    path: 'C:\\Users\\Public\\Desktop\\Clair Obscur - Expedition 33.lnk',
    extension: '.lnk',
    source: 'Public',
    icon: badgeIcon('33', '#2e2748', '#f3d19c'),
    isGame: true,
    processName: 'Sandfall-Win64-Shipping',
  },
  {
    name: 'Kingdom Come: Deliverance II',
    path: 'C:\\Users\\Player\\Desktop\\Kingdom Come Deliverance II.url',
    extension: '.url',
    source: 'User',
    icon: badgeIcon('KC', '#5a3a1e', '#f5dca8'),
    isGame: true,
    processName: 'KingdomCome',
  },
  {
    name: 'DOOM Eternal',
    path: 'C:\\Users\\Player\\Desktop\\DOOM Eternal.url',
    extension: '.url',
    source: 'User',
    icon: badgeIcon('DE', '#7a1c14', '#ffb347'),
    isGame: true,
    processName: 'DOOMEternalx64vk',
  },
  {
    name: 'HELLDIVERS 2',
    path: 'C:\\Users\\Player\\Desktop\\HELLDIVERS 2.url',
    extension: '.url',
    source: 'User',
    icon: badgeIcon('HD', '#262b30', '#ffd426'),
    isGame: true,
    processName: 'helldivers2',
  },
  {
    name: 'Cyberpunk 2077',
    path: 'C:\\Users\\Public\\Desktop\\Cyberpunk 2077.lnk',
    extension: '.lnk',
    source: 'Public',
    icon: badgeIcon('CP', '#1b2029', '#fcee0a'),
    isGame: true,
    processName: 'Cyberpunk2077',
  },
  {
    name: 'God of War Ragnarök',
    path: 'C:\\Users\\Public\\Desktop\\God of War - Ragnarok.lnk',
    extension: '.lnk',
    source: 'Public',
    icon: badgeIcon('GW', '#1c3b57', '#d9efff'),
    isGame: true,
    processName: 'GoWR',
  },
  {
    name: 'Halo Infinite',
    path: 'C:\\Users\\Player\\Desktop\\Halo Infinite.url',
    extension: '.url',
    source: 'User',
    icon: badgeIcon('HI', '#1e4234', '#9ee6c5'),
    isGame: true,
    processName: 'HaloInfinite',
  },
  {
    name: 'The Witcher 3: Wild Hunt',
    path: 'C:\\Users\\Player\\Desktop\\The Witcher 3 Wild Hunt.url',
    extension: '.url',
    source: 'User',
    icon: badgeIcon('W3', '#36191b', '#e6c88a'),
    isGame: true,
    processName: 'witcher3',
  },
  {
    name: 'Control Ultimate Edition',
    path: 'C:\\Users\\Player\\Desktop\\Control Ultimate Edition.url',
    extension: '.url',
    source: 'User',
    icon: badgeIcon('CT', '#231f24', '#ff5959'),
    isGame: true,
    processName: 'Control_DX12',
  },
  {
    name: 'Steam',
    path: 'C:\\Users\\Public\\Desktop\\Steam.lnk',
    extension: '.lnk',
    source: 'Public',
    icon: badgeIcon('ST', '#17375e', '#66c0f4'),
    isGame: false,
    processName: 'steamwebhelper',
  },
  {
    name: 'Steam Big Picture Mode',
    path: 'cerberus://shortcut/steam-big-picture',
    extension: 'uri',
    source: 'builtin',
    icon: badgeIcon('BP', '#1a4a7a', '#66c0f4'),
    isGame: false,
    processName: 'steamwebhelper',
  },
  {
    name: 'Xbox App',
    path: 'cerberus://shortcut/xbox-app',
    extension: 'app',
    source: 'builtin',
    icon: badgeIcon('XB', '#107c10', '#ffffff'),
    isGame: false,
    processName: 'XboxPcApp',
  },
  {
    name: 'File Explorer',
    path: 'C:\\Users\\Player\\Desktop\\File Explorer.lnk',
    extension: '.lnk',
    source: 'User',
    icon: badgeIcon('FE', '#4a3d18', '#ffd666'),
    isGame: false,
    processName: 'explorer',
  },
  {
    name: 'Windows Terminal',
    path: 'C:\\Users\\Player\\Desktop\\Windows Terminal.lnk',
    extension: '.lnk',
    source: 'User',
    icon: badgeIcon('>_', '#22272e', '#7ee787'),
    isGame: false,
    processName: 'WindowsTerminal',
  },
];

/** Open windows include at most one game alongside Steam and standard Windows utilities. */
export const DEMO_INITIAL_WINDOWS: OpenWindow[] = [
  {
    hwnd: 1001,
    pid: 18420,
    process: 'Dawnwalker',
    title: 'The Blood of Dawnwalker',
    icon: badgeIcon('BD', '#6b1d2f'),
  },
  {
    hwnd: 1002,
    pid: 9412,
    process: 'steamwebhelper',
    title: 'Steam Big Picture Mode',
    icon: badgeIcon('BP', '#1a4a7a', '#66c0f4'),
  },
  {
    hwnd: 1003,
    pid: 6240,
    process: 'explorer',
    title: 'File Explorer',
    icon: badgeIcon('FE', '#4a3d18', '#ffd666'),
  },
  {
    hwnd: 1004,
    pid: 11840,
    process: 'WindowsTerminal',
    title: 'Windows Terminal',
    icon: badgeIcon('>_', '#22272e', '#7ee787'),
  },
];

export function buildDemoSeed(): { fps: number[]; ft: number[] } {
  const fps = Array.from({ length: 60 }, (_, i) => {
    const v = 116 + Math.sin(i / 4) * 8 + (i % 19 === 0 ? -22 : 0);
    return Math.round(v * 10) / 10;
  });
  const ft = fps.map((f, idx) => {
    const v = 1000 / f + (idx % 19 === 0 ? 6.8 : 0);
    return Math.round(v * 100) / 100;
  });
  return { fps, ft };
}

export function buildDemoHealth(params: {
  tick: number;
  mode: string;
  streamingActive?: boolean;
  consoleEnvironmentMode: ConsoleEnvironmentMode;
  osdMode: OsdMode;
  activeGame: string | null;
  fpsHistory: number[];
  ftHistory: number[];
}): HealthResponse {
  const {
    tick,
    mode,
    streamingActive = false,
    consoleEnvironmentMode,
    osdMode,
    activeGame,
    fpsHistory,
    ftHistory,
  } = params;

  const isGameActive = Boolean(activeGame);
  const nextFps = isGameActive
    ? fpsHistory[fpsHistory.length - 1] ?? 116
    : null;
  const nextFt = isGameActive ? ftHistory[ftHistory.length - 1] ?? 8.6 : null;

  const cpuPercent = isGameActive
    ? Math.round((41 + Math.sin(tick / 5) * 4.5) * 10) / 10
    : Math.round((7 + Math.sin(tick / 6) * 2.1) * 10) / 10;
  const cpuTempC = isGameActive
    ? Math.round(63 + Math.sin(tick / 7) * 2)
    : 44;
  const gpuPercent = isGameActive
    ? Math.round(95 + Math.sin(tick / 4) * 3)
    : 4;
  const gpuTempC = isGameActive
    ? Math.round(68 + Math.sin(tick / 8) * 2)
    : 39;
  const gpuHotspotTempC = isGameActive ? gpuTempC + 13 : 44;
  const gpuVramUsedGb = isGameActive ? 11.8 : 1.9;
  const ramUsedGb = isGameActive ? 17.4 : 11.2;

  return {
    status: 'ok',
    mode: streamingActive ? 'streaming' : mode,
    underlyingMode: mode,
    activeProfileId: mode,
    activeProfileName: mode === 'living-room' ? 'Living Room' : 'Office',
    defaultProfileId: 'office',
    profiles: [
      {
        id: 'office',
        name: 'Office',
        isDefault: true,
        available: true,
        consoleEnvironmentMode: 'off',
      },
      {
        id: 'living-room',
        name: 'Living Room',
        isDefault: false,
        available: true,
        consoleEnvironmentMode: 'virtual-desktop',
      },
    ],
    streamingActive,
    streamingDisplay: streamingActive
      ? 'SudoMaker Virtual Display Adapter'
      : null,
    idleSeconds: 2,
    officeAvailable: true,
    livingRoomAvailable: true,
    audioDevice: streamingActive
      ? 'Steam Streaming Speakers'
      : mode === 'living-room'
      ? DEMO_SYSTEM_SPECS.livingRoomAudio
      : DEMO_SYSTEM_SPECS.officeAudio,
    consoleEnvironmentMode,
    officeConsoleEnvironmentMode: 'off',
    livingRoomConsoleEnvironmentMode: 'virtual-desktop',
    metrics: {
      cpuName: DEMO_SYSTEM_SPECS.cpuName,
      cpuPercent,
      cpuTempC,
      ramName: DEMO_SYSTEM_SPECS.ramName,
      ramPercent:
        Math.round((ramUsedGb / DEMO_SYSTEM_SPECS.ramTotalGb) * 1000) / 10,
      ramUsedGb,
      ramTotalGb: DEMO_SYSTEM_SPECS.ramTotalGb,
      gpuName: DEMO_SYSTEM_SPECS.gpuName,
      gpuPercent,
      gpuTempC,
      gpuHotspotTempC,
      gpuVramUsedGb,
      gpuVramTotalGb: DEMO_SYSTEM_SPECS.gpuVramTotalGb,
      fps: nextFps,
      frametimeMs: nextFt,
      fpsHistory: isGameActive ? fpsHistory : [],
      frametimeHistoryMs: isGameActive ? ftHistory : [],
      frameAppName: activeGame,
      frameAppIcon: null,
      osdEnabled: osdMode !== 'off',
      osdMode,
      availableOsdModes: ['off', 'afterburner', 'radeon'],
    },
  };
}
