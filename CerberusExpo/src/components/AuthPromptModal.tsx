import React, { useState } from 'react';
import {
  ActivityIndicator,
  Modal,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { usePCRemote } from '../state/usePCRemote';
import { Colors, Radii } from '../theme';

export function AuthPromptModal() {
  const vm = usePCRemote();
  const [password, setPassword] = useState('');

  const visible = vm.authRequired && !vm.isDemoMode;

  const handleSubmit = async () => {
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
          <View style={styles.headerRow}>
            <MaterialCommunityIcons
              name="lock-outline"
              size={22}
              color={Colors.steamBlue}
            />
            <Text style={styles.title}>AUTHENTICATION REQUIRED</Text>
          </View>
          <Text style={styles.subtitle}>
            Enter the Cerberus password configured on your PC to sign in.
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
            onSubmitEditing={() => void handleSubmit()}
            returnKeyType="done"
            style={styles.input}
          />

          {vm.authError ? (
            <Text style={styles.errorText}>{vm.authError}</Text>
          ) : null}

          <Pressable
            onPress={() => void handleSubmit()}
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
});
