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
        // Komponenty główne symulatora
        private PlcEngine plc;
        private HmiRenderer hmiRenderer;
        private PcomServer pcomServer;
        private HardwarePanel hardwarePanel;
        private SimulatorConfig config;

        // Wątek pętli skanowania PLC
        private Thread scanThread;
        private volatile bool isScanThreadRunning = true;

        // Timer odświeżania GUI (30 FPS)
        private System.Windows.Forms.Timer uiTimer;

        // Kontrolki panelu czołowego OPLC
        private PictureBox picScreen;
        private Label lblPwrLed;
        private Label lblRunLed;
        private Label lblErrLed;
        private Label lblLogoModel;
        private Label lblScreenCaption;
        private Label lblTouchHint;

        // Kontrolki paska menu
        private MenuStrip menuStrip;
        private ToolStripMenuItem mnuFile;
        private ToolStripMenuItem mnuLoad;
        private ToolStripMenuItem mnuQuickLoad;
        private ToolStripMenuItem mnuClear;
        private ToolStripMenuItem mnuExit;

        private ToolStripMenuItem mnuPlc;
        private ToolStripMenuItem mnuRun;
        private ToolStripMenuItem mnuStop;
        private ToolStripMenuItem mnuReset;

        private ToolStripMenuItem mnuHardware;
        private ToolStripMenuItem mnuHardwareConfig;
        private ToolStripMenuItem mnuPresetKomora9;
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

        // Kontrolki paska narzędziowego
        private ToolStrip toolStrip;
        private ToolStripButton btnToolbarLoad;
        private ToolStripButton btnToolbarQuick;
        private ToolStripButton btnToolbarClear;
        private ToolStripButton btnToolbarRun;
        private ToolStripButton btnToolbarStop;
        private ToolStripButton btnToolbarReset;
        private ToolStripButton btnToolbarHw;
        private ToolStripButton btnToolbarSettings;

        // Kontrolki paska stanu
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusPlcState;
        private ToolStripStatusLabel statusScanTime;
        private ToolStripStatusLabel statusPcom;
        private ToolStripStatusLabel statusActivePage;
        private ToolStripStatusLabel statusHwProfile;
        private ToolStripStatusLabel statusProjectName;

        // Kontrolki zakładek
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

        // Lista zmiennych obserwowanych
        private class WatchItem
        {
            public string Type;
            public int Address;
            public string Name;
            public string Format;
        }
        private List<WatchItem> allWatchItems = new List<WatchItem>();
        private List<WatchItem> filteredWatchItems = new List<WatchItem>();

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

            // Start serwera PCOM TCP na skonfigurowanym porcie
            int port = (config.PcomPort > 0) ? config.PcomPort : PcomServer.DEFAULT_PORT;
            pcomServer.Start(port);
            pcomServer.OnProjectDownloaded += (projPath) =>
            {
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        LoadProjectFile(projPath);
                        plc.Start();
                    }));
                }
                catch { }
            };

            // Uruchomienie wątku czasu rzeczywistego dla pętli PLC
            scanThread = new Thread(ScanWorkerLoop);
            scanThread.IsBackground = true;
            scanThread.Priority = ThreadPriority.AboveNormal;
            scanThread.Start();

            // Uruchomienie timera odświeżania interfejsu (33 ms = ~30 FPS)
            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 33;
            uiTimer.Tick += OnUiTimerTick;
            uiTimer.Start();

            this.FormClosing += MainForm_FormClosing;

            string hwName = config.Profile != null ? config.Profile.GetModelFullName() : "V350";
            plc.Log(string.Format("Inicjalizacja symulatora {0} zakonczona.", hwName));
            plc.Log("Sterownik jest w czystym stanie fabrycznym (OPLC Standby).");
            plc.Log(string.Format("Uzyj przycisku 'Wgraj projekt VisiLogic' lub polacz sie przez port TCP {0}.", port));
        }

        private void InitializeComponent()
        {
            this.Size = new Size(1340, 860);
            this.MinimumSize = new Size(1100, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(24, 28, 36);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            // ==========================================
            // GŁÓWNE MENU APLIKACJI (MenuStrip)
            // ==========================================
            menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(32, 38, 48),
                ForeColor = Color.White,
                RenderMode = ToolStripRenderMode.System
            };

            // 1. Menu: Plik / File
            mnuFile = new ToolStripMenuItem("Plik");
            mnuLoad = new ToolStripMenuItem("Wgraj projekt VisiLogic (*.vlp, *.vlx, *.udb)...", null, (s, e) => OpenProjectDialog());
            mnuQuickLoad = new ToolStripMenuItem("Wgraj Komora_9_PLC.vlp (szybki wybór z Pulpitu)", null, (s, e) => QuickLoadKomora9());
            mnuClear = new ToolStripMenuItem("Wyczyść program (Czysty OPLC - stan fabryczny)", null, (s, e) => ClearProject());
            mnuExit = new ToolStripMenuItem("Zakończ", null, (s, e) => this.Close());
            mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuLoad, mnuQuickLoad, new ToolStripSeparator(), mnuClear, new ToolStripSeparator(), mnuExit });

            // 2. Menu: Sterowanie PLC / PLC Control
            mnuPlc = new ToolStripMenuItem("Sterowanie PLC");
            mnuRun = new ToolStripMenuItem("START / RUN (F5)", null, (s, e) => StartPlc());
            mnuStop = new ToolStripMenuItem("STOP (F6)", null, (s, e) => StopPlc());
            mnuReset = new ToolStripMenuItem("RESET (F7)", null, (s, e) => ResetPlc());
            mnuPlc.DropDownItems.AddRange(new ToolStripItem[] { mnuRun, mnuStop, mnuReset });

            // 3. Menu: Sprzęt / Hardware
            mnuHardware = new ToolStripMenuItem("Sprzęt");
            mnuHardwareConfig = new ToolStripMenuItem("Konfiguracja sprzętowa sterownika i I/O...", null, (s, e) => OpenHardwareConfigDialog());
            mnuPresetKomora9 = new ToolStripMenuItem("Wczytaj profil: Komora 9 (V350 + IO-DI8-RO8)", null, (s, e) => ApplyHardwareProfile(HardwareProfile.CreateKomora9Profile()));
            mnuPresetV350 = new ToolStripMenuItem("Wczytaj profil: V350 Standalone (Standard)", null, (s, e) => ApplyHardwareProfile(HardwareProfile.CreateDefaultProfile()));
            mnuPresetV570 = new ToolStripMenuItem("Wczytaj profil: V570 Modular + V200-18-E3XB");
            mnuPresetV570.Click += (s, e) =>
            {
                var p = new HardwareProfile();
                p.SelectedModelName = "V570-57-T20B";
                p.SelectedSnapInName = "V200-18-E3XB";
                p.EnableExpansionBus = true;
                p.ExpansionSlots[0].ModuleName = "IO-DI8-RO8";
                ApplyHardwareProfile(p);
            };
            mnuPresetV1040 = new ToolStripMenuItem("Wczytaj profil: V1040 Color (800x600)");
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
                mnuPresetKomora9,
                mnuPresetV350,
                mnuPresetV570,
                mnuPresetV1040
            });

            // 4. Menu: Ustawienia / Settings
            mnuSettings = new ToolStripMenuItem("Ustawienia");
            mnuSettingsGeneral = new ToolStripMenuItem("Ustawienia symulatora...", null, (s, e) => OpenSettingsDialog());

            // Podmenu: Język / Language
            mnuLanguage = new ToolStripMenuItem("Język / Language");
            mnuLangPl = new ToolStripMenuItem("Polski (Polish)", null, (s, e) => SetLanguage(AppLanguage.Polish));
            mnuLangEn = new ToolStripMenuItem("English (Angielski)", null, (s, e) => SetLanguage(AppLanguage.English));
            mnuLanguage.DropDownItems.AddRange(new ToolStripItem[] { mnuLangPl, mnuLangEn });

            // Podmenu: Logowanie / Logging
            mnuLogging = new ToolStripMenuItem("Logowanie");
            mnuLogEnabled = new ToolStripMenuItem("Włącz logowanie do pliku", null, (s, e) => ToggleLogging(!config.LoggingEnabled)) { CheckOnClick = false };
            mnuLogLevel = new ToolStripMenuItem("Szczegółowość logowania");
            mnuLogLevelError = new ToolStripMenuItem("Tylko błędy (Error)", null, (s, e) => SetLogLevel(LogVerbosity.Error));
            mnuLogLevelWarn = new ToolStripMenuItem("Ostrzeżenia i błędy (Warn)", null, (s, e) => SetLogLevel(LogVerbosity.Warn));
            mnuLogLevelInfo = new ToolStripMenuItem("Standardowe informacje (Info)", null, (s, e) => SetLogLevel(LogVerbosity.Info));
            mnuLogLevelDebug = new ToolStripMenuItem("Szczegółowy debug (Debug)", null, (s, e) => SetLogLevel(LogVerbosity.Debug));
            mnuLogLevelTrace = new ToolStripMenuItem("Pełny zrzut pakietów (Trace)", null, (s, e) => SetLogLevel(LogVerbosity.Trace));
            mnuLogLevel.DropDownItems.AddRange(new ToolStripItem[] {
                mnuLogLevelError,
                mnuLogLevelWarn,
                mnuLogLevelInfo,
                mnuLogLevelDebug,
                mnuLogLevelTrace
            });

            mnuOpenLogFile = new ToolStripMenuItem("Otwórz plik logu (virtualplc_debug.log)...");
            mnuOpenLogFile.Click += (s, e) => AppLogger.OpenLogFile();
            mnuClearLogFile = new ToolStripMenuItem("Wyczyść plik logu");
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

            // 5. Menu: Serwer PCOM TCP
            mnuPcom = new ToolStripMenuItem("Serwer PCOM TCP");
            mnuPcomStart = new ToolStripMenuItem("Uruchom serwer PCOM", null, (s, e) => pcomServer.Start((config != null && config.PcomPort > 0) ? config.PcomPort : PcomServer.DEFAULT_PORT));
            mnuPcomStop = new ToolStripMenuItem("Zatrzymaj serwer PCOM", null, (s, e) => pcomServer.Stop());
            mnuPcom.DropDownItems.AddRange(new ToolStripItem[] { mnuPcomStart, mnuPcomStop });

            // 6. Menu: Pomoc / Help
            mnuHelp = new ToolStripMenuItem("Pomoc");
            mnuOpenLog = new ToolStripMenuItem("Otwórz plik logu debugera...", null, (s, e) => AppLogger.OpenLogFile());
            mnuInfo = new ToolStripMenuItem("Informacje systemowe OPLC...", null, (s, e) => ShowInfoScreen());
            mnuAbout = new ToolStripMenuItem("O symulatorze OPLC...", null, (s, e) => ShowAboutDialog());
            mnuHelp.DropDownItems.AddRange(new ToolStripItem[] { mnuOpenLog, new ToolStripSeparator(), mnuInfo, new ToolStripSeparator(), mnuAbout });

            menuStrip.Items.AddRange(new ToolStripItem[] { mnuFile, mnuPlc, mnuHardware, mnuSettings, mnuPcom, mnuHelp });
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            // ==========================================
            // PASEK NARZĘDZIOWY (ToolBar)
            // ==========================================
            toolStrip = new ToolStrip
            {
                BackColor = Color.FromArgb(36, 42, 54),
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(6, 4, 6, 4),
                RenderMode = ToolStripRenderMode.System
            };

            btnToolbarLoad = new ToolStripButton("  📁 WGRAJ PROJEKT  ")
            {
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarLoad.Click += (s, e) => OpenProjectDialog();

            btnToolbarQuick = new ToolStripButton("  ⚡ Komora_9_PLC.vlp  ")
            {
                BackColor = Color.FromArgb(0, 150, 136),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarQuick.Click += (s, e) => QuickLoadKomora9();

            btnToolbarClear = new ToolStripButton("  🧹 Czysty OPLC  ")
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

            btnToolbarHw = new ToolStripButton("  ⚙ SPRZĘT  ")
            {
                BackColor = Color.FromArgb(70, 80, 100),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarHw.Click += (s, e) => OpenHardwareConfigDialog();

            btnToolbarSettings = new ToolStripButton("  🔧 USTAWIENIA  ")
            {
                BackColor = Color.FromArgb(55, 65, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnToolbarSettings.Click += (s, e) => OpenSettingsDialog();

            toolStrip.Items.AddRange(new ToolStripItem[] {
                btnToolbarLoad,
                btnToolbarQuick,
                btnToolbarClear,
                new ToolStripSeparator(),
                btnToolbarRun,
                btnToolbarStop,
                btnToolbarReset,
                new ToolStripSeparator(),
                btnToolbarHw,
                btnToolbarSettings
            });
            this.Controls.Add(toolStrip);

            // ==========================================
            // DOLNY PASEK STATUSU (StatusStrip)
            // ==========================================
            statusStrip = new StatusStrip
            {
                BackColor = Color.FromArgb(18, 22, 30),
                ForeColor = Color.FromArgb(200, 210, 225)
            };

            statusPlcState = new ToolStripStatusLabel("STAN: STOP") { Width = 210, TextAlign = ContentAlignment.MiddleLeft };
            statusScanTime = new ToolStripStatusLabel("Cykl: 0.0 ms") { Width = 170, TextAlign = ContentAlignment.MiddleLeft };
            statusPcom = new ToolStripStatusLabel("PCOM TCP: :20256") { Width = 170, TextAlign = ContentAlignment.MiddleLeft };
            statusActivePage = new ToolStripStatusLabel("Ekran HMI: 1") { Width = 150, TextAlign = ContentAlignment.MiddleLeft };
            statusHwProfile = new ToolStripStatusLabel("Sprzęt: V350-35-RA22") { Width = 230, TextAlign = ContentAlignment.MiddleLeft };
            statusProjectName = new ToolStripStatusLabel("Projekt: [Brak]") { Spring = true, TextAlign = ContentAlignment.MiddleRight };

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
            // GŁÓWNY SPLITCONTAINER
            // Lewo: Panel czołowy V350 / Vision | Prawo: Zakładki
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

            // Panel fizyczny OPLC
            var pnlV350Bezel = CreateV350FrontPanel();
            splitMain.Panel1.Controls.Add(pnlV350Bezel);

            // Prawy Panel: Zakładki
            tabControlMain = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };

            // Zakładka 1: Szafka I/O
            tabIo = new TabPage("Szafka I/O");
            tabIo.BackColor = Color.FromArgb(28, 32, 40);
            tabIo.Controls.Add(hardwarePanel);
            hardwarePanel.Dock = DockStyle.Fill;
            tabControlMain.TabPages.Add(tabIo);

            // Zakładka 2: Tabela Zmiennych
            tabWatch = new TabPage("Tabela Zmiennych");
            tabWatch.BackColor = Color.FromArgb(28, 32, 40);
            InitializeWatchTab(tabWatch);
            tabControlMain.TabPages.Add(tabWatch);

            // Zakładka 3: Podgląd Drabinki
            tabLadder = new TabPage("Drabinka PLC");
            tabLadder.BackColor = Color.FromArgb(28, 32, 40);
            InitializeLadderTab(tabLadder);
            tabControlMain.TabPages.Add(tabLadder);

            // Zakładka 4: Nawigator HMI
            tabHmi = new TabPage("Ekrany HMI");
            tabHmi.BackColor = Color.FromArgb(28, 32, 40);
            InitializeHmiTab(tabHmi);
            tabControlMain.TabPages.Add(tabHmi);

            // Zakładka 5: Dziennik & Logi
            tabLog = new TabPage("Dziennik Zdarzeń");
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

            // Obudowa Vision (ramka przemysłowa)
            var pnlOplcCase = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(42, 48, 60),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlBezel.Controls.Add(pnlOplcCase);

            // Nagłówek obudowy
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

            // Diody LED stanu CPU: PWR, RUN, ERR
            lblPwrLed = CreateLedIndicator("PWR", Color.LimeGreen, 260, 12, pnlTopHeader);
            lblRunLed = CreateLedIndicator("RUN", Color.FromArgb(60, 70, 80), 310, 12, pnlTopHeader);
            lblErrLed = CreateLedIndicator("ERR", Color.FromArgb(60, 70, 80), 360, 12, pnlTopHeader);

            pnlTopHeader.Controls.Add(lblLogoUni);
            pnlTopHeader.Controls.Add(lblLogoModel);
            pnlOplcCase.Controls.Add(pnlTopHeader);

            // Kontener wyświetlacza TFT
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

            // Klawiatura membranowa: 5 klawiszy funkcyjnych F1..F5 fizycznego panelu
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
                Text = "• Ekran Touchscreen: kliknij na ekranie, aby wciskać przyciski HMI lub przełączać ekrany.\n• Klawisze F1..F5: sterują bitami SB 31..SB 35 sterownika PLC.",
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
        // ZAKŁADKA TABELA ZMIENNYCH (LIVE WATCH)
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

            lblWatchFilt = new Label { Text = "Filtr typu:", Left = 10, Top = 10, AutoSize = true, ForeColor = Color.FromArgb(200, 215, 235) };
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
                "Wszystkie zmienne z projektu",
                "Wejścia fizyczne (I)",
                "Wyjścia fizyczne (O)",
                "Bity pamięci (MB)",
                "Rejestry całkowite (MI)",
                "Rejestry długie (ML / DW)",
                "Timery (T)",
                "Operandy systemowe (SB / SI)"
            });
            cboWatchFilter.SelectedIndex = 0;
            cboWatchFilter.SelectedIndexChanged += (s, e) => FilterWatchItems();

            lblWatchSearch = new Label { Text = "Szukaj:", Left = 300, Top = 10, AutoSize = true, ForeColor = Color.FromArgb(200, 215, 235) };
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
                Text = "✏ Zmień / Wymuś wartość (Force)",
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

            dgvWatch.Columns.Add("ColType", "Typ");
            dgvWatch.Columns.Add("ColAddr", "Adres");
            dgvWatch.Columns.Add("ColName", "Nazwa / Opis operandu z VisiLogic");
            dgvWatch.Columns.Add("ColVal", "Wartość Live");
            dgvWatch.Columns.Add("ColFmt", "Jednostka / Format");

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

            // Drzewo podprogramów (Subroutines)
            var pnlLeft = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 32, 40) };
            var lblTreeHdr = new Label
            {
                Text = "Podprogramy (Subroutines):",
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

            // Podgląd szczebli wybranego podprogramu
            var pnlRight = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 24, 30) };
            var lblRungHdr = new Label
            {
                Text = "Szczeble drabinki LAD (Rungs):",
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
                Text = "▶ Przełącz na zaznaczony ekran HMI",
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
                Text = "Podwójne kliknięcie natychmiast przełącza SI 9 (Current Display ID).",
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
            lvHmiPages.Columns.Add("ID Ekranu", 90);
            lvHmiPages.Columns.Add("Nazwa / Tytuł ekranu", 300);
            lvHmiPages.Columns.Add("Liczba kontrolek", 180);
            lvHmiPages.Columns.Add("Kolor tła", 120);

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
                Text = "Wyczyść dziennik",
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
                Text = "Kopiuj do schowka",
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
                Text = "📄 Otwórz plik logu (debug.log)",
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
                Text = "Logi zapisywane na bieżąco do: virtualplc_debug.log (w katalogu aplikacji)",
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
        // DIALOGI I KONFIGURACJA SPRZĘTOWA / OPCJE
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

            plc.Log("Zastosowano nowy profil sprzetowy: " + newProfile.GetModelFullName());
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
            string projName = (plc.Project != null) ? plc.Project.ProjectName : Localization.T("Status_NoProgram");
            string hwFullName = config.Profile != null ? config.Profile.GetModelFullName() : "V350";
            this.Text = string.Format(Localization.T("AppTitle"), "0.0.4", hwFullName + " - " + projName);

            // Menu: Plik
            mnuFile.Text = Localization.T("Menu_File");
            mnuLoad.Text = Localization.T("Menu_LoadProject");
            mnuQuickLoad.Text = Localization.T("Menu_QuickLoadKomora9");
            mnuClear.Text = Localization.T("Menu_ClearProject");
            mnuExit.Text = Localization.T("Menu_Exit");

            // Menu: Sterowanie PLC
            mnuPlc.Text = Localization.T("Menu_Plc");
            mnuRun.Text = Localization.T("Menu_Start");
            mnuStop.Text = Localization.T("Menu_Stop");
            mnuReset.Text = Localization.T("Menu_Reset");

            // Menu: Sprzęt
            mnuHardware.Text = Localization.T("Menu_Hardware");
            mnuHardwareConfig.Text = Localization.T("Menu_HardwareConfig");
            mnuPresetKomora9.Text = Localization.T("HwCfg_Preset_Komora9");
            mnuPresetV350.Text = Localization.T("HwCfg_Preset_Default");

            // Menu: Ustawienia
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

            // Menu: Pomoc
            mnuHelp.Text = Localization.T("Menu_Help");
            mnuOpenLog.Text = Localization.T("Menu_OpenLog");
            mnuInfo.Text = Localization.T("Menu_Info");
            mnuAbout.Text = Localization.T("Menu_About");

            // Pasek narzędziowy
            btnToolbarLoad.Text = "  📁 " + Localization.T("Menu_LoadProject").Replace("...", "") + "  ";
            btnToolbarClear.Text = "  🧹 " + Localization.T("Menu_ClearProject") + "  ";
            btnToolbarRun.Text = "  ▶ " + Localization.T("Menu_Start") + "  ";
            btnToolbarStop.Text = "  ⏹ " + Localization.T("Menu_Stop") + "  ";
            btnToolbarReset.Text = "  🔄 " + Localization.T("Menu_Reset") + "  ";
            btnToolbarHw.Text = "  ⚙ " + Localization.T("Menu_Hardware") + "  ";
            btnToolbarSettings.Text = "  🔧 " + Localization.T("Menu_Settings") + "  ";

            // Zakładki
            tabIo.Text = Localization.T("Tab_Hardware");
            tabWatch.Text = Localization.T("Tab_Watch");
            tabLadder.Text = Localization.T("Tab_Subroutines");
            tabHmi.Text = Localization.T("Tab_HmiPages");
            tabLog.Text = Localization.T("Tab_Diagnostics");

            // Zakładka Watch
            if (lblWatchFilt != null) lblWatchFilt.Text = (Localization.CurrentLanguage == AppLanguage.Polish) ? "Filtr typu:" : "Type filter:";
            if (lblWatchSearch != null) lblWatchSearch.Text = (Localization.CurrentLanguage == AppLanguage.Polish) ? "Szukaj:" : "Search:";
            if (btnWatchForce != null) btnWatchForce.Text = (Localization.CurrentLanguage == AppLanguage.Polish) ? "✏ Zmień / Wymuś wartość (Force)" : "✏ Change / Force Value";

            if (dgvWatch != null && dgvWatch.Columns.Count >= 5)
            {
                dgvWatch.Columns[0].HeaderText = Localization.T("Watch_Col_Type");
                dgvWatch.Columns[1].HeaderText = Localization.T("Watch_Col_Addr");
                dgvWatch.Columns[2].HeaderText = Localization.T("Watch_Col_Symbol");
                dgvWatch.Columns[3].HeaderText = (Localization.CurrentLanguage == AppLanguage.Polish) ? "Wartość Live" : "Live Value";
                dgvWatch.Columns[4].HeaderText = (Localization.CurrentLanguage == AppLanguage.Polish) ? "Jednostka / Format" : "Format";
            }

            // Podpowiedzi na obudowie
            if (lblTouchHint != null && config != null && config.Profile != null)
            {
                var curSpec = config.Profile.GetCurrentModelSpec();
                lblTouchHint.Text = (Localization.CurrentLanguage == AppLanguage.Polish)
                    ? string.Format("• Ekran {0} Touchscreen: kliknij na ekranie, aby wciskać przyciski HMI lub przełączać ekrany.\n• Klawisze F1..F5: sterują bitami SB 31..SB 35 sterownika PLC.", curSpec.DisplayType)
                    : string.Format("• Display {0} Touchscreen: click on the screen to operate HMI controls.\n• Keys F1..F5: control PLC system bits SB 31..SB 35.", curSpec.DisplayType);
            }

            UpdateStatusLabels();
        }

        private void UpdateStatusLabels()
        {
            if (statusPlcState == null) return;

            // 1. Stan PLC
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

            // 2. Czas cyklu i PCOM
            statusScanTime.Text = string.Format(Localization.T("Status_ScanTime"), plc.LastScanDurationMs, 20);

            int port = (config != null && config.PcomPort > 0) ? config.PcomPort : PcomServer.DEFAULT_PORT;
            statusPcom.Text = pcomServer.IsActive
                ? string.Format(Localization.T("Status_PcomListening"), port)
                : Localization.T("Status_PcomStopped");

            // 3. Aktywny ekran HMI
            int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
            string pageName = (plc.Project != null && plc.Project.Pages.ContainsKey(activePage)) ? plc.Project.Pages[activePage].Name : "";
            statusActivePage.Text = string.Format(Localization.T("Status_Page"), activePage, pageName);

            // 4. Profil sprzętowy
            string hwName = (config != null && config.Profile != null) ? config.Profile.GetModelFullName() : "V350";
            statusHwProfile.Text = string.Format(Localization.T("Status_HardwareProfile"), hwName);

            // 5. Nazwa projektu
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
        // PĘTLA WYKONYWANIA PROGRAMU PLC (BACKGROUND)
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
                    plc.Log("Błąd w cyklu skanowania PLC: " + ex.Message);
                }
            }
        }

        // ==========================================
        // ODŚWIEŻANIE INTERFEJSU UŻYTKOWNIKA (30 FPS)
        // ==========================================
        private int watchTableRefreshCounter = 0;
        private void OnUiTimerTick(object sender, EventArgs e)
        {
            // 1. Renderowanie ekranu HMI
            int activePage = (plc.SI[9] > 0) ? plc.SI[9] : 1;
            picScreen.Image = hmiRenderer.RenderScreen(activePage);

            // 2. Aktualizacja diod LED na obudowie
            lblPwrLed.BackColor = Color.LimeGreen;
            lblRunLed.BackColor = (plc.State == PlcRunState.RUN) ? Color.LimeGreen : Color.FromArgb(50, 65, 50);
            lblErrLed.BackColor = (!plc.HasProgramLoaded || plc.State == PlcRunState.STOP) ? Color.FromArgb(200, 40, 40) : Color.FromArgb(40, 20, 20);

            // 3. Aktualizacja paska stanu
            UpdateStatusLabels();

            // 4. Odświeżanie wartości w tabeli Live Watch (co ~100 ms)
            watchTableRefreshCounter++;
            if (watchTableRefreshCounter >= 3 && tabControlMain.SelectedIndex == 1)
            {
                watchTableRefreshCounter = 0;
                RefreshWatchTableValues();
            }
        }

        // ==========================================
        // OBSŁUGA ŁADOWANIA I RESETU PROJEKTU
        // ==========================================
        public void LoadProjectFile(string filePath)
        {
            try
            {
                plc.Log("Wczytywanie projektu z pliku: " + filePath);
                var proj = VlpParser.LoadProjectFile(filePath, plc.Log);

                plc.Stop();
                plc.Project = proj;
                plc.ResetMemory();

                hardwarePanel.RefreshOperandLabels();
                PopulateWatchTable(proj);
                PopulateSubroutineTree(proj);
                PopulateHmiPageList(proj);

                ApplyLanguage();

                // Start programu po załadowaniu
                plc.Start();
                plc.Log("Projekt załadowany pomyślnie. Drabinka została uruchomiona (RUN).");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nie udało się załadować projektu:\n" + ex.Message, "Błąd VisiLogic", MessageBoxButtons.OK, MessageBoxIcon.Error);
                plc.Log("BŁĄD ŁADOWANIA PROJEKTU: " + ex.Message);
            }
        }

        private void OpenProjectDialog()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = (Localization.CurrentLanguage == AppLanguage.Polish) ? "Wybierz projekt VisiLogic OPLC" : "Select VisiLogic OPLC Project";
                ofd.Filter = "Projekty VisiLogic (*.vlp;*.vlx;*.udb)|*.vlp;*.vlx;*.udb|Wszystkie pliki (*.*)|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadProjectFile(ofd.FileName);
                }
            }
        }

        private void QuickLoadKomora9()
        {
            string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                @"NARZĘDZIA\Visilogic\Projekty aktualne\Komora nr. 9 Gardno\Komora_9_PLC.vlp");

            if (File.Exists(desktopPath))
            {
                LoadProjectFile(desktopPath);
                return;
            }

            // Alternatywne ścieżki
            string altPath = @"C:\Users\Panasonic\Desktop\NARZĘDZIA\Visilogic\Projekty aktualne\Komora nr. 9 Gardno\Komora_9_PLC.vlp";
            if (File.Exists(altPath))
            {
                LoadProjectFile(altPath);
                return;
            }

            MessageBox.Show("Nie odnaleziono domyślnego pliku Komora_9_PLC.vlp na Pulpicie.\nWskaż plik projektu ręcznie.", "Szybki wybór", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenProjectDialog();
        }

        private void ClearProject()
        {
            var res = MessageBox.Show(
                (Localization.CurrentLanguage == AppLanguage.Polish)
                    ? "Czy na pewno chcesz wyczyścić program i przywrócić sterownik do czystego stanu fabrycznego?"
                    : "Are you sure you want to clear the program and return controller to factory standby state?",
                "Czysty OPLC", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res != DialogResult.Yes) return;

            plc.Stop();
            plc.Project = null;
            plc.Reset();

            allWatchItems.Clear();
            filteredWatchItems.Clear();
            dgvWatch.Rows.Clear();
            tvSubroutines.Nodes.Clear();
            rtbLadderView.Clear();
            lvHmiPages.Items.Clear();

            ApplyLanguage();
            plc.Log("Pamięć sterownika wyczyszczona. Sterownik powrócił do fabrycznego stanu OPLC (czysty sprzęt).");
        }

        private void StartPlc()
        {
            if (!plc.HasProgramLoaded)
            {
                MessageBox.Show(
                    (Localization.CurrentLanguage == AppLanguage.Polish)
                        ? "Nie można uruchomić PLC w trybie RUN bez załadowanego programu z VisiLogic!\nUżyj przycisku 'WGRAJ PROJEKT' aby załadować kod drabinkowy."
                        : "Cannot start PLC in RUN mode without a loaded VisiLogic project!\nUse 'LOAD PROJECT' button to load ladder code.",
                    "Brak programu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            if (plc.HasProgramLoaded)
            {
                plc.Start();
            }
        }

        // ==========================================
        // TABELA ZMIENNYCH (LIVE WATCH)
        // ==========================================
        private void PopulateWatchTable(LoadedProject proj)
        {
            allWatchItems.Clear();

            // 1. Wejścia fizyczne (liczba punktów zgodna z profilem sprzętowym)
            int totalDi = (plc.Profile != null) ? plc.Profile.GetTotalDi() : 20;
            for (int i = 0; i < totalDi; i++)
            {
                string key = "I" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("Wejście cyfrowe DI " + i);
                allWatchItems.Add(new WatchItem { Type = "I", Address = i, Name = name, Format = "Bit (0/1)" });
            }

            // 2. Wyjścia fizyczne (liczba punktów zgodna z profilem sprzętowym)
            int totalDo = (plc.Profile != null) ? plc.Profile.GetTotalDo() : 16;
            for (int i = 0; i < totalDo; i++)
            {
                string key = "O" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("Wyjście cyfrowe DO " + i);
                allWatchItems.Add(new WatchItem { Type = "O", Address = i, Name = name, Format = "Bit (0/1)" });
            }

            // 3. Wejścia analogowe (AI)
            int totalAi = (plc.Profile != null) ? plc.Profile.GetTotalAi() : 2;
            for (int i = 0; i < totalAi; i++)
            {
                string key = "AN" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("Wejście analogowe AI " + i);
                allWatchItems.Add(new WatchItem { Type = "MI", Address = 100 + i, Name = name, Format = "Analog (0..1023)" });
            }

            // 4. Pełna pula bitów pamięci MB 0..255
            for (int i = 0; i <= 255; i++)
            {
                string key = "MB" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("MB " + i);
                allWatchItems.Add(new WatchItem { Type = "MB", Address = i, Name = name, Format = "Bit (0/1)" });
            }

            // 5. Pełna pula rejestrów całkowitych MI 0..127
            for (int i = 0; i <= 127; i++)
            {
                string key = "MI" + i;
                string name = proj.OperandDescriptions.ContainsKey(key) ? proj.OperandDescriptions[key] : ("MI " + i);
                allWatchItems.Add(new WatchItem { Type = "MI", Address = i, Name = name, Format = "Integer" });
            }

            // 6. Dodatkowe nazwane operandy z projektu
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
                else if (key.StartsWith("T")) { type = "T"; int.TryParse(key.Substring(1), out addr); }
                else if (key.StartsWith("SB")) { type = "SB"; int.TryParse(key.Substring(2), out addr); }
                else if (key.StartsWith("SI")) { type = "SI"; int.TryParse(key.Substring(2), out addr); }

                if (!string.IsNullOrEmpty(type) && !allWatchItems.Exists(x => x.Type == type && x.Address == addr))
                {
                    string fmt = (type == "MB" || type == "SB") ? "Bit (0/1)" : (type == "T" ? "Timer (ms)" : "Integer");
                    allWatchItems.Add(new WatchItem { Type = type, Address = addr, Name = name, Format = fmt });
                }
            }

            // 7. Kluczowe operandy systemowe
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
                // Przełącz stan bitu
                if (item.Type == "I") plc.I[item.Address] = !plc.I[item.Address];
                else if (item.Type == "O") plc.O[item.Address] = !plc.O[item.Address];
                else if (item.Type == "MB") plc.MB[item.Address] = !plc.MB[item.Address];
                else if (item.Type == "SB") plc.SB[item.Address] = !plc.SB[item.Address];

                plc.Log(string.Format("Wymuszono przełączenie bitu {0}{1}", item.Type, item.Address));
                RefreshWatchTableValues();
            }
            else
            {
                // Okno wprowadzania wartości liczbowej
                string input = PromptDialog.Show(
                    string.Format("Podaj nową wartość dla {0}{1} ({2}):", item.Type, item.Address, item.Name),
                    "Wymuszenie wartości rejestru (Force Value)",
                    GetOperandLiveString(item.Type, item.Address));

                int val = 0;
                if (int.TryParse(input, out val))
                {
                    if (item.Type == "MI" && item.Address >= 0 && item.Address < plc.MI.Length) plc.MI[item.Address] = (short)val;
                    else if (item.Type == "ML" && item.Address >= 0 && item.Address < plc.ML.Length) plc.ML[item.Address] = val;
                    else if (item.Type == "SI" && item.Address >= 0 && item.Address < plc.SI.Length) plc.SI[item.Address] = (short)val;

                    plc.Log(string.Format("Wymuszono nową wartość {0}{1} = {2}", item.Type, item.Address, val));
                    RefreshWatchTableValues();
                }
            }
        }

        // ==========================================
        // PODGLĄD DRABINKI PLC (LADDER LOGIC)
        // ==========================================
        private void PopulateSubroutineTree(LoadedProject proj)
        {
            tvSubroutines.Nodes.Clear();
            foreach (var kvp in proj.Subroutines)
            {
                var sub = kvp.Value;
                var node = new TreeNode(string.Format("{0} [{1} szczebli]", sub.Name, sub.Rungs.Count))
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
            rtbLadderView.AppendText(string.Format("=== PODPROGRAM: {0} (ID: {1}) ===\n\n", sub.Name, sub.FuncId));

            foreach (var rung in sub.Rungs)
            {
                rtbLadderView.SelectionColor = Color.FromArgb(0, 220, 255);
                rtbLadderView.AppendText(string.Format("[ SZCZEBEL #{0} | StripID: {1} ]\n", rung.SeqNum, rung.StripId));

                if (!string.IsNullOrEmpty(rung.Comment))
                {
                    rtbLadderView.SelectionColor = Color.FromArgb(170, 185, 205);
                    rtbLadderView.AppendText("  // " + rung.Comment + "\n");
                }

                if (rung.Elements.Count == 0)
                {
                    rtbLadderView.SelectionColor = Color.FromArgb(100, 110, 125);
                    rtbLadderView.AppendText("  --[ Pusty szczebel ]--\n\n");
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
                opVal = " [STAN: " + GetOperandLiveString(op.Type, op.Address) + "]";
            }

            switch (elm.ElementType)
            {
                case 200:
                    elDesc = "  --| |---  [STYK NO]       " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(144, 202, 249);
                    break;
                case 201:
                    elDesc = "  --|/|---  [STYK NC]       " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(255, 171, 145);
                    break;
                case 202:
                    elDesc = "  --|^|---  [ZBOCZE ROSNĄCE] " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(255, 245, 157);
                    break;
                case 203:
                    elDesc = "  --|v|---  [ZBOCZE OPADAJĄCE]" + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(255, 245, 157);
                    break;
                case 204:
                    elDesc = "  --( )---  [CEWKA DIRECT]   " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(129, 199, 132);
                    break;
                case 205:
                    elDesc = "  --( / )-- [CEWKA NEGATED]  " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(255, 138, 128);
                    break;
                case 206:
                    elDesc = "  --( S )-- [CEWKA SET]      " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(105, 240, 174);
                    break;
                case 207:
                    elDesc = "  --( R )-- [CEWKA RESET]    " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(255, 82, 82);
                    break;
                case 212:
                    elDesc = "  --( T )-- [CEWKA TOGGLE]   " + FormatOpRef(elm.Operands, 0) + opName;
                    elCol = Color.FromArgb(255, 215, 64);
                    break;
                case 601:
                    elDesc = "  -[STORE]- " + FormatOpRef(elm.Operands, 0) + " ==> " + FormatOpRef(elm.Operands, 1);
                    elCol = Color.FromArgb(179, 157, 219);
                    break;
                case 900:
                    elDesc = string.Format("  -[CALL SUBROUTINE #{0}]-", elm.MoreParamsLng);
                    elCol = Color.FromArgb(255, 204, 0);
                    break;
                case 1300:
                    elDesc = "  -[TIMER]- " + FormatOpRef(elm.Operands, 0) + opName + opVal;
                    elCol = Color.FromArgb(128, 222, 234);
                    break;
                default:
                    elDesc = string.Format("  -[BLOK TYP {0}]- ", elm.ElementType);
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
        // NAWIGATOR EKRANÓW HMI
        // ==========================================
        private void PopulateHmiPageList(LoadedProject proj)
        {
            lvHmiPages.Items.Clear();
            foreach (var kvp in proj.Pages)
            {
                var page = kvp.Value;
                var item = new ListViewItem(page.Id.ToString());
                item.SubItems.Add(page.Name);
                item.SubItems.Add(page.Elements.Count.ToString() + " kontrolek");
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
            plc.Log(string.Format("Przełączono aktywny ekran HMI na ID {0} ({1})", pageId, lvHmiPages.SelectedItems[0].SubItems[1].Text));
            picScreen.Image = hmiRenderer.RenderScreen(pageId);
            picScreen.Refresh();
        }

        // ==========================================
        // KLAWISZE MEMBRANOWE
        // ==========================================
        private void PulseFunctionKey(int fIdx, bool isDown)
        {
            int sbIdx = 30 + fIdx; // SB 31 .. SB 35
            if (sbIdx < plc.SB.Length)
            {
                plc.SB[sbIdx] = isDown;
            }
            if (isDown)
            {
                plc.Log(string.Format("Wciśnięto klawisz funkcyjny F{0} (SB {1} = 1)", fIdx, sbIdx));
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
                "Model sterownika: {0}\n" +
                "Stan PLC:         {1}\n" +
                "Załadowany:       {2}\n" +
                "Czas cyklu:       {3:0.0} ms\n" +
                "Licznik cykli:    {4}\n" +
                "Port PCOM TCP:    {5} ({6})\n" +
                "Aktywny ekran:    ID {7}\n" +
                "Liczba szczebli:  {8}\n" +
                "Logowanie aktywne:{9} (Poziom: {10})\n" +
                "Język interfejsu: {11}\n",
                hwName,
                plc.State,
                plc.Project != null ? plc.Project.ProjectName : "[Brak]",
                plc.LastScanDurationMs,
                plc.ScanCount,
                config.PcomPort,
                pcomServer.IsActive ? "Nasłuchuje" : "Zatrzymany",
                plc.SI[9],
                plc.Project != null ? GetTotalRungsCount(plc.Project) : 0,
                config.LoggingEnabled ? "TAK" : "NIE",
                config.LoggingVerbosity,
                Localization.CurrentLanguage
            );

            MessageBox.Show(info, "Informacje systemowe OPLC", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            string msg =
                "Unitronics Vision PLC/HMI Simulator v0.0.4 [Portable]\n\n" +
                "Wersja: 0.0.4 Portable (Natywna aplikacja Windows .NET 4.0 x86)\n" +
                "Aktywny profil: " + hwName + "\n" +
                "Język: " + Localization.CurrentLanguage + "\n\n" +
                "Właściwości:\n" +
                "• Panel konfiguracji sprzętowej na starcie (Wybór sterownika Vision V130..V1210 i modułów EX-A2X)\n" +
                "• Pełny dwujęzyczny interfejs (Polski i English) przełączany w czasie rzeczywistym\n" +
                "• Konfigurowalne opcje logowania diagnostycznego (Error, Warn, Info, Debug, Trace)\n" +
                "• Bezpośredni odczyt projektów VisiLogic (*.vlp, *.vlx, *.udb)\n" +
                "• Silnik Soft-PLC z ewaluacją logiki drabinkowej\n" +
                "• Renderer graficzny ekranów HMI z obsługą dotyku i wirtualną klawiaturą Keypad\n" +
                "• Wirtualna szafka I/O z dynamiczną magistralą EX-A2X i potencjometrami analogowymi\n" +
                "• Serwer PCOM TCP zgodny z VisiLogic (Download & Monitor)\n\n" +
                "Plik logu debugera: " + AppLogger.LogFilePath;

            MessageBox.Show(msg, "O programie Unitronics Vision Simulator", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            AppLogger.Info("Zamykanie okna głównego symulatora (FormClosing)...");
            isScanThreadRunning = false;
            if (uiTimer != null) uiTimer.Stop();
            if (pcomServer != null) pcomServer.Stop();
            if (plc != null) plc.Stop();
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
                Button cancel = new Button() { Text = "Anuluj", Left = 295, Width = 85, Height = 28, Top = 85, DialogResult = DialogResult.Cancel, BackColor = Color.FromArgb(60, 68, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };

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
