import React, { useState } from 'react';
import {
  Alert,
  Modal,
  Platform,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { Colors, Radii } from '../theme';
import { triggerHaptic } from '../utils/haptics';
import { SteamPressable, SteamSurface } from './SteamSurface';

interface PowerSectionProps {
  busy: boolean;
  onSleep: () => void;
  onShutdown: () => void;
}

export function PowerSection({
  busy,
  onSleep,
  onShutdown,
}: PowerSectionProps) {
  const [confirmKind, setConfirmKind] = useState<'sleep' | 'shutdown' | null>(
    null
  );

  const requestConfirm = (kind: 'sleep' | 'shutdown') => {
    triggerHaptic('warning');
    if (Platform.OS === 'web') {
      setConfirmKind(kind);
      return;
    }

    if (kind === 'sleep') {
      Alert.alert(
        'Put PC to sleep?',
        'If Living Room mode is active, CerberusService will revert to Office mode before sleeping.',
        [
          { text: 'Cancel', style: 'cancel' },
          { text: 'Sleep Now', style: 'destructive', onPress: onSleep },
        ]
      );
    } else {
      Alert.alert(
        'Shut down PC?',
        'If Living Room mode is active, CerberusService will revert to Office mode before shutting down.',
        [
          { text: 'Cancel', style: 'cancel' },
          { text: 'Shut Down', style: 'destructive', onPress: onShutdown },
        ]
      );
    }
  };

  return (
    <View style={styles.section}>
      <Text style={styles.sectionHeader}>POWER</Text>
      <View style={styles.row}>
        <SteamPressable
          onPress={() => requestConfirm('sleep')}
          disabled={busy}
          rippleColor={Colors.orangeBg}
          style={[styles.sleepBtn, busy && styles.disabled]}
        >
          <Ionicons name="moon" size={15} color={Colors.orange} />
          <Text style={styles.sleepText}>SLEEP</Text>
        </SteamPressable>

        <SteamPressable
          onPress={() => requestConfirm('shutdown')}
          disabled={busy}
          rippleColor={Colors.redBg}
          style={[styles.shutdownBtn, busy && styles.disabled]}
        >
          <Ionicons name="power" size={15} color={Colors.red} />
          <Text style={styles.shutdownText}>SHUT DOWN</Text>
        </SteamPressable>
      </View>

      <Modal
        visible={confirmKind !== null}
        transparent
        animationType="fade"
        onRequestClose={() => setConfirmKind(null)}
      >
        <View style={styles.modalBackdrop}>
          <SteamSurface glass featured style={styles.modalCard}>
            <View style={styles.modalInner}>
              <Text style={styles.modalTitle}>
                {confirmKind === 'sleep' ? 'Put PC to sleep?' : 'Shut down PC?'}
              </Text>
              <Text style={styles.modalBody}>
                {confirmKind === 'sleep'
                  ? 'If Living Room mode is active, CerberusService will revert to Office mode before sleeping.'
                  : 'If Living Room mode is active, CerberusService will revert to Office mode before shutting down.'}
              </Text>
              <View style={styles.modalActions}>
                <SteamPressable
                  onPress={() => setConfirmKind(null)}
                  style={styles.modalCancelBtn}
                >
                  <Text style={styles.modalCancelText}>Cancel</Text>
                </SteamPressable>
                <SteamPressable
                  onPress={() => {
                    const k = confirmKind;
                    setConfirmKind(null);
                    if (k === 'sleep') onSleep();
                    else if (k === 'shutdown') onShutdown();
                  }}
                  style={styles.modalDestructiveBtn}
                >
                  <Text style={styles.modalDestructiveText}>
                    {confirmKind === 'sleep' ? 'Sleep Now' : 'Shut Down'}
                  </Text>
                </SteamPressable>
              </View>
            </View>
          </SteamSurface>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  section: { gap: 8 },
  sectionHeader: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.textSecondary,
    paddingHorizontal: 2,
    letterSpacing: 0.9,
  },
  row: {
    flexDirection: 'row',
    gap: 8,
  },
  disabled: { opacity: 0.45 },
  sleepBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 7,
    paddingVertical: 11,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.orangeBorder,
  },
  sleepText: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 0.7,
    color: Colors.orange,
  },
  shutdownBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 7,
    paddingVertical: 11,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.redBorder,
  },
  shutdownText: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 0.7,
    color: Colors.red,
  },
  modalBackdrop: {
    flex: 1,
    backgroundColor: 'rgba(11, 14, 19, 0.78)',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 20,
  },
  modalCard: {
    width: '100%',
    maxWidth: 360,
  },
  modalInner: {
    padding: 18,
    gap: 12,
  },
  modalTitle: {
    fontSize: 16,
    fontWeight: '700',
    color: Colors.textBright,
  },
  modalBody: {
    fontSize: 13,
    color: Colors.textSecondary,
    lineHeight: 19,
  },
  modalActions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: 8,
    marginTop: 4,
  },
  modalCancelBtn: {
    paddingVertical: 8,
    paddingHorizontal: 14,
    borderRadius: Radii.sm,
    backgroundColor: Colors.surfaceElevated,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  modalCancelText: {
    color: Colors.text,
    fontWeight: '600',
    fontSize: 13,
  },
  modalDestructiveBtn: {
    paddingVertical: 8,
    paddingHorizontal: 14,
    borderRadius: Radii.sm,
    backgroundColor: Colors.red,
  },
  modalDestructiveText: {
    color: Colors.textBright,
    fontWeight: '700',
    fontSize: 13,
  },
});
