import React from 'react';
import { StyleProp, StyleSheet, Text, View, ViewStyle } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { LinearGradient } from 'expo-linear-gradient';
import { HealthResponse, ProfileSummary } from '../api/models';
import { Colors, Radii } from '../theme';
import { triggerHaptic } from '../utils/haptics';
import { SteamPressable, SteamSurface } from './SteamSurface';

interface RoomModeCardProps {
  health: HealthResponse | null;
  defaultProfile: ProfileSummary;
  activeProfile: ProfileSummary;
  nonDefaultProfiles: ProfileSummary[];
  isStreaming: boolean;
  isLivingRoom: boolean;
  busy: boolean;
  pcHost?: string;
  compact?: boolean;
  onSwitchMode: (profileId: string) => void;
  onStopStreaming: (targetProfileId: string) => void;
  onCancelPending?: () => void;
}

interface PrimaryActionButtonProps {
  variant: 'green' | 'blue';
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  disabled?: boolean;
  onPress: () => void;
  style?: StyleProp<ViewStyle>;
}

/** Steam-styled primary CTA button: Green for Play/Switch, Blue for Stop/Exit. */
function PrimaryActionButton({
  variant,
  icon,
  label,
  disabled = false,
  onPress,
  style,
}: PrimaryActionButtonProps) {
  const gradientColors: [string, string] =
    variant === 'blue'
      ? [Colors.steamBlueBright, Colors.steamBlueDeep]
      : [Colors.steamGreenBright, Colors.steamGreenDark];

  return (
    <SteamPressable
      onPress={onPress}
      disabled={disabled}
      style={[
        styles.primaryBtnWrap,
        variant === 'blue' ? styles.primaryBlueBorder : styles.primaryGreenBorder,
        disabled && styles.disabled,
        style,
      ]}
    >
      <LinearGradient
        colors={gradientColors}
        start={{ x: 0, y: 0 }}
        end={{ x: 0, y: 1 }}
        style={styles.primaryBtnInner}
      >
        <Ionicons name={icon} size={15} color={Colors.textBright} />
        <Text style={styles.primaryBtnText} numberOfLines={1}>
          {label}
        </Text>
      </LinearGradient>
    </SteamPressable>
  );
}

