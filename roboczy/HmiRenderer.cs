using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace UnitronicsV350Simulator
{
    public class HmiRenderer
    {
        public const int SCREEN_WIDTH = 320;
        public const int SCREEN_HEIGHT = 240;

        private PlcEngine plc;
        private Bitmap renderBitmap;
        private Graphics g;

        // Stan wirtualnej klawiatury numerycznej (Keypad)
        public bool IsKeypadOpen = false;
        private HmiElementDef keypadTargetElement = null;
        private string keypadCurrentInput = "";

        public HmiRenderer(PlcEngine engine)
        {
            plc = engine;
            renderBitmap = new Bitmap(SCREEN_WIDTH, SCREEN_HEIGHT);
            g = Graphics.FromImage(renderBitmap);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        public Bitmap RenderScreen(int activePageId)
        {
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
            // Tło w stylu przemysłowym OPLC
            using (var bgBrush = new SolidBrush(Color.FromArgb(12, 25, 44)))
            {
                g.FillRectangle(bgBrush, 0, 0, SCREEN_WIDTH, SCREEN_HEIGHT);
            }

            // Nagłówek Unitronics
            using (var hdrBrush = new SolidBrush(Color.FromArgb(20, 48, 85)))
            {
                g.FillRectangle(hdrBrush, 0, 0, SCREEN_WIDTH, 30);
            }
            using (var borderPen = new Pen(Color.FromArgb(0, 150, 255), 1))
            {
                g.DrawLine(borderPen, 0, 30, SCREEN_WIDTH, 30);
            }

            using (var fontHdr = new Font("Segoe UI", 10, FontStyle.Bold))
            using (var brushWhite = new SolidBrush(Color.White))
            {
                g.DrawString("UNITRONICS VISION V350", fontHdr, brushWhite, 10, 6);
            }

            using (var fontSmall = new Font("Segoe UI", 7, FontStyle.Regular))
            using (var brushCyan = new SolidBrush(Color.FromArgb(0, 220, 255)))
            {
                g.DrawString("Micro-OPLC | Color TFT 3.5\"", fontSmall, brushCyan, 185, 9);
            }

            // Ramka informacyjna
            int boxX = 15, boxY = 40, boxW = 290, boxH = 185;
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
                g.DrawString("Hardware:  Unitronics V350-35-RA22", fontSysBold, brushYellow, boxX + 10, boxY + 10);
                g.DrawString("Expansion: EX-A2X Bus Ready", fontSys, brushText, boxX + 10, boxY + 26);
                g.DrawString("OS Vers:   4.04 (12) | Boot: 2.02 (03)", fontSys, brushText, boxX + 10, boxY + 42);
                g.DrawString("PCOM Port: TCP/IP Port 20256 (PCOM)", fontSys, brushGreen, boxX + 10, boxY + 58);

                // Separator
                g.DrawLine(Pens.SteelBlue, boxX + 10, boxY + 76, boxX + boxW - 10, boxY + 76);

                // Status programu
                using (var alertFont = new Font("Segoe UI", 9, FontStyle.Bold))
                using (var alertBrush = new SolidBrush(Color.FromArgb(255, 82, 82)))
                {
                    g.DrawString("[ STATUS: BRAK PROGRAMU UŻYTKOWNIKA ]", alertFont, alertBrush, boxX + 15, boxY + 84);
                }

                using (var infoFont = new Font("Segoe UI", 8, FontStyle.Regular))
                using (var infoBrush = new SolidBrush(Color.FromArgb(180, 200, 220)))
                {
                    g.DrawString("Sterownik jest w czystym stanie fabrycznym.", infoFont, infoBrush, boxX + 15, boxY + 106);
                    g.DrawString("Wszystkie wyjścia są wyłączone (OFF).", infoFont, infoBrush, boxX + 15, boxY + 122);
                    g.DrawString("Aby uruchomić aplikację:", infoFont, brushYellow, boxX + 15, boxY + 140);
                    g.DrawString("-> Kliknij menu 'Plik -> Załaduj projekt (*.vlp)'", infoFont, brushGreen, boxX + 20, boxY + 155);
                    g.DrawString("-> LUB połącz VisiLogic po TCP 127.0.0.1:20256", infoFont, brushGreen, boxX + 20, boxY + 168);
                }
            }
        }

        private void RenderProjectPage(int pageId)
        {
            if (plc.Project == null || !plc.Project.Pages.ContainsKey(pageId))
            {
                // Fallback na pierwszą dostępną stronę
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

            // Tło strony
            Color bgColor = Color.FromArgb(235, 238, 242); // Standardowy szary HMI Unitronics
            if (page.ColorBack != 0)
            {
                bgColor = DecodeUnitronicsColor(page.ColorBack);
            }
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, 0, 0, SCREEN_WIDTH, SCREEN_HEIGHT);
            }

            // Rysowanie elementów HMI
            foreach (var elm in page.Elements)
            {
                RenderHmiElement(elm);
            }

            // Pasek statusowy HMI na dole
            using (var barBrush = new SolidBrush(Color.FromArgb(40, 44, 52)))
            {
                g.FillRectangle(barBrush, 0, 222, SCREEN_WIDTH, 18);
            }
            using (var fontBar = new Font("Segoe UI", 7, FontStyle.Regular))
            using (var brushBarText = new SolidBrush(Color.FromArgb(220, 220, 220)))
            {
                string statusText = string.Format("P{0}: {1} | SCAN: {2}ms | {3}",
                    page.Id, page.Name, plc.SI[0], plc.State == PlcRunState.RUN ? "RUN" : "STOP");
                g.DrawString(statusText, fontBar, brushBarText, 4, 224);
            }
        }

        private void RenderHmiElement(HmiElementDef elm)
        {
            Rectangle rect = new Rectangle(elm.Xs, elm.Ys, Math.Max(elm.Dx, 10), Math.Max(elm.Dy, 10));

            // Obsługa widoczności (VisibleOpAddr)
            if (elm.VisibleOpAddr >= 0 && elm.VisibleOpAddr < plc.MB.Length)
            {
                // Jeśli bit ukrycia jest aktywny, nie rysujemy
                // (W VisiLogic zależnie od konfiguracji)
            }

            // Rozróżnienie elementów
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
                // Wykres słupkowy (Bargraph)
                if (elm.VarType == 24 || (elm.Text != null && elm.Text.StartsWith("BAR_")))
                {
                    RenderBargraph(elm, rect);
                    return;
                }

                // Wskaźnik stanu binarnego (wentylatory, grzałki, sekcje, wyjścia cyfrowe O / bity MB)
                string opType = VlpParser.MapOperandTypeNumber(elm.VarOprndType);
                if (elm.VarType == 2 || opType == "O" || (opType == "MB" && elm.TouchOprndAddr < 0))
                {
                    RenderBinaryStatusIndicator(elm, rect);
                    return;
                }

                // Standardowy wyświetlacz numeryczny zmiennej
                RenderNumericVariable(elm, rect);
                return;
            }

            switch (elm.Type)
            {
                case 1: // Ramka / Box / Kontener
                    using (var fillBrush = new SolidBrush(Color.FromArgb(220, 225, 232)))
                    using (var pen = new Pen(Color.FromArgb(160, 170, 185), 1))
                    {
                        g.FillRectangle(fillBrush, rect);
                        g.DrawRectangle(pen, rect);
                    }
                    break;

                case 4: // Tekst statyczny
                    using (var font = new Font("Segoe UI", 8, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.FromArgb(30, 35, 45)))
                    {
                        var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                        g.DrawString(elm.Text, font, brush, rect, sf);
                    }
                    break;

                default:
                    // Domyślne tło i tekst
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

            Color topCol, botCol, borderCol, textCol;
            if (isDown)
            {
                topCol = Color.FromArgb(170, 180, 195);
                botCol = Color.FromArgb(210, 215, 225);
                borderCol = Color.FromArgb(80, 90, 110);
                textCol = Color.Black;
            }
            else if (isBitActive && (elm.TouchOprndAddr == 40 || (elm.TouchOprndAddr >= 70 && elm.TouchOprndAddr <= 104)))
            {
                // Aktywny przełącznik testowy/ręczny (np. W1..W6, G1..G6, VFD1, VFD2, MOC 400V, RESUME)
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

                if (!isDown && !isBitActive)
                {
                    g.DrawLine(Pens.White, rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Top + 1);
                    g.DrawLine(Pens.White, rect.Left + 1, rect.Top + 1, rect.Left + 1, rect.Bottom - 1);
                }
            }

            // Tekst na przycisku
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
                isActive = plc.O[elm.VarOprndAddr];
            }
            else if (opType == "MB" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.MB.Length)
            {
                isActive = plc.MB[elm.VarOprndAddr];
            }
            else if (elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.O.Length)
            {
                isActive = plc.O[elm.VarOprndAddr];
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

        private void RenderBargraph(HmiElementDef elm, Rectangle rect)
        {
            // Tło wykresu słupkowego
            using (var bgBrush = new SolidBrush(Color.FromArgb(20, 26, 36)))
            using (var borderPen = new Pen(Color.FromArgb(90, 105, 125), 1))
            {
                g.FillRectangle(bgBrush, rect);
                g.DrawRectangle(borderPen, rect);
            }

            // Odczyt wartości z PLC
            int val = 0;
            string opType = VlpParser.MapOperandTypeNumber(elm.VarOprndType);
            if (opType == "MI" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.MI.Length)
            {
                val = plc.MI[elm.VarOprndAddr];
            }
            else if (opType == "ML" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.ML.Length)
            {
                val = plc.ML[elm.VarOprndAddr];
            }

            // Zakres domyślny 0..100 (lub 0..6 dla sekcji grzałek)
            int minVal = 0;
            int maxVal = 100;
            if (elm.Text != null && elm.Text.Contains("GRZ")) maxVal = 6;
            else if (elm.VarOprndAddr == 50) maxVal = 6;

            int clampedVal = Math.Max(minVal, Math.Min(val, maxVal));
            double ratio = (maxVal > minVal) ? ((double)(clampedVal - minVal) / (maxVal - minVal)) : 0;
            int barHeight = (int)((rect.Height - 4) * ratio);

            // Wybór koloru słupka w zależności od typu pomiaru
            Color barColorTop, barColorBot;
            if (elm.Text != null && (elm.Text.Contains("BAR_T") || elm.Text.Contains("TEMP")))
            {
                barColorTop = Color.FromArgb(255, 80, 50);
                barColorBot = Color.FromArgb(200, 30, 20);
            }
            else if (elm.Text != null && (elm.Text.Contains("BAR_RH") || elm.Text.Contains("WILG")))
            {
                barColorTop = Color.FromArgb(0, 210, 255);
                barColorBot = Color.FromArgb(0, 110, 200);
            }
            else if (elm.Text != null && (elm.Text.Contains("VFD") || elm.Text.Contains("WENT")))
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

            // Poziome linie podziałki (25%, 50%, 75%)
            using (var gridPen = new Pen(Color.FromArgb(60, 75, 95), 1))
            {
                for (int pct = 25; pct <= 75; pct += 25)
                {
                    int gy = rect.Bottom - 2 - (int)((rect.Height - 4) * (pct / 100.0));
                    g.DrawLine(gridPen, rect.Left + 2, gy, rect.Right - 3, gy);
                }
            }

            // Podpis słupka lub wartość
            using (var font = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
                g.DrawString(val.ToString(), font, textBrush, new Rectangle(rect.X, rect.Y + 2, rect.Width, 12), sf);
            }
        }

        private void RenderNumericVariable(HmiElementDef elm, Rectangle rect)
        {
            // Tło pola edycyjnego (białe z ciemniejszą obwódką)
            using (var bgBrush = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.FromArgb(130, 140, 155), 1))
            {
                g.FillRectangle(bgBrush, rect);
                g.DrawRectangle(pen, rect);
            }

            // Pobranie wartości rejestru z PLC
            int val = 0;
            string opType = VlpParser.MapOperandTypeNumber(elm.VarOprndType);
            if (opType == "MI" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.MI.Length)
            {
                val = plc.MI[elm.VarOprndAddr];
            }
            else if (opType == "ML" && elm.VarOprndAddr >= 0 && elm.VarOprndAddr < plc.ML.Length)
            {
                val = plc.ML[elm.VarOprndAddr];
            }

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
            // Półprzezroczyste przyciemnienie tła
            using (var dimBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                g.FillRectangle(dimBrush, 0, 0, SCREEN_WIDTH, SCREEN_HEIGHT);
            }

            // Okno klawiatury numerycznej
            int kw = 180, kh = 200;
            int kx = (SCREEN_WIDTH - kw) / 2;
            int ky = (SCREEN_HEIGHT - kh) / 2;

            using (var boxBrush = new SolidBrush(Color.FromArgb(35, 40, 50)))
            using (var boxPen = new Pen(Color.FromArgb(0, 150, 255), 2))
            {
                g.FillRectangle(boxBrush, kx, ky, kw, kh);
                g.DrawRectangle(boxPen, kx, ky, kw, kh);
            }

            // Tytuł
            using (var fontHdr = new Font("Segoe UI", 8, FontStyle.Bold))
            using (var brushHdr = new SolidBrush(Color.White))
            {
                g.DrawString("EDYCJA WARTOŚCI (KEYPAD)", fontHdr, brushHdr, kx + 10, ky + 6);
            }

            // Pole wartości bieżącej
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

            // Klawisze 1..9, 0, CLR, ENTER, CANCEL
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
            string txt = (btnText ?? "").Trim().ToUpper();

            // 1. Po adresie TouchOprndAddr (MB 210..220)
            switch (touchAddr)
            {
                case 210: return 1;  // Start-Up / Główny
                case 211: return 36; // L2: Kontrola Procesu
                case 212: return 40; // L3: Receptury
                case 213: return 41; // L3: Wentylacja i Rekuperacja
                case 214: return 42; // L4: Alarmy
                case 215: return 43; // L4: Logowanie / Uprawnienia / Dostęp
                case 216: return 44; // L4: Serwis Specjalny
                case 217: return 45; // L4: Serwis Testy Ręczne Wyjść
                case 218: return 37; // L2: Wykres T / RH
                case 219: return 38; // L2: Wykres T / Grzałki
                case 220: return 39; // L2: Wykres Wentylacja
            }

            // 2. Po tekście przycisku
            if (txt.Contains("GLOWNY") || txt.Contains("STARTUP") || txt.Contains("START-UP")) return 1;
            if (txt.Contains("PROCES") || txt.Contains("KONTROLA")) return 36;
            if (txt.Contains("RECEPT")) return 40;
            if (txt.Contains("WENTYL")) return 41;
            if (txt.Contains("ALARM")) return 42;
            if (txt.Contains("DOSTEP") || txt.Contains("LOGOW")) return 43;
            if (txt.Contains("SERWIS >>")) return 44;
            if (txt.Contains("TESTY")) return 45;
            if (txt.Contains("T / RH") || txt.Contains("T/RH")) return 37;
            if (txt.Contains("T / GRZ") || txt.Contains("T/GRZ")) return 38;
            if (txt.Contains("WENT >>") || txt.Contains("T/WENT")) return 39;

            return 0;
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
            foreach (var elm in page.Elements)
            {
                Rectangle rect = new Rectangle(elm.Xs, elm.Ys, Math.Max(elm.Dx, 10), Math.Max(elm.Dy, 10));
                if (rect.Contains(x, y))
                {
                    if (elm.Type == 11 || elm.TouchOprndAddr >= 0)
                    {
                        elm.IsPressed = true;
                        int touchAddr = elm.TouchOprndAddr;

                        // 1. Sprawdzenie nawigacji ekranowej
                        int targetPage = GetTargetDisplayForButton(touchAddr, elm.Text);
                        if (targetPage > 0)
                        {
                            plc.SI[9] = (short)targetPage;
                            plc.Log(string.Format("HMI Dotyk: Przejście do ekranu P{0} ({1})", targetPage, elm.Text));
                        }

                        // 2. Obsługa bitu dotykowego w PLC
                        if (touchAddr >= 0 && touchAddr < plc.MB.Length)
                        {
                            // A. Przyciski typu toggle na ekranach serwisowych (MB 40, MB 90..104, MB 70..73, MB 80..82)
                            if (touchAddr == 40 || (touchAddr >= 70 && touchAddr <= 104))
                            {
                                plc.MB[touchAddr] = !plc.MB[touchAddr];
                                plc.Log(string.Format("HMI Dotyk [Toggle]: MB{0} = {1} ({2})", touchAddr, plc.MB[touchAddr] ? "ON" : "OFF", elm.Text));
                            }
                            else
                            {
                                // B. Przyciski chwilowe (START MB22, STOP MB20, PAUZA MB21, KWITUJ MB23, KROK MB24, itp.)
                                plc.MB[touchAddr] = true;
                                plc.Log(string.Format("HMI Dotyk [Impuls]: MB{0} = TRUE ({1})", touchAddr, elm.Text));

                                // Gwarantowany minimalny impuls (150 ms) w tle, aby pętla PLC na pewno go przetworzyła
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
                    }
                    else if (elm.Type == 5 && elm.VarKeyPadEntry == 1)
                    {
                        // Otwarcie klawiatury numerycznej
                        IsKeypadOpen = true;
                        keypadTargetElement = elm;
                        keypadCurrentInput = "";
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
                    if (touchAddr >= 0 && touchAddr < plc.MB.Length)
                    {
                        // Dla przycisków toggle nie resetujemy stanu przy TouchUp
                        if (touchAddr != 40 && !(touchAddr >= 70 && touchAddr <= 104))
                        {
                            plc.MB[touchAddr] = false;
                        }
                    }
                }
            }
        }

        private void HandleKeypadClick(int x, int y)
        {
            int kw = 180, kh = 200;
            int kx = (SCREEN_WIDTH - kw) / 2;
            int ky = (SCREEN_HEIGHT - kh) / 2;
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
                if (opType == "MI" && keypadTargetElement.VarOprndAddr >= 0 && keypadTargetElement.VarOprndAddr < plc.MI.Length)
                {
                    plc.MI[keypadTargetElement.VarOprndAddr] = (short)val;
                    plc.Log(string.Format("Zmieniono nastawę MI{0} na {1}", keypadTargetElement.VarOprndAddr, val));
                }
                else if (opType == "ML" && keypadTargetElement.VarOprndAddr >= 0 && keypadTargetElement.VarOprndAddr < plc.ML.Length)
                {
                    plc.ML[keypadTargetElement.VarOprndAddr] = val;
                    plc.Log(string.Format("Zmieniono nastawę ML{0} na {1}", keypadTargetElement.VarOprndAddr, val));
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
