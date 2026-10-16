using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace UnitronicsV350Simulator
{
    /// <summary>
    /// Bezpieczny wielowatkowy logger zapisu do pliku tekstowego 'virtualplc_debug.log'
    /// w katalogu, z ktorego aplikacja zostala uruchomiona.
    /// </summary>
    public static class AppLogger
    {
        private static readonly object _syncLock = new object();
        private static string _logFilePath;
        private static bool _initialized = false;

        public static string LogFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_logFilePath))
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    _logFilePath = Path.Combine(baseDir, "virtualplc_debug.log");
                }
                return _logFilePath;
            }
        }

        public static void Init(string version, string[] args)
        {
            lock (_syncLock)
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    // Sprawdz czy plik nie przekracza 15 MB - jesli tak, rotuj do .old.log
                    if (File.Exists(LogFilePath))
                    {
                        var fi = new FileInfo(LogFilePath);
                        if (fi.Length > 15 * 1024 * 1024)
                        {
                            string backupFile = Path.ChangeExtension(LogFilePath, ".old.log");
                            if (File.Exists(backupFile)) File.Delete(backupFile);
                            File.Move(LogFilePath, backupFile);
                        }
                    }

                    var sb = new StringBuilder();
                    sb.AppendLine("========================================================================================");
                    sb.AppendLine(string.Format(" VirtualPLC - Unitronics Vision V350 Simulator v{0} - Sesja Debug", version));
                    sb.AppendLine(string.Format(" Data startu:      {0:yyyy-MM-dd HH:mm:ss.fff}", DateTime.Now));
                    sb.AppendLine(string.Format(" Katalog startowy: {0}", AppDomain.CurrentDomain.BaseDirectory));
                    sb.AppendLine(string.Format(" Plik logu:        {0}", LogFilePath));
                    sb.AppendLine(string.Format(" Srodowisko:       .NET CLR {0} ({1}) | OS: {2}", 
                        Environment.Version, 
                        IntPtr.Size == 4 ? "32-bit x86" : "64-bit x64", 
                        Environment.OSVersion));
                    sb.AppendLine(string.Format(" Argumenty CLI:    {0}", (args != null && args.Length > 0) ? string.Join(" ", args) : "[brak]"));
                    sb.AppendLine("========================================================================================");

                    WriteRaw(sb.ToString());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("AppLogger Init error: " + ex.Message);
                }
            }
        }

        public static void Info(string message)
        {
            LogEntry("INFO", message);
        }

        public static void Debug(string message)
        {
            LogEntry("DEBUG", message);
        }

        public static void Warn(string message)
        {
            LogEntry("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            if (ex != null)
            {
                string fullMsg = string.Format("{0}\nWyjatek: {1}: {2}\nSlad stosu (Stack Trace):\n{3}", 
                    message, ex.GetType().FullName, ex.Message, ex.StackTrace);
                LogEntry("ERROR", fullMsg);
            }
            else
            {
                LogEntry("ERROR", message);
            }
        }

        public static void LogPcom(string direction, string data)
        {
            LogEntry("PCOM", string.Format("[{0}] {1}", direction, data));
        }

        public static void LogHmiTouch(int x, int y, string action)
        {
            LogEntry("TOUCH", string.Format("Koordynaty ({0}, {1}) -> {2}", x, y, action));
        }

        private static void LogEntry(string level, string message)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string line = string.Format("[{0}] [{1,-5}] [Watek {2:D2}] {3}", timeStr, level, threadId, message);

            lock (_syncLock)
            {
                WriteRaw(line + Environment.NewLine);
            }
        }

        private static void WriteRaw(string text)
        {
            try
            {
                using (var fs = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    sw.Write(text);
                    sw.Flush();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("AppLogger WriteRaw failed: " + ex.Message);
            }
        }

        public static void OpenLogFile()
        {
            try
            {
                if (!File.Exists(LogFilePath))
                {
                    // Jesli plik jeszcze nie istnieje, utworz go pustego z naglowkiem
                    WriteRaw(string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [INFO] Inicjalizacja pliku logu.\n", DateTime.Now));
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = LogFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Nie mozna otworzyc pliku logu:\n" + ex.Message + "\n\nSciezka: " + LogFilePath,
                    "Blad otwarcia logu", 
                    System.Windows.Forms.MessageBoxButtons.OK, 
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }
    }
}
