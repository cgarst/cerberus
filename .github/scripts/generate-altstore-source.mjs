import fs from 'node:fs';
import path from 'node:path';

const repo = process.env.GITHUB_REPOSITORY || 'cgarst/cerberus';
const [owner] = repo.split('/');
const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN || '';

const appJsonPath = path.resolve(process.cwd(), 'CerberusExpo/app.json');
const appJson = JSON.parse(fs.readFileSync(appJsonPath, 'utf8'));
const expo = appJson.expo;

const headers = {
  Accept: 'application/vnd.github+json',
  'User-Agent': 'cerberus-altstore-source-generator',
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

const CHANNEL_PRIORITY = {
  stable: 0,
  beta: 1,
  alpha: 2,
};

const CHANNEL_LABEL = {
  stable: 'Stable',
  beta: 'Beta',
  alpha: 'Alpha',
};

function extractVersion(release, ipaAsset) {
  if (release.tag_name === 'alpha') {
    const match = ipaAsset.name.match(/^Cerberus-(.+)-ios(?:-unsigned)?\.ipa$/i);
    if (match) {
      return match[1];
    }
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

const entries = [];

for (const release of releases) {
  if (release.draft) continue;

  const ipaAsset = (release.assets || []).find((asset) =>
    asset.name.toLowerCase().endsWith('.ipa')
  );
  if (!ipaAsset) continue;

  const version = extractVersion(release, ipaAsset);
  const channel = classifyChannel(release, version);
  const date = ipaAsset.updated_at || release.published_at || release.created_at;
  const notes = (release.body || '').trim();
  const description = notes
    ? `[${CHANNEL_LABEL[channel]}] ${notes}`
    : `[${CHANNEL_LABEL[channel]}] Cerberus ${version} (unsigned iOS IPA).`;

  entries.push({
    channel,
    priority: CHANNEL_PRIORITY[channel],
    versionObj: {
      version,
      date,
      localizedDescription: description,
      downloadURL: ipaAsset.browser_download_url,
      size: ipaAsset.size,
    },
  });
}

entries.sort((a, b) => {
  if (a.priority !== b.priority) {
    return a.priority - b.priority;
  }
  return new Date(b.versionObj.date).getTime() - new Date(a.versionObj.date).getTime();
});

const iconURL = `https://raw.githubusercontent.com/${repo}/master/CerberusExpo/assets/icon.png`;

const source = {
  name: expo.name || 'Cerberus',
  identifier: `${expo.ios?.bundleIdentifier || 'com.cerberus.app'}.source`,
  subtitle: 'Official Cerberus SideStore / AltStore source (stable, beta, alpha)',
  sourceURL: `https://raw.githubusercontent.com/${repo}/altstore/source.json`,
  iconURL,
  apps: [
    {
      name: expo.name || 'Cerberus',
      bundleIdentifier: expo.ios?.bundleIdentifier || 'com.cerberus.app',
      developerName: owner,
      subtitle: 'Remote PC room-state controller & Wake-on-LAN',
      localizedDescription:
        'Control CerberusService room profiles, display/audio switching, Wake-on-LAN, system telemetry, and RTSS OSD from your iPhone or iPad.',
      iconURL,
      tintColor: '#1a9fff',
      category: 'utilities',
      appPermissions: {
        entitlements: [],
        privacy: expo.ios?.infoPlist || {},
      },
      versions: entries.map((entry) => entry.versionObj),
    },
  ],
  news: [],
};

process.stdout.write(JSON.stringify(source, null, 2) + '\n');
