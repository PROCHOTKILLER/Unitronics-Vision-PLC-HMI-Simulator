using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace UnitronicsV350Simulator
{
    public class HmiRenderer
    {
        public const int DEFAULT_SCREEN_WIDTH = 320;
        public const int DEFAULT_SCREEN_HEIGHT = 240;

        public int ScreenWidth
        {
            get
            {
                if (plc != null && plc.Profile != null)
                {
                    return plc.Profile.GetCurrentModelSpec().ScreenWidth;
                }
                return DEFAULT_SCREEN_WIDTH;
            }
        }

        public int ScreenHeight
        {
            get
            {
                if (plc != null && plc.Profile != null)
                {
                    return plc.Profile.GetCurrentModelSpec().ScreenHeight;
                }
                return DEFAULT_SCREEN_HEIGHT;
            }
        }

        private PlcEngine plc;
        private Bitmap renderBitmap;
        private Graphics g;

        // Virtual numeric keypad state
        public bool IsKeypadOpen = false;
        private HmiElementDef keypadTargetElement = null;
        private string keypadCurrentInput = "";

        public HmiRenderer(PlcEngine engine)
        {
            plc = engine;
            EnsureBitmapSize();
        }

        private void EnsureBitmapSize()
        {
            int w = ScreenWidth;
            int h = ScreenHeight;
            if (renderBitmap == null || renderBitmap.Width != w || renderBitmap.Height != h)
            {
                if (g != null) { g.Dispose(); g = null; }
                if (renderBitmap != null) { renderBitmap.Dispose(); renderBitmap = null; }
                renderBitmap = new Bitmap(w, h);
                g = Graphics.FromImage(renderBitmap);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }
        }

        public Bitmap RenderScreen(int activePageId)
        {
            EnsureBitmapSize();

            if (!plc.HasProgramLoaded)
            {
                RenderBootScreen();
            }
            else
            {
                RenderProjectPage(activePageId);
            }

            if (IsKeypadOpen)
            {
                RenderKeypadOverlay();
            }

            return renderBitmap;
        }

        private void RenderBootScreen()
        {
            var spec = (plc != null && plc.Profile != null) ? plc.Profile.GetCurrentModelSpec() : null;
            string family = spec != null ? spec.Family : "Vision 350";
            string modelName = spec != null ? spec.ModelName : "V350-35-RA22";
            string displayType = spec != null ? spec.DisplayType : "Color TFT 3.5\"";

            // Industrial OPLC background
            using (var bgBrush = new SolidBrush(Color.FromArgb(12, 25, 44)))
            {
                g.FillRectangle(bgBrush, 0, 0, ScreenWidth, ScreenHeight);
            }

            // Unitronics header bar
            using (var hdrBrush = new SolidBrush(Color.FromArgb(20, 48, 85)))
            {
                g.FillRectangle(hdrBrush, 0, 0, ScreenWidth, 30);
            }
            using (var borderPen = new Pen(Color.FromArgb(0, 150, 255), 1))
            {
                g.DrawLine(borderPen, 0, 30, ScreenWidth, 30);
            }

            using (var fontHdr = new Font("Segoe UI", 10, FontStyle.Bold))
            using (var brushWhite = new SolidBrush(Color.White))
            {
                g.DrawString("UNITRONICS " + family.ToUpper(), fontHdr, brushWhite, 10, 6);
            }

            using (var fontSmall = new Font("Segoe UI", 7, FontStyle.Regular))
            using (var brushCyan = new SolidBrush(Color.FromArgb(0, 220, 255)))
            {
                g.DrawString(modelName + " | " + displayType, fontSmall, brushCyan, Math.Max(120, ScreenWidth - 170), 9);
            }

            // System information frame
            int boxX = 15, boxY = 40, boxW = ScreenWidth - 30, boxH = ScreenHeight - 55;
            using (var boxBrush = new SolidBrush(Color.FromArgb(18, 32, 54)))
            using (var boxPen = new Pen(Color.FromArgb(40, 75, 120), 1))
            {
                g.FillRectangle(boxBrush, boxX, boxY, boxW, boxH);
                g.DrawRectangle(boxPen, boxX, boxY, boxW, boxH);
            }

            using (var fontSys = new Font("Consolas", 8, FontStyle.Regular))
            using (var fontSysBold = new Font("Consolas", 8, FontStyle.Bold))
            using (var brushYellow = new SolidBrush(Color.FromArgb(255, 204, 0)))
            using (var brushText = new SolidBrush(Color.FromArgb(200, 220, 240)))
            using (var brushGreen = new SolidBrush(Color.FromArgb(0, 230, 118)))
            {
                g.DrawString("Hardware:  Unitronics " + (plc != null && plc.Profile != null ? plc.Profile.GetModelFullName() : modelName), fontSysBold, brushYellow, boxX + 10, boxY + 10);
                string expInfo = (plc != null && plc.Profile != null && plc.Profile.EnableExpansionBus) ? "EX-A2X Bus Ready (Active)" : "EX-A2X Bus Disabled";
                g.DrawString("Expansion: " + expInfo, fontSys, brushText, boxX + 10, boxY + 26);
                g.DrawString("OS Vers:   4.04 (12) | Boot: 2.02 (03)", fontSys, brushText, boxX + 10, boxY + 42);
                int pcomPort = SimulatorConfig.Current != null ? SimulatorConfig.Current.PcomPort : 20256;
                g.DrawString(string.Format("PCOM Port: TCP/IP Port {0} (PCOM)", pcomPort), fontSys, brushGreen, boxX + 10, boxY + 58);

                // Divider line
                g.DrawLine(Pens.SteelBlue, boxX + 10, boxY + 76, boxX + boxW - 10, boxY + 76);

                // Program status banner
                using (var alertFont = new Font("Segoe UI", 9, FontStyle.Bold))
                using (var alertBrush = new SolidBrush(Color.FromArgb(255, 82, 82)))
                {
                    g.DrawString(string.Format("[ {0}: {1} ]", Localization.T("Status_Plc", "STATUS"), Localization.T("Status_NoProgram", "NO USER PROGRAM")), alertFont, alertBrush, boxX + 15, boxY + 84);
                }

                using (var infoFont = new Font("Segoe UI", 8, FontStyle.Regular))
                using (var infoBrush = new SolidBrush(Color.FromArgb(180, 200, 220)))
                {
                    g.DrawString(Localization.T("Boot_CleanState", "Controller is in clean factory standby mode."), infoFont, infoBrush, boxX + 15, boxY + 106);
                    g.DrawString(Localization.T("Boot_OutputsOff", "All digital and analog outputs are OFF."), infoFont, infoBrush, boxX + 15, boxY + 122);
                    g.DrawString(Localization.T("Boot_HowToStart", "To start program execution:"), infoFont, brushYellow, boxX + 15, boxY + 140);
                    g.DrawString(Localization.T("Boot_LoadVlp", "-> Click 'File -> Load VisiLogic Project (*.vlp)'"), infoFont, brushGreen, boxX + 20, boxY + 155);
                    g.DrawString(string.Format(Localization.T("Boot_ConnectPcom", "-> OR connect VisiLogic via TCP 127.0.0.1:{0}"), pcomPort), infoFont, brushGreen, boxX + 20, boxY + 168);
                }
            }
        }

        private void RenderProjectPage(int pageId)
        {
            if (plc.Project == null || !plc.Project.Pages.ContainsKey(pageId))
            {
                // Fallback to the first available page
                if (plc.Project != null && plc.Project.Pages.Count > 0)
                {
                    foreach (var p in plc.Project.Pages.Values)
                    {
                        pageId = p.Id;
                        break;
                    }
                }
                else
                {
                    RenderBootScreen();
                    return;
                }
            }

            var page = plc.Project.Pages[pageId];

            // Page background
            Color bgColor = Color.FromArgb(235, 238, 242); // Standard Unitronics HMI gray
            if (page.ColorBack != 0)
            {
                bgColor = DecodeUnitronicsColor(page.ColorBack);
            }
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, 0, 0, ScreenWidth, ScreenHeight);
            }

            // Draw HMI elements
            foreach (var elm in page.Elements)
            {
                RenderHmiElement(elm);
            }

            // Bottom HMI status bar
            using (var barBrush = new SolidBrush(Color.FromArgb(40, 44, 52)))
            {
                g.FillRectangle(barBrush, 0, ScreenHeight - 18, ScreenWidth, 18);
            }
            using (var fontBar = new Font("Segoe UI", 7, FontStyle.Regular))
            using (var brushBarText = new SolidBrush(Color.FromArgb(220, 220, 220)))
            {
                string statusText = string.Format("P{0}: {1} | SCAN: {2}ms | {3}",
                    page.Id, page.Name, plc.SI[0], plc.State == PlcRunState.RUN ? "RUN" : "STOP");
                g.DrawString(statusText, fontBar, brushBarText, 4, ScreenHeight - 16);
            }
        }

        private void RenderHmiElement(HmiElementDef elm)
        {
            Rectangle rect = new Rectangle(elm.Xs, elm.Ys, Math.Max(elm.Dx, 10), Math.Max(elm.Dy, 10));

            // Optional visibility operand check (VisibleOpAddr)
            if (elm.VisibleOpAddr >= 0 && elm.VisibleOpAddr < plc.MB.Length)
            {
                // Visibility evaluation depends on project element settings
            }

            // Dispatch by element type
            if (elm.Type == 11)
            {
                RenderTouchButton(elm, rect);
                return;
            }

            if (elm.Type == 6)
            {
                RenderBitIndicator(elm, rect);
                return;
            }

            if (elm.Type == 5)
            {
                // Bargraph indicator
                if (elm.VarType == 24 || (elm.Text != null && elm.Text.StartsWith("BAR_")))
                {
                    RenderBargraph(elm, rect);
                    return;
                }

                // Binary status indicator (digital outputs O or memory bits MB)
                string opType = VlpParser.MapOperandTypeNumber(elm.VarOprndType);
                if (elm.VarType == 2 || opType == "O" || (opType == "MB" && elm.TouchOprndAddr < 0))
                {
                    RenderBinaryStatusIndicator(elm, rect);
                    return;
                }

                // Standard numeric variable display
                RenderNumericVariable(elm, rect);
                return;
            }

            switch (elm.Type)
            {
                case 1: // Frame / Box / Container
                    using (var fillBrush = new SolidBrush(Color.FromArgb(220, 225, 232)))
                    using (var pen = new Pen(Color.FromArgb(160, 170, 185), 1))
                    {
                        g.FillRectangle(fillBrush, rect);
                        g.DrawRectangle(pen, rect);
                    }
                    break;

                case 4: // Static Text
                    using (var font = new Font("Segoe UI", 8, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.FromArgb(30, 35, 45)))
                    {
                        var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                        g.DrawString(elm.Text, font, brush, rect, sf);
                    }
                    break;

                default:
                    // Default fallback text rendering
                    if (!string.IsNullOrEmpty(elm.Text))
                    {
                        using (var font = new Font("Segoe UI", 7, FontStyle.Regular))
                        using (var brush = new SolidBrush(Color.Black))
                        {
                            g.DrawString(elm.Text, font, brush, rect);
                        }
                    }
                    break;
            }
        }

        private void RenderTouchButton(HmiElementDef elm, Rectangle rect)
        {
            bool isDown = elm.IsPressed;
            bool isBitActive = false;
            if (elm.TouchOprndAddr >= 0 && elm.TouchOprndAddr < plc.MB.Length)
            {
                isBitActive = plc.MB[elm.TouchOprndAddr];
            }

            bool isCycleSelectorTab = elm.Text != null &&
                (elm.Text.IndexOf("BIEZACY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 elm.Text.IndexOf("CYKL -", StringComparison.OrdinalIgnoreCase) >= 0);
            bool isCycleTabActive = isCycleSelectorTab &&
                ((elm.TouchOprndAddr == 70 && plc.MI[72] == 1) ||
                 (elm.TouchOprndAddr >= 71 && elm.TouchOprndAddr <= 73 && plc.MI[72] == -(elm.TouchOprndAddr - 70)));

            Color topCol, botCol, borderCol, textCol;
            if (isDown)
            {
                topCol = Color.FromArgb(170, 180, 195);
                botCol = Color.FromArgb(210, 215, 225);
                borderCol = Color.FromArgb(80, 90, 110);
                textCol = Color.Black;
            }
            else if (isCycleTabActive || (!isCycleSelectorTab && isBitActive && (elm.TouchOprndAddr == 40 || (elm.TouchOprndAddr >= 70 && elm.TouchOprndAddr <= 79) || (elm.TouchOprndAddr >= 90 && elm.TouchOprndAddr <= 104))))
            {
                // Active toggle/manual switch button or selected cycle tab state
                topCol = Color.FromArgb(0, 200, 90);
                botCol = Color.FromArgb(0, 140, 60);
                borderCol = Color.FromArgb(0, 255, 120);
                textCol = Color.White;
            }
            else
            {
                topCol = Color.FromArgb(245, 248, 252);
                botCol = Color.FromArgb(195, 205, 218);
                borderCol = Color.FromArgb(120, 130, 145);
                textCol = Color.FromArgb(20, 25, 35);
            }

            using (var brush = new LinearGradientBrush(rect, topCol, botCol, LinearGradientMode.Vertical))
            using (var pen = new Pen(borderCol, 1))
            {
                g.FillRectangle(brush, rect);
                g.DrawRectangle(pen, rect);

                if (!isDown && !isBitActive && !isCycleTabActive)
                {
                    g.DrawLine(Pens.White, rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Top + 1);
                    g.DrawLine(Pens.White, rect.Left + 1, rect.Top + 1, rect.Left + 1, rect.Bottom - 1);
                }
            }

            // Button caption text
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(textCol))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(elm.Text, font, textBrush, rect, sf);
            }
        }

        private void RenderBinaryStatusIndicator(HmiElementDef elm, Rectangle rect)
        {
            bool isActive = false;
            string opType = VlpParser.MapOperandTypeNumber(elm.VarOprndType);
            if (opType == "O" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.O.Length)
            {
                if (elm.VarOprndAddr == 5)
                {
                    // On Pages 1, 39, 41: left indicator (Xs < 160) is NAWIEW VFD1 (O6/O5), right indicator (Xs >= 160) is WYCIAG VFD2 (O7/O5)
                    isActive = (elm.Xs >= 160) ? (plc.O[7] || plc.O[5]) : (plc.O[6] || plc.O[5]);
                }
                else if ((elm.VarOprndAddr == 3 || elm.VarOprndAddr == 4) && elm.Ys == 109)
                {
                    // Page 1 SEK 1..6 fan indicators: check O[addr] or manual fan bit MB[90..95]
                    int secIdx = (elm.Xs < 50) ? 0 : (elm.Xs < 100) ? 1 : (elm.Xs < 150) ? 2 : (elm.Xs < 200) ? 3 : (elm.Xs < 250) ? 4 : 5;
                    isActive = plc.O[elm.VarOprndAddr] || (!plc.MB[1] && plc.MB[90 + secIdx]);
                }
                else
                {
                    isActive = plc.O[elm.VarOprndAddr];
                }
            }
            else if (opType == "MB" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.MB.Length)
            {
                isActive = plc.MB[elm.VarOprndAddr];
            }
            else if (elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.O.Length)
            {
                if (elm.VarOprndAddr == 5)
                {
                    isActive = (elm.Xs >= 160) ? (plc.O[7] || plc.O[5]) : (plc.O[6] || plc.O[5]);
                }
                else
                {
                    isActive = plc.O[elm.VarOprndAddr];
                }
            }

            Color bgCol = isActive ? Color.FromArgb(0, 180, 80) : Color.FromArgb(60, 72, 88);
            Color borderCol = isActive ? Color.FromArgb(0, 240, 120) : Color.FromArgb(90, 105, 125);
            Color textCol = isActive ? Color.White : Color.FromArgb(170, 185, 200);
            string statusStr = isActive ? "ON" : "OFF";

            using (var brush = new SolidBrush(bgCol))
            using (var pen = new Pen(borderCol, 1))
            {
                g.FillRectangle(brush, rect);
                g.DrawRectangle(pen, rect);
            }

            using (var font = new Font("Segoe UI", 7f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(textCol))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(statusStr, font, textBrush, rect, sf);
            }
        }

        private int GetDisplayNumericValue(HmiElementDef elm)
        {
            string opType = VlpParser.MapOperandTypeNumber(elm.VarOprndType);
            string tag = (elm.Text ?? "").Trim().ToUpperInvariant();

            // In Komora 9, HMI displays use MI 1 for RH_Act / BAR_RH / RH_PV / Variable 48 while Ladder uses MI 1 for raw Pt100 and MI 101 for scaled RH (0.1 %RH)
            if (opType == "MI" && elm.VarOprndAddr == 1)
            {
                return plc.MI[101] / 10;
            }

            // In Komora 9, HMI Page 36/40 uses MI 30 for SP_Min / SP_Czas (minutes) while Ladder uses MI 30 for STB safety limit (95.0 °C)
            if (opType == "MI" && elm.VarOprndAddr == 30)
            {
                return (int)(plc.ML[50] / 60);
            }

            // In Komora 9, HMI Page 36/38 uses MI 50 for Grz_Il / Grz_Aktywne / BAR_GRZ / ACT_STG while Ladder uses MI 50 for Nr_Aktualnego_Kroku
            if (opType == "MI" && elm.VarOprndAddr == 50)
            {
                int stages = 0;
                for (int s = 0; s < 6; s++)
                {
                    if (plc.O[33 + s] || plc.O[9 + s]) stages++;
                }
                return stages;
            }

            if (opType == "MI" && elm.VarOprndAddr == 34)
            {
                return plc.MI[56];
            }

            if (opType == "MI" && elm.VarOprndAddr == 70 && tag.Contains("REC"))
            {
                return plc.MI[51];
            }

            if (opType == "MI" && elm.VarOprndAddr == 71 && tag.Contains("KROK"))
            {
                return 6;
            }

            if (opType == "MI" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.MI.Length)
            {
                return plc.MI[elm.VarOprndAddr];
            }
            if (opType == "ML" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.ML.Length)
            {
                return plc.ML[elm.VarOprndAddr];
            }
            return 0;
        }

        private void RenderBargraph(HmiElementDef elm, Rectangle rect)
        {
            // Bargraph background
            using (var bgBrush = new SolidBrush(Color.FromArgb(20, 26, 36)))
            using (var borderPen = new Pen(Color.FromArgb(90, 105, 125), 1))
            {
                g.FillRectangle(bgBrush, rect);
                g.DrawRectangle(borderPen, rect);
            }

            // Read register value from PLC
            int val = GetDisplayNumericValue(elm);

            // Default range 0..100 (or 0..6 for stage indicators)
            int minVal = 0;
            int maxVal = 100;
            if (elm.Text != null && (elm.Text.Contains("STG") || elm.Text.Contains("GRZ"))) maxVal = 6;
            else if (elm.VarOprndAddr == 50) maxVal = 6;

            int clampedVal = Math.Max(minVal, Math.Min(val, maxVal));
            double ratio = (maxVal > minVal) ? ((double)(clampedVal - minVal) / (maxVal - minVal)) : 0;
            int barHeight = (int)((rect.Height - 4) * ratio);

            // Select bar fill color based on tag category or operand address
            Color barColorTop, barColorBot;
            if (elm.VarOprndAddr == 0 || (elm.Text != null && (elm.Text.Contains("BAR_T") || elm.Text.Contains("TEMP"))))
            {
                barColorTop = Color.FromArgb(255, 80, 50);
                barColorBot = Color.FromArgb(200, 30, 20);
            }
            else if (elm.VarOprndAddr == 1 || (elm.Text != null && (elm.Text.Contains("BAR_RH") || elm.Text.Contains("HUM"))))
            {
                barColorTop = Color.FromArgb(0, 210, 255);
                barColorBot = Color.FromArgb(0, 110, 200);
            }
            else if (elm.VarOprndAddr == 61 || elm.VarOprndAddr == 62 || (elm.Text != null && (elm.Text.Contains("VFD") || elm.Text.Contains("FAN"))))
            {
                barColorTop = Color.FromArgb(0, 230, 118);
                barColorBot = Color.FromArgb(0, 140, 70);
            }
            else
            {
                barColorTop = Color.FromArgb(255, 180, 0);
                barColorBot = Color.FromArgb(200, 120, 0);
            }

            if (barHeight > 0)
            {
                Rectangle fillRect = new Rectangle(rect.X + 2, rect.Bottom - 2 - barHeight, Math.Max(rect.Width - 4, 2), barHeight);
                using (var barBrush = new LinearGradientBrush(fillRect, barColorTop, barColorBot, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(barBrush, fillRect);
                }
            }

            // Horizontal scale grid lines (25%, 50%, 75%)
            using (var gridPen = new Pen(Color.FromArgb(60, 75, 95), 1))
            {
                for (int pct = 25; pct <= 75; pct += 25)
                {
                    int gy = rect.Bottom - 2 - (int)((rect.Height - 4) * (pct / 100.0));
                    g.DrawLine(gridPen, rect.Left + 2, gy, rect.Right - 3, gy);
                }
            }

            // Bar numeric label
            using (var font = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
                g.DrawString(val.ToString(), font, textBrush, new Rectangle(rect.X, rect.Y + 2, rect.Width, 12), sf);
            }
        }

        private void RenderNumericVariable(HmiElementDef elm, Rectangle rect)
        {
            // Numeric field background (white with dark border)
            using (var bgBrush = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.FromArgb(130, 140, 155), 1))
            {
                g.FillRectangle(bgBrush, rect);
                g.DrawRectangle(pen, rect);
            }

            // Retrieve register value from PLC
            int val = GetDisplayNumericValue(elm);

            string displayStr = val.ToString();

            using (var font = new Font("Consolas", 8, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(10, 40, 100)))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Far,
                    LineAlignment = StringAlignment.Center
                };
                Rectangle textRect = new Rectangle(rect.X, rect.Y, rect.Width - 3, rect.Height);
                g.DrawString(displayStr, font, brush, textRect, sf);
            }
        }

        private void RenderBitIndicator(HmiElementDef elm, Rectangle rect)
        {
            bool bitVal = false;
            if (elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.MB.Length)
            {
                bitVal = plc.MB[elm.VarOprndAddr];
            }

            Color ledCol = bitVal ? Color.FromArgb(0, 230, 118) : Color.FromArgb(160, 170, 180);
            using (var brush = new SolidBrush(ledCol))
            using (var pen = new Pen(Color.FromArgb(80, 90, 100), 1))
            {
                int dia = Math.Min(rect.Width, rect.Height) - 4;
                g.FillEllipse(brush, rect.X + 2, rect.Y + 2, dia, dia);
                g.DrawEllipse(pen, rect.X + 2, rect.Y + 2, dia, dia);
            }
        }

        private void RenderKeypadOverlay()
        {
            // Semi-transparent background dim overlay
            using (var dimBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                g.FillRectangle(dimBrush, 0, 0, ScreenWidth, ScreenHeight);
            }

            // Numeric keypad modal window
            int kw = 180, kh = 200;
            int kx = (ScreenWidth - kw) / 2;
            int ky = (ScreenHeight - kh) / 2;

            using (var boxBrush = new SolidBrush(Color.FromArgb(35, 40, 50)))
            using (var boxPen = new Pen(Color.FromArgb(0, 150, 255), 2))
            {
                g.FillRectangle(boxBrush, kx, ky, kw, kh);
                g.DrawRectangle(boxPen, kx, ky, kw, kh);
            }

            // Keypad title
            using (var fontHdr = new Font("Segoe UI", 8, FontStyle.Bold))
            using (var brushHdr = new SolidBrush(Color.White))
            {
                g.DrawString("VALUE ENTRY (KEYPAD)", fontHdr, brushHdr, kx + 10, ky + 6);
            }

            // Current input value field
            int fieldY = ky + 25;
            using (var fieldBrush = new SolidBrush(Color.Black))
            using (var fieldPen = new Pen(Color.FromArgb(0, 200, 255), 1))
            {
                g.FillRectangle(fieldBrush, kx + 10, fieldY, kw - 20, 24);
                g.DrawRectangle(fieldPen, kx + 10, fieldY, kw - 20, 24);
            }

            using (var valFont = new Font("Consolas", 12, FontStyle.Bold))
            using (var valBrush = new SolidBrush(Color.LimeGreen))
            {
                g.DrawString(keypadCurrentInput + "_", valFont, valBrush, kx + 15, fieldY + 2);
            }

            // Keypad buttons 0..9, CLR, ESC, ENT, OK
            string[] keys = new string[] {
                "7", "8", "9", "CLR",
                "4", "5", "6", "ESC",
                "1", "2", "3", "ENT",
                "+/-", "0", ".", "OK"
            };

            int startY = fieldY + 32;
            int btnW = 36, btnH = 26;
            int gap = 4;

            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int idx = r * 4 + c;
                    int bx = kx + 10 + c * (btnW + gap);
                    int by = startY + r * (btnH + gap);

                    using (var btnBrush = new SolidBrush(Color.FromArgb(55, 62, 75)))
                    using (var btnPen = new Pen(Color.FromArgb(90, 100, 120), 1))
                    using (var btnFont = new Font("Segoe UI", 8, FontStyle.Bold))
                    using (var btnText = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(btnBrush, bx, by, btnW, btnH);
                        g.DrawRectangle(btnPen, bx, by, btnW, btnH);
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(keys[idx], btnFont, btnText, new Rectangle(bx, by, btnW, btnH), sf);
                    }
                }
            }
        }

        public static int GetTargetDisplayForButton(int touchAddr, string btnText)
        {
            string txt = (btnText ?? "").Trim().ToUpperInvariant();

            // 1. By TouchOprndAddr (MB 210..220 navigation bits & MB 28 Historia Cykli)
            switch (touchAddr)
            {
                case 210: return 1;  // Start-Up / Main Page
                case 211: return 36; // Process Control
                case 212: return 40; // Recipes
                case 213: return 41; // Ventilation & Air Handling
                case 214: return 42; // Alarms
                case 215: return 43; // Access / Login
                case 216: return 44; // Service Parameters
                case 217: return 45; // Manual I/O Test
                case 218: return 37; // Chart 1
                case 219: return 38; // Chart 2
                case 220: return 39; // Chart 3
                case 28:  return 37; // Historia Cykli -> Wykres T / RH
            }

            // 2. By button caption text (only when touchAddr is navigation or unassigned -1)
            if (touchAddr < 0 || (touchAddr >= 210 && touchAddr <= 220))
            {
                if (txt.Contains("TEST") || txt.Contains("IO TEST")) return 45;
                if (txt.Contains("MAIN") || txt.Contains("HOME") || txt.Contains("GLOWNY") || txt.Contains("STARTUP") || txt.Contains("START-UP")) return 1;
                if (txt.Contains("PROCESS") || txt.Contains("PROCES") || txt.Contains("CONTROL")) return 36;
                if (txt.Contains("RECIPE") || txt.Contains("RECEPT")) return 40;
                if (txt.Contains("VENT") || txt.Contains("WENT") || txt.Contains("FAN")) return 41;
                if (txt.Contains("ALARM")) return 42;
                if (txt.Contains("ACCESS") || txt.Contains("DOSTEP") || txt.Contains("LOGIN")) return 43;
                if (txt.Contains("SERVICE") || txt.Contains("SERWIS")) return 44;
                if (txt.Contains("T / RH") || txt.Contains("T/RH") || txt.Contains("WYKRES T")) return 37;
                if (txt.Contains("WYKRES GRZ") || txt.Contains("SEKCJI GRZ")) return 38;
                if (txt.Contains("WYKRES WENT") || txt.Contains("VFD")) return 39;
            }

            return 0;
        }

        private void PulseBit(int mbAddr, int durationMs = 150)
        {
            if (mbAddr < 0 || mbAddr >= plc.MB.Length) return;
            plc.MB[mbAddr] = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                System.Threading.Thread.Sleep(durationMs);
                plc.MB[mbAddr] = false;
            });
        }

        private static bool IsEditableNumericAddr(int addr)
        {
            switch (addr)
            {
                case 20:
                case 30:
                case 31:
                case 32:
                case 34:
                case 40:
                case 43:
                case 61:
                case 62:
                case 70:
                case 71:
                case 72:
                case 73:
                case 81:
                case 82:
                case 90:
                    return true;
                default:
                    return false;
            }
        }

        public void HandleTouchDown(int x, int y, int activePageId)
        {
            if (IsKeypadOpen)
            {
                HandleKeypadClick(x, y);
                return;
            }

            if (!plc.HasProgramLoaded) return;
            if (!plc.Project.Pages.ContainsKey(activePageId)) return;

            var page = plc.Project.Pages[activePageId];
            for (int i = page.Elements.Count - 1; i >= 0; i--)
            {
                var elm = page.Elements[i];
                Rectangle rect = new Rectangle(elm.Xs, elm.Ys, Math.Max(elm.Dx, 10), Math.Max(elm.Dy, 10));
                if (rect.Contains(x, y))
                {
                    if (elm.Type == 11 || elm.TouchOprndAddr >= 0)
                    {
                        elm.IsPressed = true;
                        int touchAddr = elm.TouchOprndAddr;
                        string btnTxt = (elm.Text ?? "").Trim().ToUpperInvariant();

                        // 1. Check screen navigation mapping
                        int targetPage = GetTargetDisplayForButton(touchAddr, elm.Text);
                        if (targetPage > 0)
                        {
                            plc.SI[9] = (short)targetPage;
                            plc.Log(string.Format("HMI Touch: Switched to screen P{0} ({1})", targetPage, elm.Text));
                        }

                        // 2. Update touch bit in PLC memory
                        if (touchAddr >= 0 && touchAddr < plc.MB.Length)
                        {
                            // A0. Cycle history selector tabs on Pages 37, 38, 39 (BIEZACY=70, CYKL -1=71, CYKL -2=72, CYKL -3=73)
                            if (btnTxt.Contains("BIEZACY") || btnTxt.Contains("CYKL -"))
                            {
                                plc.MI[72] = (short)(touchAddr == 70 ? 1 : -(touchAddr - 70));
                                PulseBit(touchAddr, 150);
                                plc.Log(string.Format("HMI Touch [Cycle History]: Selected cycle {0} ({1})", plc.MI[72], elm.Text));
                            }
                            // A. Toggle buttons on manual/service pages (MB 40, MB 70..79, or MB 90..104 on Page 41/45)
                            else if (touchAddr == 40 || (touchAddr >= 70 && touchAddr <= 79) || ((activePageId == 41 || activePageId == 45) && touchAddr >= 90 && touchAddr <= 104))
                            {
                                plc.MB[touchAddr] = !plc.MB[touchAddr];
                                if (touchAddr == 40)
                                {
                                    if (plc.MB[40])
                                    {
                                        plc.MB[41] = true;
                                        plc.MI[300] = 1999;
                                        plc.CurrentAccessLevel = 2;
                                        plc.MI[91] = 2;
                                    }
                                    else
                                    {
                                        PulseBit(42, 180); // HMI_Btn_Service_Exit
                                    }
                                }
                                else if (!plc.MB[1])
                                {
                                    // Enable manual test mode automatically when clicking manual actuators on Page 41/45
                                    plc.MB[40] = true;
                                    plc.MB[41] = true;
                                    plc.MI[300] = 1999;
                                    plc.CurrentAccessLevel = 2;
                                    plc.MI[91] = 2;
                                }
                                plc.Log(string.Format("HMI Touch [Toggle]: MB{0} = {1} ({2})", touchAddr, plc.MB[touchAddr] ? "ON" : "OFF", elm.Text));
                            }
                            else if (touchAddr == 23 || touchAddr == 30 || touchAddr == 31 || (activePageId != 45 && touchAddr == 104 && btnTxt.Contains("ALARM")))
                            {
                                // B. Alarm Acknowledge / Reset All (KASUJ ALARM MB 23 or MB 30/31)
                                plc.MB[23] = true;
                                PulseBit(23, 180);
                                PulseBit(39, 180);
                                plc.BuzzerSilenced = false;
                                if (plc.I[0] && plc.I[3] && plc.I[11] && plc.I[37])
                                {
                                    plc.MB[30] = false;
                                    for (int a = 31; a <= 37; a++) plc.MB[a] = false;
                                    for (int a = 43; a <= 48; a++) plc.MB[a] = false;
                                }
                                plc.Log(string.Format("HMI Touch [Alarm Reset]: Pulsed MB23/MB39 ({0})", elm.Text));
                            }
                            else if (touchAddr == 41)
                            {
                                // C. WYCISZ SYGNALIZATOR (Silence Buzzer on Page 42)
                                plc.BuzzerSilenced = true;
                                plc.O[2] = false;
                                PulseBit(41, 150);
                                plc.Log("HMI Touch [Silence Buzzer]: Alarm horn/lamp silenced");
                            }
                            else if (touchAddr == 50 || touchAddr == 80 || touchAddr == 81)
                            {
                                // D. LOGIN buttons on Page 43 (ZALOGUJ TECH = 80, ZALOGUJ SERW = 81, LOGIN = 50)
                                if (touchAddr == 80)
                                {
                                    plc.MI[90] = 1234;
                                    plc.MI[300] = 1234;
                                    plc.CurrentAccessLevel = 1;
                                    plc.MI[91] = 1;
                                    plc.MB[41] = true;
                                }
                                else if (touchAddr == 81)
                                {
                                    plc.MI[90] = 1999;
                                    plc.MI[300] = 1999;
                                    plc.CurrentAccessLevel = 2;
                                    plc.MI[91] = 2;
                                    plc.MB[41] = true;
                                    plc.MB[42] = true;
                                }
                                else
                                {
                                    if (plc.MI[90] == 0) plc.MI[90] = 1999;
                                    if (plc.MI[90] == 1999 || plc.MI[90] == 1234)
                                    {
                                        plc.MI[300] = 1999;
                                        plc.MB[41] = true;
                                        plc.CurrentAccessLevel = 2;
                                        plc.MI[91] = 2;
                                    }
                                    else
                                    {
                                        plc.MI[300] = plc.MI[90];
                                        plc.CurrentAccessLevel = 1;
                                        plc.MI[91] = 1;
                                    }
                                }
                                PulseBit(touchAddr, 150);
                                plc.Log(string.Format("HMI Touch [Login]: PIN={0}, AccessLevel={1}", plc.MI[90], plc.CurrentAccessLevel));
                            }
                            else if (touchAddr == 51 || touchAddr == 82)
                            {
                                // E. LOGOUT button on Page 43 (WYLOGUJ / OP = 82 or LOGOUT = 51)
                                plc.MI[90] = 0;
                                plc.CurrentAccessLevel = 0;
                                plc.MI[91] = 0;
                                plc.MI[300] = 0;
                                plc.MB[40] = false;
                                plc.MB[41] = false;
                                plc.MB[42] = false;
                                PulseBit(42, 180); // HMI_Btn_Service_Exit
                                PulseBit(touchAddr, 150);
                                plc.Log("HMI Touch [Logout]: Service session closed");
                            }
                            else
                            {
                                // F. Momentary pushbuttons (START, PAUZA, STOP, NASTEPNY KROK, RECEPTURA +, ZAPISZ DO DT)
                                plc.MB[touchAddr] = true;
                                plc.Log(string.Format("HMI Touch [Pulse]: MB{0} = TRUE ({1})", touchAddr, elm.Text));

                                if (touchAddr == 10 || touchAddr == 101 || btnTxt == "START")
                                {
                                    plc.MB[22] = true;
                                    PulseBit(22, 180);      // START -> MB 22 & MB 101
                                }
                                else if (touchAddr == 11 || touchAddr == 102 || btnTxt == "STOP")
                                {
                                    plc.MB[20] = true;
                                    PulseBit(20, 180);      // STOP -> MB 20 & MB 102
                                }
                                else if (touchAddr == 12 || touchAddr == 103 || btnTxt == "PAUZA" || btnTxt == "PAUSE")
                                {
                                    plc.MB[21] = true;
                                    PulseBit(21, 180);      // PAUSE -> MB 21 & MB 103
                                }
                                else if (touchAddr == 25 && btnTxt.Contains("RECEPT"))
                                {
                                    // RECEPTURA + on Page 40
                                    plc.MI[51] = (short)((plc.MI[51] % 10) + 1);
                                    plc.MI[20] = plc.MI[51];
                                    plc.MI[70] = plc.MI[51];
                                }
                                else if (touchAddr == 24 && btnTxt.Contains("KROK +"))
                                {
                                    // KROK + on Page 40
                                    plc.MI[50] = (short)((plc.MI[50] + 1) % 6);
                                    plc.MI[21] = plc.MI[50];
                                }
                                else if (touchAddr == 26 || (touchAddr == 21 && btnTxt.Contains("SAVE")))
                                {
                                    // ZAPISZ DO DT (MB 26) / SAVE RECIPE -> update active & DTI setpoints
                                    if (plc.MI[31] > 0) { plc.MI[52] = (short)(plc.MI[31] * 10); plc.MI[201] = plc.MI[52]; }
                                    if (plc.MI[32] > 0) { plc.MI[53] = (short)(plc.MI[32] * 10); plc.MI[202] = plc.MI[53]; }
                                }

                                // Guarantee a minimum 150 ms pulse so the PLC scan loop captures it
                                int capturedAddr = touchAddr;
                                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                                {
                                    System.Threading.Thread.Sleep(150);
                                    if (!elm.IsPressed)
                                    {
                                        plc.MB[capturedAddr] = false;
                                    }
                                });
                            }
                        }
                        return;
                    }
                    else if (elm.Type == 5 && (elm.VarKeyPadEntry == 1 || (elm.VarType == 8 && IsEditableNumericAddr(elm.VarOprndAddr))))
                    {
                        // Open numeric keypad overlay
                        IsKeypadOpen = true;
                        keypadTargetElement = elm;
                        keypadCurrentInput = "";
                        return;
                    }
                }
            }
        }

        public void HandleTouchUp(int x, int y, int activePageId)
        {
            if (!plc.HasProgramLoaded) return;
            if (!plc.Project.Pages.ContainsKey(activePageId)) return;

            var page = plc.Project.Pages[activePageId];
            foreach (var elm in page.Elements)
            {
                if (elm.IsPressed)
                {
                    elm.IsPressed = false;
                    int touchAddr = elm.TouchOprndAddr;
                    string btnTxt = (elm.Text ?? "").Trim().ToUpperInvariant();
                    bool isCycleTab = btnTxt.Contains("BIEZACY") || btnTxt.Contains("CYKL -");
                    bool isManualToggle = touchAddr == 40 ||
                                          (touchAddr >= 70 && touchAddr <= 79 && !isCycleTab) ||
                                          ((activePageId == 41 || activePageId == 45) && touchAddr >= 90 && touchAddr <= 104);
                    if (touchAddr >= 0 && touchAddr < plc.MB.Length)
                    {
                        // Do not reset toggle buttons or alarm coils (MB 30/31) on TouchUp
                        if (isCycleTab || (!isManualToggle && touchAddr != 30 && touchAddr != 31))
                        {
                            int capturedAddr = touchAddr;
                            var capturedElm = elm;
                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                            {
                                System.Threading.Thread.Sleep(120);
                                if (!capturedElm.IsPressed)
                                {
                                    plc.MB[capturedAddr] = false;
                                }
                            });
                        }
                    }
                }
            }
        }

        private void HandleKeypadClick(int x, int y)
        {
            int kw = 180, kh = 200;
            int kx = (ScreenWidth - kw) / 2;
            int ky = (ScreenHeight - kh) / 2;
            int fieldY = ky + 25;
            int startY = fieldY + 32;
            int btnW = 36, btnH = 26;
            int gap = 4;

            string[] keys = new string[] {
                "7", "8", "9", "CLR",
                "4", "5", "6", "ESC",
                "1", "2", "3", "ENT",
                "+/-", "0", ".", "OK"
            };

            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int idx = r * 4 + c;
                    int bx = kx + 10 + c * (btnW + gap);
                    int by = startY + r * (btnH + gap);
                    Rectangle bRect = new Rectangle(bx, by, btnW, btnH);

                    if (bRect.Contains(x, y))
                    {
                        string k = keys[idx];
                        if (k == "CLR")
                        {
                            keypadCurrentInput = "";
                        }
                        else if (k == "ESC")
                        {
                            IsKeypadOpen = false;
                        }
                        else if (k == "OK" || k == "ENT")
                        {
                            CommitKeypadValue();
                            IsKeypadOpen = false;
                        }
                        else if (k == "+/-")
                        {
                            if (keypadCurrentInput.StartsWith("-"))
                                keypadCurrentInput = keypadCurrentInput.Substring(1);
                            else
                                keypadCurrentInput = "-" + keypadCurrentInput;
                        }
                        else
                        {
                            if (keypadCurrentInput.Length < 8)
                            {
                                keypadCurrentInput += k;
                            }
                        }
                        return;
                    }
                }
            }
        }

        private void CommitKeypadValue()
        {
            if (keypadTargetElement == null) return;
            int val = 0;
            if (int.TryParse(keypadCurrentInput, out val))
            {
                string opType = VlpParser.MapOperandTypeNumber(keypadTargetElement.VarOprndType);
                int addr = keypadTargetElement.VarOprndAddr;
                if (opType == "MI" && addr >= 0 && addr < plc.MI.Length)
                {
                    if (addr == 30) // SP_Min / SP_Czas (min) -> ML 50 & ML 200 (seconds), keep MI 30 = 950 STB limit
                    {
                        int durSec = Math.Max(60, val * 60);
                        plc.ML[50] = durSec;
                        plc.ML[200] = durSec;
                        plc.MI[30] = 950;
                    }
                    else
                    {
                        plc.MI[addr] = (short)val;
                    }

                    // Synchronize HMI keypad setpoints with Ladder process registers
                    if (addr == 31) // SP_T (C) -> MI 52 & MI 201 (0.1 C)
                    {
                        plc.MI[52] = (short)(val * 10);
                        plc.MI[201] = (short)(val * 10);
                    }
                    else if (addr == 32) // SP_RH (%RH) -> MI 53 & MI 202 (0.1 %RH)
                    {
                        plc.MI[53] = (short)(val * 10);
                        plc.MI[202] = (short)(val * 10);
                    }
                    else if (addr == 40) // Hist_T -> MI 54 & MI 203
                    {
                        plc.MI[54] = (short)val;
                        plc.MI[203] = (short)val;
                    }
                    else if (addr == 43) // Wybieg (s) -> T3 preset (ms)
                    {
                        plc.T[3].PresetMs = Math.Max(1000, val * 1000);
                    }
                    else if (addr == 20 || addr == 70) // RCP_NO / Rec_Nr -> MI 51
                    {
                        plc.MI[51] = (short)Math.Max(1, val);
                        plc.MI[20] = plc.MI[51];
                    }
                    else if (addr == 34 || addr == 61 || addr == 62) // Wyd_Went / VFD1 / VFD2 (%) -> MI 56, MI 57 & MI 120
                    {
                        short clampedPct = (short)Math.Max(0, Math.Min(100, val));
                        plc.MI[56] = clampedPct;
                        plc.MI[57] = clampedPct;
                        plc.MI[120] = clampedPct;
                        plc.MI[61] = clampedPct;
                        plc.MI[62] = clampedPct;
                    }
                    else if (addr == 90 || addr == 81 || addr == 82) // USER_PIN / PIN_Tech / PIN_Serw -> MI 300
                    {
                        plc.MI[90] = (short)val;
                        if (val == 1999)
                        {
                            plc.MI[300] = 1999;
                            plc.MB[41] = true;
                            plc.MB[42] = true;
                            plc.CurrentAccessLevel = 2;
                            plc.MI[91] = 2;
                        }
                        else if (val == 1234)
                        {
                            plc.MI[300] = 1234;
                            plc.MB[41] = true;
                            plc.CurrentAccessLevel = 1;
                            plc.MI[91] = 1;
                        }
                        else
                        {
                            plc.MI[300] = (short)val;
                            plc.CurrentAccessLevel = 0;
                            plc.MI[91] = 0;
                        }
                    }
                    plc.Log(string.Format("Updated register MI{0} to {1}", addr, val));
                }
                else if (opType == "ML" && addr >= 0 && addr < plc.ML.Length)
                {
                    plc.ML[addr] = val;
                    plc.Log(string.Format("Updated register ML{0} to {1}", addr, val));
                }
            }
        }

        private Color DecodeUnitronicsColor(int code)
        {
            int r = (code >> 16) & 0xFF;
            int g = (code >> 8) & 0xFF;
            int b = code & 0xFF;
            if (r == 0 && g == 0 && b == 0) return Color.FromArgb(230, 235, 240);
            return Color.FromArgb(r, g, b);
        }
    }
}
