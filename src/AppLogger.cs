using System;
using System.IO;
using System.Text;
using System.Diagnostics;

namespace UnitronicsV350Simulator
{
    /// <summary>
    /// Global, thread-safe file logger with configurable verbosity levels and category filters.
    /// Automatically writes diagnostic entries to virtualplc_debug.log in the application directory.
    /// </summary>
    public static class AppLogger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;
        private static bool _initialized = false;

        // Logging configuration parameters (synchronized with SimulatorConfig)
        public static bool LoggingEnabled = true;
        public static LogVerbosity MinimumLevel = LogVerbosity.Info;
        public static bool LogPcomPackets = true;
        public static bool LogTouchEvents = true;

        public static string LogFilePath
        {
            get
            {
                EnsureInitialized();
                return _logFilePath;
            }
        }

        public static void Configure(SimulatorConfig cfg)
        {
            if (cfg == null) return;
            LoggingEnabled = cfg.LoggingEnabled;
            MinimumLevel = cfg.LoggingVerbosity;
            LogPcomPackets = cfg.LogPcomPackets;
            LogTouchEvents = cfg.LogTouchEvents;
            Info(string.Format("Logger configuration updated: Enabled={0}, Level={1}, PCOM={2}, Touch={3}",
                LoggingEnabled, MinimumLevel, LogPcomPackets, LogTouchEvents));
        }

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    _logFilePath = Path.Combine(baseDir, "virtualplc_debug.log");

                    // Rotate log file if it exceeds 15 MB
                    if (File.Exists(_logFilePath))
                    {
                        FileInfo fi = new FileInfo(_logFilePath);
                        if (fi.Length > 15 * 1024 * 1024)
                        {
                            string oldPath = _logFilePath + ".old";
                            if (File.Exists(oldPath)) File.Delete(oldPath);
                            File.Move(_logFilePath, oldPath);
                        }
                    }

                    _initialized = true;
                    WriteRaw("================================================================================");
                    WriteRaw(string.Format("=== Unitronics Vision PLC/HMI Simulator - Debug Session (v0.0.12) ==="));
                    WriteRaw(string.Format("=== Start Time: {0:yyyy-MM-dd HH:mm:ss.fff} | PID: {1} | OS: {2} | 64BitProc: {3} ===",
                        DateTime.Now, Process.GetCurrentProcess().Id, Environment.OSVersion, Environment.Is64BitProcess));
                    WriteRaw(string.Format("=== Executable Dir: {0} ===", baseDir));
                    WriteRaw("================================================================================");
                }
                catch
                {
                    _logFilePath = Path.Combine(Path.GetTempPath(), "virtualplc_debug.log");
                    _initialized = true;
                }
            }
        }

        private static void WriteRaw(string line)
        {
            if (!LoggingEnabled) return;
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Ignore disk I/O exceptions to avoid interrupting PLC scan execution
            }
        }

        public static void Log(string level, string message, LogVerbosity verbosity = LogVerbosity.Info)
        {
            EnsureInitialized();
            if (!LoggingEnabled) return;
            if (verbosity > MinimumLevel) return;

            lock (_lock)
            {
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1,-5}] [T:{2,2}] {3}",
                    DateTime.Now, level, System.Threading.Thread.CurrentThread.ManagedThreadId, message);
                WriteRaw(line);
            }
        }

        public static void Info(string message)
        {
            Log("INFO", message, LogVerbosity.Info);
        }

        public static void Warn(string message)
        {
            Log("WARN", message, LogVerbosity.Warn);
        }

        public static void Debug(string message)
        {
            Log("DEBUG", message, LogVerbosity.Debug);
        }

        public static void Trace(string message)
        {
            Log("TRACE", message, LogVerbosity.Trace);
        }

        public static void Pcom(string direction, string details)
        {
            if (!LogPcomPackets) return;
            Log("PCOM", direction + ": " + details, LogVerbosity.Debug);
        }

        public static void Touch(string details)
        {
            if (!LogTouchEvents) return;
            Log("TOUCH", details, LogVerbosity.Debug);
        }

        public static void Error(string context, Exception ex)
        {
            EnsureInitialized();
            if (!LoggingEnabled) return;
            lock (_lock)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] [T:{1,2}] {2}",
                    DateTime.Now, System.Threading.Thread.CurrentThread.ManagedThreadId, context);
                if (ex != null)
                {
                    sb.AppendLine();
                    sb.AppendFormat("    Exception: {0}: {1}", ex.GetType().FullName, ex.Message);
                    sb.AppendLine();
                    sb.AppendFormat("    StackTrace: {0}", ex.StackTrace);
                    if (ex.InnerException != null)
                    {
                        sb.AppendLine();
                        sb.AppendFormat("    InnerException: {0}: {1}", ex.InnerException.GetType().FullName, ex.InnerException.Message);
                    }
                }
                WriteRaw(sb.ToString());
            }
        }

        public static void ClearLog()
        {
            EnsureInitialized();
            lock (_lock)
            {
                try
                {
                    File.WriteAllText(_logFilePath,
                        string.Format("=== Log file cleared by user ({0:yyyy-MM-dd HH:mm:ss}) ==={1}", DateTime.Now, Environment.NewLine),
                        Encoding.UTF8);
                }
                catch { }
            }
        }

        public static void ClearLogFile()
        {
            ClearLog();
        }

        public static void OpenLogFile()
        {
            EnsureInitialized();
            try
            {
                if (File.Exists(_logFilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "notepad.exe",
                        Arguments = "\"" + _logFilePath + "\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Error("Unable to open log file in Notepad", ex);
            }
        }

        public static void OpenLogFileInNotepad()
        {
            OpenLogFile();
        }
    }
}
