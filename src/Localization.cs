using System;
using System.Collections.Generic;

namespace UnitronicsV350Simulator
{
    public enum AppLanguage
    {
        English,
        Polish
    }

    public static class Localization
    {
        public static AppLanguage CurrentLanguage { get; private set; }

        public static event Action OnLanguageChanged;

        private static readonly Dictionary<string, string> enStrings = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> plStrings = new Dictionary<string, string>();

        static Localization()
        {
            CurrentLanguage = AppLanguage.Polish;
            InitStrings();
        }

        public static void SetLanguage(AppLanguage lang)
        {
            if (CurrentLanguage == lang) return;
            CurrentLanguage = lang;
            if (OnLanguageChanged != null)
            {
                OnLanguageChanged();
            }
        }

        public static string T(string key)
        {
            if (CurrentLanguage == AppLanguage.Polish)
            {
                string val;
                if (plStrings.TryGetValue(key, out val)) return val;
            }
            else
            {
                string val;
                if (enStrings.TryGetValue(key, out val)) return val;
            }

            // Fallback
            if (enStrings.ContainsKey(key)) return enStrings[key];
            if (plStrings.ContainsKey(key)) return plStrings[key];
            return key;
        }

        public static string T(string key, string fallback)
        {
            string val = T(key);
            return (val != null && val != key) ? val : fallback;
        }

        private static void Add(string key, string en, string pl)
        {
            enStrings[key] = en;
            plStrings[key] = pl;
        }

