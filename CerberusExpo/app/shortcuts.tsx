import React, { useCallback, useLayoutEffect } from 'react';
import { Platform, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { Stack, useFocusEffect, useNavigation } from 'expo-router';
import { ShortcutsPanel } from '../src/components/ShortcutsPanel';
import { SteamPressable } from '../src/components/SteamSurface';
import { usePCRemote } from '../src/state/usePCRemote';
import { Colors, Radii } from '../src/theme';
import { triggerHaptic } from '../src/utils/haptics';

export default function ShortcutsScreen() {
  const vm = usePCRemote();
  const navigation = useNavigation();

  useLayoutEffect(() => {
    if (Platform.OS === 'ios') return;
    navigation.setOptions({
      headerRight: () => (
        <SteamPressable
          onPress={() => {
            triggerHaptic('light');
            void vm.fetchShortcuts();
          }}
          disabled={vm.busy || !vm.pcOnline}
          hitSlop={10}
          style={[
            styles.headerBtn,
            (vm.busy || !vm.pcOnline) && styles.disabled,
          ]}
        >
          <Ionicons name="refresh" size={18} color={Colors.text} />
        </SteamPressable>
      ),
    });
  }, [navigation, vm]);

  const { pcOnline, fetchShortcuts } = vm;
  useFocusEffect(
    useCallback(() => {
      if (pcOnline) {
        void fetchShortcuts();
      }
    }, [pcOnline, fetchShortcuts])
  );

  return (
    <>
      {Platform.OS === 'ios' && (
        <Stack.Toolbar placement="right">
          <Stack.Toolbar.Button
            icon="arrow.clockwise"
            accessibilityLabel="Refresh"
            disabled={vm.busy || !vm.pcOnline}
            onPress={() => {
              triggerHaptic('light');
              void vm.fetchShortcuts();
            }}
          />
        </Stack.Toolbar>
      )}
      <View style={styles.screen}>
        <View style={styles.inner}>
          <ShortcutsPanel />
        </View>
      </View>
    </>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: Colors.bg,
    alignItems: 'center',
  },
  inner: {
    flex: 1,
    width: '100%',
    maxWidth: 560,
    paddingHorizontal: 14,
    paddingTop: 12,
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
  disabled: { opacity: 0.45 },
});
