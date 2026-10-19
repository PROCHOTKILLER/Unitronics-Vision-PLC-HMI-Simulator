using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace UnitronicsV350Simulator
{
    public class MainForm : Form
    {
        // Core simulator components
        private PlcEngine plc;
        private HmiRenderer hmiRenderer;
        private PcomServer pcomServer;
        private HardwarePanel hardwarePanel;
        private SimulatorConfig config;

        // PLC background scan loop thread
        private Thread scanThread;
        private volatile bool isScanThreadRunning = true;

        // GUI refresh timer (~30 FPS)
        private System.Windows.Forms.Timer uiTimer;

        // OPLC front bezel controls
        private PictureBox picScreen;
        private Label lblPwrLed;
        private Label lblRunLed;
        private Label lblErrLed;
        private Label lblLogoModel;
        private Label lblScreenCaption;
        private Label lblTouchHint;

        // Menu strip controls
        private MenuStrip menuStrip;
        private ToolStripMenuItem mnuFile;
        private ToolStripMenuItem mnuLoad;
        private ToolStripMenuItem mnuClear;
        private ToolStripMenuItem mnuExit;

        private ToolStripMenuItem mnuPlc;
        private ToolStripMenuItem mnuRun;
        private ToolStripMenuItem mnuStop;
        private ToolStripMenuItem mnuReset;
        private ToolStripMenuItem mnuSimSetup;

        private ToolStripMenuItem mnuHardware;
        private ToolStripMenuItem mnuHardwareConfig;
        private ToolStripMenuItem mnuPresetExpanded;
        private ToolStripMenuItem mnuPresetV350;
        private ToolStripMenuItem mnuPresetV570;
        private ToolStripMenuItem mnuPresetV1040;

        private ToolStripMenuItem mnuSettings;
        private ToolStripMenuItem mnuSettingsGeneral;
        private ToolStripMenuItem mnuLanguage;
        private ToolStripMenuItem mnuLangPl;
        private ToolStripMenuItem mnuLangEn;

        private ToolStripMenuItem mnuLogging;
        private ToolStripMenuItem mnuLogEnabled;
        private ToolStripMenuItem mnuLogLevel;
        private ToolStripMenuItem mnuLogLevelError;
        private ToolStripMenuItem mnuLogLevelWarn;
        private ToolStripMenuItem mnuLogLevelInfo;
        private ToolStripMenuItem mnuLogLevelDebug;
        private ToolStripMenuItem mnuLogLevelTrace;
        private ToolStripMenuItem mnuOpenLogFile;
        private ToolStripMenuItem mnuClearLogFile;

        private ToolStripMenuItem mnuPcom;
        private ToolStripMenuItem mnuPcomStart;
        private ToolStripMenuItem mnuPcomStop;

        private ToolStripMenuItem mnuHelp;
        private ToolStripMenuItem mnuOpenLog;
        private ToolStripMenuItem mnuInfo;
        private ToolStripMenuItem mnuAbout;

        // Toolbar controls
        private ToolStrip toolStrip;
        private ToolStripButton btnToolbarLoad;
        private ToolStripButton btnToolbarClear;
        private ToolStripButton btnToolbarRun;
        private ToolStripButton btnToolbarStop;
        private ToolStripButton btnToolbarReset;
        private ToolStripButton btnToolbarSimSetup;
        private ToolStripButton btnToolbarHw;
        private ToolStripButton btnToolbarSettings;

        // Status bar controls
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusPlcState;
        private ToolStripStatusLabel statusScanTime;
        private ToolStripStatusLabel statusPcom;
        private ToolStripStatusLabel statusActivePage;
        private ToolStripStatusLabel statusHwProfile;
        private ToolStripStatusLabel statusProjectName;

        // Main tab controls
        private TabControl tabControlMain;
        private TabPage tabIo;
        private TabPage tabWatch;
        private TabPage tabLadder;
        private TabPage tabHmi;
        private TabPage tabLog;

        private DataGridView dgvWatch;
        private ComboBox cboWatchFilter;
        private TextBox txtWatchSearch;
        private Label lblWatchFilt;
        private Label lblWatchSearch;
        private Button btnWatchForce;

        private TreeView tvSubroutines;
        private RichTextBox rtbLadderView;
        private ListView lvHmiPages;
        private RichTextBox rtbLog;

        // Watch table items
        private class WatchItem
        {
            public string Type;
            public int Address;
            public string Name;
            public string Format;
        }
        private List<WatchItem> allWatchItems = new List<WatchItem>();
        private List<WatchItem> filteredWatchItems = new List<WatchItem>();
        private bool isSimSetupDialogOpen = false;
        private bool isLoadingProject = false;

        public MainForm(SimulatorConfig cfg = null)
        {
            config = cfg ?? SimulatorConfig.Current;
            plc = new PlcEngine(config.Profile);
            hmiRenderer = new HmiRenderer(plc);
            pcomServer = new PcomServer(plc);
            hardwarePanel = new HardwarePanel(plc);

            InitializeComponent();
            ApplyLanguage();

            Localization.OnLanguageChanged += ApplyLanguage;

            plc.OnLogMessage += HandlePlcLog;

            // Start PCOM TCP server on configured port
            int port = (config.PcomPort > 0) ? config.PcomPort : PcomServer.DEFAULT_PORT;
            pcomServer.Start(port);
            pcomServer.OnProjectDownloaded += (projPath) =>
            {
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        if (!isSimSetupDialogOpen && !isLoadingProject)
                        {
                            LoadProjectFile(projPath, true);
                        }
                    }));
                }
                catch { }
            };

            // Start real-time background worker thread for the PLC scan loop
            scanThread = new Thread(ScanWorkerLoop);
            scanThread.IsBackground = true;
            scanThread.Priority = ThreadPriority.AboveNormal;
            scanThread.Start();

            // Start UI refresh timer (33 ms = ~30 FPS)
            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 33;
            uiTimer.Tick += OnUiTimerTick;
            uiTimer.Start();

            this.FormClosing += MainForm_FormClosing;

            string hwName = config.Profile != null ? config.Profile.GetModelFullName() : "V350";
            plc.Log(string.Format("Simulator initialization complete ({0}).", hwName));
            if (plc.Memory != null)
            {
                plc.Log(string.Format("Simulated PLC memory files ready: ROM='{0}' (4 MB), RAM='{1}' (256 KB).",
                    plc.Memory.RomFilePath, plc.Memory.RamFilePath));
            }

            string savedProjPath = (plc.Memory != null) ? plc.Memory.GetPersistedProjectPath() : null;
            if (!string.IsNullOrEmpty(savedProjPath) && File.Exists(savedProjPath))
            {
                plc.Log("Restoring persisted project from rom.bin in neutral state: " + savedProjPath);
                LoadProjectFile(savedProjPath, false);
            }
            else
            {
                plc.Log("Controller is in clean factory standby state (OPLC Standby).");
                plc.Log(string.Format("Use 'Load VisiLogic Project' or connect via PCOM TCP port {0}.", port));
            }
        }

        private void InitializeComponent()
        {
            this.Size = new Size(1340, 860);
            this.MinimumSize = new Size(1100, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            this.KeyPreview = true;

            // ==========================================
            // MAIN MENU BAR (MenuStrip)
            // ==========================================
            menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(32, 38, 48),
                ForeColor = Color.White,
                RenderMode = ToolStripRenderMode.System
            };

            // 1. Menu: File
            mnuFile = new ToolStripMenuItem("File");
            mnuLoad = new ToolStripMenuItem("Load VisiLogic Project (*.vlp, *.vlx, *.udb)...", null, (s, e) => OpenProjectDialog());
            mnuClear = new ToolStripMenuItem("Clear Program (Factory Standby)", null, (s, e) => ClearProject());
            mnuExit = new ToolStripMenuItem("Exit", null, (s, e) => this.Close());
            mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuLoad, new ToolStripSeparator(), mnuClear, new ToolStripSeparator(), mnuExit });

            // 2. Menu: PLC Control
            mnuPlc = new ToolStripMenuItem("PLC Control");
            mnuRun = new ToolStripMenuItem("START / RUN (F5)", null, (s, e) => StartPlc());
            mnuStop = new ToolStripMenuItem("STOP (F6)", null, (s, e) => StopPlc());
            mnuReset = new ToolStripMenuItem("RESET (F7)", null, (s, e) => ResetPlc());
            mnuSimSetup = new ToolStripMenuItem("I/O & Process Simulation Setup (F8)...", null, (s, e) => OpenSimulationSetupDialog(false));
            mnuPlc.DropDownItems.AddRange(new ToolStripItem[] { mnuRun, mnuStop, mnuReset, new ToolStripSeparator(), mnuSimSetup });

            // 3. Menu: Hardware
            mnuHardware = new ToolStripMenuItem("Hardware");
            mnuHardwareConfig = new ToolStripMenuItem("Controller & I/O Hardware Configuration...", null, (s, e) => OpenHardwareConfigDialog());
            mnuPresetExpanded = new ToolStripMenuItem("Load Preset: V350 + IO-DI8-RO8 (Expanded)", null, (s, e) => ApplyHardwareProfile(HardwareProfile.CreateExpandedV350Profile()));
            mnuPresetV350 = new ToolStripMenuItem("Load Preset: V350 Standalone (Default)", null, (s, e) => ApplyHardwareProfile(HardwareProfile.CreateDefaultProfile()));
            mnuPresetV570 = new ToolStripMenuItem("Load Preset: V570 Modular + V200-18-E3XB");
            mnuPresetV570.Click += (s, e) =>
            {
                var p = new HardwareProfile();
                p.SelectedModelName = "V570-57-T20B";
                p.SelectedSnapInName = "V200-18-E3XB";
                p.EnableExpansionBus = true;
                p.ExpansionSlots[0].ModuleName = "IO-DI8-RO8";
                ApplyHardwareProfile(p);
            };
            mnuPresetV1040 = new ToolStripMenuItem("Load Preset: V1040 Color (800x600)");
            mnuPresetV1040.Click += (s, e) =>
            {
                var p = new HardwareProfile();
                p.SelectedModelName = "V1040-20-T20B";
                p.SelectedSnapInName = "V200-18-E4XB";
                p.EnableExpansionBus = false;
                ApplyHardwareProfile(p);
            };
            mnuHardware.DropDownItems.AddRange(new ToolStripItem[] {
                mnuHardwareConfig,
                new ToolStripSeparator(),
                mnuPresetExpanded,
                mnuPresetV350,
                mnuPresetV570,
                mnuPresetV1040
            });

            // 4. Menu: Settings
            mnuSettings = new ToolStripMenuItem("Settings");
            mnuSettingsGeneral = new ToolStripMenuItem("Simulator Settings...", null, (s, e) => OpenSettingsDialog());

            // Submenu: Language
            mnuLanguage = new ToolStripMenuItem("Language");
            mnuLangPl = new ToolStripMenuItem("Polski (Polish)", null, (s, e) => SetLanguage(AppLanguage.Polish));
            mnuLangEn = new ToolStripMenuItem("English", null, (s, e) => SetLanguage(AppLanguage.English));
            mnuLanguage.DropDownItems.AddRange(new ToolStripItem[] { mnuLangPl, mnuLangEn });

            // Submenu: Logging
            mnuLogging = new ToolStripMenuItem("Diagnostic Logging");
            mnuLogEnabled = new ToolStripMenuItem("Enable File Logging", null, (s, e) => ToggleLogging(!config.LoggingEnabled)) { CheckOnClick = false };
            mnuLogLevel = new ToolStripMenuItem("Log Verbosity");
            mnuLogLevelError = new ToolStripMenuItem("Errors Only (Error)", null, (s, e) => SetLogLevel(LogVerbosity.Error));
            mnuLogLevelWarn = new ToolStripMenuItem("Warnings & Errors (Warn)", null, (s, e) => SetLogLevel(LogVerbosity.Warn));
            mnuLogLevelInfo = new ToolStripMenuItem("Standard Info (Info)", null, (s, e) => SetLogLevel(LogVerbosity.Info));
            mnuLogLevelDebug = new ToolStripMenuItem("Detailed Debug (Debug)", null, (s, e) => SetLogLevel(LogVerbosity.Debug));
            mnuLogLevelTrace = new ToolStripMenuItem("Full Packet Dump (Trace)", null, (s, e) => SetLogLevel(LogVerbosity.Trace));
            mnuLogLevel.DropDownItems.AddRange(new ToolStripItem[] {
                mnuLogLevelError,
                mnuLogLevelWarn,
                mnuLogLevelInfo,
                mnuLogLevelDebug,
                mnuLogLevelTrace
            });

            mnuOpenLogFile = new ToolStripMenuItem("Open Log File (virtualplc_debug.log)...");
            mnuOpenLogFile.Click += (s, e) => AppLogger.OpenLogFile();
            mnuClearLogFile = new ToolStripMenuItem("Clear Log File");
            mnuClearLogFile.Click += (s, e) =>
            {
                AppLogger.ClearLogFile();
                MessageBox.Show(Localization.T("Settings_LogClearedMsg"), "VirtualPLC Log", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            mnuLogging.DropDownItems.AddRange(new ToolStripItem[] {
                mnuLogEnabled,
                mnuLogLevel,
                new ToolStripSeparator(),
                mnuOpenLogFile,
                mnuClearLogFile
            });

            mnuSettings.DropDownItems.AddRange(new ToolStripItem[] {
                mnuSettingsGeneral,
                new ToolStripSeparator(),
                mnuLanguage,
                mnuLogging
            });

            // 5. Menu: PCOM TCP Server
            mnuPcom = new ToolStripMenuItem("PCOM TCP Server");
            mnuPcomStart = new ToolStripMenuItem("Start PCOM Server", null, (s, e) => pcomServer.Start((config != null && config.PcomPort > 0) ? config.PcomPort : PcomServer.DEFAULT_PORT));
            mnuPcomStop = new ToolStripMenuItem("Stop PCOM Server", null, (s, e) => pcomServer.Stop());
            mnuPcom.DropDownItems.AddRange(new ToolStripItem[] { mnuPcomStart, mnuPcomStop });

            // 6. Menu: Help
            mnuHelp = new ToolStripMenuItem("Help");
            mnuOpenLog = new ToolStripMenuItem("Open Debugger Log File...", null, (s, e) => AppLogger.OpenLogFile());
            mnuInfo = new ToolStripMenuItem("OPLC System Info...", null, (s, e) => ShowInfoScreen());
            mnuAbout = new ToolStripMenuItem("About OPLC Simulator...", null, (s, e) => ShowAboutDialog());
            mnuHelp.DropDownItems.AddRange(new ToolStripItem[] { mnuOpenLog, new ToolStripSeparator(), mnuInfo, new ToolStripSeparator(), mnuAbout });

            menuStrip.Items.AddRange(new ToolStripItem[] { mnuFile, mnuPlc, mnuHardware, mnuSettings, mnuPcom, mnuHelp });
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            // ==========================================
            // TOOLBAR (ToolStrip)
            // ==========================================
            toolStrip = new ToolStrip
            {
                BackColor = Color.FromArgb(36, 42, 54),
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(6, 4, 6, 4),
                RenderMode = ToolStripRenderMode.System
            };

            btnToolbarLoad = new ToolStripButton("  📁 LOAD PROJECT  ")
            {
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarLoad.Click += (s, e) => OpenProjectDialog();

            btnToolbarClear = new ToolStripButton("  🧹 Clear OPLC  ")
            {
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.FromArgb(220, 220, 220)
            };
            btnToolbarClear.Click += (s, e) => ClearProject();

            btnToolbarRun = new ToolStripButton("  ▶ START (RUN)  ")
            {
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarRun.Click += (s, e) => StartPlc();

            btnToolbarStop = new ToolStripButton("  ⏹ STOP  ")
            {
                BackColor = Color.FromArgb(198, 40, 40),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarStop.Click += (s, e) => StopPlc();

            btnToolbarReset = new ToolStripButton("  🔄 RESET  ")
            {
                BackColor = Color.FromArgb(239, 108, 0),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarReset.Click += (s, e) => ResetPlc();

            btnToolbarSimSetup = new ToolStripButton("  🧪 SYMULACJA I/O  ")
            {
                BackColor = Color.FromArgb(0, 137, 123),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarSimSetup.Click += (s, e) => OpenSimulationSetupDialog(false);

            btnToolbarHw = new ToolStripButton("  ⚙ HARDWARE  ")
            {
                BackColor = Color.FromArgb(70, 80, 100),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarHw.Click += (s, e) => OpenHardwareConfigDialog();

            btnToolbarSettings = new ToolStripButton("  🔧 SETTINGS  ")
            {
                BackColor = Color.FromArgb(55, 65, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarSettings.Click += (s, e) => OpenSettingsDialog();

            toolStrip.Items.AddRange(new ToolStripItem[] {
                btnToolbarLoad,
                btnToolbarClear,
                new ToolStripSeparator(),
                btnToolbarRun,
                btnToolbarStop,
                btnToolbarReset,
                new ToolStripSeparator(),
                btnToolbarSimSetup,
                btnToolbarHw,
                btnToolbarSettings
            });
            this.Controls.Add(toolStrip);

            // ==========================================
            // BOTTOM STATUS BAR (StatusStrip)
            // ==========================================
            statusStrip = new StatusStrip
            {
                BackColor = Color.FromArgb(18, 22, 30),
                ForeColor = Color.FromArgb(200, 210, 225)
            };

            statusPlcState = new ToolStripStatusLabel("STATE: STOP") { Width = 210, TextAlign = ContentAlignment.MiddleLeft };
            statusScanTime = new ToolStripStatusLabel("Scan: 0.0 ms") { Width = 170, TextAlign = ContentAlignment.MiddleLeft };
            statusPcom = new ToolStripStatusLabel("PCOM TCP: :20256") { Width = 170, TextAlign = ContentAlignment.MiddleLeft };
            statusActivePage = new ToolStripStatusLabel("HMI Screen: 1") { Width = 150, TextAlign = ContentAlignment.MiddleLeft };
            statusHwProfile = new ToolStripStatusLabel("Hardware: V350-35-RA22") { Width = 230, TextAlign = ContentAlignment.MiddleLeft };
            statusProjectName = new ToolStripStatusLabel("Project: [None]") { Spring = true, TextAlign = ContentAlignment.MiddleRight };

            statusStrip.Items.AddRange(new ToolStripItem[] {
                statusPlcState,
                new ToolStripSeparator(),
                statusScanTime,
                new ToolStripSeparator(),
                statusPcom,
                new ToolStripSeparator(),
                statusActivePage,
                new ToolStripSeparator(),
                statusHwProfile,
                statusProjectName
            });
            this.Controls.Add(statusStrip);

            // ==========================================
            // MAIN SPLITCONTAINER
            // Left: V350 / Vision Front Panel | Right: Tabs
            // ==========================================
            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 420,
                FixedPanel = FixedPanel.Panel1,
                BackColor = Color.FromArgb(40, 46, 58)
            };
            this.Controls.Add(splitMain);
            splitMain.BringToFront();

            // Physical OPLC front panel bezel
            var pnlV350Bezel = CreateV350FrontPanel();
            splitMain.Panel1.Controls.Add(pnlV350Bezel);

            // Right Panel: Tabs
            tabControlMain = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };

            // Tab 1: I/O Cabinet
            tabIo = new TabPage("I/O Cabinet");
            tabIo.BackColor = Color.FromArgb(28, 32, 40);
            tabIo.Controls.Add(hardwarePanel);
            hardwarePanel.Dock = DockStyle.Fill;
            tabControlMain.TabPages.Add(tabIo);

            // Tab 2: Live Watch Table
            tabWatch = new TabPage("Live Watch Table");
            tabWatch.BackColor = Color.FromArgb(28, 32, 40);
            InitializeWatchTab(tabWatch);
            tabControlMain.TabPages.Add(tabWatch);

            // Tab 3: Ladder Logic Viewer
            tabLadder = new TabPage("Ladder Logic");
            tabLadder.BackColor = Color.FromArgb(28, 32, 40);
            InitializeLadderTab(tabLadder);
            tabControlMain.TabPages.Add(tabLadder);

            // Tab 4: HMI Navigator
            tabHmi = new TabPage("HMI Screens");
            tabHmi.BackColor = Color.FromArgb(28, 32, 40);
            InitializeHmiTab(tabHmi);
            tabControlMain.TabPages.Add(tabHmi);

            // Tab 5: Event Log & Diagnostics
            tabLog = new TabPage("Event Log");
            tabLog.BackColor = Color.FromArgb(28, 32, 40);
            InitializeLogTab(tabLog);
            tabControlMain.TabPages.Add(tabLog);

            splitMain.Panel2.Controls.Add(tabControlMain);
        }

        private Panel CreateV350FrontPanel()
        {
            var pnlBezel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(32, 36, 46),
                Padding = new Padding(12)
            };

            // Vision enclosure (industrial frame)
            var pnlOplcCase = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(42, 48, 60),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlBezel.Controls.Add(pnlOplcCase);

            // Enclosure top header
            var pnlTopHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(26, 30, 38)
            };

            var lblLogoUni = new Label
            {
                Text = "UNITRONICS",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                Left = 14,
                Top = 8,
                AutoSize = true
            };

            string family = (config != null && config.Profile != null) ? config.Profile.GetCurrentModelSpec().Family : "Vision";
            lblLogoModel = new Label
            {
                Text = family + "™",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 255),
                Left = 135,
                Top = 10,
                AutoSize = true
            };

            // CPU status LEDs: PWR, RUN, ERR
            lblPwrLed = CreateLedIndicator("PWR", Color.LimeGreen, 260, 12, pnlTopHeader);
            lblRunLed = CreateLedIndicator("RUN", Color.FromArgb(60, 70, 80), 310, 12, pnlTopHeader);
            lblErrLed = CreateLedIndicator("ERR", Color.FromArgb(60, 70, 80), 360, 12, pnlTopHeader);

            pnlTopHeader.Controls.Add(lblLogoUni);
            pnlTopHeader.Controls.Add(lblLogoModel);
            pnlOplcCase.Controls.Add(pnlTopHeader);

            // TFT display container
            var pnlScreenFrame = new Panel
            {
                Size = new Size(344, 264),
                Left = (394 - 344) / 2,
                Top = 54,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.Fixed3D
            };

            picScreen = new PictureBox
            {
                Size = new Size(320, 240),
                Left = 10,
                Top = 10,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            picScreen.MouseDown += (s, e) =>
            {
                int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
                int curW = hmiRenderer.ScreenWidth;
                int curH = hmiRenderer.ScreenHeight;
                int touchX = (int)((long)e.X * curW / Math.Max(1, picScreen.Width));
                int touchY = (int)((long)e.Y * curH / Math.Max(1, picScreen.Height));

                hmiRenderer.HandleTouchDown(touchX, touchY, activePage);
                int newPage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
                picScreen.Image = hmiRenderer.RenderScreen(newPage);
                picScreen.Refresh();
            };

            picScreen.MouseUp += (s, e) =>
            {
                int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
                int curW = hmiRenderer.ScreenWidth;
                int curH = hmiRenderer.ScreenHeight;
                int touchX = (int)((long)e.X * curW / Math.Max(1, picScreen.Width));
                int touchY = (int)((long)e.Y * curH / Math.Max(1, picScreen.Height));

                hmiRenderer.HandleTouchUp(touchX, touchY, activePage);
                int newPage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
                picScreen.Image = hmiRenderer.RenderScreen(newPage);
                picScreen.Refresh();
            };

            pnlScreenFrame.Controls.Add(picScreen);
            pnlOplcCase.Controls.Add(pnlScreenFrame);

            var spec = (config != null && config.Profile != null) ? config.Profile.GetCurrentModelSpec() : null;
            string dispType = spec != null ? spec.DisplayType : "Color TFT";
            int sw = spec != null ? spec.ScreenWidth : 320;
            int sh = spec != null ? spec.ScreenHeight : 240;

            lblScreenCaption = new Label
            {
                Text = string.Format("{0} ({1}x{2} px)", dispType, sw, sh),
                ForeColor = Color.FromArgb(160, 175, 195),
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                Top = 322,
                Left = 12,
                Width = 370,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlOplcCase.Controls.Add(lblScreenCaption);

            // Membrane keypad: 5 physical function keys F1..F5
            var pnlKeypad = new Panel
            {
                Left = 12,
                Top = 348,
                Width = 370,
                Height = 110,
                BackColor = Color.FromArgb(32, 38, 48),
                BorderStyle = BorderStyle.FixedSingle
            };

            int btnW = 64, btnH = 34, gap = 8;
            for (int i = 0; i < 5; i++)
            {
                int fIdx = i + 1;
                var btnF = new Button
                {
                    Text = "F" + fIdx,
                    Left = 8 + i * (btnW + gap),
                    Top = 10,
                    Width = btnW,
                    Height = btnH,
                    BackColor = Color.FromArgb(50, 58, 72),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold)
                };
                btnF.FlatAppearance.BorderColor = Color.FromArgb(80, 95, 115);
                btnF.MouseDown += (s, e) => PulseFunctionKey(fIdx, true);
                btnF.MouseUp += (s, e) => PulseFunctionKey(fIdx, false);
                pnlKeypad.Controls.Add(btnF);
            }

            lblTouchHint = new Label
            {
                Text = "• Touchscreen Display: click on the screen to operate HMI buttons or switch displays.\n• Keys F1..F5: control PLC system bits SB 31..SB 35.",
                Left = 8,
                Top = 52,
                Width = 352,
                Height = 50,
                ForeColor = Color.FromArgb(170, 190, 215),
                Font = new Font("Segoe UI", 8f, FontStyle.Regular)
            };
            pnlKeypad.Controls.Add(lblTouchHint);

            pnlOplcCase.Controls.Add(pnlKeypad);
            return pnlBezel;
        }

        private Label CreateLedIndicator(string text, Color initColor, int x, int y, Panel parent)
        {
            var ledDot = new Label
            {
                Text = "",
                Size = new Size(10, 10),
                Left = x,
                Top = y + 4,
                BackColor = initColor,
                BorderStyle = BorderStyle.FixedSingle
            };

            var ledText = new Label
            {
                Text = text,
                Left = x + 12,
                Top = y + 2,
                AutoSize = true,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 215, 230)
            };

            parent.Controls.Add(ledDot);
            parent.Controls.Add(ledText);
            return ledDot;
        }

        // ==========================================
        // LIVE WATCH TABLE TAB
        // ==========================================
        private void InitializeWatchTab(TabPage tab)
        {
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = Color.FromArgb(36, 42, 54),
                Padding = new Padding(8, 6, 8, 6)
            };

            lblWatchFilt = new Label { Text = "Type filter:", Left = 10, Top = 10, AutoSize = true, ForeColor = Color.FromArgb(200, 215, 235) };
            cboWatchFilter = new ComboBox
            {
                Left = 80,
                Top = 7,
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(48, 56, 70),
                ForeColor = Color.White
            };
            cboWatchFilter.Items.AddRange(new object[] {
                "All Project Operands",
                "Physical Inputs (I)",
                "Physical Outputs (O)",
                "Memory Bits (MB)",
                "Memory Integers (MI)",
                "Long Integers (ML / DW)",
                "Timers (T)",
                "System Operands (SB / SI)"
            });
            cboWatchFilter.SelectedIndex = 0;
            cboWatchFilter.SelectedIndexChanged += (s, e) => FilterWatchItems();

            lblWatchSearch = new Label { Text = "Search:", Left = 300, Top = 10, AutoSize = true, ForeColor = Color.FromArgb(200, 215, 235) };
            txtWatchSearch = new TextBox
            {
                Left = 350,
                Top = 7,
                Width = 160,
                BackColor = Color.FromArgb(48, 56, 70),
                ForeColor = Color.White
            };
            txtWatchSearch.TextChanged += (s, e) => FilterWatchItems();

            btnWatchForce = new Button
            {
                Text = "✏ Change / Force Value",
                Left = 525,
                Top = 6,
                Width = 210,
                Height = 26,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold)
            };
            btnWatchForce.Click += (s, e) => ForceSelectedWatchValue();

            pnlTop.Controls.Add(lblWatchFilt);
            pnlTop.Controls.Add(cboWatchFilter);
            pnlTop.Controls.Add(lblWatchSearch);
            pnlTop.Controls.Add(txtWatchSearch);
            pnlTop.Controls.Add(btnWatchForce);
            tab.Controls.Add(pnlTop);

            dgvWatch = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(24, 28, 36),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 58, 72),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                EnableHeadersVisualStyles = false,
                BorderStyle = BorderStyle.None
            };

            dgvWatch.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(36, 44, 58);
            dgvWatch.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0, 220, 255);
            dgvWatch.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            dgvWatch.DefaultCellStyle.BackColor = Color.FromArgb(28, 32, 42);
            dgvWatch.DefaultCellStyle.ForeColor = Color.White;
            dgvWatch.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 100, 180);
            dgvWatch.DefaultCellStyle.SelectionForeColor = Color.White;
            dgvWatch.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(32, 38, 48);

            dgvWatch.Columns.Add("ColType", "Type");
            dgvWatch.Columns.Add("ColAddr", "Address");
            dgvWatch.Columns.Add("ColName", "Symbol / Description");
            dgvWatch.Columns.Add("ColVal", "Live Value");
            dgvWatch.Columns.Add("ColFmt", "Format");

            dgvWatch.Columns[0].Width = 70;
            dgvWatch.Columns[1].Width = 70;
            dgvWatch.Columns[2].Width = 360;
            dgvWatch.Columns[3].Width = 140;
            dgvWatch.Columns[4].Width = 160;

            dgvWatch.CellDoubleClick += (s, e) => ForceSelectedWatchValue();

            tab.Controls.Add(dgvWatch);
            dgvWatch.BringToFront();
        }

        private void InitializeLadderTab(TabPage tab)
        {
            var splitLadder = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 240,
                BackColor = Color.FromArgb(40, 46, 58)
            };

            // Subroutine tree view
            var pnlLeft = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 32, 40) };
            var lblTreeHdr = new Label
            {
                Text = "Subroutines:",
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(6, 6, 0, 0),
                ForeColor = Color.FromArgb(0, 220, 255),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            tvSubroutines = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 32, 40),
                ForeColor = Color.White,
                LineColor = Color.FromArgb(0, 150, 255),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            tvSubroutines.AfterSelect += (s, e) => DisplaySubroutineRungs();
            pnlLeft.Controls.Add(tvSubroutines);
            pnlLeft.Controls.Add(lblTreeHdr);
            splitLadder.Panel1.Controls.Add(pnlLeft);

            // Rung viewer for the selected subroutine
            var pnlRight = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 24, 30) };
            var lblRungHdr = new Label
            {
                Text = "Ladder Rungs (LAD):",
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(6, 6, 0, 0),
                ForeColor = Color.FromArgb(255, 204, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            rtbLadderView = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 22, 28),
                ForeColor = Color.FromArgb(220, 230, 245),
                Font = new Font("Consolas", 9f, FontStyle.Regular),
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };
            pnlRight.Controls.Add(rtbLadderView);
            pnlRight.Controls.Add(lblRungHdr);
            splitLadder.Panel2.Controls.Add(pnlRight);

            tab.Controls.Add(splitLadder);
        }

        private void InitializeHmiTab(TabPage tab)
        {
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(36, 42, 54),
                Padding = new Padding(8, 6, 8, 6)
            };

            var btnOpenScreen = new Button
            {
                Text = "▶ Switch to Selected HMI Screen",
                Left = 10,
                Top = 7,
                Width = 280,
                Height = 28,
                BackColor = Color.FromArgb(0, 150, 136),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            btnOpenScreen.Click += (s, e) => SwitchToSelectedHmiScreen();

            var lblHint = new Label
            {
                Text = "Double-click a row to immediately update SI 9 (Current Display ID).",
                Left = 300,
                Top = 12,
                AutoSize = true,
                ForeColor = Color.FromArgb(180, 200, 220)
            };

            pnlTop.Controls.Add(btnOpenScreen);
            pnlTop.Controls.Add(lblHint);
            tab.Controls.Add(pnlTop);

            lvHmiPages = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.FromArgb(24, 28, 36),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                BorderStyle = BorderStyle.None
            };
            lvHmiPages.Columns.Add("Display ID", 90);
            lvHmiPages.Columns.Add("Display Name / Title", 300);
            lvHmiPages.Columns.Add("Element Count", 180);
            lvHmiPages.Columns.Add("Background Color", 120);

            lvHmiPages.DoubleClick += (s, e) => SwitchToSelectedHmiScreen();

            tab.Controls.Add(lvHmiPages);
            lvHmiPages.BringToFront();
        }

        private void InitializeLogTab(TabPage tab)
        {
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(36, 42, 54),
                Padding = new Padding(8, 6, 8, 6)
            };

            var btnClearLog = new Button
            {
                Text = "Clear Log",
                Left = 10,
                Top = 6,
                Width = 140,
                Height = 26,
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnClearLog.Click += (s, e) => rtbLog.Clear();

            var btnCopyLog = new Button
            {
                Text = "Copy to Clipboard",
                Left = 160,
                Top = 6,
                Width = 140,
                Height = 26,
                BackColor = Color.FromArgb(60, 68, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCopyLog.Click += (s, e) => { if (rtbLog.TextLength > 0) Clipboard.SetText(rtbLog.Text); };

            var btnOpenLog = new Button
            {
                Text = "📄 Open Log File (debug.log)",
                Left = 310,
                Top = 6,
                Width = 200,
                Height = 26,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnOpenLog.Click += (s, e) => AppLogger.OpenLogFile();

            var lblLogInfo = new Label
            {
                Text = "Live diagnostics written to: virtualplc_debug.log (in application directory)",
                Left = 525,
                Top = 11,
                AutoSize = true,
                ForeColor = Color.FromArgb(160, 180, 205),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic)
            };

            pnlTop.Controls.Add(btnClearLog);
            pnlTop.Controls.Add(btnCopyLog);
            pnlTop.Controls.Add(btnOpenLog);
            pnlTop.Controls.Add(lblLogInfo);
            tab.Controls.Add(pnlTop);

            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 18, 24),
                ForeColor = Color.FromArgb(200, 220, 240),
                Font = new Font("Consolas", 9f, FontStyle.Regular),
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };
            tab.Controls.Add(rtbLog);
            rtbLog.BringToFront();
        }

        // ==========================================
        // HARDWARE CONFIGURATION & SETTINGS DIALOGS
        // ==========================================
        private void OpenHardwareConfigDialog()
        {
            using (var dlg = new HardwareConfigDialog(config.Profile, startupMode: false))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    ApplyHardwareProfile(dlg.ResultProfile);
                    config.Profile = dlg.ResultProfile;
                    config.ShowConfigOnStartup = dlg.ShowOnStartupValue;
                    config.Save();
                }
            }
        }

        public void ApplyHardwareProfile(HardwareProfile newProfile)
        {
            if (newProfile == null) return;
            config.Profile = newProfile;
            plc.SetProfile(newProfile);
            hardwarePanel.RebuildLayout(newProfile);

            var spec = newProfile.GetCurrentModelSpec();
            if (lblLogoModel != null) lblLogoModel.Text = spec.Family + "™";
            if (lblScreenCaption != null) lblScreenCaption.Text = string.Format("{0} ({1}x{2} px)", spec.DisplayType, spec.ScreenWidth, spec.ScreenHeight);

            UpdateStatusLabels();

            int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
            picScreen.Image = hmiRenderer.RenderScreen(activePage);
            picScreen.Refresh();

            if (plc.Project != null)
            {
                PopulateWatchTable(plc.Project);
            }

            plc.Log("Applied hardware profile: " + newProfile.GetModelFullName());
        }

        private void OpenSettingsDialog()
        {
            using (var dlg = new SettingsDialog(config, config.Profile))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    UpdateLoggingMenuItems();
                    UpdateStatusLabels();
                }
            }
        }

        private void SetLanguage(AppLanguage lang)
        {
            config.Language = lang;
            config.Save();
            Localization.SetLanguage(lang);
        }

        private void SetLogLevel(LogVerbosity level)
        {
            config.LoggingVerbosity = level;
            config.Save();
            AppLogger.Configure(config);
            UpdateLoggingMenuItems();
        }

        private void ToggleLogging(bool enabled)
        {
            config.LoggingEnabled = enabled;
            config.Save();
            AppLogger.Configure(config);
            UpdateLoggingMenuItems();
        }

        private void UpdateLoggingMenuItems()
        {
            if (mnuLogEnabled != null) mnuLogEnabled.Checked = config.LoggingEnabled;
            if (mnuLogLevelError != null) mnuLogLevelError.Checked = (config.LoggingVerbosity == LogVerbosity.Error);
            if (mnuLogLevelWarn != null) mnuLogLevelWarn.Checked = (config.LoggingVerbosity == LogVerbosity.Warn);
            if (mnuLogLevelInfo != null) mnuLogLevelInfo.Checked = (config.LoggingVerbosity == LogVerbosity.Info);
            if (mnuLogLevelDebug != null) mnuLogLevelDebug.Checked = (config.LoggingVerbosity == LogVerbosity.Debug);
            if (mnuLogLevelTrace != null) mnuLogLevelTrace.Checked = (config.LoggingVerbosity == LogVerbosity.Trace);
        }

        public void ApplyLanguage()
        {
            bool pl = (Localization.CurrentLanguage == AppLanguage.Polish);
            string projName = (plc.Project != null) ? plc.Project.ProjectName : Localization.T("Status_NoProgram");
            string hwFullName = config.Profile != null ? config.Profile.GetModelFullName() : "V350";
            this.Text = string.Format(Localization.T("AppTitle"), "0.0.16", hwFullName + " - " + projName);

            // Menu: File
            mnuFile.Text = Localization.T("Menu_File");
            mnuLoad.Text = Localization.T("Menu_LoadProject");
            mnuClear.Text = Localization.T("Menu_ClearProject");
            mnuExit.Text = Localization.T("Menu_Exit");

            // Menu: PLC Control
            mnuPlc.Text = Localization.T("Menu_Plc");
            mnuRun.Text = Localization.T("Menu_Start");
            mnuStop.Text = Localization.T("Menu_Stop");
            mnuReset.Text = Localization.T("Menu_Reset");
            if (mnuSimSetup != null)
            {
                mnuSimSetup.Text = pl
                    ? "Konfigurator Symulacji Wejść/Wyjść i Procesu (F8)..."
                    : "I/O & Process Simulation Setup (F8)...";
            }

            // Menu: Hardware
            mnuHardware.Text = Localization.T("Menu_Hardware");
            mnuHardwareConfig.Text = Localization.T("Menu_HardwareConfig");
            mnuPresetExpanded.Text = Localization.T("HwCfg_Preset_Expanded");
            mnuPresetV350.Text = Localization.T("HwCfg_Preset_Default");

            // Menu: Settings
            mnuSettings.Text = Localization.T("Menu_Settings");
            mnuSettingsGeneral.Text = Localization.T("Settings_Title");
            mnuLanguage.Text = Localization.T("Menu_Language");
            mnuLangPl.Text = Localization.T("Menu_Lang_PL");
            mnuLangEn.Text = Localization.T("Menu_Lang_EN");
            mnuLangPl.Checked = (Localization.CurrentLanguage == AppLanguage.Polish);
            mnuLangEn.Checked = (Localization.CurrentLanguage == AppLanguage.English);

            mnuLogging.Text = Localization.T("Menu_Logging");
            mnuLogEnabled.Text = Localization.T("Menu_LogEnabled");
            mnuLogLevel.Text = Localization.T("Menu_LogLevel");
            mnuLogLevelError.Text = Localization.T("Settings_LogLevel_Error");
            mnuLogLevelWarn.Text = Localization.T("Settings_LogLevel_Warn");
            mnuLogLevelInfo.Text = Localization.T("Settings_LogLevel_Info");
            mnuLogLevelDebug.Text = Localization.T("Settings_LogLevel_Debug");
            mnuLogLevelTrace.Text = Localization.T("Settings_LogLevel_Trace");
            mnuOpenLogFile.Text = Localization.T("Menu_OpenLog");
            mnuClearLogFile.Text = Localization.T("Menu_ClearLog");

            UpdateLoggingMenuItems();

            // Menu: PCOM
            mnuPcom.Text = Localization.T("Menu_Pcom");
            int port = (config != null && config.PcomPort > 0) ? config.PcomPort : PcomServer.DEFAULT_PORT;
            mnuPcomStart.Text = string.Format(Localization.T("Menu_PcomStart"), port);
            mnuPcomStop.Text = Localization.T("Menu_PcomStop");

            // Menu: Help
            mnuHelp.Text = Localization.T("Menu_Help");
            mnuOpenLog.Text = Localization.T("Menu_OpenLog");
            mnuInfo.Text = Localization.T("Menu_Info");
            mnuAbout.Text = Localization.T("Menu_About");

            // Toolbar
            btnToolbarLoad.Text = "  📁 " + Localization.T("Menu_LoadProject").Replace("...", "") + "  ";
            btnToolbarClear.Text = "  🧹 " + Localization.T("Menu_ClearProject") + "  ";
            btnToolbarRun.Text = "  ▶ " + Localization.T("Menu_Start") + "  ";
            btnToolbarStop.Text = "  ⏹ " + Localization.T("Menu_Stop") + "  ";
            btnToolbarReset.Text = "  🔄 " + Localization.T("Menu_Reset") + "  ";
            if (btnToolbarSimSetup != null)
            {
                btnToolbarSimSetup.Text = pl ? "  🧪 SYMULACJA I/O (F8)  " : "  🧪 SIMULATE I/O (F8)  ";
            }
            btnToolbarHw.Text = "  ⚙ " + Localization.T("Menu_Hardware") + "  ";
            btnToolbarSettings.Text = "  🔧 " + Localization.T("Menu_Settings") + "  ";

            // Tabs
            tabIo.Text = Localization.T("Tab_Hardware");
            tabWatch.Text = Localization.T("Tab_Watch");
            tabLadder.Text = Localization.T("Tab_Subroutines");
            tabHmi.Text = Localization.T("Tab_HmiPages");
            tabLog.Text = Localization.T("Tab_Diagnostics");

            // Watch Tab
            if (lblWatchFilt != null) lblWatchFilt.Text = Localization.T("Watch_TypeFilterLabel");
            if (lblWatchSearch != null) lblWatchSearch.Text = Localization.T("Watch_SearchLabel");
            if (btnWatchForce != null) btnWatchForce.Text = Localization.T("Watch_Btn_Force");

            if (dgvWatch != null && dgvWatch.Columns.Count >= 5)
            {
                dgvWatch.Columns[0].HeaderText = Localization.T("Watch_Col_Type");
                dgvWatch.Columns[1].HeaderText = Localization.T("Watch_Col_Addr");
                dgvWatch.Columns[2].HeaderText = Localization.T("Watch_Col_Symbol");
                dgvWatch.Columns[3].HeaderText = Localization.T("Watch_Col_LiveValue");
                dgvWatch.Columns[4].HeaderText = Localization.T("Watch_Col_Format");
            }

            // Front bezel hints
            if (lblTouchHint != null && config != null && config.Profile != null)
            {
                var curSpec = config.Profile.GetCurrentModelSpec();
                lblTouchHint.Text = Localization.T("Bezel_TouchHint", curSpec.DisplayType);
            }

            UpdateStatusLabels();
        }

        private void UpdateStatusLabels()
        {
            if (statusPlcState == null) return;

            // 1. PLC State
            if (plc.State == PlcRunState.RUN)
            {
                statusPlcState.Text = Localization.T("Status_State_RUN");
                statusPlcState.ForeColor = Color.FromArgb(0, 230, 118);
            }
            else if (plc.HasProgramLoaded)
            {
                statusPlcState.Text = Localization.T("Status_State_STOP");
                statusPlcState.ForeColor = Color.FromArgb(255, 204, 0);
            }
            else
            {
                statusPlcState.Text = Localization.T("Status_NoProgram");
                statusPlcState.ForeColor = Color.FromArgb(255, 82, 82);
            }

            // 2. Scan Time & PCOM status
            statusScanTime.Text = string.Format(Localization.T("Status_ScanTime"), plc.LastScanDurationMs, 20);

            int port = (config != null && config.PcomPort > 0) ? config.PcomPort : PcomServer.DEFAULT_PORT;
            statusPcom.Text = pcomServer.IsActive
                ? string.Format(Localization.T("Status_PcomListening"), port)
                : Localization.T("Status_PcomStopped");

            // 3. Active HMI Display
            int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
            string pageName = (plc.Project != null && plc.Project.Pages.ContainsKey(activePage)) ? plc.Project.Pages[activePage].Name : "";
            statusActivePage.Text = string.Format(Localization.T("Status_Page"), activePage, pageName);

            // 4. Hardware Profile
            string hwName = (config != null && config.Profile != null) ? config.Profile.GetModelFullName() : "V350";
            statusHwProfile.Text = string.Format(Localization.T("Status_HardwareProfile"), hwName);

            // 5. Project Name
            if (plc.Project != null)
            {
                statusProjectName.Text = string.Format(Localization.T("Status_Project"), plc.Project.ProjectName, plc.Project.HardwareTarget);
            }
            else
            {
                statusProjectName.Text = Localization.T("Status_NoProject");
            }
        }

        // ==========================================
        // PLC SCAN EXECUTION LOOP (BACKGROUND THREAD)
        // ==========================================
        private void ScanWorkerLoop()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastMs = sw.ElapsedMilliseconds;

            while (isScanThreadRunning)
            {
                long now = sw.ElapsedMilliseconds;
                int delta = (int)(now - lastMs);
                if (delta < 20)
                {
                    Thread.Sleep(2);
                    continue;
                }
                lastMs = now;

                try
                {
                    plc.CycleScan(delta);
                }
                catch (Exception ex)
                {
                    plc.Log("Error in PLC scan cycle: " + ex.Message);
                }
            }
        }

        // ==========================================
        // USER INTERFACE REFRESH TIMER (~30 FPS)
        // ==========================================
        private int watchTableRefreshCounter = 0;
        private void OnUiTimerTick(object sender, EventArgs e)
        {
            if (plc.State != PlcRunState.RUN && plc.Memory != null)
            {
                plc.Memory.SyncRamFromEngine(plc, false);
            }

            // 1. Render HMI screen
            int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
            picScreen.Image = hmiRenderer.RenderScreen(activePage);

            // 2. Update enclosure LEDs
            lblPwrLed.BackColor = Color.LimeGreen;
            lblRunLed.BackColor = (plc.State == PlcRunState.RUN) ? Color.LimeGreen : Color.FromArgb(50, 65, 50);
            lblErrLed.BackColor = (!plc.HasProgramLoaded || plc.State == PlcRunState.STOP) ? Color.FromArgb(200, 40, 40) : Color.FromArgb(40, 20, 20);

            // 3. Update status bar
            UpdateStatusLabels();

            // 4. Refresh Live Watch values (~every 100 ms)
            watchTableRefreshCounter++;
            if (watchTableRefreshCounter >= 3 && tabControlMain.SelectedIndex == 1)
            {
                watchTableRefreshCounter = 0;
                RefreshWatchTableValues();
            }
        }

        // ==========================================
        // PROJECT LOADING & RESET HANDLERS
        // ==========================================
        public void LoadProjectFile(string filePath, bool promptSimulationWizard = true)
        {
            if (isLoadingProject) return;
            isLoadingProject = true;
            try
            {
                plc.Log("Loading project from file: " + filePath);
                var proj = VlpParser.LoadProjectFile(filePath, plc.Log);

                plc.Stop();
                plc.Project = proj;

                // Ensure expansion slot 1 (IO-DI8-RO8 at I32..I39 / O32..O39) is enabled if project uses expansion I/O
                if (plc.Profile != null && (proj.OperandDescriptions.ContainsKey("I32") || proj.OperandDescriptions.ContainsKey("O32")))
                {
                    plc.Profile.EnsureSlotsInitialized();
                    if (!plc.Profile.EnableExpansionBus || plc.Profile.ExpansionSlots[0].ModuleName == "None" || string.IsNullOrEmpty(plc.Profile.ExpansionSlots[0].ModuleName))
                    {
                        plc.Profile.EnableExpansionBus = true;
                        plc.Profile.ExpansionSlots[0].ModuleName = "IO-DI8-RO8";
                    }
                }

                // Always start in 100% neutral unconfigured position immediately after loading/downloading
                if (plc.SimProfile != null)
                {
                    plc.SimProfile.ResetToNeutral(plc.Profile, proj);
                }
                plc.ResetMemory();

                hardwarePanel.RebuildLayout(plc.Profile);
                PopulateWatchTable(proj);
                PopulateSubroutineTree(proj);
                PopulateHmiPageList(proj);

                ApplyLanguage();

                int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
                picScreen.Image = hmiRenderer.RenderScreen(activePage);
                picScreen.Refresh();

                plc.Log("Project loaded in NEUTRAL state (all inputs I=0, outputs O=0, analog AI=0).");

                // Ask user whether this project should be simulated, which inputs should be artificially set,
                // what role each input/output plays, and how they should behave
                if (promptSimulationWizard && this.IsHandleCreated && !isSimSetupDialogOpen)
                {
                    OpenSimulationSetupDialog(true);
                }

                // Start ladder execution after loading / wizard configuration
                plc.Start();
                plc.Log("Project ready. Ladder execution started (RUN).");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load project:\n" + ex.Message, "VisiLogic Project Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                plc.Log("PROJECT LOAD ERROR: " + ex.Message);
            }
            finally
            {
                isLoadingProject = false;
            }
        }

        public void OpenSimulationSetupDialog(bool isPostLoadPrompt = false)
        {
            if (isSimSetupDialogOpen) return;

            if (plc.SimProfile == null)
            {
                plc.SimProfile = new ProjectSimulationProfile();
                plc.SimProfile.ResetToNeutral(plc.Profile, plc.Project);
            }

            bool wasRunning = (plc.State == PlcRunState.RUN);
            if (isPostLoadPrompt)
            {
                plc.Stop();
            }

            isSimSetupDialogOpen = true;
            try
            {
                using (var dlg = new SimulationSetupDialog(plc.Project, plc.Profile, plc.SimProfile, isPostLoadPrompt))
                {
                    dlg.ShowDialog(this);
                }
            }
            finally
            {
                isSimSetupDialogOpen = false;
            }

            if (isPostLoadPrompt)
            {
                // Apply neutral reset first, then apply any user-chosen artificial inputs
                plc.ResetMemory();
            }
            else
            {
                if (!plc.SimProfile.IsSimulationEnabled)
                {
                    // User switched back to neutral mode: clear all forced inputs
                    Array.Clear(plc.I, 0, plc.I.Length);
                    if (plc.SimProfile.AnalogRules != null)
                    {
                        foreach (var kvp in plc.SimProfile.AnalogRules)
                        {
                            var ar = kvp.Value;
                            if (ar != null && ar.TargetMiAddress >= 0 && ar.TargetMiAddress < plc.MI.Length)
                                plc.MI[ar.TargetMiAddress] = 0;
                        }
                    }
                }
                else
                {
                    plc.ApplySimulationInitialStates();
                }
            }

            hardwarePanel.RebuildLayout(plc.Profile);
            RefreshWatchTableValues();

            int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
            picScreen.Image = hmiRenderer.RenderScreen(activePage);
            picScreen.Refresh();

            if (plc.SimProfile.IsSimulationEnabled)
            {
                plc.Log("I/O & Process Simulation ENABLED by user (artificial inputs and I/O behavioral rules active).");
            }
            else
            {
                plc.Log("Controller set to NEUTRAL mode (all inputs I=0, no artificial simulation rules).");
            }

            if (wasRunning && !isPostLoadPrompt)
            {
                plc.Start();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                StartPlc();
                return true;
            }
            if (keyData == Keys.F6)
            {
                StopPlc();
                return true;
            }
            if (keyData == Keys.F7)
            {
                ResetPlc();
                return true;
            }
            if (keyData == Keys.F8)
            {
                OpenSimulationSetupDialog(false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OpenProjectDialog()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = (Localization.CurrentLanguage == AppLanguage.Polish) ? "Wybierz projekt VisiLogic OPLC" : "Select VisiLogic OPLC Project";
                ofd.Filter = "VisiLogic Projects (*.vlp;*.vlx;*.udb)|*.vlp;*.vlx;*.udb|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadProjectFile(ofd.FileName, true);
                }
            }
        }

        private void ClearProject()
        {
            var res = MessageBox.Show(
                Localization.T("Msg_ConfirmClearProject"),
                "Factory Standby", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res != DialogResult.Yes) return;

            plc.Stop();
            plc.Project = null;
            if (plc.SimProfile != null)
            {
                plc.SimProfile.ResetToNeutral(plc.Profile, null);
            }
            if (plc.Memory != null)
            {
                plc.Memory.ClearFactoryReset(plc);
            }
            plc.Reset();
            hardwarePanel.RebuildLayout(plc.Profile);

            allWatchItems.Clear();
            filteredWatchItems.Clear();
            dgvWatch.Rows.Clear();
            tvSubroutines.Nodes.Clear();
            rtbLadderView.Clear();
            lvHmiPages.Items.Clear();

            ApplyLanguage();
            plc.Log("Controller memory cleared. Controller returned to factory OPLC standby state.");
        }

        private void StartPlc()
        {
            if (!plc.HasProgramLoaded)
            {
                string autoPath = VlpParser.ResolveActiveVisiLogicProjectPath();
                if (!string.IsNullOrEmpty(autoPath) && File.Exists(autoPath))
                {
                    LoadProjectFile(autoPath, true);
                    return;
                }

                MessageBox.Show(
                    Localization.T("Msg_CannotStartNoProgram"),
                    "No Program Loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            plc.Start();
        }

        private void StopPlc()
        {
            plc.Stop();
        }

        private void ResetPlc()
        {
            plc.Reset();
            hardwarePanel.RebuildLayout(plc.Profile);
            if (plc.HasProgramLoaded)
            {
                plc.Start();
            }
        }

        // ==========================================
        // LIVE WATCH TABLE
        // ==========================================
        private void PopulateWatchTable(LoadedProject proj)
        {
            allWatchItems.Clear();

            // 1. Physical digital inputs (point count matching hardware profile)
            int totalDi = (plc.Profile != null) ? plc.Profile.GetTotalDi() : 20;
            for (int i = 0; i < totalDi; i++)
            {
                string key = "I" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("Digital Input DI " + i);
                allWatchItems.Add(new WatchItem { Type = "I", Address = i, Name = name, Format = "Bit (0/1)" });
            }

            // 2. Physical digital outputs (point count matching hardware profile)
            int totalDo = (plc.Profile != null) ? plc.Profile.GetTotalDo() : 16;
            for (int i = 0; i < totalDo; i++)
            {
                string key = "O" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("Digital Output DO " + i);
                allWatchItems.Add(new WatchItem { Type = "O", Address = i, Name = name, Format = "Bit (0/1)" });
            }

            // 3. Analog inputs (AI)
            int totalAi = (plc.Profile != null) ? plc.Profile.GetTotalAi() : 2;
            for (int i = 0; i < totalAi; i++)
            {
                string key = "AN" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("Analog Input AI " + i);
                allWatchItems.Add(new WatchItem { Type = "MI", Address = 100 + i, Name = name, Format = "Analog (0..1023)" });
            }

            // 4. Memory bits MB 0..255
            for (int i = 0; i <= 255; i++)
            {
                string key = "MB" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("MB " + i);
                allWatchItems.Add(new WatchItem { Type = "MB", Address = i, Name = name, Format = "Bit (0/1)" });
            }

            // 5. Memory integers MI 0..127
            for (int i = 0; i <= 127; i++)
            {
                string key = "MI" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("MI " + i);
                allWatchItems.Add(new WatchItem { Type = "MI", Address = i, Name = name, Format = "Integer" });
            }

            // 6. Additional named operands from the loaded project
            foreach (var kvp in proj.OperandDescriptions)
            {
                string key = kvp.Key;
                string name = kvp.Value;

                string type = "";
                int addr = 0;

                if (key.StartsWith("MB")) { type = "MB"; int.TryParse(key.Substring(2), out addr); }
                else if (key.StartsWith("MI")) { type = "MI"; int.TryParse(key.Substring(2), out addr); }
                else if (key.StartsWith("ML")) { type = "ML"; int.TryParse(key.Substring(2), out addr); }
                else if (key.StartsWith("DW")) { type = "DW"; int.TryParse(key.Substring(2), out addr); }
                else if (key.StartsWith("SB")) { type = "SB"; int.TryParse(key.Substring(2), out addr); }
                else if (key.StartsWith("SI")) { type = "SI"; int.TryParse(key.Substring(2), out addr); }
                else if (key.StartsWith("T")) { type = "T"; int.TryParse(key.Substring(1), out addr); }
                else if (key.StartsWith("I")) { type = "I"; int.TryParse(key.Substring(1), out addr); }
                else if (key.StartsWith("O")) { type = "O"; int.TryParse(key.Substring(1), out addr); }

                if (!string.IsNullOrEmpty(type) && !allWatchItems.Exists(x => x.Type == type && x.Address == addr))
                {
                    string fmt = (type == "MB" || type == "SB" || type == "I" || type == "O") ? "Bit (0/1)" : (type == "T" ? "Timer (ms)" : "Integer");
                    allWatchItems.Add(new WatchItem { Type = type, Address = addr, Name = name, Format = fmt });
                }
            }

            // 7. Key system operands
            if (!allWatchItems.Exists(x => x.Type == "SB" && x.Address == 0)) allWatchItems.Add(new WatchItem { Type = "SB", Address = 0, Name = "SB 0: Power-up bit", Format = "Bit" });
            if (!allWatchItems.Exists(x => x.Type == "SB" && x.Address == 1)) allWatchItems.Add(new WatchItem { Type = "SB", Address = 1, Name = "SB 1: First scan bit", Format = "Bit" });
            if (!allWatchItems.Exists(x => x.Type == "SB" && x.Address == 2)) allWatchItems.Add(new WatchItem { Type = "SB", Address = 2, Name = "SB 2: 1 sec pulse", Format = "Bit" });
            if (!allWatchItems.Exists(x => x.Type == "SB" && x.Address == 3)) allWatchItems.Add(new WatchItem { Type = "SB", Address = 3, Name = "SB 3: 100 ms pulse", Format = "Bit" });
            if (!allWatchItems.Exists(x => x.Type == "SI" && x.Address == 0)) allWatchItems.Add(new WatchItem { Type = "SI", Address = 0, Name = "SI 0: Scan time (ms)", Format = "Integer" });
            if (!allWatchItems.Exists(x => x.Type == "SI" && x.Address == 9)) allWatchItems.Add(new WatchItem { Type = "SI", Address = 9, Name = "SI 9: Current display ID", Format = "Integer" });

            FilterWatchItems();
        }

        private void FilterWatchItems()
        {
            if (cboWatchFilter == null) return;
            int filterIdx = cboWatchFilter.SelectedIndex;
            string search = txtWatchSearch.Text.Trim().ToLower();

            filteredWatchItems.Clear();
            foreach (var item in allWatchItems)
            {
                bool matchesType = true;
                switch (filterIdx)
                {
                    case 1: matchesType = (item.Type == "I"); break;
                    case 2: matchesType = (item.Type == "O"); break;
                    case 3: matchesType = (item.Type == "MB"); break;
                    case 4: matchesType = (item.Type == "MI"); break;
                    case 5: matchesType = (item.Type == "ML" || item.Type == "DW"); break;
                    case 6: matchesType = (item.Type == "T"); break;
                    case 7: matchesType = (item.Type == "SB" || item.Type == "SI"); break;
                    default: matchesType = true; break;
                }

                if (!matchesType) continue;

                if (!string.IsNullOrEmpty(search))
                {
                    string fullKey = (item.Type + item.Address).ToLower();
                    if (!fullKey.Contains(search) && !item.Name.ToLower().Contains(search))
                        continue;
                }

                filteredWatchItems.Add(item);
            }

            dgvWatch.Rows.Clear();
            foreach (var item in filteredWatchItems)
            {
                dgvWatch.Rows.Add(item.Type, item.Address, item.Name, GetOperandLiveString(item.Type, item.Address), item.Format);
            }
        }

        private void RefreshWatchTableValues()
        {
            if (dgvWatch.Rows.Count != filteredWatchItems.Count) return;

            for (int i = 0; i < filteredWatchItems.Count; i++)
            {
                var item = filteredWatchItems[i];
                string valStr = GetOperandLiveString(item.Type, item.Address);
                dgvWatch.Rows[i].Cells[3].Value = valStr;
            }
        }

        private string GetOperandLiveString(string type, int addr)
        {
            if (type == "I" && addr >= 0 && addr < plc.I.Length) return plc.I[addr] ? "TRUE (1 / ON)" : "FALSE (0 / OFF)";
            if (type == "O" && addr >= 0 && addr < plc.O.Length) return plc.O[addr] ? "TRUE (1 / ON)" : "FALSE (0 / OFF)";
            if (type == "MB" && addr >= 0 && addr < plc.MB.Length) return plc.MB[addr] ? "TRUE (1 / ON)" : "FALSE (0 / OFF)";
            if (type == "SB" && addr >= 0 && addr < plc.SB.Length) return plc.SB[addr] ? "TRUE (1 / ON)" : "FALSE (0 / OFF)";
            if (type == "MI" && addr >= 0 && addr < plc.MI.Length) return plc.MI[addr].ToString();
            if (type == "ML" && addr >= 0 && addr < plc.ML.Length) return plc.ML[addr].ToString();
            if (type == "DW" && addr >= 0 && addr < plc.DW.Length) return plc.DW[addr].ToString();
            if (type == "SI" && addr >= 0 && addr < plc.SI.Length) return plc.SI[addr].ToString();
            if (type == "T" && addr >= 0 && addr < plc.T.Length)
            {
                var tmr = plc.T[addr];
                return string.Format("{0} ms / {1} ms ({2})", tmr.CurrentMs, tmr.PresetMs, tmr.Done ? "DONE" : (tmr.Run ? "RUN" : "STOP"));
            }
            return "-";
        }

        private void ForceSelectedWatchValue()
        {
            if (dgvWatch.SelectedRows.Count == 0) return;
            int rowIdx = dgvWatch.SelectedRows[0].Index;
            if (rowIdx < 0 || rowIdx >= filteredWatchItems.Count) return;

            var item = filteredWatchItems[rowIdx];
            if (item.Type == "I" || item.Type == "O" || item.Type == "MB" || item.Type == "SB")
            {
                // Toggle bit state
                if (item.Type == "I") plc.I[item.Address] = !plc.I[item.Address];
                else if (item.Type == "O") plc.O[item.Address] = !plc.O[item.Address];
                else if (item.Type == "MB") plc.MB[item.Address] = !plc.MB[item.Address];
                else if (item.Type == "SB") plc.SB[item.Address] = !plc.SB[item.Address];

                plc.Log(string.Format("Forced bit toggle on {0}{1}", item.Type, item.Address));
                RefreshWatchTableValues();
            }
            else
            {
                // Numeric register force prompt
                string input = PromptDialog.Show(
                    string.Format("Enter new value for {0}{1} ({2}):", item.Type, item.Address, item.Name),
                    "Force Register Value",
                    GetOperandLiveString(item.Type, item.Address));

                int val = 0;
                if (int.TryParse(input, out val))
                {
                    if (item.Type == "MI" && item.Address >= 0 && item.Address < plc.MI.Length) plc.MI[item.Address] = (short)val;
                    else if (item.Type == "ML" && item.Address >= 0 && item.Address < plc.ML.Length) plc.ML[item.Address] = val;
                    else if (item.Type == "SI" && item.Address >= 0 && item.Address < plc.SI.Length) plc.SI[item.Address] = (short)val;

                    plc.Log(string.Format("Forced new register value {0}{1} = {2}", item.Type, item.Address, val));
                    RefreshWatchTableValues();
                }
            }
        }

        // ==========================================
        // LADDER LOGIC VIEWER
        // ==========================================
        private void PopulateSubroutineTree(LoadedProject proj)
        {
            tvSubroutines.Nodes.Clear();
            foreach (var kvp in proj.Subroutines)
            {
                var sub = kvp.Value;
                var node = new TreeNode(string.Format("{0} [{1} rungs]", sub.Name, sub.Rungs.Count))
                {
                    Tag = sub
                };
                tvSubroutines.Nodes.Add(node);
            }
            if (tvSubroutines.Nodes.Count > 0)
            {
                tvSubroutines.SelectedNode = tvSubroutines.Nodes[0];
            }
        }

        private void DisplaySubroutineRungs()
        {
            if (tvSubroutines.SelectedNode == null || tvSubroutines.SelectedNode.Tag == null) return;
            var sub = (Subroutine)tvSubroutines.SelectedNode.Tag;

            rtbLadderView.Clear();
            rtbLadderView.AppendText(string.Format("=== SUBROUTINE: {0} (ID: {1}) ===\n\n", sub.Name, sub.Rungs.Count > 0 ? sub.FuncId.ToString() : "0"));

            foreach (var rung in sub.Rungs)
            {
                rtbLadderView.SelectionColor = Color.FromArgb(0, 220, 255);
                rtbLadderView.AppendText(string.Format("[ RUNG #{0} | StripID: {1} ]\n", rung.SeqNum, rung.StripId));

                if (!string.IsNullOrEmpty(rung.Comment))
                {
                    rtbLadderView.SelectionColor = Color.FromArgb(170, 185, 205);
                    rtbLadderView.AppendText("  // " + rung.Comment + "\n");
                }

                if (rung.Elements.Count == 0)
                {
                    rtbLadderView.SelectionColor = Color.FromArgb(100, 110, 125);
                    rtbLadderView.AppendText("  --[ Empty Rung ]--\n\n");
                    continue;
                }

                foreach (var elm in rung.Elements)
                {
                    FormatLadderElement(elm);
                }
                rtbLadderView.AppendText("\n");
            }
        }

        private void FormatLadderElement(LadderElement elm)
        {
            if (elm.ElementType == 100 || elm.ElementType == 101)
            {
                return;
            }

            string elDesc = "";
            Color elCol = Color.White;

            string opName = "";
            string opVal = "";

            if (elm.Operands.Count > 0)
            {
                var op = elm.Operands[0];
                string opKey = op.Type + op.Address;
                if (plc.Project != null && plc.Project.OperandDescriptions.ContainsKey(opKey))
                {
                    opName = " \"" + plc.Project.OperandDescriptions[opKey] + "\"";
                }
                if (op.Type != "CONST")
                {
                    opVal = " [STATE: " + GetOperandLiveString(op.Type, op.Address) + "]";
                }
            }

            switch (elm.ElementType)
            {
                case 200:
                    elDesc = "  --| |---  [NO CONTACT]      " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(144, 202, 249);
                    break;
                case 201:
                    elDesc = "  --|/|---  [NC CONTACT]      " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(255, 171, 145);
                    break;
                case 202:
                    elDesc = "  --|^|---  [POSITIVE EDGE]   " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(255, 245, 157);
                    break;
                case 203:
                    elDesc = "  --|v|---  [NEGATIVE EDGE]   " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(255, 245, 157);
                    break;
                case 204:
                    elDesc = "  --( )---  [DIRECT COIL]     " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(129, 199, 132);
                    break;
                case 205:
                    elDesc = "  --( / )-- [NEGATED COIL]    " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(255, 138, 128);
                    break;
                case 206:
                    elDesc = "  --( S )-- [SET COIL]        " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(105, 240, 174);
                    break;
                case 207:
                    elDesc = "  --( R )-- [RESET COIL]      " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(255, 82, 82);
                    break;
                case 212:
                    elDesc = "  --( T )-- [TOGGLE COIL]     " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(255, 215, 64);
                    break;
                case 300:
                case 301:
                case 302:
                case 303:
                case 304:
                case 305:
                {
                    string[] cmpOps = { ">", ">=", "==", "!=", "<=", "<" };
                    string cmpSym = cmpOps[elm.ElementType - 300];
                    elDesc = string.Format("  -[COMPARE: {0} {1} {2}]-", FormatOpRef(elm.Operands, 0), cmpSym, FormatOpRef(elm.Operands, 1));
                    elCol = Color.FromArgb(128, 222, 234);
                    break;
                }
                case 400:
                case 401:
                case 402:
                case 403:
                case 404:
                case 405:
                case 406:
                case 407:
                {
                    string[] mathOps = { "ADD", "SUB", "MUL", "DIV", "MOD", "AND", "OR", "XOR" };
                    string mOp = mathOps[elm.ElementType - 400];
                    elDesc = string.Format("  -[{0}]- {1} , {2} ==> {3}", mOp, FormatOpRef(elm.Operands, 0), FormatOpRef(elm.Operands, 1), FormatOpRef(elm.Operands, 2));
                    elCol = Color.FromArgb(255, 213, 79);
                    break;
                }
                case 601:
                    if (elm.Operands.Count >= 2)
                    {
                        elDesc = "  -[STORE]- " + FormatOpRef(elm.Operands, 0) + " ==> " + FormatOpRef(elm.Operands, 1);
                        elCol = Color.FromArgb(179, 157, 219);
                    }
                    else
                    {
                        int subId = (elm.Operands.Count == 1) ? elm.Operands[0].Address : elm.MoreParamsLng;
                        string subTitle = (plc.Project != null && plc.Project.Subroutines.ContainsKey(subId)) ? (" (" + plc.Project.Subroutines[subId].Name + ")") : "";
                        elDesc = string.Format("  -[CALL SUBROUTINE #{0}{1}]-", subId, subTitle);
                        elCol = Color.FromArgb(255, 204, 0);
                    }
                    break;
                case 602:
                    elDesc = "  -[RETURN]-";
                    elCol = Color.FromArgb(176, 190, 197);
                    break;
                case 800:
                    elDesc = string.Format("  -[LOAD HMI DISPLAY #{0}]-", elm.Operands.Count > 0 ? FormatOpRef(elm.Operands, 0) : elm.MoreParamsLng.ToString());
                    elCol = Color.FromArgb(100, 255, 218);
                    break;
                case 900:
                    if (elm.Operands.Count >= 2)
                    {
                        elDesc = "  -[STORE]- " + FormatOpRef(elm.Operands, 0) + " ==> " + FormatOpRef(elm.Operands, 1);
                        elCol = Color.FromArgb(179, 157, 219);
                    }
                    else
                    {
                        elDesc = string.Format("  -[CALL SUBROUTINE #{0}]-", elm.MoreParamsLng);
                        elCol = Color.FromArgb(255, 204, 0);
                    }
                    break;
                case 1300:
                    if (elm.Operands.Count >= 6)
                    {
                        elDesc = string.Format("  -[LINEARIZE]- X={0} [{1}..{2}] -> [{3}..{4}] ==> Y={5}",
                            FormatOpRef(elm.Operands, 0), FormatOpRef(elm.Operands, 1), FormatOpRef(elm.Operands, 2),
                            FormatOpRef(elm.Operands, 3), FormatOpRef(elm.Operands, 4), FormatOpRef(elm.Operands, 5));
                        elCol = Color.FromArgb(128, 222, 234);
                    }
                    else
                    {
                        elDesc = "  -[TIMER]- " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                        elCol = Color.FromArgb(128, 222, 234);
                    }
                    break;
                case 1500:
                    elDesc = "  -[INC]- " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(179, 157, 219);
                    break;
                case 1501:
                    elDesc = "  -[DEC]- " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(179, 157, 219);
                    break;
                default:
                    elDesc = string.Format("  -[BLOCK TYPE {0}]- ", elm.ElementType);
                    for (int i = 0; i < elm.Operands.Count; i++) elDesc += FormatOpRef(elm.Operands, i) + " ";
                    elCol = Color.FromArgb(200, 200, 200);
                    break;
            }

            rtbLadderView.SelectionColor = elCol;
            rtbLadderView.AppendText(elDesc + "\n");
        }

        private string FormatOpRef(List<OperandRef> ops, int idx)
        {
            if (idx >= ops.Count) return "";
            var op = ops[idx];
            if (op.Type == "CONST") return "#" + op.ConstValue;
            return op.Type + " " + op.Address;
        }

        // ==========================================
        // HMI SCREEN NAVIGATOR
        // ==========================================
        private void PopulateHmiPageList(LoadedProject proj)
        {
            lvHmiPages.Items.Clear();
            foreach (var kvp in proj.Pages)
            {
                var page = kvp.Value;
                var item = new ListViewItem(page.Id.ToString());
                item.SubItems.Add(page.Name);
                item.SubItems.Add(page.Elements.Count.ToString() + " elements");
                item.SubItems.Add("0x" + page.ColorBack.ToString("X6"));
                item.Tag = page.Id;
                lvHmiPages.Items.Add(item);
            }
        }

        private void SwitchToSelectedHmiScreen()
        {
            if (lvHmiPages.SelectedItems.Count == 0) return;
            int pageId = (int)lvHmiPages.SelectedItems[0].Tag;
            plc.SI[9] = (short)pageId;
            plc.Log(string.Format("Switched active HMI screen to ID {0} ({1})", pageId, lvHmiPages.SelectedItems[0].SubItems[1].Text));
            picScreen.Image = hmiRenderer.RenderScreen(pageId);
            picScreen.Refresh();
        }

        // ==========================================
        // MEMBRANE FUNCTION KEYS
        // ==========================================
        private void PulseFunctionKey(int fIdx, bool isDown)
        {
            int sbIdx1 = 30 + fIdx; // SB 31 .. SB 35
            int sbIdx2 = 40 + fIdx; // SB 41 .. SB 45 (V350-35-TR20 / RA22 F1..F5 system bits)
            if (isDown)
            {
                if (sbIdx1 < plc.SB.Length) plc.SB[sbIdx1] = true;
                if (sbIdx2 < plc.SB.Length) plc.SB[sbIdx2] = true;
                plc.Log(string.Format("Function key F{0} pressed (SB {1} / SB {2} = 1)", fIdx, sbIdx1, sbIdx2));
            }
            else
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(120);
                    if (sbIdx1 < plc.SB.Length) plc.SB[sbIdx1] = false;
                    if (sbIdx2 < plc.SB.Length) plc.SB[sbIdx2] = false;
                });
            }
            int curPage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
            picScreen.Image = hmiRenderer.RenderScreen(curPage);
            picScreen.Refresh();
        }

        private void ShowInfoScreen()
        {
            string hwName = config.Profile != null ? config.Profile.GetModelFullName() : "V350";
            string info = string.Format(
                "UNITRONICS VISION SIMULATOR\n" +
                "------------------------------------\n" +
                "Controller Model: {0}\n" +
                "PLC State:        {1}\n" +
                "Loaded Project:   {2}\n" +
                "Scan Time:        {3:0.0} ms\n" +
                "Scan Counter:     {4}\n" +
                "PCOM TCP Port:    {5} ({6})\n" +
                "Active Display:   ID {7}\n" +
                "Total Rungs:      {8}\n" +
                "File Logging:     {9} (Level: {10})\n" +
                "UI Language:      {11}\n",
                hwName,
                plc.State,
                plc.Project != null ? plc.Project.ProjectName : "[None]",
                plc.LastScanDurationMs,
                plc.ScanCount,
                config.PcomPort,
                pcomServer.IsActive ? "Listening" : "Stopped",
                plc.SI[9],
                plc.Project != null ? GetTotalRungsCount(plc.Project) : 0,
                config.LoggingEnabled ? "YES" : "NO",
                config.LoggingVerbosity,
                Localization.CurrentLanguage
            );

            MessageBox.Show(info, "OPLC System Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private int GetTotalRungsCount(LoadedProject proj)
        {
            int c = 0;
            foreach (var s in proj.Subroutines.Values) c += s.Rungs.Count;
            return c;
        }

        private void ShowAboutDialog()
        {
            string hwName = config.Profile != null ? config.Profile.GetModelFullName() : "V350";
            string romPath = (plc != null && plc.Memory != null) ? plc.Memory.RomFilePath : "rom.bin";
            string ramPath = (plc != null && plc.Memory != null) ? plc.Memory.RamFilePath : "ram.bin";
            string msg =
                "Unitronics Vision PLC/HMI Simulator v0.0.16 [Portable]\n\n" +
                "Version: 0.0.16 Portable (Native Windows .NET 4.0 x86 Application)\n" +
                "Active Hardware Profile: " + hwName + "\n" +
                "Language: " + Localization.CurrentLanguage + "\n\n" +
                "Key Features:\n" +
                "• 100% Universal Soft-PLC Engine (neutral I/O state after download/load)\n" +
                "• Interactive Post-Load I/O & Process Simulation Configurator (F8)\n" +
                "• Startup Hardware Configurator (Vision V130..V1210 controllers and EX-A2X expansion modules)\n" +
                "• Real-time bilingual user interface (English & Polish)\n" +
                "• Live binary PLC memory images: rom.bin (4 MB Flash ROM) & ram.bin (256 KB RAM + live hex-editor sync)\n" +
                "• Configurable diagnostic logging (Error, Warn, Info, Debug, Trace)\n" +
                "• Direct loading of VisiLogic projects (*.vlp, *.vlx, *.udb)\n" +
                "• Graphical HMI screen renderer with touchscreen and numeric keypad overlay\n" +
                "• Virtual I/O cabinet with dynamic EX-A2X expansion bus and analog sliders\n" +
                "• PCOM TCP server compatible with VisiLogic (Download & Online Test)\n\n" +
                "ROM image: " + romPath + "\n" +
                "RAM image: " + ramPath + "\n" +
                "Debugger log file: " + AppLogger.LogFilePath;

            MessageBox.Show(msg, "About Unitronics Vision Simulator", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void HandlePlcLog(string logMsg)
        {
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action<string>(HandlePlcLog), logMsg); } catch { }
                return;
            }

            if (rtbLog != null && !rtbLog.IsDisposed)
            {
                if (rtbLog.TextLength > 50000)
                {
                    rtbLog.Clear();
                }
                rtbLog.AppendText(logMsg + "\n");
                rtbLog.ScrollToCaret();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            AppLogger.Info("Closing simulator main window (FormClosing)...");
            isScanThreadRunning = false;
            if (uiTimer != null) uiTimer.Stop();
            if (pcomServer != null) pcomServer.Stop();
            if (plc != null)
            {
                plc.Stop();
                if (plc.Memory != null)
                {
                    plc.Memory.FlushAll(plc);
                }
            }
        }
    }

    public static class PromptDialog
    {
        public static string Show(string prompt, string title, string defaultValue = "")
        {
            using (Form promptForm = new Form())
            {
                promptForm.Width = 420;
                promptForm.Height = 175;
                promptForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                promptForm.Text = title;
                promptForm.StartPosition = FormStartPosition.CenterParent;
                promptForm.BackColor = Color.FromArgb(32, 36, 46);
                promptForm.ForeColor = Color.White;
                promptForm.MaximizeBox = false;
                promptForm.MinimizeBox = false;
                promptForm.ShowInTaskbar = false;

                Label lbl = new Label() { Left = 20, Top = 15, Width = 360, Text = prompt, ForeColor = Color.FromArgb(220, 230, 245) };
                TextBox txt = new TextBox() { Left = 20, Top = 45, Width = 360, Text = defaultValue, BackColor = Color.FromArgb(48, 56, 70), ForeColor = Color.White };
                Button confirmation = new Button() { Text = "OK", Left = 195, Width = 85, Height = 28, Top = 85, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                Button cancel = new Button() { Text = "Cancel", Left = 295, Width = 85, Height = 28, Top = 85, DialogResult = DialogResult.Cancel, BackColor = Color.FromArgb(60, 68, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };

                promptForm.Controls.Add(lbl);
                promptForm.Controls.Add(txt);
                promptForm.Controls.Add(confirmation);
                promptForm.Controls.Add(cancel);
                promptForm.AcceptButton = confirmation;
                promptForm.CancelButton = cancel;

                return promptForm.ShowDialog() == DialogResult.OK ? txt.Text : null;
            }
        }
    }
}
