using System.IO.Compression;

namespace VisionSimulator.Core.Project;

internal enum ProjectContainerKind
{
    Unknown,
    Zip,
    JetDatabase,
}

/// <summary>Kopia bazy Jet projektu w katalogu tymczasowym; usuwana przy Dispose.</summary>
internal sealed class TemporaryDatabase : IDisposable
{
    internal const string PreferredEntryName = "Current_OPLC.udb";
    internal const long MaxDatabaseBytes = 512L * 1024 * 1024;

    private readonly string _directory;

    private TemporaryDatabase(string directory, string path)
    {
        _directory = directory;
        FilePath = path;
    }

    public string FilePath { get; }

    // [INTENCJA I POWÓD DZIAŁANIA]
    // Jet blokuje otwarty plik (.ldb) i nie czyta z ZIP. Pracujemy zawsze na prywatnej kopii, dzięki czemu
    // projekt może być jednocześnie otwarty w VisiLogic, a oryginał nigdy nie jest modyfikowany.
    public static TemporaryDatabase Create(string sourcePath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "VisionSimulator", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "project.udb");

        try
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            switch (Detect(source))
            {
                case ProjectContainerKind.Zip:
                    ExtractDatabase(source, target);
                    break;
                case ProjectContainerKind.JetDatabase:
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(output);
                    }

                    break;
                default:
                    throw new InvalidDataException($"'{Path.GetFileName(sourcePath)}' nie jest projektem VisiLogic (ani ZIP, ani baza Jet).");
            }

            return new TemporaryDatabase(directory, target);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    public void Dispose() => TryDeleteDirectory(_directory);

    /// <summary>Rozpoznaje kontener po sygnaturze; pozycja strumienia wraca na początek.</summary>
    internal static ProjectContainerKind Detect(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        stream.Position = 0;
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        stream.Position = 0;

        if (read < header.Length)
        {
            return ProjectContainerKind.Unknown;
        }

        return header switch
        {
            [0x50, 0x4B, 0x03, 0x04] => ProjectContainerKind.Zip,
            [0x00, 0x01, 0x00, 0x00] => ProjectContainerKind.JetDatabase,
            _ => ProjectContainerKind.Unknown,
        };
    }

    /// <summary>Wybiera wpis bazy: Current_OPLC.udb, a w razie braku pierwszy plik *.udb.</summary>
    internal static ZipArchiveEntry? SelectDatabaseEntry(ZipArchive archive) =>
        archive.Entries.FirstOrDefault(e => string.Equals(e.Name, PreferredEntryName, StringComparison.OrdinalIgnoreCase))
        ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".udb", StringComparison.OrdinalIgnoreCase));

    private static void ExtractDatabase(Stream source, string target)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entry = SelectDatabaseEntry(archive)
            ?? throw new InvalidDataException("Archiwum .vlp nie zawiera bazy *.udb.");

        // [OSTRZEŻENIA I KONSEKWENCJE]
        // Nazwa wpisu służy tylko do wyboru; plik docelowy ma stałą ścieżkę (brak Zip Slip).
        // Limit rozmiaru chroni przed archiwum, które po rozpakowaniu zapełni dysk.
        if (entry.Length > MaxDatabaseBytes)
        {
            throw new InvalidDataException($"Baza w archiwum ma {entry.Length} B, limit to {MaxDatabaseBytes} B.");
        }

        using var input = entry.Open();
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Jet może chwilę trzymać plik .ldb po zamknięciu połączenia; resztę usuwa system z %TEMP%.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
