import { getWsBaseUrl, PCConnectionConfig } from './models';

export type RemoteInputConnectionState =
  | 'connecting'
  | 'connected'
  | 'disconnected'
  | 'error';

export type MouseButtonName = 'left' | 'right' | 'middle';
export type MouseButtonAction = 'down' | 'up' | 'click';
export type KeyAction = 'down' | 'up' | 'press';

export class RemoteInputClient {
  private readonly config: PCConnectionConfig;
  private readonly onStateChange?: (state: RemoteInputConnectionState) => void;
  private socket: WebSocket | null = null;
  private closedByCaller = false;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(
    config: PCConnectionConfig,
    onStateChange?: (state: RemoteInputConnectionState) => void
  ) {
    this.config = config;
    this.onStateChange = onStateChange;
  }

  connect(): void {
    this.closedByCaller = false;
    this.clearReconnectTimer();

    const wsBase = getWsBaseUrl(this.config);
    const token = this.config.bearerToken?.trim() ?? '';
    if (!wsBase || !token) {
      this.onStateChange?.('error');
      return;
    }

    if (
      this.socket &&
      (this.socket.readyState === WebSocket.OPEN ||
        this.socket.readyState === WebSocket.CONNECTING)
    ) {
      return;
    }

    this.onStateChange?.('connecting');
    const url = `${wsBase}/api/input/ws?token=${encodeURIComponent(token)}`;
    const ws = new WebSocket(url);
    this.socket = ws;

    ws.onopen = () => {
      if (this.socket !== ws || this.closedByCaller) {
        ws.close();
        return;
      }
      this.onStateChange?.('connected');
    };

    ws.onerror = () => {
      if (this.socket !== ws || this.closedByCaller) return;
      this.onStateChange?.('error');
    };

    ws.onclose = () => {
      if (this.socket !== ws) return;
      this.socket = null;
      this.onStateChange?.('disconnected');
      if (!this.closedByCaller) {
        this.scheduleReconnect();
      }
    };
  }

  disconnect(): void {
    this.closedByCaller = true;
    this.clearReconnectTimer();
    const ws = this.socket;
    this.socket = null;
    if (ws) {
      try {
        ws.close();
      } catch {
        // Ignore close errors
      }
    }
    this.onStateChange?.('disconnected');
  }

  sendMove(dx: number, dy: number): boolean {
    const roundedX = Math.round(dx);
    const roundedY = Math.round(dy);
    if (roundedX === 0 && roundedY === 0) return true;
    return this.sendPayload({ t: 'move', dx: roundedX, dy: roundedY });
  }

  sendScroll(dx: number, dy: number): boolean {
    const roundedX = Math.round(dx);
    const roundedY = Math.round(dy);
    if (roundedX === 0 && roundedY === 0) return true;
    return this.sendPayload({ t: 'scroll', dx: roundedX, dy: roundedY });
  }

  sendButton(button: MouseButtonName, action: MouseButtonAction = 'click'): boolean {
    return this.sendPayload({ t: 'btn', b: button, a: action });
  }

  sendKey(vk: number, action: KeyAction = 'press'): boolean {
    return this.sendPayload({ t: 'key', vk, a: action });
  }

  sendCombo(vks: number[]): boolean {
    if (vks.length === 0) return false;
    return this.sendPayload({ t: 'combo', vks });
  }

  sendText(text: string): boolean {
    if (!text) return true;
    return this.sendPayload({ t: 'text', s: text });
  }

  private sendPayload(payload: Record<string, unknown>): boolean {
    const ws = this.socket;
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      return false;
    }
    try {
      ws.send(JSON.stringify(payload));
      return true;
    } catch {
      return false;
    }
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer || this.closedByCaller) return;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (!this.closedByCaller) {
        this.connect();
      }
    }, 1500);
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }
}
