using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("VirtualPLC Unitronics V350 Simulator")]
[assembly: AssemblyDescription("Symulator PLC/HMI Unitronics Vision V350-35-RA22")]
[assembly: AssemblyProduct("VirtualPLC")]
[assembly: AssemblyVersion("0.0.3.0")]
[assembly: AssemblyFileVersion("0.0.3.0")]

namespace UnitronicsV350Simulator
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Inicjalizacja glownego loggera do pliku virtualplc_debug.log w biezacym katalogu startowym
            AppLogger.Init("0.0.3", args);
            AppLogger.Info("Start aplikacji VirtualPLC v0.0.3");

            // Globalne lapanie wyjatkow watku UI i domeny aplikacji
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppLogger.Error("Nieobsluzony wyjatek w watku glownym UI", e.Exception);
                MessageBox.Show(
                    "Wystapil nieobslugiwany blad w symulatorze:\n" + e.Exception.Message + 
                    "\n\nSzczegoly zostaly zapisane w pliku:\n" + AppLogger.LogFilePath,
                    "Blad VirtualPLC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                AppLogger.Error("Krytyczny wyjatek w domenie aplikacji (UnhandledException)", ex);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                var mainForm = new MainForm();

                // Jesli podano sciezke do pliku .vlp w argumentach wiersza polecen
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
                AppLogger.Error("Blad podczas wykonywania Application.Run()", ex);
                throw;
            }
            finally
            {
                AppLogger.Info("Zakonczenie pracy aplikacji VirtualPLC v0.0.3 (Clean Exit).");
            }
        }
    }
}
