import React, { useMemo, useState } from 'react';
import {
  FlatList,
  Platform,
  RefreshControl,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { usePCRemote } from '../state/usePCRemote';
import { Colors, Radii } from '../theme';
import { triggerHaptic } from '../utils/haptics';
import { Base64Image } from './Base64Image';
import { SteamPressable, SteamSurface } from './SteamSurface';

interface ShortcutsPanelProps {
  inline?: boolean;
  columns?: number;
}

export function ShortcutsPanel({
  inline = false,
  columns = 1,
}: ShortcutsPanelProps) {
  const vm = usePCRemote();
  const [searchText, setSearchText] = useState('');
  const [refreshing, setRefreshing] = useState(false);

  const filtered = useMemo(() => {
    const q = searchText.trim().toLowerCase();
    if (!q) return vm.shortcuts;
    return vm.shortcuts.filter((s) => s.name.toLowerCase().includes(q));
  }, [searchText, vm.shortcuts]);

  const onRefresh = async () => {
    setRefreshing(true);
    await vm.fetchShortcuts();
    setRefreshing(false);
  };

  const rows = useMemo(() => {
    if (!inline || columns <= 1) return [];
    const chunked: (typeof filtered)[] = [];
    for (let i = 0; i < filtered.length; i += columns) {
      chunked.push(filtered.slice(i, i + columns));
    }
    return chunked;
  }, [filtered, inline, columns]);

  const renderShortcutItem = (item: (typeof filtered)[number]) => (
    <SteamPressable
      key={item.path}
      onPress={() => {
        triggerHaptic('medium');
        void vm.launchShortcut(item);
      }}
      disabled={vm.busy}
      style={[
        styles.row,
        inline && columns > 1 && styles.gridItem,
        vm.busy && styles.disabled,
      ]}
    >
      <Base64Image
        dataUrl={item.icon}
        size={22}
        fallbackIcon="play-circle"
        fallbackColor={Colors.steamBlue}
      />
      <Text style={styles.rowTitle} numberOfLines={1}>
        {item.name}
      </Text>
      <Ionicons
        name="play"
        size={11}
        color={Colors.steamGreenBright}
      />
    </SteamPressable>
  );

  const innerContent = (
    <>
      <View style={styles.headerRow}>
        {inline ? (
          <Text style={styles.sectionHeader}>
            LIBRARY · SHORTCUTS ({vm.shortcuts.length})
          </Text>
        ) : null}
        <View style={[styles.searchBar, inline && styles.searchBarInline]}>
          <Ionicons name="search" size={14} color={Colors.textSecondary} />
          <TextInput
            value={searchText}
            onChangeText={setSearchText}
            placeholder="Filter by name"
            placeholderTextColor={Colors.textMuted}
            style={styles.searchInput}
            autoCapitalize="none"
            autoCorrect={false}
          />
          {searchText.length > 0 ? (
            <SteamPressable onPress={() => setSearchText('')} hitSlop={8}>
              <Ionicons
                name="close-circle"
                size={14}
                color={Colors.textSecondary}
              />
            </SteamPressable>
          ) : null}
        </View>
        {inline ? (
          <SteamPressable
            onPress={() => {
              triggerHaptic('light');
              void vm.fetchShortcuts();
            }}
            disabled={vm.busy || !vm.pcOnline}
            hitSlop={8}
            style={[
              styles.refreshMiniBtn,
              (vm.busy || !vm.pcOnline) && styles.disabled,
            ]}
          >
            <Ionicons name="refresh" size={14} color={Colors.textSecondary} />
          </SteamPressable>
        ) : null}
      </View>

      {!vm.pcOnline ? (
        <Text style={styles.emptyText}>PC offline</Text>
      ) : filtered.length === 0 ? (
        <Text style={styles.emptyText}>
          {vm.shortcuts.length === 0
            ? 'No desktop shortcuts found'
            : 'No matching shortcuts'}
        </Text>
      ) : inline ? (
        columns > 1 ? (
          <View style={styles.inlineList}>
            {rows.map((rowItems, rowIndex) => (
              <View key={`row-${rowIndex}`} style={styles.gridRow}>
                {rowItems.map(renderShortcutItem)}
                {Array.from({ length: columns - rowItems.length }).map(
                  (_, spacerIdx) => (
                    <View
                      key={`spacer-${rowIndex}-${spacerIdx}`}
                      pointerEvents="none"
                      style={[styles.row, styles.gridItem, styles.gridSpacer]}
                    />
                  )
                )}
              </View>
            ))}
          </View>
        ) : (
          <View style={styles.inlineList}>
            {filtered.map(renderShortcutItem)}
          </View>
        )
      ) : (
        <FlatList
          data={filtered}
          keyExtractor={(item) => item.path}
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
          renderItem={({ item }) => renderShortcutItem(item)}
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
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    flexWrap: 'wrap',
  },
  sectionHeader: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.textSecondary,
    letterSpacing: 0.9,
    marginRight: 'auto',
  },
  searchBar: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    backgroundColor: Colors.bgDeep,
    borderWidth: 1,
    borderColor: Colors.border,
    borderRadius: Radii.xs,
    paddingHorizontal: 10,
    paddingVertical: 7,
  },
  searchBarInline: {
    flex: undefined,
    minWidth: 180,
    maxWidth: 240,
    paddingVertical: 5,
  },
  searchInput: {
    flex: 1,
    fontSize: 13,
    color: Colors.text,
  },
  refreshMiniBtn: {
    padding: 7,
    borderRadius: Radii.xs,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  listContent: {
    gap: 6,
    paddingBottom: 24,
  },
  inlineList: {
    gap: 6,
  },
  gridRow: {
    flexDirection: 'row',
    gap: 6,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingVertical: 9,
    paddingHorizontal: 10,
    borderRadius: Radii.xs,
    backgroundColor: Colors.sidebar,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  gridItem: {
    flexGrow: 1,
    flexShrink: 1,
    flexBasis: 0,
    minWidth: 0,
  },
  gridSpacer: {
    opacity: 0,
  },
  rowTitle: {
    flex: 1,
    minWidth: 0,
    fontSize: 13,
    fontWeight: '500',
    color: Colors.text,
  },
  emptyText: {
    fontSize: 13,
    color: Colors.textSecondary,
    paddingVertical: 8,
  },
  disabled: { opacity: 0.45 },
});
