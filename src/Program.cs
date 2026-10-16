using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("VirtualPLC Unitronics Vision PLC/HMI Simulator")]
[assembly: AssemblyDescription("Bilingual PLC/HMI Simulator for Unitronics Vision Series (V130 - V1210)")]
[assembly: AssemblyProduct("VirtualPLC")]
[assembly: AssemblyVersion("0.0.4.0")]
[assembly: AssemblyFileVersion("0.0.4.0")]

namespace UnitronicsV350Simulator
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // 1. Ładowanie trwałej konfiguracji symulatora
            var config = SimulatorConfig.Load();

            // 2. Inicjalizacja wielowątkowego loggera do pliku virtualplc_debug.log
            AppLogger.Init("0.0.4", args);
            AppLogger.Configure(config);
            AppLogger.Info("Start aplikacji VirtualPLC v0.0.4");

            // 3. Inicjalizacja języka interfejsu
            Localization.SetLanguage(config.Language);

            // Globalne łapanie wyjątków wątku UI i domeny aplikacji
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppLogger.Error("Nieobsłużony wyjątek w wątku głównym UI", e.Exception);
                MessageBox.Show(
                    "Wystąpił nieobsługiwany błąd w symulatorze:\n" + e.Exception.Message + 
                    "\n\nSzczegóły zostały zapisane w pliku:\n" + AppLogger.LogFilePath,
                    "Błąd VirtualPLC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                AppLogger.Error("Krytyczny wyjątek w domenie aplikacji (UnhandledException)", ex);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // 4. Panel konfiguracji sprzętowej przed uruchomieniem (jeśli włączony w opcjach)
                if (config.ShowConfigOnStartup)
                {
                    AppLogger.Info("Wyświetlanie okna konfiguracji sprzętowej na starcie (ShowConfigOnStartup=true)...");
                    using (var hwDlg = new HardwareConfigDialog(config.Profile, startupMode: true))
                    {
                        var dlgRes = hwDlg.ShowDialog();
                        if (dlgRes != DialogResult.OK)
                        {
                            AppLogger.Info("Uruchomienie symulatora anulowane przez użytkownika w oknie konfiguracji sprzętowej.");
                            return;
                        }

                        config.Profile = hwDlg.ResultProfile;
                        config.ShowConfigOnStartup = hwDlg.ShowOnStartupValue;
                        config.Save();
                        AppLogger.Info("Profil sprzętowy zatwierdzony na starcie: " + config.Profile.GetModelFullName());
                    }
                }

                var mainForm = new MainForm(config);

                // Jeśli podano ścieżkę do pliku .vlp w argumentach wiersza poleceń
                if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                {
                    string argPath = args[0].Trim('"', ' ');
                    if (System.IO.File.Exists(argPath))
                    {
                        AppLogger.Info("Automatyczne wczytywanie projektu z parametru CLI: " + argPath);
                        mainForm.LoadProjectFile(argPath);
                    }
                }

                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Błąd podczas wykonywania Application.Run()", ex);
                throw;
            }
            finally
            {
                AppLogger.Info("Zakończenie pracy aplikacji VirtualPLC v0.0.4 (Clean Exit).");
            }
        }
    }
}
