import React, { useLayoutEffect } from 'react';
import { StyleSheet, Text as RNText } from 'react-native';
import {
  Button,
  Card,
  Column,
  Host,
  HorizontalDivider,
  LazyColumn,
  ListItem,
  OutlinedTextField,
  Switch,
  Text,
  useNativeState,
} from '@expo/ui/jetpack-compose';
import { fillMaxSize, fillMaxWidth, paddingAll } from '@expo/ui/jetpack-compose/modifiers';
import { useNavigation, useRouter } from 'expo-router';
import { SteamPressable } from '../src/components/SteamSurface';
import { useSettingsForm } from '../src/state/useSettingsForm';
import { Colors, Radii } from '../src/theme';
import { triggerHaptic } from '../src/utils/haptics';
import { isWakeOnLanSupported } from '../src/utils/wakeOnLan';

export default function SettingsScreen() {
  const router = useRouter();
  const navigation = useNavigation();
  const form = useSettingsForm();
  const { vm } = form;

  const hostState = useNativeState(form.hostText);
  const portState = useNativeState(form.portText);
  const macState = useNativeState(vm.pcMac);
  const broadcastState = useNativeState(vm.broadcastIp);
  const timeoutState = useNativeState(form.timeoutText);

  useLayoutEffect(() => {
    navigation.setOptions({
      headerRight: () => (
        <SteamPressable
          onPress={() => {
            triggerHaptic('light');
            form.commitHostAndPort();
            router.back();
          }}
          hitSlop={10}
          style={styles.doneBtn}
        >
          <RNText style={styles.doneText}>DONE</RNText>
        </SteamPressable>
      ),
    });
  }, [form, navigation, router]);

  const isSignedIn = vm.bearerToken.trim().length > 0;

  return (
    <Host style={styles.host} colorScheme="dark" seedColor={Colors.steamBlue}>
      <LazyColumn
        modifiers={[fillMaxSize()]}
        contentPadding={{ start: 16, top: 16, end: 16, bottom: 32 }}
        verticalArrangement={{ spacedBy: 16 }}
      >
        <Text color={Colors.textSecondary} style={{ typography: 'labelLarge' }}>
          PC CONNECTION
        </Text>
        <Card modifiers={[fillMaxWidth()]}>
          <Column modifiers={[fillMaxWidth(), paddingAll(16)]} verticalArrangement={{ spacedBy: 12 }}>
            <OutlinedTextField
              value={hostState}
              singleLine
              onValueChange={form.setHostText}
              keyboardOptions={{
                keyboardType: 'uri',
                autoCorrectEnabled: false,
                capitalization: 'none',
                imeAction: 'done',
              }}
              keyboardActions={{
                onDone: () => {
                  const normalized = form.commitHostAndPort();
                  hostState.set(normalized.host);
                  portState.set(String(normalized.port));
                },
              }}
              modifiers={[fillMaxWidth()]}
            >
              <OutlinedTextField.Label>
                <Text>PC Host / IP</Text>
              </OutlinedTextField.Label>
              <OutlinedTextField.Placeholder>
                <Text>192.168.1.100</Text>
              </OutlinedTextField.Placeholder>
            </OutlinedTextField>

            <OutlinedTextField
              value={portState}
              singleLine
              onValueChange={form.handlePortChange}
              keyboardOptions={{
                keyboardType: 'number',
                imeAction: 'done',
              }}
              modifiers={[fillMaxWidth()]}
            >
              <OutlinedTextField.Label>
                <Text>Port</Text>
              </OutlinedTextField.Label>
              <OutlinedTextField.Placeholder>
                <Text>8090</Text>
              </OutlinedTextField.Placeholder>
            </OutlinedTextField>

            <HorizontalDivider />

            <ListItem>
              <ListItem.HeadlineContent>
                <Text>Authentication</Text>
              </ListItem.HeadlineContent>
              <ListItem.SupportingContent>
                <Text>
                  {isSignedIn ? 'Signed in (access token stored)' : 'Not signed in'}
                </Text>
              </ListItem.SupportingContent>
            </ListItem>

            {isSignedIn ? (
              <Button
                onClick={() => {
                  triggerHaptic('light');
                  vm.signOut();
                }}
                modifiers={[fillMaxWidth()]}
              >
                <Text>Sign Out</Text>
              </Button>
            ) : null}
          </Column>
        </Card>

        {isWakeOnLanSupported ? (
          <>
            <Text color={Colors.textSecondary} style={{ typography: 'labelLarge' }}>
              WAKE-ON-LAN
            </Text>
            <Card modifiers={[fillMaxWidth()]}>
              <Column modifiers={[fillMaxWidth(), paddingAll(16)]} verticalArrangement={{ spacedBy: 12 }}>
                <OutlinedTextField
                  value={macState}
                  singleLine
                  onValueChange={(text) => vm.updateSetting('pcMac', text)}
                  keyboardOptions={{
                    capitalization: 'characters',
                    autoCorrectEnabled: false,
                    imeAction: 'next',
                  }}
                  modifiers={[fillMaxWidth()]}
                >
                  <OutlinedTextField.Label>
                    <Text>PC MAC Address</Text>
                  </OutlinedTextField.Label>
                  <OutlinedTextField.Placeholder>
                    <Text>AA-BB-CC-DD-EE-FF</Text>
                  </OutlinedTextField.Placeholder>
                </OutlinedTextField>

                <OutlinedTextField
                  value={broadcastState}
                  singleLine
                  onValueChange={(text) => vm.updateSetting('broadcastIp', text)}
                  keyboardOptions={{
                    keyboardType: 'decimal',
                    autoCorrectEnabled: false,
                    capitalization: 'none',
                    imeAction: 'next',
                  }}
                  modifiers={[fillMaxWidth()]}
                >
                  <OutlinedTextField.Label>
                    <Text>Broadcast IP</Text>
                  </OutlinedTextField.Label>
                  <OutlinedTextField.Placeholder>
                    <Text>192.168.1.255</Text>
                  </OutlinedTextField.Placeholder>
                </OutlinedTextField>

                <OutlinedTextField
                  value={timeoutState}
                  singleLine
                  onValueChange={form.handleTimeoutChange}
                  keyboardOptions={{
                    keyboardType: 'number',
                    imeAction: 'done',
                  }}
                  modifiers={[fillMaxWidth()]}
                >
                  <OutlinedTextField.Label>
                    <Text>Wake Timeout (Seconds)</Text>
                  </OutlinedTextField.Label>
                  <OutlinedTextField.Placeholder>
                    <Text>45</Text>
                  </OutlinedTextField.Placeholder>
                </OutlinedTextField>

                <Button
                  onClick={() => {
                    triggerHaptic('medium');
                    void vm.wakeOnly();
                  }}
                  modifiers={[fillMaxWidth()]}
                >
                  <Text>Send Wake-on-LAN Packet</Text>
                </Button>
              </Column>
            </Card>
          </>
        ) : null}

        <Text color={Colors.textSecondary} style={{ typography: 'labelLarge' }}>
          TESTING
        </Text>
        <Card modifiers={[fillMaxWidth()]}>
          <ListItem>
            <ListItem.HeadlineContent>
              <Text>Demo Mode (Simulated PC)</Text>
            </ListItem.HeadlineContent>
            <ListItem.TrailingContent>
              <Switch
                value={vm.isDemoMode}
                onCheckedChange={(val) => {
                  triggerHaptic('light');
                  vm.updateSetting('isDemoMode', val);
                }}
              />
            </ListItem.TrailingContent>
          </ListItem>
        </Card>
      </LazyColumn>
    </Host>
  );
}

const styles = StyleSheet.create({
  host: {
    flex: 1,
    backgroundColor: Colors.bg,
  },
  doneBtn: {
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: Radii.xs,
    backgroundColor: Colors.steamBlueBg,
    borderWidth: 1,
    borderColor: Colors.steamBlueBorder,
    marginRight: 14,
  },
  doneText: {
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 0.7,
    color: Colors.steamBlue,
  },
});
