using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UnitronicsV350Simulator
{
    public enum DigitalInputRole
    {
        Neutral_Manual = 0,
        Forced_Constant = 1,
        Output_Feedback = 2,
        Pulse_Oscillator = 3
    }

    public enum DigitalOutputRole
    {
        General = 0,
        Contactor_Relay = 1,
        Heater_Thermal = 2,
        Cooler_Fan = 3,
        Valve_Humidifier = 4,
        Signal_Alarm = 5
    }

    public enum AnalogInputRole
    {
        Neutral_Zero = 0,
        Forced_Constant = 1,
        Process_Temperature = 2,
        Process_Humidity = 3,
        Process_Generic = 4
    }

    public class DigitalInputRule
    {
        public int Address;
        public DigitalInputRole Role = DigitalInputRole.Neutral_Manual;
        public bool ForcedState = false;
        public int LinkedOutputAddress = -1;
        public bool InvertFeedback = false;
        public int DelayMs = 100;

        // Runtime tracking for delayed feedback or pulse oscillator
        public double RuntimeTimerMs = 0;
        public bool RuntimePendingState = false;
        public bool RuntimeInitialized = false;

        public DigitalInputRule Clone()
        {
            return new DigitalInputRule
            {
                Address = this.Address,
                Role = this.Role,
                ForcedState = this.ForcedState,
                LinkedOutputAddress = this.LinkedOutputAddress,
                InvertFeedback = this.InvertFeedback,
                DelayMs = this.DelayMs
            };
        }
    }

    public class DigitalOutputRule
    {
        public int Address;
        public DigitalOutputRole Role = DigitalOutputRole.General;

        public DigitalOutputRule Clone()
        {
            return new DigitalOutputRule
            {
                Address = this.Address,
                Role = this.Role
            };
        }
    }

    public class AnalogInputRule
    {
        public int ChannelIndex;
        public int TargetMiAddress = 100;
        public AnalogInputRole Role = AnalogInputRole.Neutral_Zero;
        public string Unit = "";
        public short InitialValue = 0;
        public short AmbientValue = 0;
        public short MinValue = 0;
        public short MaxValue = 4095;
        public string IncreasingOutputsSpec = "";
        public double IncreaseRatePerSec = 2.0;
        public string DecreasingOutputsSpec = "";
        public double DecreaseRatePerSec = 1.0;
        public double AmbientDriftRatePerSec = 0.2;

        // Runtime state
        public double CurrentPreciseValue = 0.0;
        public bool RuntimeInitialized = false;

        public AnalogInputRule Clone()
        {
            return new AnalogInputRule
            {
                ChannelIndex = this.ChannelIndex,
                TargetMiAddress = this.TargetMiAddress,
                Role = this.Role,
                Unit = this.Unit,
                InitialValue = this.InitialValue,
                AmbientValue = this.AmbientValue,
                MinValue = this.MinValue,
                MaxValue = this.MaxValue,
                IncreasingOutputsSpec = this.IncreasingOutputsSpec,
                IncreaseRatePerSec = this.IncreaseRatePerSec,
                DecreasingOutputsSpec = this.DecreasingOutputsSpec,
                DecreaseRatePerSec = this.DecreaseRatePerSec,
                AmbientDriftRatePerSec = this.AmbientDriftRatePerSec,
                CurrentPreciseValue = this.CurrentPreciseValue,
                RuntimeInitialized = this.RuntimeInitialized
            };
        }

        public List<int> GetIncreasingOutputs()
        {
            return ParseOutputSpec(IncreasingOutputsSpec);
        }

        public List<int> GetDecreasingOutputs()
        {
            return ParseOutputSpec(DecreasingOutputsSpec);
        }

        public static List<int> ParseOutputSpec(string spec)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(spec)) return result;

            string[] parts = spec.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim().ToUpperInvariant();
                if (part.StartsWith("O")) part = part.Substring(1);

                int dashIdx = part.IndexOf('-');
                if (dashIdx < 0) dashIdx = part.IndexOf("..", StringComparison.Ordinal);

                if (dashIdx > 0)
                {
                    string leftStr = part.Substring(0, dashIdx).Trim().TrimStart('O');
                    string rightStr = part.Substring(dashIdx + (part.Contains("..") ? 2 : 1)).Trim().TrimStart('O');
                    int startAddr, endAddr;
                    if (int.TryParse(leftStr, out startAddr) && int.TryParse(rightStr, out endAddr))
                    {
                        if (startAddr > endAddr)
                        {
                            int tmp = startAddr;
                            startAddr = endAddr;
                            endAddr = tmp;
                        }
                        for (int a = startAddr; a <= endAddr && a < 128; a++)
                        {
                            if (a >= 0 && !result.Contains(a)) result.Add(a);
                        }
                    }
                }
                else
                {
                    int singleAddr;
                    if (int.TryParse(part, out singleAddr) && singleAddr >= 0 && singleAddr < 128)
                    {
                        if (!result.Contains(singleAddr)) result.Add(singleAddr);
                    }
                }
            }
            return result;
        }
    }

    public class ProjectSimulationProfile
    {
        public bool IsSimulationEnabled = false;
        public bool HasUserConfigured = false;

        public Dictionary<int, DigitalInputRule> InputRules = new Dictionary<int, DigitalInputRule>();
        public Dictionary<int, DigitalOutputRule> OutputRules = new Dictionary<int, DigitalOutputRule>();
        public Dictionary<int, AnalogInputRule> AnalogRules = new Dictionary<int, AnalogInputRule>();

        public ProjectSimulationProfile()
        {
            ResetToNeutral(null, null);
        }

        /// <summary>
        /// Resets the entire simulation profile to a 100% neutral, unconfigured state.
        /// All digital inputs are Manual NO (0 / false), all outputs are General, and all analog inputs are Neutral (0).
        /// </summary>
        public void ResetToNeutral(HardwareProfile hw, LoadedProject proj)
        {
            IsSimulationEnabled = false;
            InputRules.Clear();
            OutputRules.Clear();
            AnalogRules.Clear();

            List<int> diAddresses = GetActiveDigitalInputAddresses(hw, proj);
            foreach (int addr in diAddresses)
            {
                InputRules[addr] = new DigitalInputRule
                {
                    Address = addr,
                    Role = DigitalInputRole.Neutral_Manual,
                    ForcedState = false,
                    LinkedOutputAddress = -1,
                    InvertFeedback = false,
                    DelayMs = 100
                };
            }

            List<int> doAddresses = GetActiveDigitalOutputAddresses(hw, proj);
            foreach (int addr in doAddresses)
            {
                OutputRules[addr] = new DigitalOutputRule
                {
                    Address = addr,
                    Role = DigitalOutputRole.General
                };
            }

            int totalAi = (hw != null) ? Math.Max(2, hw.GetTotalAi()) : 4;
            List<int> discoveredAnalogMis = DiscoverProjectAnalogMiRegisters(proj, totalAi);
            int channelCount = Math.Max(totalAi, discoveredAnalogMis.Count);

            for (int ch = 0; ch < channelCount; ch++)
            {
                int targetMi = (ch < discoveredAnalogMis.Count) ? discoveredAnalogMis[ch] : (100 + ch);
                AnalogRules[ch] = new AnalogInputRule
                {
                    ChannelIndex = ch,
                    TargetMiAddress = targetMi,
                    Role = AnalogInputRole.Neutral_Zero,
                    Unit = "",
                    InitialValue = 0,
                    AmbientValue = 0,
                    MinValue = 0,
                    MaxValue = 4095,
                    IncreasingOutputsSpec = "",
                    IncreaseRatePerSec = 0.0,
                    DecreasingOutputsSpec = "",
                    DecreaseRatePerSec = 0.0,
                    AmbientDriftRatePerSec = 0.0,
                    CurrentPreciseValue = 0.0,
                    RuntimeInitialized = false
                };
            }
        }

        /// <summary>
        /// Analyzes operand symbols/descriptions in the loaded VisiLogic project to suggest roles,
        /// initial NC states, contactor feedbacks, and analog process couplings.
        /// Never called automatically without the user's explicit request in the setup dialog.
        /// </summary>
        public void AutoSuggestFromProject(LoadedProject proj, HardwareProfile hw)
        {
            ResetToNeutral(hw, proj);
            if (proj == null || proj.OperandDescriptions == null) return;

            var heaterOutputs = new List<int>();
            var coolerOutputs = new List<int>();
            var humidifierOutputs = new List<int>();
            var contactorOutputs = new List<int>();

            // 1. Classify Digital Outputs (O) from operand descriptions
            foreach (var kvp in OutputRules)
            {
                int outAddr = kvp.Key;
                var rule = kvp.Value;
                string desc = GetOperandDescription(proj, "O" + outAddr).ToLowerInvariant();
                if (string.IsNullOrEmpty(desc)) continue;

                if (desc.Contains("stycznik") || desc.Contains("przekaznik") || desc.Contains("relay") || desc.Contains("contactor") || desc.Contains("_k1") || desc.Contains("_k2"))
                {
                    rule.Role = DigitalOutputRole.Contactor_Relay;
                    contactorOutputs.Add(outAddr);
                    if (desc.Contains("grzal") || desc.Contains("heat"))
                    {
                        heaterOutputs.Add(outAddr);
                    }
                }
                else if (desc.Contains("grzal") || desc.Contains("ssr") || desc.Contains("heat") || desc.Contains("nagrzew") || desc.Contains("palnik"))
                {
                    rule.Role = DigitalOutputRole.Heater_Thermal;
                    heaterOutputs.Add(outAddr);
                }
                else if (desc.Contains("went") || desc.Contains("chlod") || desc.Contains("wyciag") || desc.Contains("nawiew") || desc.Contains("fan") || desc.Contains("cool") || desc.Contains("rekuper"))
                {
                    rule.Role = DigitalOutputRole.Cooler_Fan;
                    coolerOutputs.Add(outAddr);
                }
                else if (desc.Contains("nawilz") || desc.Contains("zawor") || desc.Contains("przepust") || desc.Contains("humid") || desc.Contains("valve") || desc.Contains("damper") || desc.Contains("pomp"))
                {
                    rule.Role = DigitalOutputRole.Valve_Humidifier;
                    humidifierOutputs.Add(outAddr);
                }
                else if (desc.Contains("syren") || desc.Contains("alarm") || desc.Contains("lamp") || desc.Contains("buczek") || desc.Contains("sygna") || desc.Contains("horn"))
                {
                    rule.Role = DigitalOutputRole.Signal_Alarm;
                }
            }

            // 2. Classify Digital Inputs (I) from operand descriptions
            foreach (var kvp in InputRules)
            {
                int inAddr = kvp.Key;
                var rule = kvp.Value;
                string rawDesc = GetOperandDescription(proj, "I" + inAddr);
                string desc = rawDesc.ToLowerInvariant();
                if (string.IsNullOrEmpty(desc)) continue;

                // Check if this input is a feedback contact from an output (e.g., contactor / damper confirmation)
                if (desc.Contains("potw") || desc.Contains("feedback") || desc.Contains("zwrot") || desc.Contains("aux") ||
                    (desc.Contains("stycznik") && !desc.EndsWith("_nc")) || desc.Contains("przepustnic"))
                {
                    int matchedOut = FindMatchingOutputForInput(inAddr, desc, proj, OutputRules);
                    if (matchedOut >= 0)
                    {
                        rule.Role = DigitalInputRole.Output_Feedback;
                        rule.LinkedOutputAddress = matchedOut;
                        rule.InvertFeedback = false;
                        rule.DelayMs = 150;
                        continue;
                    }
                }

                // Check if this input is a Normally-Closed (NC) or healthy safety loop input
                if (desc.EndsWith("_nc") || desc.Contains("_nc_") || desc.Contains(" nc") ||
                    desc.Contains("termik") || desc.Contains("termostat") || desc.Contains("bezpiecz") ||
                    desc.Contains("stop") || desc.Contains("estop") || desc.Contains("drzwi") ||
                    desc.Contains("presostat") || desc.Contains("gotowosc") || desc.Contains("poprawne_zasilanie") ||
                    desc.Contains("zanik") || desc.Contains("fazy") || desc.Contains("overload") || desc.Contains("safety"))
                {
                    // Avoid forcing inputs whose description explicitly says _NO
                    if (!desc.EndsWith("_no"))
                    {
                        rule.Role = DigitalInputRole.Forced_Constant;
                        rule.ForcedState = true;
                        continue;
                    }
                }
            }

            // 3. Classify Analog Inputs (AI / MI) from operand descriptions
            string heaterSpec = FormatAddressListCompact(heaterOutputs);
            string coolerSpec = FormatAddressListCompact(coolerOutputs);
            string humidSpec = FormatAddressListCompact(humidifierOutputs);

            foreach (var kvp in AnalogRules)
            {
                var rule = kvp.Value;
                string miDesc = GetOperandDescription(proj, "MI" + rule.TargetMiAddress);
                string anDesc = GetOperandDescription(proj, "AN" + rule.ChannelIndex);
                string combined = (miDesc + " " + anDesc).ToLowerInvariant();

                if (combined.Contains("temp") || combined.Contains("pt100") || combined.Contains("tc") || combined.Contains("ciepl"))
                {
                    rule.Role = AnalogInputRole.Process_Temperature;
                    rule.Unit = "0.1 °C";
                    rule.InitialValue = 220;
                    rule.AmbientValue = 220;
                    rule.MinValue = -200;
                    rule.MaxValue = 1500;
                    rule.IncreasingOutputsSpec = heaterSpec;
                    rule.IncreaseRatePerSec = 2.0;
                    rule.DecreasingOutputsSpec = coolerSpec;
                    rule.DecreaseRatePerSec = 1.0;
                    rule.AmbientDriftRatePerSec = 0.25;
                    rule.CurrentPreciseValue = rule.InitialValue;
                }
                else if (combined.Contains("wilg") || combined.Contains("rh") || combined.Contains("humid"))
                {
                    rule.Role = AnalogInputRole.Process_Humidity;
                    // Check if raw 4-20mA register or scaled 0.1%RH register
                    bool isRaw420 = (rule.TargetMiAddress < 100 && !combined.Contains("0.1"));
                    if (isRaw420)
                    {
                        rule.Unit = "raw (4-20mA)";
                        rule.InitialValue = 2293; // ~45% RH in 819..4095
                        rule.AmbientValue = 2293;
                        rule.MinValue = 819;
                        rule.MaxValue = 4095;
                        rule.IncreaseRatePerSec = 8.0;
                        rule.DecreaseRatePerSec = 6.0;
                        rule.AmbientDriftRatePerSec = 1.0;
                    }
                    else
                    {
                        rule.Unit = "0.1 %RH";
                        rule.InitialValue = 450;
                        rule.AmbientValue = 450;
                        rule.MinValue = 0;
                        rule.MaxValue = 1000;
                        rule.IncreaseRatePerSec = 2.0;
                        rule.DecreaseRatePerSec = 1.5;
                        rule.AmbientDriftRatePerSec = 0.25;
                    }
                    rule.IncreasingOutputsSpec = humidSpec;
                    rule.DecreasingOutputsSpec = string.IsNullOrEmpty(coolerSpec) ? heaterSpec : coolerSpec;
                    rule.CurrentPreciseValue = rule.InitialValue;
                }
                else if (combined.Contains("przeplyw") || combined.Contains("cisn") || combined.Contains("poziom") || combined.Contains("flow") || combined.Contains("press"))
                {
                    rule.Role = AnalogInputRole.Process_Generic;
                    rule.Unit = "jednostki";
                    rule.InitialValue = 0;
                    rule.AmbientValue = 0;
                    rule.MinValue = 0;
                    rule.MaxValue = 4095;
                    rule.IncreasingOutputsSpec = coolerSpec;
                    rule.IncreaseRatePerSec = 50.0;
                    rule.DecreasingOutputsSpec = "";
                    rule.DecreaseRatePerSec = 50.0;
                    rule.AmbientDriftRatePerSec = 20.0;
                    rule.CurrentPreciseValue = rule.InitialValue;
                }
            }
        }

        private static int FindMatchingOutputForInput(int inAddr, string inDescLower, LoadedProject proj, Dictionary<int, DigitalOutputRule> outputRules)
        {
            // Direct index match if O[inAddr] exists and shares keywords
            string directOutDesc = GetOperandDescription(proj, "O" + inAddr).ToLowerInvariant();
            if (!string.IsNullOrEmpty(directOutDesc))
            {
                if ((inDescLower.Contains("przepust") && directOutDesc.Contains("przepust")) ||
                    (inDescLower.Contains("stycznik") && directOutDesc.Contains("stycznik")) ||
                    (inDescLower.Contains("went") && directOutDesc.Contains("went")))
                {
                    return inAddr;
                }
            }

            // Keyword match across all outputs
            foreach (var kvp in outputRules)
            {
                int outAddr = kvp.Key;
                string outDesc = GetOperandDescription(proj, "O" + outAddr).ToLowerInvariant();
                if (string.IsNullOrEmpty(outDesc)) continue;

                if (inDescLower.Contains("grzal") && outDesc.Contains("grzal")) return outAddr;
                if (inDescLower.Contains("przepust") && outDesc.Contains("przepust")) return outAddr;
                if (inDescLower.Contains("went_1") && outDesc.Contains("went") && outDesc.Contains("1")) return outAddr;
                if (inDescLower.Contains("went_2") && outDesc.Contains("went") && outDesc.Contains("2")) return outAddr;
                if (inDescLower.Contains("stycznik") && outDesc.Contains("stycznik")) return outAddr;
            }

            // Fallback: if there is any Contactor_Relay output, return the first one
            foreach (var kvp in outputRules)
            {
                if (kvp.Value.Role == DigitalOutputRole.Contactor_Relay)
                    return kvp.Key;
            }

            return -1;
        }

        private static string FormatAddressListCompact(List<int> addrs)
        {
            if (addrs == null || addrs.Count == 0) return "";
            addrs.Sort();
            var parts = new List<string>();
            int i = 0;
            while (i < addrs.Count)
            {
                int start = addrs[i];
                int end = start;
                while (i + 1 < addrs.Count && addrs[i + 1] == end + 1)
                {
                    end = addrs[i + 1];
                    i++;
                }
                if (end > start + 1)
                    parts.Add(string.Format("O{0}-O{1}", start, end));
                else if (end == start + 1)
                {
                    parts.Add("O" + start);
                    parts.Add("O" + end);
                }
                else
                    parts.Add("O" + start);
                i++;
            }
            return string.Join(", ", parts.ToArray());
        }

        public static string GetOperandDescription(LoadedProject proj, string key)
        {
            if (proj == null || proj.OperandDescriptions == null) return "";
            string desc;
            if (proj.OperandDescriptions.TryGetValue(key, out desc) && !string.IsNullOrEmpty(desc))
                return desc;
            return "";
        }

        public static List<int> GetActiveDigitalInputAddresses(HardwareProfile hw, LoadedProject proj)
        {
            var list = new List<int>();
            int cpuDi = 20;
            if (hw != null)
            {
                var modelSpec = hw.GetCurrentModelSpec();
                var snapSpec = hw.GetCurrentSnapInSpec();
                cpuDi = modelSpec.IsModular ? snapSpec.Di : modelSpec.OnboardDi;
            }
            for (int i = 0; i < cpuDi; i++) list.Add(i);

            if (hw != null && hw.EnableExpansionBus && hw.ExpansionSlots != null)
            {
                int cur = cpuDi;
                for (int s = 0; s < hw.ExpansionSlots.Count; s++)
                {
                    var expSpec = hw.GetExpansionSpec(hw.ExpansionSlots[s].ModuleName);
                    if (expSpec.Di > 0)
                    {
                        int start = Math.Max(cur, 32 + s * 16);
                        for (int j = 0; j < expSpec.Di; j++)
                        {
                            int a = start + j;
                            if (!list.Contains(a)) list.Add(a);
                        }
                        cur = start + expSpec.Di;
                    }
                }
            }

            if (proj != null && proj.OperandDescriptions != null)
            {
                foreach (var key in proj.OperandDescriptions.Keys)
                {
                    int addr;
                    if (key.StartsWith("I") && int.TryParse(key.Substring(1), out addr) && addr >= 0 && addr < 128)
                    {
                        if (!list.Contains(addr)) list.Add(addr);
                    }
                }
            }

            list.Sort();
            return list;
        }

        public static List<int> GetActiveDigitalOutputAddresses(HardwareProfile hw, LoadedProject proj)
        {
            var list = new List<int>();
            int cpuDo = 12;
            if (hw != null)
            {
                var modelSpec = hw.GetCurrentModelSpec();
                var snapSpec = hw.GetCurrentSnapInSpec();
                cpuDo = modelSpec.IsModular ? snapSpec.TotalDo : modelSpec.TotalOnboardDo;
            }
            for (int i = 0; i < cpuDo; i++) list.Add(i);

            if (hw != null && hw.EnableExpansionBus && hw.ExpansionSlots != null)
            {
                int cur = cpuDo;
                for (int s = 0; s < hw.ExpansionSlots.Count; s++)
                {
                    var expSpec = hw.GetExpansionSpec(hw.ExpansionSlots[s].ModuleName);
                    if (expSpec.TotalDo > 0)
                    {
                        int start = Math.Max(cur, 32 + s * 16);
                        for (int j = 0; j < expSpec.TotalDo; j++)
                        {
                            int a = start + j;
                            if (!list.Contains(a)) list.Add(a);
                        }
                        cur = start + expSpec.TotalDo;
                    }
                }
            }

            if (proj != null && proj.OperandDescriptions != null)
            {
                foreach (var key in proj.OperandDescriptions.Keys)
                {
                    int addr;
                    if (key.StartsWith("O") && int.TryParse(key.Substring(1), out addr) && addr >= 0 && addr < 128)
                    {
                        if (!list.Contains(addr)) list.Add(addr);
                    }
                }
            }

            list.Sort();
            return list;
        }

        public static List<int> DiscoverProjectAnalogMiRegisters(LoadedProject proj, int minChannels)
        {
            var list = new List<int>();
            if (proj != null && proj.OperandDescriptions != null)
            {
                foreach (var kvp in proj.OperandDescriptions)
                {
                    int addr;
                    if (kvp.Key.StartsWith("MI") && int.TryParse(kvp.Key.Substring(2), out addr) && addr >= 0 && addr < 2048)
                    {
                        string d = (kvp.Value ?? "").ToLowerInvariant();
                        if (d.StartsWith("analog_") || d.StartsWith("ai") || d.StartsWith("an_") ||
                            d.Contains("pt100") || d.Contains("4_20ma") || d.Contains("czujnik_temp") ||
                            d.Contains("temp_komory") || d.Contains("wilg_komory"))
                        {
                            if (!list.Contains(addr)) list.Add(addr);
                        }
                    }
                }
            }

            list.Sort();
            for (int ch = 0; list.Count < minChannels; ch++)
            {
                int fallbackMi = 100 + ch;
                if (!list.Contains(fallbackMi))
                {
                    list.Add(fallbackMi);
                }
            }
            return list;
        }

        public void SaveToFile(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Unitronics Universal PLC I/O Simulation Profile");
            sb.AppendLine("IsSimulationEnabled=" + (IsSimulationEnabled ? "1" : "0"));

            foreach (var kvp in InputRules)
            {
                var r = kvp.Value;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "DI={0}|{1}|{2}|{3}|{4}|{5}",
                    r.Address, (int)r.Role, r.ForcedState ? 1 : 0, r.LinkedOutputAddress, r.InvertFeedback ? 1 : 0, r.DelayMs));
            }

            foreach (var kvp in OutputRules)
            {
                var r = kvp.Value;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "DO={0}|{1}", r.Address, (int)r.Role));
            }

            foreach (var kvp in AnalogRules)
            {
                var r = kvp.Value;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "AI={0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}",
                    r.ChannelIndex, r.TargetMiAddress, (int)r.Role, (r.Unit ?? "").Replace("|", "/"),
                    r.InitialValue, r.AmbientValue, r.MinValue, r.MaxValue,
                    (r.IncreasingOutputsSpec ?? "").Replace("|", ","), r.IncreaseRatePerSec,
                    (r.DecreasingOutputsSpec ?? "").Replace("|", ","), r.DecreaseRatePerSec));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return;
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                if (line.StartsWith("IsSimulationEnabled="))
                {
                    IsSimulationEnabled = (line.Substring("IsSimulationEnabled=".Length).Trim() == "1");
                }
                else if (line.StartsWith("DI="))
                {
                    string[] p = line.Substring(3).Split('|');
                    int addr;
                    if (p.Length >= 6 && int.TryParse(p[0], out addr))
                    {
                        var r = new DigitalInputRule { Address = addr };
                        int roleInt, forcedInt, linkedOut, invInt, delay;
                        if (int.TryParse(p[1], out roleInt)) r.Role = (DigitalInputRole)roleInt;
                        if (int.TryParse(p[2], out forcedInt)) r.ForcedState = (forcedInt != 0);
                        if (int.TryParse(p[3], out linkedOut)) r.LinkedOutputAddress = linkedOut;
                        if (int.TryParse(p[4], out invInt)) r.InvertFeedback = (invInt != 0);
                        if (int.TryParse(p[5], out delay)) r.DelayMs = delay;
                        InputRules[addr] = r;
                    }
                }
                else if (line.StartsWith("DO="))
                {
                    string[] p = line.Substring(3).Split('|');
                    int addr, roleInt;
                    if (p.Length >= 2 && int.TryParse(p[0], out addr) && int.TryParse(p[1], out roleInt))
                    {
                        OutputRules[addr] = new DigitalOutputRule { Address = addr, Role = (DigitalOutputRole)roleInt };
                    }
                }
                else if (line.StartsWith("AI="))
                {
                    string[] p = line.Substring(3).Split('|');
                    int ch;
                    if (p.Length >= 12 && int.TryParse(p[0], out ch))
                    {
                        var r = new AnalogInputRule { ChannelIndex = ch };
                        int targetMi, roleInt;
                        short initVal, ambVal, minVal, maxVal;
                        double incRate, decRate;
                        if (int.TryParse(p[1], out targetMi)) r.TargetMiAddress = targetMi;
                        if (int.TryParse(p[2], out roleInt)) r.Role = (AnalogInputRole)roleInt;
                        r.Unit = p[3];
                        if (short.TryParse(p[4], out initVal)) r.InitialValue = initVal;
                        if (short.TryParse(p[5], out ambVal)) r.AmbientValue = ambVal;
                        if (short.TryParse(p[6], out minVal)) r.MinValue = minVal;
                        if (short.TryParse(p[7], out maxVal)) r.MaxValue = maxVal;
                        r.IncreasingOutputsSpec = p[8];
                        if (double.TryParse(p[9], NumberStyles.Any, CultureInfo.InvariantCulture, out incRate)) r.IncreaseRatePerSec = incRate;
                        r.DecreasingOutputsSpec = p[10];
                        if (double.TryParse(p[11], NumberStyles.Any, CultureInfo.InvariantCulture, out decRate)) r.DecreaseRatePerSec = decRate;
                        r.CurrentPreciseValue = r.InitialValue;
                        AnalogRules[ch] = r;
                    }
                }
            }
            HasUserConfigured = true;
        }
    }
}
