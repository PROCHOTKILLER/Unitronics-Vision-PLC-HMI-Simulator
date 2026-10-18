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
        public string HardwareTarget { get { return PlcModel; } set { PlcModel = value; } }
        public List<string> Modules = new List<string>();
        public Dictionary<int, Subroutine> Subroutines = new Dictionary<int, Subroutine>();
        public Dictionary<int, HmiPageDef> Pages = new Dictionary<int, HmiPageDef>();
        public Dictionary<string, string> OperandDescriptions = new Dictionary<string, string>();
        public Dictionary<int, int> TimerPresets = new Dictionary<int, int>();
        public Dictionary<string, int> PowerUpValues = new Dictionary<string, int>();
    }

    public class PlcEngine
    {
        // Digital and analog operands
        public bool[] I = new bool[128];
        public bool[] O = new bool[128];
        public bool[] MB = new bool[4096];
        public short[] MI = new short[2048];
        public int[] ML = new int[512];
        public uint[] DW = new uint[512];
        public float[] MF = new float[64];
        public TimerState[] T = new TimerState[256];
        public CounterState[] C = new CounterState[32];

        // System operands
        public bool[] SB = new bool[512];
        public short[] SI = new short[512];

        // Analog outputs (0..10V or 4..20mA scaled to volts)
        public double[] AO_Volts = new double[8];

        // Previous scan state for rising/falling edge detection
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
        private int accumMs1s = 0;
        private int accumMs100ms = 0;
        private bool isFirstScan = true;
        private short prev_MI301 = 1;
        private double simTempDeciC = 220.0;
        private double simRhDeciPct = 450.0;
        public short CurrentAccessLevel = 0;
        public bool BuzzerSilenced = false;

        public event Action OnScanCompleted;
        public event Action<string> OnLogMessage;

        public HardwareProfile Profile { get; set; }
        public PlcMemoryStorage Memory { get; private set; }

        public PlcEngine(HardwareProfile profile = null)
        {
            Profile = profile ?? (SimulatorConfig.Current != null && SimulatorConfig.Current.Profile != null ? SimulatorConfig.Current.Profile : HardwareProfile.CreateDefaultProfile());
            Memory = new PlcMemoryStorage();
            ResetMemory();
        }

        public void SetProfile(HardwareProfile newProfile)
        {
            if (newProfile != null)
            {
                Profile = newProfile;
                Log(string.Format("Updated PLC hardware profile: {0}", Profile.GetModelFullName()));
            }
        }

        private bool IsKomora9Project()
        {
            if (Project == null) return false;
            if (Project.ProjectName != null && Project.ProjectName.IndexOf("Komora", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (Project.Pages != null && Project.Pages.ContainsKey(36) && Project.Pages.ContainsKey(40))
                return true;
            if (Project.OperandDescriptions != null && Project.OperandDescriptions.ContainsKey("MB22") && Project.OperandDescriptions.ContainsKey("MI100"))
                return true;
            return false;
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

            // Apply timer presets and power-up values from loaded project if available
            if (Project != null)
            {
                if (Project.TimerPresets != null)
                {
                    foreach (var kvp in Project.TimerPresets)
                    {
                        if (kvp.Key >= 0 && kvp.Key < T.Length)
                        {
                            T[kvp.Key].PresetMs = kvp.Value;
                        }
                    }
                }

                if (Project.PowerUpValues != null)
                {
                    foreach (var kvp in Project.PowerUpValues)
                    {
                        string key = kvp.Key;
                        int val = kvp.Value;
                        int addr = 0;
                        if (key.StartsWith("MB") && int.TryParse(key.Substring(2), out addr) && addr >= 0 && addr < MB.Length)
                            MB[addr] = (val != 0);
                        else if (key.StartsWith("MI") && int.TryParse(key.Substring(2), out addr) && addr >= 0 && addr < MI.Length)
                            MI[addr] = (short)val;
                        else if (key.StartsWith("ML") && int.TryParse(key.Substring(2), out addr) && addr >= 0 && addr < ML.Length)
                            ML[addr] = val;
                        else if (key.StartsWith("DW") && int.TryParse(key.Substring(2), out addr) && addr >= 0 && addr < DW.Length)
                            DW[addr] = (uint)val;
                        else if (key.StartsWith("SI") && int.TryParse(key.Substring(2), out addr) && addr >= 0 && addr < SI.Length)
                            SI[addr] = (short)val;
                    }
                }

                // Automatically set Normally-Closed (_NC) digital inputs to TRUE (closed circuit in healthy state)
                if (Project.OperandDescriptions != null)
                {
                    foreach (var kvp in Project.OperandDescriptions)
                    {
                        int inAddr;
                        if (kvp.Key.StartsWith("I") && int.TryParse(kvp.Key.Substring(1), out inAddr) && inAddr >= 0 && inAddr < I.Length)
                        {
                            if (kvp.Value != null && kvp.Value.EndsWith("_NC", StringComparison.OrdinalIgnoreCase))
                            {
                                I[inAddr] = true;
                            }
                        }
                    }
                }

                if (IsKomora9Project())
                {
                    // Healthy NC safety circuit inputs for Komora 9
                    I[0] = true;  // Przycisk_STOP_NC
                    I[3] = true;  // Przekaznik_Bezpieczenstwa (checked via NC contact 201[I3] in 06_SAFETY_ALARMS Rung 2)
                    I[4] = true;  // Otwarcie_Drzwi_Komory_NC
                    for (int idx = 5; idx <= 11; idx++) I[idx] = true; // Termik_Wentylator_1..6_NC & Termostat_Grzalek_NC
                    I[32] = true; // Presostaty_Rekuperatora_NC
                    I[33] = true; // Gotowosc_VFD_1_NC
                    I[34] = true; // Gotowosc_VFD_2_NC
                    I[37] = true; // Przekaznik_Poprawne_Zasilanie_NC

                    // Default timer presets when not stored in UDB
                    if (T[0].PresetMs <= 0) T[0].PresetMs = 1000;  // 1.0s recipe clock
                    if (T[1].PresetMs <= 0) T[1].PresetMs = 2000;  // 2.0s fan 1 feedback
                    if (T[2].PresetMs <= 0) T[2].PresetMs = 2000;  // 2.0s fan 2 feedback
                    if (T[3].PresetMs <= 0) T[3].PresetMs = 5000;  // 5.0s cooling run-out
                    if (T[4].PresetMs <= 0) T[4].PresetMs = 5000;
                    if (T[5].PresetMs <= 0) T[5].PresetMs = 5000;
                    if (T[6].PresetMs <= 0) T[6].PresetMs = 3000;  // 3.0s damper feedback

                    // Default ambient analog inputs (22.0 °C, 45.0 %RH, 2000 m3/h airflow)
                    simTempDeciC = 220.0;
                    simRhDeciPct = 450.0;
                    MI[1] = 220;   // Analog_Temp_Pt100 (22.0 °C)
                    MI[2] = 2293;  // Analog_Wilg (4-20mA raw for 45.0 %RH: 819 + 0.45 * 3276 = 2293)
                    MI[3] = 2130;  // Analog_Przeplyw_Pow (~2000 m3/h)
                    MI[4] = 200;   // Analog_Temp_Zewn (20.0 °C)
                    MI[5] = 220;   // Analog_Temp_Nawiew
                    MI[6] = 220;   // Analog_Temp_Wywiew
                    MI[7] = 210;   // Analog_Temp_Rekup

                    // Default DTI Recipe Row buffer & active step parameters (80.0 °C, 30.0 %RH, 60 min, Mode 2 = Heating+Drying)
                    ML[200] = 3600; // DTI_Row_Czas_s (60 min)
                    MI[201] = 800;  // DTI_Row_Temp_Zad (80.0 °C)
                    MI[202] = 300;  // DTI_Row_Wilg_Zad (30.0 %RH)
                    MI[203] = 10;   // DTI_Row_Delta_T (1.0 °C)
                    MI[204] = 2;    // DTI_Row_Tryb (2 = Grzanie + Suszenie)

                    ML[50] = 3600;  // Step_Duration_s (3600 s = 60 min)
                    ML[51] = 0;     // Step_Elapsed_s (0..3600 s; incremented by 03_RECIPES StripId=1018)
                    MI[51] = 1;     // Nr_Aktywnej_Receptury
                    MI[52] = 800;   // Step_Target_Temp (80.0 °C)
                    MI[53] = 300;   // Step_Target_RH (30.0 %RH)
                    MI[54] = 10;    // Step_Max_Delta_T
                    MI[55] = 2;     // Step_Mode (2 = Heating + Drying)
                    MI[56] = 65;    // Step_Vent_Power (65%)
                    MI[57] = 65;    // Step_Drying_Vent_Power (65%)
                    MI[59] = 1;     // Step_Circ_Fans_En (1 = Enabled)
                    MI[103] = 20;   // Hist_Wilg_Rekup (2.0 %RH hysteresis)

                    // HMI & safety registers
                    MI[20] = 1;     // RCP_NO
                    MI[21] = 0;     // STEP
                    MI[23] = 60;    // MIN (Remaining minutes)
                    MI[30] = 950;   // Lim_Temp_STB_Max (95.0 °C in 06_SAFETY_ALARMS StripId=1055)
                    MI[31] = 80;    // SP_T (80 °C)
                    MI[32] = 30;    // SP_RH (30 %RH)
                    MI[40] = 10;    // Hist_T (1.0 °C)
                    MI[43] = 5;     // Wybieg (5 s)
                    MI[70] = 25;    // PID_KP
                    MI[71] = 120;   // PID_TI
                    MI[72] = 1;     // NrCykl / PID_TD
                    MI[73] = 105;   // MAX_T (105 °C)
                    MI[81] = 1234;  // PIN_Tech (Page 43)
                    MI[82] = 1999;  // PIN_Serw (Page 43)
                    CurrentAccessLevel = 0;
                    BuzzerSilenced = false;

                    Array.Copy(I, prev_I, I.Length);
                }
            }

            // Default Unitronics Vision OS system operand values:
            // SB 0 = Always 0, SB 1 = Always 1, SB 2 = Power-Up bit (1 for first scan),
            // SB 3 = 1s pulse, SB 7 = 100ms pulse, SB 8 = Battery OK
            SB[0] = false;
            SB[1] = true;
            SB[2] = true;
            SB[7] = true;
            SB[8] = true;
            SI[9] = 1;      // Default HMI page (Start-Up Display ID = 1)
            prev_MI301 = 1;
            ScanCount = 0;
            isFirstScan = true;
            accumMs1s = 0;
            accumMs100ms = 0;
            clock1s.Restart();
            clock100ms.Restart();

            if (Memory != null)
            {
                if (Project != null)
                {
                    Memory.SaveProjectToRom(Project, Profile);
                }
                Memory.SyncRamFromEngine(this, true);
            }
        }

        public void Start()
        {
            if (!HasProgramLoaded)
            {
                Log("WARNING: Cannot start PLC - no VisiLogic project loaded!");
                return;
            }
            State = PlcRunState.RUN;
            isFirstScan = true;
            SB[0] = false; // Always 0
            SB[1] = true;  // Always 1
            SB[2] = true;  // Power-Up / First scan bit
            Log("PLC switched to RUN mode (ladder execution started).");
            CycleScan(10); // Execute initial Power-Up scan (SB 2 = 1) immediately upon entering RUN
            if (Memory != null) Memory.SyncRamFromEngine(this, true);
        }

        public void Stop()
        {
            State = PlcRunState.STOP;
            Array.Clear(O, 0, O.Length); // Turn off physical outputs in STOP mode
            Array.Clear(AO_Volts, 0, AO_Volts.Length);
            if (Memory != null) Memory.SyncRamFromEngine(this, true);
            Log("PLC stopped (STOP mode - outputs de-energized).");
            if (OnScanCompleted != null) OnScanCompleted();
        }

        public void Reset()
        {
            Stop();
            ResetMemory();
            Log("PLC controller reset executed (Warm Restart).");
            if (OnScanCompleted != null) OnScanCompleted();
        }

        public void CycleScan(int elapsedMs)
        {
            scanStopwatch.Restart();

            if (State != PlcRunState.RUN || !HasProgramLoaded)
            {
                // In STOP mode or without a loaded program: skip ladder execution, but keep ram.bin synchronized
                if (Memory != null) Memory.SyncRamFromEngine(this, false);
                scanStopwatch.Stop();
                LastScanDurationMs = 0.1;
                return;
            }

            bool pulse1sEdge = false;
            accumMs1s += elapsedMs;
            accumMs100ms += elapsedMs;
            // Update Unitronics Vision system clock bits: SB 3 (1s pulse) and SB 7 (100ms pulse)
            if (clock1s.ElapsedMilliseconds >= 500 || accumMs1s >= 500)
            {
                SB[3] = !SB[3];
                if (SB[3]) pulse1sEdge = true;
                clock1s.Restart();
                accumMs1s = 0;
            }
            if (clock100ms.ElapsedMilliseconds >= 50 || accumMs100ms >= 50)
            {
                SB[7] = !SB[7];
                clock100ms.Restart();
                accumMs100ms = 0;
            }

            // Maintain Always-0 (SB 0), Always-1 (SB 1), and Power-Up First Scan bit (SB 2)
            SB[0] = false;
            SB[1] = true;
            SB[2] = isFirstScan;

            // 1. Update timers
            for (int tIdx = 0; tIdx < T.Length; tIdx++)
            {
                var tmr = T[tIdx];
                if (tmr.Run && !tmr.Done)
                {
                    tmr.CurrentMs += elapsedMs;
                    if (tmr.PresetMs > 0 && tmr.CurrentMs >= tmr.PresetMs)
                    {
                        tmr.CurrentMs = tmr.PresetMs;
                        tmr.Done = true;
                    }
                    else if (tmr.PresetMs <= 0)
                    {
                        tmr.Done = true;
                    }
                }
            }

            int prevML51 = ML[51];

            // 2. Execute loaded ladder program
            ExecuteLadderProgram();

            // 3. Synchronize Komora 9 physical plant feedback, PID output, and HMI display registers
            if (IsKomora9Project())
            {
                UpdateKomora9Simulation(elapsedMs, pulse1sEdge, prevML51);
            }

            // 4. Clear First Scan flag after the first scan cycle completes
            if (isFirstScan)
            {
                isFirstScan = false;
                SB[2] = false;
            }

            // 5. Sync analog output voltages (AO_Volts[0..7]) from MI 280..287 or MI 110..117 (12-bit 0..4095 -> 0..10.0V)
            for (int ao = 0; ao < AO_Volts.Length; ao++)
            {
                int val280 = (280 + ao < MI.Length) ? MI[280 + ao] : 0;
                int val110 = (!IsKomora9Project() && 110 + ao < MI.Length) ? MI[110 + ao] : 0;
                int rawDac = (val280 != 0) ? val280 : val110;
                AO_Volts[ao] = Math.Max(0.0, Math.Min(10.0, (rawDac / 4095.0) * 10.0));
            }

            // 6. Copy current state to prev_ arrays for edge detection
            Array.Copy(I, prev_I, I.Length);
            Array.Copy(MB, prev_MB, MB.Length);
            Array.Copy(SB, prev_SB, SB.Length);

            ScanCount++;
            scanStopwatch.Stop();
            LastScanDurationMs = scanStopwatch.Elapsed.TotalMilliseconds;
            SI[0] = (short)Math.Max(1, (int)LastScanDurationMs);

            if (Memory != null)
            {
                Memory.SyncRamFromEngine(this, false);
            }

            if (OnScanCompleted != null)
            {
                OnScanCompleted();
            }
        }

        private void UpdateKomora9Simulation(int elapsedMs, bool pulse1sEdge, int prevML51)
        {
            // A. Contactor auxiliary feedback contacts & damper limit switch (O12 -> I12)
            // In Komora_9_PLC.vlp (Subroutine 30 StripId 1053), I35/I36 monitor heater contactor O33 weld status (!O33 && I35 -> T6);
            // in English Komora9_VisiLogic.vlp (Subroutine 7), I35/I36 are fan contactor feedbacks for O3/O4.
            if (Project != null && Project.Subroutines != null && Project.Subroutines.ContainsKey(30))
            {
                I[35] = O[33];
                I[36] = O[33] && O[34];
            }
            else
            {
                I[35] = O[3];
                I[36] = O[4];
            }
            I[12] = O[12];

            // B. Ensure recipe parameters and STB safety limit (MI 30 = 950 -> 95.0 °C) are populated
            if (MI[30] < 850) MI[30] = 950;
            if (ML[200] <= 0) ML[200] = 3600;
            if (MI[201] <= 0) MI[201] = 800;
            if (MI[202] <= 0) MI[202] = 300;
            if (MI[203] <= 0) MI[203] = 10;
            if (MI[204] <= 0) MI[204] = 2;
            if (ML[50] <= 0) ML[50] = ML[200];
            if (MI[52] <= 0) MI[52] = MI[201];
            if (MI[53] <= 0) MI[53] = MI[202];
            if (MI[54] <= 0 || (MI[54] >= 1 && MI[54] <= 6)) MI[54] = 10;
            if (MI[55] < 1 || MI[55] > 2) MI[55] = 2;
            if (MI[59] <= 0) MI[59] = 1;
            if (MI[56] <= 0) MI[56] = 65;
            MI[57] = MI[56]; // Keep Mode 2 drying fan power synced with MI 56

            // Track ventilation cascade completion (MB 10) and stop/cooldown transition (MB 3)
            MB[10] = O[3] && O[4] && O[5];
            if (!MB[1] && prev_MB[1] && !MB[30])
            {
                MB[3] = true;
            }
            if (!MB[1])
            {
                MB[11] = false;
                MB[12] = false;
            }

            // C. 1-second recipe step elapsed counter (ML 51) if not already incremented by ladder StripId=1018
            if (MB[1] && !MB[2])
            {
                if (pulse1sEdge && ML[51] == prevML51)
                {
                    ML[51]++;
                    ML[52]++;
                }
                if (ML[50] > 0 && ML[51] >= ML[50])
                {
                    MB[61] = true; // Step time elapsed -> advance step
                }
            }

            // D. Proportional heating controller & heater stage shedding when Heat_Permitted (MB 62) is active
            int errDeciC = MI[52] - MI[100]; // Error in 0.1 °C
            if (MB[62])
            {
                MI[110] = (short)errDeciC;
                int heatPct = (errDeciC <= 0) ? 0 : Math.Min(100, Math.Max(10, errDeciC));
                MI[60] = (short)heatPct;

                // Keep stage 1 (O33) continuously ON when error >= 1.0 °C; shed stages O34..O38 as error decreases
                if (errDeciC >= 10) O[33] = true;
                if (errDeciC < 20) O[34] = false;
                if (errDeciC < 38) O[35] = false;
                if (errDeciC < 55) O[36] = false;
                if (errDeciC < 72) O[37] = false;
                if (errDeciC < 90) O[38] = false;
                if (errDeciC <= 0) O[33] = false;
            }
            else
            {
                MI[60] = 0;
            }

            // E. Manual / Service mode actuator mapping for HMI Page 45 (MB 90..104 or English MB 70..81) when process is stopped
            if (!MB[1] && !MB[30])
            {
                bool anyMan90 = false;
                for (int m = 90; m <= 104; m++)
                {
                    if (MB[m]) { anyMan90 = true; break; }
                }

                if (anyMan90)
                {
                    O[3] = MB[90] || MB[91];
                    O[4] = MB[92] || MB[93];
                    O[5] = MB[94] || MB[95];
                    O[6] = MB[102];
                    O[7] = MB[103];
                    bool anyHeater96 = false;
                    for (int s = 0; s < 6; s++)
                    {
                        O[33 + s] = MB[96 + s];
                        if (MB[96 + s]) anyHeater96 = true;
                    }
                    O[32] = MB[104] || anyHeater96;
                }
                else if (MB[40] && (Project == null || Project.Subroutines == null || !Project.Subroutines.ContainsKey(30)))
                {
                    for (int f = 0; f < 6; f++)
                    {
                        O[3 + f] = MB[70 + f];
                    }
                    bool anyHeaterMan = false;
                    for (int s = 0; s < 6; s++)
                    {
                        if (MB[76 + s])
                        {
                            O[33 + s] = true;
                            anyHeaterMan = true;
                        }
                    }
                    if (anyHeaterMan || MB[74]) O[32] = true;
                }
            }

            // F. Mirror heater section outputs O33..O38 to O9..O14 (for HMI Page 38 & Page 45 indicators)
            //    and drive signal tower lamps O0 (RUN), O1 (PAUSE), O2 (ALARM) & main 400V contactor O8
            int activeHeaterStages = 0;
            for (int s = 0; s < 6; s++)
            {
                O[9 + s] = O[33 + s];
                if (O[33 + s]) activeHeaterStages++;
            }
            O[0] = MB[1] && !MB[2];
            O[1] = MB[2];
            O[2] = MB[30] && !BuzzerSilenced;
            O[8] = O[32];

            // G. Thermal & humidity chamber physics simulation (while respecting manual slider/watch overrides)
            if (Math.Abs(MI[1] - (int)Math.Round(simTempDeciC)) > 10)
            {
                simTempDeciC = MI[1];
            }
            int expectedRawRh = (int)Math.Round(819.0 + (simRhDeciPct / 1000.0) * (4095.0 - 819.0));
            if (Math.Abs(MI[2] - expectedRawRh) > 25)
            {
                simRhDeciPct = Math.Max(0.0, Math.Min(1000.0, ((MI[2] - 819.0) * 1000.0) / (4095.0 - 819.0)));
            }

            double dtSec = Math.Max(0.01, elapsedMs / 1000.0);
            if (O[32] && activeHeaterStages > 0)
            {
                // Heating rate proportional to active heater stages (~2.5 deci-°C/stage/s) capped softly near target unless manual override
                double nextTemp = simTempDeciC + (activeHeaterStages * 2.5) * dtSec;
                if (MB[1] && MI[52] > 0 && simTempDeciC < MI[52] && nextTemp > MI[52])
                {
                    nextTemp = MI[52];
                }
                simTempDeciC = nextTemp;
            }
            else
            {
                // Natural cooling toward 22.0 °C ambient (faster when fans O3..O7 or recuperator O11 are running)
                double coolFactor = (O[3] || O[11]) ? 1.5 : 0.4;
                if (simTempDeciC > 220.0)
                {
                    simTempDeciC = Math.Max(220.0, simTempDeciC - coolFactor * dtSec);
                }
            }

            if (O[11] || (O[32] && activeHeaterStages > 0))
            {
                // Drying / recuperation reduces chamber humidity toward target RH (or 25% floor)
                double targetRh = Math.Max(250.0, (double)MI[53]);
                if (simRhDeciPct > targetRh)
                {
                    double dryRate = (O[11] ? 3.0 : 1.2);
                    simRhDeciPct = Math.Max(targetRh, simRhDeciPct - dryRate * dtSec);
                }
            }
            else if (simRhDeciPct < 450.0)
            {
                simRhDeciPct = Math.Min(450.0, simRhDeciPct + 0.3 * dtSec);
            }

            MI[1] = (short)Math.Round(simTempDeciC);
            MI[100] = MI[1];
            MI[2] = (short)Math.Round(819.0 + (simRhDeciPct / 1000.0) * (4095.0 - 819.0));
            MI[101] = (short)Math.Round(simRhDeciPct);

            // H. Sync VFD analog outputs (MI 120 % -> MI 61/62 & AO0/AO1 MI 280/281 0..4095)
            short vfdPct = (O[3] || O[6] || O[7] || MB[1]) ? (MI[120] > 0 ? MI[120] : MI[56]) : (short)0;
            MI[120] = vfdPct;
            MI[61] = vfdPct;
            MI[62] = vfdPct;
            short vfdDac = (short)Math.Max(0, Math.Min(4095, (vfdPct * 4095) / 100));
            MI[280] = vfdDac;
            MI[281] = vfdDac;
            MI[3] = (short)(vfdPct * 32); // Airflow in m3/h (e.g. 65% -> 2080 m3/h)

            // I. Sync HMI display variables (MI 0 = Temp °C, MI 20 = Recipe, MI 21 = Step, MI 23 / ML 33 = Remaining min, MI 31/32 = SP)
            MI[0] = (short)(MI[100] / 10);
            MI[20] = MI[51];
            MI[21] = (short)(MB[1] ? (MI[50] + 1) : MI[50]);
            int remSec = Math.Max(0, ML[50] - ML[51]);
            MI[23] = (short)((remSec + 59) / 60);
            ML[33] = MI[23];
            MI[31] = (short)(MI[52] / 10);
            MI[32] = (short)(MI[53] / 10);
            MI[91] = CurrentAccessLevel;

            // J. Sync HMI Page 42 Alarm status indicators (O15..O18 and MI 80 active alarm count / access level)
            if (!MB[30]) BuzzerSilenced = false;
            O[15] = MB[30] || MB[31];
            O[16] = MB[32] || MB[45];
            O[17] = MB[33] || MB[34] || MB[35];
            O[18] = MB[36] || MB[37] || MB[46] || MB[47] || MB[48];

            short almCount = 0;
            for (int a = 31; a <= 37; a++) if (MB[a]) almCount++;
            for (int a = 45; a <= 48; a++) if (MB[a]) almCount++;
            if (MB[30] && almCount == 0) almCount = 1;
            MI[80] = (almCount > 0) ? almCount : CurrentAccessLevel;

            // K. Handle F1..F4 screen navigation via MI 301 changes in 08_HMI_LOGIC
            if (MI[301] != prev_MI301)
            {
                prev_MI301 = MI[301];
                switch (MI[301])
                {
                    case 1: SI[9] = 1; break;
                    case 2: SI[9] = 36; break;
                    case 3: SI[9] = 40; break;
                    case 4: SI[9] = 41; break;
                    case 5: SI[9] = 42; break;
                    case 6: SI[9] = 45; break;
                }
            }
        }

        private void ExecuteLadderProgram()
        {
            if (Project == null || Project.Subroutines == null || Project.Subroutines.Count == 0) return;

            // In VisiLogic, Subroutine ID 2 (or the first subroutine in LdrFunctions) is the Main Routine,
            // which orchestrates execution of other subroutines via Call Subroutine (ElmntType 601).
            Subroutine mainSub = null;
            if (Project.Subroutines.ContainsKey(2))
            {
                mainSub = Project.Subroutines[2];
            }
            else
            {
                foreach (var s in Project.Subroutines.Values)
                {
                    mainSub = s;
                    break;
                }
            }

            bool mainCallsSubroutines = false;
            if (mainSub != null && mainSub.Rungs != null)
            {
                foreach (var r in mainSub.Rungs)
                {
                    foreach (var e in r.Elements)
                    {
                        if ((e.ElementType == 601 && (e.Operands.Count == 1 || e.MoreParamsLng > 0)) ||
                            (e.ElementType == 900 && e.Operands.Count < 2 && e.MoreParamsLng > 0))
                        {
                            mainCallsSubroutines = true;
                            break;
                        }
                    }
                    if (mainCallsSubroutines) break;
                }
            }

            if (mainSub != null && mainCallsSubroutines)
            {
                ExecuteSubroutine(mainSub);
            }
            else
            {
                foreach (var kvp in Project.Subroutines)
                {
                    ExecuteSubroutine(kvp.Value);
                }
            }
        }

        public void ExecuteSubroutine(Subroutine sub)
        {
            if (sub == null || sub.Rungs == null) return;

            foreach (var rung in sub.Rungs)
            {
                bool shouldReturn = ExecuteRung(rung);
                if (shouldReturn)
                {
                    return;
                }
            }
        }

        private static long PackNode(int x, int y)
        {
            return (((long)x) << 32) | ((uint)y);
        }

        private bool ExecuteRung(LadderRung rung)
        {
            if (rung.Elements == null || rung.Elements.Count == 0) return false;

            // Check whether this rung uses 2D VisiLogic grid coordinates / wire elements
            bool has2DCoords = false;
            foreach (var elm in rung.Elements)
            {
                if (elm.Xs != 0 || elm.Dx != 0 || elm.Dy != 0 || elm.ElementType == 100 || elm.ElementType == 101)
                {
                    has2DCoords = true;
                    break;
                }
            }

            if (!has2DCoords)
            {
                bool powerFlow = true;
                foreach (var elm in rung.Elements)
                {
                    if (IsConditionElement(elm.ElementType))
                    {
                        if (!EvaluateCondition(elm))
                        {
                            powerFlow = false;
                            break;
                        }
                    }
                }

                foreach (var elm in rung.Elements)
                {
                    if (elm.ElementType == 602)
                    {
                        if (powerFlow) return true;
                    }
                    else if (IsActionElement(elm.ElementType))
                    {
                        ExecuteAction(elm, powerFlow);
                    }
                }
                return false;
            }

            // 2D VisiLogic Ladder Net Evaluation:
            // Propagate power from the left power rail (X = 0) across horizontal wires (100),
            // vertical wires (101), contacts (200..203), comparisons (300..305), and series function blocks.
            var powered = new HashSet<long>();

            // Seed left power rail (X = 0) for all rows present in the rung
            foreach (var elm in rung.Elements)
            {
                powered.Add(PackNode(0, elm.Ys));
                if (elm.Dy > 0)
                {
                    for (int y = elm.Ys; y <= elm.Ys + elm.Dy; y++)
                    {
                        powered.Add(PackNode(0, y));
                    }
                }
            }

            // Evaluate condition elements once per rung scan so edge contacts ([P]/[N]) are deterministic
            var condResults = new Dictionary<int, bool>();
            foreach (var elm in rung.Elements)
            {
                if (IsConditionElement(elm.ElementType))
                {
                    condResults[elm.ElementId] = EvaluateCondition(elm);
                }
            }

            bool changed = true;
            int maxIterations = rung.Elements.Count * 4 + 8;
            int iter = 0;

            while (changed && iter < maxIterations)
            {
                changed = false;
                iter++;

                foreach (var elm in rung.Elements)
                {
                    int t = elm.ElementType;
                    if (t == 100) // Horizontal wire: (Xs, Ys) -> (Xs + Dx, Ys)
                    {
                        int span = Math.Max(1, elm.Dx);
                        if (powered.Contains(PackNode(elm.Xs, elm.Ys)))
                        {
                            for (int x = elm.Xs + 1; x <= elm.Xs + span; x++)
                            {
                                if (powered.Add(PackNode(x, elm.Ys))) changed = true;
                            }
                        }
                    }
                    else if (t == 101) // Vertical wire: connects all (Xs, y) for y in [Ys, Ys + Dy]
                    {
                        int spanY = Math.Max(1, elm.Dy);
                        bool anyPowered = false;
                        for (int y = elm.Ys; y <= elm.Ys + spanY; y++)
                        {
                            if (powered.Contains(PackNode(elm.Xs, y)))
                            {
                                anyPowered = true;
                                break;
                            }
                        }
                        if (anyPowered)
                        {
                            for (int y = elm.Ys; y <= elm.Ys + spanY; y++)
                            {
                                if (powered.Add(PackNode(elm.Xs, y))) changed = true;
                            }
                        }
                    }
                    else if (IsConditionElement(t))
                    {
                        bool condOk = false;
                        condResults.TryGetValue(elm.ElementId, out condOk);
                        if (condOk && powered.Contains(PackNode(elm.Xs, elm.Ys)))
                        {
                            int width = elm.Dx > 0 ? elm.Dx : ((t >= 300 && t <= 305) ? 13 : 6);
                            if (powered.Add(PackNode(elm.Xs + width, elm.Ys))) changed = true;
                        }
                    }
                    else if (IsActionElement(t) && t != 602)
                    {
                        // Pass power through series-connected function blocks / coils if another element follows on the right
                        if (powered.Contains(PackNode(elm.Xs, elm.Ys)))
                        {
                            int width = elm.Dx > 0 ? elm.Dx : 6;
                            if (powered.Add(PackNode(elm.Xs + width, elm.Ys))) changed = true;
                        }
                    }
                }
            }

            // Execute action elements in top-to-bottom, left-to-right order based on node power at (Xs, Ys)
            foreach (var elm in rung.Elements)
            {
                bool nodePowered = powered.Contains(PackNode(elm.Xs, elm.Ys));
                if (elm.ElementType == 602)
                {
                    // Conditional or end-of-subroutine Return (RET)
                    if (nodePowered && (elm.Xs > 0 || rung.IsReturnStrip))
                    {
                        return true;
                    }
                }
                else if (IsActionElement(elm.ElementType))
                {
                    ExecuteAction(elm, nodePowered);
                }
            }

            return false;
        }

        private bool IsConditionElement(int type)
        {
            // 200..203 contacts, 300..305 comparison blocks
            return (type >= 200 && type <= 203) || (type >= 300 && type <= 305);
        }

        private bool IsActionElement(int type)
        {
            // 204..212 coils, 400..407 math, 601 Call Sub / Store, 602 Return, 800 Load HMI Display,
            // 900 Store Direct, 1300 Linearize / Timer, 1500 Increment, 1501 Decrement
            return (type >= 204 && type <= 212) ||
                   (type >= 400 && type <= 407) ||
                   type == 601 || type == 602 || type == 800 ||
                   type == 900 || type == 1300 || type == 1500 || type == 1501;
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

                case 202: // Positive Transition Contact [P]
                    return GetBitValue(op) && !GetPrevBitValue(op);

                case 203: // Negative Transition Contact [N]
                    return !GetBitValue(op) && GetPrevBitValue(op);

                case 300: // Greater Than (A > B)
                    if (elm.Operands.Count >= 2)
                        return GetNumericValue(elm.Operands[0]) > GetNumericValue(elm.Operands[1]);
                    return false;

                case 301: // Greater Than or Equal (A >= B)
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

                case 304: // Less Than or Equal (A <= B)
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
            if (elm.Operands.Count == 0 && elm.ElementType != 601 && elm.ElementType != 900) return;

            switch (elm.ElementType)
            {
                case 204: // Direct Coil ( ) - also drives Timer (T) coils in VisiLogic
                    SetBitValue(elm.Operands[0], powerFlow);
                    break;

                case 205: // Inverted Coil (/)
                    SetBitValue(elm.Operands[0], !powerFlow);
                    break;

                case 206: // Set Coil (S)
                    if (powerFlow) SetBitValue(elm.Operands[0], true);
                    break;

                case 207: // Reset Coil (R) - also resets Timers (T)
                    if (powerFlow) SetBitValue(elm.Operands[0], false);
                    break;

                case 212: // Toggle Coil
                    if (powerFlow)
                    {
                        bool cur = GetBitValue(elm.Operands[0]);
                        SetBitValue(elm.Operands[0], !cur);
                    }
                    break;

                case 601: // Call Subroutine (CS operand or MoreParamsLng) or legacy Store
                    if (powerFlow)
                    {
                        if (elm.Operands.Count == 1)
                        {
                            int subId = elm.Operands[0].Address != 0 ? elm.Operands[0].Address : elm.Operands[0].ConstValue;
                            if (Project != null && Project.Subroutines.ContainsKey(subId))
                            {
                                ExecuteSubroutine(Project.Subroutines[subId]);
                            }
                        }
                        else if (elm.Operands.Count >= 2)
                        {
                            int srcVal = GetNumericValue(elm.Operands[0]);
                            SetNumericValue(elm.Operands[1], srcVal);
                        }
                        else if (elm.MoreParamsLng > 0 && Project != null && Project.Subroutines.ContainsKey(elm.MoreParamsLng))
                        {
                            ExecuteSubroutine(Project.Subroutines[elm.MoreParamsLng]);
                        }
                    }
                    break;

                case 800: // Load HMI Display (JHP)
                    if (powerFlow && elm.Operands.Count >= 1)
                    {
                        int pageId = GetNumericValue(elm.Operands[0]);
                        if (pageId <= 0) pageId = elm.Operands[0].Address;
                        if (pageId > 0) SI[9] = (short)pageId;
                    }
                    break;

                case 900: // Store Direct (MOVE: Op[0] -> Op[1]) or legacy Call Subroutine
                    if (powerFlow)
                    {
                        if (elm.Operands.Count >= 2)
                        {
                            int srcVal = GetNumericValue(elm.Operands[0]);
                            SetNumericValue(elm.Operands[1], srcVal);
                        }
                        else if (elm.MoreParamsLng > 0 && Project != null && Project.Subroutines.ContainsKey(elm.MoreParamsLng))
                        {
                            ExecuteSubroutine(Project.Subroutines[elm.MoreParamsLng]);
                        }
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

                case 407: // MOD: A % B -> Dest
                    if (powerFlow && elm.Operands.Count >= 3)
                    {
                        int b = GetNumericValue(elm.Operands[1]);
                        if (b != 0)
                        {
                            int res = GetNumericValue(elm.Operands[0]) % b;
                            SetNumericValue(elm.Operands[2], res);
                        }
                    }
                    break;

                case 1300: // Linearize (LIN: X1, Y1, X2, Y2, X_in -> Y_out) or Timer Block
                    if (elm.Operands.Count >= 6)
                    {
                        if (powerFlow)
                        {
                            long x1 = GetNumericValue(elm.Operands[0]);
                            long y1 = GetNumericValue(elm.Operands[1]);
                            long x2 = GetNumericValue(elm.Operands[2]);
                            long y2 = GetNumericValue(elm.Operands[3]);
                            long xVal = GetNumericValue(elm.Operands[4]);
                            if (x2 != x1)
                            {
                                long yVal = y1 + ((xVal - x1) * (y2 - y1)) / (x2 - x1);
                                SetNumericValue(elm.Operands[5], (int)yVal);
                            }
                        }
                    }
                    else if (elm.Operands.Count >= 1)
                    {
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
                                tmr.PresetMs = GetNumericValue(elm.Operands[1]) * 10; // 10ms resolution units
                            }
                        }
                    }
                    break;

                case 1500: // INC: Increment (+1)
                    if (powerFlow && elm.Operands.Count >= 1)
                    {
                        SetNumericValue(elm.Operands[0], GetNumericValue(elm.Operands[0]) + 1);
                    }
                    break;

                case 1501: // DEC: Decrement (-1)
                    if (powerFlow && elm.Operands.Count >= 1)
                    {
                        SetNumericValue(elm.Operands[0], GetNumericValue(elm.Operands[0]) - 1);
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
            else if (t == "T" && addr >= 0 && addr < T.Length)
            {
                T[addr].Run = val;
                if (!val)
                {
                    T[addr].CurrentMs = 0;
                    T[addr].Done = false;
                }
            }
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
