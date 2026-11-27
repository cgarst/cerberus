import React, { useEffect, useLayoutEffect } from 'react';
import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation } from 'expo-router';
import { ShortcutsPanel } from '../src/components/ShortcutsPanel';
import { SteamPressable } from '../src/components/SteamSurface';
import { usePCRemote } from '../src/state/usePCRemote';
import { Colors, Radii } from '../src/theme';
import { triggerHaptic } from '../src/utils/haptics';

export default function ShortcutsScreen() {
  const vm = usePCRemote();
  const navigation = useNavigation();

  useLayoutEffect(() => {
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

  useEffect(() => {
    if (vm.pcOnline) {
      void vm.fetchShortcuts();
    }
  }, [vm.pcOnline, vm.fetchShortcuts]);

  return (
    <View style={styles.screen}>
      <View style={styles.inner}>
        <ShortcutsPanel />
      </View>
    </View>
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
    padding: 6,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  disabled: { opacity: 0.45 },
});
