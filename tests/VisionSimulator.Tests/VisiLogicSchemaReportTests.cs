using VisionSimulator.Core.Project;

namespace VisionSimulator.Tests;

public sealed class VisiLogicSchemaReportTests
{
    private static VisiLogicProject CreateProject() => new(
        SourcePath: @"C:\projekty\poufny-katalog\Demo.vlp",
        ProjectName: "Demo",
        ControllerModel: "V350-35-TR20",
        Modules: [],
        Subroutines: [],
        Operands: [new OperandInfo(1, 10, null, "Opis operandu klienta", "5", true)],
        Displays: [],
        Schema:
        [
            new DatabaseTable("LdrOperands", 12, true, [new DatabaseColumn("OprndType", "Integer")]),
            new DatabaseTable("UnknownTable", -1, false, [new DatabaseColumn("RowID", "Integer"), new DatabaseColumn("Payload", "LongVarBinary")]),
        ],
        Warnings: ["LdrElmsOprs: nieznane kody typów operandów: 13, 14."]);

    [Fact]
    public void Format_ListsTablesColumnsAndWarnings()
    {
        var report = VisiLogicSchemaReport.Format(CreateProject(), "X64 | Microsoft.ACE.OLEDB.16.0", DateTimeOffset.UnixEpoch);

        Assert.Contains("# Schemat bazy VisiLogic: Demo.vlp", report, StringComparison.Ordinal);
        Assert.Contains("| LdrOperands | 12 | 1 | tak |", report, StringComparison.Ordinal);
        Assert.Contains("| UnknownTable | ? | 2 | nie |", report, StringComparison.Ordinal);
        Assert.Contains("`Payload` LongVarBinary", report, StringComparison.Ordinal);
        Assert.Contains("nieznane kody typów operandów: 13, 14", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_DoesNotExposeProjectContentOrDirectory()
    {
        var report = VisiLogicSchemaReport.Format(CreateProject(), "X86", DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain("poufny-katalog", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Opis operandu klienta", report, StringComparison.Ordinal);
        Assert.DoesNotContain("### LdrOperands", report, StringComparison.Ordinal);
    }
}
