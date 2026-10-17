using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UnitronicsV350Simulator
{
    public enum LogVerbosity
    {
        Error = 1,
        Warn = 2,
        Info = 3,
        Debug = 4,
        Trace = 5
    }

    /// <summary>
    /// Simulator configuration model persisted to virtualplc_config.json.
    /// Stores language preferences, logging settings, network port, and active hardware profile.
    /// </summary>
    public class SimulatorConfig
    {
        public static SimulatorConfig Current { get; set; }

        public AppLanguage Language { get; set; }
        public bool ShowConfigOnStartup { get; set; }
        public bool AutoRunOnProjectLoad { get; set; }
        public int PcomPort { get; set; }

        // Logging settings
        public bool LoggingEnabled { get; set; }
        public LogVerbosity LoggingVerbosity { get; set; }
        public bool LogPcomPackets { get; set; }
        public bool LogTouchEvents { get; set; }

        // Active PLC hardware profile
        public HardwareProfile Profile { get; set; }

        static SimulatorConfig()
        {
            Current = Load();
        }

        public SimulatorConfig()
        {
            Language = AppLanguage.English;
            ShowConfigOnStartup = true;
            AutoRunOnProjectLoad = true;
            PcomPort = 20256;

            LoggingEnabled = true;
            LoggingVerbosity = LogVerbosity.Info;
            LogPcomPackets = true;
            LogTouchEvents = true;

            Profile = HardwareProfile.CreateExpandedV350Profile();
        }

        public static string ConfigFilePath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "virtualplc_config.json");
            }
        }

        public static SimulatorConfig Load()
        {
            try
            {
                string path = ConfigFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    SimulatorConfig cfg = ser.Deserialize<SimulatorConfig>(json);
                    if (cfg != null)
                    {
                        if (cfg.Profile == null)
                            cfg.Profile = HardwareProfile.CreateExpandedV350Profile();
                        cfg.Profile.EnsureSlotsInitialized();
                        if (cfg.PcomPort <= 0 || cfg.PcomPort > 65535)
                            cfg.PcomPort = 20256;
                        if ((int)cfg.LoggingVerbosity < 1 || (int)cfg.LoggingVerbosity > 5)
                            cfg.LoggingVerbosity = LogVerbosity.Info;
                        Current = cfg;
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error loading simulator configuration from JSON", ex);
            }

            var defaultCfg = new SimulatorConfig();
            Current = defaultCfg;
            return defaultCfg;
        }

        public void Save()
        {
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(this);
                File.WriteAllText(ConfigFilePath, FormatJson(json), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error saving simulator configuration to JSON", ex);
            }
        }

        private static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inQuotes = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                }
                else if (!inQuotes)
                {
                    if (c == '{' || c == '[')
                    {
                        sb.Append(c);
                        sb.AppendLine();
                        indent++;
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (c == '}' || c == ']')
                    {
                        sb.AppendLine();
                        indent = Math.Max(0, indent - 1);
                        sb.Append(new string(' ', indent * 2));
                        sb.Append(c);
                    }
                    else if (c == ',')
                    {
                        sb.Append(c);
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (c == ':')
                    {
                        sb.Append(": ");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
