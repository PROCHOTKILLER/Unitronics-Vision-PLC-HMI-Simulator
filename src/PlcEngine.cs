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
        public short CurrentAccessLevel = 0;
        public bool BuzzerSilenced = false;

        public event Action OnScanCompleted;
        public event Action<string> OnLogMessage;

        public HardwareProfile Profile { get; set; }
        public PlcMemoryStorage Memory { get; private set; }
        public ProjectSimulationProfile SimProfile { get; set; }

        public PlcEngine(HardwareProfile profile = null)
        {
            Profile = profile ?? (SimulatorConfig.Current != null && SimulatorConfig.Current.Profile != null ? SimulatorConfig.Current.Profile : HardwareProfile.CreateDefaultProfile());
            Memory = new PlcMemoryStorage();
            SimProfile = new ProjectSimulationProfile();
            SimProfile.ResetToNeutral(Profile, null);
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

        public void ResetMemory()
        {
            // 1. Always clear all physical I/O and internal memory to 100% neutral position (0 / false)
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

            // 2. Apply timer presets and power-up values defined directly in the loaded project file
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
            }

            // 3. Only apply artificial input states & initial analog values if user explicitly enabled simulation
            if (SimProfile != null && SimProfile.IsSimulationEnabled)
            {
                ApplySimulationInitialStates();
            }

            CurrentAccessLevel = 0;
            BuzzerSilenced = false;
            Array.Copy(I, prev_I, I.Length);

            // 4. Default Unitronics Vision OS system operand values:
            // SB 0 = Always 0, SB 1 = Always 1, SB 2 = Power-Up bit (1 for first scan),
            // SB 3 = 1s pulse, SB 7 = 100ms pulse, SB 8 = Battery OK
            SB[0] = false;
            SB[1] = true;
            SB[2] = true;
            SB[7] = true;
            SB[8] = true;
            SI[9] = 1;      // Default HMI page (Start-Up Display ID = 1)
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

        public void ApplySimulationInitialStates()
        {
            if (SimProfile == null || !SimProfile.IsSimulationEnabled) return;

            if (SimProfile.InputRules != null)
            {
                foreach (var kvp in SimProfile.InputRules)
                {
                    DigitalInputRule rule = kvp.Value;
                    if (rule == null || rule.Address < 0 || rule.Address >= I.Length) continue;
                    rule.RuntimeTimerMs = 0;
                    rule.RuntimeInitialized = true;
                    if (rule.Role == DigitalInputRole.Forced_Constant)
                    {
                        I[rule.Address] = rule.ForcedState;
                    }
                    else if (rule.Role == DigitalInputRole.Output_Feedback)
                    {
                        int outIdx = rule.LinkedOutputAddress;
                        bool outState = (outIdx >= 0 && outIdx < O.Length) && O[outIdx];
                        I[rule.Address] = rule.InvertFeedback ? !outState : outState;
                    }
                    else
                    {
                        I[rule.Address] = rule.ForcedState;
                    }
                }
            }

            if (SimProfile.AnalogRules != null)
            {
                foreach (var kvp in SimProfile.AnalogRules)
                {
                    AnalogInputRule aRule = kvp.Value;
                    if (aRule == null) continue;
                    aRule.CurrentPreciseValue = (aRule.Role == AnalogInputRole.Neutral_Zero) ? 0.0 : (double)aRule.InitialValue;
                    aRule.RuntimeInitialized = true;
                    if (aRule.TargetMiAddress >= 0 && aRule.TargetMiAddress < MI.Length)
                    {
                        MI[aRule.TargetMiAddress] = (short)Math.Round(aRule.CurrentPreciseValue);
                    }
                }
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

            accumMs1s += elapsedMs;
            accumMs100ms += elapsedMs;
            // Update Unitronics Vision system clock bits: SB 3 (1s pulse) and SB 7 (100ms pulse)
            if (clock1s.ElapsedMilliseconds >= 500 || accumMs1s >= 500)
            {
                SB[3] = !SB[3];
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

            // 2. Execute loaded ladder program
            ExecuteLadderProgram();

            // 3. Execute universal user-configured I/O & process simulation rules (only when enabled)
            if (SimProfile != null && SimProfile.IsSimulationEnabled)
            {
                UpdateUniversalIoSimulation(elapsedMs);
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
                int val110 = (110 + ao < MI.Length) ? MI[110 + ao] : 0;
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

        private void UpdateUniversalIoSimulation(int elapsedMs)
        {
            if (SimProfile == null || !SimProfile.IsSimulationEnabled) return;

            // A. Digital Input Rules (Forced_Constant, Output_Feedback, Pulse_Oscillator)
            if (SimProfile.InputRules != null)
            {
                foreach (var kvp in SimProfile.InputRules)
                {
                    DigitalInputRule rule = kvp.Value;
                    if (rule == null || rule.Address < 0 || rule.Address >= I.Length) continue;

                    if (rule.Role == DigitalInputRole.Forced_Constant)
                    {
                        I[rule.Address] = rule.ForcedState;
                    }
                    else if (rule.Role == DigitalInputRole.Output_Feedback)
                    {
                        int outIdx = rule.LinkedOutputAddress;
                        if (outIdx >= 0 && outIdx < O.Length)
                        {
                            bool outActive = O[outIdx];
                            bool targetState = rule.InvertFeedback ? !outActive : outActive;
                            if (I[rule.Address] != targetState)
                            {
                                rule.RuntimeTimerMs += elapsedMs;
                                if (rule.RuntimeTimerMs >= Math.Max(0, rule.DelayMs))
                                {
                                    I[rule.Address] = targetState;
                                    rule.RuntimeTimerMs = 0;
                                }
                            }
                            else
                            {
                                rule.RuntimeTimerMs = 0;
                            }
                        }
                    }
                    else if (rule.Role == DigitalInputRole.Pulse_Oscillator)
                    {
                        int halfPeriodMs = Math.Max(25, rule.DelayMs);
                        rule.RuntimeTimerMs += elapsedMs;
                        if (rule.RuntimeTimerMs >= halfPeriodMs)
                        {
                            I[rule.Address] = !I[rule.Address];
                            rule.RuntimeTimerMs = 0;
                        }
                    }
                }
            }

            // B. Analog Input / Process Simulation Rules
            double dtSec = Math.Max(0.01, elapsedMs / 1000.0);
            if (SimProfile.AnalogRules != null)
            {
                foreach (var kvp in SimProfile.AnalogRules)
                {
                    AnalogInputRule aRule = kvp.Value;
                    if (aRule == null) continue;
                    int miAddr = aRule.TargetMiAddress;
                    if (miAddr < 0 || miAddr >= MI.Length) continue;

                    if (!aRule.RuntimeInitialized)
                    {
                        aRule.CurrentPreciseValue = (aRule.Role == AnalogInputRole.Neutral_Zero) ? 0.0 : (double)aRule.InitialValue;
                        aRule.RuntimeInitialized = true;
                        MI[miAddr] = (short)Math.Round(aRule.CurrentPreciseValue);
                    }

                    if (aRule.Role == AnalogInputRole.Forced_Constant)
                    {
                        aRule.CurrentPreciseValue = aRule.InitialValue;
                        MI[miAddr] = aRule.InitialValue;
                        continue;
                    }

                    if (aRule.Role == AnalogInputRole.Process_Temperature ||
                        aRule.Role == AnalogInputRole.Process_Humidity ||
                        aRule.Role == AnalogInputRole.Process_Generic)
                    {
                        // Respect manual changes made via HardwarePanel slider or Watch Table
                        if (Math.Abs(MI[miAddr] - (int)Math.Round(aRule.CurrentPreciseValue)) > 5)
                        {
                            aRule.CurrentPreciseValue = MI[miAddr];
                        }

                        var incOuts = aRule.GetIncreasingOutputs();
                        var decOuts = aRule.GetDecreasingOutputs();

                        int activeIncCount = 0;
                        foreach (int oAddr in incOuts)
                        {
                            if (oAddr >= 0 && oAddr < O.Length && O[oAddr]) activeIncCount++;
                        }

                        int activeDecCount = 0;
                        foreach (int oAddr in decOuts)
                        {
                            if (oAddr >= 0 && oAddr < O.Length && O[oAddr]) activeDecCount++;
                        }

                        double deltaPerSec = 0.0;
                        if (activeIncCount > 0) deltaPerSec += aRule.IncreaseRatePerSec * activeIncCount;
                        if (activeDecCount > 0) deltaPerSec -= aRule.DecreaseRatePerSec * activeDecCount;

                        if (Math.Abs(deltaPerSec) > 0.0001)
                        {
                            aRule.CurrentPreciseValue += deltaPerSec * dtSec;
                        }
                        else if (aRule.AmbientDriftRatePerSec > 0.0)
                        {
                            if (aRule.CurrentPreciseValue > aRule.AmbientValue)
                            {
                                aRule.CurrentPreciseValue = Math.Max((double)aRule.AmbientValue, aRule.CurrentPreciseValue - aRule.AmbientDriftRatePerSec * dtSec);
                            }
                            else if (aRule.CurrentPreciseValue < aRule.AmbientValue)
                            {
                                aRule.CurrentPreciseValue = Math.Min((double)aRule.AmbientValue, aRule.CurrentPreciseValue + aRule.AmbientDriftRatePerSec * dtSec);
                            }
                        }

                        aRule.CurrentPreciseValue = Math.Max((double)aRule.MinValue, Math.Min((double)aRule.MaxValue, aRule.CurrentPreciseValue));
                        MI[miAddr] = (short)Math.Round(aRule.CurrentPreciseValue);
                    }
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
