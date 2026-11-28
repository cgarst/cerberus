import React from 'react';
import { Appearance, Image, Platform, StyleSheet, Text, View } from 'react-native';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { PCRemoteProvider } from '../src/state/usePCRemote';
import { AuthPromptModal } from '../src/components/AuthPromptModal';
import { Colors } from '../src/theme';

if (Platform.OS === 'ios') {
  Appearance.setColorScheme('dark');
}

export default function RootLayout() {
  return (
    <SafeAreaProvider>
      <PCRemoteProvider>
        <StatusBar style="light" />
        <Stack
          screenOptions={{
            headerStyle: {
              backgroundColor:
                Platform.OS === 'ios' ? 'rgba(23, 26, 33, 0.85)' : Colors.chrome,
            },
            headerBlurEffect:
              Platform.OS === 'ios' ? 'systemChromeMaterialDark' : undefined,
            headerUserInterfaceStyle:
              Platform.OS === 'ios' ? 'dark' : undefined,
            headerBackButtonDisplayMode:
              Platform.OS === 'ios' ? 'minimal' : undefined,
            headerTintColor: Colors.textBright,
            headerTitleStyle: {
              fontWeight: '700',
              fontSize: 16,
            },
            headerShadowVisible: false,
            contentStyle: { backgroundColor: Colors.bg },
          }}
        >
          <Stack.Screen
            name="index"
            options={{
              title: 'Cerberus',
              headerTitle: () => (
                <View style={styles.headerTitleRow}>
                  <Image
                    source={require('../assets/icon.png')}
                    style={styles.headerIcon}
                    resizeMode="contain"
                  />
                  <Text style={styles.headerTitleText}>Cerberus</Text>
                </View>
              ),
            }}
          />
          <Stack.Screen
            name="shortcuts"
            options={{ title: 'Desktop Shortcuts' }}
          />
          <Stack.Screen name="windows" options={{ title: 'Open Windows' }} />
          <Stack.Screen
            name="settings"
            options={{
              title: 'SETTINGS',
              presentation: 'modal',
              ...(Platform.OS === 'ios'
                ? {
                    headerStyle: { backgroundColor: '#1c1c1e' },
                    headerBlurEffect: undefined,
                    contentStyle: { backgroundColor: '#1c1c1e' },
                  }
                : null),
            }}
          />
        </Stack>
        <AuthPromptModal />
      </PCRemoteProvider>
    </SafeAreaProvider>
  );
}

const styles = StyleSheet.create({
  headerTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  headerIcon: {
    width: 24,
    height: 24,
    borderRadius: 5,
  },
  headerTitleText: {
    color: Colors.textBright,
    fontWeight: '700',
    fontSize: 16,
  },
});
