using System.ComponentModel;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace VisionSimulator.Core.Project;

/// <summary>Wynik instalatora Access Database Engine (kod wyjścia Windows Installer).</summary>
public enum AceInstallResult
{
    Installed,
    InstalledRebootRequired,
    Failed,
}

/// <summary>
/// Wykrywanie providera ACE i instalacja Microsoft Access Database Engine 2016 Redistributable (x64)
/// z katalogu <c>redist</c> obok programu.
/// </summary>
public static class AceProviderInstaller
{
    public const string RedistDirectory = "redist";
    public const string InstallerFileName = "accessdatabaseengine_X64.exe";
    public const string ExpectedPublisher = "Microsoft Corporation";

    private const int ErrorSuccess = 0;
    private const int ErrorSuccessRebootRequired = 3010;
    private const int ErrorCancelled = 1223;

    public static string DefaultInstallerPath => Path.Combine(AppContext.BaseDirectory, RedistDirectory, InstallerFileName);

    // [ARCHITEKTURA I ADR]
    // Instalator w redist jest tylko w wersji x64 i dotyczy wyłącznie providerów ACE; wersja x86 używa Jet z Windows.
    public static bool IsInstallerApplicable(string provider, bool is64BitProcess) =>
        is64BitProcess && provider.StartsWith("Microsoft.ACE.", StringComparison.OrdinalIgnoreCase);

    public static AceInstallResult InterpretExitCode(int exitCode) => exitCode switch
    {
        ErrorSuccess => AceInstallResult.Installed,
        ErrorSuccessRebootRequired => AceInstallResult.InstalledRebootRequired,
        _ => AceInstallResult.Failed,
    };

    public static bool IsCancelledByUser(Win32Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.NativeErrorCode == ErrorCancelled;
    }

    /// <summary>Sprawdza, czy provider OLE DB jest zarejestrowany dla architektury bieżącego procesu.</summary>
    public static bool IsProviderRegistered(string provider)
    {
        try
        {
            using var sources = new OleDbEnumerator().GetElements();
            return sources.Rows
                .Cast<DataRow>()
                .Any(r => string.Equals(
                    Convert.ToString(r["SOURCES_NAME"], CultureInfo.InvariantCulture),
                    provider,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is OleDbException or InvalidOperationException or COMException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Uszkodzony rejestr providerów: traktujemy jak brak; właściwy błąd pokaże próba otwarcia bazy.
            return false;
        }
    }

    /// <summary>
    /// Weryfikuje podpis Authenticode instalatora (zaufany łańcuch + wydawca Microsoft) i uruchamia go
    /// z podniesionymi uprawnieniami w trybie <c>/quiet</c>.
    /// </summary>
    /// <exception cref="FileNotFoundException">Brak pliku instalatora.</exception>
    /// <exception cref="InvalidDataException">Podpis nieprawidłowy lub wydawca inny niż Microsoft.</exception>
    /// <exception cref="Win32Exception">Odmowa UAC (<see cref="IsCancelledByUser"/>) lub błąd uruchomienia.</exception>
    public static async Task<(AceInstallResult Result, int ExitCode)> InstallAsync(string installerPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Nie znaleziono instalatora Access Database Engine.", installerPath);
        }

        VerifyPublisherSignature(installerPath);

        // [OSTRZEŻENIA I KONSEKWENCJE]
        // /quiet jest wymagane: przy zainstalowanym 32-bit Office instalator x64 w trybie interaktywnym odmawia instalacji.
        // Uruchomienie "runas" wywołuje UAC — bez zgody administratora sterownik nie zostanie zainstalowany.
        var startInfo = new ProcessStartInfo(installerPath, "/quiet")
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Nie udało się uruchomić instalatora Access Database Engine.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (InterpretExitCode(process.ExitCode), process.ExitCode);
    }

    private static void VerifyPublisherSignature(string path)
    {
        // [INTENCJA I POWÓD DZIAŁANIA]
        // Plik uruchamiany z uprawnieniami administratora musi mieć ważny podpis Microsoft, inaczej podmiana
        // pliku w katalogu redist dawałaby wykonanie dowolnego kodu jako administrator.
        var trust = NativeMethods.VerifyFile(path);
        if (trust != 0)
        {
            throw new InvalidDataException(
                $"Instalator '{Path.GetFileName(path)}' nie ma ważnego podpisu cyfrowego (WinVerifyTrust 0x{trust:X8}).");
        }

#pragma warning disable SYSLIB0057 // Brak zamiennika w X509CertificateLoader dla podpisów Authenticode.
        using var signature = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        using var signer = new X509Certificate2(signature);
        var publisher = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        if (!string.Equals(publisher, ExpectedPublisher, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Instalator '{Path.GetFileName(path)}' jest podpisany przez '{publisher}', oczekiwano '{ExpectedPublisher}'.");
        }
    }

    private static class NativeMethods
    {
        private const uint WtdUiNone = 2;
        private const uint WtdRevokeNone = 0;
        private const uint WtdChoiceFile = 1;
        private const uint WtdStateActionVerify = 1;
        private const uint WtdStateActionClose = 2;

        // WINTRUST_ACTION_GENERIC_VERIFY_V2
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustFileInfo
        {
            public uint CbStruct;
            public IntPtr FilePath;
            public IntPtr File;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint CbStruct;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProvFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

        /// <summary>Zwraca 0, gdy podpis Authenticode pliku jest ważny i łańcuch zaufany.</summary>
        public static int VerifyFile(string path)
        {
            // [OSTRZEŻENIA I KONSEKWENCJE]
            // Bez sprawdzania odwołań certyfikatów (WTD_REVOKE_NONE): stacje warsztatowe bywają offline,
            // a sprawdzanie CRL kończyłoby się wtedy błędem mimo poprawnego podpisu.
            var filePath = Marshal.StringToHGlobalUni(path);
            var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                var fileInfo = new WinTrustFileInfo { CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = filePath };
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

                var data = new WinTrustData
                {
                    CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    UiChoice = WtdUiNone,
                    RevocationChecks = WtdRevokeNone,
                    UnionChoice = WtdChoiceFile,
                    FileInfo = fileInfoPtr,
                    StateAction = WtdStateActionVerify,
                };

                var action = GenericVerifyV2;
                var noWindow = new IntPtr(-1);
                var result = WinVerifyTrust(noWindow, ref action, ref data);

                data.StateAction = WtdStateActionClose;
                _ = WinVerifyTrust(noWindow, ref action, ref data);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
                Marshal.FreeHGlobal(filePath);
            }
        }
    }
}
