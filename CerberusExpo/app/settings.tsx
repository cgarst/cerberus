import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import {
  Platform,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  TextInput,
  View,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { Stack, useNavigation, useRouter } from 'expo-router';
import { normalizeHostInput } from '../src/api/models';
import { SteamPressable, SteamSurface } from '../src/components/SteamSurface';
import { usePCRemote } from '../src/state/usePCRemote';
import { Colors, Radii } from '../src/theme';
import { triggerHaptic } from '../src/utils/haptics';
import { isWakeOnLanSupported } from '../src/utils/wakeOnLan';

export default function SettingsScreen() {
  const vm = usePCRemote();
  const router = useRouter();
  const navigation = useNavigation();

  const [hostText, setHostText] = useState(vm.pcHost);
  const [portText, setPortText] = useState(String(vm.pcPort));
  const [timeoutText, setTimeoutText] = useState(String(vm.wakeTimeoutSeconds));
  const hostTextRef = useRef(hostText);
  const portTextRef = useRef(portText);

  useEffect(() => {
    hostTextRef.current = hostText;
  }, [hostText]);

  useEffect(() => {
    portTextRef.current = portText;
  }, [portText]);

  const commitHostAndPort = useCallback(() => {
    const portNum = Number(portTextRef.current);
    const fallbackPort =
      Number.isInteger(portNum) && portNum > 0 && portNum <= 65535
        ? portNum
        : vm.pcPort;
    const normalized = normalizeHostInput(hostTextRef.current, fallbackPort);
    if (normalized.host !== vm.pcHost) {
      vm.updateSetting('pcHost', normalized.host);
    }
    if (normalized.port !== vm.pcPort) {
      vm.updateSetting('pcPort', normalized.port);
    }
    setHostText(normalized.host);
    setPortText(String(normalized.port));
  }, [vm]);

  const commitRef = useRef(commitHostAndPort);
  useEffect(() => {
    commitRef.current = commitHostAndPort;
  }, [commitHostAndPort]);

  useEffect(() => {
    return () => {
      commitRef.current();
    };
  }, []);

  useLayoutEffect(() => {
    if (Platform.OS === 'ios') return;
    navigation.setOptions({
      headerRight: () => (
        <SteamPressable
          onPress={() => {
            triggerHaptic('light');
            commitHostAndPort();
            router.back();
          }}
          hitSlop={10}
          style={styles.doneBtn}
        >
          <Text style={styles.doneText}>DONE</Text>
        </SteamPressable>
      ),
    });
  }, [commitHostAndPort, navigation, router]);

  return (
    <>
      {Platform.OS === 'ios' && (
        <Stack.Toolbar placement="right">
          <Stack.Toolbar.Button
            onPress={() => {
              triggerHaptic('light');
              commitHostAndPort();
              router.back();
            }}
          >
            Done
          </Stack.Toolbar.Button>
        </Stack.Toolbar>
      )}
      <ScrollView
      style={styles.scroll}
      contentContainerStyle={styles.scrollContent}
      keyboardShouldPersistTaps="handled"
    >
      <View style={styles.container}>
        <View style={styles.section}>
          <Text style={styles.sectionHeader}>PC CONNECTION</Text>
          <SteamSurface style={styles.card}>
            <View style={styles.fieldRow}>
              <Text style={styles.fieldLabel}>PC HOST / IP</Text>
              <TextInput
                value={hostText}
                onChangeText={setHostText}
                onEndEditing={commitHostAndPort}
                placeholder="192.168.1.100"
                placeholderTextColor={Colors.textMuted}
                autoCapitalize="none"
                autoCorrect={false}
                keyboardType={Platform.OS === 'ios' ? 'url' : 'default'}
                style={styles.input}
              />
            </View>
            <View style={styles.divider} />

            <View style={styles.fieldRow}>
              <Text style={styles.fieldLabel}>PORT</Text>
              <TextInput
                value={portText}
                onChangeText={(text) => {
                  setPortText(text);
                  const val = Number(text);
                  if (Number.isInteger(val) && val > 0 && val <= 65535) {
                    vm.updateSetting('pcPort', val);
                  }
                }}
                placeholder="8090"
                placeholderTextColor={Colors.textMuted}
                keyboardType="number-pad"
                style={styles.input}
              />
            </View>
            <View style={styles.divider} />

            <View style={styles.fieldRow}>
              <Text style={styles.fieldLabel}>AUTHENTICATION</Text>
              <Text style={styles.toggleLabel}>
                {vm.bearerToken.trim().length > 0
                  ? 'Signed in (access token stored)'
                  : 'Not signed in'}
              </Text>
            </View>
            {vm.bearerToken.trim().length > 0 ? (
              <>
                <View style={styles.divider} />
                <SteamPressable
                  onPress={vm.signOut}
                  style={styles.wolButton}
                >
                  <Ionicons
                    name="log-out-outline"
                    size={18}
                    color={Colors.steamBlue}
                  />
                  <Text style={styles.wolButtonText}>Sign Out</Text>
                </SteamPressable>
              </>
            ) : null}
          </SteamSurface>
        </View>

        {isWakeOnLanSupported ? (
          <View style={styles.section}>
            <Text style={styles.sectionHeader}>WAKE-ON-LAN</Text>
            <SteamSurface style={styles.card}>
              <View style={styles.fieldRow}>
                <Text style={styles.fieldLabel}>PC MAC ADDRESS</Text>
                <TextInput
                  value={vm.pcMac}
                  onChangeText={(text) => vm.updateSetting('pcMac', text)}
                  placeholder="AA-BB-CC-DD-EE-FF"
                  placeholderTextColor={Colors.textMuted}
                  autoCapitalize="characters"
                  autoCorrect={false}
                  style={styles.input}
                />
              </View>
              <View style={styles.divider} />

              <View style={styles.fieldRow}>
                <Text style={styles.fieldLabel}>BROADCAST IP</Text>
                <TextInput
                  value={vm.broadcastIp}
                  onChangeText={(text) => vm.updateSetting('broadcastIp', text)}
                  placeholder="192.168.1.255"
                  placeholderTextColor={Colors.textMuted}
                  autoCapitalize="none"
                  autoCorrect={false}
                  keyboardType={
                    Platform.OS === 'ios' ? 'decimal-pad' : 'default'
                  }
                  style={styles.input}
                />
              </View>
              <View style={styles.divider} />

              <View style={styles.fieldRow}>
                <Text style={styles.fieldLabel}>WAKE TIMEOUT (SECONDS)</Text>
                <TextInput
                  value={timeoutText}
                  onChangeText={(text) => {
                    setTimeoutText(text);
                    const val = Number(text);
                    if (Number.isInteger(val) && val >= 5 && val <= 180) {
                      vm.updateSetting('wakeTimeoutSeconds', val);
                    }
                  }}
                  placeholder="45"
                  placeholderTextColor={Colors.textMuted}
                  keyboardType="number-pad"
                  style={styles.input}
                />
              </View>
              <View style={styles.divider} />

              <SteamPressable
                onPress={() => {
                  triggerHaptic('medium');
                  void vm.wakeOnly();
                }}
                style={styles.wolButton}
              >
                <Ionicons name="flash" size={16} color={Colors.steamBlue} />
                <Text style={styles.wolButtonText}>
                  Send Wake-on-LAN Packet
                </Text>
              </SteamPressable>
            </SteamSurface>
          </View>
        ) : null}

        <View style={styles.section}>
          <Text style={styles.sectionHeader}>TESTING</Text>
          <SteamSurface style={styles.card}>
            <View style={styles.toggleRow}>
              <Text style={styles.toggleLabel}>Demo Mode (Simulated PC)</Text>
              <Switch
                value={vm.isDemoMode}
                onValueChange={(val) => vm.updateSetting('isDemoMode', val)}
                trackColor={{ false: '#323a45', true: Colors.steamBlue }}
                thumbColor="#ffffff"
              />
            </View>
          </SteamSurface>
        </View>
      </View>
    </ScrollView>
    </>
  );
}

const styles = StyleSheet.create({
  scroll: {
    flex: 1,
    backgroundColor: Colors.bg,
  },
  scrollContent: {
    paddingHorizontal: 14,
    paddingVertical: 14,
    alignItems: 'center',
  },
  container: {
    width: '100%',
    maxWidth: 560,
    gap: 18,
    paddingBottom: 32,
  },
  doneBtn: {
    paddingHorizontal: Platform.OS === 'ios' ? 8 : 10,
    paddingVertical: Platform.OS === 'ios' ? 4 : 5,
    ...(Platform.OS !== 'ios'
      ? {
          borderRadius: Radii.xs,
          backgroundColor: Colors.steamBlueBg,
          borderWidth: 1,
          borderColor: Colors.steamBlueBorder,
          marginRight: 14,
        }
      : null),
  },
  doneText: {
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 0.7,
    color: Colors.steamBlue,
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
  card: {
    borderRadius: Radii.md,
  },
  fieldRow: {
    paddingHorizontal: 14,
    paddingVertical: 10,
    gap: 4,
  },
  fieldLabel: {
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.7,
    color: Colors.textSecondary,
  },
  input: {
    fontSize: 14,
    color: Colors.textBright,
    paddingVertical: 4,
  },
  divider: {
    height: 1,
    backgroundColor: Colors.border,
  },
  wolButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    paddingHorizontal: 14,
    paddingVertical: 13,
  },
  wolButtonText: {
    fontSize: 14,
    fontWeight: '600',
    color: Colors.steamBlue,
  },
  toggleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 14,
    paddingVertical: 12,
  },
  toggleLabel: {
    fontSize: 14,
    color: Colors.text,
  },
});
