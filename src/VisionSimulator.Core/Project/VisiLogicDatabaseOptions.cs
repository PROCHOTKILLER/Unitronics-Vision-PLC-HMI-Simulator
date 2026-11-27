using System.Data.OleDb;

namespace VisionSimulator.Core.Project;

/// <summary>Dostęp do baz Jet 4.0 wewnątrz plików VisiLogic (sekcja <c>VisiLogicDatabase</c>).</summary>
public sealed class VisiLogicDatabaseOptions
{
    public const string SectionName = "VisiLogicDatabase";
    public const string JetProvider = "Microsoft.Jet.OLEDB.4.0";
    public const string AceProvider = "Microsoft.ACE.OLEDB.16.0";

    // [ARCHITEKTURA I ADR]
    // Stałe hasło dostawcy: VisiLogic zabezpiecza nim bazy Jet w każdej instalacji, więc nie jest sekretem
    // konkretnego projektu (decyzja użytkownika 2026-11-25). Konfiguracja może je nadpisać.
    public const string DefaultPassword = "UniOpenSesame";

    /// <summary>Nadpisanie hasła (appsettings.Local.json / VisiLogicDatabase__Password); puste = <see cref="DefaultPassword"/>.</summary>
    public string? Password { get; set; }

    public bool IsPasswordOverridden => !string.IsNullOrEmpty(Password);

    public string EffectivePassword => IsPasswordOverridden ? Password! : DefaultPassword;

    /// <summary>Nazwa providera OLE DB; pusta = wybór według architektury procesu (x86 → Jet 4.0, x64 → ACE 16.0).</summary>
    public string? Provider { get; set; }

    public string ResolveProvider() => ResolveProvider(Provider, Environment.Is64BitProcess);

    // [ARCHITEKTURA I ADR]
    // Jet 4.0 jest wbudowany w Windows, ale tylko jako 32-bit. W x64 ten sam format .mdb/.udb czyta ACE
    // (Microsoft Access Database Engine 2016 Redistributable x64), który trzeba doinstalować na stacji.
    // Nazwa providera jest konfigurowalna (np. ACE.OLEDB.12.0), aby nie wymuszać konkretnej wersji ACE.
    public static string ResolveProvider(string? configured, bool is64BitProcess)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return is64BitProcess ? AceProvider : JetProvider;
        }

        var provider = configured.Trim();
        if (is64BitProcess && provider.StartsWith("Microsoft.Jet.", StringComparison.OrdinalIgnoreCase))
        {
            throw new PlatformNotSupportedException(
                $"Provider '{provider}' istnieje tylko w procesie x86. Użyj wersji x86 programu albo providera ACE.");
        }

        return provider;
    }

    public OleDbConnection CreateConnection(string databasePath, bool readOnly = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var builder = new OleDbConnectionStringBuilder
        {
            Provider = ResolveProvider(),
            DataSource = databasePath,
        };
        builder["Mode"] = readOnly ? "Share Deny None" : "Share Exclusive";

        // Jet i ACE używają tej samej nazwy właściwości hasła bazy.
        builder["Jet OLEDB:Database Password"] = EffectivePassword;

        return new OleDbConnection(builder.ConnectionString);
    }

    /// <summary>Tworzy i otwiera połączenie; brak providera zamienia na czytelny <see cref="PlatformNotSupportedException"/>.</summary>
    public OleDbConnection OpenConnection(string databasePath, bool readOnly = true)
    {
        var connection = CreateConnection(databasePath, readOnly);
        try
        {
            connection.Open();
            return connection;
        }
        catch (InvalidOperationException ex)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // OleDbConnection.Open() rzuca InvalidOperationException, gdy provider nie jest zarejestrowany
            // dla architektury procesu (typowo: x64 bez zainstalowanego ACE).
            connection.Dispose();
            var architecture = Environment.Is64BitProcess ? "x64" : "x86";
            throw new PlatformNotSupportedException(
                $"Provider '{ResolveProvider()}' nie jest zainstalowany dla procesu {architecture}. " +
                "Zainstaluj Microsoft Access Database Engine 2016 Redistributable (x64) albo uruchom wersję x86 programu.",
                ex);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
