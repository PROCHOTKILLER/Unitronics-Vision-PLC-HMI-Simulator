using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using VisionSimulator.App.Services;
using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Pcom;
using VisionSimulator.Core.Profile;
using VisionSimulator.Core.Project;

namespace VisionSimulator.App.ViewModels;

/// <summary>Wiersz tabeli podprogramów w oknie głównym.</summary>
public sealed record SubroutineRow(int Id, string Name, int Rungs, int Elements);

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan UiRefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly PlcEngine _engine;
    private readonly ScanLoop _scanLoop;
    private readonly PcomServer _pcom;
    private readonly SimulatorHostedService _simulatorHost;
    private readonly LadderInterpreter _ladder;
    private readonly IProjectReader _projectReader;
    private readonly IProjectFileDialog _fileDialog;
    private readonly SimulatorLog _log;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _uiTimer;
    private readonly bool _isPasswordOverridden;
    private readonly VisiLogicDatabaseOptions _database;
    private readonly PlantSimulationStep _plant;
    private readonly StartupProfile _startupProfile;
    private string? _profilePath;

    [ObservableProperty]
    private string _runtimeInfo = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallAceCommand))]
    private bool _isAceMissing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallAceCommand))]
    private bool _isInstallingAce;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private PlcRunState _state;

    [ObservableProperty]
    private long _scanCount;

    [ObservableProperty]
    private long _anomalyCount;

    [ObservableProperty]
    private string? _lastAnomaly;

    [ObservableProperty]
    private string _pcomStatus = "PCOM: …";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadProjectCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _projectSummary = "Brak wczytanego projektu.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportSchemaCommand))]
    private VisiLogicProject? _project;

    [ObservableProperty]
    private string _profileSummary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnloadProfileCommand))]
    private bool _isProfileLoaded;

    public MainViewModel(
        PlcEngine engine,
        ScanLoop scanLoop,
        PcomServer pcom,
        SimulatorHostedService simulatorHost,
        LadderInterpreter ladder,
        IProjectReader projectReader,
        IProjectFileDialog fileDialog,
        SimulatorLog log,
        HmiPanelViewModel hmiPanel,
        OperandTableViewModel operandTable,
        ForceTableViewModel forceTable,
        LadderViewModel ladderView,
        IOptions<VisiLogicDatabaseOptions> database,
        PlantSimulationStep plant,
        StartupProfile startupProfile)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(startupProfile);
        _plant = plant;
        _startupProfile = startupProfile;

        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(scanLoop);
        ArgumentNullException.ThrowIfNull(pcom);
        ArgumentNullException.ThrowIfNull(simulatorHost);
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(projectReader);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(hmiPanel);
        ArgumentNullException.ThrowIfNull(operandTable);
        ArgumentNullException.ThrowIfNull(forceTable);
        ArgumentNullException.ThrowIfNull(ladderView);
        _engine = engine;
        _scanLoop = scanLoop;
        _pcom = pcom;
        _simulatorHost = simulatorHost;
        _ladder = ladder;
        _projectReader = projectReader;
        _fileDialog = fileDialog;
        _log = log;
        Hmi = hmiPanel;
        Operands = operandTable;
        Forces = forceTable;
        LadderView = ladderView;
        Log = log;
        _database = database.Value;
        _isPasswordOverridden = _database.IsPasswordOverridden;
        _dispatcher = Dispatcher.CurrentDispatcher;
        RefreshProviderState();
        RefreshProfileSummary();

        // [OSTRZEŻENIA I KONSEKWENCJE]
        // ControlRequested przychodzi z wątków puli (klient PCOM); polecenia Run/Stop/Reset wykonujemy na wątku UI.
        _pcom.ControlRequested += OnControlRequested;

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Skan działa na osobnym wątku (ScanLoop); ten timer tylko odświeża widok, krótko trzymając blokadę pamięci.
        _uiTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = UiRefreshInterval };
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();
        UpdateFromEngine();
    }

    public HmiPanelViewModel Hmi { get; }

    public OperandTableViewModel Operands { get; }

    public ForceTableViewModel Forces { get; }

    public LadderViewModel LadderView { get; }

    public SimulatorLog Log { get; }

    public ObservableCollection<SubroutineRow> Subroutines { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    [RelayCommand(CanExecute = nameof(CanLoadProject))]
    private async Task LoadProjectAsync()
    {
        var path = _fileDialog.PickProjectFile();
        if (path is null)
        {
            return;
        }

        IsLoading = true;
        ProjectSummary = $"Wczytywanie: {Path.GetFileName(path)}…";
        try
        {
            ShowProject(await _projectReader.ReadAsync(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OleDbException
                                       or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Uszkodzony plik, niepoprawne hasło Jet lub brak providera: komunikat w oknie zamiast zamknięcia aplikacji.
            // Poprzednio wczytany projekt zostaje bez zmian.
            ProjectSummary = Project is null ? "Brak wczytanego projektu." : Describe(Project);
            var hint = ex switch
            {
                OleDbException when _isPasswordOverridden =>
                    " — hasło bazy jest nadpisane w konfiguracji (appsettings.Local.json lub VisiLogicDatabase__Password); " +
                    "usuń nadpisanie, aby użyć domyślnego hasła VisiLogic.",
                PlatformNotSupportedException when IsAceMissing => " — użyj przycisku „Zainstaluj ACE…”.",
                _ => string.Empty,
            };
            var message = $"Błąd wczytywania '{Path.GetFileName(path)}': {ex.Message}{hint}";
            Warnings.Insert(0, message);
            _log.Add(message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLoadProject() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanExportSchema))]
    private async Task ExportSchemaAsync()
    {
        if (Project is not { } project)
        {
            return;
        }

        var path = _fileDialog.PickSchemaReportFile($"{project.ProjectName}-schemat.md");
        if (path is null)
        {
            return;
        }

        try
        {
            var report = VisiLogicSchemaReport.Format(project, RuntimeInfo, DateTimeOffset.Now);
            await File.WriteAllTextAsync(path, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Warnings.Insert(0, $"Zapisano schemat bazy: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Plik otwarty w innym programie lub katalog bez prawa zapisu: komunikat zamiast zamknięcia aplikacji.
            var message = $"Błąd zapisu schematu '{Path.GetFileName(path)}': {ex.Message}";
            Warnings.Insert(0, message);
            _log.Add(message);
        }
    }

    private bool CanExportSchema() => Project is not null;

    [RelayCommand]
    private async Task LoadProfileAsync()
    {
        var path = _fileDialog.PickProfileFile();
        if (path is null)
        {
            return;
        }

        var fileName = Path.GetFileName(path);
        try
        {
            // [ARCHITEKTURA I ADR]
            // Układ pamięci działającego procesu jest stały (singleton PlcMemory), więc adresy profilu walidujemy
            // względem niego. Inny układ w profilu działa dopiero po wpisaniu go do Simulation:ProfilePath i restarcie.
            var layout = _engine.Memory.Layout;
            var (plant, layoutDiffers) = await Task.Run(() =>
            {
                var profile = SimulatorProfileLoader.Load(path);
                var compiled = SimulatorProfileLoader.Compile(profile, layout);
                return (compiled, compiled.Layout != layout);
            });

            _plant.Load(plant);
            _profilePath = path;
            RefreshProfileSummary();

            var message = $"Profil: wczytano '{fileName}'. {StartupProfile.Describe(plant)}";
            if (layoutDiffers)
            {
                message += " | Układ pamięci z profilu różni się od działającego — wpisz ścieżkę do Simulation:ProfilePath i uruchom program ponownie.";
            }

            long scans;
            lock (_engine.Memory.SyncRoot)
            {
                scans = _engine.ScanCount;
            }

            if (scans > 0 && plant.InitialValues.Count > 0)
            {
                message += " | Wartości początkowe zostaną zapisane po RESET.";
            }

            Warnings.Insert(0, message);
            _log.Add(message);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Błędny profil nie zmienia działającej symulacji obiektu; lista błędów trafia do ostrzeżeń i dziennika.
            var message = $"Profil: nie wczytano '{fileName}': {ex.Message}";
            Warnings.Insert(0, message);
            _log.Add(message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsProfileLoaded))]
    private void UnloadProfile()
    {
        _plant.Unload();
        _profilePath = null;
        RefreshProfileSummary();
        _log.Add("Profil: symulacja obiektu wyłączona.");
    }

    private void RefreshProfileSummary()
    {
        var layout = $"układ pamięci: {_engine.Memory.Layout.Model}" +
                     (_startupProfile.Path is null ? " (domyślny)" : $" (z {Path.GetFileName(_startupProfile.Path)})");
        if (_plant.Model is { } model)
        {
            var source = _profilePath ?? _startupProfile.Path;
            var file = source is null ? string.Empty : $"{Path.GetFileName(source)}: ";
            ProfileSummary = $"Profil: {file}{StartupProfile.Describe(model)} | {layout}";
            IsProfileLoaded = true;
        }
        else
        {
            ProfileSummary = $"Profil: symulacja obiektu wyłączona | {layout}";
            IsProfileLoaded = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallAce))]
    private async Task InstallAceAsync()
    {
        var installer = AceProviderInstaller.DefaultInstallerPath;
        if (!File.Exists(installer))
        {
            Warnings.Insert(0,
                $"Brak instalatora {installer}. Pobierz Microsoft Access Database Engine 2016 Redistributable (x64) " +
                $"i zapisz plik {AceProviderInstaller.InstallerFileName} w katalogu '{AceProviderInstaller.RedistDirectory}' obok programu.");
            return;
        }

        IsInstallingAce = true;
        try
        {
            var (result, exitCode) = await AceProviderInstaller.InstallAsync(installer);
            Warnings.Insert(0, result switch
            {
                AceInstallResult.Installed => "Zainstalowano Access Database Engine (x64).",
                AceInstallResult.InstalledRebootRequired => "Zainstalowano Access Database Engine (x64); instalator wymaga ponownego uruchomienia Windows.",
                _ => $"Instalator Access Database Engine zakończył się błędem (kod {exitCode}).",
            });
        }
        catch (Win32Exception ex) when (AceProviderInstaller.IsCancelledByUser(ex))
        {
            Warnings.Insert(0, "Instalacja Access Database Engine anulowana w oknie UAC.");
        }
        catch (Exception ex) when (ex is InvalidDataException or Win32Exception or IOException or InvalidOperationException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Podpis inny niż Microsoft, uszkodzony plik lub brak uprawnień: instalator nie jest uruchamiany.
            Warnings.Insert(0, $"Nie zainstalowano Access Database Engine: {ex.Message}");
        }
        finally
        {
            IsInstallingAce = false;
            RefreshProviderState();
        }
    }

    private bool CanInstallAce() => IsAceMissing && !IsInstallingAce;

    private void RefreshProviderState()
    {
        RuntimeInfo = DescribeRuntime(_database);
        try
        {
            var provider = _database.ResolveProvider();
            IsAceMissing = AceProviderInstaller.IsInstallerApplicable(provider, Environment.Is64BitProcess)
                && !AceProviderInstaller.IsProviderRegistered(provider);
        }
        catch (PlatformNotSupportedException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Jet wpisany ręcznie w procesie x64: instalacja ACE nie naprawi konfiguracji, przyczynę pokazuje pasek stanu.
            IsAceMissing = false;
        }

        if (IsAceMissing)
        {
            RuntimeInfo += " | provider niezainstalowany";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run() => StartScan();

    private bool CanRun() => State != PlcRunState.Run;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync() => await StopScanAsync();

    private bool CanStop() => State == PlcRunState.Run;

    [RelayCommand]
    private async Task ResetAsync() => await ResetScanAsync();

    private void StartScan()
    {
        _scanLoop.Start();
        UpdateFromEngine();
    }

    private async Task StopScanAsync()
    {
        await _scanLoop.StopAsync();
        UpdateFromEngine();
    }

    private async Task ResetScanAsync()
    {
        await _scanLoop.StopAsync();

        // [ARCHITEKTURA I ADR]
        // Reset czyści pamięć, więc wymaga tej samej blokady co skan, UI i serwer PCOM.
        lock (_engine.Memory.SyncRoot)
        {
            _engine.Reset();
        }

        UpdateFromEngine();
    }

    private void OnControlRequested(object? sender, PcomControlCommand command) =>
        _dispatcher.InvokeAsync<Task>(() => HandleControlAsync(command));

    private async Task HandleControlAsync(PcomControlCommand command)
    {
        try
        {
            switch (command)
            {
                case PcomControlCommand.Run:
                    StartScan();
                    break;
                case PcomControlCommand.Stop:
                    await StopScanAsync();
                    break;
                case PcomControlCommand.Reset:
                    await ResetScanAsync();
                    break;
            }

            _log.Add($"PCOM: wykonano polecenie {command}.");
        }
        catch (ObjectDisposedException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Polecenie z klienta PCOM dotarło w trakcie zamykania aplikacji — pomijamy je.
        }
    }

    private void ShowProject(VisiLogicProject project)
    {
        Project = project;
        ProjectSummary = Describe(project);

        Subroutines.Clear();
        foreach (var subroutine in project.Subroutines)
        {
            Subroutines.Add(new SubroutineRow(
                subroutine.Id,
                subroutine.Name,
                subroutine.Rungs.Count,
                subroutine.Rungs.Sum(r => r.Elements.Count)));
        }

        Warnings.Clear();
        foreach (var warning in project.Warnings)
        {
            Warnings.Add(warning);
        }

        LoadLadder(project);
        LadderView.Reload();
        Hmi.SetProject(project);

        // [ARCHITEKTURA I ADR]
        // Tabela operandów potrzebuje układu drabinki (miejsca użycia), więc jest budowana po LoadLadder.
        Operands.SetProject(project, _ladder.GetLayout());
        Forces.SetProject(project);
    }

    private void LoadLadder(VisiLogicProject project)
    {
        try
        {
            _ladder.Load(project);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException
                                       or KeyNotFoundException or ArithmeticException or InvalidCastException or FormatException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Kompilacja drabinki nie powiodła się: program LAD zostaje wyładowany, a ekrany HMI nadal działają.
            _ladder.Unload();
            var message = $"Nie wczytano programu LAD: {ex.Message}";
            Warnings.Add(message);
            _log.Add(message);
            return;
        }

        foreach (var diagnostic in _ladder.LoadDiagnostics)
        {
            Warnings.Add($"LAD: {diagnostic}");
        }

        if (_ladder.UnsupportedElementTypes.Count > 0)
        {
            Warnings.Add($"LAD: nieobsługiwane typy elementów: {string.Join(", ", _ladder.UnsupportedElementTypes)}.");
        }
    }

    private static string Describe(VisiLogicProject project)
    {
        var modules = project.Modules.Count == 0 ? "brak" : string.Join(", ", project.Modules.Select(m => m.Model));
        return string.Format(
            CultureInfo.CurrentCulture,
            "{0} | sterownik: {1} | moduły: {2} | podprogramy: {3:N0}, rungi: {4:N0}, elementy LAD: {5:N0} | operandy z opisem: {6:N0} | ekrany HMI: {7:N0}",
            project.ProjectName,
            project.ControllerModel.Length == 0 ? "?" : project.ControllerModel,
            modules,
            project.Subroutines.Count,
            project.RungCount,
            project.ElementCount,
            project.DescribedOperandCount,
            project.Displays.Count);
    }

    private static string DescribeRuntime(VisiLogicDatabaseOptions database)
    {
        var architecture = RuntimeInformation.ProcessArchitecture;

        // [OSTRZEŻENIA I KONSEKWENCJE]
        // Pokazujemy wyłącznie źródło hasła, nigdy jego treść ani długość.
        var password = database.IsPasswordOverridden ? "hasło: z konfiguracji" : "hasło: domyślne VisiLogic";
        try
        {
            return $"{architecture} | {database.ResolveProvider()} | {password}";
        }
        catch (PlatformNotSupportedException ex)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Provider Jet ustawiony ręcznie w procesie x64: aplikacja startuje, a pasek stanu wskazuje przyczynę.
            return $"{architecture} | {ex.Message} | {password}";
        }
    }

    private void OnUiTick(object? sender, EventArgs e) => UpdateFromEngine();

    private void UpdateFromEngine()
    {
        long scans;
        PlcRunState state;
        HmiScreen? screen;
        IReadOnlyList<KeyValuePair<OperandRow, string>>? operands;
        IReadOnlyList<KeyValuePair<ForceRow, string>>? forces;
        LadderSubroutineState? ladder;

        // [OPTYMALIZACJA WYDAJNOŚCI (PERFORMANCE)]
        // Pod blokadą tylko odczyt danych do struktur pośrednich; wszystkie zapisy właściwości UI poza nią,
        // aby wątek skanu nie czekał na interfejs.
        lock (_engine.Memory.SyncRoot)
        {
            scans = _engine.ScanCount;
            state = _engine.State;
            screen = Hmi.CaptureScreen();
            operands = Operands.CaptureValues();
            forces = Forces.CaptureValues();
            ladder = LadderView.CaptureState();
        }

        State = state;
        ScanCount = scans;
        Hmi.ApplyRunState(state == PlcRunState.Run);
        Hmi.ApplyScreen(screen);
        OperandTableViewModel.ApplyValues(operands);
        ForceTableViewModel.ApplyValues(forces);
        LadderView.ApplyState(ladder);
        AnomalyCount = _ladder.AnomalyCount;
        LastAnomaly = _ladder.LastAnomaly;
        PcomStatus = DescribePcom();
    }

    private string DescribePcom() => _simulatorHost.State switch
    {
        PcomState.Disabled => "PCOM: wyłączony",
        PcomState.Failed => "PCOM: błąd",
        PcomState.Running when _pcom.LocalEndPoint is { } endpoint =>
            string.Create(CultureInfo.InvariantCulture, $"PCOM: {endpoint} | klienci: {_pcom.ConnectedClients}"),
        _ => "PCOM: zatrzymany",
    };
}