export function RoomModeCard({
  health,
  defaultProfile,
  activeProfile,
  nonDefaultProfiles,
  isStreaming,
  isLivingRoom,
  busy,
  pcHost,
  compact = false,
  onSwitchMode,
  onStopStreaming,
  onCancelPending,
}: RoomModeCardProps) {
  const defaultUnavailable = defaultProfile.available === false;
  const primaryTarget =
    nonDefaultProfiles.find((p) => p.available !== false) ??
    nonDefaultProfiles[0];
  const primaryUnavailable = primaryTarget?.available === false;
  const pendingProfileId = health?.pendingProfileId?.trim() || '';
  const pendingProfile = pendingProfileId
    ? [defaultProfile, ...nonDefaultProfiles].find(
        (p) => p.id.toLowerCase() === pendingProfileId.toLowerCase()
      ) ?? { id: pendingProfileId, name: pendingProfileId }
    : null;
  const pendingLabel = pendingProfile
    ? health?.pendingSwitchReason === 'waiting-for-display-ready'
      ? `Waiting for ${pendingProfile.name} TV/HDR to come online...`
      : `Waiting for ${pendingProfile.name} display to come online...`
    : null;

  const soundLabel =
    health?.audioDevice?.trim() ||
    (isStreaming ? 'Virtual' : activeProfile.name);

  const metaColumns = (
    <View style={styles.metaRow}>
      <View style={styles.metaCol}>
        <Text style={styles.metaLabel}>DISPLAY</Text>
        <Text style={styles.metaValue} numberOfLines={1}>
          {isStreaming ? 'Virtual' : activeProfile.name}
        </Text>
      </View>
      <View style={[styles.metaCol, styles.soundMetaCol]}>
        <Text style={styles.metaLabel}>SOUND</Text>
        <Text style={styles.metaValue} numberOfLines={1}>
          {soundLabel}
        </Text>
      </View>
      {pcHost ? (
        <View style={styles.metaCol}>
          <Text style={styles.metaLabel}>HOST</Text>
          <Text style={styles.metaValue} numberOfLines={1}>
            {pcHost}
          </Text>
        </View>
      ) : null}
    </View>
  );

  if (isStreaming) {
    return (
      <SteamSurface
        glass
        tintColor="rgba(167, 139, 250, 0.16)"
        style={[styles.playBarCard, styles.streamingBorder]}
      >
        <View
          style={[
            styles.playBarInner,
            compact && styles.playBarInnerCompact,
          ]}
        >
          <View style={styles.playCluster}>
            <PrimaryActionButton
              variant="blue"
              icon="close"
              label="EXIT STREAM"
              disabled={busy}
              onPress={() => {
                triggerHaptic('medium');
                onStopStreaming(defaultUnavailable ? '' : defaultProfile.id);
              }}
              style={!compact && styles.fullWidthBtn}
            />
          </View>

          {compact ? metaColumns : null}
        </View>
        {pendingLabel ? (
          <View style={styles.pendingBanner}>
            <Text style={styles.pendingBannerText}>{pendingLabel}</Text>
            {onCancelPending ? (
              <SteamPressable
                onPress={() => {
                  triggerHaptic('light');
                  onCancelPending();
                }}
                disabled={busy}
                style={styles.cancelPendingBtn}
              >
                <Text style={styles.cancelPendingText}>Cancel</Text>
              </SteamPressable>
            ) : null}
          </View>
        ) : null}
      </SteamSurface>
    );
  }

  if (isLivingRoom) {
    const otherRooms = nonDefaultProfiles.filter(
      (p) => p.id.toLowerCase() !== activeProfile.id.toLowerCase()
    );

    return (
      <SteamSurface
        glass
        tintColor="rgba(26, 159, 255, 0.16)"
        style={[styles.playBarCard, styles.livingBorder]}
      >
        <View
          style={[
            styles.playBarInner,
            compact && styles.playBarInnerCompact,
          ]}
        >
          <View style={styles.playCluster}>
            <PrimaryActionButton
              variant="blue"
              icon="close"
              label={`EXIT ${activeProfile.name.toUpperCase()}`}
              disabled={busy}
              onPress={() => {
                triggerHaptic('medium');
                onSwitchMode(defaultProfile.id);
              }}
              style={!compact && otherRooms.length === 0 && styles.fullWidthBtn}
            />

            {otherRooms.map((profile) => {
              return (
                <SteamPressable
                  key={profile.id}
                  onPress={() => {
                    triggerHaptic('light');
                    onSwitchMode(profile.id);
                  }}
                  disabled={busy}
                  style={[
                    styles.secondaryBtn,
                    busy && styles.disabled,
                  ]}
                >
                  <Ionicons
                    name="swap-horizontal"
                    size={14}
                    color={Colors.text}
                  />
                  <Text style={styles.secondaryBtnText} numberOfLines={1}>
                    {profile.name.toUpperCase()}
                  </Text>
                </SteamPressable>
              );
            })}
          </View>

          {compact ? metaColumns : null}
        </View>
        {pendingLabel ? (
          <View style={styles.pendingBanner}>
            <Text style={styles.pendingBannerText}>{pendingLabel}</Text>
            {onCancelPending ? (
              <SteamPressable
                onPress={() => {
                  triggerHaptic('light');
                  onCancelPending();
                }}
                disabled={busy}
                style={styles.cancelPendingBtn}
              >
                <Text style={styles.cancelPendingText}>Cancel</Text>
              </SteamPressable>
            ) : null}
          </View>
        ) : null}
      </SteamSurface>
    );
  }

  if (!primaryTarget) {
    if (!compact) {
      return null;
    }

    return (
      <SteamSurface glass style={styles.playBarCard}>
        <View style={[styles.playBarInner, styles.playBarInnerCompact]}>
          {metaColumns}
        </View>
      </SteamSurface>
    );
  }

  const extraTargets = nonDefaultProfiles.filter(
    (p) => p.id.toLowerCase() !== primaryTarget.id.toLowerCase()
  );

  return (
    <SteamSurface glass style={styles.playBarCard}>
      <View
        style={[
          styles.playBarInner,
          compact && styles.playBarInnerCompact,
        ]}
      >
        <View style={styles.playCluster}>
          <PrimaryActionButton
            variant="green"
            icon="play"
            label={`SWITCH TO ${primaryTarget.name.toUpperCase()}`}
            disabled={busy}
            onPress={() => {
              triggerHaptic('medium');
              onSwitchMode(primaryTarget.id);
            }}
            style={!compact && extraTargets.length === 0 && styles.fullWidthBtn}
          />

          {extraTargets.map((profile) => {
            return (
              <SteamPressable
                key={profile.id}
                onPress={() => {
                  triggerHaptic('medium');
                  onSwitchMode(profile.id);
                }}
                disabled={busy}
                style={[
                  styles.secondaryBtn,
                  busy && styles.disabled,
                ]}
              >
                <Ionicons name="play" size={12} color={Colors.text} />
                <Text style={styles.secondaryBtnText} numberOfLines={1}>
                  {profile.name.toUpperCase()}
                </Text>
              </SteamPressable>
            );
          })}
        </View>

        {compact ? (
          metaColumns
        ) : pendingLabel ? (
          <View style={styles.pendingBanner}>
            <Text style={styles.pendingBannerText}>{pendingLabel}</Text>
            {onCancelPending ? (
              <SteamPressable
                onPress={() => {
                  triggerHaptic('light');
                  onCancelPending();
                }}
                disabled={busy}
                style={styles.cancelPendingBtn}
              >
                <Text style={styles.cancelPendingText}>Cancel</Text>
              </SteamPressable>
            ) : null}
          </View>
        ) : null}
      </View>
      {compact && pendingLabel ? (
        <View style={[styles.pendingBanner, styles.pendingBannerCompact]}>
          <Text style={styles.pendingBannerText}>{pendingLabel}</Text>
          {onCancelPending ? (
            <SteamPressable
              onPress={() => {
                triggerHaptic('light');
                onCancelPending();
              }}
              disabled={busy}
              style={styles.cancelPendingBtn}
            >
              <Text style={styles.cancelPendingText}>Cancel</Text>
            </SteamPressable>
          ) : null}
        </View>
      ) : null}
    </SteamSurface>
  );
}

