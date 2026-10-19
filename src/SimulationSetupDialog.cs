using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace UnitronicsV350Simulator
{
    public class SimulationSetupDialog : Form
    {
        private LoadedProject project;
        private HardwareProfile hwProfile;
        private ProjectSimulationProfile simProfile;

        private RadioButton rbNeutralMode;
        private RadioButton rbSimulateMode;
        private Button btnAutoSuggest;
        private Button btnResetNeutral;
        private Button btnLoadProfile;
        private Button btnSaveProfile;

        private TabControl tabControl;
        private DataGridView dgvInputs;
        private DataGridView dgvOutputs;
        private DataGridView dgvAnalogs;

        private Button btnApply;
        private Button btnStayNeutral;

        private static readonly string[] InputRoleLabelsPl = new string[]
        {
            "Neutralne (Ręczne NO — pozycja 0)",
            "Sztucznie ustawione (Stały stan NC/NO)",
            "Sprzężenie zwrotne od wyjścia O[x]",
            "Generator impulsów (Taktowanie)"
        };

        private static readonly string[] InputRoleLabelsEn = new string[]
        {
            "Neutral (Manual NO — state 0)",
            "Artificially Set (Constant NC/NO state)",
            "Feedback from Output O[x]",
            "Pulse Generator (Oscillator)"
        };

        private static readonly string[] OutputRoleLabelsPl = new string[]
        {
            "Wyjście ogólne (Brak specjalnej roli)",
            "Stycznik / Przekaźnik wykonawczy",
            "Element grzejny (Grzałka / SSR)",
            "Wentylator / Chłodzenie / Wyciąg",
            "Zawór / Nawilżanie / Przepustnica",
            "Sygnalizator / Alarm"
        };

        private static readonly string[] OutputRoleLabelsEn = new string[]
        {
            "General Output (No special role)",
            "Contactor / Relay",
            "Heating Element (Heater / SSR)",
            "Fan / Cooling / Exhaust",
            "Valve / Humidifier / Damper",
            "Signal Tower / Alarm Horn"
        };

        private static readonly string[] AnalogRoleLabelsPl = new string[]
        {
            "Neutralne (Pozycja zerowa = 0)",
            "Sztucznie ustawiona stała wartość",
            "Czujnik temperatury (Dynamika cieplna)",
            "Czujnik wilgotności (Dynamika wilgotności)",
            "Czujnik procesowy ogólny (Ciśnienie/Przepływ)"
        };

        private static readonly string[] AnalogRoleLabelsEn = new string[]
        {
            "Neutral (Zero position = 0)",
            "Artificially Set Constant Value",
            "Temperature Sensor (Thermal dynamics)",
            "Humidity Sensor (Humidity dynamics)",
            "Generic Process Sensor (Pressure/Flow)"
        };

        public SimulationSetupDialog(LoadedProject proj, HardwareProfile hw, ProjectSimulationProfile profile, bool isPostLoadPrompt)
        {
            this.project = proj;
            this.hwProfile = hw ?? HardwareProfile.CreateDefaultProfile();
            this.simProfile = profile ?? new ProjectSimulationProfile();

            InitializeComponent(isPostLoadPrompt);
            PopulateGridsFromProfile();
        }

        private bool IsPl
        {
            get { return Localization.CurrentLanguage == AppLanguage.Polish; }
        }

        private string[] GetInputRoleLabels() { return IsPl ? InputRoleLabelsPl : InputRoleLabelsEn; }
        private string[] GetOutputRoleLabels() { return IsPl ? OutputRoleLabelsPl : OutputRoleLabelsEn; }
        private string[] GetAnalogRoleLabels() { return IsPl ? AnalogRoleLabelsPl : AnalogRoleLabelsEn; }

        private void InitializeComponent(bool isPostLoadPrompt)
        {
            string projTitle = (project != null && !string.IsNullOrEmpty(project.ProjectName)) ? project.ProjectName : "VisiLogic OPLC";
            this.Text = IsPl
                ? string.Format("Konfiguracja symulacji projektu I/O — {0}", projTitle)
                : string.Format("Project I/O Simulation Setup — {0}", projTitle);

            this.Size = new Size(1120, 740);
            this.MinimumSize = new Size(960, 620);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            this.FormBorderStyle = FormBorderStyle.Sizable;

            // ==========================================
            // TOP HEADER PANEL (Mode Question & Actions)
            // ==========================================
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 152,
                BackColor = Color.FromArgb(30, 36, 46),
                Padding = new Padding(14, 10, 14, 10)
            };

            var lblBannerTitle = new Label
            {
                Text = isPostLoadPrompt
                    ? (IsPl
                        ? string.Format("Wgrano dane projektu \"{0}\". Sterownik został ustawiony w POZYCJI NEUTRALNEJ (wszystkie wejścia/wyjścia = 0).", projTitle)
                        : string.Format("Project \"{0}\" loaded. Controller is set to NEUTRAL POSITION (all inputs/outputs = 0).", projTitle))
                    : (IsPl
                        ? string.Format("Konfiguracja zachowania i ról wejść/wyjść dla projektu \"{0}\"", projTitle)
                        : string.Format("Configure I/O roles and simulation behavior for project \"{0}\"", projTitle)),
                Location = new Point(14, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 220, 255)
            };

            var lblQuestion = new Label
            {
                Text = IsPl
                    ? "Czy ten projekt ma być symulowany? Wybierz tryb pracy oraz określ, które wejścia mają być sztucznie ustawione i jak mają zachowywać się wejścia/wyjścia:"
                    : "Should this project be simulated? Select operating mode and specify which inputs should be artificially set and how I/O points behave:",
                Location = new Point(14, 34),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                ForeColor = Color.FromArgb(210, 220, 235)
            };

            rbNeutralMode = new RadioButton
            {
                Text = IsPl
                    ? "Pozycja neutralna / nieustawiona (Brak sztucznej symulacji — wszystkie wejścia I=0, AI=0, sterowanie wyłącznie ręczne)"
                    : "Neutral / unconfigured position (No artificial simulation — all inputs I=0, AI=0, manual control only)",
                Location = new Point(18, 60),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 215, 64),
                Checked = !simProfile.IsSimulationEnabled
            };

            rbSimulateMode = new RadioButton
            {
                Text = IsPl
                    ? "Symuluj ten projekt (Aktywuj sztucznie ustawione wejścia, role wejść/wyjść oraz sprzężenia procesowe poniżej)"
                    : "Simulate this project (Activate artificially set inputs, I/O roles, and process feedback rules configured below)",
                Location = new Point(18, 84),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 230, 118),
                Checked = simProfile.IsSimulationEnabled
            };

            btnAutoSuggest = new Button
            {
                Text = IsPl ? "🔍 Zaproponuj role i wejścia z symboli projektu" : "🔍 Auto-Suggest Roles & Inputs from Project Symbols",
                Location = new Point(18, 112),
                Size = new Size(330, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            btnAutoSuggest.FlatAppearance.BorderColor = Color.FromArgb(0, 160, 255);
            btnAutoSuggest.Click += (s, e) =>
            {
                simProfile.AutoSuggestFromProject(project, hwProfile);
                simProfile.IsSimulationEnabled = true;
                rbSimulateMode.Checked = true;
                PopulateGridsFromProfile();
            };

            btnResetNeutral = new Button
            {
                Text = IsPl ? "🧹 Wyzeruj wszystko do pozycji neutralnej (0)" : "🧹 Reset All to Neutral Position (0)",
                Location = new Point(358, 112),
                Size = new Size(280, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(65, 72, 85),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            btnResetNeutral.FlatAppearance.BorderColor = Color.FromArgb(100, 110, 130);
            btnResetNeutral.Click += (s, e) =>
            {
                simProfile.ResetToNeutral(hwProfile, project);
                rbNeutralMode.Checked = true;
                PopulateGridsFromProfile();
            };

            btnLoadProfile = new Button
            {
                Text = IsPl ? "📂 Wczytaj profil..." : "📂 Load Profile...",
                Location = new Point(648, 112),
                Size = new Size(145, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 58, 70),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            btnLoadProfile.FlatAppearance.BorderColor = Color.FromArgb(90, 100, 120);
            btnLoadProfile.Click += (s, e) => LoadProfileFromFileDialog();

            btnSaveProfile = new Button
            {
                Text = IsPl ? "💾 Zapisz profil..." : "💾 Save Profile...",
                Location = new Point(800, 112),
                Size = new Size(145, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 58, 70),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            btnSaveProfile.FlatAppearance.BorderColor = Color.FromArgb(90, 100, 120);
            btnSaveProfile.Click += (s, e) => SaveProfileToFileDialog();

            pnlTop.Controls.AddRange(new Control[]
            {
                lblBannerTitle,
                lblQuestion,
                rbNeutralMode,
                rbSimulateMode,
                btnAutoSuggest,
                btnResetNeutral,
                btnLoadProfile,
                btnSaveProfile
            });

            // ==========================================
            // CENTER TAB CONTROL (DI, DO, AI/MI)
            // ==========================================
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            var tabInputs = new TabPage(IsPl
                ? "1. Wejścia cyfrowe (I) — Sztuczne ustawienia i zachowanie"
                : "1. Digital Inputs (I) — Artificial States & Behavior")
            {
                BackColor = Color.FromArgb(24, 28, 36),
                Padding = new Padding(8)
            };
            BuildInputsTab(tabInputs);

            var tabOutputs = new TabPage(IsPl
                ? "2. Wyjścia cyfrowe (O) — Role elementów wykonawczych"
                : "2. Digital Outputs (O) — Actuator Roles")
            {
                BackColor = Color.FromArgb(24, 28, 36),
                Padding = new Padding(8)
            };
            BuildOutputsTab(tabOutputs);

            var tabAnalogs = new TabPage(IsPl
                ? "3. Wejścia analogowe (AI / MI) — Czujniki i dynamika procesu"
                : "3. Analog Inputs (AI / MI) — Sensors & Process Dynamics")
            {
                BackColor = Color.FromArgb(24, 28, 36),
                Padding = new Padding(8)
            };
            BuildAnalogsTab(tabAnalogs);

            tabControl.TabPages.Add(tabInputs);
            tabControl.TabPages.Add(tabOutputs);
            tabControl.TabPages.Add(tabAnalogs);

            // ==========================================
            // BOTTOM BUTTON BAR
            // ==========================================
            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                BackColor = Color.FromArgb(30, 36, 46),
                Padding = new Padding(14, 10, 14, 10)
            };

            btnStayNeutral = new Button
            {
                Text = IsPl
                    ? "⏸ Uruchom w pozycji neutralnej (Brak symulacji — wszystko = 0)"
                    : "⏸ Run in Neutral Position (No simulation — all = 0)",
                Location = new Point(14, 10),
                Size = new Size(410, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(75, 82, 96),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnStayNeutral.FlatAppearance.BorderColor = Color.FromArgb(120, 130, 150);
            btnStayNeutral.Click += (s, e) =>
            {
                simProfile.ResetToNeutral(hwProfile, project);
                simProfile.IsSimulationEnabled = false;
                simProfile.HasUserConfigured = true;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnApply = new Button
            {
                Text = IsPl
                    ? "✔ Zastosuj wybraną konfigurację i uruchom"
                    : "✔ Apply Selected Configuration & Continue",
                Location = new Point(440, 10),
                Size = new Size(360, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            btnApply.FlatAppearance.BorderColor = Color.FromArgb(0, 230, 118);
            btnApply.Click += (s, e) =>
            {
                CommitGridsToProfile();
                simProfile.HasUserConfigured = true;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            pnlBottom.Controls.Add(btnStayNeutral);
            pnlBottom.Controls.Add(btnApply);

            this.Controls.Add(tabControl);
            this.Controls.Add(pnlTop);
            this.Controls.Add(pnlBottom);
        }

        private void BuildInputsTab(TabPage page)
        {
            var pnlActions = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(28, 34, 44)
            };

            var btnSetNcBySymbol = new Button
            {
                Text = IsPl ? "Zaznacz wejścia '_NC' / bezpieczeństwa jako Sztucznie ustawione (1)" : "Set '_NC' / safety inputs as Artificially Set (1)",
                Location = new Point(6, 4),
                Size = new Size(410, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 85, 130),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular)
            };
            btnSetNcBySymbol.Click += (s, e) =>
            {
                string[] roleLabels = GetInputRoleLabels();
                foreach (DataGridViewRow row in dgvInputs.Rows)
                {
                    string sym = Convert.ToString(row.Cells[1].Value ?? "").ToLowerInvariant();
                    if (sym.EndsWith("_nc") || sym.Contains("_nc_") || sym.Contains("termik") ||
                        sym.Contains("bezpiecz") || sym.Contains("stop") || sym.Contains("drzwi") ||
                        sym.Contains("presostat") || sym.Contains("gotowosc") || sym.Contains("zasilanie"))
                    {
                        row.Cells[2].Value = roleLabels[(int)DigitalInputRole.Forced_Constant];
                        row.Cells[3].Value = true;
                    }
                }
                rbSimulateMode.Checked = true;
            };

            var btnClearAllInputs = new Button
            {
                Text = IsPl ? "Ustaw wszystkie wejścia na Neutralne (0)" : "Set all inputs to Neutral (0)",
                Location = new Point(424, 4),
                Size = new Size(250, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular)
            };
            btnClearAllInputs.Click += (s, e) =>
            {
                string[] roleLabels = GetInputRoleLabels();
                foreach (DataGridViewRow row in dgvInputs.Rows)
                {
                    row.Cells[2].Value = roleLabels[(int)DigitalInputRole.Neutral_Manual];
                    row.Cells[3].Value = false;
                    row.Cells[4].Value = "(Brak)";
                    row.Cells[5].Value = false;
                }
            };

            pnlActions.Controls.Add(btnSetNcBySymbol);
            pnlActions.Controls.Add(btnClearAllInputs);

            dgvInputs = CreateStyledGrid();
            dgvInputs.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ColAddr",
                HeaderText = IsPl ? "Wejście" : "Input",
                Width = 75,
                ReadOnly = true
            });
            dgvInputs.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ColSymbol",
                HeaderText = IsPl ? "Symbol / Opis w projekcie" : "Project Symbol / Description",
                Width = 240,
                ReadOnly = true
            });

            var colRole = new DataGridViewComboBoxColumn
            {
                Name = "ColRole",
                HeaderText = IsPl ? "Rola i sposób zachowania wejścia" : "Input Role & Behavior",
                Width = 270,
                FlatStyle = FlatStyle.Flat
            };
            colRole.Items.AddRange(GetInputRoleLabels());
            dgvInputs.Columns.Add(colRole);

            dgvInputs.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "ColForcedState",
                HeaderText = IsPl ? "Sztucznie ustawiony stan (1=ON)" : "Artificially Set State (1=ON)",
                Width = 135
            });

            var colLinkedOut = new DataGridViewComboBoxColumn
            {
                Name = "ColLinkedOut",
                HeaderText = IsPl ? "Powiązane wyjście O[x]" : "Linked Output O[x]",
                Width = 145,
                FlatStyle = FlatStyle.Flat
            };
            colLinkedOut.Items.Add("(Brak)");
            List<int> outAddrs = ProjectSimulationProfile.GetActiveDigitalOutputAddresses(hwProfile, project);
            foreach (int oa in outAddrs)
            {
                colLinkedOut.Items.Add("O" + oa);
            }
            dgvInputs.Columns.Add(colLinkedOut);

            dgvInputs.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "ColInvert",
                HeaderText = IsPl ? "Negacja (!O[x])" : "Invert (!O[x])",
                Width = 95
            });

            dgvInputs.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ColDelay",
                HeaderText = IsPl ? "Opóźnienie / Okres [ms]" : "Delay / Period [ms]",
                Width = 125
            });

            dgvInputs.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && (e.ColumnIndex == 2 || e.ColumnIndex == 3 || e.ColumnIndex == 4))
                {
                    if (e.ColumnIndex == 3 && Convert.ToBoolean(dgvInputs.Rows[e.RowIndex].Cells[3].Value ?? false))
                    {
                        string curRole = Convert.ToString(dgvInputs.Rows[e.RowIndex].Cells[2].Value ?? "");
                        string[] labels = GetInputRoleLabels();
                        if (curRole == labels[0])
                        {
                            dgvInputs.Rows[e.RowIndex].Cells[2].Value = labels[(int)DigitalInputRole.Forced_Constant];
                        }
                        rbSimulateMode.Checked = true;
                    }
                }
            };
            dgvInputs.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (dgvInputs.IsCurrentCellDirty) dgvInputs.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            page.Controls.Add(dgvInputs);
            page.Controls.Add(pnlActions);
        }

        private void BuildOutputsTab(TabPage page)
        {
            var pnlActions = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(28, 34, 44)
            };

            var btnSyncToAnalog = new Button
            {
                Text = IsPl
                    ? "🔗 Przekaż wybrane role wyjść (Grzałki / Wentylatory / Zawory) do tabeli wejść analogowych (Zakładka 3)"
                    : "🔗 Link Selected Output Roles (Heaters / Fans / Valves) to Analog Inputs Table (Tab 3)",
                Location = new Point(6, 4),
                Size = new Size(640, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold)
            };
            btnSyncToAnalog.Click += (s, e) => SyncOutputRolesToAnalogGrid();

            pnlActions.Controls.Add(btnSyncToAnalog);

            dgvOutputs = CreateStyledGrid();
            dgvOutputs.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ColOutAddr",
                HeaderText = IsPl ? "Wyjście" : "Output",
                Width = 90,
                ReadOnly = true
            });
            dgvOutputs.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ColOutSymbol",
                HeaderText = IsPl ? "Symbol / Opis w projekcie" : "Project Symbol / Description",
                Width = 320,
                ReadOnly = true
            });

            var colOutRole = new DataGridViewComboBoxColumn
            {
                Name = "ColOutRole",
                HeaderText = IsPl ? "Rola wyjścia w symulowanym obiekcie" : "Output Role in Simulated Plant",
                Width = 340,
                FlatStyle = FlatStyle.Flat
            };
            colOutRole.Items.AddRange(GetOutputRoleLabels());
            dgvOutputs.Columns.Add(colOutRole);

            dgvOutputs.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (dgvOutputs.IsCurrentCellDirty) dgvOutputs.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            page.Controls.Add(dgvOutputs);
            page.Controls.Add(pnlActions);
        }

        private void BuildAnalogsTab(TabPage page)
        {
            var pnlActions = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(28, 34, 44)
            };

            var btnAddChannel = new Button
            {
                Text = IsPl ? "➕ Dodaj kanał analogowy / rejestr MI" : "➕ Add Analog Channel / MI Register",
                Location = new Point(6, 4),
                Size = new Size(260, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold)
            };
            btnAddChannel.Click += (s, e) =>
            {
                int nextCh = dgvAnalogs.Rows.Count;
                int nextMi = 100 + nextCh;
                string[] roleLabels = GetAnalogRoleLabels();
                dgvAnalogs.Rows.Add(
                    "AI" + nextCh,
                    nextMi.ToString(),
                    ProjectSimulationProfile.GetOperandDescription(project, "MI" + nextMi),
                    roleLabels[(int)AnalogInputRole.Neutral_Zero],
                    "0", "0", "0", "4095", "", "2.0", "", "1.0");
            };

            var btnZeroAnalogs = new Button
            {
                Text = IsPl ? "Wyzeruj wszystkie wejścia analogowe (Pozycja 0)" : "Zero All Analog Inputs (Position 0)",
                Location = new Point(276, 4),
                Size = new Size(290, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular)
            };
            btnZeroAnalogs.Click += (s, e) =>
            {
                string[] roleLabels = GetAnalogRoleLabels();
                foreach (DataGridViewRow row in dgvAnalogs.Rows)
                {
                    row.Cells[3].Value = roleLabels[(int)AnalogInputRole.Neutral_Zero];
                    row.Cells[4].Value = "0";
                    row.Cells[5].Value = "0";
                    row.Cells[8].Value = "";
                    row.Cells[10].Value = "";
                }
            };

            pnlActions.Controls.Add(btnAddChannel);
            pnlActions.Controls.Add(btnZeroAnalogs);

            dgvAnalogs = CreateStyledGrid();
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColAiCh", HeaderText = IsPl ? "Kanał" : "Ch", Width = 55, ReadOnly = true });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColTargetMi", HeaderText = IsPl ? "Docelowe MI" : "Target MI", Width = 80 });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColAiSymbol", HeaderText = IsPl ? "Symbol MI/AN" : "MI/AN Symbol", Width = 165, ReadOnly = true });

            var colAiRole = new DataGridViewComboBoxColumn
            {
                Name = "ColAiRole",
                HeaderText = IsPl ? "Rola i zachowanie wejścia analogowego" : "Analog Input Role & Behavior",
                Width = 230,
                FlatStyle = FlatStyle.Flat
            };
            colAiRole.Items.AddRange(GetAnalogRoleLabels());
            dgvAnalogs.Columns.Add(colAiRole);

            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColInitVal", HeaderText = IsPl ? "Wart. początkowa" : "Initial Val", Width = 85 });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColAmbVal", HeaderText = IsPl ? "Otoczenie" : "Ambient", Width = 75 });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColMinVal", HeaderText = "Min", Width = 55 });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColMaxVal", HeaderText = "Max", Width = 60 });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColIncOut", HeaderText = IsPl ? "Wyjścia zwiększające (np. O33-O38)" : "Increasing Outputs (e.g. O33-O38)", Width = 140 });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColIncRate", HeaderText = IsPl ? "Wzrost/s" : "Inc/s", Width = 65 });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColDecOut", HeaderText = IsPl ? "Wyjścia zmniejszające (np. O3-O8)" : "Decreasing Outputs (e.g. O3-O8)", Width = 140 });
            dgvAnalogs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColDecRate", HeaderText = IsPl ? "Spadek/s" : "Dec/s", Width = 65 });

            dgvAnalogs.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 1)
                {
                    int miAddr;
                    string raw = Convert.ToString(dgvAnalogs.Rows[e.RowIndex].Cells[1].Value ?? "").Trim().ToUpperInvariant().TrimStart('M', 'I');
                    if (int.TryParse(raw, out miAddr))
                    {
                        dgvAnalogs.Rows[e.RowIndex].Cells[2].Value = ProjectSimulationProfile.GetOperandDescription(project, "MI" + miAddr);
                    }
                }
            };
            dgvAnalogs.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (dgvAnalogs.IsCurrentCellDirty) dgvAnalogs.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            page.Controls.Add(dgvAnalogs);
            page.Controls.Add(pnlActions);
        }

        private DataGridView CreateStyledGrid()
        {
            var dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(20, 24, 32),
                ForeColor = Color.Black,
                GridColor = Color.FromArgb(55, 65, 80),
                BorderStyle = BorderStyle.FixedSingle,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            };
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(36, 44, 58);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            dgv.EnableHeadersVisualStyles = false;
            dgv.DefaultCellStyle.BackColor = Color.FromArgb(238, 242, 248);
            dgv.DefaultCellStyle.ForeColor = Color.FromArgb(15, 20, 30);
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(180, 215, 255);
            dgv.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgv.DefaultCellStyle.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            return dgv;
        }

        private void PopulateGridsFromProfile()
        {
            rbNeutralMode.Checked = !simProfile.IsSimulationEnabled;
            rbSimulateMode.Checked = simProfile.IsSimulationEnabled;

            // 1. Digital Inputs
            dgvInputs.Rows.Clear();
            string[] inRoleLabels = GetInputRoleLabels();
            var inKeys = new List<int>(simProfile.InputRules.Keys);
            inKeys.Sort();

            var colLinked = (DataGridViewComboBoxColumn)dgvInputs.Columns["ColLinkedOut"];

            foreach (int addr in inKeys)
            {
                var r = simProfile.InputRules[addr];
                string desc = ProjectSimulationProfile.GetOperandDescription(project, "I" + addr);
                string roleStr = inRoleLabels[(int)r.Role];
                string linkedStr = (r.LinkedOutputAddress >= 0) ? ("O" + r.LinkedOutputAddress) : "(Brak)";
                if (!colLinked.Items.Contains(linkedStr))
                {
                    colLinked.Items.Add(linkedStr);
                }

                dgvInputs.Rows.Add(
                    "I" + addr,
                    desc,
                    roleStr,
                    r.ForcedState,
                    linkedStr,
                    r.InvertFeedback,
                    r.DelayMs.ToString());
            }

            // 2. Digital Outputs
            dgvOutputs.Rows.Clear();
            string[] outRoleLabels = GetOutputRoleLabels();
            var outKeys = new List<int>(simProfile.OutputRules.Keys);
            outKeys.Sort();

            foreach (int addr in outKeys)
            {
                var r = simProfile.OutputRules[addr];
                string desc = ProjectSimulationProfile.GetOperandDescription(project, "O" + addr);
                string roleStr = outRoleLabels[(int)r.Role];
                dgvOutputs.Rows.Add("O" + addr, desc, roleStr);
            }

            // 3. Analog Inputs
            dgvAnalogs.Rows.Clear();
            string[] aiRoleLabels = GetAnalogRoleLabels();
            var aiKeys = new List<int>(simProfile.AnalogRules.Keys);
            aiKeys.Sort();

            foreach (int ch in aiKeys)
            {
                var r = simProfile.AnalogRules[ch];
                string desc = ProjectSimulationProfile.GetOperandDescription(project, "MI" + r.TargetMiAddress);
                if (string.IsNullOrEmpty(desc))
                {
                    desc = ProjectSimulationProfile.GetOperandDescription(project, "AN" + ch);
                }
                string roleStr = aiRoleLabels[(int)r.Role];

                dgvAnalogs.Rows.Add(
                    "AI" + ch,
                    r.TargetMiAddress.ToString(),
                    desc,
                    roleStr,
                    r.InitialValue.ToString(),
                    r.AmbientValue.ToString(),
                    r.MinValue.ToString(),
                    r.MaxValue.ToString(),
                    r.IncreasingOutputsSpec ?? "",
                    r.IncreaseRatePerSec.ToString("0.##", CultureInfo.InvariantCulture),
                    r.DecreasingOutputsSpec ?? "",
                    r.DecreaseRatePerSec.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        private void SyncOutputRolesToAnalogGrid()
        {
            var heaters = new List<string>();
            var coolers = new List<string>();
            var valves = new List<string>();
            string[] outLabels = GetOutputRoleLabels();

            foreach (DataGridViewRow row in dgvOutputs.Rows)
            {
                string outName = Convert.ToString(row.Cells[0].Value ?? "");
                string roleStr = Convert.ToString(row.Cells[2].Value ?? "");
                int roleIdx = Array.IndexOf(outLabels, roleStr);
                if (roleIdx == (int)DigitalOutputRole.Heater_Thermal) heaters.Add(outName);
                else if (roleIdx == (int)DigitalOutputRole.Cooler_Fan) coolers.Add(outName);
                else if (roleIdx == (int)DigitalOutputRole.Valve_Humidifier) valves.Add(outName);
            }

            string heaterSpec = string.Join(", ", heaters.ToArray());
            string coolerSpec = string.Join(", ", coolers.ToArray());
            string valveSpec = string.Join(", ", valves.ToArray());

            string[] aiLabels = GetAnalogRoleLabels();
            foreach (DataGridViewRow row in dgvAnalogs.Rows)
            {
                string roleStr = Convert.ToString(row.Cells[3].Value ?? "");
                int roleIdx = Array.IndexOf(aiLabels, roleStr);
                if (roleIdx == (int)AnalogInputRole.Process_Temperature)
                {
                    row.Cells[8].Value = heaterSpec;
                    row.Cells[10].Value = coolerSpec;
                }
                else if (roleIdx == (int)AnalogInputRole.Process_Humidity)
                {
                    row.Cells[8].Value = valveSpec;
                    row.Cells[10].Value = !string.IsNullOrEmpty(coolerSpec) ? coolerSpec : heaterSpec;
                }
            }

            tabControl.SelectedIndex = 2;
        }

        private void CommitGridsToProfile()
        {
            simProfile.IsSimulationEnabled = rbSimulateMode.Checked;

            // 1. Commit Digital Inputs
            string[] inRoleLabels = GetInputRoleLabels();
            simProfile.InputRules.Clear();

            foreach (DataGridViewRow row in dgvInputs.Rows)
            {
                string addrStr = Convert.ToString(row.Cells[0].Value ?? "").TrimStart('I');
                int addr;
                if (!int.TryParse(addrStr, out addr)) continue;

                string roleStr = Convert.ToString(row.Cells[2].Value ?? "");
                int roleIdx = Array.IndexOf(inRoleLabels, roleStr);
                if (roleIdx < 0) roleIdx = 0;

                bool forced = Convert.ToBoolean(row.Cells[3].Value ?? false);
                string linkedStr = Convert.ToString(row.Cells[4].Value ?? "").Trim();
                int linkedOut = -1;
                if (linkedStr.StartsWith("O"))
                {
                    int.TryParse(linkedStr.Substring(1), out linkedOut);
                }

                bool invert = Convert.ToBoolean(row.Cells[5].Value ?? false);
                int delay = 100;
                int.TryParse(Convert.ToString(row.Cells[6].Value ?? "100"), out delay);

                // If user checked ForcedState while Role was still Neutral, promote Role to Forced_Constant
                DigitalInputRole role = (DigitalInputRole)roleIdx;
                if (role == DigitalInputRole.Neutral_Manual && forced)
                {
                    role = DigitalInputRole.Forced_Constant;
                    simProfile.IsSimulationEnabled = true;
                }
                else if (role != DigitalInputRole.Neutral_Manual)
                {
                    simProfile.IsSimulationEnabled = true;
                }

                simProfile.InputRules[addr] = new DigitalInputRule
                {
                    Address = addr,
                    Role = role,
                    ForcedState = forced,
                    LinkedOutputAddress = linkedOut,
                    InvertFeedback = invert,
                    DelayMs = Math.Max(0, delay)
                };
            }

            // 2. Commit Digital Outputs
            string[] outRoleLabels = GetOutputRoleLabels();
            simProfile.OutputRules.Clear();
            foreach (DataGridViewRow row in dgvOutputs.Rows)
            {
                string addrStr = Convert.ToString(row.Cells[0].Value ?? "").TrimStart('O');
                int addr;
                if (!int.TryParse(addrStr, out addr)) continue;

                string roleStr = Convert.ToString(row.Cells[2].Value ?? "");
                int roleIdx = Array.IndexOf(outRoleLabels, roleStr);
                if (roleIdx < 0) roleIdx = 0;

                simProfile.OutputRules[addr] = new DigitalOutputRule
                {
                    Address = addr,
                    Role = (DigitalOutputRole)roleIdx
                };
            }

            // 3. Commit Analog Inputs
            string[] aiRoleLabels = GetAnalogRoleLabels();
            simProfile.AnalogRules.Clear();
            for (int i = 0; i < dgvAnalogs.Rows.Count; i++)
            {
                DataGridViewRow row = dgvAnalogs.Rows[i];
                int targetMi = 100 + i;
                string miStr = Convert.ToString(row.Cells[1].Value ?? "").Trim().ToUpperInvariant().TrimStart('M', 'I');
                int.TryParse(miStr, out targetMi);

                string roleStr = Convert.ToString(row.Cells[3].Value ?? "");
                int roleIdx = Array.IndexOf(aiRoleLabels, roleStr);
                if (roleIdx < 0) roleIdx = 0;

                short initVal = 0, ambVal = 0, minVal = 0, maxVal = 4095;
                short.TryParse(Convert.ToString(row.Cells[4].Value ?? "0"), out initVal);
                short.TryParse(Convert.ToString(row.Cells[5].Value ?? "0"), out ambVal);
                short.TryParse(Convert.ToString(row.Cells[6].Value ?? "0"), out minVal);
                short.TryParse(Convert.ToString(row.Cells[7].Value ?? "4095"), out maxVal);

                string incSpec = Convert.ToString(row.Cells[8].Value ?? "").Trim();
                double incRate = 2.0;
                double.TryParse(Convert.ToString(row.Cells[9].Value ?? "2.0").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out incRate);

                string decSpec = Convert.ToString(row.Cells[10].Value ?? "").Trim();
                double decRate = 1.0;
                double.TryParse(Convert.ToString(row.Cells[11].Value ?? "1.0").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out decRate);

                AnalogInputRole role = (AnalogInputRole)roleIdx;
                if (role == AnalogInputRole.Neutral_Zero && initVal != 0)
                {
                    role = AnalogInputRole.Forced_Constant;
                    simProfile.IsSimulationEnabled = true;
                }
                else if (role != AnalogInputRole.Neutral_Zero)
                {
                    simProfile.IsSimulationEnabled = true;
                }

                simProfile.AnalogRules[i] = new AnalogInputRule
                {
                    ChannelIndex = i,
                    TargetMiAddress = targetMi,
                    Role = role,
                    InitialValue = initVal,
                    AmbientValue = ambVal,
                    MinValue = minVal,
                    MaxValue = maxVal,
                    IncreasingOutputsSpec = incSpec,
                    IncreaseRatePerSec = incRate,
                    DecreasingOutputsSpec = decSpec,
                    DecreaseRatePerSec = decRate,
                    AmbientDriftRatePerSec = 0.25,
                    CurrentPreciseValue = initVal,
                    RuntimeInitialized = true
                };
            }
        }

        private void LoadProfileFromFileDialog()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "PLC Simulation Profile (*.simprofile)|*.simprofile|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    simProfile.LoadFromFile(ofd.FileName);
                    PopulateGridsFromProfile();
                }
            }
        }

        private void SaveProfileToFileDialog()
        {
            CommitGridsToProfile();
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "PLC Simulation Profile (*.simprofile)|*.simprofile|All Files (*.*)|*.*";
                if (project != null && !string.IsNullOrEmpty(project.ProjectName))
                {
                    sfd.FileName = project.ProjectName + ".simprofile";
                }
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    simProfile.SaveToFile(sfd.FileName);
                }
            }
        }
    }
}
