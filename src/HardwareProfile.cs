using System;
using System.Collections.Generic;

namespace UnitronicsV350Simulator
{
    public class PlcModelSpec
    {
        public string Family { get; set; }
        public string ModelName { get; set; }
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public bool HasTouchscreen { get; set; }
        public bool IsModular { get; set; }
        public string DisplayType { get; set; }
        public int OnboardDi { get; set; }
        public int OnboardDoRelay { get; set; }
        public int OnboardDoTrans { get; set; }
        public int OnboardAi { get; set; }
        public int OnboardAo { get; set; }
        public string Description { get; set; }
        public string IdentificationString { get; set; }

        public int TotalOnboardDo { get { return OnboardDoRelay + OnboardDoTrans; } }

        public PlcModelSpec()
        {
            DisplayType = "Color TFT";
        }
    }

    public class SnapInModuleSpec
    {
        public string ModelName { get; set; }
        public int Di { get; set; }
        public int DoRelay { get; set; }
        public int DoTrans { get; set; }
        public int Ai { get; set; }
        public int Ao { get; set; }
        public string Description { get; set; }

        public int TotalDo { get { return DoRelay + DoTrans; } }
    }

    public class ExpansionModuleSpec
    {
        public string ModelName { get; set; }
        public int Di { get; set; }
        public int DoRelay { get; set; }
        public int DoTrans { get; set; }
        public int Ai { get; set; }
        public int Ao { get; set; }
        public string Description { get; set; }

        public int TotalDo { get { return DoRelay + DoTrans; } }
    }

    public class ExpansionSlotConfig
    {
        public int SlotNumber { get; set; }
        public string ModuleName { get; set; }

        public ExpansionSlotConfig()
        {
            ModuleName = "None";
        }

        public ExpansionSlotConfig(int slot, string mod)
        {
            SlotNumber = slot;
            ModuleName = mod;
        }
    }

    public class HardwareProfile
    {
        public string SelectedModelName { get; set; }
        public string SelectedSnapInName { get; set; }
        public bool EnableExpansionBus { get; set; }
        public List<ExpansionSlotConfig> ExpansionSlots { get; set; }

        // Katalog dostępnych modeli sterowników
        public static readonly Dictionary<string, PlcModelSpec> AvailableModels = new Dictionary<string, PlcModelSpec>(StringComparer.OrdinalIgnoreCase);

        // Katalog modułów Snap-in dla sterowników modułowych
        public static readonly Dictionary<string, SnapInModuleSpec> AvailableSnapIns = new Dictionary<string, SnapInModuleSpec>(StringComparer.OrdinalIgnoreCase);

        // Katalog modułów rozszerzeń EX-A2X
        public static readonly Dictionary<string, ExpansionModuleSpec> AvailableExpansions = new Dictionary<string, ExpansionModuleSpec>(StringComparer.OrdinalIgnoreCase);

        static HardwareProfile()
        {
            InitCatalogs();
        }

        public HardwareProfile()
        {
            SelectedModelName = "V350-35-RA22";
            SelectedSnapInName = "None";
            EnableExpansionBus = true;
            ExpansionSlots = new List<ExpansionSlotConfig>();
            EnsureSlotsInitialized();
        }

        public void EnsureSlotsInitialized()
        {
            if (ExpansionSlots == null) ExpansionSlots = new List<ExpansionSlotConfig>();
            while (ExpansionSlots.Count < 8)
            {
                ExpansionSlots.Add(new ExpansionSlotConfig(ExpansionSlots.Count + 1, "None"));
            }
        }

        public PlcModelSpec GetCurrentModelSpec()
        {
            PlcModelSpec spec;
            if (AvailableModels.TryGetValue(SelectedModelName ?? "V350-35-RA22", out spec))
            {
                return spec;
            }
            return AvailableModels["V350-35-RA22"];
        }

