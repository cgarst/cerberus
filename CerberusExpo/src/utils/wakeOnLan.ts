import { Platform } from 'react-native';
import {
  isNativeWakeOnLanAvailable,
  sendNativeMagicPacket,
} from '../../modules/wake-on-lan';

export const isWakeOnLanSupported = Platform.OS !== 'web';

export async function sendWakeOnLanPacket(
  macAddress: string,
  broadcastIp: string
): Promise<void> {
  if (Platform.OS === 'web') {
    throw new Error('Waking a sleeping PC requires the iOS or Android app.');
  }
  if (!isNativeWakeOnLanAvailable()) {
    throw new Error(
      'Wake-on-LAN native module not loaded. Run a native build (npx expo run:ios / run:android).'
    );
  }
  await sendNativeMagicPacket(macAddress, broadcastIp);
}
