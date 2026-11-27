using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace RoomAgent
{
    internal static class StartupTaskManager
    {
        private const string TaskName = "CerberusService";
        private const string FirewallRuleName = "CerberusService HTTP";
        private static readonly string[] LegacyTaskNames = { "RoomAgent", "PCRemoteAgent" };
        private static readonly string[] LegacyFirewallRuleNames = { "RoomAgent HTTP", "PCRemoteAgent HTTP" };

        public static bool IsEnabled()
        {
            try
            {
                int exitCode;
                string output = RunProcess("schtasks.exe", "/Query /TN \"" + TaskName + "\"", out exitCode);
                return exitCode == 0 && !string.IsNullOrEmpty(output);
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enable, string exePath, string workingDir, ILogger logger)
        {
            if (enable)
            {
                RegisterTask(exePath, workingDir, logger);
            }
            else
            {
                UnregisterTask(TaskName, logger);
            }
        }

        public static void ConfigureSystem(int port, bool registerStartupTask, string exePath, string workingDir, ILogger logger)
        {
            foreach (string legacyTask in LegacyTaskNames)
            {
                UnregisterTask(legacyTask, null);
            }

            EnsureNetworkAccess(port, logger);

            if (registerStartupTask)
            {
                RegisterTask(exePath, workingDir, logger);
            }
        }

        public static void UnconfigureSystem(int port, ILogger logger)
        {
            foreach (string legacyTask in LegacyTaskNames)
            {
                UnregisterTask(legacyTask, null);
            }
            UnregisterTask(TaskName, logger);

            foreach (string legacyRule in LegacyFirewallRuleNames)
            {
                DeleteFirewallRule(legacyRule);
            }
            DeleteFirewallRule(FirewallRuleName);

            int exitCode;
            RunProcess("netsh.exe", "http delete urlacl url=http://+:" + port + "/", out exitCode);
            if (logger != null)
            {
                logger.Info("Removed firewall rule and URL ACL for port " + port + ".");
            }
        }

        public static void EnsureNetworkAccess(int port, ILogger logger)
        {
            if (port <= 0 || port > 65535)
            {
                return;
            }

            string currentUser = WindowsIdentity.GetCurrent().Name;
            string urlPrefix = "http://+:" + port + "/";
            int exitCode;

            RunProcess("netsh.exe", "http delete urlacl url=" + urlPrefix, out exitCode);
            string urlOutput = RunProcess("netsh.exe", "http add urlacl url=" + urlPrefix + " user=\"" + currentUser + "\"", out exitCode);
            if (exitCode != 0 && logger != null)
            {
                logger.Warn("netsh http add urlacl returned " + exitCode + ": " + urlOutput);
            }

            foreach (string legacyRule in LegacyFirewallRuleNames)
            {
                DeleteFirewallRule(legacyRule);
            }
            DeleteFirewallRule(FirewallRuleName);

            string fwArgs = string.Format(
                "advfirewall firewall add rule name=\"{0}\" dir=in action=allow protocol=TCP localport={1} profile=private",
                FirewallRuleName,
                port);
            string fwOutput = RunProcess("netsh.exe", fwArgs, out exitCode);
            if (exitCode != 0)
            {
                throw new InvalidOperationException("netsh advfirewall add rule failed (" + exitCode + "): " + fwOutput);
            }

            if (logger != null)
            {
                logger.Info("Configured URL ACL and Private firewall rule for TCP port " + port + ".");
            }
        }

        private static void DeleteFirewallRule(string ruleName)
        {
            int exitCode;
            RunProcess("netsh.exe", "advfirewall firewall delete rule name=\"" + ruleName + "\"", out exitCode);
        }

        private static void RegisterTask(string exePath, string workingDir, ILogger logger)
        {
            string currentUser = WindowsIdentity.GetCurrent().Name;
            string escapedUser = SecurityElement.Escape(currentUser);
            string escapedExe = SecurityElement.Escape(exePath);
            string escapedWorkDir = SecurityElement.Escape(workingDir);

            string taskXml = string.Join(Environment.NewLine, new[]
            {
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>",
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">",
                "  <RegistrationInfo>",
                "    <Description>Starts CerberusService at user logon with elevated privileges.</Description>",
                "  </RegistrationInfo>",
                "  <Triggers>",
                "    <LogonTrigger>",
                "      <Enabled>true</Enabled>",
                "      <UserId>" + escapedUser + "</UserId>",
                "    </LogonTrigger>",
                "  </Triggers>",
                "  <Principals>",
                "    <Principal id=\"Author\">",
                "      <UserId>" + escapedUser + "</UserId>",
                "      <LogonType>InteractiveToken</LogonType>",
                "      <RunLevel>HighestAvailable</RunLevel>",
                "    </Principal>",
                "  </Principals>",
                "  <Settings>",
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>",
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>",
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>",
                "    <AllowHardTerminate>true</AllowHardTerminate>",
                "    <StartWhenAvailable>true</StartWhenAvailable>",
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>",
                "    <AllowStartOnDemand>true</AllowStartOnDemand>",
                "    <Enabled>true</Enabled>",
                "    <Hidden>false</Hidden>",
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>",
                "    <Priority>7</Priority>",
                "    <RestartOnFailure>",
                "      <Interval>PT1M</Interval>",
                "      <Count>3</Count>",
                "    </RestartOnFailure>",
                "  </Settings>",
                "  <Actions Context=\"Author\">",
                "    <Exec>",
                "      <Command>" + escapedExe + "</Command>",
                "      <WorkingDirectory>" + escapedWorkDir + "</WorkingDirectory>",
                "    </Exec>",
                "  </Actions>",
                "</Task>"
            });

            string tempXml = Path.Combine(Path.GetTempPath(), "CerberusServiceTask_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(tempXml, taskXml, Encoding.Unicode);
                int exitCode;
                string output = RunProcess("schtasks.exe", "/Create /TN \"" + TaskName + "\" /XML \"" + tempXml + "\" /F", out exitCode);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException("schtasks /Create failed (" + exitCode + "): " + output);
                }
                if (logger != null)
                {
                    logger.Info("Enabled 'Start with Windows' scheduled task for " + currentUser + ".");
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempXml))
                    {
                        File.Delete(tempXml);
                    }
                }
                catch
                {
                }
            }
        }

        private static void UnregisterTask(string taskName, ILogger logger)
        {
            int queryCode;
            string queryOut = RunProcess("schtasks.exe", "/Query /TN \"" + taskName + "\"", out queryCode);
            if (queryCode != 0 || string.IsNullOrEmpty(queryOut))
            {
                return;
            }

            int exitCode;
            string output = RunProcess("schtasks.exe", "/Delete /TN \"" + taskName + "\" /F", out exitCode);
            if (exitCode != 0)
            {
                throw new InvalidOperationException("schtasks /Delete failed (" + exitCode + "): " + output);
            }
            if (logger != null)
            {
                logger.Info("Disabled scheduled task '" + taskName + "'.");
            }
        }

        private static string RunProcess(string fileName, string arguments, out int exitCode)
        {
            ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process proc = Process.Start(psi))
            {
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(10000);
                exitCode = proc.ExitCode;
                return (stdout + " " + stderr).Trim();
            }
        }
    }
}