        private static void InitStrings()
        {
            // --- Główne okno / Main Window ---
            Add("AppTitle", "Unitronics Vision PLC/HMI Simulator v{0} [{1}]", "Symulator PLC/HMI Unitronics Vision v{0} [{1}]");
            Add("Menu_File", "File", "Plik");
            Add("Menu_LoadProject", "Load VisiLogic Project (*.vlp, *.vlx, *.udb)...", "Wgraj projekt VisiLogic (*.vlp, *.vlx, *.udb)...");
            Add("Menu_QuickLoadKomora9", "Quick Load Komora_9_PLC.vlp (Desktop)", "Wgraj Komora_9_PLC.vlp (szybki wybór z Pulpitu)");
            Add("Menu_ClearProject", "Clear Program (Factory State OPLC Standby)", "Wyczyść program (Czysty OPLC - stan fabryczny)");
            Add("Menu_Exit", "Exit", "Zakończ");

            Add("Menu_Plc", "PLC Control", "Sterowanie PLC");
            Add("Menu_Start", "START / RUN (F5)", "START / RUN (F5)");
            Add("Menu_Stop", "STOP (F6)", "STOP (F6)");
            Add("Menu_Reset", "RESET (F7)", "RESET (F7)");

            Add("Menu_Hardware", "Hardware", "Sprzęt");
            Add("Menu_HardwareConfig", "Hardware Configuration & Profile...", "Konfiguracja sprzętowa sterownika i I/O...");

            Add("Menu_Pcom", "PCOM TCP Server", "Serwer PCOM TCP");
            Add("Menu_PcomStart", "Start PCOM Server (:20256)", "Uruchom serwer PCOM (:20256)");
            Add("Menu_PcomStop", "Stop PCOM Server", "Zatrzymaj serwer PCOM");

            Add("Menu_Settings", "Settings", "Ustawienia");
            Add("Menu_SettingsGeneral", "Simulator Settings...", "Ustawienia symulatora...");

            Add("Menu_Language", "Language / Język", "Język / Language");
            Add("Menu_Lang_PL", "Polski (Polish)", "Polski (Polish)");
            Add("Menu_Lang_EN", "English (Angielski)", "English (Angielski)");

            Add("Menu_Help", "Help", "Pomoc");
            Add("Menu_OpenLog", "Open Debug Log (virtualplc_debug.log)...", "Otwórz plik logu debugera (virtualplc_debug.log)...");
            Add("Menu_Info", "OPLC System Info...", "Informacje systemowe OPLC...");
            Add("Menu_About", "About Simulator...", "O symulatorze OPLC...");

            // --- Pasek stanu / Status Bar ---
            Add("Status_State_STOP", "PLC: STOP (Standby)", "PLC: STOP (Czuwanie)");
            Add("Status_State_RUN", "PLC: RUN (Running)", "PLC: RUN (Praca)");
            Add("Status_State_PAUSE", "PLC: PAUSE", "PLC: PAUZA");
            Add("Status_ScanTime", "Scan: {0:F1} ms (Target {1} ms)", "Skan: {0:F1} ms (Cel {1} ms)");
            Add("Status_NoProject", "Project: [None - Factory Default]", "Projekt: [Brak - Stan fabryczny]");
            Add("Status_Project", "Project: {0} ({1})", "Projekt: {0} ({1})");
            Add("Status_PcomListening", "PCOM: TCP :{0} (Listening)", "PCOM: TCP :{0} (Nasłuch)");
            Add("Status_PcomStopped", "PCOM: Stopped", "PCOM: Zatrzymany");
            Add("Status_Page", "Display: #{0} '{1}'", "Ekran: #{0} '{1}'");
            Add("Status_HardwareProfile", "Hardware: {0}", "Sprzęt: {0}");

            // --- Panel czołowy / Front Panel ---
            Add("Front_Header", "UNITRONICS VISION VIRTUAL OPLC", "UNITRONICS VISION VIRTUAL OPLC");
            Add("Front_Keys_F1_F8", "FUNCTION KEYS (F1 - F8)", "KLAWISZE FUNKCYJNE (F1 - F8)");
            Add("Front_Keys_Nav", "SYSTEM & NAVIGATION KEYS", "KLAWISZE SYSTEMOWE I NAWIGACJI");
            Add("Front_Key_ESC", "ESC", "ESC");
            Add("Front_Key_ENTER", "ENTER", "ENTER");
            Add("Front_Key_INFO", "<i> INFO", "<i> INFO");

            // --- Zakładki główne / Main Tabs ---
            Add("Tab_Hardware", "Hardware I/O & Panels", "Sprzęt i Wejścia/Wyjścia I/O");
            Add("Tab_Watch", "Operand Watch Table", "Tabela Zmiennych (Watch)");
            Add("Tab_Subroutines", "Ladder Logic Subroutines", "Podprogramy Drabinkowe (Ladder)");
            Add("Tab_HmiPages", "HMI Displays List", "Ekrany Graficzne HMI");
            Add("Tab_Diagnostics", "Diagnostics & Logs", "Diagnostyka i Logi");

            // --- Panel sprzętowy / Hardware Panel ---
            Add("Hw_Tab_DI", "Digital Inputs (DI)", "Wejścia Cyfrowe (DI)");
            Add("Hw_Tab_DO", "Digital Outputs (DO / Relays)", "Wyjścia Cyfrowe (DO / Przekaźniki)");
            Add("Hw_Tab_AI", "Analog Inputs (AI)", "Wejścia Analogowe (AI)");
            Add("Hw_Tab_AO", "Analog Outputs (AO)", "Wyjścia Analogowe (AO)");
            Add("Hw_Grp_CpuInputs", "CPU Onboard Inputs ({0})", "Wejścia wbudowane CPU ({0})");
            Add("Hw_Grp_CpuOutputs", "CPU Onboard Outputs ({0})", "Wyjścia wbudowane CPU ({0})");
            Add("Hw_Grp_ExtSlot", "EX-A2X Bus: Slot {0} - {1}", "Szyna EX-A2X: Slot {0} - {1}");
            Add("Hw_Btn_Pulse", "Pulse", "Impuls");
            Add("Hw_Label_Potentiometer", "Potentiometer {0}:", "Potencjometr {0}:");
            Add("Hw_Label_RawVal", "Raw value (0..1023 / 0..4095):", "Wartość surowa (0..1023 / 0..4095):");
            Add("Hw_Label_Volts", "Volts / mA:", "Napięcie / Prąd:");
            Add("Hw_Label_Relay", "RELAY", "PRZEKAŹNIK");
            Add("Hw_Label_Transistor", "TRANSISTOR", "TRANZYSTOR");

            // --- Okno Konfiguracji Sprzętowej / Hardware Config Dialog ---
            Add("HwCfg_Title", "Hardware Configuration - Unitronics Vision Series", "Konfiguracja Sprzętowa - Unitronics Vision");
            Add("HwCfg_BannerTitle", "HARDWARE PROFILE CONFIGURATION", "KONFIGURACJA PROFILU SPRZĘTOWEGO");
            Add("HwCfg_BannerSub", "Select Vision controller model and I/O expansion modules enforced by the simulator", "Wybierz model sterownika Vision i moduły rozszerzeń I/O respektowane przez symulator");
            Add("HwCfg_LangLabel", "Language / Język:", "Język / Language:");
            Add("HwCfg_Section_Cpu", "1. PLC Controller Selection", "1. Wybór Sterownika PLC");
            Add("HwCfg_FamilyLabel", "Series / Family:", "Rodzina sterowników:");
            Add("HwCfg_ModelLabel", "Controller Model:", "Model sterownika:");
            Add("HwCfg_SnapinLabel", "Snap-in I/O Module (V570/V1040/V1210):", "Moduł wpinany Snap-in (V570/V1040/V1210):");
            Add("HwCfg_Specs_Resolution", "Display:", "Ekran:");
            Add("HwCfg_Specs_Touch", "Touchscreen:", "Ekran dotykowy:");
            Add("HwCfg_Specs_Yes", "Yes (Resistive Color)", "Tak (Rezystancyjny Kolor)");
            Add("HwCfg_Specs_No", "No (Keypad only)", "Nie (Tylko klawisze)");
            Add("HwCfg_Specs_Memory", "Memory Range:", "Pamięć rejestrów:");
            Add("HwCfg_Specs_OnboardIo", "Onboard I/O:", "I/O na płycie CPU:");

            Add("HwCfg_Section_Exp", "2. I/O Expansion Bus (EX-A2X / EX-RC1)", "2. Szyna Rozszerzeń Wejść/Wyjść (EX-A2X / EX-RC1)");
            Add("HwCfg_EnableExp", "Enable EX-A2X Expansion Bus (Up to 8 modules)", "Włącz magistralę rozszerzeń EX-A2X (do 8 modułów)");
            Add("HwCfg_Col_Slot", "Slot", "Slot");
            Add("HwCfg_Col_Module", "Expansion Module", "Moduł rozszerzeń");
            Add("HwCfg_Col_Di", "DI", "DI");
            Add("HwCfg_Col_Do", "DO", "DO");
            Add("HwCfg_Col_Ai", "AI", "AI");
            Add("HwCfg_Col_Ao", "AO", "AO");
            Add("HwCfg_Col_Description", "Description", "Opis modułu");

            Add("HwCfg_Section_Summary", "3. Configured I/O Summary", "3. Podsumowanie Skonfigurowanych Punktów I/O");
            Add("HwCfg_Sum_Di", "Total Digital Inputs (DI): {0} (I0 .. I{1})", "Łącznie wejścia cyfrowe (DI): {0} (I0 .. I{1})");
            Add("HwCfg_Sum_Do", "Total Digital Outputs (DO): {0} (O0 .. O{1})", "Łącznie wyjścia cyfrowe (DO): {0} (O0 .. O{1})");
            Add("HwCfg_Sum_Ai", "Total Analog Inputs (AI): {0} (AN0 .. AN{1})", "Łącznie wejścia analogowe (AI): {0} (AN0 .. AN{1})");
            Add("HwCfg_Sum_Ao", "Total Analog Outputs (AO): {0} (AO0 .. AO{1})", "Łącznie wyjścia analogowe (AO): {0} (AO0 .. AO{1})");

            Add("HwCfg_Preset_Komora9", "Load Preset: V350-RA22 + IO-DI8-RO8 Relay", "Wczytaj profil: V350-RA22 + IO-DI8-RO8 Przekaźniki");
            Add("HwCfg_Preset_Default", "Reset to Default (V350-RA22 Standalone)", "Przywróć domyślny (V350-RA22 bez rozszerzeń)");
            Add("HwCfg_ShowOnStartup", "Always show this configuration dialog on startup", "Zawsze pokazuj ten panel przy uruchamianiu");
            Add("HwCfg_Btn_Start", "Start Simulator", "Uruchom symulator");
            Add("HwCfg_Btn_Apply", "Apply Configuration", "Zastosuj konfigurację");
            Add("HwCfg_Btn_Cancel", "Cancel / Exit", "Anuluj / Wyjdź");

            // --- Okno Ustawień / Settings Dialog ---
            Add("Settings_Title", "Simulator Settings & Options", "Ustawienia i Opcje Symulatora");
            Add("Settings_Tab_General", "General", "Ogólne");
            Add("Settings_Tab_Logging", "Logging & Diagnostics", "Logowanie i Diagnostyka");
            Add("Settings_Tab_Network", "PCOM Network", "Sieć PCOM");

            Add("Settings_LangLabel", "Display Language / Język interfejsu:", "Język wyświetlania / Display Language:");
            Add("Settings_StartupLabel", "Startup Behavior:", "Zachowanie przy starcie:");
            Add("Settings_ShowHwOnStartup", "Show hardware configuration panel on startup", "Pokazuj panel konfiguracji sprzętowej przy starcie");
            Add("Settings_CurrentHw", "Active Hardware Profile: {0}", "Aktywny profil sprzętowy: {0}");
            Add("Settings_Btn_ChangeHw", "Change Hardware Profile...", "Zmień profil sprzętowy...");

            Add("Settings_EnableLogging", "Enable logging to file (virtualplc_debug.log)", "Włącz zapisywanie logów do pliku (virtualplc_debug.log)");
            Add("Settings_LogLevelLabel", "Logging Verbosity (Log Level):", "Szczegółowość logowania (Poziom logowania):");
            Add("Settings_LogLevel_Error", "Errors Only (Critical issues only)", "Tylko błędy (Tylko problemy krytyczne)");
            Add("Settings_LogLevel_Warn", "Warnings and Errors", "Ostrzeżenia i błędy");
            Add("Settings_LogLevel_Info", "Standard Information (Recommended)", "Standardowe informacje (Zalecane)");
            Add("Settings_LogLevel_Debug", "Detailed Debug (Internal engine & states)", "Szczegółowy debug (Silnik i stany)");
            Add("Settings_LogLevel_Trace", "Verbose / Trace (All packets, scans & touches)", "Pełny / Trace (Wszystkie pakiety, skany i dotyk)");
            Add("Settings_LogPcom", "Log PCOM TCP communication packets", "Zapisuj pakiety komunikacji PCOM TCP");
            Add("Settings_LogTouch", "Log HMI touchscreen coordinates and press events", "Zapisuj zdarzenia i koordynaty dotyku HMI");
            Add("Settings_LogPathLabel", "Log file location:", "Lokalizacja pliku logu:");
            Add("Settings_Btn_OpenLog", "Open Log File", "Otwórz plik logu");
            Add("Settings_Btn_ClearLog", "Clear Log File", "Wyczyść plik logu");
            Add("Settings_LogClearedMsg", "Log file has been cleared.", "Plik logu został wyczyszczony.");

            Add("Settings_PcomPortLabel", "PCOM TCP Server Port:", "Port serwera PCOM TCP:");
            Add("Settings_Btn_Save", "Save & Apply", "Zapisz i Zastosuj");
            Add("Settings_Btn_Cancel", "Cancel", "Anuluj");

            // --- Tabela Watch / Watch Table ---
            Add("Watch_Col_Type", "Type", "Typ");
            Add("Watch_Col_Addr", "Address", "Adres");
            Add("Watch_Col_Symbol", "Symbol / Name", "Symbol / Nazwa");
            Add("Watch_Col_Dec", "Value (Dec)", "Wartość (Dec)");
            Add("Watch_Col_Hex", "Value (Hex)", "Wartość (Hex)");
            Add("Watch_Col_Bin", "Value (Bin)", "Wartość (Bin)");
            Add("Watch_Filter_All", "All Operands", "Wszystkie Operandy");
            Add("Watch_Filter_Inputs", "Inputs (I)", "Wejścia (I)");
            Add("Watch_Filter_Outputs", "Outputs (O)", "Wyjścia (O)");
            Add("Watch_Filter_MB", "Memory Bits (MB)", "Bity pamięci (MB)");
            Add("Watch_Filter_MI", "Memory Integers (MI)", "Rejestry (MI)");
            Add("Watch_Filter_ML", "Long Integers (ML)", "Rejestry długie (ML)");
            Add("Watch_Filter_DW", "Double Words (DW)", "Podwójne słowa (DW)");
            Add("Watch_Filter_MF", "Floats (MF)", "Zmiennoprzecinkowe (MF)");
            Add("Watch_Filter_SB", "System Bits (SB)", "Bity systemowe (SB)");
            Add("Watch_Filter_SI", "System Integers (SI)", "Rejestry systemowe (SI)");
            Add("Watch_SearchPlaceholder", "Search symbol or address...", "Szukaj symbolu lub adresu...");

            // --- Boot Screen & Logging Menu ---
            Add("Boot_CleanState", "Controller is in clean factory standby mode.", "Sterownik jest w czystym stanie fabrycznym (czuwanie).");
            Add("Boot_OutputsOff", "All digital and analog outputs are OFF.", "Wszystkie wyjscia sa wylaczone (OFF).");
            Add("Boot_HowToStart", "To start program execution:", "Aby uruchomic aplikacje:");
            Add("Boot_LoadVlp", "-> Click 'File -> Load VisiLogic Project (*.vlp)'", "-> Kliknij menu 'Plik -> Wgraj projekt (*.vlp)'");
            Add("Boot_ConnectPcom", "-> OR connect VisiLogic via TCP 127.0.0.1:{0}", "-> LUB polacz VisiLogic po TCP 127.0.0.1:{0}");
            Add("Status_NoProgram", "NO USER PROGRAM", "BRAK PROGRAMU");
            Add("Status_Plc", "STATUS", "STAN");
            Add("Menu_Logging", "Logging", "Logowanie");
            Add("Menu_LogEnabled", "Enable Logging", "Wlacz logowanie");
            Add("Menu_LogLevel", "Log Verbosity Level", "Szczegolowosc logowania");
            Add("Menu_ClearLog", "Clear Debug Log File", "Wyczysc plik logu");
        }
    }
}
