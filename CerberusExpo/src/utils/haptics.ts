import { Platform } from 'react-native';
import * as Haptics from 'expo-haptics';

/** Trigger subtle native haptic feedback on iOS/Android; no-op on web. */
export function triggerHaptic(
  kind: 'light' | 'medium' | 'success' | 'warning' = 'light'
) {
  if (Platform.OS === 'web') return;

  if (kind === 'success') {
    void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
  } else if (kind === 'warning') {
    void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
  } else if (kind === 'medium') {
    void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
  } else {
    void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
  }
}
