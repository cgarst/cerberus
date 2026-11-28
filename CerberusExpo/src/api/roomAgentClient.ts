import {
  ApiErrorResponse,
  DesktopShortcut,
  getBaseUrl,
  HealthResponse,
  OpenWindow,
  OsdResponse,
  PCConnectionConfig,
  ShortcutsResponse,
  UpdateStatusResponse,
  WindowsResponse,
} from './models';

export type RoomAgentErrorKind =
  | 'invalid_configuration'
  | 'invalid_response'
  | 'unauthorized'
  | 'http_error'
  | 'unreachable';

export class RoomAgentClientError extends Error {
  readonly kind: RoomAgentErrorKind;
  readonly statusCode?: number;

  constructor(kind: RoomAgentErrorKind, message: string, statusCode?: number) {
    super(message);
    this.name = 'RoomAgentClientError';
    this.kind = kind;
    this.statusCode = statusCode;
  }

  get isConnectivityError(): boolean {
    return this.kind === 'unreachable';
  }

  static invalidConfiguration(): RoomAgentClientError {
    return new RoomAgentClientError(
      'invalid_configuration',
      'Invalid PC host or port in Settings.'
    );
  }

  static invalidResponse(): RoomAgentClientError {
    return new RoomAgentClientError(
      'invalid_response',
      'Invalid response from CerberusService.'
    );
  }

  static unauthorized(message?: string): RoomAgentClientError {
    return new RoomAgentClientError(
      'unauthorized',
      message ?? 'Authentication required. Please sign in.',
      401
    );
  }

  static httpError(code: number, message: string): RoomAgentClientError {
    return new RoomAgentClientError(
      'http_error',
      message || `CerberusService returned HTTP ${code}`,
      code
    );
  }

  static unreachable(detail: string): RoomAgentClientError {
    return new RoomAgentClientError(
      'unreachable',
      `PC is unreachable: ${detail}`
    );
  }
}

export class RoomAgentClient {
  private readonly config: PCConnectionConfig;

  constructor(config: PCConnectionConfig) {
    this.config = config;
  }

