using System;
using System.IO;
using System.Text;

namespace RoomAgent
{
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception ex = null);
    }

    public class FileLogger : ILogger
    {
        private readonly string _logDir;
        private readonly bool _echoConsole;
        private readonly object _sync = new object();

        public FileLogger(string logDir, bool echoConsole)
        {
            _logDir = logDir;
            _echoConsole = echoConsole;
            Directory.CreateDirectory(_logDir);
        }

        public void Info(string message)
        {
            Write("INFO", message, null);
        }

        public void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        private void Write(string level, string message, Exception ex)
        {
            DateTime now = DateTime.Now;
            string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}", now, level, message);
            if (ex != null)
            {
                line = line + Environment.NewLine + ex.ToString();
            }

            lock (_sync)
            {
                string fileName = string.Format("cerberus-service-{0:yyyy-MM-dd}.log", now);
                string filePath = Path.Combine(_logDir, fileName);
                File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);

                if (_echoConsole)
                {
                    Console.WriteLine(line);
                }
            }
        }
    }
}
