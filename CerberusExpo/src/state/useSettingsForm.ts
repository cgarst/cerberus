import { useCallback, useEffect, useRef, useState } from 'react';
import { normalizeHostInput } from '../api/models';
import { usePCRemote } from './usePCRemote';

/**
 * Shared form state and validation logic for the platform-specific Settings screens.
 */
export function useSettingsForm() {
  const vm = usePCRemote();

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
    return normalized;
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

  const handlePortChange = useCallback(
    (text: string) => {
      setPortText(text);
      const val = Number(text);
      if (Number.isInteger(val) && val > 0 && val <= 65535) {
        vm.updateSetting('pcPort', val);
      }
    },
    [vm]
  );

  const handleTimeoutChange = useCallback(
    (text: string) => {
      setTimeoutText(text);
      const val = Number(text);
      if (Number.isInteger(val) && val >= 5 && val <= 180) {
        vm.updateSetting('wakeTimeoutSeconds', val);
      }
    },
    [vm]
  );

  return {
    vm,
    hostText,
    setHostText,
    portText,
    handlePortChange,
    timeoutText,
    handleTimeoutChange,
    commitHostAndPort,
  };
}