  async login(password: string): Promise<{ enabled: boolean; token: string }> {
    const baseUrl = getBaseUrl(this.config);
    if (!baseUrl) {
      throw RoomAgentClientError.invalidConfiguration();
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 5000);
    let response: Response;
    try {
      response = await fetch(`${baseUrl}/api/auth/login`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json; charset=utf-8',
          'Cache-Control': 'no-cache',
        },
        body: JSON.stringify({ password }),
        cache: 'no-store',
        signal: controller.signal,
      });
    } catch (err) {
      const msg =
        err instanceof Error
          ? err.name === 'AbortError'
            ? 'Request timed out after 5000ms'
            : err.message
          : String(err);
      throw RoomAgentClientError.unreachable(msg);
    } finally {
      clearTimeout(timer);
    }

    const rawText = await response.text();
    let parsed: { enabled?: boolean; token?: string; error?: string; retryAfterSeconds?: number } = {};
    try {
      if (rawText) parsed = JSON.parse(rawText);
    } catch {
      // Ignore parse failure
    }

    if (response.status === 401) {
      throw RoomAgentClientError.unauthorized('Incorrect password.');
    }
    if (response.status === 429) {
      const wait = parsed.retryAfterSeconds ? ` Try again in ${parsed.retryAfterSeconds}s.` : '';
      throw RoomAgentClientError.httpError(429, `Too many failed attempts.${wait}`);
    }
    if (!response.ok) {
      throw RoomAgentClientError.httpError(response.status, parsed.error ?? '');
    }

    return {
      enabled: Boolean(parsed.enabled),
      token: parsed.token ?? '',
    };
  }

  async fetchAuthStatus(timeoutMs = 3000): Promise<{ enabled: boolean }> {
    const resp = await this.request<{ enabled?: boolean }>(
      'GET',
      '/api/auth/status',
      undefined,
      timeoutMs
    );
    return { enabled: Boolean(resp?.enabled) };
  }

  async fetchHealth(timeoutMs = 2000): Promise<HealthResponse> {
    return this.request<HealthResponse>('GET', '/api/health', undefined, timeoutMs);
  }

  async fetchUpdateStatus(timeoutMs = 3000): Promise<UpdateStatusResponse> {
    return this.request<UpdateStatusResponse>('GET', '/api/update', undefined, timeoutMs);
  }

  async switchMode(
    profileId: string,
    consoleEnvironmentMode?: string
  ): Promise<void> {
    const path =
      profileId === 'living-room'
        ? '/api/mode/living-room'
        : profileId === 'office'
        ? '/api/mode/office'
        : `/api/profiles/${encodeURIComponent(profileId)}/activate`;
    const body = consoleEnvironmentMode ? { consoleEnvironmentMode } : undefined;
    await this.request<unknown>('POST', path, body, 25000);
  }

  async stopStreaming(
    targetMode: string,
    consoleEnvironmentMode?: string
  ): Promise<void> {
    const body: Record<string, string> = { targetMode };
    if (consoleEnvironmentMode) {
      body.consoleEnvironmentMode = consoleEnvironmentMode;
    }
    await this.request<unknown>('POST', '/api/streaming/stop', body, 25000);
  }

  async setConsoleEnvironmentMode(mode: string): Promise<void> {
    await this.request<unknown>(
      'POST',
      '/api/console-environment',
      { consoleEnvironmentMode: mode },
      15000
    );
  }

  async setOsdEnabled(enabled: boolean): Promise<boolean | null | undefined> {
    const resp = await this.request<OsdResponse>(
      'POST',
      '/api/osd',
      { enabled },
      5000
    );
    return resp.osdEnabled;
  }

  async sleepPC(): Promise<void> {
    await this.request<unknown>('POST', '/api/sleep', undefined, 5000);
  }

  async shutdownPC(): Promise<void> {
    await this.request<unknown>('POST', '/api/shutdown', undefined, 5000);
  }

  async fetchShortcuts(): Promise<DesktopShortcut[]> {
    const resp = await this.request<ShortcutsResponse>(
      'GET',
      '/api/shortcuts',
      undefined,
      4000
    );
    return resp.shortcuts ?? [];
  }

  async launchShortcut(path: string): Promise<void> {
    await this.request<unknown>(
      'POST',
      '/api/shortcuts/launch',
      { path },
      5000
    );
  }

  async fetchWindows(): Promise<OpenWindow[]> {
    const resp = await this.request<WindowsResponse>(
      'GET',
      '/api/windows',
      undefined,
      4000
    );
    return resp.windows ?? [];
  }

  async focusWindow(hwnd: number): Promise<void> {
    await this.request<unknown>(
      'POST',
      '/api/windows/focus',
      { hwnd },
      5000
    );
  }

  async closeWindow(hwnd: number): Promise<void> {
    await this.request<unknown>(
      'POST',
      '/api/windows/close',
      { hwnd },
      5000
    );
  }

  async closeAllWindows(): Promise<void> {
    await this.request<unknown>(
      'POST',
      '/api/windows/close',
      { all: true },
      5000
    );
  }

  async openTaskView(): Promise<void> {
    await this.request<unknown>('POST', '/api/windows/task-view', {}, 5000);
  }

  private async request<T>(
    method: 'GET' | 'POST',
    path: string,
    body?: unknown,
    timeoutMs = 5000
  ): Promise<T> {
    const baseUrl = getBaseUrl(this.config);
    if (!baseUrl) {
      throw RoomAgentClientError.invalidConfiguration();
    }

    const url = `${baseUrl}${path}`;
    const headers: Record<string, string> = {
      'Cache-Control': 'no-cache',
    };

    const token = this.config.bearerToken.trim();
    if (token.length > 0) {
      headers.Authorization = `Bearer ${token}`;
    }

    let encodedBody: string | undefined;
    if (method === 'POST' && body !== undefined) {
      headers['Content-Type'] = 'application/json; charset=utf-8';
      encodedBody = JSON.stringify(body);
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);

    let response: Response;
    try {
      response = await fetch(url, {
        method,
        headers,
        body: encodedBody,
        cache: 'no-store',
        signal: controller.signal,
      });
    } catch (err) {
      const msg =
        err instanceof Error
          ? err.name === 'AbortError'
            ? `Request timed out after ${timeoutMs}ms`
            : err.message
          : String(err);
      throw RoomAgentClientError.unreachable(msg);
    } finally {
      clearTimeout(timer);
    }

    if (response.status === 401) {
      throw RoomAgentClientError.unauthorized();
    }

    const rawText = await response.text();
    if (!response.ok) {
      let apiErrMsg = '';
      try {
        const parsed = JSON.parse(rawText) as ApiErrorResponse;
        apiErrMsg = parsed.error ?? '';
      } catch {
        // Ignore parse failure on error body
      }
      throw RoomAgentClientError.httpError(response.status, apiErrMsg);
    }

    if (!rawText) {
      return undefined as T;
    }

    try {
      return JSON.parse(rawText) as T;
    } catch {
      throw RoomAgentClientError.invalidResponse();
    }
  }
}
