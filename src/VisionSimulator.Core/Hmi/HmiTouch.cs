using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Hmi;

/// <summary>Obsługa dotyku ekranu HMI.</summary>
public static class HmiTouch
{
    /// <summary>
    /// Przycisk chwilowy: wciśnięcie ustawia bit, zwolnienie go zeruje.
    /// Wywołujący musi trzymać <c>lock (memory.SyncRoot)</c>.
    /// </summary>
    /// <returns>True, gdy bit został zapisany; false dla adresu spoza pamięci lub operandu niebitowego.</returns>
    public static bool Press(PlcMemory memory, OperandAddress address, bool pressed)
    {
        ArgumentNullException.ThrowIfNull(memory);

        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Adres z projektu może wykraczać poza układ pamięci — dotyk jest wtedy ignorowany, bez wyjątku.
        if (!address.Type.IsBit() || !memory.Contains(address))
        {
            return false;
        }

        memory.SetBit(address, pressed);
        return true;
    }
}
