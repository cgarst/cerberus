import fs from 'node:fs';

const repo = process.env.GITHUB_REPOSITORY || 'cgarst/cerberus';
const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN || '';

const headers = {
  Accept: 'application/vnd.github+json',
  'User-Agent': 'cerberus-update-manifest-generator',
};
if (token) {
  headers.Authorization = `Bearer ${token}`;
}

const response = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=100`, {
  headers,
});

if (!response.ok) {
  const body = await response.text();
  throw new Error(`GitHub API error ${response.status}: ${body}`);
}

const releases = await response.json();

function extractVersion(release, setupAsset) {
  const match = setupAsset.name.match(/^CerberusServiceSetup-(.+)-win-x64\.exe$/i);
  if (match) {
    return match[1];
  }
  if (release.tag_name === 'alpha') {
    if (release.name) {
      return release.name.replace(/^Alpha\s+/i, '').trim();
    }
    return 'alpha';
  }
  return release.tag_name.replace(/^v/, '');
}

function classifyChannel(release, version) {
  if (release.tag_name === 'alpha' || version.includes('-alpha')) {
    return 'alpha';
  }
  if (release.prerelease || version.includes('-')) {
    return 'beta';
  }
  return 'stable';
}

async function fetchSha256(shaAsset) {
  if (!shaAsset) return '';
  const shaHeaders = {
    'User-Agent': 'cerberus-update-manifest-generator',
  };
  if (token) {
    shaHeaders.Authorization = `Bearer ${token}`;
  }
  const res = await fetch(shaAsset.browser_download_url, { headers: shaHeaders });
  if (!res.ok) return '';
  const text = (await res.text()).trim();
  const match = text.match(/([a-fA-F0-9]{64})/);
  return match ? match[1].toLowerCase() : '';
}

const channels = {};

for (const release of releases) {
  if (release.draft) continue;

  const assets = release.assets || [];
  const setupAsset = assets.find((asset) =>
    /^CerberusServiceSetup-.*-win-x64\.exe$/i.test(asset.name)
  );
  if (!setupAsset) continue;

  const version = extractVersion(release, setupAsset);
  const channel = classifyChannel(release, version);
  const publishedAt = setupAsset.updated_at || release.published_at || release.created_at;

  const existing = channels[channel];
  if (existing && new Date(existing.publishedAt).getTime() >= new Date(publishedAt).getTime()) {
    continue;
  }

  const shaAsset = assets.find(
    (asset) => asset.name.toLowerCase() === `${setupAsset.name.toLowerCase()}.sha256`
  );
  const sha256 = await fetchSha256(shaAsset);

  channels[channel] = {
    version,
    url: setupAsset.browser_download_url,
    sha256,
    size: setupAsset.size,
    publishedAt,
    releaseUrl: release.html_url,
    notes: (release.body || '').trim(),
  };
}

const manifest = {
  schemaVersion: 1,
  generatedAt: new Date().toISOString(),
  channels,
};

process.stdout.write(JSON.stringify(manifest, null, 2) + '\n');
