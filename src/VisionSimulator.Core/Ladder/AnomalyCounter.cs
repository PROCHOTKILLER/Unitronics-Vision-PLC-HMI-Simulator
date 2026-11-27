namespace VisionSimulator.Core.Ladder;

/// <summary>Licznik anomalii wykonania (dzielenie przez zero, operand poza zakresem, zbyt głębokie wywołania).</summary>
/// <remarks>
/// [OPTYMALIZACJA WYDAJNOŚCI (PERFORMANCE)]
/// Anomalia może powtarzać się w każdym skanie, więc komunikat składany jest dopiero przy odczycie
/// (<see cref="LastMessage"/>), a w skanie zapisywane są tylko liczby i stały tekst powodu.
/// </remarks>
internal sealed class AnomalyCounter
{
    private long _count;
    private int _lastElementId;
    private string _lastReason = string.Empty;

    public long Count => Interlocked.Read(ref _count);

    public string? LastMessage
    {
        get
        {
            var reason = Volatile.Read(ref _lastReason);
            return reason.Length == 0 ? null : $"Element {Volatile.Read(ref _lastElementId)}: {reason}";
        }
    }

    public void Report(int elementId, string reason)
    {
        Interlocked.Increment(ref _count);
        Volatile.Write(ref _lastElementId, elementId);
        Volatile.Write(ref _lastReason, reason);
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _count, 0);
        Volatile.Write(ref _lastReason, string.Empty);
    }
}
