using System.IO.Compression;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Tests;

public sealed class ProjectContainerTests : IDisposable
{
    private static readonly byte[] JetHeader = [0x00, 0x01, 0x00, 0x00, 0x53, 0x74, 0x61, 0x6E];

    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "VisionSimulator.Tests", Guid.NewGuid().ToString("N"));

    public ProjectContainerTests() => Directory.CreateDirectory(_workDir);

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14 }, "Zip")]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x53 }, "JetDatabase")]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, "Unknown")]
    [InlineData(new byte[] { 0x50, 0x4B }, "Unknown")]
    public void Detect_RecognisesSignature_AndRewindsStream(byte[] content, string expected)
    {
        // ProjectContainerKind jest internal, a publiczna metoda testowa nie może go przyjmować jako parametru.
        using var stream = new MemoryStream(content);

        Assert.Equal(expected, TemporaryDatabase.Detect(stream).ToString());
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void SelectDatabaseEntry_PrefersCurrentOplc()
    {
        using var archive = OpenZip(("Backup/Old.udb", JetHeader), ("Project/current_oplc.UDB", JetHeader), ("Info.xml", [1]));

        Assert.Equal("current_oplc.UDB", TemporaryDatabase.SelectDatabaseEntry(archive)?.Name);
    }

    [Fact]
    public void SelectDatabaseEntry_FallsBackToAnyUdb_OrNull()
    {
        using (var archive = OpenZip(("Info.xml", [1]), ("Other.udb", JetHeader)))
        {
            Assert.Equal("Other.udb", TemporaryDatabase.SelectDatabaseEntry(archive)?.Name);
        }

        using (var archive = OpenZip(("Info.xml", [1])))
        {
            Assert.Null(TemporaryDatabase.SelectDatabaseEntry(archive));
        }
    }

    [Fact]
    public void Create_FromZip_ExtractsToFixedPath_AndDeletesOnDispose()
    {
        var source = Path.Combine(_workDir, "project.vlp");
        File.WriteAllBytes(source, ZipBytes(("../../escape/Current_OPLC.udb", JetHeader)));

        string extracted;
        using (var database = TemporaryDatabase.Create(source))
        {
            extracted = database.FilePath;
            Assert.Equal("project.udb", Path.GetFileName(extracted));
            Assert.Equal(JetHeader, File.ReadAllBytes(extracted));
        }

        Assert.False(File.Exists(extracted));
    }

    [Fact]
    public void Create_FromJetFile_CopiesDatabase()
    {
        var source = Path.Combine(_workDir, "routine.vlx");
        File.WriteAllBytes(source, JetHeader);

        using var database = TemporaryDatabase.Create(source);

        Assert.NotEqual(source, database.FilePath);
        Assert.Equal(JetHeader, File.ReadAllBytes(database.FilePath));
    }

    [Fact]
    public void Create_RejectsUnknownFile_AndZipWithoutDatabase()
    {
        var unknown = Path.Combine(_workDir, "notes.vlp");
        File.WriteAllText(unknown, "not a project");
        var emptyZip = Path.Combine(_workDir, "empty.vlp");
        File.WriteAllBytes(emptyZip, ZipBytes(("Info.xml", [1])));

        Assert.Throws<InvalidDataException>(() => TemporaryDatabase.Create(unknown));
        Assert.Throws<InvalidDataException>(() => TemporaryDatabase.Create(emptyZip));
    }

    private static ZipArchive OpenZip(params (string Name, byte[] Content)[] entries) =>
        new(new MemoryStream(ZipBytes(entries)), ZipArchiveMode.Read);

    private static byte[] ZipBytes(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
