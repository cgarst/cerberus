import React, { useEffect, useLayoutEffect, useState } from 'react';
import {
  ActivityIndicator,
  Image,
  Platform,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  useWindowDimensions,
  View,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { LinearGradient } from 'expo-linear-gradient';
import { useNavigation, useRouter } from 'expo-router';
import { CONSOLE_ENVIRONMENT_MODES, getBaseUrl, OSD_MODES, OsdMode } from '../src/api/models';
import { PowerSection } from '../src/components/PowerSection';
import { RoomModeCard } from '../src/components/RoomModeCard';
import { ShortcutsPanel } from '../src/components/ShortcutsPanel';
import { SteamPressable, SteamSurface } from '../src/components/SteamSurface';
import { TelemetrySection } from '../src/components/TelemetrySection';
import { WindowsPanel } from '../src/components/WindowsPanel';
import { usePCRemote } from '../src/state/usePCRemote';
import { Colors, Radii } from '../src/theme';
import { triggerHaptic } from '../src/utils/haptics';
import { getCachedSteamHeroArt } from '../src/utils/steamHero';
import { isWakeOnLanSupported } from '../src/utils/wakeOnLan';

export default function HomeScreen() {
  const vm = usePCRemote();
  const router = useRouter();
  const navigation = useNavigation();
  const { width } = useWindowDimensions();
  const isWide = width >= 768;
  const isDesktop = width >= 1024;
  const isXL = width >= 1200;
  const [refreshing, setRefreshing] = useState(false);

  const runningGameName = vm.pcOnline ? vm.health?.metrics?.frameAppName : null;
  const backendHeroUrl = vm.pcOnline
    ? vm.health?.metrics?.frameAppHeroUrl
    : null;
  const [failedHeroUri, setFailedHeroUri] = useState<string | null>(null);
  const [heroImageAspect, setHeroImageAspect] = useState<{
    uri: string;
    ratio: number;
  } | null>(null);

  const baseUrl = getBaseUrl({
    pcHost: vm.pcHost,
    pcPort: vm.pcPort,
    pcMac: vm.pcMac,
    broadcastIp: vm.broadcastIp,
    bearerToken: vm.bearerToken,
    wakeTimeoutSeconds: vm.wakeTimeoutSeconds,
  });

  const resolvedBackendHeroUrl =
    backendHeroUrl && backendHeroUrl.startsWith('/') && baseUrl
      ? `${baseUrl}${backendHeroUrl}`
      : backendHeroUrl;

  const demoHeroArt =
    vm.isDemoMode && runningGameName
      ? getCachedSteamHeroArt(runningGameName)
      : null;

  const heroCandidateUri = resolvedBackendHeroUrl
    ? resolvedBackendHeroUrl
    : demoHeroArt
    ? failedHeroUri === demoHeroArt.heroUrl
      ? demoHeroArt.headerUrl
      : demoHeroArt.heroUrl
    : null;

  const heroUri =
    heroCandidateUri && failedHeroUri !== heroCandidateUri
      ? heroCandidateUri
      : null;
  const activeHeroAspectRatio =
    heroUri && heroImageAspect?.uri === heroUri ? heroImageAspect.ratio : null;

  useEffect(() => {
    if (!heroUri) return;
    let cancelled = false;
    Image.getSize(
      heroUri,
      (imgWidth, imgHeight) => {
        if (!cancelled && imgWidth > 0 && imgHeight > 0) {
          setHeroImageAspect({
            uri: heroUri,
            ratio: imgWidth / imgHeight,
          });
        }
      },
      () => {}
    );
    return () => {
      cancelled = true;
    };
  }, [heroUri]);

  useLayoutEffect(() => {
    navigation.setOptions({
      headerRight: () => (
        <View style={styles.headerActions}>
          <SteamPressable
            onPress={() => {
              triggerHaptic('light');
              void vm.refreshNow();
            }}
            hitSlop={10}
            style={styles.headerBtn}
          >
            <Ionicons name="refresh" size={18} color={Colors.text} />
          </SteamPressable>
          <SteamPressable
            onPress={() => {
              triggerHaptic('light');
              router.push('/settings');
            }}
            hitSlop={10}
            style={styles.headerBtn}
          >
            <Ionicons name="settings-outline" size={18} color={Colors.text} />
          </SteamPressable>
        </View>
      ),
    });
  }, [navigation, router, vm]);

  const onRefresh = async () => {
    setRefreshing(true);
    await vm.refreshNow();
    setRefreshing(false);
  };

  const heroGradientColors: [string, string, string] = vm.wakeInProgress
    ? ['#281f15', '#1d222c', Colors.bg]
    : !vm.pcOnline
    ? ['#161b22', '#19202a', Colors.bg]
    : vm.isStreaming
    ? ['#221b33', '#1c2433', Colors.bg]
    : vm.isLivingRoom
    ? ['#11283f', '#192638', Colors.bg]
    : ['#14281b', '#192533', Colors.bg];

  const heroImageOverlayColors: [string, string, string] = [
    'rgba(17, 23, 31, 0.42)',
    'rgba(17, 23, 31, 0.68)',
    'rgba(23, 29, 37, 0.94)',
  ];

  const heroTitle =
    vm.pcOnline && vm.health?.metrics?.frameAppName
      ? vm.health.metrics.frameAppName
      : 'Idle';

  return (
    <ScrollView
      style={styles.scroll}
      contentContainerStyle={styles.scrollContent}
      refreshControl={
        Platform.OS !== 'web' ? (
          <RefreshControl
            refreshing={refreshing}
            onRefresh={onRefresh}
            tintColor={Colors.steamBlue}
          />
        ) : undefined
      }
    >
      <View
        style={[
          styles.container,
          isWide && styles.containerTablet,
          isDesktop && styles.containerDesktop,
        ]}
      >
        {/* Steam Library Hero Header */}
        <View style={styles.heroBanner}>
          {heroUri ? (
            <Image
              source={{ uri: heroUri }}
              style={[
                styles.heroImage,
                activeHeroAspectRatio
                  ? { aspectRatio: activeHeroAspectRatio }
                  : styles.heroImagePending,
              ]}
              resizeMode="cover"
              onLoad={(e) => {
                const nativeEvent = e.nativeEvent as {
                  source?: { width?: number; height?: number };
                  target?: { naturalWidth?: number; naturalHeight?: number };
                };
                const width =
                  nativeEvent?.source?.width ??
                  nativeEvent?.target?.naturalWidth ??
                  0;
                const height =
                  nativeEvent?.source?.height ??
                  nativeEvent?.target?.naturalHeight ??
                  0;
                if (width > 0 && height > 0) {
                  setHeroImageAspect({
                    uri: heroUri,
                    ratio: width / height,
                  });
                }
              }}
              onError={() => setFailedHeroUri(heroUri)}
            />
          ) : null}
          <LinearGradient
            colors={heroUri ? heroImageOverlayColors : heroGradientColors}
            start={{ x: 0, y: 0 }}
            end={{ x: 1, y: 1 }}
            style={[
              styles.heroGradient,
              heroUri ? styles.heroGradientWithArt : null,
            ]}
          >
            {vm.isDemoMode || !vm.pcOnline ? (
              <View style={styles.heroTopRow}>
                <View style={styles.heroTags}>
                  {vm.isDemoMode ? (
                    <View style={styles.heroTag}>
                      <Text style={styles.heroTagText}>DEMO</Text>
                    </View>
                  ) : null}
                  {!vm.pcOnline ? (
                    <View style={styles.heroTag}>
                      <Text style={styles.heroTagText}>OFFLINE</Text>
                    </View>
                  ) : null}
                </View>
              </View>
            ) : null}

            <View style={styles.heroTitleBlock}>
              <Text style={styles.heroTitle} numberOfLines={1}>
                {heroTitle}
              </Text>
            </View>
          </LinearGradient>
        </View>

        {vm.lastError ? (
          <View style={styles.errorBanner}>
            <Ionicons name="warning" size={17} color={Colors.red} />
            <Text style={styles.errorText}>{vm.lastError}</Text>
            <SteamPressable onPress={vm.clearError} hitSlop={8}>
              <Ionicons name="close" size={16} color={Colors.textSecondary} />
            </SteamPressable>
          </View>
        ) : null}

        {vm.wakeInProgress ? (
          <WakingSection
            targetMode={vm.wakeTargetMode}
            onSwitchTarget={vm.switchWakeTarget}
            onCancel={vm.cancelWake}
          />
        ) : vm.pcOnline ? (
          <OnlineSection
            isWide={isWide}
            isDesktop={isDesktop}
            isXL={isXL}
          />
        ) : (
          <OfflineSection
            pcHost={vm.pcHost}
            pcPort={vm.pcPort}
            pcMac={vm.pcMac}
            busy={vm.busy}
            onWakeLivingRoom={() => void vm.switchMode('living-room')}
            onWakeOffice={() => void vm.switchMode('office')}
          />
        )}
      </View>
    </ScrollView>
  );
}

function OfflineSection({
  pcHost,
  pcPort,
  pcMac,
  busy,
  onWakeLivingRoom,
  onWakeOffice,
}: {
  pcHost: string;
  pcPort: number;
  pcMac: string;
  busy: boolean;
  onWakeLivingRoom: () => void;
  onWakeOffice: () => void;
}) {
  if (!isWakeOnLanSupported) {
    return (
      <View style={styles.offlineContainer}>
        <SteamSurface featured style={styles.webOfflineCard}>
          <View style={styles.webOfflineInner}>
            <View style={styles.webOfflineIconBox}>
              <Ionicons
                name="desktop-outline"
                size={22}
                color={Colors.steamBlue}
              />
            </View>
            <View style={styles.flexOne}>
              <Text style={styles.cardHeadline}>PC IS OFFLINE / ASLEEP</Text>
              <Text style={styles.cardSub}>
                CerberusService is not reachable. Make sure your PC is powered
                on and connected to the network.
              </Text>
            </View>
          </View>
        </SteamSurface>
        <Text style={styles.targetCaption}>
          TARGET: {pcHost}:{pcPort}
        </Text>
      </View>
    );
  }

  return (
    <View style={styles.offlineContainer}>
      <SteamSurface glass style={styles.wakePlayBar}>
        <SteamPressable
          onPress={() => {
            triggerHaptic('medium');
            onWakeLivingRoom();
          }}
          disabled={busy}
          style={[styles.steamGreenWrap, busy && styles.disabled]}
        >
          <LinearGradient
            colors={[Colors.steamGreenBright, Colors.steamGreenDark]}
            start={{ x: 0, y: 0 }}
            end={{ x: 0, y: 1 }}
            style={styles.steamGreenBtn}
          >
            <Ionicons name="play" size={16} color={Colors.textBright} />
            <Text style={styles.steamGreenText}>WAKE & LIVING ROOM</Text>
          </LinearGradient>
        </SteamPressable>

        <SteamPressable
          onPress={() => {
            triggerHaptic('light');
            onWakeOffice();
          }}
          disabled={busy}
          style={[styles.wakeOfficeBtn, busy && styles.disabled]}
        >
          <Ionicons name="desktop-outline" size={16} color={Colors.text} />
          <Text style={styles.wakeOfficeText}>WAKE TO OFFICE ONLY</Text>
        </SteamPressable>
      </SteamSurface>

      <Text style={styles.targetCaption}>
        TARGET: {pcHost}:{pcPort} · MAC {pcMac}
      </Text>
    </View>
  );
}

function WakingSection({
  targetMode,
  onSwitchTarget,
  onCancel,
}: {
  targetMode: string;
  onSwitchTarget: (mode: string) => void;
  onCancel: () => void;
}) {
  const targetIsOffice = targetMode === 'office';
  return (
    <View style={styles.offlineContainer}>
      <SteamSurface
        glass
        tintColor="rgba(229, 147, 38, 0.16)"
        style={styles.wakingCard}
      >
        <View style={styles.wakingInner}>
          <View style={styles.orangeIconBox}>
            <ActivityIndicator color={Colors.orange} />
          </View>
          <View style={styles.flexOne}>
            <Text style={styles.cardHeadline}>
              {targetIsOffice
                ? 'WAKING FOR OFFICE…'
                : 'WAKING FOR LIVING ROOM…'}
            </Text>
            <Text style={styles.cardSub}>
              Magic packet sent · waiting for CerberusService
            </Text>
          </View>
        </View>
      </SteamSurface>

      <SteamPressable
        onPress={() => {
          triggerHaptic('light');
          onSwitchTarget(targetIsOffice ? 'living-room' : 'office');
        }}
        style={styles.wakeOfficeBtn}
      >
        <Text style={styles.wakeOfficeText}>
          {targetIsOffice
            ? 'SWITCH TARGET TO LIVING ROOM'
            : 'SWITCH TARGET TO OFFICE'}
        </Text>
      </SteamPressable>

      <SteamPressable
        onPress={() => {
          triggerHaptic('warning');
          onCancel();
        }}
        style={styles.cancelWakeBtn}
      >
        <Text style={styles.cancelWakeText}>Cancel Wake</Text>
      </SteamPressable>
    </View>
  );
}

function OnlineSection({
  isWide,
  isDesktop,
  isXL,
}: {
  isWide: boolean;
  isDesktop: boolean;
  isXL: boolean;
}) {
  const vm = usePCRemote();
  const router = useRouter();

  const desktopSection = (
    <View style={styles.section}>
      <Text style={styles.sectionHeader}>CONSOLE ENVIRONMENT</Text>

      <View style={styles.steamSubNav}>
        {CONSOLE_ENVIRONMENT_MODES.map((m) => {
          const active = vm.currentConsoleEnvironmentMode === m.id;
          return (
            <SteamPressable
              key={m.id}
              disabled={vm.busy}
              onPress={() => {
                triggerHaptic('light');
                void vm.setConsoleEnvironmentMode(m.id);
              }}
              style={[
                styles.subNavTab,
                active && styles.subNavTabActive,
                vm.busy && styles.disabled,
              ]}
            >
              <Text
                style={[
                  styles.subNavLabel,
                  active && styles.subNavLabelActive,
                ]}
                numberOfLines={1}
              >
                {m.shortLabel}
              </Text>
              {active ? <View style={styles.subNavUnderline} /> : null}
            </SteamPressable>
          );
        })}
      </View>
    </View>
  );

  const desktopTilesSection = !isWide ? (
    <View style={styles.section}>
      <Text style={styles.sectionHeader}>DESKTOP</Text>
      <View style={styles.tilesRow}>
        <SteamPressable
          onPress={() => {
            triggerHaptic('light');
            router.push('/shortcuts');
          }}
          style={styles.navTilePressable}
        >
          <SteamSurface glass isInteractive style={styles.navTile}>
            <View style={styles.navTileTop}>
              <Ionicons
                name="grid-outline"
                size={19}
                color={Colors.steamBlue}
              />
              <View style={styles.countBadge}>
                <Text style={styles.countBadgeText}>
                  {vm.shortcuts.length}
                </Text>
              </View>
            </View>
            <Text style={styles.navTileTitle}>Shortcuts</Text>
          </SteamSurface>
        </SteamPressable>

        <SteamPressable
          onPress={() => {
            triggerHaptic('light');
            router.push('/windows');
          }}
          style={styles.navTilePressable}
        >
          <SteamSurface glass isInteractive style={styles.navTile}>
            <View style={styles.navTileTop}>
              <Ionicons
                name="browsers-outline"
                size={19}
                color={Colors.steamBlue}
              />
              <View style={styles.countBadge}>
                <Text style={styles.countBadgeText}>{vm.windows.length}</Text>
              </View>
            </View>
            <Text style={styles.navTileTitle}>Windows</Text>
          </SteamSurface>
        </SteamPressable>
      </View>
    </View>
  ) : null;

  const osdEnabled = vm.health?.metrics?.osdEnabled;
  const hasOsd = osdEnabled !== null && osdEnabled !== undefined;
  const currentOsdMode: OsdMode = osdEnabled ? 'afterburner' : 'off';

  const osdSection = hasOsd ? (
    <View style={styles.section}>
      <Text style={styles.sectionHeader}>OSD</Text>

      <View style={styles.steamSubNav}>
        {OSD_MODES.map((m) => {
          const active = currentOsdMode === m.id;
          return (
            <SteamPressable
              key={m.id}
              disabled={vm.busy}
              onPress={() => {
                triggerHaptic('light');
                if (!active) {
                  void vm.toggleOsd(m.id === 'afterburner');
                }
              }}
              style={[
                styles.subNavTab,
                active && styles.subNavTabActive,
                vm.busy && styles.disabled,
              ]}
            >
              <Text
                style={[
                  styles.subNavLabel,
                  active && styles.subNavLabelActive,
                ]}
                numberOfLines={1}
              >
                {m.shortLabel}
              </Text>
              {active ? <View style={styles.subNavUnderline} /> : null}
            </SteamPressable>
          );
        })}
      </View>
    </View>
  ) : null;

  if (!isWide) {
    return (
      <View style={styles.onlineColumn}>
        <RoomModeCard
          health={vm.health}
          defaultProfile={vm.defaultProfile}
          activeProfile={vm.activeProfile}
          nonDefaultProfiles={vm.nonDefaultProfiles}
          isStreaming={vm.isStreaming}
          isLivingRoom={vm.isLivingRoom}
          busy={vm.busy}
          pcHost={vm.pcHost}
          onSwitchMode={(m) => void vm.switchMode(m)}
          onStopStreaming={(m) => void vm.stopStreaming(m)}
        />
        {vm.health?.metrics ? (
          <TelemetrySection metrics={vm.health.metrics} />
        ) : null}
        {desktopSection}
        {desktopTilesSection}
        {osdSection}
        <PowerSection
          busy={vm.busy}
          onSleep={() => void vm.sleepPC()}
          onShutdown={() => void vm.shutdownPC()}
        />
      </View>
    );
  }

  if (!isDesktop) {
    return (
      <View style={styles.onlineColumn}>
        <RoomModeCard
          health={vm.health}
          defaultProfile={vm.defaultProfile}
          activeProfile={vm.activeProfile}
          nonDefaultProfiles={vm.nonDefaultProfiles}
          isStreaming={vm.isStreaming}
          isLivingRoom={vm.isLivingRoom}
          busy={vm.busy}
          pcHost={vm.pcHost}
          compact
          onSwitchMode={(m) => void vm.switchMode(m)}
          onStopStreaming={(m) => void vm.stopStreaming(m)}
        />

        <View style={styles.onlineGrid}>
          <View style={[styles.onlineColumn, styles.tabletLeftCol]}>
            {desktopSection}
            {osdSection}
            <WindowsPanel inline />
            <PowerSection
              busy={vm.busy}
              onSleep={() => void vm.sleepPC()}
              onShutdown={() => void vm.shutdownPC()}
            />
          </View>

          {vm.health?.metrics ? (
            <View style={[styles.onlineColumn, styles.flexOne]}>
              <TelemetrySection
                metrics={vm.health.metrics}
                alwaysShowGameStats
              />
            </View>
          ) : null}
        </View>

        <ShortcutsPanel inline columns={3} />
      </View>
    );
  }

  const leftWidth = isXL ? 400 : 350;
  const gaugesWidth = isXL ? 320 : 260;

  return (
    <View style={styles.onlineColumn}>
      <RoomModeCard
        health={vm.health}
        defaultProfile={vm.defaultProfile}
        activeProfile={vm.activeProfile}
        nonDefaultProfiles={vm.nonDefaultProfiles}
        isStreaming={vm.isStreaming}
        isLivingRoom={vm.isLivingRoom}
        busy={vm.busy}
        pcHost={vm.pcHost}
        compact
        onSwitchMode={(m) => void vm.switchMode(m)}
        onStopStreaming={(m) => void vm.stopStreaming(m)}
      />

      <View style={styles.onlineGrid}>
        <View style={[styles.onlineColumn, { width: leftWidth }]}>
          {desktopSection}
          {osdSection}
          <WindowsPanel inline />
          <PowerSection
            busy={vm.busy}
            onSleep={() => void vm.sleepPC()}
            onShutdown={() => void vm.shutdownPC()}
          />
        </View>

        <View style={[styles.onlineColumn, styles.flexOne]}>
          {vm.health?.metrics ? (
            <TelemetrySection
              metrics={vm.health.metrics}
              layout="desktop"
              gaugesWidth={gaugesWidth}
              alwaysShowGameStats
            />
          ) : null}
          <ShortcutsPanel inline columns={isXL ? 4 : 3} />
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  scroll: {
    flex: 1,
    backgroundColor: Colors.bg,
  },
  scrollContent: {
    paddingHorizontal: 14,
    paddingVertical: 12,
    alignItems: 'center',
  },
  container: {
    width: '100%',
    maxWidth: 560,
    gap: 12,
    paddingBottom: 28,
  },
  containerTablet: {
    maxWidth: 960,
  },
  containerDesktop: {
    maxWidth: 1320,
  },
  flexOne: { flex: 1 },
  disabled: { opacity: 0.45 },
  headerActions: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    marginRight: Platform.OS === 'ios' ? 0 : 14,
  },
  headerBtn: {
    padding: Platform.OS === 'ios' ? 4 : 6,
    ...(Platform.OS !== 'ios'
      ? {
          borderRadius: Radii.sm,
          backgroundColor: Colors.surfaceElevated,
          borderWidth: 1,
          borderColor: Colors.border,
        }
      : null),
  },
  heroBanner: {
    position: 'relative',
    borderRadius: Radii.md,
    overflow: 'hidden',
    borderWidth: 1,
    borderColor: Colors.border,
  },
  heroImage: {
    width: '100%',
  },
  heroImagePending: {
    minHeight: 136,
  },
  heroGradient: {
    padding: 14,
    gap: 12,
  },
  heroGradientWithArt: {
    position: 'absolute',
    top: 0,
    right: 0,
    bottom: 0,
    left: 0,
    justifyContent: 'space-between',
  },
  heroTopRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: 8,
  },
  heroTags: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  heroTag: {
    paddingHorizontal: 7,
    paddingVertical: 3,
    borderRadius: Radii.xs,
    backgroundColor: 'rgba(0, 0, 0, 0.38)',
    borderWidth: 1,
    borderColor: Colors.border,
  },
  heroTagText: {
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.7,
    color: Colors.textSecondary,
  },
  heroTitleBlock: {
    gap: 2,
    marginTop: 'auto',
  },
  heroTitle: {
    fontSize: 22,
    fontWeight: '800',
    letterSpacing: 1.1,
    color: Colors.textBright,
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    padding: 12,
    borderRadius: Radii.sm,
    backgroundColor: Colors.redBg,
    borderWidth: 1,
    borderColor: Colors.redBorder,
  },
  errorText: {
    flex: 1,
    fontSize: 13,
    color: Colors.text,
  },
  offlineContainer: {
    gap: 10,
  },
  webOfflineCard: {
    borderRadius: Radii.md,
  },
  webOfflineInner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    padding: 14,
  },
  webOfflineIconBox: {
    width: 42,
    height: 42,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  wakePlayBar: {
    padding: 12,
    gap: 10,
  },
  steamGreenWrap: {
    borderRadius: Radii.sm,
    overflow: 'hidden',
    borderWidth: 1,
    borderColor: Colors.steamGreenBorder,
  },
  steamGreenBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 12,
    paddingHorizontal: 18,
  },
  steamGreenText: {
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.8,
    color: Colors.textBright,
  },
  wakingCard: {
    borderColor: Colors.orangeBorder,
  },
  wakingInner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    padding: 14,
  },
  orangeIconBox: {
    width: 42,
    height: 42,
    borderRadius: Radii.sm,
    backgroundColor: Colors.orangeBg,
    borderWidth: 1,
    borderColor: Colors.orangeBorder,
    alignItems: 'center',
    justifyContent: 'center',
  },
  cardHeadline: {
    fontSize: 14,
    fontWeight: '800',
    letterSpacing: 0.7,
    color: Colors.textBright,
  },
  cardSub: {
    fontSize: 12,
    color: Colors.textSecondary,
    marginTop: 2,
    lineHeight: 17,
  },
  wakeOfficeBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 11,
    paddingHorizontal: 14,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
  },
  wakeOfficeText: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 0.6,
    color: Colors.text,
  },
  cancelWakeBtn: {
    alignItems: 'center',
    paddingVertical: 8,
  },
  cancelWakeText: {
    fontSize: 13,
    color: Colors.textSecondary,
  },
  targetCaption: {
    fontSize: 11,
    color: Colors.textMuted,
    textAlign: 'center',
    letterSpacing: 0.4,
    marginTop: 2,
  },
  onlineGrid: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 14,
  },
  onlineColumn: {
    gap: 12,
  },
  tabletLeftCol: {
    width: 330,
  },
  section: {
    gap: 8,
  },
  sectionHeader: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.textSecondary,
    paddingHorizontal: 2,
    letterSpacing: 0.9,
  },
  steamSubNav: {
    flexDirection: 'row',
    backgroundColor: Colors.chrome,
    borderRadius: Radii.sm,
    borderWidth: 1,
    borderColor: Colors.border,
    overflow: 'hidden',
  },
  subNavTab: {
    flex: 1,
    paddingVertical: 10,
    paddingHorizontal: 6,
    alignItems: 'center',
    justifyContent: 'center',
    position: 'relative',
  },
  subNavTabActive: {
    backgroundColor: Colors.surfaceElevated,
  },
  subNavLabel: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
  subNavLabelActive: {
    color: Colors.textBright,
    fontWeight: '700',
  },
  subNavUnderline: {
    position: 'absolute',
    bottom: 0,
    left: 0,
    right: 0,
    height: 2,
    backgroundColor: Colors.steamBlue,
  },
  tilesRow: {
    flexDirection: 'row',
    gap: 10,
  },
  navTilePressable: {
    flex: 1,
  },
  navTile: {
    padding: 12,
    gap: 8,
  },
  navTileTop: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  countBadge: {
    paddingHorizontal: 7,
    paddingVertical: 2,
    borderRadius: Radii.xs,
    backgroundColor: Colors.steamBlueBg,
    borderWidth: 1,
    borderColor: Colors.steamBlueBorder,
  },
  countBadgeText: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.steamBlue,
  },
  navTileTitle: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.text,
  },
});
