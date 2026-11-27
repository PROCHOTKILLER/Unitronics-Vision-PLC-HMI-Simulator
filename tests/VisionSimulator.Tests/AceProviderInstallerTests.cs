using System.ComponentModel;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Tests;

public sealed class AceProviderInstallerTests
{
    [Theory]
    [InlineData(VisiLogicDatabaseOptions.AceProvider, true, true)]
    [InlineData("Microsoft.ACE.OLEDB.12.0", true, true)]
    [InlineData(VisiLogicDatabaseOptions.AceProvider, false, false)]
    [InlineData(VisiLogicDatabaseOptions.JetProvider, false, false)]
    public void IsInstallerApplicable_OnlyForAceIn64BitProcess(string provider, bool is64Bit, bool expected)
    {
        Assert.Equal(expected, AceProviderInstaller.IsInstallerApplicable(provider, is64Bit));
    }

    [Theory]
    [InlineData(0, AceInstallResult.Installed)]
    [InlineData(3010, AceInstallResult.InstalledRebootRequired)]
    [InlineData(1603, AceInstallResult.Failed)]
    [InlineData(-1, AceInstallResult.Failed)]
    public void InterpretExitCode_MapsWindowsInstallerCodes(int exitCode, AceInstallResult expected)
    {
        Assert.Equal(expected, AceProviderInstaller.InterpretExitCode(exitCode));
    }

    [Fact]
    public void IsCancelledByUser_RecognizesUacRefusal()
    {
        Assert.True(AceProviderInstaller.IsCancelledByUser(new Win32Exception(1223)));
        Assert.False(AceProviderInstaller.IsCancelledByUser(new Win32Exception(2)));
    }

    [Fact]
    public async Task InstallAsync_MissingInstaller_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");

        await Assert.ThrowsAsync<FileNotFoundException>(() => AceProviderInstaller.InstallAsync(path));
    }

    [Fact]
    public async Task InstallAsync_UnsignedFile_IsRejectedBeforeLaunch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(path, [0x4D, 0x5A, 0x00, 0x00]);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => AceProviderInstaller.InstallAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
