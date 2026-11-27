const { AndroidConfig, withAndroidManifest } = require('@expo/config-plugins');

/**
 * Enables cleartext (HTTP) traffic on Android so the app can connect to
 * local CerberusService instances by hostname or LAN IP.
 */
module.exports = function withAndroidCleartextTraffic(config) {
  return withAndroidManifest(config, (mod) => {
    const mainApplication = AndroidConfig.Manifest.getMainApplicationOrThrow(mod.modResults);
    mainApplication.$['android:usesCleartextTraffic'] = 'true';
    return mod;
  });
};
