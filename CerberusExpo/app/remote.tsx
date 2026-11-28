import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  GestureResponderEvent,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { Stack } from 'expo-router';
import {
  RemoteInputClient,
  RemoteInputConnectionState,
} from '../src/api/remoteInputClient';
import { SteamPressable, SteamSurface } from '../src/components/SteamSurface';
import { usePCRemote } from '../src/state/usePCRemote';
import { Colors, Radii } from '../src/theme';
import { triggerHaptic } from '../src/utils/haptics';

type SensitivityLevel = 'slow' | 'med' | 'fast';

const SENSITIVITY_MULTIPLIERS: Record<SensitivityLevel, number> = {
  slow: 0.7,
  med: 1.25,
  fast: 2.0,
};

const SENSITIVITY_LABELS: Record<SensitivityLevel, string> = {
  slow: 'Slow',
  med: 'Med',
  fast: 'Fast',
};

const SENSITIVITY_ORDER: SensitivityLevel[] = ['slow', 'med', 'fast'];

interface QuickKeyDef {
  id: string;
  label: string;
  vk?: number;
  combo?: number[];
}

const QUICK_KEYS: QuickKeyDef[] = [
  { id: 'enter', label: 'Enter', vk: 0x0d },
  { id: 'esc', label: 'Esc', vk: 0x1b },
  { id: 'tab', label: 'Tab', vk: 0x09 },
  { id: 'shift-tab', label: 'Shift+Tab', combo: [0x10, 0x09] },
  { id: 'win-g', label: 'Win+G', combo: [0x5b, 0x47] },
  { id: 'win', label: 'Win', vk: 0x5b },
  { id: 'alt-tab', label: 'Alt+Tab', combo: [0x12, 0x09] },
  { id: 'alt-f4', label: 'Alt+F4', combo: [0x12, 0x73] },
  { id: 'left', label: '←', vk: 0x25 },
  { id: 'up', label: '↑', vk: 0x26 },
  { id: 'down', label: '↓', vk: 0x28 },
  { id: 'right', label: '→', vk: 0x27 },
  { id: 'vol-down', label: 'Vol -', vk: 0xae },
  { id: 'vol-mute', label: 'Mute', vk: 0xad },
  { id: 'vol-up', label: 'Vol +', vk: 0xaf },
  { id: 'play-pause', label: 'Play/Pause', vk: 0xb3 },
];

