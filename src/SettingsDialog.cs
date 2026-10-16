using System;
using System.Drawing;
using System.Windows.Forms;

namespace UnitronicsV350Simulator
{
    public class SettingsDialog : Form
    {
        private SimulatorConfig config;
        private HardwareProfile currentProfile;

        // UI Controls
        private TabControl tabControl;
        private TabPage tabGeneral;
        private TabPage tabLogging;
        private TabPage tabNetwork;

        // General
        private Label lblLanguage;
        private ComboBox cboLanguage;
        private CheckBox chkShowHwOnStartup;
        private Label lblCurrentHw;
        private Button btnChangeHw;

        // Logging
        private CheckBox chkEnableLogging;
        private Label lblLogLevel;
        private ComboBox cboLogLevel;
        private CheckBox chkLogPcom;
        private CheckBox chkLogTouch;
        private Label lblLogPath;
        private TextBox txtLogPath;
        private Button btnOpenLog;
        private Button btnClearLog;

        // Network
        private Label lblPcomPort;
        private NumericUpDown numPcomPort;

        // Actions
        private Button btnSave;
        private Button btnCancel;

        public SettingsDialog(SimulatorConfig currentConfig, HardwareProfile profile)
        {
            config = currentConfig;
            currentProfile = profile;

            InitializeComponent();
            ApplyLanguage();
            LoadConfigToUi();

            Localization.OnLanguageChanged += ApplyLanguage;
            this.FormClosed += (s, e) => { Localization.OnLanguageChanged -= ApplyLanguage; };
        }

        private void InitializeComponent()
        {
            this.Text = Localization.T("Settings_Title");
            this.Size = new Size(620, 500);
            this.MinimumSize = new Size(580, 460);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            tabControl = new TabControl
            {
                Dock = DockStyle.Top,
                Height = 380,
                Padding = new Point(12, 6)
            };

            // --- 1. Zakładka Ogólne ---
            tabGeneral = new TabPage(Localization.T("Settings_Tab_General"));
            tabGeneral.BackColor = Color.FromArgb(28, 33, 42);

            lblLanguage = new Label { Location = new Point(20, 25), Size = new Size(220, 24), ForeColor = Color.White };
            cboLanguage = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(40, 46, 58),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(250, 22),
                Width = 180
            };
            cboLanguage.Items.Add("Polski");
            cboLanguage.Items.Add("English");
            cboLanguage.SelectedIndexChanged += (s, e) =>
            {
                var newLang = (cboLanguage.SelectedIndex == 0) ? AppLanguage.Polish : AppLanguage.English;
                Localization.SetLanguage(newLang);
            };

            chkShowHwOnStartup = new CheckBox
            {
                Location = new Point(20, 75),
                Size = new Size(520, 24),
                ForeColor = Color.White
            };

            lblCurrentHw = new Label
            {
                Location = new Point(20, 125),
                Size = new Size(520, 30),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 220, 255)
            };

