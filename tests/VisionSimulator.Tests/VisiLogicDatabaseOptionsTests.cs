using VisionSimulator.Core.Project;

namespace VisionSimulator.Tests;

public sealed class VisiLogicDatabaseOptionsTests
{
    [Theory]
    [InlineData(null, false, VisiLogicDatabaseOptions.JetProvider)]
    [InlineData("", true, VisiLogicDatabaseOptions.AceProvider)]
    [InlineData("  ", false, VisiLogicDatabaseOptions.JetProvider)]
    [InlineData(null, true, VisiLogicDatabaseOptions.AceProvider)]
    [InlineData(" Microsoft.ACE.OLEDB.12.0 ", true, "Microsoft.ACE.OLEDB.12.0")]
    [InlineData("Microsoft.ACE.OLEDB.16.0", false, "Microsoft.ACE.OLEDB.16.0")]
    [InlineData("Microsoft.Jet.OLEDB.4.0", false, "Microsoft.Jet.OLEDB.4.0")]
    public void ResolveProvider_ChoosesByArchitectureOrConfiguration(string? configured, bool is64Bit, string expected)
    {
        Assert.Equal(expected, VisiLogicDatabaseOptions.ResolveProvider(configured, is64Bit));
    }

    [Fact]
    public void ResolveProvider_RejectsJetIn64BitProcess()
    {
        Assert.Throws<PlatformNotSupportedException>(
            () => VisiLogicDatabaseOptions.ResolveProvider("microsoft.jet.oledb.4.0", is64BitProcess: true));
    }

    [Fact]
    public void CreateConnection_UsesResolvedProvider_AndPasswordProperty()
    {
        var options = new VisiLogicDatabaseOptions { Provider = "Microsoft.ACE.OLEDB.16.0", Password = "test-only" };

        using var connection = options.CreateConnection(@"C:\temp\project.udb");

        Assert.Equal("Microsoft.ACE.OLEDB.16.0", connection.Provider);
        Assert.Contains("Jet OLEDB:Database Password", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Share Deny None", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EffectivePassword_FallsBackToVendorDefault(string? configured)
    {
        var options = new VisiLogicDatabaseOptions { Password = configured };

        Assert.False(options.IsPasswordOverridden);
        Assert.Equal(VisiLogicDatabaseOptions.DefaultPassword, options.EffectivePassword);
    }

    [Fact]
    public void EffectivePassword_UsesConfiguredOverride()
    {
        var options = new VisiLogicDatabaseOptions { Password = "test-only" };

        Assert.True(options.IsPasswordOverridden);
        Assert.Equal("test-only", options.EffectivePassword);
    }

    [Fact]
    public void CreateConnection_WithoutConfiguredPassword_SetsVendorDefault()
    {
        var options = new VisiLogicDatabaseOptions { Provider = VisiLogicDatabaseOptions.AceProvider };

        using var connection = options.CreateConnection(@"C:\temp\project.udb");
        var builder = new System.Data.OleDb.OleDbConnectionStringBuilder(connection.ConnectionString);

        Assert.Equal(VisiLogicDatabaseOptions.DefaultPassword, builder["Jet OLEDB:Database Password"]);
    }
}
