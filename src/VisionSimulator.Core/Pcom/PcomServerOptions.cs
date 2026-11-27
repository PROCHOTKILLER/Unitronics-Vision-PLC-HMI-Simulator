using System.Net;

namespace VisionSimulator.Core.Pcom;

/// <summary>Ustawienia serwera PCOM (sekcja <c>Pcom</c> w appsettings.json).</summary>
public sealed class PcomServerOptions
{
    public const string SectionName = "Pcom";

    /// <summary>Czy serwer PCOM ma być uruchamiany razem z aplikacją.</summary>
    public bool Enabled { get; set; } = true;

    // [OSTRZEŻENIA I KONSEKWENCJE]
    // PCOM nie ma uwierzytelnienia: każdy, kto dosięgnie portu, czyta i zapisuje pamięć symulatora.
    // Domyślnie nasłuch tylko na pętli zwrotnej; adres LAN ustawia się świadomie w appsettings.Local.json.
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>Maksymalna długość ramki PCOM (bajty); dłuższa ramka kończy połączenie.</summary>
    public const int MaxFrameLength = 4096;

    /// <summary>Port TCP; 0 oznacza port przydzielony przez system (odczyt: <c>PcomServer.LocalEndPoint</c>).</summary>
    public int Port { get; set; } = 20256;

    /// <summary>Maksymalna liczba równoczesnych klientów; kolejne połączenia są zamykane od razu.</summary>
    public int MaxClients { get; set; } = 4;

    /// <summary>Czas bezczynności połączenia w sekundach, po którym serwer je zamyka; 0 wyłącza limit.</summary>
    public int IdleTimeoutSeconds { get; set; } = 60;

    /// <summary>Nazwa zwracana w odpowiedzi na polecenie ID.</summary>
    public string PlcName { get; set; } = "virtual";

    public IPEndPoint ToEndPoint()
    {
        if (!IPAddress.TryParse(BindAddress, out var address))
        {
            throw new InvalidOperationException($"Pcom:BindAddress '{BindAddress}' nie jest adresem IP.");
        }

        if (Port is < 0 or > 65535)
        {
            throw new InvalidOperationException($"Pcom:Port {Port} poza zakresem 0..65535.");
        }

        return new IPEndPoint(address, Port);
    }
}
