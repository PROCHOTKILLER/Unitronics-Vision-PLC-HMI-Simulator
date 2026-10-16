using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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

    public class SimulatorConfig
    {
        public static string ConfigFilePath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "virtualplc_config.json");
            }
        }

        public AppLanguage Language { get; set; }
        public bool ShowConfigOnStartup { get; set; }
        public int PcomPort { get; set; }

        // Logowanie
        public bool LoggingEnabled { get; set; }
        public LogVerbosity LoggingVerbosity { get; set; }
        public bool LogPcomPackets { get; set; }
        public bool LogTouchEvents { get; set; }

        // Profil sprzętowy
        public HardwareProfile Profile { get; set; }
        public HardwareProfile HardwareProfile { get { return Profile; } set { Profile = value; } }

        public SimulatorConfig()
        {
            Language = AppLanguage.Polish;
            ShowConfigOnStartup = true;
            PcomPort = 20256;
            LoggingEnabled = true;
            LoggingVerbosity = LogVerbosity.Info;
            LogPcomPackets = true;
            LogTouchEvents = true;
            Profile = HardwareProfile.CreateKomora9Profile();
        }

        private static SimulatorConfig _current;
        public static SimulatorConfig Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Load();
                }
                return _current;
            }
            set
            {
                _current = value;
            }
        }

        public static SimulatorConfig Load()
        {
            var cfg = new SimulatorConfig();
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
                    cfg.ParseJson(json);
                }
                else
                {
                    // Zapisz domyślny plik
                    cfg.Save();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Błąd wczytywania konfiguracji virtualplc_config.json, użyto domyślnej", ex);
            }
            return cfg;
        }

        public void Save()
        {
            try
            {
                string json = ToJson();
                File.WriteAllText(ConfigFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Błąd zapisu pliku konfiguracji: " + ConfigFilePath, ex);
            }
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine(string.Format("  \"Language\": \"{0}\",", Language.ToString()));
            sb.AppendLine(string.Format("  \"ShowConfigOnStartup\": {0},", ShowConfigOnStartup.ToString().ToLower()));
            sb.AppendLine(string.Format("  \"PcomPort\": {0},", PcomPort));
            sb.AppendLine(string.Format("  \"LoggingEnabled\": {0},", LoggingEnabled.ToString().ToLower()));
            sb.AppendLine(string.Format("  \"LoggingVerbosity\": \"{0}\",", LoggingVerbosity.ToString()));
            sb.AppendLine(string.Format("  \"LogPcomPackets\": {0},", LogPcomPackets.ToString().ToLower()));
            sb.AppendLine(string.Format("  \"LogTouchEvents\": {0},", LogTouchEvents.ToString().ToLower()));
            sb.AppendLine("  \"HardwareProfile\": {");
            sb.AppendLine(string.Format("    \"SelectedModelName\": \"{0}\",", Profile.SelectedModelName));
            sb.AppendLine(string.Format("    \"SelectedSnapInName\": \"{0}\",", Profile.SelectedSnapInName));
            sb.AppendLine(string.Format("    \"EnableExpansionBus\": {0},", Profile.EnableExpansionBus.ToString().ToLower()));
            sb.AppendLine("    \"ExpansionSlots\": [");
            if (Profile.ExpansionSlots != null)
            {
                for (int i = 0; i < Profile.ExpansionSlots.Count; i++)
                {
                    var slot = Profile.ExpansionSlots[i];
                    string comma = (i < Profile.ExpansionSlots.Count - 1) ? "," : "";
                    sb.AppendLine(string.Format("      {{ \"SlotNumber\": {0}, \"ModuleName\": \"{1}\" }}{2}", slot.SlotNumber, slot.ModuleName, comma));
                }
            }
            sb.AppendLine("    ]");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public void ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            // Prosty, bezpieczny parser par klucz-wartość
            var lines = json.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inProfile = false;
            bool inSlots = false;

            if (Profile == null) Profile = new HardwareProfile();
            Profile.EnsureSlotsInitialized();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("\"HardwareProfile\""))
                {
                    inProfile = true;
                    continue;
                }
                if (line.StartsWith("\"ExpansionSlots\""))
                {
                    inSlots = true;
                    continue;
                }
                if (line.StartsWith("]"))
                {
                    inSlots = false;
                    continue;
                }
                if (line.StartsWith("}") && inProfile && !inSlots)
                {
                    inProfile = false;
                    continue;
                }

                if (inSlots)
                {
                    // Slot format: { "SlotNumber": 1, "ModuleName": "IO-DI8-RO8" }
                    int slotNum = ExtractInt(line, "SlotNumber");
                    string modName = ExtractString(line, "ModuleName");
                    if (slotNum >= 1 && slotNum <= 8 && !string.IsNullOrEmpty(modName))
                    {
                        Profile.ExpansionSlots[slotNum - 1].ModuleName = modName;
                    }
                    continue;
                }

                if (inProfile)
                {
                    if (line.Contains("\"SelectedModelName\""))
                    {
                        string val = ExtractString(line, "SelectedModelName");
                        if (!string.IsNullOrEmpty(val)) Profile.SelectedModelName = val;
                    }
                    else if (line.Contains("\"SelectedSnapInName\""))
                    {
                        string val = ExtractString(line, "SelectedSnapInName");
                        if (!string.IsNullOrEmpty(val)) Profile.SelectedSnapInName = val;
                    }
                    else if (line.Contains("\"EnableExpansionBus\""))
                    {
                        Profile.EnableExpansionBus = ExtractBool(line, "EnableExpansionBus", true);
                    }
                    continue;
                }

                // Sekcja główna
                if (line.Contains("\"Language\""))
                {
                    string l = ExtractString(line, "Language");
                    if (string.Equals(l, "English", StringComparison.OrdinalIgnoreCase) || string.Equals(l, "en", StringComparison.OrdinalIgnoreCase))
                    {
                        Language = AppLanguage.English;
                    }
                    else
                    {
                        Language = AppLanguage.Polish;
                    }
                }
                else if (line.Contains("\"ShowConfigOnStartup\""))
                {
                    ShowConfigOnStartup = ExtractBool(line, "ShowConfigOnStartup", true);
                }
                else if (line.Contains("\"PcomPort\""))
                {
                    PcomPort = ExtractInt(line, "PcomPort", 20256);
                }
                else if (line.Contains("\"LoggingEnabled\""))
                {
                    LoggingEnabled = ExtractBool(line, "LoggingEnabled", true);
                }
                else if (line.Contains("\"LoggingVerbosity\""))
                {
                    string v = ExtractString(line, "LoggingVerbosity");
                    try
                    {
                        LoggingVerbosity = (LogVerbosity)Enum.Parse(typeof(LogVerbosity), v, true);
                    }
                    catch
                    {
                        LoggingVerbosity = LogVerbosity.Info;
                    }
                }
                else if (line.Contains("\"LogPcomPackets\""))
                {
                    LogPcomPackets = ExtractBool(line, "LogPcomPackets", true);
                }
                else if (line.Contains("\"LogTouchEvents\""))
                {
                    LogTouchEvents = ExtractBool(line, "LogTouchEvents", true);
                }
            }
        }

        private static string ExtractString(string line, string key)
        {
            int idx = line.IndexOf("\"" + key + "\"");
            if (idx < 0) return null;
            int colon = line.IndexOf(':', idx);
            if (colon < 0) return null;
            int q1 = line.IndexOf('"', colon);
            if (q1 < 0) return null;
            int q2 = line.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return line.Substring(q1 + 1, q2 - q1 - 1).Trim();
        }

        private static bool ExtractBool(string line, string key, bool def)
        {
            int idx = line.IndexOf("\"" + key + "\"");
            if (idx < 0) return def;
            int colon = line.IndexOf(':', idx);
            if (colon < 0) return def;
            string val = line.Substring(colon + 1).Trim().TrimEnd(',', '}');
            bool res;
            if (bool.TryParse(val, out res)) return res;
            return def;
        }

        private static int ExtractInt(string line, string key, int def = 0)
        {
            int idx = line.IndexOf("\"" + key + "\"");
            if (idx < 0) return def;
            int colon = line.IndexOf(':', idx);
            if (colon < 0) return def;
            string val = line.Substring(colon + 1).Trim().TrimEnd(',', '}');
            int res;
            if (int.TryParse(val, out res)) return res;
            return def;
        }
    }
}
