using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace UnitronicsV350Simulator
{
    public enum PlcRunState
    {
        STOP,
        RUN,
        PAUSE
    }

    public class TimerState
    {
        public int PresetMs;
        public int CurrentMs;
        public bool Run;
        public bool Done;
    }

    public class CounterState
    {
        public int Preset;
        public int Current;
        public bool Done;
    }

    public class OperandRef
    {
        public string Type;   // "I", "O", "MB", "MI", "ML", "DW", "T", "C", "SB", "SI", "CONST"
        public int Address;
        public int ConstValue;
    }

    public class LadderElement
    {
        public int ElementId;
        public int ElementType; // 200..207, 212, 300..305, 400..407, 601, 900, 1300 etc.
        public int Xs;
        public int Ys;
        public int Dx;
        public int Dy;
        public string MoreParamsStr = "";
        public int MoreParamsLng = 0;
        public List<OperandRef> Operands = new List<OperandRef>();
    }

    public class LadderRung
    {
        public int StripId;
        public int FuncId;
        public int SeqNum;
        public string Comment = "";
        public bool IsReturnStrip = false;
        public List<LadderElement> Elements = new List<LadderElement>();
    }

    public class Subroutine
    {
        public int FuncId;
        public string Name;
        public List<LadderRung> Rungs = new List<LadderRung>();
    }

    public class HmiElementDef
    {
        public int Id;
        public int DisplayId;
        public int Type;        // 1=Box/Frame, 4=Text, 5=VarDisplay/Num, 11=Button, 14=Bar/Graph
        public int VarType;
        public string Text = "";
        public int Xs;
        public int Ys;
        public int Dx;
        public int Dy;
        public int ColorBack;
        public int ColorFore;
        public int VarOprndType;
        public int VarOprndAddr;
        public int TouchOprndAddr = -1;
        public int VisibleOpAddr = -1;
        public int VarKeyPadEntry = 0;
        public bool IsPressed = false;
    }

    public class HmiPageDef
    {
        public int Id;
        public string Name = "";
        public int ColorBack;
        public List<HmiElementDef> Elements = new List<HmiElementDef>();
    }

    public class LoadedProject
    {
        public string ProjectName = "";
        public string FilePath = "";
        public string PlcModel = "V350-35-RA22";
        public List<string> Modules = new List<string>();
        public Dictionary<int, Subroutine> Subroutines = new Dictionary<int, Subroutine>();
        public Dictionary<int, HmiPageDef> Pages = new Dictionary<int, HmiPageDef>();
        public Dictionary<string, string> OperandDescriptions = new Dictionary<string, string>();
    }

    public class PlcEngine
    {
        // Operandy cyfrowe i analogowe
        public bool[] I = new bool[128];
        public bool[] O = new bool[128];
        public bool[] MB = new bool[4096];
        public short[] MI = new short[2048];
        public int[] ML = new int[512];
        public uint[] DW = new uint[512];
        public float[] MF = new float[64];
        public TimerState[] T = new TimerState[256];
        public CounterState[] C = new CounterState[32];

        // Operandy systemowe
        public bool[] SB = new bool[512];
        public short[] SI = new short[512];

        // Wyjścia analogowe (0..10V lub 4..20mA)
        public double[] AO_Volts = new double[8];

        // Stan poprzedni dla wykrywania zboczy
        private bool[] prev_I = new bool[128];
        private bool[] prev_MB = new bool[4096];
        private bool[] prev_SB = new bool[512];

        public PlcRunState State = PlcRunState.STOP;
        public LoadedProject Project = null;
        public bool HasProgramLoaded { get { return Project != null && Project.Subroutines.Count > 0; } }

        public long ScanCount = 0;
        public double LastScanDurationMs = 0;
        public int TargetScanIntervalMs = 25; // 40 Hz standard PLC scan loop

        private Stopwatch scanStopwatch = new Stopwatch();
        private Stopwatch clock1s = new Stopwatch();
        private Stopwatch clock100ms = new Stopwatch();
        private bool isFirstScan = true;

        public event Action OnScanCompleted;
        public event Action<string> OnLogMessage;

        public PlcEngine()
        {
            ResetMemory();
        }

        public void ResetMemory()
        {
            Array.Clear(I, 0, I.Length);
            Array.Clear(O, 0, O.Length);
            Array.Clear(MB, 0, MB.Length);
            Array.Clear(MI, 0, MI.Length);
            Array.Clear(ML, 0, ML.Length);
            Array.Clear(DW, 0, DW.Length);
            Array.Clear(MF, 0, MF.Length);
            Array.Clear(SB, 0, SB.Length);
            Array.Clear(SI, 0, SI.Length);
            Array.Clear(AO_Volts, 0, AO_Volts.Length);

            Array.Clear(prev_I, 0, prev_I.Length);
            Array.Clear(prev_MB, 0, prev_MB.Length);
            Array.Clear(prev_SB, 0, prev_SB.Length);

            for (int i = 0; i < T.Length; i++)
            {
                T[i] = new TimerState();
            }
            for (int i = 0; i < C.Length; i++)
            {
                C[i] = new CounterState();
            }

            // Domyślne wartości systemowe
            SB[7] = true;   // Bateria podtrzymująca OK
            SI[9] = 1;      // Domyślna strona HMI (Start-up = 1)
            ScanCount = 0;
            isFirstScan = true;
            clock1s.Restart();
            clock100ms.Restart();
        }

        public void Start()
        {
            if (!HasProgramLoaded)
            {
                Log("UWAGA: Nie można uruchomić PLC - brak załadowanego programu z VisiLogic!");
                return;
            }
            State = PlcRunState.RUN;
            isFirstScan = true;
            SB[0] = true; // Power-Up bit
            SB[1] = true; // First scan bit
            Log("PLC przeszedł w tryb RUN (wykonywanie drabinki rozpoczęte).");
        }

        public void Stop()
        {
            State = PlcRunState.STOP;
            Array.Clear(O, 0, O.Length); // Wyłączenie fizycznych wyjść w trybie STOP
            Array.Clear(AO_Volts, 0, AO_Volts.Length);
            Log("PLC zatrzymany (tryb STOP - wyjścia odłączone).");
            if (OnScanCompleted != null) OnScanCompleted();
        }

        public void Reset()
        {
            Stop();
            ResetMemory();
            Log("Wykonano reset sterownika PLC (Warm Restart).");
            if (OnScanCompleted != null) OnScanCompleted();
        }

        public void CycleScan(int elapsedMs)
        {
            scanStopwatch.Restart();

            if (State != PlcRunState.RUN || !HasProgramLoaded)
            {
                // W trybie STOP lub bez programu: nie wykonujemy drabinki
                scanStopwatch.Stop();
                LastScanDurationMs = 0.1;
                return;
            }

            // Aktualizacja zegarów systemowych SB 2 (1s) i SB 3 (100ms)
            if (clock1s.ElapsedMilliseconds >= 1000)
            {
                SB[2] = !SB[2];
                clock1s.Restart();
            }
            if (clock100ms.ElapsedMilliseconds >= 100)
            {
                SB[3] = !SB[3];
                clock100ms.Restart();
            }

            // Obsługa First Scan bit SB 1
            if (isFirstScan)
            {
                SB[1] = true;
                SB[0] = true;
            }
            else
            {
                SB[1] = false;
            }

            // 1. Aktualizacja timerów
            for (int tIdx = 0; tIdx < T.Length; tIdx++)
            {
                var tmr = T[tIdx];
                if (tmr.Run && !tmr.Done)
                {
                    tmr.CurrentMs += elapsedMs;
                    if (tmr.PresetMs > 0 && tmr.CurrentMs >= tmr.PresetMs)
                    {
                        tmr.Done = true;
                    }
                }
            }

            // 2. Wykonanie drabinki załadowanego projektu
            ExecuteLadderProgram();

            // 3. Po pierwszym cyklu wyłączamy First Scan
            if (isFirstScan)
            {
                isFirstScan = false;
            }

            // 4. Kopiowanie stanu do prev_ dla wykrywania zboczy
            Array.Copy(I, prev_I, I.Length);
            Array.Copy(MB, prev_MB, MB.Length);
            Array.Copy(SB, prev_SB, SB.Length);

            ScanCount++;
            scanStopwatch.Stop();
            LastScanDurationMs = scanStopwatch.Elapsed.TotalMilliseconds;
            SI[0] = (short)Math.Max(1, (int)LastScanDurationMs);

            if (OnScanCompleted != null)
            {
                OnScanCompleted();
            }
        }

        private void ExecuteLadderProgram()
        {
            if (Project == null || Project.Subroutines == null) return;

            // Wykonujemy najpierw podprogram główny _Start lub 00_Main jeśli istnieje,
            // a potem wywołane lub wszystkie zdefiniowane podprogramy
            foreach (var kvp in Project.Subroutines)
            {
                var sub = kvp.Value;
                ExecuteSubroutine(sub);
            }
        }

        public void ExecuteSubroutine(Subroutine sub)
        {
            if (sub == null || sub.Rungs == null) return;

            foreach (var rung in sub.Rungs)
            {
                ExecuteRung(rung);
            }
        }

        private void ExecuteRung(LadderRung rung)
        {
            if (rung.Elements == null || rung.Elements.Count == 0) return;

            // Ewaluacja szczebla drabinkowego:
            // Standardowy model Unitronics: Warunki (styki, porównania) znajdują się z lewej strony (Xs < 70).
            // Cewki, Store, wywołania funkcji znajdują się z prawej strony (Xs >= 70 lub ElmntType cewek/akcji).
            bool powerFlow = true;

            // 1. Sprawdzanie styków i warunków (wejścia szczebla)
            foreach (var elm in rung.Elements)
            {
                if (IsConditionElement(elm.ElementType))
                {
                    bool cond = EvaluateCondition(elm);
                    if (!cond)
                    {
                        powerFlow = false;
                        break;
                    }
                }
            }

            // 2. Wykonanie akcji / cewek szczebla jeśli warunek jest spełniony (lub dla cewek typu Direct/Inverted)
            foreach (var elm in rung.Elements)
            {
                if (IsActionElement(elm.ElementType))
                {
                    ExecuteAction(elm, powerFlow);
                }
            }
        }

        private bool IsConditionElement(int type)
        {
            // 200..203 styki, 300..305 porównania
            return (type >= 200 && type <= 203) || (type >= 300 && type <= 305);
        }

        private bool IsActionElement(int type)
        {
            // 204..207, 212 cewki, 400..407 matma, 601 Store, 900 Call, 1300 Timer
            return (type >= 204 && type <= 212) || (type >= 400 && type <= 407) || type == 601 || type == 900 || type == 1300;
        }

        private bool EvaluateCondition(LadderElement elm)
        {
            if (elm.Operands.Count == 0) return true;
            var op = elm.Operands[0];

            switch (elm.ElementType)
            {
                case 200: // Direct Contact NO
                    return GetBitValue(op);

                case 201: // Inverted Contact NC
                    return !GetBitValue(op);

                case 202: // Rise Contact [P]
                    return GetBitValue(op) && !GetPrevBitValue(op);

                case 203: // Fall Contact [N]
                    return !GetBitValue(op) && GetPrevBitValue(op);

                case 300: // Greater Than (A > B)
                    if (elm.Operands.Count >= 2)
                        return GetNumericValue(elm.Operands[0]) > GetNumericValue(elm.Operands[1]);
                    return false;

                case 301: // Greater Equal (A >= B)
                    if (elm.Operands.Count >= 2)
                        return GetNumericValue(elm.Operands[0]) >= GetNumericValue(elm.Operands[1]);
                    return false;

                case 302: // Equal (A == B)
                    if (elm.Operands.Count >= 2)
                        return GetNumericValue(elm.Operands[0]) == GetNumericValue(elm.Operands[1]);
                    return false;

                case 303: // Not Equal (A != B)
                    if (elm.Operands.Count >= 2)
                        return GetNumericValue(elm.Operands[0]) != GetNumericValue(elm.Operands[1]);
                    return false;

                case 304: // Less Equal (A <= B)
                    if (elm.Operands.Count >= 2)
                        return GetNumericValue(elm.Operands[0]) <= GetNumericValue(elm.Operands[1]);
                    return false;

                case 305: // Less Than (A < B)
                    if (elm.Operands.Count >= 2)
                        return GetNumericValue(elm.Operands[0]) < GetNumericValue(elm.Operands[1]);
                    return false;
            }

            return true;
        }

        private void ExecuteAction(LadderElement elm, bool powerFlow)
        {
            if (elm.Operands.Count == 0) return;

            switch (elm.ElementType)
            {
                case 204: // Direct Coil ( )
                    SetBitValue(elm.Operands[0], powerFlow);
                    break;

                case 205: // Inverted Coil (/)
                    SetBitValue(elm.Operands[0], !powerFlow);
                    break;

                case 206: // Set Coil (S)
                    if (powerFlow) SetBitValue(elm.Operands[0], true);
                    break;

                case 207: // Reset Coil (R)
                    if (powerFlow) SetBitValue(elm.Operands[0], false);
                    break;

                case 212: // Toggle Coil
                    if (powerFlow)
                    {
                        bool cur = GetBitValue(elm.Operands[0]);
                        SetBitValue(elm.Operands[0], !cur);
                    }
                    break;

                case 601: // Store (MOVE): A -> Dest
                    if (powerFlow && elm.Operands.Count >= 2)
                    {
                        int srcVal = GetNumericValue(elm.Operands[0]);
                        SetNumericValue(elm.Operands[1], srcVal);
                    }
                    break;

                case 405: // ADD: A + B -> Dest
                    if (powerFlow && elm.Operands.Count >= 3)
                    {
                        int res = GetNumericValue(elm.Operands[0]) + GetNumericValue(elm.Operands[1]);
                        SetNumericValue(elm.Operands[2], res);
                    }
                    break;

                case 400: // SUB: A - B -> Dest
                    if (powerFlow && elm.Operands.Count >= 3)
                    {
                        int res = GetNumericValue(elm.Operands[0]) - GetNumericValue(elm.Operands[1]);
                        SetNumericValue(elm.Operands[2], res);
                    }
                    break;

                case 406: // MUL: A * B -> Dest
                    if (powerFlow && elm.Operands.Count >= 3)
                    {
                        int res = GetNumericValue(elm.Operands[0]) * GetNumericValue(elm.Operands[1]);
                        SetNumericValue(elm.Operands[2], res);
                    }
                    break;

                case 401: // DIV: A / B -> Dest
                    if (powerFlow && elm.Operands.Count >= 3)
                    {
                        int b = GetNumericValue(elm.Operands[1]);
                        if (b != 0)
                        {
                            int res = GetNumericValue(elm.Operands[0]) / b;
                            SetNumericValue(elm.Operands[2], res);
                        }
                    }
                    break;

                case 900: // Call Subroutine
                    if (powerFlow)
                    {
                        int subId = elm.MoreParamsLng;
                        if (Project.Subroutines.ContainsKey(subId))
                        {
                            ExecuteSubroutine(Project.Subroutines[subId]);
                        }
                    }
                    break;

                case 1300: // Timer Block
                    var tOp = elm.Operands[0];
                    if (tOp.Address >= 0 && tOp.Address < T.Length)
                    {
                        var tmr = T[tOp.Address];
                        tmr.Run = powerFlow;
                        if (!powerFlow)
                        {
                            tmr.CurrentMs = 0;
                            tmr.Done = false;
                        }
                        else if (elm.Operands.Count >= 2)
                        {
                            tmr.PresetMs = GetNumericValue(elm.Operands[1]) * 10; // setki ms lub sekundy
                        }
                    }
                    break;
            }
        }

        public bool GetBitValue(OperandRef op)
        {
            if (op == null) return false;
            int addr = op.Address;
            string t = op.Type.ToUpper();

            if (t == "I" && addr >= 0 && addr < I.Length) return I[addr];
            if (t == "O" && addr >= 0 && addr < O.Length) return O[addr];
            if (t == "MB" && addr >= 0 && addr < MB.Length) return MB[addr];
            if (t == "SB" && addr >= 0 && addr < SB.Length) return SB[addr];
            if (t == "T" && addr >= 0 && addr < T.Length) return T[addr].Done;
            return false;
        }

        private bool GetPrevBitValue(OperandRef op)
        {
            if (op == null) return false;
            int addr = op.Address;
            string t = op.Type.ToUpper();

            if (t == "I" && addr >= 0 && addr < prev_I.Length) return prev_I[addr];
            if (t == "MB" && addr >= 0 && addr < prev_MB.Length) return prev_MB[addr];
            if (t == "SB" && addr >= 0 && addr < prev_SB.Length) return prev_SB[addr];
            return false;
        }

        public void SetBitValue(OperandRef op, bool val)
        {
            if (op == null) return;
            int addr = op.Address;
            string t = op.Type.ToUpper();

            if (t == "O" && addr >= 0 && addr < O.Length) O[addr] = val;
            else if (t == "MB" && addr >= 0 && addr < MB.Length) MB[addr] = val;
            else if (t == "SB" && addr >= 0 && addr < SB.Length) SB[addr] = val;
            else if (t == "I" && addr >= 0 && addr < I.Length) I[addr] = val;
        }

        public int GetNumericValue(OperandRef op)
        {
            if (op == null) return 0;
            if (op.Type.ToUpper() == "CONST") return op.ConstValue;

            int addr = op.Address;
            string t = op.Type.ToUpper();

            if (t == "MI" && addr >= 0 && addr < MI.Length) return MI[addr];
            if (t == "ML" && addr >= 0 && addr < ML.Length) return ML[addr];
            if (t == "DW" && addr >= 0 && addr < DW.Length) return (int)DW[addr];
            if (t == "SI" && addr >= 0 && addr < SI.Length) return SI[addr];
            if (t == "T" && addr >= 0 && addr < T.Length) return T[addr].CurrentMs / 1000;
            return 0;
        }

        public void SetNumericValue(OperandRef op, int val)
        {
            if (op == null) return;
            int addr = op.Address;
            string t = op.Type.ToUpper();

            if (t == "MI" && addr >= 0 && addr < MI.Length) MI[addr] = (short)val;
            else if (t == "ML" && addr >= 0 && addr < ML.Length) ML[addr] = val;
            else if (t == "DW" && addr >= 0 && addr < DW.Length) DW[addr] = (uint)val;
            else if (t == "SI" && addr >= 0 && addr < SI.Length) SI[addr] = (short)val;
        }

        public void Log(string msg)
        {
            AppLogger.Info(msg);
            if (OnLogMessage != null)
            {
                OnLogMessage(string.Format("[{0:HH:mm:ss.fff}] {1}", DateTime.Now, msg));
            }
        }
    }
}
