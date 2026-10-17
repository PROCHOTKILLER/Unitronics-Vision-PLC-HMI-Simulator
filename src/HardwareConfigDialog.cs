using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace UnitronicsV350Simulator
{
    public class HardwareConfigDialog : Form
    {
        private HardwareProfile profile;
        private bool isStartupMode;

        // UI Controls
        private Panel bannerPanel;
        private Label lblBannerTitle;
        private Label lblBannerSub;
        private ComboBox cboLanguage;

        private GroupBox grpCpu;
        private Label lblFamily;
        private ComboBox cboFamily;
        private Label lblModel;
        private ComboBox cboModel;
        private Label lblSnapIn;
        private ComboBox cboSnapIn;
        private Panel pnlSpecCard;
        private Label lblSpecDisplay;
        private Label lblSpecTouch;
        private Label lblSpecMemory;
        private Label lblSpecIo;

        private GroupBox grpExp;
        private CheckBox chkEnableExp;
        private DataGridView dgvSlots;

        private GroupBox grpSummary;
        private Label lblSumDi;
        private Label lblSumDo;
        private Label lblSumAi;
        private Label lblSumAo;

        private Button btnPresetExpanded;
        private Button btnPresetDefault;
        private CheckBox chkShowOnStartup;
        private Button btnStart;
        private Button btnCancel;

        public HardwareProfile ResultProfile { get { return profile; } }
        public bool ShowOnStartupValue { get { return chkShowOnStartup.Checked; } }

        public HardwareConfigDialog(HardwareProfile initialProfile, bool startupMode = false)
        {
            profile = initialProfile ?? HardwareProfile.CreateDefaultProfile();
            profile.EnsureSlotsInitialized();
            isStartupMode = startupMode;

            InitializeComponent();
            ApplyLanguage();
            LoadProfileToUi();

            Localization.OnLanguageChanged += ApplyLanguage;
            this.FormClosed += (s, e) => { Localization.OnLanguageChanged -= ApplyLanguage; };
        }

        private void InitializeComponent()
        {
            this.Text = Localization.T("HwCfg_Title");
            this.Size = new Size(880, 750);
            this.MinimumSize = new Size(850, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            // 1. Header Banner
            bannerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(16, 20, 28)
            };

            lblBannerTitle = new Label
            {
                Text = Localization.T("HwCfg_BannerTitle"),
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 220, 255),
                Location = new Point(18, 12),
                AutoSize = true
            };
            lblBannerSub = new Label
            {
                Text = Localization.T("HwCfg_BannerSub"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 190, 205),
                Location = new Point(19, 38),
                AutoSize = true
            };

            var lblLang = new Label
            {
                Text = Localization.T("HwCfg_LangLabel"),
                ForeColor = Color.FromArgb(200, 210, 225),
                Location = new Point(640, 15),
                Size = new Size(110, 22),
                TextAlign = ContentAlignment.MiddleRight
            };

            cboLanguage = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(36, 42, 54),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(755, 15),
                Width = 95
            };
            cboLanguage.Items.Add("Polski");
            cboLanguage.Items.Add("English");
            cboLanguage.SelectedIndex = (Localization.CurrentLanguage == AppLanguage.Polish) ? 0 : 1;
            cboLanguage.SelectedIndexChanged += (s, e) =>
            {
                if (cboLanguage.SelectedIndex == 0)
                {
                    Localization.SetLanguage(AppLanguage.Polish);
                    SimulatorConfig.Current.Language = AppLanguage.Polish;
                }
                else
                {
                    Localization.SetLanguage(AppLanguage.English);
                    SimulatorConfig.Current.Language = AppLanguage.English;
                }
            };

            bannerPanel.Controls.AddRange(new Control[] { lblBannerTitle, lblBannerSub, lblLang, cboLanguage });
            this.Controls.Add(bannerPanel);

            // Main body inside ScrollPanel
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(15, 10, 15, 10)
            };
            this.Controls.Add(mainPanel);

            // --- 2. CPU Selection Section ---
            grpCpu = new GroupBox
            {
                Text = Localization.T("HwCfg_Section_Cpu"),
                Location = new Point(15, 80),
                Size = new Size(835, 180),
                ForeColor = Color.FromArgb(0, 220, 255)
            };

            lblFamily = new Label { Text = Localization.T("HwCfg_FamilyLabel"), Location = new Point(15, 25), Size = new Size(140, 22), ForeColor = Color.White };
            cboFamily = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(36, 42, 54),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(160, 25),
                Width = 200
            };
            cboFamily.Items.AddRange(new object[] { "Vision 350", "Vision 130", "Vision 430", "Vision 570", "Vision 1040", "Vision 1210" });
            cboFamily.SelectedIndexChanged += CboFamily_SelectedIndexChanged;

            lblModel = new Label { Text = Localization.T("HwCfg_ModelLabel"), Location = new Point(15, 55), Size = new Size(140, 22), ForeColor = Color.White };
            cboModel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(36, 42, 54),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(160, 55),
                Width = 200
            };
            cboModel.SelectedIndexChanged += CboModel_SelectedIndexChanged;

            lblSnapIn = new Label { Text = Localization.T("HwCfg_SnapinLabel"), Location = new Point(15, 85), Size = new Size(140, 32), ForeColor = Color.White };
            cboSnapIn = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(36, 42, 54),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(160, 88),
                Width = 200,
                Enabled = false
            };
            foreach (var kvp in HardwareProfile.AvailableSnapIns)
            {
                cboSnapIn.Items.Add(kvp.Key);
            }
            cboSnapIn.SelectedIndexChanged += (s, e) =>
            {
                profile.SelectedSnapInName = cboSnapIn.SelectedItem != null ? cboSnapIn.SelectedItem.ToString() : "None";
                UpdateSpecsAndSummary();
            };

            // Spec Card
            pnlSpecCard = new Panel
            {
                Location = new Point(380, 20),
                Size = new Size(440, 145),
                BackColor = Color.FromArgb(32, 38, 48),
                BorderStyle = BorderStyle.FixedSingle
            };
            lblSpecDisplay = new Label { Location = new Point(10, 10), Size = new Size(420, 20), ForeColor = Color.FromArgb(230, 230, 240) };
            lblSpecTouch = new Label { Location = new Point(10, 35), Size = new Size(420, 20), ForeColor = Color.FromArgb(230, 230, 240) };
            lblSpecMemory = new Label { Location = new Point(10, 60), Size = new Size(420, 20), ForeColor = Color.FromArgb(230, 230, 240) };
            lblSpecIo = new Label { Location = new Point(10, 85), Size = new Size(420, 50), ForeColor = Color.FromArgb(0, 220, 255) };
            pnlSpecCard.Controls.AddRange(new Control[] { lblSpecDisplay, lblSpecTouch, lblSpecMemory, lblSpecIo });

            grpCpu.Controls.AddRange(new Control[] { lblFamily, cboFamily, lblModel, cboModel, lblSnapIn, cboSnapIn, pnlSpecCard });
            mainPanel.Controls.Add(grpCpu);

            // --- 3. Expansion Bus Section ---
            grpExp = new GroupBox
            {
                Text = Localization.T("HwCfg_Section_Exp"),
                Location = new Point(15, 270),
                Size = new Size(835, 235),
                ForeColor = Color.FromArgb(0, 220, 255)
            };

            chkEnableExp = new CheckBox
            {
                Text = Localization.T("HwCfg_EnableExp"),
                Location = new Point(15, 22),
                Size = new Size(450, 22),
                ForeColor = Color.White,
                Checked = true
            };
            chkEnableExp.CheckedChanged += (s, e) =>
            {
                profile.EnableExpansionBus = chkEnableExp.Checked;
                dgvSlots.Enabled = chkEnableExp.Checked;
                UpdateSpecsAndSummary();
            };

            dgvSlots = new DataGridView
            {
                Location = new Point(15, 48),
                Size = new Size(805, 175),
                BackgroundColor = Color.FromArgb(32, 38, 48),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 56, 70),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(28, 33, 42),
                    ForeColor = Color.White,
                    SelectionBackColor = Color.FromArgb(0, 120, 180),
                    SelectionForeColor = Color.White
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(40, 46, 60),
                    ForeColor = Color.FromArgb(0, 220, 255),
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold)
                },
                EnableHeadersVisualStyles = false
            };

            // DataGridView Columns
            var colSlot = new DataGridViewTextBoxColumn { Name = "Slot", HeaderText = "Slot", Width = 55, ReadOnly = true };
            var colModule = new DataGridViewComboBoxColumn
            {
                Name = "Module",
                HeaderText = Localization.T("HwCfg_Col_Module"),
                Width = 160,
                FlatStyle = FlatStyle.Flat
            };
            foreach (var kvp in HardwareProfile.AvailableExpansions)
            {
                colModule.Items.Add(kvp.Key);
            }
            var colDi = new DataGridViewTextBoxColumn { Name = "DI", HeaderText = "DI", Width = 50, ReadOnly = true };
            var colDo = new DataGridViewTextBoxColumn { Name = "DO", HeaderText = "DO", Width = 50, ReadOnly = true };
            var colAi = new DataGridViewTextBoxColumn { Name = "AI", HeaderText = "AI", Width = 50, ReadOnly = true };
            var colAo = new DataGridViewTextBoxColumn { Name = "AO", HeaderText = "AO", Width = 50, ReadOnly = true };
            var colDesc = new DataGridViewTextBoxColumn { Name = "Desc", HeaderText = Localization.T("HwCfg_Col_Description"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true };

            dgvSlots.Columns.AddRange(new DataGridViewColumn[] { colSlot, colModule, colDi, colDo, colAi, colAo, colDesc });

            dgvSlots.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 1)
                {
                    var modName = dgvSlots.Rows[e.RowIndex].Cells[1].Value as string;
                    if (e.RowIndex < profile.ExpansionSlots.Count)
                    {
                        profile.ExpansionSlots[e.RowIndex].ModuleName = modName ?? "None";
                    }
                    UpdateSlotRowSpecs(e.RowIndex, modName);
                    UpdateSpecsAndSummary();
                }
            };

            grpExp.Controls.AddRange(new Control[] { chkEnableExp, dgvSlots });
            mainPanel.Controls.Add(grpExp);

            // --- 4. Configured I/O Summary Section ---
            grpSummary = new GroupBox
            {
                Text = Localization.T("HwCfg_Section_Summary"),
                Location = new Point(15, 515),
                Size = new Size(835, 75),
                ForeColor = Color.FromArgb(0, 220, 255)
            };

            lblSumDi = new Label { Location = new Point(20, 22), Size = new Size(390, 20), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 255, 128) };
            lblSumDo = new Label { Location = new Point(420, 22), Size = new Size(390, 20), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(255, 200, 50) };
            lblSumAi = new Label { Location = new Point(20, 46), Size = new Size(390, 20), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 200, 255) };
            lblSumAo = new Label { Location = new Point(420, 46), Size = new Size(390, 20), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 140, 255) };

            grpSummary.Controls.AddRange(new Control[] { lblSumDi, lblSumDo, lblSumAi, lblSumAo });
            mainPanel.Controls.Add(grpSummary);

            // --- 5. Bottom Action Buttons & Presets ---
            btnPresetExpanded = new Button
            {
                Text = Localization.T("HwCfg_Preset_Expanded"),
                Location = new Point(15, 600),
                Size = new Size(310, 30),
                BackColor = Color.FromArgb(40, 48, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnPresetExpanded.FlatAppearance.BorderColor = Color.FromArgb(0, 180, 220);
            btnPresetExpanded.Click += (s, e) =>
            {
                profile = HardwareProfile.CreateExpandedV350Profile();
                LoadProfileToUi();
            };

            btnPresetDefault = new Button
            {
                Text = Localization.T("HwCfg_Preset_Default"),
                Location = new Point(335, 600),
                Size = new Size(240, 30),
                BackColor = Color.FromArgb(40, 48, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnPresetDefault.FlatAppearance.BorderColor = Color.FromArgb(100, 110, 130);
            btnPresetDefault.Click += (s, e) =>
            {
                profile = new HardwareProfile();
                profile.SelectedModelName = "V350-35-RA22";
                profile.SelectedSnapInName = "None";
                profile.EnableExpansionBus = false;
                profile.EnsureSlotsInitialized();
                LoadProfileToUi();
            };

            chkShowOnStartup = new CheckBox
            {
                Text = Localization.T("HwCfg_ShowOnStartup"),
                Location = new Point(15, 638),
                Size = new Size(400, 24),
                ForeColor = Color.White,
                Checked = SimulatorConfig.Current.ShowConfigOnStartup
            };

            btnStart = new Button
            {
                Text = isStartupMode ? Localization.T("HwCfg_Btn_Start") : Localization.T("HwCfg_Btn_Apply"),
                Location = new Point(590, 634),
                Size = new Size(150, 34),
                BackColor = Color.FromArgb(0, 140, 90),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnStart.FlatAppearance.BorderColor = Color.FromArgb(0, 220, 140);
            btnStart.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnCancel = new Button
            {
                Text = Localization.T("HwCfg_Btn_Cancel"),
                Location = new Point(748, 634),
                Size = new Size(100, 34),
                BackColor = Color.FromArgb(60, 30, 36),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(180, 50, 60);
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            mainPanel.Controls.AddRange(new Control[] { btnPresetExpanded, btnPresetDefault, chkShowOnStartup, btnStart, btnCancel });
        }

        private void CboFamily_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cboFamily.SelectedItem == null) return;
            string family = cboFamily.SelectedItem.ToString();

            cboModel.Items.Clear();
            foreach (var kvp in HardwareProfile.AvailableModels)
            {
                if (kvp.Value.Family.Equals(family, StringComparison.OrdinalIgnoreCase))
                {
                    cboModel.Items.Add(kvp.Key);
                }
            }
            if (cboModel.Items.Count > 0)
            {
                cboModel.SelectedIndex = 0;
            }
        }

        private void CboModel_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cboModel.SelectedItem == null) return;
            profile.SelectedModelName = cboModel.SelectedItem.ToString();
            var spec = profile.GetCurrentModelSpec();

            cboSnapIn.Enabled = spec.IsModular;
            if (!spec.IsModular)
            {
                cboSnapIn.SelectedItem = "None";
                profile.SelectedSnapInName = "None";
            }
            else if (cboSnapIn.SelectedItem == null || cboSnapIn.SelectedItem.ToString() == "None")
            {
                cboSnapIn.SelectedItem = "V200-18-E1B";
                profile.SelectedSnapInName = "V200-18-E1B";
            }

            UpdateSpecsAndSummary();
        }

        private void LoadProfileToUi()
        {
            var spec = profile.GetCurrentModelSpec();
            int famIdx = cboFamily.FindStringExact(spec.Family);
            if (famIdx >= 0) cboFamily.SelectedIndex = famIdx;

            int modelIdx = cboModel.FindStringExact(spec.ModelName);
            if (modelIdx >= 0) cboModel.SelectedIndex = modelIdx;

            int snapIdx = cboSnapIn.FindStringExact(profile.SelectedSnapInName);
            if (snapIdx >= 0) cboSnapIn.SelectedIndex = snapIdx;

            chkEnableExp.Checked = profile.EnableExpansionBus;

            dgvSlots.Rows.Clear();
            for (int i = 0; i < 8; i++)
            {
                var slotCfg = profile.ExpansionSlots[i];
                var expSpec = profile.GetExpansionSpec(slotCfg.ModuleName);

                int rowIdx = dgvSlots.Rows.Add(
                    "#" + (i + 1),
                    slotCfg.ModuleName,
                    expSpec.Di > 0 ? expSpec.Di.ToString() : "-",
                    expSpec.TotalDo > 0 ? expSpec.TotalDo.ToString() : "-",
                    expSpec.Ai > 0 ? expSpec.Ai.ToString() : "-",
                    expSpec.Ao > 0 ? expSpec.Ao.ToString() : "-",
                    expSpec.Description
                );
            }

            UpdateSpecsAndSummary();
        }

        private void UpdateSlotRowSpecs(int rowIndex, string modName)
        {
            var expSpec = profile.GetExpansionSpec(modName);
            var row = dgvSlots.Rows[rowIndex];
            row.Cells[2].Value = expSpec.Di > 0 ? expSpec.Di.ToString() : "-";
            row.Cells[3].Value = expSpec.TotalDo > 0 ? expSpec.TotalDo.ToString() : "-";
            row.Cells[4].Value = expSpec.Ai > 0 ? expSpec.Ai.ToString() : "-";
            row.Cells[5].Value = expSpec.Ao > 0 ? expSpec.Ao.ToString() : "-";
            row.Cells[6].Value = expSpec.Description;
        }

        private void UpdateSpecsAndSummary()
        {
            var spec = profile.GetCurrentModelSpec();
            var snap = profile.GetCurrentSnapInSpec();

            lblSpecDisplay.Text = string.Format("{0} {1}x{2} px ({3})",
                Localization.T("HwCfg_Specs_Resolution"), spec.ScreenWidth, spec.ScreenHeight, spec.Family);

            lblSpecTouch.Text = string.Format("{0} {1}",
                Localization.T("HwCfg_Specs_Touch"),
                spec.HasTouchscreen ? Localization.T("HwCfg_Specs_Yes") : Localization.T("HwCfg_Specs_No"));

            lblSpecMemory.Text = string.Format("{0} MB 4096, MI 2048, ML 512, DW 512, MF 64, T 256, C 32",
                Localization.T("HwCfg_Specs_Memory"));

            if (spec.IsModular)
            {
                lblSpecIo.Text = string.Format("{0} Snap-in [{1}]: {2} DI, {3} DO, {4} AI, {5} AO\n({6})",
                    Localization.T("HwCfg_Specs_OnboardIo"), snap.ModelName, snap.Di, snap.TotalDo, snap.Ai, snap.Ao, snap.Description);
            }
            else
            {
                lblSpecIo.Text = string.Format("{0} {1} DI, {2} DO ({3} Relay + {4} Trans), {5} AI, {6} AO\n({7})",
                    Localization.T("HwCfg_Specs_OnboardIo"), spec.OnboardDi, spec.TotalOnboardDo, spec.OnboardDoRelay, spec.OnboardDoTrans,
                    spec.OnboardAi, spec.OnboardAo, spec.Description);
            }

            int totalDi = profile.GetTotalDi();
            int totalDo = profile.GetTotalDo();
            int totalAi = profile.GetTotalAi();
            int totalAo = profile.GetTotalAo();

            lblSumDi.Text = string.Format(Localization.T("HwCfg_Sum_Di"), totalDi, Math.Max(0, totalDi - 1));
            lblSumDo.Text = string.Format(Localization.T("HwCfg_Sum_Do"), totalDo, Math.Max(0, totalDo - 1));
            lblSumAi.Text = string.Format(Localization.T("HwCfg_Sum_Ai"), totalAi, Math.Max(0, totalAi - 1));
            lblSumAo.Text = string.Format(Localization.T("HwCfg_Sum_Ao"), totalAo, Math.Max(0, totalAo - 1));
        }

        private void ApplyLanguage()
        {
            this.Text = Localization.T("HwCfg_Title");
            lblBannerTitle.Text = Localization.T("HwCfg_BannerTitle");
            lblBannerSub.Text = Localization.T("HwCfg_BannerSub");

            grpCpu.Text = Localization.T("HwCfg_Section_Cpu");
            lblFamily.Text = Localization.T("HwCfg_FamilyLabel");
            lblModel.Text = Localization.T("HwCfg_ModelLabel");
            lblSnapIn.Text = Localization.T("HwCfg_SnapinLabel");

            grpExp.Text = Localization.T("HwCfg_Section_Exp");
            chkEnableExp.Text = Localization.T("HwCfg_EnableExp");

            if (dgvSlots.Columns.Count >= 7)
            {
                dgvSlots.Columns[1].HeaderText = Localization.T("HwCfg_Col_Module");
                dgvSlots.Columns[6].HeaderText = Localization.T("HwCfg_Col_Description");
            }

            grpSummary.Text = Localization.T("HwCfg_Section_Summary");
            btnPresetExpanded.Text = Localization.T("HwCfg_Preset_Expanded");
            btnPresetDefault.Text = Localization.T("HwCfg_Preset_Default");
            chkShowOnStartup.Text = Localization.T("HwCfg_ShowOnStartup");

            btnStart.Text = isStartupMode ? Localization.T("HwCfg_Btn_Start") : Localization.T("HwCfg_Btn_Apply");
            btnCancel.Text = Localization.T("HwCfg_Btn_Cancel");

            UpdateSpecsAndSummary();
        }
    }
}
