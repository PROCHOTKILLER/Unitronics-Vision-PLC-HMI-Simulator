using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace UnitronicsV350Simulator
{
    public class HardwarePanel : UserControl
    {
        private PlcEngine plc;
        private HardwareProfile profile;

        private TabControl tabControl;
        private TabPage tabDi;
        private TabPage tabDo;
        private TabPage tabAi;
        private TabPage tabAo;

        private FlowLayoutPanel flowInputsCpu;
        private List<FlowLayoutPanel> flowInputsExt = new List<FlowLayoutPanel>();

        private FlowLayoutPanel flowOutputsCpu;
        private List<FlowLayoutPanel> flowOutputsExt = new List<FlowLayoutPanel>();

        private Panel panelAnalogInputs;
        private Panel panelAnalogOutputs;

        // Digital input controls (up to 128)
        private CheckBox[] chkInputs = new CheckBox[128];
        private Label[] lblInputLeds = new Label[128];
        private Label[] lblInputTexts = new Label[128];
        private Button[] btnMomentaryInputs = new Button[128];

        // Digital output controls (up to 128)
        private Label[] lblOutputLeds = new Label[128];
        private Label[] lblOutputTexts = new Label[128];

        // Analog input controls (up to 16)
        private TrackBar[] trackAnalogs = new TrackBar[16];
        private NumericUpDown[] numAnalogs = new NumericUpDown[16];
        private Label[] lblAnalogLabels = new Label[16];

        // Analog output controls (up to 8)
        private Label[] lblAoVoltages = new Label[8];
        private ProgressBar[] barAoVoltages = new ProgressBar[8];

        public HardwarePanel(PlcEngine engine)
        {
            plc = engine;
            profile = engine.Profile ?? HardwareProfile.CreateDefaultProfile();

            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(28, 32, 40);
            this.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            this.ForeColor = Color.White;

            BuildLayout();
            plc.OnScanCompleted += OnPlcScanCompleted;
            Localization.OnLanguageChanged += ApplyLanguage;
        }

        public void RebuildLayout(HardwareProfile newProfile)
        {
            profile = newProfile ?? HardwareProfile.CreateDefaultProfile();
            this.Controls.Clear();
            flowInputsExt.Clear();
            flowOutputsExt.Clear();
            Array.Clear(chkInputs, 0, chkInputs.Length);
            Array.Clear(lblInputLeds, 0, lblInputLeds.Length);
            Array.Clear(lblInputTexts, 0, lblInputTexts.Length);
            Array.Clear(btnMomentaryInputs, 0, btnMomentaryInputs.Length);
            Array.Clear(lblOutputLeds, 0, lblOutputLeds.Length);
            Array.Clear(lblOutputTexts, 0, lblOutputTexts.Length);
            Array.Clear(trackAnalogs, 0, trackAnalogs.Length);
            Array.Clear(numAnalogs, 0, numAnalogs.Length);
            Array.Clear(lblAnalogLabels, 0, lblAnalogLabels.Length);
            Array.Clear(lblAoVoltages, 0, lblAoVoltages.Length);
            Array.Clear(barAoVoltages, 0, barAoVoltages.Length);

            BuildLayout();
            ApplyProjectDescriptions();
        }

        private void BuildLayout()
        {
            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;

            // --- Tab 1: Digital Inputs (DI) ---
            tabDi = new TabPage(Localization.T("Hw_Tab_DI"));
            tabDi.BackColor = Color.FromArgb(28, 32, 40);

            var pnlDiContainer = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            BuildDynamicInputs(pnlDiContainer);
            tabDi.Controls.Add(pnlDiContainer);

            // --- Tab 2: Digital Outputs (DO) ---
            tabDo = new TabPage(Localization.T("Hw_Tab_DO"));
            tabDo.BackColor = Color.FromArgb(28, 32, 40);

            var pnlDoContainer = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            BuildDynamicOutputs(pnlDoContainer);
            tabDo.Controls.Add(pnlDoContainer);

            // --- Tab 3: Analog Inputs (AI) ---
            tabAi = new TabPage(Localization.T("Hw_Tab_AI"));
            tabAi.BackColor = Color.FromArgb(28, 32, 40);

            panelAnalogInputs = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            BuildDynamicAnalogInputs(panelAnalogInputs);
            tabAi.Controls.Add(panelAnalogInputs);

            // --- Tab 4: Analog Outputs (AO) ---
            tabAo = new TabPage(Localization.T("Hw_Tab_AO"));
            tabAo.BackColor = Color.FromArgb(28, 32, 40);

            panelAnalogOutputs = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            BuildDynamicAnalogOutputs(panelAnalogOutputs);
            tabAo.Controls.Add(panelAnalogOutputs);

            tabControl.TabPages.Add(tabDi);
            tabControl.TabPages.Add(tabDo);
            tabControl.TabPages.Add(tabAi);
            tabControl.TabPages.Add(tabAo);

            this.Controls.Add(tabControl);
        }

        private void BuildDynamicInputs(Panel container)
        {
            var modelSpec = profile.GetCurrentModelSpec();
            var snapSpec = profile.GetCurrentSnapInSpec();
            int cpuDiCount = modelSpec.IsModular ? snapSpec.Di : modelSpec.OnboardDi;
            string cpuTitle = modelSpec.IsModular
                ? string.Format("{0} [{1}]", modelSpec.ModelName, snapSpec.ModelName)
                : modelSpec.ModelName;

            int currentInputAddr = 0;
            int xOffset = 10;

            // 1. CPU / Snap-in inputs group
            if (cpuDiCount > 0)
            {
                var grpCpu = new GroupBox
                {
                    Text = string.Format(Localization.T("Hw_Grp_CpuInputs"), cpuTitle),
                    Location = new Point(xOffset, 10),
                    Size = new Size(250, Math.Max(200, cpuDiCount * 30 + 35)),
                    ForeColor = Color.FromArgb(0, 220, 255)
                };

                flowInputsCpu = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false
                };

                for (int i = 0; i < cpuDiCount; i++)
                {
                    flowInputsCpu.Controls.Add(CreateInputRow(currentInputAddr++));
                }
                grpCpu.Controls.Add(flowInputsCpu);
                container.Controls.Add(grpCpu);
                xOffset += 265;
            }

            // 2. EX-A2X expansion module input groups (Unitronics expansion slots start at I32, +16 per slot)
            if (profile.EnableExpansionBus && profile.ExpansionSlots != null)
            {
                for (int s = 0; s < profile.ExpansionSlots.Count; s++)
                {
                    var slot = profile.ExpansionSlots[s];
                    var expSpec = profile.GetExpansionSpec(slot.ModuleName);
                    if (expSpec.Di > 0)
                    {
                        int startAddr = Math.Max(currentInputAddr, 32 + s * 16);
                        currentInputAddr = startAddr;
                        int endAddr = startAddr + expSpec.Di - 1;

                        var grpSlot = new GroupBox
                        {
                            Text = string.Format(Localization.T("Hw_Grp_ExtSlot"), slot.SlotNumber, expSpec.ModelName + string.Format(" (I{0}..I{1})", startAddr, endAddr)),
                            Location = new Point(xOffset, 10),
                            Size = new Size(250, Math.Max(200, expSpec.Di * 30 + 35)),
                            ForeColor = Color.FromArgb(0, 220, 255)
                        };

                        var flowExt = new FlowLayoutPanel
                        {
                            Dock = DockStyle.Fill,
                            AutoScroll = true,
                            FlowDirection = FlowDirection.TopDown,
                            WrapContents = false
                        };

                        for (int i = 0; i < expSpec.Di; i++)
                        {
                            flowExt.Controls.Add(CreateInputRow(currentInputAddr++));
                        }

                        grpSlot.Controls.Add(flowExt);
                        flowInputsExt.Add(flowExt);
                        container.Controls.Add(grpSlot);
                        xOffset += 265;
                    }
                }
            }
        }

        private Panel CreateInputRow(int inputIdx)
        {
            var rowPanel = new Panel
            {
                Width = 230,
                Height = 28,
                Margin = new Padding(2)
            };

            bool initState = (inputIdx < plc.I.Length) && plc.I[inputIdx];

            // Status LED
            var led = new Label
            {
                Width = 14,
                Height = 14,
                Left = 4,
                Top = 7,
                BackColor = initState ? Color.FromArgb(0, 230, 118) : Color.FromArgb(60, 65, 75),
                BorderStyle = BorderStyle.FixedSingle
            };
            lblInputLeds[inputIdx] = led;

            // Latching toggle checkbox
            var chk = new CheckBox
            {
                Text = "I" + inputIdx,
                Left = 24,
                Top = 4,
                Width = 50,
                Checked = initState,
                ForeColor = Color.White
            };
            chk.CheckedChanged += (s, e) =>
            {
                if (inputIdx < plc.I.Length && plc.I[inputIdx] != chk.Checked)
                {
                    plc.I[inputIdx] = chk.Checked;
                    if (plc.SimProfile != null && plc.SimProfile.InputRules != null && plc.SimProfile.InputRules.ContainsKey(inputIdx))
                    {
                        plc.SimProfile.InputRules[inputIdx].ForcedState = chk.Checked;
                    }
                }
                UpdateLedColor(led, chk.Checked, Color.FromArgb(0, 230, 118));
            };
            chkInputs[inputIdx] = chk;

            // Momentary pulse button
            var btnMom = new Button
            {
                Text = Localization.T("Hw_Btn_Pulse"),
                Left = 78,
                Top = 3,
                Width = 46,
                Height = 22,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 55, 68),
                ForeColor = Color.FromArgb(200, 220, 240),
                Font = new Font("Segoe UI", 7f)
            };
            btnMom.FlatAppearance.BorderColor = Color.FromArgb(80, 90, 110);
            btnMom.MouseDown += (s, e) => { if (inputIdx < plc.I.Length) plc.I[inputIdx] = true; chk.Checked = true; };
            btnMom.MouseUp += (s, e) => { if (inputIdx < plc.I.Length) plc.I[inputIdx] = false; chk.Checked = false; };
            btnMomentaryInputs[inputIdx] = btnMom;

            // Operand symbol / description
            var lblDesc = new Label
            {
                Text = GetDefaultInputName(inputIdx),
                Left = 128,
                Top = 7,
                Width = 100,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(170, 185, 205),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular)
            };
            lblInputTexts[inputIdx] = lblDesc;

            rowPanel.Controls.AddRange(new Control[] { led, chk, btnMom, lblDesc });
            return rowPanel;
        }

        private void BuildDynamicOutputs(Panel container)
        {
            var modelSpec = profile.GetCurrentModelSpec();
            var snapSpec = profile.GetCurrentSnapInSpec();
            int cpuDoCount = modelSpec.IsModular ? snapSpec.TotalDo : modelSpec.TotalOnboardDo;
            int cpuRelayCount = modelSpec.IsModular ? snapSpec.DoRelay : modelSpec.OnboardDoRelay;
            string cpuTitle = modelSpec.IsModular
                ? string.Format("{0} [{1}]", modelSpec.ModelName, snapSpec.ModelName)
                : modelSpec.ModelName;

            int currentOutputAddr = 0;
            int xOffset = 10;

            // 1. CPU outputs group
            if (cpuDoCount > 0)
            {
                var grpCpu = new GroupBox
                {
                    Text = string.Format(Localization.T("Hw_Grp_CpuOutputs"), cpuTitle),
                    Location = new Point(xOffset, 10),
                    Size = new Size(250, Math.Max(200, cpuDoCount * 28 + 35)),
                    ForeColor = Color.FromArgb(255, 204, 0)
                };

                flowOutputsCpu = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false
                };

                for (int i = 0; i < cpuDoCount; i++)
                {
                    bool isRelay = (i < cpuRelayCount);
                    flowOutputsCpu.Controls.Add(CreateOutputRow(currentOutputAddr++, isRelay));
                }

                grpCpu.Controls.Add(flowOutputsCpu);
                container.Controls.Add(grpCpu);
                xOffset += 265;
            }

            // 2. EX-A2X expansion module output groups (Unitronics expansion slots start at O32, +16 per slot)
            if (profile.EnableExpansionBus && profile.ExpansionSlots != null)
            {
                for (int s = 0; s < profile.ExpansionSlots.Count; s++)
                {
                    var slot = profile.ExpansionSlots[s];
                    var expSpec = profile.GetExpansionSpec(slot.ModuleName);
                    if (expSpec.TotalDo > 0)
                    {
                        int startAddr = Math.Max(currentOutputAddr, 32 + s * 16);
                        currentOutputAddr = startAddr;
                        int endAddr = startAddr + expSpec.TotalDo - 1;

                        var grpSlot = new GroupBox
                        {
                            Text = string.Format(Localization.T("Hw_Grp_ExtSlot"), slot.SlotNumber, expSpec.ModelName + string.Format(" (O{0}..O{1})", startAddr, endAddr)),
                            Location = new Point(xOffset, 10),
                            Size = new Size(250, Math.Max(200, expSpec.TotalDo * 28 + 35)),
                            ForeColor = Color.FromArgb(255, 204, 0)
                        };

                        var flowExt = new FlowLayoutPanel
                        {
                            Dock = DockStyle.Fill,
                            AutoScroll = true,
                            FlowDirection = FlowDirection.TopDown,
                            WrapContents = false
                        };

                        for (int i = 0; i < expSpec.TotalDo; i++)
                        {
                            bool isRelay = (i < expSpec.DoRelay);
                            flowExt.Controls.Add(CreateOutputRow(currentOutputAddr++, isRelay));
                        }

                        grpSlot.Controls.Add(flowExt);
                        flowOutputsExt.Add(flowExt);
                        container.Controls.Add(grpSlot);
                        xOffset += 265;
                    }
                }
            }
        }

        private Panel CreateOutputRow(int outIdx, bool isRelay)
        {
            var rowPanel = new Panel
            {
                Width = 230,
                Height = 26,
                Margin = new Padding(2)
            };

            // Output status LED
            var led = new Label
            {
                Width = 14,
                Height = 14,
                Left = 4,
                Top = 6,
                BackColor = Color.FromArgb(60, 65, 75),
                BorderStyle = BorderStyle.FixedSingle
            };
            lblOutputLeds[outIdx] = led;

            // Output address O0..O127
            var lblName = new Label
            {
                Text = "O" + outIdx,
                Left = 24,
                Top = 5,
                Width = 40,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold)
            };

            // Output hardware type (RO / TO)
            var lblType = new Label
            {
                Text = isRelay ? "RO" : "TO",
                Left = 66,
                Top = 6,
                Width = 26,
                ForeColor = isRelay ? Color.FromArgb(255, 215, 64) : Color.FromArgb(120, 200, 255),
                Font = new Font("Segoe UI", 7.5f)
            };

            // Project operand description
            var lblDesc = new Label
            {
                Text = GetDefaultOutputName(outIdx),
                Left = 96,
                Top = 6,
                Width = 130,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(170, 185, 205),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular)
            };
            lblOutputTexts[outIdx] = lblDesc;

            rowPanel.Controls.AddRange(new Control[] { led, lblName, lblType, lblDesc });
            return rowPanel;
        }

        private void BuildDynamicAnalogInputs(Panel container)
        {
            int totalAi = profile.GetTotalAi();
            int yPos = 15;

            for (int i = 0; i < Math.Min(totalAi, 16); i++)
            {
                int aiIdx = i;
                int miRegister = 100 + aiIdx;
                string unitStr = "raw";
                string aiDesc = GetDefaultAiName(aiIdx);

                if (plc.SimProfile != null && plc.SimProfile.AnalogRules != null && plc.SimProfile.AnalogRules.ContainsKey(aiIdx))
                {
                    var aRule = plc.SimProfile.AnalogRules[aiIdx];
                    if (aRule != null)
                    {
                        miRegister = aRule.TargetMiAddress;
                        if (!string.IsNullOrEmpty(aRule.Unit)) unitStr = aRule.Unit;
                    }
                }

                var lblTitle = new Label
                {
                    Text = string.Format("AI{0} / MI{1} [{2}]", aiIdx, miRegister, aiDesc),
                    Left = 20,
                    Top = yPos,
                    Width = 400,
                    ForeColor = Color.FromArgb(0, 230, 118),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
                };
                lblAnalogLabels[aiIdx] = lblTitle;

                int initVal = (miRegister >= 0 && miRegister < plc.MI.Length)
                    ? Math.Max(0, Math.Min(4095, (int)plc.MI[miRegister]))
                    : 0;

                var track = new TrackBar
                {
                    Left = 20,
                    Top = yPos + 22,
                    Width = 320,
                    Minimum = 0,
                    Maximum = 4095,
                    Value = initVal,
                    TickFrequency = 256,
                    SmallChange = 10,
                    LargeChange = 100
                };
                trackAnalogs[aiIdx] = track;

                var num = new NumericUpDown
                {
                    Left = 350,
                    Top = yPos + 24,
                    Width = 80,
                    Minimum = 0,
                    Maximum = 4095,
                    Value = track.Value,
                    BackColor = Color.FromArgb(40, 46, 58),
                    ForeColor = Color.White
                };
                numAnalogs[aiIdx] = num;

                var lblVal = new Label
                {
                    Left = 440,
                    Top = yPos + 26,
                    Width = 180,
                    ForeColor = Color.FromArgb(200, 220, 240)
                };

                Action<bool> updateAi = (writePlc) =>
                {
                    int curMiReg = miRegister;
                    string curUnit = unitStr;
                    if (plc.SimProfile != null && plc.SimProfile.AnalogRules != null && plc.SimProfile.AnalogRules.ContainsKey(aiIdx))
                    {
                        var r = plc.SimProfile.AnalogRules[aiIdx];
                        if (r != null)
                        {
                            curMiReg = r.TargetMiAddress;
                            if (!string.IsNullOrEmpty(r.Unit)) curUnit = r.Unit;
                        }
                    }

                    short rawVal = (short)num.Value;
                    if (writePlc && curMiReg >= 0 && curMiReg < plc.MI.Length)
                    {
                        plc.MI[curMiReg] = rawVal;
                        if (plc.SimProfile != null && plc.SimProfile.AnalogRules != null && plc.SimProfile.AnalogRules.ContainsKey(aiIdx))
                        {
                            plc.SimProfile.AnalogRules[aiIdx].CurrentPreciseValue = rawVal;
                        }
                    }
                    lblVal.Text = string.Format("MI{0} = {1} ({2})", curMiReg, rawVal, curUnit);
                };

                track.ValueChanged += (s, e) =>
                {
                    if (num.Value != track.Value) num.Value = track.Value;
                    updateAi(true);
                };
                num.ValueChanged += (s, e) =>
                {
                    if (num.Value <= track.Maximum && track.Value != (int)num.Value) track.Value = (int)num.Value;
                    updateAi(true);
                };

                updateAi(false);

                container.Controls.AddRange(new Control[] { lblTitle, track, num, lblVal });
                yPos += 68;
            }
        }

        private void BuildDynamicAnalogOutputs(Panel container)
        {
            int totalAo = profile.GetTotalAo();
            int yPos = 15;

            for (int i = 0; i < Math.Min(totalAo, 8); i++)
            {
                int aoIdx = i;
                int miRegister = 110 + aoIdx;

                var lblTitle = new Label
                {
                    Text = string.Format("AO{0} / MI{1} [Analog Output AO{0}]", aoIdx, miRegister),
                    Left = 20,
                    Top = yPos,
                    Width = 400,
                    ForeColor = Color.FromArgb(255, 128, 0),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
                };

                var pbar = new ProgressBar
                {
                    Left = 20,
                    Top = yPos + 22,
                    Width = 320,
                    Height = 20,
                    Minimum = 0,
                    Maximum = 1000,
                    Value = 0
                };
                barAoVoltages[aoIdx] = pbar;

                var lblVolt = new Label
                {
                    Text = "0.00 V (0.0 Hz)",
                    Left = 350,
                    Top = yPos + 24,
                    Width = 160,
                    ForeColor = Color.White
                };
                lblAoVoltages[aoIdx] = lblVolt;

                container.Controls.AddRange(new Control[] { lblTitle, pbar, lblVolt });
                yPos += 58;
            }
        }

        private void OnPlcScanCompleted()
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    // Update digital input checkboxes & LEDs across all mapped addresses (including I32..I39)
                    for (int i = 0; i < chkInputs.Length && i < plc.I.Length; i++)
                    {
                        if (chkInputs[i] != null && chkInputs[i].Checked != plc.I[i])
                        {
                            chkInputs[i].Checked = plc.I[i];
                        }
                        if (lblInputLeds[i] != null)
                        {
                            UpdateLedColor(lblInputLeds[i], plc.I[i], Color.FromArgb(0, 230, 118));
                        }
                    }

                    // Update digital output LEDs across all mapped addresses (including O32..O39)
                    for (int i = 0; i < lblOutputLeds.Length && i < plc.O.Length; i++)
                    {
                        if (lblOutputLeds[i] != null)
                        {
                            UpdateLedColor(lblOutputLeds[i], plc.O[i], Color.FromArgb(255, 140, 0));
                        }
                    }

                    // Sync analog input sliders if modified by process simulation or Watch Table
                    int totalAi = profile.GetTotalAi();
                    for (int i = 0; i < totalAi && i < numAnalogs.Length; i++)
                    {
                        int miReg = 100 + i;
                        if (plc.SimProfile != null && plc.SimProfile.AnalogRules != null && plc.SimProfile.AnalogRules.ContainsKey(i))
                        {
                            var r = plc.SimProfile.AnalogRules[i];
                            if (r != null) miReg = r.TargetMiAddress;
                        }
                        if (numAnalogs[i] != null && miReg >= 0 && miReg < plc.MI.Length)
                        {
                            int clampedVal = Math.Max(0, Math.Min(4095, (int)plc.MI[miReg]));
                            if ((int)numAnalogs[i].Value != clampedVal && !numAnalogs[i].Focused && (trackAnalogs[i] == null || !trackAnalogs[i].Focused))
                            {
                                numAnalogs[i].Value = clampedVal;
                            }
                        }
                    }

                    // Update analog output gauges
                    int totalAo = profile.GetTotalAo();
                    for (int i = 0; i < totalAo && i < barAoVoltages.Length; i++)
                    {
                        if (barAoVoltages[i] != null && i < plc.AO_Volts.Length)
                        {
                            double v = plc.AO_Volts[i];
                            int pct = (int)Math.Min(1000, Math.Max(0, (v / 10.0) * 1000));
                            barAoVoltages[i].Value = pct;
                            if (lblAoVoltages[i] != null)
                            {
                                lblAoVoltages[i].Text = string.Format("{0:F2} V ({1:F1} Hz)", v, v * 5.0);
                            }
                        }
                    }
                }));
            }
            catch { }
        }

        private void UpdateLedColor(Label led, bool state, Color onColor)
        {
            led.BackColor = state ? onColor : Color.FromArgb(60, 65, 75);
        }

        public void RefreshOperandLabels()
        {
            ApplyProjectDescriptions();
        }

        public void ApplyProjectDescriptions()
        {
            if (plc.Project == null) return;

            for (int i = 0; i < lblInputTexts.Length; i++)
            {
                if (chkInputs[i] != null && i < plc.I.Length)
                {
                    chkInputs[i].Checked = plc.I[i];
                }
                if (lblInputLeds[i] != null && i < plc.I.Length)
                {
                    UpdateLedColor(lblInputLeds[i], plc.I[i], Color.FromArgb(0, 230, 118));
                }
                if (lblInputTexts[i] == null) continue;
                string key = "I" + i;
                string desc;
                if (plc.Project.OperandDescriptions.TryGetValue(key, out desc))
                {
                    lblInputTexts[i].Text = desc;
                }
            }

            for (int i = 0; i < lblOutputTexts.Length; i++)
            {
                if (lblOutputLeds[i] != null && i < plc.O.Length)
                {
                    UpdateLedColor(lblOutputLeds[i], plc.O[i], Color.FromArgb(255, 140, 0));
                }
                if (lblOutputTexts[i] == null) continue;
                string key = "O" + i;
                string desc;
                if (plc.Project.OperandDescriptions.TryGetValue(key, out desc))
                {
                    lblOutputTexts[i].Text = desc;
                }
            }

            int totalAi = profile.GetTotalAi();
            for (int i = 0; i < totalAi && i < lblAnalogLabels.Length; i++)
            {
                int miReg = 100 + i;
                string desc = null;
                if (plc.SimProfile != null && plc.SimProfile.AnalogRules != null && plc.SimProfile.AnalogRules.ContainsKey(i))
                {
                    var r = plc.SimProfile.AnalogRules[i];
                    if (r != null)
                    {
                        miReg = r.TargetMiAddress;
                    }
                }
                if (lblAnalogLabels[i] != null)
                {
                    if (!plc.Project.OperandDescriptions.TryGetValue("MI" + miReg, out desc))
                        plc.Project.OperandDescriptions.TryGetValue("AN" + i, out desc);
                    if (!string.IsNullOrEmpty(desc))
                    {
                        lblAnalogLabels[i].Text = string.Format("AI{0} / MI{1} [{2}]", i, miReg, desc);
                    }
                }
                if (numAnalogs[i] != null && miReg >= 0 && miReg < plc.MI.Length)
                {
                    int clampedVal = Math.Max(0, Math.Min(4095, (int)plc.MI[miReg]));
                    numAnalogs[i].Value = clampedVal;
                }
            }
        }

        private void ApplyLanguage()
        {
            tabDi.Text = Localization.T("Hw_Tab_DI");
            tabDo.Text = Localization.T("Hw_Tab_DO");
            tabAi.Text = Localization.T("Hw_Tab_AI");
            tabAo.Text = Localization.T("Hw_Tab_AO");

            for (int i = 0; i < btnMomentaryInputs.Length; i++)
            {
                if (btnMomentaryInputs[i] != null)
                {
                    btnMomentaryInputs[i].Text = Localization.T("Hw_Btn_Pulse");
                }
            }
        }

        private string GetDefaultInputName(int i)
        {
            return "Digital Input I" + i;
        }

        private string GetDefaultOutputName(int i)
        {
            return "Digital Output O" + i;
        }

        private string GetDefaultAiName(int i)
        {
            return "Analog Input AI" + i;
        }
    }
}
