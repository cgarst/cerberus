using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace RoomAgent
{
    public partial class SetupWindow : Window
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly AgentConfig _liveConfig;
        private readonly RoomController _controller;
        private readonly ILogger _logger;
        private readonly string _demoDnsUrl;

        private AgentConfig _workingConfig;
        private List<NativeInterop.DisplayDeviceInfo> _displays = new List<NativeInterop.DisplayDeviceInfo>();
        private List<NativeInterop.AudioDeviceInfo> _audioDevices = new List<NativeInterop.AudioDeviceInfo>();
        private List<NativeInterop.DisplayModeInfo> _currentMonitorModes = new List<NativeInterop.DisplayModeInfo>();
        private bool _suppressEvents;

        private sealed class ComboItem
        {
            public string Value { get; private set; }
            public string Text { get; private set; }

            public ComboItem(string value, string text)
            {
                Value = value ?? "";
                Text = text ?? "";
            }

            public override string ToString()
            {
                return Text;
            }
        }

        public SetupWindow(AgentConfig config, RoomController controller, ILogger logger, string demoDnsUrl = null)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (controller == null) throw new ArgumentNullException("controller");

            _liveConfig = config;
            _controller = controller;
            _logger = logger;
            _demoDnsUrl = demoDnsUrl;

            InitializeComponent();
            Icon = TrayIconHost.CreateWindowIcon();
            double maxInitialHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24);
            MaxHeight = maxInitialHeight;
            Loaded += SetupWindow_Loaded;
            PopulateStaticCombos();
            ReloadFromLive();
        }

        private void SetupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Rect workArea = SystemParameters.WorkArea;
            double maxAllowed = Math.Max(MinHeight, workArea.Height - 24);
            double fittedHeight = Math.Min(ActualHeight, maxAllowed);
            SizeToContent = SizeToContent.Manual;
            ClearValue(MaxHeightProperty);
            Height = fittedHeight;
            UpdateLayout();

            if (ProfilesScrollViewer != null && ProfilesScrollViewer.ScrollableHeight > 0 && Height < maxAllowed)
            {
                Height = Math.Min(maxAllowed, Height + Math.Ceiling(ProfilesScrollViewer.ScrollableHeight));
            }

            if (Top < workArea.Top || Top + Height > workArea.Bottom)
            {
                Top = Math.Max(workArea.Top, workArea.Top + (workArea.Height - Height) / 2.0);
            }
        }

        private void PopulateStaticCombos()
        {
            ProfileHdrCombo.Items.Add(new ComboItem("null", "Leave Unchanged"));
            ProfileHdrCombo.Items.Add(new ComboItem("true", "Enable HDR"));
            ProfileHdrCombo.Items.Add(new ComboItem("false", "Disable HDR"));

            ProfileConsoleEnvironmentCombo.Items.Add(new ComboItem("off", "Off"));
            ProfileConsoleEnvironmentCombo.Items.Add(new ComboItem("virtual-desktop", "Virtual Desktop"));
            ProfileConsoleEnvironmentCombo.Items.Add(new ComboItem("xbox", "Xbox Mode"));
            ProfileConsoleEnvironmentCombo.Items.Add(new ComboItem("minimize-all", "Minimize All"));

            StreamingConsoleEnvironmentCombo.Items.Add(new ComboItem("", "Inherit (Unchanged)"));
            StreamingConsoleEnvironmentCombo.Items.Add(new ComboItem("off", "Off"));
            StreamingConsoleEnvironmentCombo.Items.Add(new ComboItem("virtual-desktop", "Virtual Desktop"));
            StreamingConsoleEnvironmentCombo.Items.Add(new ComboItem("xbox", "Xbox Mode"));
            StreamingConsoleEnvironmentCombo.Items.Add(new ComboItem("minimize-all", "Minimize All"));

            SteamCloseModeCombo.Items.Add(new ComboItem("exit-bigpicture", "Exit Big Picture"));
            SteamCloseModeCombo.Items.Add(new ComboItem("close-window", "Close Steam Window"));
            SteamCloseModeCombo.Items.Add(new ComboItem("none", "Do Nothing"));
        }

        internal void ReloadFromLive()
        {
            string selectedProfileId = GetSelectedProfile()?.Id;
            int selectedIndex = ProfilesList.SelectedIndex;
            string json = JsonSerializer.Serialize(_liveConfig, JsonOptions);
            _workingConfig = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions) ?? new AgentConfig();
            _workingConfig.ApplyDefaultsAndNormalize(json);
            RefreshDevicesAndRender(selectedProfileId, selectedIndex);
        }

        private void RefreshDevicesAndRender(string preferredProfileId = null, int fallbackIndex = -1)
        {
            try
            {
                if (preferredProfileId == null && fallbackIndex < 0)
                {
                    preferredProfileId = GetSelectedProfile()?.Id;
                    fallbackIndex = ProfilesList.SelectedIndex;
                }
                _displays = NativeInterop.GetAllDisplayInfos();
                _audioDevices = NativeInterop.GetAudioPlaybackDevices();
                RenderGlobalSettings();
                RenderProfilesList(FindProfileIndex(preferredProfileId, fallbackIndex));
                SetStatus("Loaded local devices and configuration.", false);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Failed to refresh devices in SetupWindow", ex);
                SetStatus("Error loading devices: " + ex.Message, true);
            }
        }

        private int FindProfileIndex(string preferredProfileId, int fallbackIndex)
        {
            if (_workingConfig.Profiles == null || _workingConfig.Profiles.Count == 0) return 0;
            if (!string.IsNullOrWhiteSpace(preferredProfileId))
            {
                for (int i = 0; i < _workingConfig.Profiles.Count; i++)
                {
                    if (string.Equals(_workingConfig.Profiles[i].Id, preferredProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
            if (fallbackIndex >= 0 && fallbackIndex < _workingConfig.Profiles.Count) return fallbackIndex;
            return 0;
        }

        private void RenderGlobalSettings()
        {
            _suppressEvents = true;
            try
            {
                PortBox.Text = _workingConfig.Port.ToString();
                RefreshControllerTriggerDropdown();
                WakeGuardBox.Text = _workingConfig.WakeGuardSeconds.ToString();
                IdleRevertBox.Text = _workingConfig.IdleRevertMinutes.ToString();
                DisplaySettleBox.Text = _workingConfig.DisplaySettleDelayMs.ToString();
                SteamPathBox.Text = _workingConfig.SteamPath ?? "";
                SelectComboValue(SteamCloseModeCombo, _workingConfig.SteamCloseMode ?? "exit-bigpicture");
                ShortcutsDirectoryBox.Text = _workingConfig.ShortcutsDirectory ?? "";
                IdleSleepChk.IsChecked = _workingConfig.IdleSleepAfterRevert;
                RunIntegrationsOnConsoleEnvironmentChk.IsChecked = _workingConfig.RunIntegrationsOnConsoleEnvironmentSwitch;
                SelectComboValue(StreamingConsoleEnvironmentCombo, _workingConfig.StreamingConsoleEnvironmentMode ?? "");
                StreamingAutoDetectChk.IsChecked = _workingConfig.StreamingAutoDetectEnabled;
                StreamingRevertConsoleEnvironmentChk.IsChecked = _workingConfig.StreamingRevertConsoleEnvironmentOnEnd;
                AuthPasswordBox.Password = "";
                UpdateAuthStatusDisplay();
                UpdateWebAccessDisplay();
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void UpdateWebAccessDisplay()
        {
            if (!string.IsNullOrWhiteSpace(_demoDnsUrl))
            {
                HeaderDnsText.Text = _demoDnsUrl;
                return;
            }

            int port = _workingConfig != null ? _workingConfig.Port : 8090;
            if (int.TryParse((PortBox.Text ?? "").Trim(), out int parsedPort) && parsedPort >= 1 && parsedPort <= 65535)
            {
                port = parsedPort;
            }

            WebAccessAddresses info = NetworkAddressHelper.GetWebAccessAddresses(port);
            HeaderDnsText.Text = info.PrimaryDnsUrl;
        }

        private void PortBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents) return;
            UpdateWebAccessDisplay();
        }

        private void UpdateAuthStatusDisplay()
        {
            bool enabled = AuthService.IsAuthEnabled(_workingConfig);
            AuthStatusText.Text = enabled
                ? "Status: Enabled (password required for new clients)"
                : "Status: Disabled (open LAN access)";
            ClearAuthBtn.IsEnabled = enabled;
            RevokeTokensBtn.IsEnabled = enabled;
        }

        private void ClearAuthBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_workingConfig == null) return;
            AuthService.ClearPassword(_workingConfig);
            AuthPasswordBox.Password = "";
            UpdateAuthStatusDisplay();
            SetStatus("Password protection cleared. Click Save to apply.", false);
        }

        private void RevokeTokensBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_workingConfig == null) return;
            AuthService.RotateTokenSecret(_workingConfig);
            SetStatus("Existing device tokens marked for revocation. Click Save to apply.", false);
        }

        private void OpenDnsBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenUrlInBrowser(HeaderDnsText.Text, "web address");
        }

        private void CopyDnsBtn_Click(object sender, RoutedEventArgs e)
        {
            CopyAddressToClipboard(HeaderDnsText.Text, "web address");
        }

        private void QrDnsBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowQrCodeDialog(HeaderDnsText.Text, "Web Address");
        }

        private void ShowQrCodeDialog(string url, string label)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                System.Windows.Media.Imaging.BitmapSource qrBitmap = QrCodeGenerator.CreateBitmapSource(url, pixelsPerModule: 8, quietZoneModules: 4);

                Window dialog = new Window
                {
                    Owner = this,
                    Title = label + " QR Code",
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Icon = Icon
                };

                StackPanel root = new StackPanel
                {
                    Margin = new Thickness(20),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                Border qrBorder = new Border
                {
                    Background = Brushes.White,
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                Image qrImage = new Image
                {
                    Source = qrBitmap,
                    Width = 240,
                    Height = 240,
                    Stretch = Stretch.Uniform
                };
                RenderOptions.SetBitmapScalingMode(qrImage, BitmapScalingMode.NearestNeighbor);
                qrBorder.Child = qrImage;
                root.Children.Add(qrBorder);

                TextBox urlBox = new TextBox
                {
                    Text = url,
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 0),
                    TextAlignment = TextAlignment.Center
                };
                root.Children.Add(urlBox);

                StackPanel buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 14, 0, 0)
                };

                Button copyBtn = new Button
                {
                    Content = "Copy URL",
                    Padding = new Thickness(14, 6, 14, 6),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                copyBtn.Click += delegate
                {
                    CopyAddressToClipboard(url, label);
                };

                Button closeBtn = new Button
                {
                    Content = "Close",
                    Padding = new Thickness(16, 6, 16, 6),
                    IsCancel = true,
                    IsDefault = true
                };
                closeBtn.Click += delegate
                {
                    dialog.Close();
                };

                buttons.Children.Add(copyBtn);
                buttons.Children.Add(closeBtn);
                root.Children.Add(buttons);

                dialog.Content = root;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                SetStatus("Failed to generate QR code for " + label + ": " + ex.Message, true);
            }
        }

        private void OpenUrlInBrowser(string url, string label)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Failed to open " + label + " in browser: " + url, ex);
                SetStatus("Failed to open " + label + ": " + ex.Message, true);
            }
        }

        private void CopyAddressToClipboard(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                Clipboard.SetText(text);
                SetStatus("Copied " + label + " to clipboard.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Failed to copy " + label + ": " + ex.Message, true);
            }
        }

        private void RefreshControllerTriggerDropdown(string overrideSelection = null)
        {
            string current = overrideSelection;
            if (string.IsNullOrEmpty(current))
            {
                current = GetSelectedComboValue(ControllerTriggerCombo);
            }
            if (string.IsNullOrEmpty(current))
            {
                current = _workingConfig.XboxReceiverTriggerEnabled
                    ? (_workingConfig.ControllerTriggerProfileId ?? "")
                    : AgentConfig.DisabledControllerTriggerId;
            }
            ControllerTriggerCombo.Items.Clear();
            ControllerTriggerCombo.Items.Add(new ComboItem(AgentConfig.DisabledControllerTriggerId, "Disabled"));
            for (int i = 0; i < _workingConfig.Profiles.Count; i++)
            {
                EnvironmentConfig p = _workingConfig.Profiles[i];
                ControllerTriggerCombo.Items.Add(new ComboItem(p.Id, string.Format("{0} ({1})", p.Name, p.Id)));
            }
            SelectComboValue(ControllerTriggerCombo, current);
        }

        private void RenderProfilesList(int selectIndex)
        {
            _suppressEvents = true;
            try
            {
                ProfilesList.Items.Clear();
                string activeId = _controller.ActiveProfileId ?? "";
                for (int i = 0; i < _workingConfig.Profiles.Count; i++)
                {
                    ProfilesList.Items.Add(FormatProfileListLabel(_workingConfig.Profiles[i], activeId));
                }
                if (ProfilesList.Items.Count > 0)
                {
                    if (selectIndex < 0 || selectIndex >= ProfilesList.Items.Count) selectIndex = 0;
                    ProfilesList.SelectedIndex = selectIndex;
                }
            }
            finally
            {
                _suppressEvents = false;
            }
            RenderSelectedProfile();
        }

        private void RefreshProfileListLabelsPreservingSelection()
        {
            int idx = ProfilesList.SelectedIndex;
            if (idx < 0 || idx >= _workingConfig.Profiles.Count) return;
            _suppressEvents = true;
            try
            {
                string activeId = _controller.ActiveProfileId ?? "";
                ProfilesList.Items[idx] = FormatProfileListLabel(_workingConfig.Profiles[idx], activeId);
                ProfilesList.SelectedIndex = idx;
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private static string FormatProfileListLabel(EnvironmentConfig p, string activeId)
        {
            string label = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name;
            if (p.IsDefault) label += " [Default]";
            if (!string.IsNullOrEmpty(activeId) && string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase))
            {
                label += " [Active]";
            }
            return label;
        }

        private EnvironmentConfig GetSelectedProfile()
        {
            int idx = ProfilesList.SelectedIndex;
            if (idx < 0 || idx >= _workingConfig.Profiles.Count) return null;
            return _workingConfig.Profiles[idx];
        }

        private void RenderSelectedProfile()
        {
            EnvironmentConfig p = GetSelectedProfile();
            _suppressEvents = true;
            try
            {
                bool hasProfile = p != null;
                ProfileNameBox.IsEnabled = hasProfile;
                ProfileMonitorCombo.IsEnabled = hasProfile;
                ProfileAudioCombo.IsEnabled = hasProfile;
                ProfileResolutionCombo.IsEnabled = hasProfile;
                ProfileRefreshCombo.IsEnabled = hasProfile;
                ProfileHdrCombo.IsEnabled = hasProfile;
                ProfileConsoleEnvironmentCombo.IsEnabled = hasProfile;
                ProfileCloseAppsList.IsEnabled = hasProfile;
                AddCloseAppBtn.IsEnabled = hasProfile;
                ProfileLaunchAppsList.IsEnabled = hasProfile;
                AddLaunchAppBtn.IsEnabled = hasProfile;
                DeleteProfileBtn.IsEnabled = hasProfile && _workingConfig.Profiles.Count > 1;
                SetDefaultBtn.IsEnabled = hasProfile;
                ActivateProfileBtn.IsEnabled = hasProfile;

                if (p == null)
                {
                    ProfileCloseAppsList.Items.Clear();
                    RemoveCloseAppBtn.IsEnabled = false;
                    ProfileLaunchAppsList.Items.Clear();
                    RemoveLaunchAppBtn.IsEnabled = false;
                    LaunchAppCloseWindowChk.IsEnabled = false;
                    LaunchAppCloseWindowChk.IsChecked = false;
                    return;
                }

                ProfileNameBox.Text = p.Name ?? "";

                PopulateDeviceCombo(ProfileMonitorCombo, p.Monitor, _displays.ConvertAll(d => d.FriendlyName));
                PopulateDeviceCombo(ProfileAudioCombo, p.Audio, _audioDevices.ConvertAll(a => a.Name));

                PopulateResolutionAndRefreshCombos(GetSelectedComboValue(ProfileMonitorCombo), p.Width, p.Height, p.RefreshRate, preserveSaved: true);

                string hdrVal = p.Hdr == null ? "null" : (p.Hdr.Value ? "true" : "false");
                SelectComboValue(ProfileHdrCombo, hdrVal);
                SelectComboValue(ProfileConsoleEnvironmentCombo, string.IsNullOrEmpty(p.ConsoleEnvironmentMode) ? "off" : p.ConsoleEnvironmentMode);

                PopulateStringListBox(ProfileCloseAppsList, p.CloseApps);
                RemoveCloseAppBtn.IsEnabled = ProfileCloseAppsList.SelectedItems.Count > 0;
                PopulateLaunchAppsList(AppLauncherIntegration.GetLaunchEntries(p));
                RemoveLaunchAppBtn.IsEnabled = ProfileLaunchAppsList.SelectedItems.Count > 0;
                UpdateLaunchAppCloseWindowCheckbox();
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private static void PopulateDeviceCombo(ComboBox combo, string currentVal, List<string> deviceNames)
        {
            combo.Items.Clear();
            combo.Items.Add(new ComboItem("", "-- None / Keep Current --"));
            string selectedValue = currentVal ?? "";
            bool foundExact = string.IsNullOrWhiteSpace(currentVal);
            string uniqueSubstringMatch = null;
            int substringMatchCount = 0;
            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < deviceNames.Count; i++)
            {
                string name = deviceNames[i];
                if (string.IsNullOrWhiteSpace(name) || !added.Add(name)) continue;
                combo.Items.Add(new ComboItem(name, name));
                if (!foundExact)
                {
                    if (string.Equals(name, currentVal, StringComparison.OrdinalIgnoreCase))
                    {
                        foundExact = true;
                    }
                    else if (name.IndexOf(currentVal, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        uniqueSubstringMatch = name;
                        substringMatchCount++;
                    }
                }
            }

            if (!foundExact && substringMatchCount == 1)
            {
                selectedValue = uniqueSubstringMatch;
            }
            else if (!foundExact && !string.IsNullOrWhiteSpace(currentVal))
            {
                combo.Items.Add(new ComboItem(currentVal, currentVal + " (custom/offline)"));
            }

            SelectComboValue(combo, selectedValue);
        }

        private void CommitCurrentProfileField(Action<EnvironmentConfig> update)
        {
            if (_suppressEvents) return;
            EnvironmentConfig p = GetSelectedProfile();
            if (p == null) return;
            update(p);
            RefreshProfileListLabelsPreservingSelection();
            RefreshControllerTriggerDropdown();
        }

        private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_suppressEvents) RenderSelectedProfile();
        }

        private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents) return;
            EnvironmentConfig p = GetSelectedProfile();
            if (p == null) return;

            string oldId = p.Id;
            p.Name = ProfileNameBox.Text;
            p.Id = GenerateUniqueProfileId(p);

            string triggerSelection = GetSelectedComboValue(ControllerTriggerCombo);
            if (!string.IsNullOrEmpty(oldId) &&
                string.Equals(_workingConfig.ControllerTriggerProfileId, oldId, StringComparison.OrdinalIgnoreCase))
            {
                _workingConfig.ControllerTriggerProfileId = p.Id;
            }
            if (!string.IsNullOrEmpty(oldId) &&
                string.Equals(triggerSelection, oldId, StringComparison.OrdinalIgnoreCase))
            {
                triggerSelection = p.Id;
            }

            RefreshProfileListLabelsPreservingSelection();
            RefreshControllerTriggerDropdown(triggerSelection);
        }

        private static readonly int[][] StandardResolutions = new int[][]
        {
            new[] { 3840, 2160 },
            new[] { 3440, 1440 },
            new[] { 2560, 1440 },
            new[] { 2560, 1080 },
            new[] { 1920, 1080 },
            new[] { 1280, 720 }
        };

        private static readonly int[] StandardRefreshRates = new int[] { 240, 165, 144, 120, 100, 75, 60 };

        private void PopulateResolutionAndRefreshCombos(string monitorName, int targetWidth, int targetHeight, int targetRefresh, bool preserveSaved)
        {
            _currentMonitorModes = NativeInterop.GetDisplayModes(monitorName);

            ProfileResolutionCombo.Items.Clear();
            ProfileResolutionCombo.Items.Add(new ComboItem("0x0", "-- Keep Current --"));

            HashSet<string> addedRes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_currentMonitorModes.Count > 0)
            {
                for (int i = 0; i < _currentMonitorModes.Count; i++)
                {
                    NativeInterop.DisplayModeInfo m = _currentMonitorModes[i];
                    string key = m.Width + "x" + m.Height;
                    if (addedRes.Add(key))
                    {
                        ProfileResolutionCombo.Items.Add(new ComboItem(key, m.Width + " x " + m.Height));
                    }
                }
            }
            else
            {
                for (int i = 0; i < StandardResolutions.Length; i++)
                {
                    int w = StandardResolutions[i][0];
                    int h = StandardResolutions[i][1];
                    string key = w + "x" + h;
                    if (addedRes.Add(key))
                    {
                        ProfileResolutionCombo.Items.Add(new ComboItem(key, w + " x " + h));
                    }
                }
            }

            string targetResKey = (targetWidth > 0 && targetHeight > 0) ? (targetWidth + "x" + targetHeight) : "0x0";
            if (targetWidth > 0 && targetHeight > 0 && !addedRes.Contains(targetResKey))
            {
                if (preserveSaved)
                {
                    ProfileResolutionCombo.Items.Add(new ComboItem(targetResKey, targetWidth + " x " + targetHeight + " (saved)"));
                }
                else
                {
                    targetResKey = ProfileResolutionCombo.Items.Count > 1
                        ? ((ComboItem)ProfileResolutionCombo.Items[1]).Value
                        : "0x0";
                }
            }

            SelectComboValue(ProfileResolutionCombo, targetResKey);
            PopulateRefreshComboForResolution(GetSelectedComboValue(ProfileResolutionCombo), targetRefresh, preserveSaved);
        }

        private void PopulateRefreshComboForResolution(string resolutionValue, int targetRefresh, bool preserveSaved)
        {
            ProfileRefreshCombo.Items.Clear();
            ProfileRefreshCombo.Items.Add(new ComboItem("0", "-- Keep Current --"));

            TryParseResolution(resolutionValue, out int selW, out int selH);

            List<int> rates = new List<int>();
            HashSet<int> seenRates = new HashSet<int>();
            if (_currentMonitorModes.Count > 0)
            {
                for (int i = 0; i < _currentMonitorModes.Count; i++)
                {
                    NativeInterop.DisplayModeInfo m = _currentMonitorModes[i];
                    if (selW > 0 && selH > 0 && (m.Width != selW || m.Height != selH)) continue;
                    if (seenRates.Add(m.RefreshRate))
                    {
                        rates.Add(m.RefreshRate);
                    }
                }
            }

            if (rates.Count == 0)
            {
                for (int i = 0; i < StandardRefreshRates.Length; i++)
                {
                    int r = StandardRefreshRates[i];
                    if (seenRates.Add(r))
                    {
                        rates.Add(r);
                    }
                }
            }

            for (int i = 0; i < rates.Count; i++)
            {
                int r = rates[i];
                ProfileRefreshCombo.Items.Add(new ComboItem(r.ToString(), r + " Hz"));
            }

            string targetRefreshKey = targetRefresh > 0 ? targetRefresh.ToString() : "0";
            if (targetRefresh > 0 && !seenRates.Contains(targetRefresh))
            {
                if (preserveSaved)
                {
                    ProfileRefreshCombo.Items.Add(new ComboItem(targetRefreshKey, targetRefresh + " Hz (saved)"));
                }
                else
                {
                    targetRefreshKey = rates.Count > 0 ? rates[0].ToString() : "0";
                }
            }

            SelectComboValue(ProfileRefreshCombo, targetRefreshKey);
        }

        private static bool TryParseResolution(string value, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Split('x');
            if (parts.Length != 2) return false;
            return int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height);
        }

        private void ProfileMonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            EnvironmentConfig p = GetSelectedProfile();
            if (p == null) return;

            string monitor = GetSelectedComboValue(ProfileMonitorCombo);
            _suppressEvents = true;
            try
            {
                PopulateResolutionAndRefreshCombos(monitor, p.Width, p.Height, p.RefreshRate, preserveSaved: false);
            }
            finally
            {
                _suppressEvents = false;
            }

            TryParseResolution(GetSelectedComboValue(ProfileResolutionCombo), out int w, out int h);
            int.TryParse(GetSelectedComboValue(ProfileRefreshCombo), out int hz);
            CommitCurrentProfileField(prof =>
            {
                prof.Monitor = monitor;
                prof.Width = w;
                prof.Height = h;
                prof.RefreshRate = hz;
            });
        }

        private void ProfileAudioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitCurrentProfileField(p => p.Audio = GetSelectedComboValue(ProfileAudioCombo));
        }

        private void ProfileResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            EnvironmentConfig p = GetSelectedProfile();
            if (p == null) return;

            string resVal = GetSelectedComboValue(ProfileResolutionCombo);
            TryParseResolution(resVal, out int w, out int h);

            _suppressEvents = true;
            try
            {
                PopulateRefreshComboForResolution(resVal, p.RefreshRate, preserveSaved: false);
            }
            finally
            {
                _suppressEvents = false;
            }

            int.TryParse(GetSelectedComboValue(ProfileRefreshCombo), out int hz);
            CommitCurrentProfileField(prof =>
            {
                prof.Width = w;
                prof.Height = h;
                prof.RefreshRate = hz;
            });
        }

        private void ProfileRefreshCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (int.TryParse(GetSelectedComboValue(ProfileRefreshCombo), out int val))
            {
                CommitCurrentProfileField(p => p.RefreshRate = val);
            }
        }

        private void ProfileHdrCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitCurrentProfileField(p =>
            {
                string v = GetSelectedComboValue(ProfileHdrCombo);
                p.Hdr = v == "null" ? (bool?)null : (v == "true");
            });
        }

        private void ProfileConsoleEnvironmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitCurrentProfileField(p => p.ConsoleEnvironmentMode = GetSelectedComboValue(ProfileConsoleEnvironmentCombo));
        }

        private static void PopulateStringListBox(ListBox listBox, string[] items)
        {
            listBox.Items.Clear();
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                string val = (items[i] ?? "").Trim();
                if (!string.IsNullOrEmpty(val))
                {
                    listBox.Items.Add(val);
                }
            }
        }

        private static string[] GetListBoxStrings(ListBox listBox)
        {
            List<string> items = new List<string>();
            for (int i = 0; i < listBox.Items.Count; i++)
            {
                string s = listBox.Items[i] != null ? listBox.Items[i].ToString().Trim() : "";
                if (!string.IsNullOrEmpty(s))
                {
                    items.Add(s);
                }
            }
            return items.ToArray();
        }

        private void ProfileCloseAppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveCloseAppBtn.IsEnabled = GetSelectedProfile() != null && ProfileCloseAppsList.SelectedItems.Count > 0;
        }

        private void ProfileCloseAppsList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && RemoveCloseAppBtn.IsEnabled)
            {
                RemoveSelectedListItems(ProfileCloseAppsList, RemoveCloseAppBtn, CommitCloseAppsFromList);
                e.Handled = true;
            }
        }

        private void AddCloseAppBtn_Click(object sender, RoutedEventArgs e)
        {
            EnvironmentConfig profile = GetSelectedProfile();
            if (profile == null) return;
            ShowAppPickerDialog(profile, isLaunchMode: false);
        }

        private void RemoveCloseAppBtn_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedListItems(ProfileCloseAppsList, RemoveCloseAppBtn, CommitCloseAppsFromList);
        }

        private void ProfileLaunchAppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveLaunchAppBtn.IsEnabled = GetSelectedProfile() != null && ProfileLaunchAppsList.SelectedItems.Count > 0;
            UpdateLaunchAppCloseWindowCheckbox();
        }

        private void LaunchAppCloseWindowChk_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            bool closeWhenReady = LaunchAppCloseWindowChk.IsChecked == true;
            bool changed = false;
            foreach (object item in ProfileLaunchAppsList.SelectedItems)
            {
                if (item is LaunchAppEntry entry && entry.CloseWindowWhenReady != closeWhenReady)
                {
                    entry.CloseWindowWhenReady = closeWhenReady;
                    changed = true;
                }
            }
            if (changed)
            {
                ProfileLaunchAppsList.Items.Refresh();
                CommitLaunchAppsFromList();
            }
        }

        private void UpdateLaunchAppCloseWindowCheckbox()
        {
            bool prevSuppress = _suppressEvents;
            _suppressEvents = true;
            try
            {
                int count = 0;
                int checkedCount = 0;
                foreach (object item in ProfileLaunchAppsList.SelectedItems)
                {
                    if (item is LaunchAppEntry entry)
                    {
                        count++;
                        if (entry.CloseWindowWhenReady) checkedCount++;
                    }
                }
                LaunchAppCloseWindowChk.IsEnabled = GetSelectedProfile() != null && count > 0;
                if (count == 0)
                {
                    LaunchAppCloseWindowChk.IsChecked = false;
                }
                else if (checkedCount == count)
                {
                    LaunchAppCloseWindowChk.IsChecked = true;
                }
                else if (checkedCount == 0)
                {
                    LaunchAppCloseWindowChk.IsChecked = false;
                }
                else
                {
                    LaunchAppCloseWindowChk.IsChecked = null;
                }
            }
            finally
            {
                _suppressEvents = prevSuppress;
            }
        }

        private void PopulateLaunchAppsList(LaunchAppEntry[] entries)
        {
            ProfileLaunchAppsList.Items.Clear();
            if (entries == null) return;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && !string.IsNullOrWhiteSpace(entries[i].Path))
                {
                    ProfileLaunchAppsList.Items.Add(new LaunchAppEntry
                    {
                        Path = entries[i].Path.Trim(),
                        CloseWindowWhenReady = entries[i].CloseWindowWhenReady,
                        WindowTitle = entries[i].WindowTitle
                    });
                }
            }
        }

        private void ProfileLaunchAppsList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && RemoveLaunchAppBtn.IsEnabled)
            {
                RemoveSelectedListItems(ProfileLaunchAppsList, RemoveLaunchAppBtn, CommitLaunchAppsFromList);
                e.Handled = true;
            }
        }

        private void AddLaunchAppBtn_Click(object sender, RoutedEventArgs e)
        {
            EnvironmentConfig profile = GetSelectedProfile();
            if (profile == null) return;
            ShowAppPickerDialog(profile, isLaunchMode: true);
        }

        private void RemoveLaunchAppBtn_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedListItems(ProfileLaunchAppsList, RemoveLaunchAppBtn, CommitLaunchAppsFromList);
        }

        private static void RemoveSelectedListItems(ListBox listBox, Button removeBtn, Action commitAction)
        {
            if (listBox.SelectedItems.Count == 0) return;

            List<object> toRemove = new List<object>();
            foreach (object item in listBox.SelectedItems)
            {
                toRemove.Add(item);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                listBox.Items.Remove(toRemove[i]);
            }
            commitAction();
            removeBtn.IsEnabled = listBox.SelectedItems.Count > 0;
        }

        private void CommitCloseAppsFromList()
        {
            CommitCurrentProfileField(p => p.CloseApps = GetListBoxStrings(ProfileCloseAppsList));
        }

        private void CommitLaunchAppsFromList()
        {
            List<LaunchAppEntry> entries = new List<LaunchAppEntry>();
            for (int i = 0; i < ProfileLaunchAppsList.Items.Count; i++)
            {
                if (ProfileLaunchAppsList.Items[i] is LaunchAppEntry entry && !string.IsNullOrWhiteSpace(entry.Path))
                {
                    entries.Add(entry);
                }
            }
            CommitCurrentProfileField(p => EnsureIntegrationDict(p, "appLauncher")["launch"] = AppLauncherIntegration.SerializeLaunchEntries(entries));
        }

        private static string GetPickerValueForApp(DiscoveredApp app, bool isLaunchMode)
        {
            if (app == null) return "";
            if (isLaunchMode && !string.IsNullOrWhiteSpace(app.ExecutablePath))
            {
                return app.ExecutablePath.Trim();
            }
            return AppDiscovery.NormalizeProcessName(app.ProcessName);
        }

        private static bool IsAppInSet(DiscoveredApp app, HashSet<string> existingValues, HashSet<string> existingProcs, bool isLaunchMode)
        {
            if (app == null) return false;
            string val = GetPickerValueForApp(app, isLaunchMode);
            if (!string.IsNullOrEmpty(val) && existingValues.Contains(val)) return true;
            string proc = AppDiscovery.NormalizeProcessName(app.ProcessName);
            return !string.IsNullOrEmpty(proc) && existingProcs.Contains(proc);
        }

        private static void AddAppsToList(ListBox targetList, IEnumerable<string> values, bool normalizeToProcessName, Action commitAction)
        {
            if (values == null) return;

            HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targetList.Items.Count; i++)
            {
                object item = targetList.Items[i];
                string rawVal = item is LaunchAppEntry le
                    ? (le.Path ?? "").Trim()
                    : (item != null ? item.ToString().Trim() : "");
                string key = normalizeToProcessName ? AppDiscovery.NormalizeProcessName(rawVal) : rawVal;
                if (!string.IsNullOrEmpty(key)) existing.Add(key);
            }

            bool addedAny = false;
            bool isLaunchList = !normalizeToProcessName;
            foreach (string raw in values)
            {
                string trimmed = (raw ?? "").Trim();
                string entry = normalizeToProcessName ? AppDiscovery.NormalizeProcessName(trimmed) : trimmed;
                if (string.IsNullOrEmpty(entry) || existing.Contains(entry)) continue;
                existing.Add(entry);
                targetList.Items.Add(isLaunchList ? (object)new LaunchAppEntry { Path = entry } : entry);
                addedAny = true;
            }

            if (addedAny)
            {
                commitAction();
            }
        }

        private sealed class CloseAppPickerItem
        {
            public DiscoveredApp App { get; private set; }
            public bool AlreadyAdded { get; private set; }

            public CloseAppPickerItem(DiscoveredApp app, bool alreadyAdded)
            {
                App = app;
                AlreadyAdded = alreadyAdded;
            }

            public override string ToString()
            {
                string label = string.Equals(App.DisplayName, App.ProcessName, StringComparison.OrdinalIgnoreCase)
                    ? string.Format("{0}  —  {1}", App.DisplayName, App.SourceLabel)
                    : string.Format("{0} ({1}.exe)  —  {2}", App.DisplayName, App.ProcessName, App.SourceLabel);
                if (!string.IsNullOrWhiteSpace(App.Detail) &&
                    !string.Equals(App.Detail, App.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(App.Detail, App.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    label += "  [" + App.Detail + "]";
                }
                if (AlreadyAdded)
                {
                    label += "  (Added)";
                }
                return label;
            }
        }

        private void ShowAppPickerDialog(EnvironmentConfig profile, bool isLaunchMode)
        {
            ListBox targetList = isLaunchMode ? ProfileLaunchAppsList : ProfileCloseAppsList;
            Action commitAction = isLaunchMode ? (Action)CommitLaunchAppsFromList : CommitCloseAppsFromList;
            string modeLabel = isLaunchMode ? "Launch Apps" : "Close Apps";

            HashSet<string> currentValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> currentProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targetList.Items.Count; i++)
            {
                object item = targetList.Items[i];
                string val = item is LaunchAppEntry le
                    ? (le.Path ?? "").Trim()
                    : (item != null ? item.ToString().Trim() : "");
                if (!string.IsNullOrEmpty(val))
                {
                    currentValues.Add(val);
                    string norm = AppDiscovery.NormalizeProcessName(val);
                    if (!string.IsNullOrEmpty(norm)) currentProcs.Add(norm);
                }
            }

            Window dialog = new Window
            {
                Title = "Add " + modeLabel + " — " + (profile.Name ?? "Profile"),
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 600,
                Height = 520,
                MinWidth = 480,
                MinHeight = 400,
                ResizeMode = ResizeMode.CanResize
            };

            Grid root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Search + Source filter + Refresh
            Grid filterBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox searchBox = new TextBox
            {
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Filter by app name, process name, or window title"
            };
            Grid.SetColumn(searchBox, 0);

            ComboBox sourceCombo = new ComboBox { Margin = new Thickness(0, 0, 8, 0) };
            sourceCombo.Items.Add(new ComboItem("All", "All Apps"));
            sourceCombo.Items.Add(new ComboItem("Running", "Running Only"));
            sourceCombo.Items.Add(new ComboItem("Installed", "Installed Only"));
            sourceCombo.SelectedIndex = 0;
            Grid.SetColumn(sourceCombo, 1);

            Button refreshAppsBtn = new Button
            {
                Content = "Refresh",
                Padding = new Thickness(12, 5, 12, 5)
            };
            Grid.SetColumn(refreshAppsBtn, 2);

            filterBar.Children.Add(searchBox);
            filterBar.Children.Add(sourceCombo);
            filterBar.Children.Add(refreshAppsBtn);
            Grid.SetRow(filterBar, 0);
            root.Children.Add(filterBar);

            // Row 1: App List
            ListBox appsList = new ListBox
            {
                SelectionMode = SelectionMode.Extended,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(appsList, 1);
            root.Children.Add(appsList);

            // Row 2: Discovery status
            TextBlock pickerStatus = new TextBlock
            {
                Opacity = 0.72,
                Margin = new Thickness(0, 0, 0, 10),
                Text = "Loading running apps..."
            };
            Grid.SetRow(pickerStatus, 2);
            root.Children.Add(pickerStatus);

            // Row 3: Custom entry (+ Browse... in Launch Apps mode)
            Grid customRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (isLaunchMode)
            {
                customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            TextBlock customLabel = new TextBlock
            {
                Text = isLaunchMode ? "Custom path/URI:" : "Custom process:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(customLabel, 0);

            TextBox customBox = new TextBox
            {
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = isLaunchMode
                    ? "Enter an executable path, command, or URI (e.g. C:\\App\\app.exe or steam://...)"
                    : "Enter a process name (e.g. Discord or Discord.exe)"
            };
            Grid.SetColumn(customBox, 1);

            Button addCustomBtn = new Button
            {
                Content = "Add Custom",
                Padding = new Thickness(12, 5, 12, 5),
                IsEnabled = false
            };
            Grid.SetColumn(addCustomBtn, 2);

            customRow.Children.Add(customLabel);
            customRow.Children.Add(customBox);
            customRow.Children.Add(addCustomBtn);

            Button browseBtn = null;
            if (isLaunchMode)
            {
                browseBtn = new Button
                {
                    Content = "Browse...",
                    Padding = new Thickness(12, 5, 12, 5),
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(browseBtn, 3);
                customRow.Children.Add(browseBtn);
            }

            Grid.SetRow(customRow, 3);
            root.Children.Add(customRow);

            // Row 4: Dialog buttons
            StackPanel actionButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Button addSelectedBtn = new Button
            {
                Content = "Add Selected",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = false
            };
            object accentStyle = TryFindResource("AccentButtonStyle");
            if (accentStyle is Style style)
            {
                addSelectedBtn.Style = style;
            }

            Button closeBtn = new Button
            {
                Content = "Close",
                Padding = new Thickness(16, 6, 16, 6),
                IsCancel = true
            };
            actionButtons.Children.Add(addSelectedBtn);
            actionButtons.Children.Add(closeBtn);
            Grid.SetRow(actionButtons, 4);
            root.Children.Add(actionButtons);

            List<DiscoveredApp> allApps = new List<DiscoveredApp>();
            bool isClosed = false;
            dialog.Closed += delegate { isClosed = true; };

            Action renderFilteredList = delegate
            {
                string sourceFilter = GetSelectedComboValue(sourceCombo);
                List<DiscoveredApp> filtered = AppDiscovery.FilterApps(allApps, searchBox.Text, sourceFilter);
                appsList.Items.Clear();
                for (int i = 0; i < filtered.Count; i++)
                {
                    DiscoveredApp app = filtered[i];
                    appsList.Items.Add(new CloseAppPickerItem(app, IsAppInSet(app, currentValues, currentProcs, isLaunchMode)));
                }
                addSelectedBtn.IsEnabled = appsList.SelectedItems.Count > 0;
            };

            Action loadAppsAsync = async delegate
            {
                refreshAppsBtn.IsEnabled = false;
                pickerStatus.Text = "Scanning running and installed apps...";
                try
                {
                    List<DiscoveredApp> running = AppDiscovery.GetRunningApps();
                    allApps = AppDiscovery.MergeApps(running, null);
                    renderFilteredList();
                    pickerStatus.Text = string.Format("Loaded {0} running apps; scanning installed apps...", running.Count);

                    List<DiscoveredApp> installed = await Task.Run(() => AppDiscovery.GetInstalledApps());
                    if (isClosed) return;

                    allApps = AppDiscovery.MergeApps(running, installed);
                    renderFilteredList();
                    pickerStatus.Text = string.Format("Showing {0} apps ({1} running, {2} installed).", allApps.Count, running.Count, installed.Count);
                }
                catch (Exception ex)
                {
                    if (!isClosed)
                    {
                        pickerStatus.Text = "Error scanning apps: " + ex.Message;
                    }
                }
                finally
                {
                    if (!isClosed)
                    {
                        refreshAppsBtn.IsEnabled = true;
                    }
                }
            };

            searchBox.TextChanged += delegate { renderFilteredList(); };
            sourceCombo.SelectionChanged += delegate { renderFilteredList(); };
            refreshAppsBtn.Click += delegate { loadAppsAsync(); };
            appsList.SelectionChanged += delegate { addSelectedBtn.IsEnabled = appsList.SelectedItems.Count > 0; };
            customBox.TextChanged += delegate
            {
                string text = (customBox.Text ?? "").Trim();
                addCustomBtn.IsEnabled = isLaunchMode
                    ? !string.IsNullOrEmpty(text)
                    : !string.IsNullOrEmpty(AppDiscovery.NormalizeProcessName(text));
            };

            Action<string> trackAdded = delegate(string val)
            {
                if (string.IsNullOrWhiteSpace(val)) return;
                currentValues.Add(val.Trim());
                string norm = AppDiscovery.NormalizeProcessName(val);
                if (!string.IsNullOrEmpty(norm)) currentProcs.Add(norm);
            };

            Action commitSelectedAndClose = delegate
            {
                List<string> selectedValues = new List<string>();
                foreach (object item in appsList.SelectedItems)
                {
                    if (item is CloseAppPickerItem pickerItem)
                    {
                        string val = GetPickerValueForApp(pickerItem.App, isLaunchMode);
                        if (!string.IsNullOrEmpty(val))
                        {
                            selectedValues.Add(val);
                        }
                    }
                }
                if (selectedValues.Count > 0)
                {
                    AddAppsToList(targetList, selectedValues, !isLaunchMode, commitAction);
                    dialog.Close();
                }
            };

            appsList.MouseDoubleClick += delegate
            {
                if (appsList.SelectedItem is CloseAppPickerItem pickerItem)
                {
                    string val = GetPickerValueForApp(pickerItem.App, isLaunchMode);
                    if (string.IsNullOrEmpty(val)) return;
                    AddAppsToList(targetList, new[] { val }, !isLaunchMode, commitAction);
                    trackAdded(val);
                    renderFilteredList();
                    pickerStatus.Text = "Added \"" + val + "\" to " + modeLabel + ".";
                }
            };

            addSelectedBtn.Click += delegate { commitSelectedAndClose(); };

            addCustomBtn.Click += delegate
            {
                string raw = (customBox.Text ?? "").Trim();
                string entry = isLaunchMode ? raw : AppDiscovery.NormalizeProcessName(raw);
                if (string.IsNullOrEmpty(entry)) return;
                AddAppsToList(targetList, new[] { entry }, !isLaunchMode, commitAction);
                trackAdded(entry);
                customBox.Text = "";
                renderFilteredList();
                pickerStatus.Text = "Added \"" + entry + "\" to " + modeLabel + ".";
            };

            if (browseBtn != null)
            {
                browseBtn.Click += delegate
                {
                    OpenFileDialog dlg = new OpenFileDialog
                    {
                        Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*"
                    };
                    if (dlg.ShowDialog(dialog) == true)
                    {
                        AddAppsToList(targetList, new[] { dlg.FileName }, false, commitAction);
                        trackAdded(dlg.FileName);
                        renderFilteredList();
                        pickerStatus.Text = "Added \"" + dlg.FileName + "\" to " + modeLabel + ".";
                    }
                };
            }

            customBox.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.Key == Key.Enter && addCustomBtn.IsEnabled)
                {
                    addCustomBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ke.Handled = true;
                }
            };

            closeBtn.Click += delegate { dialog.Close(); };

            dialog.Content = root;
            dialog.Loaded += delegate
            {
                loadAppsAsync();
                searchBox.Focus();
            };
            dialog.ShowDialog();
        }

        private void AddProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            int n = _workingConfig.Profiles.Count + 1;
            EnvironmentConfig created = new EnvironmentConfig
            {
                Name = "Profile " + n,
                IsDefault = false,
                Monitor = _displays.Count > 0 ? (_displays[0].FriendlyName ?? "") : "",
                Audio = _audioDevices.Count > 0 ? (_audioDevices[0].Name ?? "") : "",
                Width = 3840,
                Height = 2160,
                RefreshRate = 120,
                Hdr = true,
                ConsoleEnvironmentMode = "off"
            };
            created.Id = GenerateUniqueProfileId(created);
            _workingConfig.Profiles.Add(created);
            RefreshControllerTriggerDropdown();
            RenderProfilesList(_workingConfig.Profiles.Count - 1);
        }

        private void DeleteProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            int idx = ProfilesList.SelectedIndex;
            if (idx < 0 || _workingConfig.Profiles.Count <= 1)
            {
                SetStatus("At least one profile is required.", true);
                return;
            }

            _workingConfig.Profiles.RemoveAt(idx);
            bool hasDefault = false;
            for (int i = 0; i < _workingConfig.Profiles.Count; i++)
            {
                if (_workingConfig.Profiles[i].IsDefault)
                {
                    hasDefault = true;
                    break;
                }
            }
            if (!hasDefault && _workingConfig.Profiles.Count > 0)
            {
                _workingConfig.Profiles[0].IsDefault = true;
            }

            RefreshControllerTriggerDropdown();
            RenderProfilesList(Math.Max(0, idx - 1));
        }

        private void SetDefaultBtn_Click(object sender, RoutedEventArgs e)
        {
            EnvironmentConfig selected = GetSelectedProfile();
            if (selected == null) return;

            for (int i = 0; i < _workingConfig.Profiles.Count; i++)
            {
                _workingConfig.Profiles[i].IsDefault = false;
            }
            selected.IsDefault = true;
            RenderProfilesList(ProfilesList.SelectedIndex);
        }

        private async void ActivateProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            await ActivateSelectedProfileAsync();
        }

        private async Task ActivateSelectedProfileAsync()
        {
            EnvironmentConfig selected = GetSelectedProfile();
            if (selected == null || string.IsNullOrWhiteSpace(selected.Id)) return;

            if (_liveConfig.GetProfile(selected.Id) == null)
            {
                SetStatus("Save Config first before activating a newly added profile.", true);
                return;
            }

            ActivateProfileBtn.IsEnabled = false;
            try
            {
                SetStatus("Activating " + (selected.Name ?? selected.Id) + "...", false);
                await _controller.SwitchToProfileAsync(selected.Id, true, null, "native-ui");
                RenderProfilesList(ProfilesList.SelectedIndex);
                SetStatus("Activated " + (selected.Name ?? selected.Id) + ".", false);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Failed to activate profile from SetupWindow", ex);
                SetStatus("Activation failed: " + ex.Message, true);
            }
            finally
            {
                ActivateProfileBtn.IsEnabled = true;
            }
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshDevicesAndRender();
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            await SaveConfigAsync();
        }

        private async Task SaveConfigAsync()
        {
            SaveBtn.IsEnabled = false;
            try
            {
                if (!int.TryParse((PortBox.Text ?? "").Trim(), out int port) || port < 1 || port > 65535)
                {
                    SetStatus("Web Port must be a number between 1 and 65535.", true);
                    return;
                }
                _workingConfig.Port = port;
                string triggerProfileId = GetSelectedComboValue(ControllerTriggerCombo);
                _workingConfig.ControllerTriggerProfileId = string.IsNullOrEmpty(triggerProfileId)
                    ? AgentConfig.DisabledControllerTriggerId
                    : triggerProfileId;
                _workingConfig.XboxReceiverTriggerEnabled = !AgentConfig.IsDisabledControllerTriggerValue(_workingConfig.ControllerTriggerProfileId);
                if (TryParseIntInRange(WakeGuardBox.Text, 0, 3600, out int wakeGuard)) _workingConfig.WakeGuardSeconds = wakeGuard;
                if (TryParseIntInRange(IdleRevertBox.Text, 0, 1440, out int idleRevert)) _workingConfig.IdleRevertMinutes = idleRevert;
                if (TryParseIntInRange(DisplaySettleBox.Text, 0, 30000, out int settleMs)) _workingConfig.DisplaySettleDelayMs = settleMs;
                _workingConfig.SteamPath = SteamPathBox.Text.Trim();
                _workingConfig.SteamCloseMode = GetSelectedComboValue(SteamCloseModeCombo);
                _workingConfig.ShortcutsDirectory = ShortcutsDirectoryBox.Text.Trim();
                _workingConfig.IdleSleepAfterRevert = IdleSleepChk.IsChecked == true;
                _workingConfig.RunIntegrationsOnConsoleEnvironmentSwitch = RunIntegrationsOnConsoleEnvironmentChk.IsChecked == true;
                _workingConfig.StreamingConsoleEnvironmentMode = GetSelectedComboValue(StreamingConsoleEnvironmentCombo) ?? "";
                _workingConfig.StreamingAutoDetectEnabled = StreamingAutoDetectChk.IsChecked == true;
                _workingConfig.StreamingRevertConsoleEnvironmentOnEnd = StreamingRevertConsoleEnvironmentChk.IsChecked == true;

                string newPassword = AuthPasswordBox.Password;
                if (!string.IsNullOrEmpty(newPassword))
                {
                    AuthService.SetPassword(_workingConfig, newPassword);
                    AuthPasswordBox.Password = "";
                }

                string rawJson = JsonSerializer.Serialize(_workingConfig, JsonOptions);
                AgentConfig toApply = JsonSerializer.Deserialize<AgentConfig>(rawJson, JsonOptions);
                await _controller.ReloadConfigAsync(toApply, rawJson);

                ReloadFromLive();
                SetStatus("Configuration saved and applied.", false);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Failed to save configuration from SetupWindow", ex);
                SetStatus("Save failed: " + ex.Message, true);
            }
            finally
            {
                SaveBtn.IsEnabled = true;
            }
        }

        private void BrowseSteamBtn_Click(object sender, RoutedEventArgs e)
        {
            BrowseExeIntoBox(SteamPathBox);
        }

        private void BrowseShortcutsDirBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dlg = new OpenFolderDialog
            {
                Title = "Select Shortcut Folder"
            };
            if (!string.IsNullOrWhiteSpace(ShortcutsDirectoryBox.Text))
            {
                try
                {
                    string dir = Environment.ExpandEnvironmentVariables(ShortcutsDirectoryBox.Text.Trim());
                    if (Directory.Exists(dir))
                    {
                        dlg.InitialDirectory = dir;
                    }
                }
                catch { }
            }
            if (dlg.ShowDialog(this) == true)
            {
                ShortcutsDirectoryBox.Text = dlg.FolderName;
            }
        }

        private void ResetShortcutsDirBtn_Click(object sender, RoutedEventArgs e)
        {
            ShortcutsDirectoryBox.Text = "";
        }

        private void BrowseExeIntoBox(TextBox box)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*"
            };
            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                try
                {
                    string dir = Path.GetDirectoryName(box.Text);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        dlg.InitialDirectory = dir;
                    }
                }
                catch { }
            }
            if (dlg.ShowDialog(this) == true)
            {
                box.Text = dlg.FileName;
            }
        }

        private void SetStatus(string message, bool isError)
        {
            StatusLabel.Text = message;
            object brushKey = isError ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush";
            Brush resourceBrush = TryFindResource(brushKey) as Brush;
            if (resourceBrush != null)
            {
                StatusLabel.Foreground = resourceBrush;
            }
            else
            {
                StatusLabel.Foreground = isError ? Brushes.IndianRed : Brushes.SeaGreen;
            }
        }

        private static bool TryParseIntInRange(string text, int min, int max, out int value)
        {
            if (int.TryParse((text ?? "").Trim(), out value))
            {
                if (value < min) value = min;
                if (value > max) value = max;
                return true;
            }
            return false;
        }

        private static string GetSelectedComboValue(ComboBox combo)
        {
            ComboItem item = combo.SelectedItem as ComboItem;
            return item != null ? item.Value : "";
        }

        private static void SelectComboValue(ComboBox combo, string value)
        {
            string target = value ?? "";
            for (int i = 0; i < combo.Items.Count; i++)
            {
                ComboItem item = combo.Items[i] as ComboItem;
                if (item != null && string.Equals(item.Value, target, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private static Dictionary<string, object> EnsureIntegrationDict(EnvironmentConfig profile, string key)
        {
            if (profile.Integrations == null)
            {
                profile.Integrations = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            }
            Dictionary<string, object> dict;
            if (!profile.Integrations.TryGetValue(key, out dict) || dict == null)
            {
                dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                profile.Integrations[key] = dict;
            }
            return dict;
        }

        private static bool GetIntegrationBool(EnvironmentConfig profile, string section, string key, bool fallback)
        {
            if (profile.Integrations == null) return fallback;
            Dictionary<string, object> dict;
            object val;
            if (profile.Integrations.TryGetValue(section, out dict) && dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                if (val is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.True) return true;
                    if (je.ValueKind == JsonValueKind.False) return false;
                    if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out bool jb)) return jb;
                    return fallback;
                }
                if (val is bool) return (bool)val;
                if (bool.TryParse(val.ToString(), out bool b)) return b;
            }
            return fallback;
        }

        private static string[] GetIntegrationStringArray(EnvironmentConfig profile, string section, string key)
        {
            if (profile.Integrations == null) return new string[0];
            Dictionary<string, object> dict;
            object val;
            if (profile.Integrations.TryGetValue(section, out dict) && dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                if (val is JsonElement je && je.ValueKind == JsonValueKind.Array)
                {
                    List<string> list = new List<string>();
                    foreach (JsonElement elem in je.EnumerateArray())
                    {
                        string s = elem.ValueKind == JsonValueKind.String ? elem.GetString() : elem.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                    }
                    return list.ToArray();
                }
                object[] arr = val as object[];
                if (arr != null)
                {
                    List<string> list = new List<string>();
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i] != null)
                        {
                            string s = arr[i].ToString().Trim();
                            if (!string.IsNullOrEmpty(s)) list.Add(s);
                        }
                    }
                    return list.ToArray();
                }
                string[] strArr = val as string[];
                if (strArr != null) return strArr;
            }
            return new string[0];
        }

        private string GenerateUniqueProfileId(EnvironmentConfig profile)
        {
            int idx = _workingConfig.Profiles.IndexOf(profile);
            string baseSlug = AgentConfig.NormalizeProfileId(profile.Name);
            if (string.IsNullOrWhiteSpace(baseSlug))
            {
                baseSlug = "profile-" + (idx >= 0 ? idx + 1 : _workingConfig.Profiles.Count + 1);
            }

            string candidate = baseSlug;
            int suffix = 2;
            while (IsProfileIdInUse(candidate, profile))
            {
                candidate = baseSlug + "-" + suffix;
                suffix++;
            }
            return candidate;
        }

        private bool IsProfileIdInUse(string id, EnvironmentConfig excludeProfile)
        {
            for (int i = 0; i < _workingConfig.Profiles.Count; i++)
            {
                EnvironmentConfig other = _workingConfig.Profiles[i];
                if (other == null || object.ReferenceEquals(other, excludeProfile)) continue;
                if (string.Equals(other.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
