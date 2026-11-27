using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Profile;

/// <summary>Etap skanu symulujący obiekt według profilu: wartości początkowe, sprzężenia bitowe i analogi.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Etap rejestrowany jako pierwszy w skanie: wejścia obiektu wyliczane są z wyjść poprzedniego skanu (jak w
/// rzeczywistej maszynie), a wymuszenia z <see cref="ForceTable"/> nakładane po nim mają pierwszeństwo.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// <see cref="Load"/> i <see cref="Unload"/> można wołać z wątku UI w trakcie pracy — model jest podmieniany
/// atomowo, a stan przebiegów (liczniki opóźnień, wartości analogów) tworzony od nowa.
/// </remarks>
public sealed class PlantSimulationStep : IScanStep
{
    private PlantRuntime? _runtime;

    /// <summary>Aktualnie załadowany model albo null.</summary>
    public PlantModel? Model => Volatile.Read(ref _runtime)?.Model;

    public void Load(PlantModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Volatile.Write(ref _runtime, new PlantRuntime(model));
    }

    public void Unload() => Volatile.Write(ref _runtime, null);

    public void Execute(PlcMemory memory, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(context);
        Volatile.Read(ref _runtime)?.Execute(memory, context);
    }

    private sealed class PlantRuntime(PlantModel model)
    {
        private readonly TimeSpan[] _pending = new TimeSpan[model.DigitalFeedback.Count];
        private readonly double[] _analogValue = new double[model.AnalogFollowers.Count];
        private readonly double[] _analogWritten = new double[model.AnalogFollowers.Count];
        private readonly bool[] _analogInitialized = new bool[model.AnalogFollowers.Count];

        public PlantModel Model { get; } = model;

        public void Execute(PlcMemory memory, ScanContext context)
        {
            if (context.IsFirstScan)
            {
                Array.Clear(_pending);
                Array.Clear(_analogInitialized);
                foreach (var initial in Model.InitialValues)
                {
                    Write(memory, initial.Address, initial.Value);
                }
            }

            var elapsed = context.Elapsed < TimeSpan.Zero ? TimeSpan.Zero : context.Elapsed;
            for (var i = 0; i < Model.DigitalFeedback.Count; i++)
            {
                UpdateFeedback(memory, Model.DigitalFeedback[i], ref _pending[i], elapsed);
            }

            for (var i = 0; i < Model.AnalogFollowers.Count; i++)
            {
                UpdateAnalog(memory, Model.AnalogFollowers[i], i, elapsed);
            }
        }

        private static void UpdateFeedback(PlcMemory memory, DigitalFeedback link, ref TimeSpan pending, TimeSpan elapsed)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Model wczytany w trakcie pracy mógł być walidowany względem innego układu — adres spoza pamięci pomijamy.
            if (!memory.Contains(link.Source) || !memory.Contains(link.Target))
            {
                return;
            }

            var desired = memory.GetBit(link.Source) ^ link.Invert;
            if (memory.GetBit(link.Target) == desired)
            {
                pending = TimeSpan.Zero;
                return;
            }

            pending += elapsed;
            if (pending >= (desired ? link.OnDelay : link.OffDelay))
            {
                memory.SetBit(link.Target, desired);
                pending = TimeSpan.Zero;
            }
        }

        private void UpdateAnalog(PlcMemory memory, AnalogFollower link, int index, TimeSpan elapsed)
        {
            if (!memory.Contains(link.Target)
                || (link.SourceBit is { } bit && !memory.Contains(bit))
                || (link.SourceRegister is { } register && !memory.Contains(register)))
            {
                return;
            }

            // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
            // Stan trzymany jako double, bo zaokrąglanie do rejestru całkowitego przy krótkim skanie zatrzymałoby
            // przebieg (przyrost < 0,5 na skan). Gdy ktoś zapisał rejestr z zewnątrz (UI, PCOM, LAD), przebieg
            // startuje od tej wartości. Inercja I rzędu w postaci dyskretnej, dokładnej dla dowolnego dt:
            // y ← y + (z − y)·(1 − e^(−dt/τ)).
            var current = Read(memory, link.Target);
            if (!_analogInitialized[index] || current != _analogWritten[index])
            {
                _analogValue[index] = current;
                _analogInitialized[index] = true;
            }

            var setpoint = link.SourceBit is { } sourceBit
                ? (memory.GetBit(sourceBit) ? link.OnValue : link.OffValue)
                : (link.Gain * Read(memory, link.SourceRegister!.Value)) + link.Offset;

            var value = _analogValue[index];
            value = link.TimeConstant <= TimeSpan.Zero
                ? setpoint
                : value + ((setpoint - value) * (1 - Math.Exp(-elapsed.TotalMilliseconds / link.TimeConstant.TotalMilliseconds)));

            value = Math.Clamp(value, link.Min, link.Max);
            _analogValue[index] = value;
            Write(memory, link.Target, value);
            _analogWritten[index] = Read(memory, link.Target);
        }

        private static double Read(PlcMemory memory, OperandAddress address)
        {
            if (address.Type == OperandType.MF)
            {
                return memory.GetFloat(address.Index);
            }

            if (address.Type.IsBit())
            {
                return memory.GetBit(address) ? 1.0 : 0.0;
            }

            return memory.GetNumber(address);
        }

        private static void Write(PlcMemory memory, OperandAddress address, double value)
        {
            if (!memory.Contains(address))
            {
                return;
            }

            if (address.Type == OperandType.MF)
            {
                memory.SetFloat(address.Index, (float)value);
            }
            else if (address.Type.IsBit())
            {
                memory.SetBit(address, value >= 0.5);
            }
            else
            {
                // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
                // Nasycenie do zakresu typu zamiast zawinięcia bitów (MI 40000 → 32767, a nie −25536).
                var (min, max) = RangeOf(address.Type);
                memory.SetNumber(address, (long)Math.Round(Math.Clamp(value, min, max), MidpointRounding.AwayFromZero));
            }
        }

        private static (double Min, double Max) RangeOf(OperandType type) => type switch
        {
            OperandType.MI or OperandType.SI or OperandType.XI => (short.MinValue, short.MaxValue),
            OperandType.ML or OperandType.SL or OperandType.XL => (int.MinValue, int.MaxValue),
            OperandType.DW or OperandType.SDW or OperandType.XDW => (uint.MinValue, uint.MaxValue),
            _ => (long.MinValue, long.MaxValue),
        };
    }
}
