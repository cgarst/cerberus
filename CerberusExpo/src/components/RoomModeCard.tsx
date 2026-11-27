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
}: RoomModeCardProps) {
  const defaultUnavailable = defaultProfile.available === false;
  const primaryTarget =
    nonDefaultProfiles.find((p) => p.available !== false) ??
    nonDefaultProfiles[0] ??
    defaultProfile;
  const primaryUnavailable = primaryTarget.available === false;

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
              label={
                defaultUnavailable
                  ? `${defaultProfile.name.toUpperCase()} UNAVAILABLE`
                  : 'EXIT STREAM'
              }
              disabled={busy || defaultUnavailable}
              onPress={() => {
                triggerHaptic('medium');
                onStopStreaming(defaultProfile.id);
              }}
              style={!compact && styles.fullWidthBtn}
            />
          </View>

          {compact ? metaColumns : null}
        </View>
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
              label={
                defaultUnavailable
                  ? `${defaultProfile.name.toUpperCase()} UNAVAILABLE`
                  : `EXIT ${activeProfile.name.toUpperCase()}`
              }
              disabled={busy || defaultUnavailable}
              onPress={() => {
                triggerHaptic('medium');
                onSwitchMode(defaultProfile.id);
              }}
              style={!compact && otherRooms.length === 0 && styles.fullWidthBtn}
            />

            {otherRooms.map((profile) => {
              const unavailable = profile.available === false;
              return (
                <SteamPressable
                  key={profile.id}
                  onPress={() => {
                    triggerHaptic('light');
                    onSwitchMode(profile.id);
                  }}
                  disabled={busy || unavailable}
                  style={[
                    styles.secondaryBtn,
                    (busy || unavailable) && styles.disabled,
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
            label={
              primaryUnavailable
                ? `${primaryTarget.name.toUpperCase()} UNAVAILABLE`
                : `SWITCH TO ${primaryTarget.name.toUpperCase()}`
            }
            disabled={busy || primaryUnavailable}
            onPress={() => {
              triggerHaptic('medium');
              onSwitchMode(primaryTarget.id);
            }}
            style={!compact && extraTargets.length === 0 && styles.fullWidthBtn}
          />

          {extraTargets.map((profile) => {
            const unavailable = profile.available === false;
            return (
              <SteamPressable
                key={profile.id}
                onPress={() => {
                  triggerHaptic('medium');
                  onSwitchMode(profile.id);
                }}
                disabled={busy || unavailable}
                style={[
                  styles.secondaryBtn,
                  (busy || unavailable) && styles.disabled,
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
        ) : primaryUnavailable ? (
          <Text style={styles.mobileHint}>
            {`${primaryTarget.name} display is currently unavailable`}
          </Text>
        ) : null}
      </View>
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
  mobileHint: {
    fontSize: 12,
    color: Colors.textSecondary,
    textAlign: 'center',
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
