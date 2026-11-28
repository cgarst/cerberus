using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RoomAgent
{
    internal sealed class TrayIconHost : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private int _port;
        private readonly string _configPath;
        private readonly AgentConfig _config;
        private readonly RoomController _controller;
        private readonly ILogger _logger;
        private readonly UpdateService _updateService;
        private readonly SynchronizationContext _uiContext;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private ToolStripMenuItem _headerItem;
        private ToolStripMenuItem _updateItem;
        private readonly Icon _ownedIcon;
        private SetupWindow _setupWindow;
        private IntPtr _iconHandle;
        private string _lastNotifiedUpdateVersion;
        private bool _disposed;

        public TrayIconHost(int port, string configPath, AgentConfig config, RoomController controller, ILogger logger, UpdateService updateService = null)
        {
            _port = port;
            _configPath = configPath;
            _config = config;
            _controller = controller;
            _logger = logger;
            _updateService = updateService;
            _uiContext = SynchronizationContext.Current;

            _ownedIcon = CreateTrayIcon(out _iconHandle);
            _menu = BuildMenu();

            _notifyIcon = new NotifyIcon
            {
                Icon = _ownedIcon ?? SystemIcons.Application,
                Text = FormatPortLabel(_port),
                ContextMenuStrip = _menu,
                Visible = true
            };

            if (_controller != null)
            {
                _controller.ConfigReloaded += OnConfigReloaded;
            }

            if (_updateService != null)
            {
                _updateService.StateChanged += OnUpdateStateChanged;
                UpdateTrayUpdateItem(_updateService.GetSnapshot());
            }

            _notifyIcon.BalloonTipClicked += async delegate(object sender, EventArgs e)
            {
                if (_updateService == null) return;
                UpdateStateSnapshot snap = _updateService.GetSnapshot();
                if (snap.Status == UpdateStatus.UpdateAvailable && snap.AvailableUpdate != null)
                {
                    await PromptAndInstallUpdateAsync(snap);
                }
            };

            _notifyIcon.DoubleClick += delegate(object sender, EventArgs e)
            {
                OpenSetupUi();
            };
        }

        private static string FormatPortLabel(int port)
        {
            string label = string.Format("CerberusService (Port {0})", port);
            return label.Length > 63 ? label.Substring(0, 63) : label;
        }

        private void UpdatePortDisplay()
        {
            _port = _config != null ? _config.Port : _port;
            string label = FormatPortLabel(_port);
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = label;
            }
            if (_headerItem != null)
            {
                _headerItem.Text = label;
            }
        }

        private void OnConfigReloaded(object sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (_uiContext != null && SynchronizationContext.Current != _uiContext)
            {
                _uiContext.Post(delegate
                {
                    if (!_disposed)
                    {
                        UpdatePortDisplay();
                    }
                }, null);
            }
            else
            {
                UpdatePortDisplay();
            }
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            _headerItem = new ToolStripMenuItem(FormatPortLabel(_port));
            _headerItem.Enabled = false;
            menu.Items.Add(_headerItem);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem openSetupItem = new ToolStripMenuItem("Open Configuration UI");
            openSetupItem.Font = new Font(openSetupItem.Font, FontStyle.Bold);
            openSetupItem.Click += delegate(object sender, EventArgs e)
            {
                OpenSetupUi();
            };
            menu.Items.Add(openSetupItem);

            ToolStripMenuItem openConfigFolderItem = new ToolStripMenuItem("Open Config Folder");
            openConfigFolderItem.Click += delegate(object sender, EventArgs e)
            {
                string configDir = !string.IsNullOrWhiteSpace(_configPath)
                    ? Path.GetDirectoryName(Path.GetFullPath(_configPath))
                    : AppDomain.CurrentDomain.BaseDirectory;
                OpenPath(configDir, "config folder");
            };
            menu.Items.Add(openConfigFolderItem);

            ToolStripMenuItem reloadConfigItem = new ToolStripMenuItem("Reload Config");
            reloadConfigItem.Click += async delegate(object sender, EventArgs e)
            {
                await ReloadConfigFromDiskAsync();
            };
            menu.Items.Add(reloadConfigItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Checked = StartupTaskManager.IsEnabled();
            menu.Opening += delegate(object sender, CancelEventArgs e)
            {
                UpdatePortDisplay();
                startupItem.Checked = StartupTaskManager.IsEnabled();
            };
            startupItem.Click += delegate(object sender, EventArgs e)
            {
                bool targetState = !startupItem.Checked;
                try
                {
                    string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                    string workingDir = !string.IsNullOrWhiteSpace(_configPath)
                        ? Path.GetDirectoryName(Path.GetFullPath(_configPath))
                        : AppDomain.CurrentDomain.BaseDirectory;
                    StartupTaskManager.SetEnabled(targetState, exePath, workingDir, _logger);
                    startupItem.Checked = StartupTaskManager.IsEnabled();
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to toggle 'Start with Windows'", ex);
                    MessageBox.Show(
                        "Failed to update 'Start with Windows': " + ex.Message,
                        "CerberusService",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };
            menu.Items.Add(startupItem);

            if (_updateService != null)
            {
                _updateItem = new ToolStripMenuItem("Check for Updates...");
                _updateItem.Click += async delegate(object sender, EventArgs e)
                {
                    await HandleUpdateMenuClickAsync();
                };
                menu.Items.Add(_updateItem);
            }

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate(object sender, EventArgs e)
            {
                _logger.Info("Exit requested from system tray.");
                _notifyIcon.Visible = false;
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Shutdown();
                }
                else
                {
                    Application.Exit();
                }
            };
            menu.Items.Add(exitItem);

            return menu;
        }

        private void OnUpdateStateChanged(object sender, UpdateStateSnapshot snapshot)
        {
            if (_disposed || snapshot == null) return;

            if (_uiContext != null && SynchronizationContext.Current != _uiContext)
            {
                _uiContext.Post(delegate
                {
                    if (!_disposed)
                    {
                        ApplyUpdateStateToTray(snapshot);
                    }
                }, null);
            }
            else
            {
                ApplyUpdateStateToTray(snapshot);
            }
        }

        private void ApplyUpdateStateToTray(UpdateStateSnapshot snapshot)
        {
            UpdateTrayUpdateItem(snapshot);
            if (snapshot.Status == UpdateStatus.UpdateAvailable &&
                snapshot.AvailableUpdate != null &&
                !string.Equals(_lastNotifiedUpdateVersion, snapshot.AvailableUpdate.Version, StringComparison.OrdinalIgnoreCase))
            {
                _lastNotifiedUpdateVersion = snapshot.AvailableUpdate.Version;
                if (_notifyIcon != null)
                {
                    _notifyIcon.ShowBalloonTip(
                        5000,
                        "CerberusService Update Available",
                        string.Format("Version {0} is available. Click here or use the tray menu to update.", snapshot.AvailableUpdate.Version),
                        ToolTipIcon.Info);
                }
            }
        }

        private void UpdateTrayUpdateItem(UpdateStateSnapshot snapshot)
        {
            if (_updateItem == null || snapshot == null) return;

            switch (snapshot.Status)
            {
                case UpdateStatus.Checking:
                    _updateItem.Text = "Checking for Updates...";
                    _updateItem.Enabled = false;
                    break;
                case UpdateStatus.UpdateAvailable:
                    string ver = snapshot.AvailableUpdate != null ? snapshot.AvailableUpdate.Version : "";
                    _updateItem.Text = string.IsNullOrEmpty(ver)
                        ? "Install Available Update..."
                        : string.Format("Install Update (v{0})...", ver);
                    _updateItem.Enabled = true;
                    break;
                case UpdateStatus.Downloading:
                    _updateItem.Text = string.Format("Downloading Update ({0}%)...", snapshot.DownloadProgressPercent);
                    _updateItem.Enabled = false;
                    break;
                case UpdateStatus.Installing:
                    _updateItem.Text = "Installing Update...";
                    _updateItem.Enabled = false;
                    break;
                default:
                    _updateItem.Text = "Check for Updates...";
                    _updateItem.Enabled = true;
                    break;
            }
        }

        private async Task HandleUpdateMenuClickAsync()
        {
            if (_updateService == null) return;

            UpdateStateSnapshot current = _updateService.GetSnapshot();
            if (current.Status == UpdateStatus.UpdateAvailable && current.AvailableUpdate != null)
            {
                await PromptAndInstallUpdateAsync(current);
                return;
            }

            UpdateStateSnapshot result = await _updateService.CheckForUpdatesAsync();
            if (result.Status == UpdateStatus.UpdateAvailable && result.AvailableUpdate != null)
            {
                await PromptAndInstallUpdateAsync(result);
            }
            else if (result.Status == UpdateStatus.UpToDate)
            {
                MessageBox.Show(
                    string.Format("CerberusService is up to date (v{0}, channel: {1}).", result.CurrentVersion, result.EffectiveChannel),
                    "CerberusService",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else if (result.Status == UpdateStatus.Error)
            {
                MessageBox.Show(
                    "Failed to check for updates: " + (result.ErrorMessage ?? "Unknown error"),
                    "CerberusService",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else if (string.Equals(result.EffectiveChannel, "off", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Update checks are disabled in configuration (UpdateChannel = off).",
                    "CerberusService",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private async Task PromptAndInstallUpdateAsync(UpdateStateSnapshot snapshot)
        {
            if (_updateService == null || snapshot == null || snapshot.AvailableUpdate == null) return;

            DialogResult confirm = MessageBox.Show(
                string.Format(
                    "A new version of CerberusService is available:\n\nCurrent: {0}\nAvailable: {1} ({2})\n\nDownload and install now?",
                    snapshot.CurrentVersion,
                    snapshot.AvailableUpdate.Version,
                    snapshot.EffectiveChannel),
                "CerberusService Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (confirm != DialogResult.Yes) return;

            bool ok = await _updateService.DownloadAndInstallAsync();
            if (!ok)
            {
                UpdateStateSnapshot failed = _updateService.GetSnapshot();
                MessageBox.Show(
                    "Failed to install update: " + (failed.ErrorMessage ?? "Unknown error"),
                    "CerberusService",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OpenSetupUi()
        {
            try
            {
                if (_setupWindow == null)
                {
                    _setupWindow = new SetupWindow(_config, _controller, _logger, updateService: _updateService);
                    _setupWindow.Closed += delegate { _setupWindow = null; };
                    _setupWindow.Show();
                }
                else
                {
                    if (_setupWindow.WindowState == System.Windows.WindowState.Minimized)
                    {
                        _setupWindow.WindowState = System.Windows.WindowState.Normal;
                    }
                    _setupWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to open configuration UI", ex);
            }
        }

        private void OpenPath(string path, string label)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    _logger.Warn(string.Format("Cannot open {0}; path does not exist: {1}", label, fullPath));
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo(fullPath);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to open " + label + ": " + path, ex);
            }
        }

        private async Task ReloadConfigFromDiskAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
                {
                    _logger.Warn("Cannot reload config; file does not exist: " + (_configPath ?? "(null)"));
                    MessageBox.Show(
                        "Configuration file not found: " + (_configPath ?? ""),
                        "CerberusService",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string rawJson = File.ReadAllText(_configPath);
                AgentConfig loaded = AgentConfig.Load(_configPath);
                await _controller.ReloadConfigAsync(loaded, rawJson, save: false);
                UpdatePortDisplay();

                if (_setupWindow != null)
                {
                    _setupWindow.ReloadFromLive();
                }

                _logger.Info("Configuration reloaded from disk via tray menu: " + _configPath);
                if (_notifyIcon != null)
                {
                    _notifyIcon.ShowBalloonTip(2000, "CerberusService", "Configuration reloaded (Port " + _config.Port + ").", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to reload configuration from disk: " + _configPath, ex);
                MessageBox.Show(
                    "Failed to reload configuration: " + ex.Message,
                    "CerberusService",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        internal static System.Windows.Media.ImageSource CreateWindowIcon()
        {
            IntPtr iconHandle = IntPtr.Zero;
            try
            {
                using (Icon embedded = LoadEmbeddedIcon(64))
                {
                    if (embedded != null)
                    {
                        System.Windows.Media.Imaging.BitmapSource source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            embedded.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        return source;
                    }
                }

                using (Bitmap bmp = RenderIconBitmap(64))
                {
                    iconHandle = bmp.GetHicon();
                    System.Windows.Media.Imaging.BitmapSource source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        iconHandle,
                        System.Windows.Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (iconHandle != IntPtr.Zero)
                {
                    DestroyIcon(iconHandle);
                }
            }
        }

        private static Icon CreateTrayIcon(out IntPtr iconHandle)
        {
            iconHandle = IntPtr.Zero;
            try
            {
                Icon embedded = LoadEmbeddedIcon(32);
                if (embedded != null)
                {
                    return embedded;
                }

                using (Bitmap bmp = RenderIconBitmap(32))
                {
                    iconHandle = bmp.GetHicon();
                    return Icon.FromHandle(iconHandle);
                }
            }
            catch
            {
                return null;
            }
        }

        private static Icon LoadEmbeddedIcon(int size)
        {
            using (Stream stream = typeof(TrayIconHost).Assembly.GetManifestResourceStream("Cerberus.ico"))
            {
                if (stream == null)
                {
                    return null;
                }
                return new Icon(stream, new Size(size, size));
            }
        }

        private static Bitmap RenderIconBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                if (size != 32)
                {
                    float scale = size / 32f;
                    g.ScaleTransform(scale, scale);
                }

                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(19, 27, 44)))
                using (Pen borderPen = new Pen(Color.FromArgb(56, 189, 248), 2.5f))
                {
                    g.FillEllipse(bgBrush, 2, 2, 28, 28);
                    g.DrawEllipse(borderPen, 2, 2, 28, 28);
                }

                using (Pen screenPen = new Pen(Color.FromArgb(224, 242, 254), 2.2f))
                {
                    g.DrawRectangle(screenPen, 8, 9, 16, 11);
                    g.DrawLine(screenPen, 16, 20, 16, 24);
                    g.DrawLine(screenPen, 12, 24, 20, 24);
                }
            }
            return bmp;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            if (_controller != null)
            {
                _controller.ConfigReloaded -= OnConfigReloaded;
            }

            if (_updateService != null)
            {
                _updateService.StateChanged -= OnUpdateStateChanged;
            }

            if (_setupWindow != null)
            {
                _setupWindow.Close();
                _setupWindow = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            if (_menu != null)
            {
                _menu.Dispose();
            }

            if (_ownedIcon != null)
            {
                _ownedIcon.Dispose();
            }

            if (_iconHandle != IntPtr.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
        }
    }
}
