import React, { useEffect, useState } from 'react';
import {
  AccessibilityInfo,
  Platform,
  Pressable,
  PressableProps,
  StyleProp,
  StyleSheet,
  View,
  ViewStyle,
} from 'react-native';
import { BlurView } from 'expo-blur';
import {
  GlassView,
  isGlassEffectAPIAvailable,
  isLiquidGlassAvailable,
} from 'expo-glass-effect';
import { Colors, Radii } from '../theme';

let cachedGlassSupport: boolean | null = null;

function checkNativeGlass(): boolean {
  if (Platform.OS !== 'ios') return false;
  if (cachedGlassSupport !== null) return cachedGlassSupport;
  try {
    cachedGlassSupport =
      isLiquidGlassAvailable() && isGlassEffectAPIAvailable();
  } catch {
    cachedGlassSupport = false;
  }
  return cachedGlassSupport;
}

/** Hook that returns true when iOS Liquid Glass is available and Reduce Transparency is off. */
export function useLiquidGlassEnabled(): boolean {
  const [enabled, setEnabled] = useState<boolean>(() => checkNativeGlass());

  useEffect(() => {
    if (Platform.OS !== 'ios' || !checkNativeGlass()) {
      return;
    }

    let mounted = true;
    AccessibilityInfo.isReduceTransparencyEnabled()
      .then((reduced) => {
        if (mounted) setEnabled(!reduced);
      })
      .catch(() => {});

    const sub = AccessibilityInfo.addEventListener(
      'reduceTransparencyChanged',
      (reduced) => {
        if (mounted) setEnabled(!reduced);
      }
    );

    return () => {
      mounted = false;
      sub.remove();
    };
  }, []);

  return enabled;
}

interface SteamSurfaceProps {
  children: React.ReactNode;
  style?: StyleProp<ViewStyle>;
  /** Use translucent glass/blur on supported platforms (best for hero overlays & floating cards). */
  glass?: boolean;
  /** Optional tint for iOS Liquid Glass. */
  tintColor?: string;
  /** Enables interactive Liquid Glass touch feedback on iOS. */
  isInteractive?: boolean;
  /** Adds Steam's signature #1a9fff top accent strip (like Featured cards). */
  featured?: boolean;
}

/**
 * Steam-styled surface container that adapts to platform capabilities:
 * - iOS 26+: Native Liquid Glass (`GlassView`) when `glass` is true
 * - Android / older iOS: `BlurView` dark frosted glass when `glass` is true
 * - Standard / Web: Crisp Steam dark slate card with subtle border
 */
export function SteamSurface({
  children,
  style,
  glass = false,
  tintColor = 'rgba(23, 29, 37, 0.55)',
  isInteractive = false,
  featured = false,
}: SteamSurfaceProps) {
  const liquidGlass = useLiquidGlassEnabled();

  const content = (
    <>
      {featured ? <View style={styles.featuredTopBar} /> : null}
      {children}
    </>
  );

  if (glass && liquidGlass) {
    return (
      <GlassView
        glassEffectStyle="regular"
        colorScheme="dark"
        tintColor={tintColor}
        isInteractive={isInteractive}
        style={[styles.baseCard, styles.glassCard, style]}
      >
        {content}
      </GlassView>
    );
  }

  if (glass && Platform.OS !== 'web') {
    return (
      <View style={[styles.baseCard, styles.blurWrapper, style]}>
        <BlurView
          intensity={28}
          tint="dark"
          experimentalBlurMethod={
            Platform.OS === 'android' ? 'dimezisBlurView' : undefined
          }
          style={StyleSheet.absoluteFill}
        />
        <View style={styles.blurOverlay} pointerEvents="none" />
        {content}
      </View>
    );
  }

  return (
    <View
      style={[styles.baseCard, glass && styles.webGlassCard, style]}
    >
      {content}
    </View>
  );
}

interface SteamPressableProps extends PressableProps {
  style?: StyleProp<ViewStyle>;
  rippleColor?: string;
}

/** Pressable with native Android Material ripple and crisp iOS/Web opacity feedback. */
export function SteamPressable({
  style,
  rippleColor = Colors.ripple,
  ...rest
}: SteamPressableProps) {
  return (
    <Pressable
      android_ripple={{ color: rippleColor, borderless: false }}
      style={({ pressed }) => [
        style,
        pressed && Platform.OS !== 'android' && styles.pressed,
      ]}
      {...rest}
    />
  );
}

const styles = StyleSheet.create({
  baseCard: {
    backgroundColor: Colors.surface,
    borderRadius: Radii.md,
    borderWidth: 1,
    borderColor: Colors.border,
    overflow: 'hidden',
  },
  glassCard: {
    backgroundColor: 'transparent',
    borderColor: Colors.borderStrong,
  },
  blurWrapper: {
    backgroundColor: 'rgba(27, 34, 44, 0.68)',
    borderColor: Colors.borderStrong,
  },
  blurOverlay: {
    position: 'absolute',
    top: 0,
    right: 0,
    bottom: 0,
    left: 0,
    backgroundColor: 'rgba(23, 29, 37, 0.42)',
  },
  webGlassCard: {
    backgroundColor: Colors.surfaceGlass,
    borderColor: Colors.borderStrong,
  },
  featuredTopBar: {
    height: 3,
    width: '100%',
    backgroundColor: Colors.steamBlue,
  },
  pressed: {
    opacity: 0.78,
  },
});
