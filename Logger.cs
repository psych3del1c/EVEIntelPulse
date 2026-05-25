using System;
using System.IO;

namespace EVEIntelPulse
{
    /// <summary>
    /// Simple file-and-console logger for application events.
    /// </summary>
    public static class Logger
    {
        private static string? _logFilePath;
        private static readonly object _lock = new();

        /// <summary>
        /// Initializes the logger. Must be called once at startup.
        /// </summary>
        /// <param name="logFilePath">Full path to the log file.</param>
        public static void Initialize(string logFilePath)
        {
            _logFilePath = logFilePath;
            var dir = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// Writes a timestamped message to both the log file and the console.
        /// </summary>
        /// <param name="message">Message to log.</param>
        public static void Write(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logMessage = $"[{timestamp}] {message}";
            Console.WriteLine(logMessage);

            if (string.IsNullOrEmpty(_logFilePath))
                return;

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
                }
                catch
                {
                    // Silently ignore logging failures to avoid crashing the app.
                }
            }
        }
    }
}