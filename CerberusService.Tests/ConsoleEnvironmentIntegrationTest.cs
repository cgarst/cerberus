using System;
using System.Threading;

namespace RoomAgent.Tests
{
    internal class ConsoleLogger : ILogger
    {
        public void Info(string message)
        {
            Console.WriteLine("[INFO] " + message);
        }

        public void Warn(string message)
        {
            Console.WriteLine("[WARN] " + message);
        }

        public void Error(string message, Exception ex = null)
        {
            Console.WriteLine("[ERROR] " + message + (ex != null ? " -> " + ex.Message : ""));
        }
    }

    internal static class ConsoleEnvironmentIntegrationTest
    {
        [STAThread]
        private static int Main()
        {
            ConsoleLogger logger = new ConsoleLogger();
            AgentConfig config = new AgentConfig();
            WindowsSystemActions actions = new WindowsSystemActions(config, logger);

            Console.WriteLine("Starting console environment integration test (no display/audio/Steam changes)...");
            try
            {
                actions.SwitchToFreshVirtualDesktop();
                actions.HideDesktopIcons();
                actions.HideTaskbar();

                Console.WriteLine("Console environment active. Reverting in 5 seconds...");
                Thread.Sleep(5000);
            }
            finally
            {
                Console.WriteLine("Reverting console environment...");
                actions.ShowTaskbar();
                actions.ShowDesktopIcons();
                actions.RevertVirtualDesktop();
            }

            Console.WriteLine("Integration test complete.");
            return 0;
        }
    }
}
