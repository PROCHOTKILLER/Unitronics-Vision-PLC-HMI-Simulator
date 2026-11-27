using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Engine;

/// <summary>
/// Tabela wymuszeń bitów (I/O, MB, SB). Dwa etapy skanu — <see cref="BeforeLogic"/> i <see cref="AfterLogic"/> —
/// nakładają wymuszone wartości przed i po wykonaniu drabinki.
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Wejścia, MB i SB są wymuszane przed LAD, aby program widział wymuszoną wartość. Po LAD nakładane są wszystkie
/// wymuszenia, więc cewka programu nie przywraca wartości wyjścia O ani bitu MB.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Własna blokada chroni tylko słownik wymuszeń; zapis do <see cref="PlcMemory"/> odbywa się w wątku skanu pod
/// <see cref="PlcMemory.SyncRoot"/>.
/// </remarks>
public sealed class ForceTable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<OperandAddress, bool> _forced = new();

    public ForceTable()
    {
        BeforeLogic = new ApplyStep(this, includeOutputs: false);
        AfterLogic = new ApplyStep(this, includeOutputs: true);
    }

    /// <summary>Zdarzenie zmiany zawartości tabeli (wywoływane poza blokadą, w wątku wywołującego).</summary>
    public event EventHandler? Changed;

    /// <summary>Etap skanu wymuszający I/MB/SB (bez wyjść O) przed drabinką.</summary>
    public IScanStep BeforeLogic { get; }

    /// <summary>Etap skanu nakładający wszystkie wymuszenia po drabince.</summary>
    public IScanStep AfterLogic { get; }

    /// <summary>Wymusza wartość bitu; dozwolone tylko operandy bitowe (I, O, MB, SB, XB).</summary>
    public void Force(OperandAddress bit, bool value)
    {
        if (!bit.Type.IsBit())
        {
            throw new ArgumentException($"{bit.Type} nie jest operandem bitowym i nie może być wymuszony.", nameof(bit));
        }

        if (bit.Index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bit), bit, "Indeks operandu nie może być ujemny.");
        }

        lock (_gate)
        {
            _forced[bit] = value;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zdejmuje wymuszenie; zwraca false, gdy operand nie był wymuszony. Ostatnia wartość pozostaje w pamięci.</summary>
    public bool Release(OperandAddress address)
    {
        bool removed;
        lock (_gate)
        {
            removed = _forced.Remove(address);
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>Zdejmuje wszystkie wymuszenia.</summary>
    public void ReleaseAll()
    {
        bool any;
        lock (_gate)
        {
            any = _forced.Count > 0;
            _forced.Clear();
        }

        if (any)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Kopia aktualnych wymuszeń (niezależna od dalszych zmian).</summary>
    public IReadOnlyDictionary<OperandAddress, bool> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<OperandAddress, bool>(_forced);
        }
    }

    private void Apply(PlcMemory memory, bool includeOutputs)
    {
        KeyValuePair<OperandAddress, bool>[] entries;
        lock (_gate)
        {
            if (_forced.Count == 0)
            {
                return;
            }

            entries = _forced.ToArray();
        }

        foreach (var (address, value) in entries)
        {
            if (!includeOutputs && address.Type == OperandType.O)
            {
                continue;
            }

            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Wymuszenie poza zakresem układu pamięci (np. po zmianie modelu) jest pomijane, a nie przerywa skanu.
            if (memory.Contains(address))
            {
                memory.SetBit(address, value);
            }
        }
    }

    private sealed class ApplyStep(ForceTable owner, bool includeOutputs) : IScanStep
    {
        public void Execute(PlcMemory memory, ScanContext context) => owner.Apply(memory, includeOutputs);
    }
}
