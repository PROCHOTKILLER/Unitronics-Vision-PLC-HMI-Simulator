using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Unitronics Vision PLC/HMI Simulator")]
[assembly: AssemblyDescription("Standalone Unitronics Vision PLC/HMI Hardware Simulator")]
[assembly: AssemblyCompany("Unitronics Simulator Project")]
[assembly: AssemblyProduct("Unitronics Vision Virtual PLC")]
[assembly: AssemblyVersion("0.0.7.0")]
[assembly: AssemblyFileVersion("0.0.7.0")]

namespace UnitronicsV350Simulator
{
    static class Program
    {
        /// <summary>
        /// Main entry point for the standalone Unitronics Vision PLC/HMI Simulator.
        /// Initializes configuration, localization, logging, and the Hardware Configuration Wizard.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Load persistent configuration first
            SimulatorConfig config = SimulatorConfig.Load();
            Localization.SetLanguage(config.Language);

            AppLogger.EnsureInitialized();
            AppLogger.Configure(config);
            AppLogger.Info("Initializing Windows Forms environment and exception handlers...");

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += new ThreadExceptionEventHandler(OnThreadException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnUnhandledException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // Show Hardware Configuration Wizard on startup (unless disabled in configuration)
                if (config.ShowConfigOnStartup)
                {
                    AppLogger.Info("Opening startup Hardware Configuration Wizard...");
                    using (HardwareConfigDialog hwDlg = new HardwareConfigDialog(config.Profile, true))
                    {
                        DialogResult res = hwDlg.ShowDialog();
                        if (res == DialogResult.OK)
                        {
                            config.Profile = hwDlg.ResultProfile;
                            config.ShowConfigOnStartup = hwDlg.ShowOnStartupValue;
                            config.Save();
                            AppLogger.Info("Selected hardware profile: " + config.Profile.GetModelFullName());
                        }
                        else
                        {
                            AppLogger.Info("User closed the Hardware Configuration Wizard - exiting application.");
                            return;
                        }
                    }
                }

                AppLogger.Info("Creating MainForm instance and starting main event loop...");
                Application.Run(new MainForm(config));
                AppLogger.Info("Application exited cleanly.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Critical exception in Main()", ex);
                MessageBox.Show(
                    "Critical simulator error:\n\n" + ex.Message + "\n\nFull error details were saved to:\n" + AppLogger.LogFilePath,
                    "Unitronics Simulator - Critical Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            AppLogger.Error("Unhandled UI thread exception (Application.ThreadException)", e.Exception);
            MessageBox.Show(
                "An unexpected UI error occurred:\n\n" + e.Exception.Message +
                "\n\nDetails were logged to:\n" + AppLogger.LogFilePath,
                "Unitronics Simulator - Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            AppLogger.Error("Unhandled application domain exception (IsTerminating=" + e.IsTerminating + ")", ex);
        }
    }
}
