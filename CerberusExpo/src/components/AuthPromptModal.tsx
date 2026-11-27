import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Modal,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { usePathname, useRouter } from 'expo-router';
import { normalizeHostInput } from '../api/models';
import { usePCRemote } from '../state/usePCRemote';
import { Colors, Radii } from '../theme';

export function AuthPromptModal() {
  const vm = usePCRemote();
  const pathname = usePathname();
  const router = useRouter();

  const [hostInput, setHostInput] = useState('');
  const [portInput, setPortInput] = useState(String(vm.pcPort || 8090));
  const [connecting, setConnecting] = useState(false);
  const [hostError, setHostError] = useState<string | null>(null);
  const [attemptedUnreachable, setAttemptedUnreachable] = useState(false);
  const [password, setPassword] = useState('');

  const isSettingsRoute = pathname === '/settings';
  const [modalReady, setModalReady] = useState(!isSettingsRoute);

  useEffect(() => {
    const timer = setTimeout(
      () => setModalReady(!isSettingsRoute),
      isSettingsRoute ? 0 : 180
    );
    return () => clearTimeout(timer);
  }, [isSettingsRoute]);

  const showHostStep = vm.needsHostSetup;
  const showAuthStep = !vm.needsHostSetup && vm.authRequired;
  const visible =
    !vm.isDemoMode &&
    (showHostStep || showAuthStep) &&
    !isSettingsRoute &&
    modalReady;

  const parsedPort = Number(portInput);
  const effectiveFallbackPort =
    Number.isInteger(parsedPort) && parsedPort > 0 && parsedPort <= 65535
      ? parsedPort
      : vm.pcPort || 8090;

  const handleConnect = async () => {
    if (connecting) return;
    const normalized = normalizeHostInput(hostInput, effectiveFallbackPort);
    if (!normalized.host) {
      setHostError('Enter your PC IP address or hostname.');
      return;
    }
    if (
      !Number.isInteger(normalized.port) ||
      normalized.port <= 0 ||
      normalized.port > 65535
    ) {
      setHostError('Enter a valid port between 1 and 65535.');
      return;
    }

    setConnecting(true);
    setHostError(null);
    const res = await vm.connectHost(normalized.host, normalized.port);
    setConnecting(false);

    if (res.ok) {
      setHostError(null);
      setAttemptedUnreachable(false);
      setPassword('');
    } else {
      setHostError(
        res.error ??
          `Could not reach PC at ${normalized.host}:${normalized.port}.`
      );
      setAttemptedUnreachable(true);
    }
  };

  const handleSaveOffline = () => {
    const normalized = normalizeHostInput(hostInput, effectiveFallbackPort);
    if (!normalized.host) {
      setHostError('Enter your PC IP address or hostname.');
      return;
    }
    vm.updateSetting('pcHost', normalized.host);
    vm.updateSetting('pcPort', normalized.port);
    setHostError(null);
    setAttemptedUnreachable(false);
  };

  const handlePasswordSubmit = async () => {
    if (vm.authSubmitting || !password) return;
    const ok = await vm.login(password);
    if (ok) {
      setPassword('');
    }
  };

  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      statusBarTranslucent
    >
      <View style={styles.backdrop}>
        <View style={styles.card}>
          {showHostStep ? (
            <>
              <View style={styles.headerRow}>
                <MaterialCommunityIcons
                  name="server-network"
                  size={22}
                  color={Colors.steamBlue}
                />
                <Text style={styles.title}>CONNECT TO YOUR PC</Text>
              </View>
              <Text style={styles.subtitle}>
                Enter your PC&apos;s IP address or hostname to check connection
                and sign in.
              </Text>

              <View style={styles.fieldGroup}>
                <Text style={styles.fieldLabel}>PC HOST / IP</Text>
                <TextInput
                  value={hostInput}
                  onChangeText={(text) => {
                    setHostInput(text);
                    if (hostError) setHostError(null);
                  }}
                  placeholder="192.168.1.100"
                  placeholderTextColor={Colors.textMuted}
                  autoCapitalize="none"
                  autoCorrect={false}
                  autoFocus
                  keyboardType={Platform.OS === 'ios' ? 'url' : 'default'}
                  editable={!connecting}
                  onSubmitEditing={() => void handleConnect()}
                  returnKeyType="go"
                  style={styles.input}
                />
              </View>

              <View style={styles.fieldGroup}>
                <Text style={styles.fieldLabel}>PORT</Text>
                <TextInput
                  value={portInput}
                  onChangeText={setPortInput}
                  placeholder="8090"
                  placeholderTextColor={Colors.textMuted}
                  keyboardType="number-pad"
                  editable={!connecting}
                  onSubmitEditing={() => void handleConnect()}
                  returnKeyType="go"
                  style={styles.input}
                />
              </View>

              {hostError ? (
                <Text style={styles.errorText}>{hostError}</Text>
              ) : null}

              <Pressable
                onPress={() => void handleConnect()}
                disabled={connecting || !hostInput.trim()}
                style={({ pressed }) => [
                  styles.button,
                  (connecting || !hostInput.trim()) && styles.buttonDisabled,
                  pressed && styles.buttonPressed,
                ]}
              >
                {connecting ? (
                  <ActivityIndicator size="small" color={Colors.textBright} />
                ) : (
                  <Text style={styles.buttonText}>CONNECT</Text>
                )}
              </Pressable>

              {attemptedUnreachable ? (
                <Pressable
                  onPress={handleSaveOffline}
                  style={({ pressed }) => [
                    styles.secondaryButton,
                    pressed && styles.buttonPressed,
                  ]}
                >
                  <Text style={styles.secondaryButtonText}>
                    Save &amp; Continue Offline
                  </Text>
                </Pressable>
              ) : null}

              <Pressable
                onPress={() => vm.updateSetting('isDemoMode', true)}
                style={styles.linkButton}
              >
                <Text style={styles.linkText}>Try Demo Mode</Text>
              </Pressable>
            </>
          ) : (
            <>
              <View style={styles.headerRow}>
                <MaterialCommunityIcons
                  name="lock-outline"
                  size={22}
                  color={Colors.steamBlue}
                />
                <Text style={styles.title}>AUTHENTICATION REQUIRED</Text>
              </View>
              <Text style={styles.subtitle}>
                Enter the Cerberus password configured on{' '}
                {vm.pcHost.trim() || 'your PC'} to sign in.
              </Text>

              <TextInput
                value={password}
                onChangeText={setPassword}
                placeholder="Password"
                placeholderTextColor={Colors.textMuted}
                secureTextEntry
                autoCapitalize="none"
                autoCorrect={false}
                autoFocus
                editable={!vm.authSubmitting}
                onSubmitEditing={() => void handlePasswordSubmit()}
                returnKeyType="done"
                style={styles.input}
              />

              {vm.authError ? (
                <Text style={styles.errorText}>{vm.authError}</Text>
              ) : null}

              <Pressable
                onPress={() => void handlePasswordSubmit()}
                disabled={vm.authSubmitting || !password}
                style={({ pressed }) => [
                  styles.button,
                  (vm.authSubmitting || !password) && styles.buttonDisabled,
                  pressed && styles.buttonPressed,
                ]}
              >
                {vm.authSubmitting ? (
                  <ActivityIndicator size="small" color={Colors.textBright} />
                ) : (
                  <Text style={styles.buttonText}>SIGN IN</Text>
                )}
              </Pressable>

              <Pressable
                onPress={() => router.push('/settings')}
                style={styles.linkButton}
              >
                <Text style={styles.linkText}>Change PC Host</Text>
              </Pressable>
            </>
          )}
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: 'rgba(10, 12, 16, 0.82)',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 20,
  },
  card: {
    width: '100%',
    maxWidth: 380,
    backgroundColor: Colors.surface,
    borderRadius: Radii.md,
    borderWidth: 1,
    borderColor: Colors.border,
    padding: 20,
    gap: 12,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  title: {
    fontSize: 13,
    fontWeight: '700',
    letterSpacing: 0.8,
    color: Colors.textBright,
  },
  subtitle: {
    fontSize: 13,
    color: Colors.textSecondary,
    lineHeight: 18,
  },
  fieldGroup: {
    gap: 6,
  },
  fieldLabel: {
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 0.7,
    color: Colors.textSecondary,
  },
  input: {
    backgroundColor: Colors.bg,
    borderWidth: 1,
    borderColor: Colors.border,
    borderRadius: Radii.sm,
    paddingHorizontal: 12,
    paddingVertical: 10,
    fontSize: 15,
    color: Colors.textBright,
  },
  errorText: {
    fontSize: 12,
    color: Colors.red,
    fontWeight: '600',
  },
  button: {
    backgroundColor: Colors.steamBlue,
    borderRadius: Radii.sm,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 4,
  },
  buttonDisabled: {
    opacity: 0.5,
  },
  buttonPressed: {
    opacity: 0.85,
  },
  buttonText: {
    fontSize: 13,
    fontWeight: '700',
    letterSpacing: 0.8,
    color: Colors.textBright,
  },
  secondaryButton: {
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.borderStrong,
    borderRadius: Radii.sm,
    paddingVertical: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  secondaryButtonText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.text,
  },
  linkButton: {
    alignItems: 'center',
    paddingVertical: 4,
  },
  linkText: {
    fontSize: 12,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
});