            btnChangeHw = new Button
            {
                Location = new Point(20, 165),
                Size = new Size(220, 32),
                BackColor = Color.FromArgb(40, 48, 64),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnChangeHw.FlatAppearance.BorderColor = Color.FromArgb(0, 180, 220);
            btnChangeHw.Click += (s, e) =>
            {
                using (var dlg = new HardwareConfigDialog(currentProfile, false))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        currentProfile = dlg.ResultProfile;
                        config.Profile = currentProfile;
                        config.ShowConfigOnStartup = dlg.ShowOnStartupValue;
                        chkShowHwOnStartup.Checked = config.ShowConfigOnStartup;
                        UpdateCurrentHwLabel();
                    }
                }
            };

            tabGeneral.Controls.AddRange(new Control[] { lblLanguage, cboLanguage, chkShowHwOnStartup, lblCurrentHw, btnChangeHw });

            // --- 2. Zakładka Logowanie ---
            tabLogging = new TabPage(Localization.T("Settings_Tab_Logging"));
            tabLogging.BackColor = Color.FromArgb(28, 33, 42);

            chkEnableLogging = new CheckBox
            {
                Location = new Point(20, 20),
                Size = new Size(520, 24),
                ForeColor = Color.FromArgb(0, 255, 160),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            chkEnableLogging.CheckedChanged += (s, e) =>
            {
                bool en = chkEnableLogging.Checked;
                cboLogLevel.Enabled = en;
                chkLogPcom.Enabled = en;
                chkLogTouch.Enabled = en;
            };

            lblLogLevel = new Label { Location = new Point(20, 60), Size = new Size(240, 24), ForeColor = Color.White };
            cboLogLevel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(40, 46, 58),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(270, 58),
                Width = 280
            };
            // Items dodawane w ApplyLanguage

            chkLogPcom = new CheckBox
            {
                Location = new Point(20, 100),
                Size = new Size(520, 24),
                ForeColor = Color.White
            };

            chkLogTouch = new CheckBox
            {
                Location = new Point(20, 130),
                Size = new Size(520, 24),
                ForeColor = Color.White
            };

            lblLogPath = new Label { Location = new Point(20, 175), Size = new Size(520, 20), ForeColor = Color.FromArgb(180, 190, 205) };
            txtLogPath = new TextBox
            {
                Location = new Point(20, 200),
                Size = new Size(530, 24),
                BackColor = Color.FromArgb(20, 24, 32),
                ForeColor = Color.FromArgb(200, 220, 240),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtLogPath.Text = AppLogger.LogFilePath;

            btnOpenLog = new Button
            {
                Location = new Point(20, 240),
                Size = new Size(160, 30),
                BackColor = Color.FromArgb(40, 48, 64),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnOpenLog.FlatAppearance.BorderColor = Color.FromArgb(0, 180, 220);
            btnOpenLog.Click += (s, e) => AppLogger.OpenLogFile();

            btnClearLog = new Button
            {
                Location = new Point(190, 240),
                Size = new Size(160, 30),
                BackColor = Color.FromArgb(50, 30, 36),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnClearLog.FlatAppearance.BorderColor = Color.FromArgb(200, 60, 80);
            btnClearLog.Click += (s, e) =>
            {
                AppLogger.ClearLog();
                MessageBox.Show(this, Localization.T("Settings_LogClearedMsg"), "VirtualPLC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            tabLogging.Controls.AddRange(new Control[] {
                chkEnableLogging, lblLogLevel, cboLogLevel, chkLogPcom, chkLogTouch,
                lblLogPath, txtLogPath, btnOpenLog, btnClearLog
            });

            // --- 3. Zakładka Sieć ---
            tabNetwork = new TabPage(Localization.T("Settings_Tab_Network"));
            tabNetwork.BackColor = Color.FromArgb(28, 33, 42);

            lblPcomPort = new Label { Location = new Point(20, 25), Size = new Size(220, 24), ForeColor = Color.White };
            numPcomPort = new NumericUpDown
            {
                Location = new Point(250, 22),
                Width = 120,
                Minimum = 1024,
                Maximum = 65535,
                Value = 20256,
                BackColor = Color.FromArgb(40, 46, 58),
                ForeColor = Color.White
            };
            tabNetwork.Controls.AddRange(new Control[] { lblPcomPort, numPcomPort });

            tabControl.TabPages.AddRange(new TabPage[] { tabGeneral, tabLogging, tabNetwork });
            this.Controls.Add(tabControl);

            // Dolne przyciski
            btnSave = new Button
            {
                Location = new Point(340, 405),
                Size = new Size(130, 34),
                BackColor = Color.FromArgb(0, 130, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnSave.FlatAppearance.BorderColor = Color.FromArgb(0, 220, 140);
            btnSave.Click += BtnSave_Click;

            btnCancel = new Button
            {
                Location = new Point(480, 405),
                Size = new Size(100, 34),
                BackColor = Color.FromArgb(50, 56, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(100, 110, 130);
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.Controls.AddRange(new Control[] { btnSave, btnCancel });
        }

        private void LoadConfigToUi()
        {
            cboLanguage.SelectedIndex = (config.Language == AppLanguage.Polish) ? 0 : 1;
            chkShowHwOnStartup.Checked = config.ShowConfigOnStartup;
            UpdateCurrentHwLabel();

            chkEnableLogging.Checked = config.LoggingEnabled;
            chkLogPcom.Checked = config.LogPcomPackets;
            chkLogTouch.Checked = config.LogTouchEvents;

            // Poziom logowania
            int vIdx = (int)config.LoggingVerbosity - 1;
            if (vIdx >= 0 && vIdx < cboLogLevel.Items.Count)
            {
                cboLogLevel.SelectedIndex = vIdx;
            }

            numPcomPort.Value = (config.PcomPort > 0) ? config.PcomPort : 20256;
        }

        private void UpdateCurrentHwLabel()
        {
            var spec = currentProfile.GetCurrentModelSpec();
            lblCurrentHw.Text = string.Format(Localization.T("Settings_CurrentHw"),
                spec.ModelName + " (" + currentProfile.GetTotalDi() + " DI, " + currentProfile.GetTotalDo() + " DO)");
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            config.Language = (cboLanguage.SelectedIndex == 0) ? AppLanguage.Polish : AppLanguage.English;
            config.ShowConfigOnStartup = chkShowHwOnStartup.Checked;
            config.Profile = currentProfile;

            config.LoggingEnabled = chkEnableLogging.Checked;
            config.LoggingVerbosity = (LogVerbosity)(cboLogLevel.SelectedIndex + 1);
            config.LogPcomPackets = chkLogPcom.Checked;
            config.LogTouchEvents = chkLogTouch.Checked;
            config.PcomPort = (int)numPcomPort.Value;

            config.Save();
            AppLogger.Configure(config);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ApplyLanguage()
        {
            this.Text = Localization.T("Settings_Title");
            tabGeneral.Text = Localization.T("Settings_Tab_General");
            tabLogging.Text = Localization.T("Settings_Tab_Logging");
            tabNetwork.Text = Localization.T("Settings_Tab_Network");

            lblLanguage.Text = Localization.T("Settings_LangLabel");
            chkShowHwOnStartup.Text = Localization.T("Settings_ShowHwOnStartup");
            btnChangeHw.Text = Localization.T("Settings_Btn_ChangeHw");
            UpdateCurrentHwLabel();

            chkEnableLogging.Text = Localization.T("Settings_EnableLogging");
            lblLogLevel.Text = Localization.T("Settings_LogLevelLabel");

            int oldLevelIdx = cboLogLevel.SelectedIndex;
            cboLogLevel.Items.Clear();
            cboLogLevel.Items.Add(Localization.T("Settings_LogLevel_Error"));
            cboLogLevel.Items.Add(Localization.T("Settings_LogLevel_Warn"));
            cboLogLevel.Items.Add(Localization.T("Settings_LogLevel_Info"));
            cboLogLevel.Items.Add(Localization.T("Settings_LogLevel_Debug"));
            cboLogLevel.Items.Add(Localization.T("Settings_LogLevel_Trace"));
            if (oldLevelIdx >= 0 && oldLevelIdx < cboLogLevel.Items.Count)
            {
                cboLogLevel.SelectedIndex = oldLevelIdx;
            }

            chkLogPcom.Text = Localization.T("Settings_LogPcom");
            chkLogTouch.Text = Localization.T("Settings_LogTouch");
            lblLogPath.Text = Localization.T("Settings_LogPathLabel");
            btnOpenLog.Text = Localization.T("Settings_Btn_OpenLog");
            btnClearLog.Text = Localization.T("Settings_Btn_ClearLog");

            lblPcomPort.Text = Localization.T("Settings_PcomPortLabel");
            btnSave.Text = Localization.T("Settings_Btn_Save");
            btnCancel.Text = Localization.T("Settings_Btn_Cancel");
        }
    }
}