export default function RemoteInputScreen() {
  const vm = usePCRemote();
  const insets = useSafeAreaInsets();
  const [connState, setConnState] = useState<RemoteInputConnectionState>(
    vm.isDemoMode ? 'connected' : 'connecting'
  );
  const [sensitivity, setSensitivity] = useState<SensitivityLevel>('med');
  const [dragLock, setDragLock] = useState(false);
  const [touchActive, setTouchActive] = useState(false);
  const [textDraft, setTextDraft] = useState('');

  const clientRef = useRef<RemoteInputClient | null>(null);
  const sensitivityRef = useRef<SensitivityLevel>(sensitivity);
  const dragLockRef = useRef<boolean>(dragLock);
  const isDemoRef = useRef<boolean>(vm.isDemoMode);

  useEffect(() => {
    sensitivityRef.current = sensitivity;
  }, [sensitivity]);

  useEffect(() => {
    dragLockRef.current = dragLock;
  }, [dragLock]);

  useEffect(() => {
    isDemoRef.current = vm.isDemoMode;
  }, [vm.isDemoMode]);

  // Sub-pixel accumulators and batched frame state for 60Hz pointer streaming
  const pendingDxRef = useRef(0);
  const pendingDyRef = useRef(0);
  const pendingScrollYRef = useRef(0);
  const rafIdRef = useRef<number | null>(null);

  const gestureMetaRef = useRef({
    startTime: 0,
    maxTouches: 1,
    totalMoveDist: 0,
    lastX: 0,
    lastY: 0,
    lastTouchCount: 0,
  });

  useEffect(() => {
    if (vm.isDemoMode) {
      return;
    }

    const client = new RemoteInputClient(
      {
        pcHost: vm.pcHost,
        pcPort: vm.pcPort,
        pcMac: vm.pcMac,
        broadcastIp: vm.broadcastIp,
        bearerToken: vm.bearerToken,
        wakeTimeoutSeconds: vm.wakeTimeoutSeconds,
      },
      (nextState) => setConnState(nextState)
    );
    clientRef.current = client;
    client.connect();

    return () => {
      if (dragLockRef.current) {
        client.sendButton('left', 'up');
      }
      client.disconnect();
      clientRef.current = null;
      if (rafIdRef.current !== null) {
        cancelAnimationFrame(rafIdRef.current);
        rafIdRef.current = null;
      }
    };
  }, [
    vm.isDemoMode,
    vm.pcHost,
    vm.pcPort,
    vm.pcMac,
    vm.broadcastIp,
    vm.bearerToken,
    vm.wakeTimeoutSeconds,
  ]);

  const flushMotion = useCallback(() => {
    rafIdRef.current = null;
    if (isDemoRef.current) {
      pendingDxRef.current = 0;
      pendingDyRef.current = 0;
      pendingScrollYRef.current = 0;
      return;
    }

    const client = clientRef.current;
    if (!client) return;

    const intDx = Math.trunc(pendingDxRef.current);
    const intDy = Math.trunc(pendingDyRef.current);
    if (intDx !== 0 || intDy !== 0) {
      pendingDxRef.current -= intDx;
      pendingDyRef.current -= intDy;
      client.sendMove(intDx, intDy);
    }

    const intScroll = Math.trunc(pendingScrollYRef.current);
    if (intScroll !== 0) {
      pendingScrollYRef.current -= intScroll;
      client.sendScroll(0, intScroll);
    }
  }, []);

  const scheduleFlush = useCallback(() => {
    if (rafIdRef.current !== null) return;
    rafIdRef.current = requestAnimationFrame(flushMotion);
  }, [flushMotion]);

  const getTouchCentroid = (evt: GestureResponderEvent) => {
    const touches = evt.nativeEvent.touches;
    if (!touches || touches.length === 0) {
      return {
        x: evt.nativeEvent.locationX,
        y: evt.nativeEvent.locationY,
        count: 1,
      };
    }
    let sumX = 0;
    let sumY = 0;
    for (let i = 0; i < touches.length; i++) {
      sumX += touches[i].pageX;
      sumY += touches[i].pageY;
    }
    return {
      x: sumX / touches.length,
      y: sumY / touches.length,
      count: touches.length,
    };
  };

  const handleTrackpadGrant = (evt: GestureResponderEvent) => {
    const c = getTouchCentroid(evt);
    gestureMetaRef.current = {
      startTime: Date.now(),
      maxTouches: c.count,
      totalMoveDist: 0,
      lastX: c.x,
      lastY: c.y,
      lastTouchCount: c.count,
    };
    setTouchActive(true);
  };

  const handleTrackpadMove = (evt: GestureResponderEvent) => {
    const c = getTouchCentroid(evt);
    const meta = gestureMetaRef.current;
    if (c.count > meta.maxTouches) {
      meta.maxTouches = c.count;
    }
    if (c.count !== meta.lastTouchCount) {
      // Reset anchor when finger count changes to avoid jump
      meta.lastX = c.x;
      meta.lastY = c.y;
      meta.lastTouchCount = c.count;
      return;
    }

    const rawDx = c.x - meta.lastX;
    const rawDy = c.y - meta.lastY;
    meta.lastX = c.x;
    meta.lastY = c.y;
    meta.totalMoveDist += Math.hypot(rawDx, rawDy);

    if (c.count >= 2) {
      // 2-finger vertical scroll (natural scroll direction -> Windows WHEEL_DELTA)
      pendingScrollYRef.current += rawDy * 3.5;
    } else {
      const mult = SENSITIVITY_MULTIPLIERS[sensitivityRef.current];
      pendingDxRef.current += rawDx * mult;
      pendingDyRef.current += rawDy * mult;
    }
    scheduleFlush();
  };

  const handleTrackpadRelease = () => {
    setTouchActive(false);
    flushMotion();

    const meta = gestureMetaRef.current;
    const elapsed = Date.now() - meta.startTime;
    if (elapsed < 280 && meta.totalMoveDist < 10) {
      triggerHaptic('light');
      if (!isDemoRef.current) {
        if (meta.maxTouches >= 2) {
          clientRef.current?.sendButton('right', 'click');
        } else if (dragLockRef.current) {
          clientRef.current?.sendButton('left', 'up');
          setDragLock(false);
        } else {
          clientRef.current?.sendButton('left', 'click');
        }
      } else if (meta.maxTouches === 1 && dragLockRef.current) {
        setDragLock(false);
      }
    }
  };

  const handleTrackpadTerminate = () => {
    setTouchActive(false);
    flushMotion();
  };

  const cycleSensitivity = () => {
    triggerHaptic('light');
    const idx = SENSITIVITY_ORDER.indexOf(sensitivity);
    const next = SENSITIVITY_ORDER[(idx + 1) % SENSITIVITY_ORDER.length];
    setSensitivity(next);
  };

  const handleToggleDragLock = () => {
    triggerHaptic('light');
    const next = !dragLock;
    setDragLock(next);
    if (!vm.isDemoMode) {
      clientRef.current?.sendButton('left', next ? 'down' : 'up');
    }
  };

  const handleQuickKey = (def: QuickKeyDef) => {
    triggerHaptic('light');
    if (vm.isDemoMode) return;
    if (def.combo) {
      clientRef.current?.sendCombo(def.combo);
    } else if (def.vk) {
      clientRef.current?.sendKey(def.vk, 'press');
    }
  };

  const handleTextChange = (nextText: string) => {
    if (nextText.length > 0) {
      if (!vm.isDemoMode) {
        clientRef.current?.sendText(nextText);
      }
      setTextDraft('');
    } else {
      setTextDraft('');
    }
  };

  const handleKeyPress = (key: string) => {
    if (key === 'Backspace') {
      if (!vm.isDemoMode) {
        clientRef.current?.sendKey(0x08, 'press');
      }
    } else if (key === 'Enter') {
      if (!vm.isDemoMode) {
        clientRef.current?.sendKey(0x0d, 'press');
      }
    }
  };

  const effectiveConnState: RemoteInputConnectionState = vm.isDemoMode
    ? 'connected'
    : connState;

  const statusColor =
    effectiveConnState === 'connected'
      ? Colors.steamGreenBright
      : effectiveConnState === 'connecting'
      ? Colors.orange
      : Colors.red;

  const statusLabel =
    effectiveConnState === 'connected'
      ? 'Connected'
      : effectiveConnState === 'connecting'
      ? 'Connecting'
      : 'Disconnected';

  return (
    <KeyboardAvoidingView
      style={styles.keyboardAvoid}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      keyboardVerticalOffset={Platform.OS === 'ios' ? insets.top + 44 : 0}
    >
      <View style={styles.root}>
      <Stack.Screen
        options={{
          title: 'Remote Input',
          gestureEnabled: false,
          ...(Platform.OS !== 'ios'
            ? {
                headerRight: () => (
                  <SteamPressable
                    onPress={cycleSensitivity}
                    style={styles.sensitivityPill}
                    hitSlop={8}
                  >
                    <Text style={styles.sensitivityText}>
                      {SENSITIVITY_LABELS[sensitivity]}
                    </Text>
                  </SteamPressable>
                ),
              }
            : null),
        }}
      />
      {Platform.OS === 'ios' && (
        <Stack.Toolbar placement="right">
          <Stack.Toolbar.Button
            accessibilityLabel={`Trackpad speed: ${SENSITIVITY_LABELS[sensitivity]}`}
            onPress={cycleSensitivity}
          >
            {SENSITIVITY_LABELS[sensitivity]}
          </Stack.Toolbar.Button>
        </Stack.Toolbar>
      )}

      {/* Status bar */}
      <View style={styles.statusRow}>
        <View style={styles.statusLeft}>
          <View style={[styles.statusDot, { backgroundColor: statusColor }]} />
          <Text style={styles.statusText}>{statusLabel}</Text>
        </View>
        {Platform.OS === 'web' && (
          <SteamPressable
            onPress={cycleSensitivity}
            style={styles.sensitivityPill}
          >
            <Text style={styles.sensitivityText}>
              Speed: {SENSITIVITY_LABELS[sensitivity]}
            </Text>
          </SteamPressable>
        )}
      </View>

      {/* Trackpad surface */}
      <View
        style={[styles.trackpad, touchActive && styles.trackpadActive]}
        onStartShouldSetResponder={() => true}
        onMoveShouldSetResponder={() => true}
        onResponderTerminationRequest={() => false}
        onResponderGrant={handleTrackpadGrant}
        onResponderMove={handleTrackpadMove}
        onResponderRelease={handleTrackpadRelease}
        onResponderTerminate={handleTrackpadTerminate}
      >
        <Ionicons
          name="hand-left-outline"
          size={28}
          color={touchActive ? Colors.steamBlue : Colors.textMuted}
        />
        <Text style={styles.trackpadTitle}>Trackpad</Text>
        <Text style={styles.trackpadHint}>
          1-finger drag: Move  •  1-tap: Left Click
        </Text>
        <Text style={styles.trackpadHint}>
          2-finger drag: Scroll  •  2-tap: Right Click
        </Text>
      </View>

      {/* Mouse buttons */}
      <View style={styles.mouseButtonsRow}>
        <SteamPressable
          style={styles.mouseBtn}
          onPress={() => {
            triggerHaptic('light');
            if (!vm.isDemoMode) {
              clientRef.current?.sendButton('left', 'click');
            }
          }}
        >
          <Text style={styles.mouseBtnText}>Left Click</Text>
        </SteamPressable>

        <SteamPressable
          style={[
            styles.mouseBtn,
            styles.dragLockBtn,
            dragLock && styles.dragLockBtnActive,
          ]}
          onPress={handleToggleDragLock}
        >
          <Ionicons
            name={dragLock ? 'lock-closed' : 'lock-open-outline'}
            size={14}
            color={dragLock ? Colors.textBright : Colors.textSecondary}
          />
          <Text
            style={[
              styles.mouseBtnText,
              dragLock && styles.dragLockTextActive,
            ]}
          >
            Drag Lock
          </Text>
        </SteamPressable>

        <SteamPressable
          style={styles.mouseBtn}
          onPress={() => {
            triggerHaptic('light');
            if (!vm.isDemoMode) {
              clientRef.current?.sendButton('right', 'click');
            }
          }}
        >
          <Text style={styles.mouseBtnText}>Right Click</Text>
        </SteamPressable>
      </View>

      {/* Quick keys */}
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        style={styles.quickKeysScroll}
        contentContainerStyle={styles.quickKeysContent}
      >
        {QUICK_KEYS.map((k) => (
          <SteamPressable
            key={k.id}
            style={styles.quickKeyChip}
            onPress={() => handleQuickKey(k)}
          >
            <Text style={styles.quickKeyText}>{k.label}</Text>
          </SteamPressable>
        ))}
      </ScrollView>

      {/* Live keyboard input */}
      <SteamSurface style={styles.keyboardBar}>
        <Ionicons
          name="keypad-outline"
          size={18}
          color={Colors.textSecondary}
        />
        <TextInput
          style={styles.textInput}
          value={textDraft}
          onChangeText={handleTextChange}
          onKeyPress={(e) => handleKeyPress(e.nativeEvent.key)}
          placeholder="Type on PC..."
          placeholderTextColor={Colors.textMuted}
          autoCapitalize="none"
          autoCorrect={false}
          returnKeyType="send"
          onSubmitEditing={() => {
            if (!vm.isDemoMode) {
              clientRef.current?.sendKey(0x0d, 'press');
            }
          }}
        />
        <SteamPressable
          style={styles.enterBtn}
          onPress={() => {
            triggerHaptic('light');
            if (!vm.isDemoMode) {
              clientRef.current?.sendKey(0x0d, 'press');
            }
          }}
        >
          <Text style={styles.enterBtnText}>Enter</Text>
        </SteamPressable>
      </SteamSurface>
      </View>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  keyboardAvoid: {
    flex: 1,
    backgroundColor: Colors.bg,
  },
  root: {
    flex: 1,
    backgroundColor: Colors.bg,
    paddingHorizontal: 14,
    paddingTop: 10,
    paddingBottom: 16,
    gap: 10,
  },
  statusRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  statusLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 7,
  },
  statusDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
  },
  statusText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
  sensitivityPill: {
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: Radii.pill,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
  },
  sensitivityText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.textBright,
  },
  trackpad: {
    flex: 1,
    minHeight: 80,
    backgroundColor: Colors.surface,
    borderRadius: Radii.lg,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    padding: 16,
  },
  trackpadActive: {
    borderColor: Colors.steamBlue,
    backgroundColor: Colors.surfaceElevated,
  },
  trackpadTitle: {
    fontSize: 15,
    fontWeight: '700',
    color: Colors.text,
    marginTop: 4,
  },
  trackpadHint: {
    fontSize: 12,
    color: Colors.textSecondary,
    textAlign: 'center',
  },
  mouseButtonsRow: {
    flexDirection: 'row',
    gap: 8,
  },
  mouseBtn: {
    flex: 1,
    height: 44,
    backgroundColor: Colors.surfaceElevated,
    borderRadius: Radii.md,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
    alignItems: 'center',
    justifyContent: 'center',
    flexDirection: 'row',
    gap: 6,
  },
  dragLockBtn: {
    flex: 0.9,
  },
  dragLockBtnActive: {
    backgroundColor: Colors.steamBlueDeep,
    borderColor: Colors.steamBlueBright,
  },
  mouseBtnText: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.text,
  },
  dragLockTextActive: {
    color: Colors.textBright,
  },
  quickKeysScroll: {
    maxHeight: 40,
    flexGrow: 0,
  },
  quickKeysContent: {
    gap: 8,
    alignItems: 'center',
  },
  quickKeyChip: {
    paddingHorizontal: 12,
    height: 36,
    borderRadius: Radii.md,
    backgroundColor: Colors.surface,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
    alignItems: 'center',
    justifyContent: 'center',
  },
  quickKeyText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.text,
  },
  keyboardBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: Radii.md,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
    backgroundColor: Colors.surface,
  },
  textInput: {
    flex: 1,
    color: Colors.textBright,
    fontSize: 14,
    paddingVertical: 6,
  },
  enterBtn: {
    paddingHorizontal: 12,
    paddingVertical: 7,
    borderRadius: Radii.sm,
    backgroundColor: Colors.steamBlueDeep,
    borderWidth: 1,
    borderColor: Colors.steamBlueButtonBorder,
  },
  enterBtnText: {
    fontSize: 12,
    fontWeight: '700',
    color: Colors.textBright,
  },
});
