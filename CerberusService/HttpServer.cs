using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RoomAgent
{
    public class HttpServer : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly AgentConfig _config;
        private readonly RoomController _controller;
        private readonly ILogger _logger;
        private readonly ISystemMonitor _systemMonitor;
        private readonly UpdateService _updateService;
        private readonly AuthService _auth = new AuthService();
        private readonly IRemoteInputSender _remoteInputSender = new WindowsRemoteInputSender();
        private readonly object _listenerLock = new object();
        private HttpListener _listener;
        private int _listeningPort;
        private CancellationTokenSource _cts;
        private bool _disposed;

        public HttpServer(AgentConfig config, RoomController controller, ILogger logger, ISystemMonitor systemMonitor = null, UpdateService updateService = null)
        {
            _config = config;
            _controller = controller;
            _logger = logger;
            _systemMonitor = systemMonitor;
            _updateService = updateService;
            if (_controller != null)
            {
                _controller.ConfigReloaded += OnConfigReloaded;
            }
        }

        public void Start()
        {
            lock (_listenerLock)
            {
                StartListenerLocked();
            }
        }

        private void StartListenerLocked()
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listeningPort = _config.Port;
            _listener.Prefixes.Add(string.Format("http://+:{0}/", _listeningPort));
            _listener.Start();
            _logger.Info(string.Format("HTTP server listening on port {0}", _listeningPort));
            HttpListener listener = _listener;
            CancellationToken token = _cts.Token;
            Task.Run(() => AcceptLoopAsync(listener, token));
        }

        private void StopListenerLocked()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
                try { _cts.Dispose(); } catch { }
                _cts = null;
            }
            if (_listener != null)
            {
                try
                {
                    if (_listener.IsListening)
                    {
                        _listener.Stop();
                    }
                }
                catch { }
                try { _listener.Close(); } catch { }
                _listener = null;
            }
        }

        private void OnConfigReloaded(object sender, EventArgs e)
        {
            int targetPort = _config.Port;
            lock (_listenerLock)
            {
                if (_disposed || _listener == null || targetPort == _listeningPort)
                {
                    return;
                }
            }

            Task.Run(async () =>
            {
                await Task.Delay(150).ConfigureAwait(false);
                lock (_listenerLock)
                {
                    if (_disposed || _listener == null || _config.Port == _listeningPort)
                    {
                        return;
                    }
                    int oldPort = _listeningPort;
                    try
                    {
                        StartupTaskManager.EnsureNetworkAccess(_config.Port, _logger);
                        StopListenerLocked();
                        StartListenerLocked();
                        _logger.Info(string.Format("HTTP server restarted on port {0} (was {1})", _listeningPort, oldPort));
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(string.Format("Failed to restart HTTP server on port {0}", _config.Port), ex);
                    }
                }
            });
        }

        private async Task AcceptLoopAsync(HttpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener.IsListening)
            {
                try
                {
                    HttpListenerContext ctx = await listener.GetContextAsync().ConfigureAwait(false);
                    Task ignored = Task.Run(() => HandleRequestAsync(ctx));
                }
                catch (HttpListenerException)
                {
                    if (token.IsCancellationRequested) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("AcceptLoop error", ex);
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;

            try
            {
                string method = req.HttpMethod.ToUpperInvariant();
                string rawPath = req.Url.AbsolutePath ?? "/";
                string path = rawPath.TrimEnd('/').ToLowerInvariant();
                if (string.IsNullOrEmpty(path)) path = "/";

                if (method == "GET" && path == "/api/auth/status")
                {
                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "enabled", AuthService.IsAuthEnabled(_config) }
                    }, JsonOptions));
                    return;
                }

                if (method == "POST" && path == "/api/auth/login")
                {
                    if (!AuthService.IsAuthEnabled(_config))
                    {
                        WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            { "enabled", false },
                            { "token", "" }
                        }, JsonOptions));
                        return;
                    }

                    string clientIp = req.RemoteEndPoint != null && req.RemoteEndPoint.Address != null
                        ? req.RemoteEndPoint.Address.ToString()
                        : "unknown";

                    int retryAfter;
                    if (!_auth.IsLoginAllowed(clientIp, out retryAfter))
                    {
                        res.Headers["Retry-After"] = retryAfter.ToString();
                        WriteJson(res, 429, JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            { "error", "too_many_attempts" },
                            { "retryAfterSeconds", retryAfter }
                        }, JsonOptions));
                        return;
                    }

                    Dictionary<string, object> body = ReadJsonBody(req);
                    string password = GetString(body, "password");
                    if (!AuthService.VerifyPassword(_config, password))
                    {
                        _auth.RecordLoginFailure(clientIp);
                        WriteJson(res, 401, "{\"error\":\"invalid_password\"}");
                        return;
                    }

                    _auth.ResetLoginFailures(clientIp);
                    string issuedToken = AuthService.IssueToken(_config);
                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "enabled", true },
                        { "token", issuedToken }
                    }, JsonOptions));
                    return;
                }

                if (method == "GET" && path == "/api/input/ws")
                {
                    if (!AuthService.IsAuthEnabled(_config))
                    {
                        WriteJson(res, 403, "{\"error\":\"auth_required_for_remote_input\"}");
                        return;
                    }

                    string wsToken = ExtractBearerToken(req.Headers["Authorization"]);
                    if (string.IsNullOrWhiteSpace(wsToken))
                    {
                        wsToken = req.QueryString["token"] ?? "";
                    }
                    if (!AuthService.VerifyToken(_config, wsToken))
                    {
                        res.Headers["WWW-Authenticate"] = "Bearer";
                        WriteJson(res, 401, "{\"error\":\"unauthorized\"}");
                        return;
                    }

                    if (!req.IsWebSocketRequest)
                    {
                        WriteJson(res, 400, "{\"error\":\"websocket_required\"}");
                        return;
                    }

                    await HandleRemoteInputWebSocketAsync(ctx).ConfigureAwait(false);
                    return;
                }

                if (path.StartsWith("/api/", StringComparison.Ordinal) && AuthService.IsAuthEnabled(_config))
                {
                    string bearerToken = ExtractBearerToken(req.Headers["Authorization"]);
                    if (!AuthService.VerifyToken(_config, bearerToken))
                    {
                        res.Headers["WWW-Authenticate"] = "Bearer";
                        WriteJson(res, 401, "{\"error\":\"unauthorized\"}");
                        return;
                    }
                }

                if (method == "GET" && (path == "/" || path == "/index.html"))
                {
                    byte[] htmlBytes = GetWebUiHtmlBytes("index.html");
                    if (htmlBytes == null)
                    {
                        WriteJson(res, 404, "{\"error\":\"web_ui_not_found\"}");
                        return;
                    }
                    WriteBytes(res, 200, "text/html; charset=utf-8", htmlBytes, "no-cache");
                    return;
                }

                if (method == "GET" && path == "/manifest.webmanifest")
                {
                    WriteBytes(res, 200, "application/manifest+json; charset=utf-8", WebManifestBytes, "public, max-age=86400");
                    return;
                }

                if (method == "GET" && (path == "/apple-touch-icon.png" || path == "/apple-touch-icon-precomposed.png" || path == "/icon-180.png"))
                {
                    WriteBytes(res, 200, "image/png", WebIconPngBytes, "public, max-age=86400");
                    return;
                }

                if (method == "GET" && path == "/api/devices")
                {
                    List<NativeInterop.DisplayDeviceInfo> displays = NativeInterop.GetAllDisplayInfos();
                    List<NativeInterop.AudioDeviceInfo> audioDevices = NativeInterop.GetAudioPlaybackDevices();

                    List<Dictionary<string, object>> displayItems = new List<Dictionary<string, object>>();
                    for (int i = 0; i < displays.Count; i++)
                    {
                        NativeInterop.DisplayDeviceInfo d = displays[i];
                        displayItems.Add(new Dictionary<string, object>
                        {
                            { "friendlyName", d.FriendlyName },
                            { "devicePath", d.DevicePath },
                            { "isActive", d.IsActive },
                            { "isAvailable", d.IsAvailable },
                            { "width", d.Width },
                            { "height", d.Height },
                            { "refreshRate", d.RefreshRate },
                            { "hdrSupported", d.HdrSupported },
                            { "hdrEnabled", d.HdrEnabled }
                        });
                    }

                    List<Dictionary<string, object>> audioItems = new List<Dictionary<string, object>>();
                    for (int i = 0; i < audioDevices.Count; i++)
                    {
                        NativeInterop.AudioDeviceInfo a = audioDevices[i];
                        audioItems.Add(new Dictionary<string, object>
                        {
                            { "id", a.Id },
                            { "name", a.Name },
                            { "isDefault", a.IsDefault }
                        });
                    }

                    List<Dictionary<string, object>> integrationItems = new List<Dictionary<string, object>>();
                    IList<IRoomIntegration> integrations = _controller.GetIntegrations();
                    for (int i = 0; i < integrations.Count; i++)
                    {
                        IRoomIntegration integ = integrations[i];
                        integrationItems.Add(new Dictionary<string, object>
                        {
                            { "id", integ.Id },
                            { "name", integ.DisplayName },
                            { "enabled", integ.IsEnabled(_config) }
                        });
                    }

                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "displays", displayItems },
                        { "audioDevices", audioItems },
                        { "integrations", integrationItems }
                    }, JsonOptions));
                    return;
                }

                if (method == "GET" && path == "/api/profiles")
                {
                    IList<EnvironmentConfig> profiles = _controller.GetProfiles();
                    List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
                    for (int i = 0; i < profiles.Count; i++)
                    {
                        EnvironmentConfig p = profiles[i];
                        items.Add(new Dictionary<string, object>
                        {
                            { "id", p.Id },
                            { "name", p.Name },
                            { "isDefault", p.IsDefault },
                            { "available", _controller.IsProfileMonitorAvailable(p) },
                            { "monitor", p.Monitor },
                            { "audio", p.Audio },
                            { "width", p.Width },
                            { "height", p.Height },
                            { "refreshRate", p.RefreshRate },
                            { "hdr", p.Hdr },
                            { "consoleEnvironmentMode", _config.GetRoomConsoleEnvironmentMode(p.Id) },
                            { "closeApps", p.CloseApps },
                            { "integrations", p.Integrations }
                        });
                    }
                    EnvironmentConfig defProfile = _config.GetDefaultProfile();
                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "activeProfileId", _controller.ActiveProfileId },
                        { "defaultProfileId", defProfile != null ? defProfile.Id : "" },
                        { "profiles", items }
                    }, JsonOptions));
                    return;
                }

                if (method == "GET" && path == "/api/config")
                {
                    JsonObject configNode = JsonSerializer.SerializeToNode(_config, JsonOptions) as JsonObject ?? new JsonObject();
                    configNode["AuthPasswordHash"] = "";
                    configNode["AuthPasswordSalt"] = "";
                    configNode["AuthTokenSecret"] = "";
                    WriteJson(res, 200, configNode.ToJsonString(JsonOptions));
                    return;
                }

                if (method == "POST" && path == "/api/config")
                {
                    string rawJson = ReadRawBody(req);
                    if (string.IsNullOrWhiteSpace(rawJson))
                    {
                        WriteJson(res, 400, "{\"error\":\"missing_body\"}");
                        return;
                    }
                    AgentConfig updated = JsonSerializer.Deserialize<AgentConfig>(rawJson, JsonOptions);
                    if (updated == null)
                    {
                        WriteJson(res, 400, "{\"error\":\"invalid_json\"}");
                        return;
                    }
                    updated.AuthPasswordHash = _config.AuthPasswordHash;
                    updated.AuthPasswordSalt = _config.AuthPasswordSalt;
                    updated.AuthTokenSecret = _config.AuthTokenSecret;
                    await _controller.ReloadConfigAsync(updated, rawJson).ConfigureAwait(false);
                    WriteJson(res, 200, "{\"status\":\"ok\"}");
                    return;
                }

                if (method == "GET" && path == "/api/update")
                {
                    if (_updateService == null)
                    {
                        WriteJson(res, 200, JsonSerializer.Serialize(new
                        {
                            status = "unavailable",
                            currentVersion = UpdateService.GetCurrentVersion()
                        }, JsonOptions));
                        return;
                    }
                    WriteJson(res, 200, JsonSerializer.Serialize(_updateService.GetSnapshot(), JsonOptions));
                    return;
                }

                if (method == "POST" && path == "/api/update/check")
                {
                    if (_updateService == null)
                    {
                        WriteJson(res, 503, "{\"error\":\"update_service_unavailable\"}");
                        return;
                    }
                    UpdateStateSnapshot snap = await _updateService.CheckForUpdatesAsync().ConfigureAwait(false);
                    WriteJson(res, 200, JsonSerializer.Serialize(snap, JsonOptions));
                    return;
                }

                if (method == "POST" && path == "/api/update/install")
                {
                    if (!AuthService.IsAuthEnabled(_config))
                    {
                        WriteJson(res, 403, "{\"error\":\"auth_required_for_update\"}");
                        return;
                    }
                    if (_updateService == null)
                    {
                        WriteJson(res, 503, "{\"error\":\"update_service_unavailable\"}");
                        return;
                    }
                    UpdateStateSnapshot snap = _updateService.GetSnapshot();
                    if (snap.Status != UpdateStatus.UpdateAvailable || snap.AvailableUpdate == null)
                    {
                        WriteJson(res, 409, "{\"error\":\"no_update_available\"}");
                        return;
                    }
                    _ = Task.Run(() => _updateService.DownloadAndInstallAsync());
                    WriteJson(res, 202, "{\"status\":\"installing\"}");
                    return;
                }

                if (method == "GET" && path == "/api/health")
                {
                    StreamingSessionInfo streaming = _controller.GetStreamingSessionInfo();
                    bool streamingActive = streaming != null && streaming.Active;
                    string underlyingMode = _controller.CurrentMode == RoomMode.LivingRoom ? "living-room" : "office";
                    string modeStr = streamingActive ? "streaming" : underlyingMode;
                    int idleSec = _controller.GetIdleSeconds();
                    bool officeAvail = _controller.IsOfficeMonitorAvailable();
                    bool livingAvail = _controller.IsLivingRoomMonitorAvailable();
                    string audioDevice = NativeInterop.GetDefaultAudioPlaybackDeviceName();
                    string consoleEnvironmentMode = _controller.ActiveConsoleEnvironmentMode;
                    EnvironmentConfig activeProfile = _controller.ActiveProfile;
                    EnvironmentConfig defProfile = _config.GetDefaultProfile();
                    WakeOnLanNetworkInfo wolInfo = NetworkAddressHelper.GetWakeOnLanInfo();
                    List<Dictionary<string, object>> profileSummaries = new List<Dictionary<string, object>>();
                    IList<EnvironmentConfig> allProfiles = _controller.GetProfiles();
                    for (int i = 0; i < allProfiles.Count; i++)
                    {
                        EnvironmentConfig p = allProfiles[i];
                        profileSummaries.Add(new Dictionary<string, object>
                        {
                            { "id", p.Id },
                            { "name", p.Name },
                            { "isDefault", p.IsDefault },
                            { "available", _controller.IsProfileMonitorAvailable(p) },
                            { "consoleEnvironmentMode", _config.GetRoomConsoleEnvironmentMode(p.Id) }
                        });
                    }
                    Dictionary<string, object> healthPayload = new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "version", UpdateService.GetCurrentVersion() },
                        { "authEnabled", AuthService.IsAuthEnabled(_config) },
                        { "mode", modeStr },
                        { "underlyingMode", underlyingMode },
                        { "activeProfileId", _controller.ActiveProfileId },
                        { "activeProfileName", activeProfile != null ? activeProfile.Name : _controller.ActiveProfileId },
                        { "defaultProfileId", defProfile != null ? defProfile.Id : "" },
                        { "profiles", profileSummaries },
                        { "streamingActive", streamingActive },
                        { "streamingDisplay", streamingActive ? (streaming.DisplayName ?? "") : "" },
                        { "streamingReason", streamingActive ? (streaming.Reason ?? "") : "" },
                        { "idleSeconds", idleSec },
                        { "officeAvailable", officeAvail },
                        { "livingRoomAvailable", livingAvail },
                        { "audioDevice", audioDevice ?? "" },
                        { "consoleEnvironmentOnLivingRoom", !string.Equals(_config.GetRoomConsoleEnvironmentMode(RoomMode.LivingRoom), "off", StringComparison.OrdinalIgnoreCase) },
                        { "consoleEnvironmentMode", consoleEnvironmentMode },
                        { "streamingConsoleEnvironmentMode", _config.StreamingConsoleEnvironmentMode ?? "" },
                        { "officeConsoleEnvironmentMode", _config.GetRoomConsoleEnvironmentMode(RoomMode.Office) },
                        { "livingRoomConsoleEnvironmentMode", _config.GetRoomConsoleEnvironmentMode(RoomMode.LivingRoom) },
                        { "wolMac", wolInfo != null ? (wolInfo.MacAddress ?? "") : "" },
                        { "wolBroadcastIp", wolInfo != null ? (wolInfo.BroadcastIp ?? "") : "" }
                    };
                    if (_systemMonitor != null)
                    {
                        SystemMetricsSnapshot metrics = _systemMonitor.GetSnapshot();
                        if (metrics != null)
                        {
                            healthPayload["metrics"] = new Dictionary<string, object>
                            {
                                { "cpuName", metrics.CpuName },
                                { "cpuPercent", metrics.CpuPercent },
                                { "cpuTempC", metrics.CpuTempC },
                                { "ramName", metrics.RamName },
                                { "ramPercent", metrics.RamPercent },
                                { "ramUsedGb", metrics.RamUsedGb },
                                { "ramTotalGb", metrics.RamTotalGb },
                                { "gpuPercent", metrics.GpuPercent },
                                { "gpuTempC", metrics.GpuTempC },
                                { "gpuHotspotTempC", metrics.GpuHotspotTempC },
                                { "gpuVramUsedGb", metrics.GpuVramUsedGb },
                                { "gpuVramTotalGb", metrics.GpuVramTotalGb },
                                { "gpuName", metrics.GpuName },
                                { "fps", metrics.Fps },
                                { "frametimeMs", metrics.FrametimeMs },
                                { "fpsHistory", metrics.FpsHistory },
                                { "frametimeHistoryMs", metrics.FrametimeHistoryMs },
                                { "frameAppName", metrics.FrameAppName },
                                { "frameAppIcon", metrics.FrameAppIcon },
                                { "frameAppHeroUrl", metrics.FrameAppHeroUrl },
                                { "osdEnabled", metrics.OsdEnabled },
                                { "osdMode", metrics.OsdMode },
                                { "availableOsdModes", metrics.AvailableOsdModes }
                            };
                        }
                    }
                    string body = JsonSerializer.Serialize(healthPayload, JsonOptions);
                    WriteJson(res, 200, body);
                    return;
                }

                if (method == "GET" && path == "/api/frame-app-hero")
                {
                    string key = req.QueryString["key"];
                    byte[] heroBytes;
                    string contentType;
                    if (_systemMonitor != null && _systemMonitor.TryGetHeroImage(key, out heroBytes, out contentType))
                    {
                        WriteBytes(res, 200, contentType, heroBytes, "public, max-age=3600");
                        return;
                    }
                    WriteJson(res, 404, "{\"error\":\"hero_not_found\"}");
                    return;
                }

                if (method == "POST" && path == "/api/console-environment")
                {
                    string consoleEnvironmentMode = req.QueryString["consoleEnvironmentMode"];
                    if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                    {
                        consoleEnvironmentMode = req.QueryString["mode"];
                    }
                    if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                    {
                        Dictionary<string, object> reqBody = ReadJsonBody(req);
                        consoleEnvironmentMode = GetString(reqBody, "consoleEnvironmentMode");
                        if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                        {
                            consoleEnvironmentMode = GetString(reqBody, "mode");
                        }
                    }
                    if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                    {
                        WriteJson(res, 400, "{\"error\":\"missing_console_environment_mode\"}");
                        return;
                    }

                    await _controller.SetConsoleEnvironmentModeAsync(consoleEnvironmentMode).ConfigureAwait(false);
                    string modeStr = _controller.CurrentMode == RoomMode.LivingRoom ? "living-room" : "office";
                    string body = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "mode", modeStr },
                        { "consoleEnvironmentMode", _controller.ActiveConsoleEnvironmentMode }
                    }, JsonOptions);
                    WriteJson(res, 200, body);
                    return;
                }

                if (method == "POST" && path.StartsWith("/api/profiles/") && path.EndsWith("/activate"))
                {
                    string profileId = path.Substring("/api/profiles/".Length, path.Length - "/api/profiles/".Length - "/activate".Length).Trim('/');
                    string consoleEnvironmentMode = req.QueryString["consoleEnvironmentMode"];
                    if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                    {
                        Dictionary<string, object> reqBody = ReadJsonBody(req);
                        consoleEnvironmentMode = GetString(reqBody, "consoleEnvironmentMode");
                    }
                    await _controller.SwitchToProfileAsync(profileId, true, consoleEnvironmentMode, "api").ConfigureAwait(false);
                    string body = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "activeProfileId", _controller.ActiveProfileId },
                        { "mode", _controller.CurrentMode == RoomMode.LivingRoom ? "living-room" : "office" },
                        { "consoleEnvironmentMode", _controller.ActiveConsoleEnvironmentMode }
                    }, JsonOptions);
                    WriteJson(res, 200, body);
                    return;
                }

                if (method == "POST" && path.StartsWith("/api/mode/"))
                {
                    string modeId = path.Substring("/api/mode/".Length).Trim('/');
                    string consoleEnvironmentMode = req.QueryString["consoleEnvironmentMode"];
                    if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                    {
                        Dictionary<string, object> reqBody = ReadJsonBody(req);
                        consoleEnvironmentMode = GetString(reqBody, "consoleEnvironmentMode");
                    }
                    if (string.Equals(modeId, "living-room", StringComparison.OrdinalIgnoreCase))
                    {
                        await _controller.SwitchToLivingRoomAsync(true, consoleEnvironmentMode).ConfigureAwait(false);
                    }
                    else if (string.Equals(modeId, "office", StringComparison.OrdinalIgnoreCase))
                    {
                        await _controller.SwitchToOfficeAsync("api", consoleEnvironmentMode).ConfigureAwait(false);
                    }
                    else
                    {
                        await _controller.SwitchToProfileAsync(modeId, true, consoleEnvironmentMode, "api").ConfigureAwait(false);
                    }
                    string body = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "activeProfileId", _controller.ActiveProfileId },
                        { "mode", _controller.CurrentMode == RoomMode.LivingRoom ? "living-room" : "office" },
                        { "consoleEnvironmentMode", _controller.ActiveConsoleEnvironmentMode }
                    }, JsonOptions);
                    WriteJson(res, 200, body);
                    return;
                }

                if (method == "POST" && path == "/api/streaming/start")
                {
                    string consoleEnvironmentMode = req.QueryString["consoleEnvironmentMode"] ?? req.QueryString["mode"];
                    if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                    {
                        Dictionary<string, object> reqBody = ReadJsonBody(req);
                        consoleEnvironmentMode = GetString(reqBody, "consoleEnvironmentMode");
                        if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                        {
                            consoleEnvironmentMode = GetString(reqBody, "mode");
                        }
                    }

                    await _controller.StartStreamingEnvironmentAsync(consoleEnvironmentMode, "api").ConfigureAwait(false);
                    StreamingSessionInfo streaming = _controller.GetStreamingSessionInfo();
                    bool streamingActive = streaming != null && streaming.Active;
                    string underlyingMode = _controller.CurrentMode == RoomMode.LivingRoom ? "living-room" : "office";
                    string modeStr = streamingActive ? "streaming" : underlyingMode;
                    string body = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "mode", modeStr },
                        { "streamingActive", streamingActive },
                        { "consoleEnvironmentMode", _controller.ActiveConsoleEnvironmentMode }
                    }, JsonOptions);
                    WriteJson(res, 200, body);
                    return;
                }

                if (method == "POST" && path == "/api/streaming/end")
                {
                    await _controller.EndStreamingEnvironmentAsync("api").ConfigureAwait(false);
                    StreamingSessionInfo streaming = _controller.GetStreamingSessionInfo();
                    bool streamingActive = streaming != null && streaming.Active;
                    string underlyingMode = _controller.CurrentMode == RoomMode.LivingRoom ? "living-room" : "office";
                    string modeStr = streamingActive ? "streaming" : underlyingMode;
                    string body = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "mode", modeStr },
                        { "streamingActive", streamingActive },
                        { "consoleEnvironmentMode", _controller.ActiveConsoleEnvironmentMode }
                    }, JsonOptions);
                    WriteJson(res, 200, body);
                    return;
                }

                if (method == "POST" && path == "/api/streaming/stop")
                {
                    Dictionary<string, object> reqBody = null;
                    string targetMode = req.QueryString["targetMode"] ?? req.QueryString["mode"];
                    string consoleEnvironmentMode = req.QueryString["consoleEnvironmentMode"];
                    if (string.IsNullOrWhiteSpace(targetMode) || string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                    {
                        reqBody = ReadJsonBody(req);
                        if (string.IsNullOrWhiteSpace(targetMode))
                        {
                            targetMode = GetString(reqBody, "targetMode");
                            if (string.IsNullOrWhiteSpace(targetMode))
                            {
                                targetMode = GetString(reqBody, "mode");
                            }
                        }
                        if (string.IsNullOrWhiteSpace(consoleEnvironmentMode))
                        {
                            consoleEnvironmentMode = GetString(reqBody, "consoleEnvironmentMode");
                        }
                    }

                    await _controller.StopStreamingAsync(targetMode, consoleEnvironmentMode).ConfigureAwait(false);
                    StreamingSessionInfo streaming = _controller.GetStreamingSessionInfo();
                    bool streamingActive = streaming != null && streaming.Active;
                    string underlyingMode = _controller.CurrentMode == RoomMode.LivingRoom ? "living-room" : "office";
                    string modeStr = streamingActive ? "streaming" : underlyingMode;
                    string body = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "mode", modeStr },
                        { "streamingActive", streamingActive },
                        { "consoleEnvironmentMode", _controller.ActiveConsoleEnvironmentMode }
                    }, JsonOptions);
                    WriteJson(res, 200, body);
                    return;
                }

                if (method == "POST" && path == "/api/sleep")
                {
                    WriteJson(res, 200, "{\"status\":\"ok\",\"action\":\"sleep\"}");
                    Task ignored = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(300).ConfigureAwait(false);
                            await _controller.SleepAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("Error executing sleep action", ex);
                        }
                    });
                    return;
                }

                if (method == "POST" && path == "/api/shutdown")
                {
                    WriteJson(res, 200, "{\"status\":\"ok\",\"action\":\"shutdown\"}");
                    Task ignored = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(300).ConfigureAwait(false);
                            await _controller.ShutdownAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("Error executing shutdown action", ex);
                        }
                    });
                    return;
                }

                if (method == "GET" && path == "/api/shortcuts")
                {
                    IList<DesktopShortcutInfo> shortcuts = _controller.GetDesktopShortcuts();
                    List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
                    foreach (DesktopShortcutInfo s in shortcuts)
                    {
                        items.Add(new Dictionary<string, object>
                        {
                            { "name", s.Name },
                            { "path", s.Path },
                            { "extension", s.Extension },
                            { "source", s.Source },
                            { "icon", s.IconDataUrl ?? "" }
                        });
                    }
                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object> { { "shortcuts", items } }, JsonOptions));
                    return;
                }

                if (method == "GET" && path == "/api/icon")
                {
                    string filePath = req.QueryString["path"];
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        WriteJson(res, 400, "{\"error\":\"missing_path\"}");
                        return;
                    }
                    if (!File.Exists(filePath))
                    {
                        WriteJson(res, 404, "{\"error\":\"file_not_found\"}");
                        return;
                    }
                    string icon = NativeInterop.ExtractShellFileIconDataUrl(filePath) ?? "";
                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object> { { "icon", icon } }, JsonOptions));
                    return;
                }

                if (method == "POST" && path == "/api/shortcuts/launch")
                {
                    Dictionary<string, object> reqBody = ReadJsonBody(req);
                    string target = GetString(reqBody, "path");
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        target = GetString(reqBody, "name");
                    }
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        WriteJson(res, 400, "{\"error\":\"missing_shortcut\"}");
                        return;
                    }

                    string launchedName;
                    if (!_controller.LaunchDesktopShortcut(target, out launchedName))
                    {
                        WriteJson(res, 404, "{\"error\":\"shortcut_not_found\"}");
                        return;
                    }

                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "launched", launchedName }
                    }, JsonOptions));
                    return;
                }

                if (method == "GET" && path == "/api/windows")
                {
                    IList<OpenWindowInfo> windows = _controller.GetOpenWindows();
                    List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
                    foreach (OpenWindowInfo w in windows)
                    {
                        items.Add(new Dictionary<string, object>
                        {
                            { "hwnd", w.Hwnd },
                            { "pid", w.Pid },
                            { "process", w.ProcessName ?? "" },
                            { "title", w.Title ?? "" },
                            { "icon", w.IconDataUrl ?? "" }
                        });
                    }
                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object> { { "windows", items } }, JsonOptions));
                    return;
                }

                if (method == "POST" && path == "/api/windows/close")
                {
                    Dictionary<string, object> reqBody = ReadJsonBody(req);
                    bool closeAll = GetBool(reqBody, "all");
                    if (closeAll)
                    {
                        int closed = _controller.CloseAllOpenWindows();
                        WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            { "status", "ok" },
                            { "closed", closed }
                        }, JsonOptions));
                        return;
                    }

                    long hwnd;
                    if (!TryGetLong(reqBody, "hwnd", out hwnd) || hwnd == 0)
                    {
                        WriteJson(res, 400, "{\"error\":\"missing_hwnd\"}");
                        return;
                    }

                    if (!_controller.CloseWindow(hwnd))
                    {
                        WriteJson(res, 404, "{\"error\":\"window_not_found\"}");
                        return;
                    }

                    WriteJson(res, 200, "{\"status\":\"ok\",\"closed\":1}");
                    return;
                }

                if (method == "POST" && path == "/api/windows/focus")
                {
                    Dictionary<string, object> reqBody = ReadJsonBody(req);
                    long hwnd;
                    if (!TryGetLong(reqBody, "hwnd", out hwnd) || hwnd == 0)
                    {
                        WriteJson(res, 400, "{\"error\":\"missing_hwnd\"}");
                        return;
                    }

                    if (!_controller.FocusWindow(hwnd))
                    {
                        WriteJson(res, 404, "{\"error\":\"window_not_found\"}");
                        return;
                    }

                    WriteJson(res, 200, "{\"status\":\"ok\",\"focused\":1}");
                    return;
                }

                if (method == "POST" && path == "/api/windows/maximize")
                {
                    Dictionary<string, object> reqBody = ReadJsonBody(req);
                    long hwnd;
                    if (!TryGetLong(reqBody, "hwnd", out hwnd) || hwnd == 0)
                    {
                        WriteJson(res, 400, "{\"error\":\"missing_hwnd\"}");
                        return;
                    }

                    if (!_controller.MaximizeWindow(hwnd))
                    {
                        WriteJson(res, 404, "{\"error\":\"window_not_found\"}");
                        return;
                    }

                    WriteJson(res, 200, "{\"status\":\"ok\",\"maximized\":1}");
                    return;
                }

                if (method == "POST" && path == "/api/windows/task-view")
                {
                    if (!_controller.OpenTaskView())
                    {
                        WriteJson(res, 500, "{\"error\":\"task_view_failed\"}");
                        return;
                    }

                    WriteJson(res, 200, "{\"status\":\"ok\"}");
                    return;
                }

                if (method == "POST" && path == "/api/osd")
                {
                    string requestedMode = req.QueryString["mode"];
                    bool? requested = null;
                    string qEnabled = req.QueryString["enabled"];
                    bool parsed;
                    if (!string.IsNullOrWhiteSpace(qEnabled) && bool.TryParse(qEnabled, out parsed))
                    {
                        requested = parsed;
                    }
                    else if (string.IsNullOrWhiteSpace(requestedMode))
                    {
                        Dictionary<string, object> reqBody = ReadJsonBody(req);
                        object rawMode;
                        if (reqBody != null && reqBody.TryGetValue("mode", out rawMode) && rawMode != null)
                        {
                            requestedMode = rawMode is JsonElement jeMode
                                ? (jeMode.ValueKind == JsonValueKind.String ? jeMode.GetString() : null)
                                : rawMode.ToString();
                        }

                        object rawVal;
                        if (string.IsNullOrWhiteSpace(requestedMode) && reqBody != null && reqBody.TryGetValue("enabled", out rawVal) && rawVal != null)
                        {
                            if (rawVal is JsonElement jeEnabled)
                            {
                                if (jeEnabled.ValueKind == JsonValueKind.True) requested = true;
                                else if (jeEnabled.ValueKind == JsonValueKind.False) requested = false;
                                else if (jeEnabled.ValueKind == JsonValueKind.String && bool.TryParse(jeEnabled.GetString(), out parsed)) requested = parsed;
                            }
                            else if (rawVal is bool)
                            {
                                requested = (bool)rawVal;
                            }
                            else if (bool.TryParse(rawVal.ToString(), out parsed))
                            {
                                requested = parsed;
                            }
                        }
                    }

                    bool actualState;
                    string actualMode;
                    if (!string.IsNullOrWhiteSpace(requestedMode))
                    {
                        string normalizedMode = requestedMode.Trim().ToLowerInvariant();
                        if (normalizedMode != "off" && normalizedMode != "afterburner" && normalizedMode != "radeon")
                        {
                            WriteJson(res, 400, "{\"error\":\"invalid_osd_mode\"}");
                            return;
                        }

                        if (!_controller.TrySetOsdMode(normalizedMode, out actualMode, out actualState))
                        {
                            WriteJson(res, 500, "{\"error\":\"osd_toggle_failed\"}");
                            return;
                        }
                    }
                    else
                    {
                        bool targetEnabled;
                        if (requested.HasValue)
                        {
                            targetEnabled = requested.Value;
                        }
                        else
                        {
                            bool? current = _controller.GetOsdEnabled();
                            targetEnabled = !(current ?? true);
                        }

                        if (!_controller.TrySetOsdEnabled(targetEnabled, out actualState))
                        {
                            WriteJson(res, 500, "{\"error\":\"osd_toggle_failed\"}");
                            return;
                        }
                        actualMode = _controller.GetOsdMode() ?? (actualState ? "afterburner" : "off");
                    }

                    WriteJson(res, 200, JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "osdMode", actualMode },
                        { "osdEnabled", actualState }
                    }, JsonOptions));
                    return;
                }

                if (method == "POST" && path == "/api/input")
                {
                    if (!AuthService.IsAuthEnabled(_config))
                    {
                        WriteJson(res, 403, "{\"error\":\"auth_required_for_remote_input\"}");
                        return;
                    }

                    string rawJson = ReadRawBody(req);
                    string inputErr;
                    if (!RemoteInputProcessor.ProcessJsonMessage(rawJson, _remoteInputSender, out inputErr))
                    {
                        WriteJson(res, 400, JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            { "error", inputErr ?? "invalid_input" }
                        }, JsonOptions));
                        return;
                    }

                    WriteJson(res, 200, "{\"status\":\"ok\"}");
                    return;
                }

                if (method == "GET" && !path.StartsWith("/api/") && TryServeWebUiAsset(rawPath, res))
                {
                    return;
                }

                WriteJson(res, 404, "{\"error\":\"not_found\"}");
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warn("Operation rejected for " + req.Url.AbsolutePath + ": " + ex.Message);
                try
                {
                    string escaped = (ex.Message ?? "operation_failed").Replace("\\", "\\\\").Replace("\"", "\\\"");
                    WriteJson(res, 409, "{\"error\":\"" + escaped + "\"}");
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error handling request " + req.Url.AbsolutePath, ex);
                try
                {
                    WriteJson(res, 500, "{\"error\":\"internal_error\"}");
                }
                catch
                {
                }
            }
        }

        private static string ReadRawBody(HttpListenerRequest req)
        {
            if (req == null || !req.HasEntityBody)
            {
                return null;
            }

            try
            {
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> ReadJsonBody(HttpListenerRequest req)
        {
            if (req == null || !req.HasEntityBody)
            {
                return null;
            }

            try
            {
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                {
                    string raw = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return null;
                    }
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(raw, JsonOptions);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                if (val is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return null;
                    return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
                }
                return val.ToString();
            }
            return null;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                if (val is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.True) return true;
                    if (je.ValueKind == JsonValueKind.False) return false;
                    if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out bool jb)) return jb;
                    return false;
                }
                if (val is bool) return (bool)val;
                bool parsed;
                if (bool.TryParse(val.ToString(), out parsed)) return parsed;
            }
            return false;
        }

        private static bool TryGetLong(Dictionary<string, object> dict, string key, out long result)
        {
            result = 0;
            object val;
            if (dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                if (val is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out result)) return true;
                    if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out result)) return true;
                    return false;
                }
                if (val is long) { result = (long)val; return true; }
                if (val is int) { result = (int)val; return true; }
                return long.TryParse(val.ToString(), out result);
            }
            return false;
        }

        private static readonly byte[] WebManifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            { "name", "Cerberus" },
            { "short_name", "Cerberus" },
            { "start_url", "/" },
            { "display", "standalone" },
            { "background_color", "#0b0f19" },
            { "theme_color", "#0b0f19" },
            {
                "icons", new[]
                {
                    new Dictionary<string, object>
                    {
                        { "src", "/apple-touch-icon.png" },
                        { "sizes", "180x180" },
                        { "type", "image/png" }
                    }
                }
            }
        }));

        private static readonly byte[] WebIconPngBytes = LoadWebIconPngBytes();

        private static byte[] LoadWebIconPngBytes()
        {
            byte[] embedded = ReadEmbeddedResourceBytes("WebUI/apple-touch-icon.png");
            if (embedded != null && embedded.Length > 0)
            {
                return embedded;
            }
            return BuildWebIconPng(180);
        }
        private static readonly object WebUiLock = new object();
        private static Dictionary<string, string> _webUiResourceMap;
        private static readonly Dictionary<string, byte[]> _cachedHtmlByKey = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, string> GetWebUiResourceMap()
        {
            if (_webUiResourceMap != null)
            {
                return _webUiResourceMap;
            }

            lock (WebUiLock)
            {
                if (_webUiResourceMap != null)
                {
                    return _webUiResourceMap;
                }

                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    string resName = names[i];
                    string normalized = resName.Replace('\\', '/');
                    if (normalized.StartsWith("WebUI/", StringComparison.OrdinalIgnoreCase))
                    {
                        string rel = normalized.Substring("WebUI/".Length);
                        map[rel] = resName;
                    }
                }
                _webUiResourceMap = map;
                return _webUiResourceMap;
            }
        }

        private static byte[] ReadEmbeddedResourceBytes(string resourceName)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (MemoryStream ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        private static byte[] GetWebUiHtmlBytes(string relativeHtmlPath)
        {
            string key = (relativeHtmlPath ?? "index.html").TrimStart('/');
            lock (WebUiLock)
            {
                byte[] cached;
                if (_cachedHtmlByKey.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }

            Dictionary<string, string> map = GetWebUiResourceMap();
            string resName;
            if (!map.TryGetValue(key, out resName))
            {
                return null;
            }

            byte[] raw = ReadEmbeddedResourceBytes(resName);
            if (raw == null || raw.Length == 0)
            {
                return null;
            }

            string html = Encoding.UTF8.GetString(raw);
            const string marker = "</head>";
            int idx = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                html = html.Insert(idx, "  <script>window.__PC_HOSTED__ = true;</script>\n");
            }

            byte[] injected = Encoding.UTF8.GetBytes(html);
            lock (WebUiLock)
            {
                _cachedHtmlByKey[key] = injected;
            }
            return injected;
        }

        private static bool TryServeWebUiAsset(string rawPath, HttpListenerResponse res)
        {
            string trimmed = (rawPath ?? "").TrimStart('/');
            if (string.IsNullOrEmpty(trimmed) || trimmed.Contains(".."))
            {
                return false;
            }

            Dictionary<string, string> map = GetWebUiResourceMap();
            string resName;

            if (map.TryGetValue(trimmed, out resName))
            {
                if (trimmed.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] htmlBytes = GetWebUiHtmlBytes(trimmed);
                    if (htmlBytes == null) return false;
                    WriteBytes(res, 200, "text/html; charset=utf-8", htmlBytes, "no-cache");
                    return true;
                }

                byte[] bytes = ReadEmbeddedResourceBytes(resName);
                if (bytes == null) return false;

                string contentType = GetMimeType(trimmed);
                string cacheControl = (trimmed.StartsWith("_expo/", StringComparison.OrdinalIgnoreCase) ||
                                       trimmed.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    ? "public, max-age=31536000, immutable"
                    : "public, max-age=86400";

                WriteBytes(res, 200, contentType, bytes, cacheControl);
                return true;
            }

            // Static route without extension (e.g. /shortcuts, /windows, /settings) -> <route>.html or index.html
            if (!Path.HasExtension(trimmed))
            {
                string routeHtml = trimmed.TrimEnd('/') + ".html";
                byte[] htmlBytes = GetWebUiHtmlBytes(routeHtml) ?? GetWebUiHtmlBytes("index.html");
                if (htmlBytes != null)
                {
                    WriteBytes(res, 200, "text/html; charset=utf-8", htmlBytes, "no-cache");
                    return true;
                }
            }

            return false;
        }

        private static string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js":
                case ".mjs": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".webmanifest": return "application/manifest+json; charset=utf-8";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                case ".ttf": return "font/ttf";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                default: return "application/octet-stream";
            }
        }

        private static byte[] BuildWebIconPng(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Brush bg = new SolidBrush(Color.FromArgb(255, 15, 23, 42)))
            using (Brush fg = new SolidBrush(Color.FromArgb(255, 56, 189, 248)))
            using (Pen framePen = new Pen(Color.FromArgb(255, 56, 189, 248), 10f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRectangle(bg, 0, 0, size, size);

                using (GraphicsPath frame = CreateRoundedRectPath(37, 49, 106, 66, 10))
                {
                    g.DrawPath(framePen, frame);
                }

                g.FillRectangle(fg, 85, 118, 10, 18);
                using (GraphicsPath standBase = CreateRoundedRectPath(62, 132, 56, 10, 5))
                {
                    g.FillPath(fg, standBase);
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        private static GraphicsPath CreateRoundedRectPath(float x, float y, float width, float height, float radius)
        {
            float d = radius * 2f;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static string ExtractBearerToken(string authHeader)
        {
            if (string.IsNullOrWhiteSpace(authHeader)) return "";
            const string prefix = "Bearer ";
            if (authHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authHeader.Substring(prefix.Length).Trim();
            }
            return "";
        }

        private async Task HandleRemoteInputWebSocketAsync(HttpListenerContext ctx)
        {
            HttpListenerWebSocketContext wsCtx = await ctx.AcceptWebSocketAsync(null).ConfigureAwait(false);
            WebSocket socket = wsCtx.WebSocket;
            byte[] buffer = new byte[4096];
            CancellationToken token = _cts != null ? _cts.Token : CancellationToken.None;

            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        ms.SetLength(0);
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                if (socket.State == WebSocketState.CloseReceived)
                                {
                                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
                                }
                                return;
                            }

                            if (ms.Length + result.Count > 16384)
                            {
                                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too big", CancellationToken.None).ConfigureAwait(false);
                                return;
                            }

                            if (result.Count > 0)
                            {
                                ms.Write(buffer, 0, result.Count);
                            }
                        }
                        while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Text && ms.Length > 0)
                        {
                            string message = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                            string ignoredError;
                            RemoteInputProcessor.ProcessJsonMessage(message, _remoteInputSender, out ignoredError);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            catch (Exception ex)
            {
                _logger.Warn("Remote input WebSocket closed with error: " + ex.Message);
            }
            finally
            {
                try
                {
                    socket.Dispose();
                }
                catch
                {
                }
            }
        }

        private static void WriteBytes(HttpListenerResponse res, int statusCode, string contentType, byte[] bytes, string cacheControl)
        {
            res.StatusCode = statusCode;
            res.ContentType = contentType;
            if (!string.IsNullOrEmpty(cacheControl))
            {
                res.Headers["Cache-Control"] = cacheControl;
            }
            res.ContentLength64 = bytes.Length;
            using (Stream output = res.OutputStream)
            {
                output.Write(bytes, 0, bytes.Length);
            }
        }

        private static void WriteJson(HttpListenerResponse res, int statusCode, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            res.StatusCode = statusCode;
            res.ContentType = "application/json; charset=utf-8";
            res.Headers["Cache-Control"] = "no-store";
            res.ContentLength64 = bytes.Length;
            using (Stream output = res.OutputStream)
            {
                output.Write(bytes, 0, bytes.Length);
            }
        }

        public void Dispose()
        {
            if (_controller != null)
            {
                _controller.ConfigReloaded -= OnConfigReloaded;
            }
            lock (_listenerLock)
            {
                _disposed = true;
                StopListenerLocked();
            }
        }
    }
}
