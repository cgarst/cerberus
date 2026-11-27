using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RoomAgent
{
    public sealed class DiscoveredApp
    {
        public string ProcessName { get; set; }
        public string DisplayName { get; set; }
        public string ExecutablePath { get; set; }
        public bool IsRunning { get; set; }
        public bool IsInstalled { get; set; }
        public string Detail { get; set; }

        public string SourceLabel
        {
            get
            {
                if (IsRunning && IsInstalled) return "Running & Installed";
                if (IsRunning) return "Running";
                if (IsInstalled) return "Installed";
                return "Custom";
            }
        }
    }

    public static class AppDiscovery
    {
        private static readonly HashSet<string> IgnoredProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle",
            "System",
            "Registry",
            "Memory Compression",
            "Secure System",
            "CerberusService",
            "svchost",
            "conhost",
            "csrss",
            "smss",
            "wininit",
            "services",
            "lsass",
            "dwm",
            "fontdrvhost",
            "sihost",
            "ctfmon",
            "taskhostw",
            "RuntimeBroker",
            "dllhost",
            "SearchHost",
            "StartMenuExperienceHost",
            "ShellExperienceHost",
            "TextInputHost",
            "ApplicationFrameHost",
            "WmiPrvSE",
            "audiodg",
            "spoolsv"
        };

        private static readonly string[] SystemDirectories = new[]
        {
            @"C:\Windows\System32",
            @"C:\Windows\SysWOW64",
            @"C:\Windows\WinSxS",
            @"C:\Windows\SystemApps"
        };

        private static readonly string[] IgnoredInstallerTokens = new[]
        {
            "uninstall",
            "uninst",
            "unins0",
            "msiexec"
        };

        public static string NormalizeProcessName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "";
            }

            string trimmed = rawName.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "";
            }

            string withoutExt = Path.GetFileNameWithoutExtension(trimmed);
            return string.IsNullOrWhiteSpace(withoutExt) ? "" : withoutExt.Trim();
        }

        public static List<DiscoveredApp> GetRunningApps()
        {
            Dictionary<string, string> windowTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                IList<OpenWindowInfo> openWindows = NativeInterop.EnumerateOpenWindows(includeShellCloaked: true);
                if (openWindows != null)
                {
                    foreach (OpenWindowInfo win in openWindows)
                    {
                        string proc = NormalizeProcessName(win.ProcessName);
                        if (string.IsNullOrEmpty(proc) || IgnoredProcessNames.Contains(proc))
                        {
                            continue;
                        }
                        if (!windowTitles.ContainsKey(proc) && !string.IsNullOrWhiteSpace(win.Title))
                        {
                            windowTitles[proc] = win.Title.Trim();
                        }
                    }
                }
            }
            catch
            {
                // Best-effort window title enrichment.
            }

            Dictionary<string, DiscoveredApp> byProcess = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);
            Process[] processes = Process.GetProcesses();
            foreach (Process proc in processes)
            {
                try
                {
                    string procName = NormalizeProcessName(proc.ProcessName);
                    if (string.IsNullOrEmpty(procName) || IgnoredProcessNames.Contains(procName))
                    {
                        continue;
                    }

                    string windowTitle;
                    bool hasOpenWindow = windowTitles.TryGetValue(procName, out windowTitle);
                    if (!hasOpenWindow && proc.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                    {
                        hasOpenWindow = true;
                        windowTitle = proc.MainWindowTitle.Trim();
                    }

                    string exePath = null;
                    string fileDescription = null;
                    try
                    {
                        exePath = proc.MainModule != null ? proc.MainModule.FileName : null;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
                            if (fvi != null && !string.IsNullOrWhiteSpace(fvi.FileDescription))
                            {
                                fileDescription = fvi.FileDescription.Trim();
                            }
                        }
                    }
                    catch
                    {
                        // Protected/system processes throw when accessing MainModule.
                    }

                    if (!hasOpenWindow)
                    {
                        if (string.IsNullOrEmpty(exePath) || IsSystemBinaryPath(exePath))
                        {
                            continue;
                        }
                    }

                    string displayName = !string.IsNullOrWhiteSpace(fileDescription) ? fileDescription : procName;
                    DiscoveredApp existing;
                    if (!byProcess.TryGetValue(procName, out existing))
                    {
                        byProcess[procName] = new DiscoveredApp
                        {
                            ProcessName = procName,
                            DisplayName = displayName,
                            ExecutablePath = exePath ?? "",
                            IsRunning = true,
                            IsInstalled = false,
                            Detail = !string.IsNullOrWhiteSpace(windowTitle) ? windowTitle : procName + ".exe"
                        };
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(existing.ExecutablePath) && !string.IsNullOrEmpty(exePath))
                        {
                            existing.ExecutablePath = exePath;
                        }
                        if (string.Equals(existing.DisplayName, existing.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(fileDescription))
                        {
                            existing.DisplayName = fileDescription;
                        }
                        if (!string.IsNullOrWhiteSpace(windowTitle) &&
                            string.Equals(existing.Detail, existing.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            existing.Detail = windowTitle;
                        }
                    }
                }
                catch
                {
                    // Ignore transient process inspection failures.
                }
                finally
                {
                    proc.Dispose();
                }
            }

            List<DiscoveredApp> result = new List<DiscoveredApp>(byProcess.Values);
            result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static List<DiscoveredApp> GetInstalledApps()
        {
            Dictionary<string, DiscoveredApp> byProcess = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

            CollectStartMenuApps(byProcess);
            CollectUninstallRegistryApps(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", byProcess);
            CollectUninstallRegistryApps(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", byProcess);
            CollectUninstallRegistryApps(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", byProcess);
            CollectAppPathsRegistryApps(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", byProcess);
            CollectAppPathsRegistryApps(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", byProcess);

            List<DiscoveredApp> result = new List<DiscoveredApp>(byProcess.Values);
            result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static List<DiscoveredApp> MergeApps(IEnumerable<DiscoveredApp> running, IEnumerable<DiscoveredApp> installed)
        {
            Dictionary<string, DiscoveredApp> merged = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

            if (running != null)
            {
                foreach (DiscoveredApp app in running)
                {
                    if (app == null) continue;
                    string proc = NormalizeProcessName(app.ProcessName);
                    if (string.IsNullOrEmpty(proc)) continue;

                    merged[proc] = new DiscoveredApp
                    {
                        ProcessName = proc,
                        DisplayName = string.IsNullOrWhiteSpace(app.DisplayName) ? proc : app.DisplayName,
                        ExecutablePath = app.ExecutablePath ?? "",
                        IsRunning = true,
                        IsInstalled = app.IsInstalled,
                        Detail = app.Detail ?? (proc + ".exe")
                    };
                }
            }

            if (installed != null)
            {
                foreach (DiscoveredApp app in installed)
                {
                    if (app == null) continue;
                    string proc = NormalizeProcessName(app.ProcessName);
                    if (string.IsNullOrEmpty(proc)) continue;

                    DiscoveredApp existing;
                    if (merged.TryGetValue(proc, out existing))
                    {
                        existing.IsInstalled = true;
                        if (string.IsNullOrWhiteSpace(existing.ExecutablePath) && !string.IsNullOrWhiteSpace(app.ExecutablePath))
                        {
                            existing.ExecutablePath = app.ExecutablePath;
                        }
                        if (string.Equals(existing.DisplayName, existing.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(app.DisplayName))
                        {
                            existing.DisplayName = app.DisplayName;
                        }
                    }
                    else
                    {
                        merged[proc] = new DiscoveredApp
                        {
                            ProcessName = proc,
                            DisplayName = string.IsNullOrWhiteSpace(app.DisplayName) ? proc : app.DisplayName,
                            ExecutablePath = app.ExecutablePath ?? "",
                            IsRunning = false,
                            IsInstalled = true,
                            Detail = app.Detail ?? (proc + ".exe")
                        };
                    }
                }
            }

            List<DiscoveredApp> result = new List<DiscoveredApp>(merged.Values);
            result.Sort((a, b) =>
            {
                int runningCmp = b.IsRunning.CompareTo(a.IsRunning);
                if (runningCmp != 0) return runningCmp;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public static List<DiscoveredApp> FilterApps(IEnumerable<DiscoveredApp> apps, string query, string sourceFilter)
        {
            List<DiscoveredApp> filtered = new List<DiscoveredApp>();
            if (apps == null) return filtered;

            string q = (query ?? "").Trim();
            string mode = (sourceFilter ?? "All").Trim();

            foreach (DiscoveredApp app in apps)
            {
                if (app == null) continue;
                if (string.Equals(mode, "Running", StringComparison.OrdinalIgnoreCase) && !app.IsRunning) continue;
                if (string.Equals(mode, "Installed", StringComparison.OrdinalIgnoreCase) && !app.IsInstalled) continue;

                if (!string.IsNullOrEmpty(q))
                {
                    bool matchDisplay = !string.IsNullOrEmpty(app.DisplayName) &&
                                        app.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchProc = !string.IsNullOrEmpty(app.ProcessName) &&
                                     app.ProcessName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchDetail = !string.IsNullOrEmpty(app.Detail) &&
                                       app.Detail.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchDisplay && !matchProc && !matchDetail) continue;
                }

                filtered.Add(app);
            }

            return filtered;
        }

        private static bool IsSystemBinaryPath(string path)
        {
            for (int i = 0; i < SystemDirectories.Length; i++)
            {
                if (path.StartsWith(SystemDirectories[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsIgnoredInstallerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            for (int i = 0; i < IgnoredInstallerTokens.Length; i++)
            {
                if (name.IndexOf(IgnoredInstallerTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectStartMenuApps(Dictionary<string, DiscoveredApp> byProcess)
        {
            object shell = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                shell = Activator.CreateInstance(shellType);

                string[] roots = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
                };

                foreach (string root in roots)
                {
                    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

                    List<string> lnkFiles = new List<string>();
                    CollectLnkFilesSafe(root, lnkFiles);

                    foreach (string lnkPath in lnkFiles)
                    {
                        string shortcutName = Path.GetFileNameWithoutExtension(lnkPath);
                        if (IsIgnoredInstallerName(shortcutName)) continue;

                        object shortcut = null;
                        try
                        {
                            shortcut = shellType.InvokeMember(
                                "CreateShortcut",
                                System.Reflection.BindingFlags.InvokeMethod,
                                null,
                                shell,
                                new object[] { lnkPath });
                            if (shortcut == null) continue;

                            object targetObj = shortcut.GetType().InvokeMember(
                                "TargetPath",
                                System.Reflection.BindingFlags.GetProperty,
                                null,
                                shortcut,
                                null);
                            string targetPath = targetObj as string;
                            if (string.IsNullOrWhiteSpace(targetPath) ||
                                !targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                !File.Exists(targetPath))
                            {
                                continue;
                            }

                            string procName = NormalizeProcessName(targetPath);
                            if (string.IsNullOrEmpty(procName) ||
                                IgnoredProcessNames.Contains(procName) ||
                                IsIgnoredInstallerName(procName) ||
                                IsSystemBinaryPath(targetPath))
                            {
                                continue;
                            }

                            if (!byProcess.ContainsKey(procName))
                            {
                                byProcess[procName] = new DiscoveredApp
                                {
                                    ProcessName = procName,
                                    DisplayName = string.IsNullOrWhiteSpace(shortcutName) ? procName : shortcutName,
                                    ExecutablePath = targetPath,
                                    IsRunning = false,
                                    IsInstalled = true,
                                    Detail = procName + ".exe"
                                };
                            }
                        }
                        catch
                        {
                            // Ignore malformed shortcuts.
                        }
                        finally
                        {
                            if (shortcut != null && Marshal.IsComObject(shortcut))
                            {
                                Marshal.ReleaseComObject(shortcut);
                            }
                        }
                    }
                }
            }
            catch
            {
                // WScript.Shell unavailable or blocked.
            }
            finally
            {
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }

        private static void CollectLnkFilesSafe(string dir, List<string> results)
        {
            try
            {
                results.AddRange(Directory.GetFiles(dir, "*.lnk", SearchOption.TopDirectoryOnly));
                foreach (string subDir in Directory.GetDirectories(dir))
                {
                    CollectLnkFilesSafe(subDir, results);
                }
            }
            catch
            {
                // Skip inaccessible Start Menu subdirectories.
            }
        }

        private static void CollectUninstallRegistryApps(RegistryKey hive, string subKeyPath, Dictionary<string, DiscoveredApp> byProcess)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(subKeyPath))
                {
                    if (key == null) return;
                    foreach (string name in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey sub = key.OpenSubKey(name))
                            {
                                if (sub == null) continue;
                                object sysComp = sub.GetValue("SystemComponent");
                                if (sysComp is int && (int)sysComp == 1) continue;

                                string displayName = sub.GetValue("DisplayName") as string;
                                if (string.IsNullOrWhiteSpace(displayName) || IsIgnoredInstallerName(displayName)) continue;

                                string displayIcon = sub.GetValue("DisplayIcon") as string;
                                string exePath = ExtractExePathFromIconValue(displayIcon);
                                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath) || IsSystemBinaryPath(exePath))
                                {
                                    continue;
                                }

                                string procName = NormalizeProcessName(exePath);
                                if (string.IsNullOrEmpty(procName) ||
                                    IgnoredProcessNames.Contains(procName) ||
                                    IsIgnoredInstallerName(procName))
                                {
                                    continue;
                                }

                                if (!byProcess.ContainsKey(procName))
                                {
                                    byProcess[procName] = new DiscoveredApp
                                    {
                                        ProcessName = procName,
                                        DisplayName = displayName.Trim(),
                                        ExecutablePath = exePath,
                                        IsRunning = false,
                                        IsInstalled = true,
                                        Detail = procName + ".exe"
                                    };
                                }
                            }
                        }
                        catch
                        {
                            // Ignore unreadable uninstall entry.
                        }
                    }
                }
            }
            catch
            {
                // Ignore inaccessible registry hive.
            }
        }

        private static void CollectAppPathsRegistryApps(RegistryKey hive, string subKeyPath, Dictionary<string, DiscoveredApp> byProcess)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(subKeyPath))
                {
                    if (key == null) return;
                    foreach (string name in key.GetSubKeyNames())
                    {
                        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            using (RegistryKey sub = key.OpenSubKey(name))
                            {
                                if (sub == null) continue;
                                string exePath = ExtractExePathFromIconValue(sub.GetValue(null) as string);
                                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath) || IsSystemBinaryPath(exePath))
                                {
                                    continue;
                                }

                                string procName = NormalizeProcessName(exePath);
                                if (string.IsNullOrEmpty(procName) ||
                                    IgnoredProcessNames.Contains(procName) ||
                                    IsIgnoredInstallerName(procName))
                                {
                                    continue;
                                }

                                if (!byProcess.ContainsKey(procName))
                                {
                                    string displayName = procName;
                                    try
                                    {
                                        FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
                                        if (fvi != null && !string.IsNullOrWhiteSpace(fvi.FileDescription))
                                        {
                                            displayName = fvi.FileDescription.Trim();
                                        }
                                    }
                                    catch { }

                                    byProcess[procName] = new DiscoveredApp
                                    {
                                        ProcessName = procName,
                                        DisplayName = displayName,
                                        ExecutablePath = exePath,
                                        IsRunning = false,
                                        IsInstalled = true,
                                        Detail = procName + ".exe"
                                    };
                                }
                            }
                        }
                        catch
                        {
                            // Ignore unreadable App Paths entry.
                        }
                    }
                }
            }
            catch
            {
                // Ignore inaccessible registry hive.
            }
        }

        private static string ExtractExePathFromIconValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim();
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                int endQuote = s.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    s = s.Substring(1, endQuote - 1);
                }
            }
            else
            {
                int commaIdx = s.IndexOf(',');
                if (commaIdx > 0)
                {
                    s = s.Substring(0, commaIdx);
                }
            }

            s = s.Trim().Trim('"');
            return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s : null;
        }
    }
}