        public SnapInModuleSpec GetCurrentSnapInSpec()
        {
            SnapInModuleSpec spec;
            if (AvailableSnapIns.TryGetValue(SelectedSnapInName ?? "None", out spec))
            {
                return spec;
            }
            return AvailableSnapIns["None"];
        }

        public ExpansionModuleSpec GetExpansionSpec(string moduleName)
        {
            ExpansionModuleSpec spec;
            if (AvailableExpansions.TryGetValue(moduleName ?? "None", out spec))
            {
                return spec;
            }
            return AvailableExpansions["None"];
        }

        public int GetTotalDi()
        {
            var model = GetCurrentModelSpec();
            int total = model.OnboardDi;

            if (model.IsModular)
            {
                var snapIn = GetCurrentSnapInSpec();
                total += snapIn.Di;
            }

            if (EnableExpansionBus && ExpansionSlots != null)
            {
                foreach (var slot in ExpansionSlots)
                {
                    var exp = GetExpansionSpec(slot.ModuleName);
                    total += exp.Di;
                }
            }
            return total;
        }

        public int GetTotalDo()
        {
            var model = GetCurrentModelSpec();
            int total = model.TotalOnboardDo;

            if (model.IsModular)
            {
                var snapIn = GetCurrentSnapInSpec();
                total += snapIn.TotalDo;
            }

            if (EnableExpansionBus && ExpansionSlots != null)
            {
                foreach (var slot in ExpansionSlots)
                {
                    var exp = GetExpansionSpec(slot.ModuleName);
                    total += exp.TotalDo;
                }
            }
            return total;
        }

        public int GetTotalAi()
        {
            var model = GetCurrentModelSpec();
            int total = model.OnboardAi;

            if (model.IsModular)
            {
                var snapIn = GetCurrentSnapInSpec();
                total += snapIn.Ai;
            }

            if (EnableExpansionBus && ExpansionSlots != null)
            {
                foreach (var slot in ExpansionSlots)
                {
                    var exp = GetExpansionSpec(slot.ModuleName);
                    total += exp.Ai;
                }
            }
            return total;
        }

        public int GetTotalAo()
        {
            var model = GetCurrentModelSpec();
            int total = model.OnboardAo;

            if (model.IsModular)
            {
                var snapIn = GetCurrentSnapInSpec();
                total += snapIn.Ao;
            }

            if (EnableExpansionBus && ExpansionSlots != null)
            {
                foreach (var slot in ExpansionSlots)
                {
                    var exp = GetExpansionSpec(slot.ModuleName);
                    total += exp.Ao;
                }
            }
            return total;
        }

        public string GetPcomIdString()
        {
            var spec = GetCurrentModelSpec();
            return spec.IdentificationString ?? (spec.ModelName + ",4.04(12)");
        }

        public string GetModelFullName()
        {
            var spec = GetCurrentModelSpec();
            if (spec.IsModular && !string.IsNullOrEmpty(SelectedSnapInName) && SelectedSnapInName != "None")
            {
                return string.Format("{0} [{1}]", spec.ModelName, SelectedSnapInName);
            }
            return spec.ModelName;
        }

        public static HardwareProfile CreateKomora9Profile()
        {
            var prof = new HardwareProfile();
            prof.SelectedModelName = "V350-35-RA22";
            prof.SelectedSnapInName = "None";
            prof.EnableExpansionBus = true;
            prof.ExpansionSlots = new List<ExpansionSlotConfig>
            {
                new ExpansionSlotConfig(1, "IO-DI8-RO8"),
                new ExpansionSlotConfig(2, "None"),
                new ExpansionSlotConfig(3, "None"),
                new ExpansionSlotConfig(4, "None"),
                new ExpansionSlotConfig(5, "None"),
                new ExpansionSlotConfig(6, "None"),
                new ExpansionSlotConfig(7, "None"),
                new ExpansionSlotConfig(8, "None")
            };
            return prof;
        }

