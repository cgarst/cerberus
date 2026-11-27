export interface SteamHeroArt {
  appId: number;
  heroUrl: string;
  headerUrl: string;
}

const STEAM_CDN_BASE =
  'https://shared.fastly.steamstatic.com/store_item_assets/steam/apps';

function buildHeroArt(appId: number): SteamHeroArt {
  return {
    appId,
    heroUrl: `${STEAM_CDN_BASE}/${appId}/library_hero.jpg`,
    headerUrl: `${STEAM_CDN_BASE}/${appId}/header.jpg`,
  };
}

// Pre-seeded fallback entries (ensures demo mode and web CORS environments still resolve)
const KNOWN_STEAM_APPS: Record<string, number> = {
  'blood of the dawnwalker': 3751260,
  'the blood of dawnwalker': 3751260,
  'kingdom come: deliverance ii': 1771300,
  'doom eternal': 782330,
  'helldivers 2': 553850,
  'halo infinite': 1240440,
  'the witcher 3: wild hunt': 292030,
  'control ultimate edition': 870780,
  'clair obscur: expedition 33': 1903340,
  'cyberpunk 2077': 1091500,
  'god of war ragnarök': 2322010,
};

const heroCache = new Map<string, SteamHeroArt | null>();
const inflightRequests = new Map<string, Promise<SteamHeroArt | null>>();

export function normalizeSteamQuery(appName: string): string {
  return appName
    .trim()
    .replace(/\.exe$/i, '')
    .replace(/\s+/g, ' ')
    .trim();
}

export function getCachedSteamHeroArt(
  appName?: string | null
): SteamHeroArt | null | undefined {
  if (!appName) return null;
  const normalized = normalizeSteamQuery(appName).toLowerCase();
  if (!normalized) return null;
  if (heroCache.has(normalized)) {
    return heroCache.get(normalized);
  }
  const knownId = KNOWN_STEAM_APPS[normalized];
  if (knownId) {
    const art = buildHeroArt(knownId);
    heroCache.set(normalized, art);
    return art;
  }
  return undefined;
}

interface SteamStoreSearchResponse {
  items?: {
    id?: number;
    name?: string;
    type?: string;
  }[];
}

export async function fetchSteamHeroArt(
  appName?: string | null
): Promise<SteamHeroArt | null> {
  if (!appName) return null;
  const query = normalizeSteamQuery(appName);
  if (!query) return null;

  const key = query.toLowerCase();
  const cached = getCachedSteamHeroArt(query);
  if (cached !== undefined) {
    return cached;
  }

  const existing = inflightRequests.get(key);
  if (existing) {
    return existing;
  }

  const promise = (async (): Promise<SteamHeroArt | null> => {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 5000);
    try {
      const url = `https://store.steampowered.com/api/storesearch/?term=${encodeURIComponent(
        query
      )}&l=english&cc=US`;
      const resp = await fetch(url, {
        method: 'GET',
        signal: controller.signal,
      });
      if (!resp.ok) {
        heroCache.set(key, null);
        return null;
      }
      const data = (await resp.json()) as SteamStoreSearchResponse;
      const items = data.items ?? [];
      const exact = items.find(
        (item) =>
          typeof item.id === 'number' &&
          item.name?.trim().toLowerCase() === key
      );
      const match =
        exact ??
        items.find(
          (item) => typeof item.id === 'number' && item.type === 'app'
        ) ??
        items.find((item) => typeof item.id === 'number');

      if (!match || typeof match.id !== 'number') {
        heroCache.set(key, null);
        return null;
      }

      const art = buildHeroArt(match.id);
      heroCache.set(key, art);
      return art;
    } catch {
      // Network error or web CORS restriction; don't poison cache permanently on transient failure
      return null;
    } finally {
      clearTimeout(timer);
      inflightRequests.delete(key);
    }
  })();

  inflightRequests.set(key, promise);
  return promise;
}
