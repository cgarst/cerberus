import Constants from 'expo-constants';

const ALPHA_DATE_PREFIX_RE = /^(\d+\.\d+\.\d+-alpha\.\d{8})-/i;
const INTEGER_RE = /^\d+$/;

interface ParsedSemVer {
  major: number;
  minor: number;
  patch: number;
  prerelease: string[];
}

/**
 * Normalizes a SemVer string by trimming whitespace, stripping a leading `v`,
 * and removing any `+build` metadata suffix.
 */
export function normalizeVersionString(version?: string | null): string {
  if (!version) return '';
  let trimmed = version.trim();
  if (!trimmed) return '';
  if (trimmed.startsWith('v') || trimmed.startsWith('V')) {
    trimmed = trimmed.slice(1);
  }
  const plusIdx = trimmed.indexOf('+');
  if (plusIdx >= 0) {
    trimmed = trimmed.slice(0, plusIdx);
  }
  return trimmed.trim();
}

/**
 * Returns the normalized client app version from Expo config, or `''` if unavailable.
 */
export function getClientVersion(): string {
  return normalizeVersionString(Constants.expoConfig?.version ?? '');
}

function parseSemVer(raw: string): ParsedSemVer {
  const norm = normalizeVersionString(raw);
  const result: ParsedSemVer = { major: 0, minor: 0, patch: 0, prerelease: [] };
  if (!norm) return result;

  const dashIdx = norm.indexOf('-');
  const core = dashIdx >= 0 ? norm.slice(0, dashIdx) : norm;
  const pre = dashIdx >= 0 ? norm.slice(dashIdx + 1) : '';

  const coreParts = core.split('.');
  if (coreParts[0]) {
    const n = Number.parseInt(coreParts[0], 10);
    if (!Number.isNaN(n)) result.major = n;
  }
  if (coreParts[1]) {
    const n = Number.parseInt(coreParts[1], 10);
    if (!Number.isNaN(n)) result.minor = n;
  }
  if (coreParts[2]) {
    const n = Number.parseInt(coreParts[2], 10);
    if (!Number.isNaN(n)) result.patch = n;
  }

  if (pre.trim()) {
    result.prerelease = pre.split('.').filter((part) => part.length > 0);
  }
  return result;
}

/**
 * Compares two SemVer strings, returning `< 0` if `left < right`,
 * `0` if equal, or `> 0` if `left > right`.
 */
export function compareVersions(left: string, right: string): number {
  const a = parseSemVer(left);
  const b = parseSemVer(right);

  if (a.major !== b.major) return a.major - b.major;
  if (a.minor !== b.minor) return a.minor - b.minor;
  if (a.patch !== b.patch) return a.patch - b.patch;

  const aHasPre = a.prerelease.length > 0;
  const bHasPre = b.prerelease.length > 0;
  if (!aHasPre && !bHasPre) return 0;
  if (!aHasPre) return 1;
  if (!bHasPre) return -1;

  const len = Math.min(a.prerelease.length, b.prerelease.length);
  for (let i = 0; i < len; i++) {
    const pa = a.prerelease[i];
    const pb = b.prerelease[i];
    const aNum = INTEGER_RE.test(pa);
    const bNum = INTEGER_RE.test(pb);
    if (aNum && bNum) {
      const na = Number(pa);
      const nb = Number(pb);
      if (na !== nb) return na - nb;
    } else if (aNum !== bNum) {
      return aNum ? -1 : 1;
    } else {
      const la = pa.toLowerCase();
      const lb = pb.toLowerCase();
      if (la < lb) return -1;
      if (la > lb) return 1;
    }
  }

  return a.prerelease.length - b.prerelease.length;
}

/**
 * Returns `true` when `serverVersion` is strictly older than `clientVersion`.
 * Same-day alpha builds that differ only by commit SHA are not flagged as older
 * because git SHAs are not monotonically ordered.
 */
export function isServerVersionOlder(
  serverVersion: string | null | undefined,
  clientVersion: string | null | undefined
): boolean {
  const server = normalizeVersionString(serverVersion);
  const client = normalizeVersionString(clientVersion);
  if (!server || !client) return false;
  if (server.toLowerCase() === client.toLowerCase()) return false;

  const serverAlpha = ALPHA_DATE_PREFIX_RE.exec(server);
  const clientAlpha = ALPHA_DATE_PREFIX_RE.exec(client);
  if (
    serverAlpha &&
    clientAlpha &&
    serverAlpha[1].toLowerCase() === clientAlpha[1].toLowerCase()
  ) {
    return false;
  }

  return compareVersions(server, client) < 0;
}
