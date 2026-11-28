using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace RoomAgent
{
    internal static class Program
    {
        private static string ResolveDefaultConfigPath()
        {
            string portablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(portablePath))
            {
                return portablePath;
            }

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "CerberusService", "config.json");
        }

        private static AgentConfig LoadOrCreateConfig(string configPath, bool allowCreate)
        {
            if (allowCreate && !File.Exists(configPath))
            {
                AgentConfig created = new AgentConfig();
                created.ApplyDefaultsAndNormalize();
                created.ConfigPath = configPath;
                created.Save(configPath);
                return created;
            }

            if (!File.Exists(configPath))
            {
                AgentConfig fallback = new AgentConfig();
                fallback.ApplyDefaultsAndNormalize();
                return fallback;
            }

            return AgentConfig.Load(configPath);
        }

        private static int RunSetupCommand(
            bool configureMode,
            bool unconfigureMode,
            bool autostart,
            bool explicitConfig,
            string configPath,
            bool consoleMode)
        {
            AgentConfig config;
            try
            {
                config = LoadOrCreateConfig(configPath, configureMode && !explicitConfig);
            }
            catch
            {
                config = new AgentConfig();
                config.ApplyDefaultsAndNormalize();
            }

            string configDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? AppDomain.CurrentDomain.BaseDirectory;
            string logDir = Path.IsPathRooted(config.LogDirectory)
                ? config.LogDirectory
                : Path.Combine(configDir, config.LogDirectory);

            FileLogger logger = new FileLogger(logDir, consoleMode);
            try
            {
                if (unconfigureMode)
                {
                    StartupTaskManager.UnconfigureSystem(config.Port, logger);
                    return 0;
                }

                string exePath = Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;
                StartupTaskManager.ConfigureSystem(config.Port, autostart, exePath, configDir, logger);
                return 0;
            }
            catch (Exception ex)
            {
                logger.Error("Setup command failed", ex);
                if (consoleMode)
                {
                    Console.Error.WriteLine("Setup command failed: " + ex.Message);
                }
                return 1;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            bool consoleMode = false;
            bool explicitConfig = false;
            bool configureMode = false;
            bool unconfigureMode = false;
            bool autostart = false;
            bool demoSetupMode = false;
            string configPath = ResolveDefaultConfigPath();

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--console", StringComparison.OrdinalIgnoreCase))
                {
                    consoleMode = true;
                }
                else if (string.Equals(args[i], "--configure", StringComparison.OrdinalIgnoreCase))
                {
                    configureMode = true;
                }
                else if (string.Equals(args[i], "--unconfigure", StringComparison.OrdinalIgnoreCase))
                {
                    unconfigureMode = true;
                }
                else if (string.Equals(args[i], "--autostart", StringComparison.OrdinalIgnoreCase))
                {
                    autostart = true;
                }
                else if (string.Equals(args[i], "--demo-setup", StringComparison.OrdinalIgnoreCase))
                {
                    demoSetupMode = true;
                }
                else if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    configPath = args[++i];
                    explicitConfig = true;
                }
            }

            if (configureMode || unconfigureMode)
            {
                return RunSetupCommand(configureMode, unconfigureMode, autostart, explicitConfig, configPath, consoleMode);
            }

            if (demoSetupMode)
            {
                AgentConfig demoConfig = new AgentConfig();
                demoConfig.ApplyDefaultsAndNormalize();
                foreach (var profile in demoConfig.Profiles)
                {
                    if (string.Equals(profile.Id, "office", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.Name = "Desk";
                    }
                    profile.CloseApps = Array.Empty<string>();
                }
                FileLogger demoLogger = new FileLogger(Path.Combine(Path.GetTempPath(), "cerberus-demo-logs"), false);
                WindowsIdleMonitor demoIdle = new WindowsIdleMonitor();
                XboxReceiverControllerMonitor demoXbox = new XboxReceiverControllerMonitor(demoConfig.XboxReceiverDevicePrefixes);
                WindowsSystemActions demoActions = new WindowsSystemActions(demoConfig, demoLogger);
                using (RoomController demoController = new RoomController(demoConfig, demoActions, demoIdle, demoXbox, demoLogger))
                {
                    System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                    Application wpfApp = new Application
                    {
                        ShutdownMode = ShutdownMode.OnMainWindowClose,
                        ThemeMode = ThemeMode.Dark
                    };
                    SetupWindow window = new SetupWindow(demoConfig, demoController, demoLogger, "http://valkyrie-pc:8090");
                    wpfApp.MainWindow = window;
                    window.Show();
                    wpfApp.Run();
                }
                return 0;
            }

            bool createdNew;
            using (Mutex singleInstance = new Mutex(true, "Local\\CerberusServiceSingleInstanceMutex", out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                AgentConfig config;
                try
                {
                    config = LoadOrCreateConfig(configPath, !explicitConfig);
                }
                catch (Exception ex)
                {
                    if (consoleMode)
                    {
                        Console.Error.WriteLine("Failed to load config: " + ex.Message);
                    }
                    return 1;
                }

                string configDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.IsPathRooted(config.LogDirectory)
                    ? config.LogDirectory
                    : Path.Combine(configDir, config.LogDirectory);

                FileLogger logger = new FileLogger(logDir, consoleMode);
                logger.Info("CerberusService starting...");

                WindowsIdleMonitor idleMonitor = new WindowsIdleMonitor();
                XboxReceiverControllerMonitor controllerMonitor = new XboxReceiverControllerMonitor(config.XboxReceiverDevicePrefixes);
                WindowsSystemActions actions = new WindowsSystemActions(config, logger);
                Application wpfApp = null;
                Action requestShutdown = () =>
                {
                    if (wpfApp != null)
                    {
                        wpfApp.Dispatcher.BeginInvoke(new Action(() => wpfApp.Shutdown()));
                    }
                    else if (Application.Current != null)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
                    }
                };
                using (WindowsSystemMonitor systemMonitor = new WindowsSystemMonitor(logger, config))
                using (RoomController controller = new RoomController(config, actions, idleMonitor, controllerMonitor, logger))
                using (UpdateService updateService = new UpdateService(config, logger, requestShutdown))
                using (HttpServer server = new HttpServer(config, controller, logger, systemMonitor, updateService))
                using (TrayIconHost trayIcon = new TrayIconHost(config.Port, configPath, config, controller, logger, updateService))
                {
                    PowerModeChangedEventHandler powerHandler = delegate(object sender, PowerModeChangedEventArgs e)
                    {
                        if (e.Mode == PowerModes.Suspend)
                        {
                            controller.OnPowerSuspend();
                        }
                        else if (e.Mode == PowerModes.Resume)
                        {
                            controller.OnPowerResume();
                        }
                    };

                    SystemEvents.PowerModeChanged += powerHandler;

                    SessionEndingEventHandler sessionEndingHandler = delegate(object sender, SessionEndingEventArgs e)
                    {
                        controller.OnSessionEnding(e.Reason.ToString());
                    };

                    SystemEvents.SessionEnding += sessionEndingHandler;

                    CancellationTokenSource idleCts = new CancellationTokenSource();
                    Task.Run(async () =>
                    {
                        while (!idleCts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                await Task.Delay(TimeSpan.FromSeconds(15), idleCts.Token).ConfigureAwait(false);
                                await controller.CheckIdleAsync().ConfigureAwait(false);
                            }
                            catch (TaskCanceledException)
                            {
                                break;
                            }
                            catch (Exception ex)
                            {
                                logger.Error("Idle check loop error", ex);
                            }
                        }
                    });

                    int pollMs = config.ControllerPollIntervalMs > 0 ? config.ControllerPollIntervalMs : 1000;
                    if (config.GetControllerTriggerProfile() != null)
                    {
                        logger.Info("Xbox receiver controller trigger enabled (poll=" + pollMs + "ms).");
                    }
                    else
                    {
                        logger.Info("Xbox receiver controller trigger disabled.");
                    }
                    Task.Run(async () =>
                    {
                        while (!idleCts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                await Task.Delay(pollMs, idleCts.Token).ConfigureAwait(false);
                                await controller.CheckStreamingStateAsync().ConfigureAwait(false);
                                await controller.CheckControllerTriggerAsync().ConfigureAwait(false);
                            }
                            catch (TaskCanceledException)
                            {
                                break;
                            }
                            catch (InvalidOperationException ex)
                            {
                                logger.Warn("Controller trigger skipped: " + ex.Message);
                            }
                            catch (Exception ex)
                            {
                                logger.Error("Controller trigger loop error", ex);
                            }
                        }
                    });

                    try
                    {
                        wpfApp = new Application
                        {
                            ShutdownMode = ShutdownMode.OnExplicitShutdown,
                            ThemeMode = ThemeMode.System
                        };

                        server.Start();
                        Task.Run(async () =>
                        {
                            try
                            {
                                await controller.ReconcileStartupStateAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                logger.Error("Startup state reconciliation error", ex);
                            }
                        });
                        Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(TimeSpan.FromSeconds(10), idleCts.Token).ConfigureAwait(false);
                                if (config.CheckForUpdatesOnStartup)
                                {
                                    await updateService.CheckForUpdatesAsync(idleCts.Token).ConfigureAwait(false);
                                }
                                while (!idleCts.Token.IsCancellationRequested)
                                {
                                    await Task.Delay(TimeSpan.FromHours(6), idleCts.Token).ConfigureAwait(false);
                                    if (config.CheckForUpdatesOnStartup)
                                    {
                                        await updateService.CheckForUpdatesAsync(idleCts.Token).ConfigureAwait(false);
                                    }
                                }
                            }
                            catch (TaskCanceledException)
                            {
                            }
                            catch (Exception ex)
                            {
                                logger.Warn("Background update check error: " + ex.Message);
                            }
                        });
                        // WPF message loop services SystemEvents, NotifyIcon, and SetupWindow with ThemeMode.System
                        wpfApp.Run();
                    }
                    finally
                    {
                        idleCts.Cancel();
                        idleCts.Dispose();
                        SystemEvents.PowerModeChanged -= powerHandler;
                        SystemEvents.SessionEnding -= sessionEndingHandler;
                        logger.Info("CerberusService stopped.");
                    }
                }
            }

            return 0;
        }
    }
}