        public static HardwareProfile CreateDefaultProfile()
        {
            return CreateKomora9Profile();
        }

        private static void InitCatalogs()
        {
            // --- 1. Vision 350 ---
            AvailableModels["V350-35-RA22"] = new PlcModelSpec
            {
                Family = "Vision 350",
                ModelName = "V350-35-RA22",
                ScreenWidth = 320,
                ScreenHeight = 240,
                HasTouchscreen = true,
                IsModular = false,
                DisplayType = "Color TFT 3.5\" QVGA",
                OnboardDi = 12,
                OnboardDoRelay = 6,
                OnboardDoTrans = 2,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "3.5\" QVGA Color Touchscreen, 12 DI, 2 AI, 6 Relay Out, 2 Transistor High-Speed Out",
                IdentificationString = "V350-35-RA22,4.04(12)"
            };
            AvailableModels["V350-35-TRA22"] = new PlcModelSpec
            {
                Family = "Vision 350",
                ModelName = "V350-35-TRA22",
                ScreenWidth = 320,
                ScreenHeight = 240,
                HasTouchscreen = true,
                IsModular = false,
                DisplayType = "Color TFT 3.5\" QVGA",
                OnboardDi = 12,
                OnboardDoRelay = 4,
                OnboardDoTrans = 4,
                OnboardAi = 2,
                OnboardAo = 2,
                Description = "3.5\" QVGA Color Touchscreen, 12 DI, 2 AI, 4 Relay, 4 Transistor, 2 Analog Out",
                IdentificationString = "V350-35-TRA22,4.04(12)"
            };
            AvailableModels["V350-35-TA24"] = new PlcModelSpec
            {
                Family = "Vision 350",
                ModelName = "V350-35-TA24",
                ScreenWidth = 320,
                ScreenHeight = 240,
                HasTouchscreen = true,
                IsModular = false,
                DisplayType = "Color TFT 3.5\" QVGA",
                OnboardDi = 12,
                OnboardDoRelay = 0,
                OnboardDoTrans = 12,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "3.5\" QVGA Color Touchscreen, 12 DI, 2 AI, 12 Transistor Out (High Speed)",
                IdentificationString = "V350-35-TA24,4.04(12)"
            };
            AvailableModels["V350-35-R34"] = new PlcModelSpec
            {
                Family = "Vision 350",
                ModelName = "V350-35-R34",
                ScreenWidth = 320,
                ScreenHeight = 240,
                HasTouchscreen = true,
                IsModular = false,
                DisplayType = "Color TFT 3.5\" QVGA",
                OnboardDi = 22,
                OnboardDoRelay = 12,
                OnboardDoTrans = 0,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "3.5\" QVGA Color Touchscreen, 22 DI, 2 AI, 12 Relay Out",
                IdentificationString = "V350-35-R34,4.04(12)"
            };
            AvailableModels["V350-35-T38"] = new PlcModelSpec
            {
                Family = "Vision 350",
                ModelName = "V350-35-T38",
                ScreenWidth = 320,
                ScreenHeight = 240,
                HasTouchscreen = true,
                IsModular = false,
                DisplayType = "Color TFT 3.5\" QVGA",
                OnboardDi = 22,
                OnboardDoRelay = 0,
                OnboardDoTrans = 16,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "3.5\" QVGA Color Touchscreen, 22 DI, 2 AI, 16 Transistor Out",
                IdentificationString = "V350-35-T38,4.04(12)"
            };

            // --- 2. Vision 130 ---
            AvailableModels["V130-33-RA22"] = new PlcModelSpec
            {
                Family = "Vision 130",
                ModelName = "V130-33-RA22",
                ScreenWidth = 320,
                ScreenHeight = 240,
                HasTouchscreen = false,
                IsModular = false,
                DisplayType = "STN Graphic 2.4\" Monochrome",
                OnboardDi = 12,
                OnboardDoRelay = 6,
                OnboardDoTrans = 2,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "2.4\" Graphic LCD (Keypad only), 12 DI, 2 AI, 6 Relay Out, 2 Transistor Out",
                IdentificationString = "V130-33-RA22,4.04(12)"
            };
            AvailableModels["V130-33-TA24"] = new PlcModelSpec
            {
                Family = "Vision 130",
                ModelName = "V130-33-TA24",
                ScreenWidth = 320,
                ScreenHeight = 240,
                HasTouchscreen = false,
                IsModular = false,
                DisplayType = "STN Graphic 2.4\" Monochrome",
                OnboardDi = 12,
                OnboardDoRelay = 0,
                OnboardDoTrans = 12,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "2.4\" Graphic LCD (Keypad only), 12 DI, 2 AI, 12 Transistor Out",
                IdentificationString = "V130-33-TA24,4.04(12)"
            };

            // --- 3. Vision 430 ---
            AvailableModels["V430-J-RA22"] = new PlcModelSpec
            {
                Family = "Vision 430",
                ModelName = "V430-J-RA22",
                ScreenWidth = 480,
                ScreenHeight = 272,
                HasTouchscreen = true,
                IsModular = false,
                DisplayType = "Color TFT 4.3\" WQVGA",
                OnboardDi = 12,
                OnboardDoRelay = 6,
                OnboardDoTrans = 2,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "4.3\" Widescreen Color Touchscreen, 12 DI, 2 AI, 6 Relay, 2 Transistor",
                IdentificationString = "V430-J-RA22,4.04(12)"
            };
            AvailableModels["V430-J-TA24"] = new PlcModelSpec
            {
                Family = "Vision 430",
                ModelName = "V430-J-TA24",
                ScreenWidth = 480,
                ScreenHeight = 272,
                HasTouchscreen = true,
                IsModular = false,
                DisplayType = "Color TFT 4.3\" WQVGA",
                OnboardDi = 12,
                OnboardDoRelay = 0,
                OnboardDoTrans = 12,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "4.3\" Widescreen Color Touchscreen, 12 DI, 2 AI, 12 Transistor Out",
                IdentificationString = "V430-J-TA24,4.04(12)"
            };
            AvailableModels["V430-J-RH2"] = new PlcModelSpec
            {
                Family = "Vision 430",
                ModelName = "V430-J-RH2",
                ScreenWidth = 480,
                ScreenHeight = 272,
                HasTouchscreen = true,
                IsModular = false,
                DisplayType = "Color TFT 4.3\" WQVGA",
                OnboardDi = 16,
                OnboardDoRelay = 10,
                OnboardDoTrans = 2,
                OnboardAi = 2,
                OnboardAo = 0,
                Description = "4.3\" Widescreen Color Touchscreen, 16 DI, 2 AI, 10 Relay, 2 Transistor",
                IdentificationString = "V430-J-RH2,4.04(12)"
            };

            // --- 4. Vision 570 ---
            AvailableModels["V570-57-T20B"] = new PlcModelSpec
            {
                Family = "Vision 570",
                ModelName = "V570-57-T20B",
                ScreenWidth = 320,
                ScreenHeight = 240,
                HasTouchscreen = true,
                IsModular = true,
                DisplayType = "Color TFT 5.7\" QVGA",
                OnboardDi = 0,
                OnboardDoRelay = 0,
                OnboardDoTrans = 0,
                OnboardAi = 0,
                OnboardAo = 0,
                Description = "5.7\" QVGA Color Touchscreen, Modular Architecture (Snap-in I/O slot required)",
                IdentificationString = "V570-57-T20B,4.04(12)"
            };

            // --- 5. Vision 1040 ---
            AvailableModels["V1040-20-T20B"] = new PlcModelSpec
            {
                Family = "Vision 1040",
                ModelName = "V1040-20-T20B",
                ScreenWidth = 800,
                ScreenHeight = 600,
                HasTouchscreen = true,
                IsModular = true,
                DisplayType = "Color TFT 10.4\" SVGA",
                OnboardDi = 0,
                OnboardDoRelay = 0,
                OnboardDoTrans = 0,
                OnboardAi = 0,
                OnboardAo = 0,
                Description = "10.4\" SVGA Color Touchscreen + Function Keys, Modular (Snap-in I/O slot required)",
                IdentificationString = "V1040-20-T20B,4.04(12)"
            };

            // --- 6. Vision 1210 ---
            AvailableModels["V1210-T20BJ"] = new PlcModelSpec
            {
                Family = "Vision 1210",
                ModelName = "V1210-T20BJ",
                ScreenWidth = 800,
                ScreenHeight = 600,
                HasTouchscreen = true,
                IsModular = true,
                DisplayType = "Color TFT 12.1\" SVGA",
                OnboardDi = 0,
                OnboardDoRelay = 0,
                OnboardDoTrans = 0,
                OnboardAi = 0,
                OnboardAo = 0,
                Description = "12.1\" SVGA Color Touchscreen, Edge-to-edge Flat Bezel, Modular (Snap-in I/O slot required)",
                IdentificationString = "V1210-T20BJ,4.04(12)"
            };

            // --- SNAP-IN MODULES (dla V570, V1040, V1210) ---
            AvailableSnapIns["None"] = new SnapInModuleSpec
            {
                ModelName = "None",
                Di = 0,
                DoRelay = 0,
                DoTrans = 0,
                Ai = 0,
                Ao = 0,
                Description = "No Snap-in module attached"
            };
            AvailableSnapIns["V200-18-E1B"] = new SnapInModuleSpec
            {
                ModelName = "V200-18-E1B",
                Di = 16,
                DoRelay = 10,
                DoTrans = 4,
                Ai = 3,
                Ao = 0,
                Description = "16 DI, 3 AI, 10 Relay Out, 4 Transistor Out"
            };
            AvailableSnapIns["V200-18-E2B"] = new SnapInModuleSpec
            {
                ModelName = "V200-18-E2B",
                Di = 16,
                DoRelay = 0,
                DoTrans = 10,
                Ai = 3,
                Ao = 0,
                Description = "16 DI, 3 AI, 10 Transistor Out"
            };
            AvailableSnapIns["V200-18-E3XB"] = new SnapInModuleSpec
            {
                ModelName = "V200-18-E3XB",
                Di = 18,
                DoRelay = 15,
                DoTrans = 2,
                Ai = 4,
                Ao = 2,
                Description = "18 DI, 4 AI, 2 AO, 15 Relay Out, 2 Transistor Out"
            };
            AvailableSnapIns["V200-18-E4XB"] = new SnapInModuleSpec
            {
                ModelName = "V200-18-E4XB",
                Di = 18,
                DoRelay = 0,
                DoTrans = 16,
                Ai = 4,
                Ao = 2,
                Description = "18 DI, 4 AI, 2 AO, 16 Transistor Out"
            };
            AvailableSnapIns["V200-18-E5B"] = new SnapInModuleSpec
            {
                ModelName = "V200-18-E5B",
                Di = 18,
                DoRelay = 14,
                DoTrans = 2,
                Ai = 3,
                Ao = 2,
                Description = "18 DI, 3 AI, 2 AO, 14 Relay Out, 2 Transistor Out"
            };
            AvailableSnapIns["V200-18-E6B"] = new SnapInModuleSpec
            {
                ModelName = "V200-18-E6B",
                Di = 18,
                DoRelay = 16,
                DoTrans = 0,
                Ai = 2,
                Ao = 2,
                Description = "18 DI, 2 AI, 2 AO, 16 Relay Out"
            };
            AvailableSnapIns["V200-18-E62B"] = new SnapInModuleSpec
            {
                ModelName = "V200-18-E62B",
                Di = 30,
                DoRelay = 0,
                DoTrans = 28,
                Ai = 2,
                Ao = 0,
                Description = "30 DI, 2 AI, 28 Transistor Out"
            };

            // --- EXPANSION MODULES (EX-A2X Bus, Slots 1..8) ---
            AvailableExpansions["None"] = new ExpansionModuleSpec
            {
                ModelName = "None",
                Di = 0,
                DoRelay = 0,
                DoTrans = 0,
                Ai = 0,
                Ao = 0,
                Description = "Empty Slot"
            };
            AvailableExpansions["IO-DI8-RO8"] = new ExpansionModuleSpec
            {
                ModelName = "IO-DI8-RO8",
                Di = 8,
                DoRelay = 8,
                DoTrans = 0,
                Ai = 0,
                Ao = 0,
                Description = "8 Digital Inputs (pnp/npn), 8 Relay Outputs"
            };
            AvailableExpansions["IO-DI8-TO8"] = new ExpansionModuleSpec
            {
                ModelName = "IO-DI8-TO8",
                Di = 8,
                DoRelay = 0,
                DoTrans = 8,
                Ai = 0,
                Ao = 0,
                Description = "8 Digital Inputs, 8 Transistor Outputs"
            };
            AvailableExpansions["IO-DI16"] = new ExpansionModuleSpec
            {
                ModelName = "IO-DI16",
                Di = 16,
                DoRelay = 0,
                DoTrans = 0,
                Ai = 0,
                Ao = 0,
                Description = "16 Digital Inputs (24VDC)"
            };
            AvailableExpansions["IO-RO16"] = new ExpansionModuleSpec
            {
                ModelName = "IO-RO16",
                Di = 0,
                DoRelay = 16,
                DoTrans = 0,
                Ai = 0,
                Ao = 0,
                Description = "16 Relay Outputs"
            };
            AvailableExpansions["IO-TO16"] = new ExpansionModuleSpec
            {
                ModelName = "IO-TO16",
                Di = 0,
                DoRelay = 0,
                DoTrans = 16,
                Ai = 0,
                Ao = 0,
                Description = "16 Transistor Outputs"
            };
            AvailableExpansions["IO-AI4-AO2"] = new ExpansionModuleSpec
            {
                ModelName = "IO-AI4-AO2",
                Di = 0,
                DoRelay = 0,
                DoTrans = 0,
                Ai = 4,
                Ao = 2,
                Description = "4 Analog Inputs (12-bit, 0-10V / 4-20mA), 2 Analog Outputs (12-bit)"
            };
            AvailableExpansions["IO-AI8"] = new ExpansionModuleSpec
            {
                ModelName = "IO-AI8",
                Di = 0,
                DoRelay = 0,
                DoTrans = 0,
                Ai = 8,
                Ao = 0,
                Description = "8 Analog Inputs (12-bit, Current/Voltage)"
            };
            AvailableExpansions["IO-AO6X"] = new ExpansionModuleSpec
            {
                ModelName = "IO-AO6X",
                Di = 0,
                DoRelay = 0,
                DoTrans = 0,
                Ai = 0,
                Ao = 6,
                Description = "6 Isolated Analog Outputs (12-bit)"
            };
            AvailableExpansions["IO-D16A3-RO16"] = new ExpansionModuleSpec
            {
                ModelName = "IO-D16A3-RO16",
                Di = 16,
                DoRelay = 16,
                DoTrans = 0,
                Ai = 3,
                Ao = 0,
                Description = "16 Digital Inputs, 3 Analog Inputs, 16 Relay Outputs"
            };
            AvailableExpansions["IO-D16A3-TO16"] = new ExpansionModuleSpec
            {
                ModelName = "IO-D16A3-TO16",
                Di = 16,
                DoRelay = 0,
                DoTrans = 16,
                Ai = 3,
                Ao = 0,
                Description = "16 Digital Inputs, 3 Analog Inputs, 16 Transistor Outputs"
            };
        }
    }
}
