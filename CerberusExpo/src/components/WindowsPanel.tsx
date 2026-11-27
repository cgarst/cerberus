import React, { useState } from 'react';
import {
  FlatList,
  Platform,
  RefreshControl,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { OpenWindow } from '../api/models';
import { usePCRemote } from '../state/usePCRemote';
import { Colors, Radii } from '../theme';
import { triggerHaptic } from '../utils/haptics';
import { Base64Image } from './Base64Image';
import { SteamPressable, SteamSurface } from './SteamSurface';

function processLabel(win: OpenWindow): string {
  if (win.process && win.process.length > 0) {
    return `${win.process}.exe`;
  }
  if (win.pid !== null && win.pid !== undefined) {
    return `PID ${win.pid}`;
  }
  return '';
}

export function WindowsPanel({ inline = false }: { inline?: boolean }) {
  const vm = usePCRemote();
  const [refreshing, setRefreshing] = useState(false);

  const onRefresh = async () => {
    setRefreshing(true);
    await vm.fetchWindows();
    setRefreshing(false);
  };

  const renderWindowItem = (item: OpenWindow) => (
    <View key={item.hwnd} style={styles.row}>
      <Base64Image
        dataUrl={item.icon}
        size={22}
        fallbackIcon="browsers-outline"
        fallbackColor={Colors.steamBlue}
      />
      <View style={styles.rowInfo}>
        <Text style={styles.rowTitle} numberOfLines={1}>
          {item.title && item.title.length > 0 ? item.title : '(untitled)'}
        </Text>
        <Text style={styles.rowSub} numberOfLines={1}>
          {processLabel(item)}
        </Text>
      </View>

      <SteamPressable
        onPress={() => {
          triggerHaptic('light');
          void vm.focusWindow(item);
        }}
        disabled={vm.busy}
        style={[styles.smallBtn, vm.busy && styles.disabled]}
      >
        <Text style={styles.smallBtnText}>Focus</Text>
      </SteamPressable>

      <SteamPressable
        onPress={() => {
          triggerHaptic('warning');
          void vm.closeWindow(item);
        }}
        disabled={vm.busy}
        rippleColor={Colors.redBg}
        style={[styles.smallDestructiveBtn, vm.busy && styles.disabled]}
      >
        <Text style={styles.smallDestructiveText}>Close</Text>
      </SteamPressable>
    </View>
  );

  const innerContent = (
    <>
      {inline ? (
        <View style={styles.inlineHeader}>
          <Text style={styles.sectionHeader}>
            OPEN WINDOWS ({vm.windows.length})
          </Text>
          <View style={styles.inlineActions}>
            <SteamPressable
              onPress={() => {
                triggerHaptic('light');
                void vm.openTaskView();
              }}
              disabled={vm.busy || !vm.pcOnline}
              style={[
                styles.miniActionBtn,
                (vm.busy || !vm.pcOnline) && styles.disabled,
              ]}
            >
              <Text style={styles.miniActionText}>Task View</Text>
            </SteamPressable>
            <SteamPressable
              onPress={() => {
                triggerHaptic('warning');
                void vm.closeAllWindows();
              }}
              disabled={vm.busy || vm.windows.length === 0}
              rippleColor={Colors.redBg}
              style={[
                styles.miniDestructiveBtn,
                (vm.busy || vm.windows.length === 0) && styles.disabled,
              ]}
            >
              <Text style={styles.miniDestructiveText}>Close All</Text>
            </SteamPressable>
            <SteamPressable
              onPress={() => {
                triggerHaptic('light');
                void vm.fetchWindows();
              }}
              disabled={vm.busy || !vm.pcOnline}
              hitSlop={8}
              style={[
                styles.miniActionBtn,
                (vm.busy || !vm.pcOnline) && styles.disabled,
              ]}
            >
              <Ionicons name="refresh" size={13} color={Colors.textSecondary} />
            </SteamPressable>
          </View>
        </View>
      ) : vm.pcOnline ? (
        <View style={styles.topActions}>
          <SteamPressable
            onPress={() => {
              triggerHaptic('light');
              void vm.openTaskView();
            }}
            disabled={vm.busy}
            style={[styles.actionBtn, vm.busy && styles.disabled]}
          >
            <Ionicons name="albums-outline" size={15} color={Colors.steamBlue} />
            <Text style={styles.actionBtnText}>Task View</Text>
          </SteamPressable>

          <SteamPressable
            onPress={() => {
              triggerHaptic('warning');
              void vm.closeAllWindows();
            }}
            disabled={vm.busy || vm.windows.length === 0}
            rippleColor={Colors.redBg}
            style={[
              styles.closeAllBtn,
              (vm.busy || vm.windows.length === 0) && styles.disabled,
            ]}
          >
            <Ionicons
              name="close-circle-outline"
              size={15}
              color={Colors.red}
            />
            <Text style={styles.closeAllText}>Close All</Text>
          </SteamPressable>
        </View>
      ) : null}

      {!vm.pcOnline ? (
        <Text style={styles.emptyText}>PC offline</Text>
      ) : vm.windows.length === 0 ? (
        <Text style={styles.emptyText}>No open windows</Text>
      ) : inline ? (
        <View style={styles.inlineList}>
          {vm.windows.map(renderWindowItem)}
        </View>
      ) : (
        <FlatList
          data={vm.windows}
          keyExtractor={(item) => String(item.hwnd)}
          contentContainerStyle={styles.listContent}
          refreshControl={
            Platform.OS !== 'web' ? (
              <RefreshControl
                refreshing={refreshing}
                onRefresh={onRefresh}
                tintColor={Colors.steamBlue}
              />
            ) : undefined
          }
          renderItem={({ item }) => renderWindowItem(item)}
        />
      )}
    </>
  );

  if (inline) {
    return (
      <SteamSurface style={styles.inlineCard}>{innerContent}</SteamSurface>
    );
  }

  return <View style={styles.panel}>{innerContent}</View>;
}

const styles = StyleSheet.create({
  panel: {
    flex: 1,
    gap: 10,
  },
  inlineCard: {
    padding: 14,
    gap: 10,
  },
  inlineHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 8,
    flexWrap: 'wrap',
  },
  sectionHeader: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.textSecondary,
    letterSpacing: 0.9,
  },
  inlineActions: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  miniActionBtn: {
    paddingVertical: 5,
    paddingHorizontal: 9,
    borderRadius: Radii.xs,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  miniActionText: {
    fontSize: 11,
    fontWeight: '600',
    color: Colors.text,
  },
  miniDestructiveBtn: {
    paddingVertical: 5,
    paddingHorizontal: 9,
    borderRadius: Radii.xs,
    backgroundColor: Colors.redBg,
    borderWidth: 1,
    borderColor: Colors.redBorder,
  },
  miniDestructiveText: {
    fontSize: 11,
    fontWeight: '600',
    color: Colors.red,
  },
  topActions: {
    flexDirection: 'row',
    gap: 8,
  },
  actionBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 7,
    paddingVertical: 10,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  actionBtnText: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.text,
  },
  closeAllBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 7,
    paddingVertical: 10,
    borderRadius: Radii.sm,
    backgroundColor: Colors.redBg,
    borderWidth: 1,
    borderColor: Colors.redBorder,
  },
  closeAllText: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.red,
  },
  listContent: {
    gap: 6,
    paddingBottom: 24,
  },
  inlineList: {
    gap: 6,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    padding: 9,
    borderRadius: Radii.xs,
    backgroundColor: Colors.sidebar,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  rowInfo: {
    flex: 1,
    minWidth: 0,
    gap: 2,
  },
  rowTitle: {
    fontSize: 13,
    fontWeight: '600',
    color: Colors.text,
  },
  rowSub: {
    fontSize: 11,
    color: Colors.textSecondary,
  },
  smallBtn: {
    paddingVertical: 6,
    paddingHorizontal: 10,
    borderRadius: Radii.xs,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  smallBtnText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.text,
  },
  smallDestructiveBtn: {
    paddingVertical: 6,
    paddingHorizontal: 10,
    borderRadius: Radii.xs,
    backgroundColor: Colors.redBg,
    borderWidth: 1,
    borderColor: Colors.redBorder,
  },
  smallDestructiveText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.red,
  },
  emptyText: {
    fontSize: 13,
    color: Colors.textSecondary,
    paddingVertical: 8,
  },
  disabled: { opacity: 0.45 },
});
