const { withAppBuildGradle } = require('@expo/config-plugins');

/**
 * Configures Android release signing from environment variables when present:
 * - ANDROID_KEYSTORE_FILE
 * - ANDROID_KEYSTORE_PASSWORD
 * - ANDROID_KEY_ALIAS
 * - ANDROID_KEY_PASSWORD
 *
 * Falls back to debug signing for local builds when ANDROID_KEYSTORE_FILE is unset.
 */
module.exports = function withAndroidSigning(config) {
  return withAppBuildGradle(config, (mod) => {
    let contents = mod.modResults.contents;

    if (!contents.includes('signingConfigs {\n        release {') && !contents.includes('signingConfigs {\r\n        release {')) {
      const releaseSigningBlock = `signingConfigs {
        release {
            def keystorePath = System.getenv("ANDROID_KEYSTORE_FILE")
            if (keystorePath) {
                storeFile file(keystorePath)
                storePassword System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias System.getenv("ANDROID_KEY_ALIAS")
                keyPassword System.getenv("ANDROID_KEY_PASSWORD")
            }
        }`;

      contents = contents.replace(/signingConfigs\s*\{/, releaseSigningBlock);
    }

    contents = contents.replace(
      /(buildTypes\s*\{[\s\S]*?release\s*\{[\s\S]*?)signingConfig\s+signingConfigs\.debug/,
      '$1signingConfig System.getenv("ANDROID_KEYSTORE_FILE") ? signingConfigs.release : signingConfigs.debug'
    );

    mod.modResults.contents = contents;
    return mod;
  });
};
