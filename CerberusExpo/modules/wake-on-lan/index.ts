import { requireOptionalNativeModule } from 'expo-modules-core';

interface WakeOnLanNativeModule {
  sendMagicPacket(macAddress: string, broadcastIp: string): Promise<void>;
}

const nativeModule = requireOptionalNativeModule<WakeOnLanNativeModule>('WakeOnLan');

export function isNativeWakeOnLanAvailable(): boolean {
  return nativeModule != null;
}

export async function sendNativeMagicPacket(
  macAddress: string,
  broadcastIp: string
): Promise<void> {
  if (!nativeModule) {
    throw new Error(
      'Wake-on-LAN native module is unavailable. Build with expo prebuild / development build.'
    );
  }
  await nativeModule.sendMagicPacket(macAddress, broadcastIp);
}
