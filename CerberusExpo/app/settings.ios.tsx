import React from 'react';
import {
  Button,
  Form,
  Host,
  LabeledContent,
  Section,
  Text,
  TextField,
  Toggle,
  useNativeState,
} from '@expo/ui/swift-ui';
import {
  autocorrectionDisabled,
  foregroundStyle,
  keyboardType,
  multilineTextAlignment,
  textInputAutocapitalization,
} from '@expo/ui/swift-ui/modifiers';
import { Stack, useRouter } from 'expo-router';
import { useSettingsForm } from '../src/state/useSettingsForm';
import { triggerHaptic } from '../src/utils/haptics';
import { isWakeOnLanSupported } from '../src/utils/wakeOnLan';

export default function SettingsScreen() {
  const router = useRouter();
  const form = useSettingsForm();
  const { vm } = form;

  const hostState = useNativeState(form.hostText);
  const portState = useNativeState(form.portText);
  const macState = useNativeState(vm.pcMac);
  const broadcastState = useNativeState(vm.broadcastIp);
  const timeoutState = useNativeState(form.timeoutText);

  const isSignedIn = vm.bearerToken.trim().length > 0;

  return (
    <>
      <Stack.Toolbar placement="right">
        <Stack.Toolbar.Button
          onPress={() => {
            triggerHaptic('light');
            const normalized = form.commitHostAndPort();
            hostState.set(normalized.host);
            portState.set(String(normalized.port));
            router.back();
          }}
        >
          Done
        </Stack.Toolbar.Button>
      </Stack.Toolbar>

      <Host style={{ flex: 1 }} colorScheme="dark">
        <Form>
          <Section title="PC Connection">
            <LabeledContent label="PC Host / IP">
              <TextField
                text={hostState}
                placeholder="192.168.1.100"
                onTextChange={form.setHostText}
                onFocusChange={(focused) => {
                  if (!focused) {
                    const normalized = form.commitHostAndPort();
                    hostState.set(normalized.host);
                    portState.set(String(normalized.port));
                  }
                }}
                modifiers={[
                  keyboardType('url'),
                  textInputAutocapitalization('never'),
                  autocorrectionDisabled(true),
                  multilineTextAlignment('trailing'),
                ]}
              />
            </LabeledContent>

            <LabeledContent label="Port">
              <TextField
                text={portState}
                placeholder="8090"
                onTextChange={form.handlePortChange}
                modifiers={[
                  keyboardType('ascii-capable-number-pad'),
                  multilineTextAlignment('trailing'),
                ]}
              />
            </LabeledContent>

            <LabeledContent label="Authentication">
              <Text
                modifiers={[
                  foregroundStyle({ type: 'hierarchical', style: 'secondary' }),
                ]}
              >
                {isSignedIn ? 'Signed in' : 'Not signed in'}
              </Text>
            </LabeledContent>

            {isSignedIn ? (
              <Button
                systemImage="rectangle.portrait.and.arrow.right"
                label="Sign Out"
                onPress={() => {
                  triggerHaptic('light');
                  vm.signOut();
                }}
              />
            ) : null}
          </Section>

          {isWakeOnLanSupported ? (
            <Section title="Wake-on-LAN">
              <LabeledContent label="MAC Address">
                <TextField
                  text={macState}
                  placeholder="AA-BB-CC-DD-EE-FF"
                  onTextChange={(text) => vm.updateSetting('pcMac', text)}
                  modifiers={[
                    textInputAutocapitalization('characters'),
                    autocorrectionDisabled(true),
                    multilineTextAlignment('trailing'),
                  ]}
                />
              </LabeledContent>

              <LabeledContent label="Broadcast IP">
                <TextField
                  text={broadcastState}
                  placeholder="192.168.1.255"
                  onTextChange={(text) => vm.updateSetting('broadcastIp', text)}
                  modifiers={[
                    keyboardType('decimal-pad'),
                    textInputAutocapitalization('never'),
                    autocorrectionDisabled(true),
                    multilineTextAlignment('trailing'),
                  ]}
                />
              </LabeledContent>

              <LabeledContent label="Wake Timeout (s)">
                <TextField
                  text={timeoutState}
                  placeholder="45"
                  onTextChange={form.handleTimeoutChange}
                  modifiers={[
                    keyboardType('ascii-capable-number-pad'),
                    multilineTextAlignment('trailing'),
                  ]}
                />
              </LabeledContent>

              <Button
                systemImage="bolt.fill"
                label="Send Wake-on-LAN Packet"
                onPress={() => {
                  triggerHaptic('medium');
                  void vm.wakeOnly();
                }}
              />
            </Section>
          ) : null}

          <Section title="Testing">
            <Toggle
              isOn={vm.isDemoMode}
              label="Demo Mode (Simulated PC)"
              onIsOnChange={(val) => {
                triggerHaptic('light');
                vm.updateSetting('isDemoMode', val);
              }}
            />
          </Section>
        </Form>
      </Host>
    </>
  );
}