const styles = StyleSheet.create({
  disabled: { opacity: 0.45 },
  playBarCard: {
    borderRadius: Radii.md,
  },
  streamingBorder: {
    borderColor: Colors.purpleBorder,
  },
  livingBorder: {
    borderColor: Colors.steamBlueBorder,
  },
  playBarInner: {
    padding: 12,
    gap: 10,
  },
  playBarInnerCompact: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 16,
  },
  playCluster: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: 8,
  },
  primaryBtnWrap: {
    borderRadius: Radii.sm,
    overflow: 'hidden',
    borderWidth: 1,
  },
  primaryGreenBorder: {
    borderColor: Colors.steamGreenBorder,
  },
  primaryBlueBorder: {
    borderColor: Colors.steamBlueButtonBorder,
  },
  fullWidthBtn: {
    width: '100%',
  },
  primaryBtnInner: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 11,
    paddingHorizontal: 18,
  },
  primaryBtnText: {
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.8,
    color: Colors.textBright,
  },
  secondaryBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    paddingVertical: 10,
    paddingHorizontal: 14,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
  },
  secondaryBtnText: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 0.6,
    color: Colors.text,
  },
  pendingBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 10,
    paddingHorizontal: 12,
    paddingVertical: 8,
    marginHorizontal: 12,
    marginBottom: 10,
    borderRadius: Radii.sm,
    backgroundColor: 'rgba(26, 159, 255, 0.14)',
    borderWidth: 1,
    borderColor: Colors.steamBlueBorder,
  },
  pendingBannerCompact: {
    marginTop: -2,
  },
  pendingBannerText: {
    flex: 1,
    fontSize: 12,
    fontWeight: '600',
    color: Colors.text,
  },
  cancelPendingBtn: {
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
  },
  cancelPendingText: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.text,
  },
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 22,
    flexWrap: 'wrap',
  },
  metaCol: {
    gap: 2,
  },
  soundMetaCol: {
    maxWidth: 200,
    flexShrink: 1,
  },
  metaLabel: {
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.7,
    color: Colors.textSecondary,
  },
  metaValue: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.text,
  },
});
