using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace UnitronicsV350Simulator
{
    public class HardwarePanel : UserControl
    {
        private PlcEngine plc;

        private FlowLayoutPanel flowInputsCpu;
        private FlowLayoutPanel flowInputsExt;
        private FlowLayoutPanel flowOutputsCpu;
        private FlowLayoutPanel flowOutputsExt;
        private Panel panelAnalogInputs;
        private Panel panelAnalogOutputs;

        // Kontrolki wejść cyfrowych
        private CheckBox[] chkInputs = new CheckBox[20];
        private Label[] lblInputLeds = new Label[20];
        private Label[] lblInputTexts = new Label[20];
        private Button[] btnMomentaryInputs = new Button[20];

        // Kontrolki wyjść cyfrowych
        private Label[] lblOutputLeds = new Label[16];
        private Label[] lblOutputTexts = new Label[16];

        // Kontrolki wejść analogowych
        private TrackBar[] trackAnalogs = new TrackBar[4];
        private NumericUpDown[] numAnalogs = new NumericUpDown[4];
        private Label[] lblAnalogLabels = new Label[4];

        // Kontrolki wyjść analogowych
        private Label[] lblAoVoltages = new Label[2];
        private ProgressBar[] barAoVoltages = new ProgressBar[2];

        public HardwarePanel(PlcEngine engine)
        {
            plc = engine;
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(28, 32, 40);
            this.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            this.ForeColor = Color.White;

            InitializeLayout();
            plc.OnScanCompleted += OnPlcScanCompleted;
        }

        private void InitializeLayout()
        {
            var tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;

            // Zakładka 1: Wejścia cyfrowe (DI)
            var tabDi = new TabPage("Wejścia Cyfrowe (DI)");
            tabDi.BackColor = Color.FromArgb(28, 32, 40);

            var splitDi = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 240,
                BackColor = Color.FromArgb(40, 45, 55)
            };

            var grpCpuDi = new GroupBox
            {
                Text = "CPU V350-35-RA22 (Wejścia I0 .. I11)",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(0, 220, 255)
            };
            flowInputsCpu = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            grpCpuDi.Controls.Add(flowInputsCpu);
            splitDi.Panel1.Controls.Add(grpCpuDi);

            var grpExtDi = new GroupBox
            {
                Text = "Szyna EX-A2X: Moduł 1 (Wejścia I12 .. I19)",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(0, 220, 255)
            };
            flowInputsExt = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            grpExtDi.Controls.Add(flowInputsExt);
            splitDi.Panel2.Controls.Add(grpExtDi);
            tabDi.Controls.Add(splitDi);

            // Zakładka 2: Wyjścia cyfrowe (DO / RO)
            var tabDo = new TabPage("Wyjścia Cyfrowe (DO)");
            tabDo.BackColor = Color.FromArgb(28, 32, 40);

            var splitDo = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 240,
                BackColor = Color.FromArgb(40, 45, 55)
            };

            var grpCpuDo = new GroupBox
            {
                Text = "CPU V350 (Wyjścia O0 .. O7: 6 RO, 2 TO)",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(255, 204, 0)
            };
            flowOutputsCpu = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            grpCpuDo.Controls.Add(flowOutputsCpu);
            splitDo.Panel1.Controls.Add(grpCpuDo);

            var grpExtDo = new GroupBox
            {
                Text = "Szyna EX-A2X: Moduł 1 (Wyjścia O8 .. O15)",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(255, 204, 0)
            };
            flowOutputsExt = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            grpExtDo.Controls.Add(flowOutputsExt);
            splitDo.Panel2.Controls.Add(grpExtDo);
            tabDo.Controls.Add(splitDo);

            // Zakładka 3: Wejścia i Wyjścia Analogowe (AI / AO)
            var tabAi = new TabPage("Sygnały Analogowe (AI / AO)");
            tabAi.BackColor = Color.FromArgb(28, 32, 40);

            var splitAnalog = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 240,
                BackColor = Color.FromArgb(40, 45, 55)
            };

            var grpAi = new GroupBox
            {
                Text = "Wejścia Analogowe (AI0 .. AI3: Pt100 / 0-10V / 4-20mA)",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(0, 230, 118)
            };
            panelAnalogInputs = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            grpAi.Controls.Add(panelAnalogInputs);
            splitAnalog.Panel1.Controls.Add(grpAi);

            var grpAo = new GroupBox
            {
                Text = "Wyjścia Analogowe (AO0 .. AO1: Falowniki VFD1 / VFD2 0-10V)",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(255, 128, 0)
            };
            panelAnalogOutputs = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            grpAo.Controls.Add(panelAnalogOutputs);
            splitAnalog.Panel2.Controls.Add(grpAo);
            tabAi.Controls.Add(splitAnalog);

            tabControl.TabPages.Add(tabDi);
            tabControl.TabPages.Add(tabDo);
            tabControl.TabPages.Add(tabAi);

            this.Controls.Add(tabControl);

            BuildInputRows();
            BuildOutputRows();
            BuildAnalogRows();
        }

        private void BuildInputRows()
        {
            for (int i = 0; i < 20; i++)
            {
                int inputIdx = i;
                var rowPanel = new Panel
                {
                    Width = 230,
                    Height = 28,
                    Margin = new Padding(2)
                };

                // Dioda LED
                var led = new Label
                {
                    Width = 14,
                    Height = 14,
                    Left = 4,
                    Top = 7,
                    BackColor = Color.FromArgb(60, 65, 75),
                    BorderStyle = BorderStyle.FixedSingle
                };
                lblInputLeds[inputIdx] = led;

                // Przełącznik hebelkowy bistabilny
                var chk = new CheckBox
                {
                    Text = "I" + inputIdx,
                    Left = 24,
                    Top = 4,
                    Width = 46,
                    ForeColor = Color.White
                };
                chk.CheckedChanged += (s, e) =>
                {
                    plc.I[inputIdx] = chk.Checked;
                    UpdateLedColor(led, chk.Checked, Color.FromArgb(0, 230, 118));
                };
                chkInputs[inputIdx] = chk;

                // Przycisk chwilowy (Momentary)
                var btnMom = new Button
                {
                    Text = "PULS",
                    Left = 72,
                    Top = 3,
                    Width = 42,
                    Height = 22,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(50, 55, 68),
                    ForeColor = Color.FromArgb(200, 220, 240)
                };
                btnMom.FlatAppearance.BorderColor = Color.FromArgb(80, 90, 110);
                btnMom.MouseDown += (s, e) => { plc.I[inputIdx] = true; chk.Checked = true; };
                btnMom.MouseUp += (s, e) => { plc.I[inputIdx] = false; chk.Checked = false; };
                btnMomentaryInputs[inputIdx] = btnMom;

                // Opis operandu
                var lblDesc = new Label
                {
                    Text = GetDefaultInputName(inputIdx),
                    Left = 118,
                    Top = 7,
                    Width = 108,
                    AutoEllipsis = true,
                    ForeColor = Color.FromArgb(170, 185, 205),
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Regular)
                };
                lblInputTexts[inputIdx] = lblDesc;

                rowPanel.Controls.Add(led);
                rowPanel.Controls.Add(chk);
                rowPanel.Controls.Add(btnMom);
                rowPanel.Controls.Add(lblDesc);

                if (inputIdx < 12)
                {
                    flowInputsCpu.Controls.Add(rowPanel);
                }
                else
                {
                    flowInputsExt.Controls.Add(rowPanel);
                }
            }
        }

        private void BuildOutputRows()
        {
            for (int i = 0; i < 16; i++)
            {
                int outIdx = i;
                var rowPanel = new Panel
                {
                    Width = 230,
                    Height = 26,
                    Margin = new Padding(2)
                };

                // Dioda LED wyjścia
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

                // Nazwa wyjścia O0..O15
                var lblName = new Label
                {
                    Text = "O" + outIdx,
                    Left = 24,
                    Top = 5,
                    Width = 35,
                    ForeColor = Color.FromArgb(255, 204, 0),
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold)
                };

                // Opis odbiornika
                var lblDesc = new Label
                {
                    Text = GetDefaultOutputName(outIdx),
                    Left = 62,
                    Top = 6,
                    Width = 160,
                    AutoEllipsis = true,
                    ForeColor = Color.FromArgb(190, 205, 225),
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Regular)
                };
                lblOutputTexts[outIdx] = lblDesc;

                rowPanel.Controls.Add(led);
                rowPanel.Controls.Add(lblName);
                rowPanel.Controls.Add(lblDesc);

                if (outIdx < 8)
                {
                    flowOutputsCpu.Controls.Add(rowPanel);
                }
                else
                {
                    flowOutputsExt.Controls.Add(rowPanel);
                }
            }
        }

        private void BuildAnalogRows()
        {
            string[] names = new string[] {
                "AI0: Pomiar Temp Raw (0..1023)",
                "AI1: Wilgotność komory 4-20mA (0..1023)",
                "AI2: Temp Komory Pt100 (0..150°C)",
                "AI3: Temp Drewna Pt100 (0..150°C)"
            };

            int[] maxVals = new int[] { 1023, 1023, 1500, 1500 };

            for (int i = 0; i < 4; i++)
            {
                int aiIdx = i;
                int y = 10 + i * 52;

                var lbl = new Label
                {
                    Text = names[aiIdx],
                    Left = 10,
                    Top = y,
                    Width = 240,
                    ForeColor = Color.FromArgb(220, 230, 240)
                };
                lblAnalogLabels[aiIdx] = lbl;

                var track = new TrackBar
                {
                    Left = 10,
                    Top = y + 20,
                    Width = 180,
                    Height = 28,
                    Minimum = 0,
                    Maximum = maxVals[aiIdx],
                    Value = aiIdx >= 2 ? 220 : 100, // Domyślnie 22.0°C dla temperatur
                    TickStyle = TickStyle.None
                };

                var num = new NumericUpDown
                {
                    Left = 196,
                    Top = y + 20,
                    Width = 65,
                    Minimum = 0,
                    Maximum = maxVals[aiIdx],
                    Value = track.Value,
                    BackColor = Color.FromArgb(40, 45, 55),
                    ForeColor = Color.White
                };

                track.ValueChanged += (s, e) =>
                {
                    num.Value = track.Value;
                    UpdatePlcAnalogValue(aiIdx, track.Value);
                };
                num.ValueChanged += (s, e) =>
                {
                    track.Value = (int)num.Value;
                    UpdatePlcAnalogValue(aiIdx, (int)num.Value);
                };

                trackAnalogs[aiIdx] = track;
                numAnalogs[aiIdx] = num;

                panelAnalogInputs.Controls.Add(lbl);
                panelAnalogInputs.Controls.Add(track);
                panelAnalogInputs.Controls.Add(num);
            }

            // Wyjścia analogowe AO0 i AO1
            for (int i = 0; i < 2; i++)
            {
                int aoIdx = i;
                int y = 10 + i * 45;

                var lblDesc = new Label
                {
                    Text = string.Format("AO{0}: Falownik VFD {1} (0..10V / 0..50Hz)", aoIdx, aoIdx + 1),
                    Left = 10,
                    Top = y,
                    Width = 230,
                    ForeColor = Color.FromArgb(255, 150, 50)
                };

                var bar = new ProgressBar
                {
                    Left = 10,
                    Top = y + 20,
                    Width = 170,
                    Height = 16,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0
                };
                barAoVoltages[aoIdx] = bar;

                var lblVal = new Label
                {
                    Text = "0.00 V (0.0 Hz)",
                    Left = 185,
                    Top = y + 19,
                    Width = 95,
                    ForeColor = Color.Yellow,
                    Font = new Font("Consolas", 8.25f, FontStyle.Bold)
                };
                lblAoVoltages[aoIdx] = lblVal;

                panelAnalogOutputs.Controls.Add(lblDesc);
                panelAnalogOutputs.Controls.Add(bar);
                panelAnalogOutputs.Controls.Add(lblVal);
            }
        }

        private void UpdatePlcAnalogValue(int idx, int val)
        {
            if (idx == 0) plc.MI[0] = (short)val;
            else if (idx == 1) plc.MI[1] = (short)val;
            else if (idx == 2) plc.MI[2] = (short)val;
            else if (idx == 3) plc.MI[3] = (short)val;
        }

        private void OnPlcScanCompleted()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(SyncUiWithPlc));
            }
            else
            {
                SyncUiWithPlc();
            }
        }

        private void SyncUiWithPlc()
        {
            // Aktualizacja wyjść cyfrowych DO
            for (int i = 0; i < 16; i++)
            {
                bool state = plc.O[i];
                UpdateLedColor(lblOutputLeds[i], state, Color.FromArgb(255, 204, 0));
            }

            // Aktualizacja wejść cyfrowych (gdy zmienione z zewnątrz / programu)
            for (int i = 0; i < 20; i++)
            {
                bool state = plc.I[i];
                UpdateLedColor(lblInputLeds[i], state, Color.FromArgb(0, 230, 118));
            }

            // Aktualizacja wyjść analogowych AO
            for (int i = 0; i < 2; i++)
            {
                double volts = plc.AO_Volts[i];
                double hz = volts * 5.0; // 0..10V = 0..50Hz
                barAoVoltages[i].Value = Math.Min(100, (int)(volts * 10.0));
                lblAoVoltages[i].Text = string.Format("{0:0.00} V ({1:0.0} Hz)", volts, hz);
            }
        }

        public void RefreshOperandLabels()
        {
            if (plc.Project == null) return;

            // Odśwież etykiety wejść I0..I19
            for (int i = 0; i < 20; i++)
            {
                string key = "I" + i;
                if (plc.Project.OperandDescriptions.ContainsKey(key))
                {
                    lblInputTexts[i].Text = plc.Project.OperandDescriptions[key];
                }
            }

            // Odśwież etykiety wyjść O0..O15
            for (int i = 0; i < 16; i++)
            {
                string key = "O" + i;
                if (plc.Project.OperandDescriptions.ContainsKey(key))
                {
                    lblOutputTexts[i].Text = plc.Project.OperandDescriptions[key];
                }
            }
        }

        private void UpdateLedColor(Label led, bool state, Color activeColor)
        {
            led.BackColor = state ? activeColor : Color.FromArgb(50, 55, 65);
        }

        private string GetDefaultInputName(int idx)
        {
            switch (idx)
            {
                case 0: return "Went 1 Termik";
                case 1: return "Went 2 Termik";
                case 2: return "Went 3 Termik";
                case 3: return "Went 4 Termik";
                case 4: return "Went 5 Termik";
                case 5: return "Went 6 Termik";
                case 6: return "E-Stop (Bezpiecz)";
                case 7: return "STB 95°C Termostat";
                case 8: return "STOP Przycisk";
                case 9: return "PAUZA Przycisk";
                case 10: return "START Przycisk";
                case 11: return "Krańcówka Drzwi";
                case 12: return "Presostaty Rekup";
                case 13: return "Gotowość VFD LS";
                case 14: return "Prąd Grzałek 1-3";
                case 15: return "Prąd Grzałek 4-6";
                case 16: return "Asymetria Faz CKF";
                case 17: return "Przełącznik Went";
                default: return "Wejście DI " + idx;
            }
        }

        private string GetDefaultOutputName(int idx)
        {
            switch (idx)
            {
                case 0: return "Lampka PRACA";
                case 1: return "Lampka AWARIA";
                case 2: return "Syrena Alarmowa";
                case 3: return "Stycznik Went 1-3";
                case 4: return "Stycznik Went 4-6";
                case 5: return "Zezwolenie VFD";
                case 6: return "VFD 1 Praca";
                case 7: return "VFD 2 Praca";
                case 8: return "Stycznik Grzałek K1";
                case 9: return "Sekcja SSR 1";
                case 10: return "Sekcja SSR 2";
                case 11: return "Sekcja SSR 3";
                case 12: return "Sekcja SSR 4";
                case 13: return "Sekcja SSR 5";
                case 14: return "Sekcja SSR 6";
                case 15: return "Klapa Rekuperatora";
                default: return "Wyjście DO " + idx;
            }
        }
    }
}
