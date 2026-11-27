using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VisionSimulator.App.Services;
using VisionSimulator.App.ViewModels;
using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Pcom;
using VisionSimulator.Core.Profile;
using VisionSimulator.Core.Project;

namespace VisionSimulator.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // [ARCHITEKTURA I ADR]
        // Generic Host daje DI, konfigurację (appsettings.json -> appsettings.Local.json -> zmienne środowiskowe)
        // i logowanie. ContentRoot = katalog exe, aby konfiguracja działała niezależnie od katalogu startowego.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.Configure<PcomServerOptions>(builder.Configuration.GetSection(PcomServerOptions.SectionName));
        builder.Services.Configure<VisiLogicDatabaseOptions>(builder.Configuration.GetSection(VisiLogicDatabaseOptions.SectionName));
        builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection(SimulationOptions.SectionName));

        // [ARCHITEKTURA I ADR]
        // Układ pamięci pochodzi z profilu projektu (Simulation:ProfilePath), więc profil jest wczytywany przed Build().
        // Błędny profil nie blokuje startu — StartupProfile wraca do V350, a przyczyna trafia do dziennika.
        var simulation = builder.Configuration.GetSection(SimulationOptions.SectionName).Get<SimulationOptions>() ?? new SimulationOptions();
        var startupProfile = StartupProfile.Resolve(simulation.ProfilePath, AppContext.BaseDirectory);
        builder.Services.AddSingleton(startupProfile);
        builder.Services.AddSingleton(startupProfile.Layout);
        builder.Services.AddSingleton<PlcMemory>();

        // [ARCHITEKTURA I ADR]
        // Kolejność rejestracji IScanStep = kolejność wykonania w skanie (PlcEngine pobiera GetServices<IScanStep>()):
        // symulacja obiektu (wejścia z wyjść poprzedniego skanu) -> wymuszenia wejść -> bity systemowe -> numer ekranu
        // HMI (SI 252/251) -> drabinka -> wymuszenia wyjść -> skoki ekranów HMI. Wymuszenia po symulacji obiektu mają
        // pierwszeństwo przed jej wynikiem; skoki ekranów na końcu widzą bity po drabince i wymuszeniach.
        builder.Services.AddSingleton<PlantSimulationStep>();
        builder.Services.AddSingleton<ForceTable>();
        builder.Services.AddSingleton<HmiState>();
        builder.Services.AddSingleton<HmiDisplayStatusStep>();
        builder.Services.AddSingleton<HmiJumpStep>();
        builder.Services.AddSingleton<LadderInterpreter>();
        builder.Services.AddSingleton<SystemBitsStep>();
        builder.Services.AddSingleton<IScanStep>(sp => sp.GetRequiredService<PlantSimulationStep>());
        builder.Services.AddSingleton<IScanStep>(sp => sp.GetRequiredService<ForceTable>().BeforeLogic);
        builder.Services.AddSingleton<IScanStep>(sp => sp.GetRequiredService<SystemBitsStep>());
        builder.Services.AddSingleton<IScanStep>(sp => sp.GetRequiredService<HmiDisplayStatusStep>());
        builder.Services.AddSingleton<IScanStep>(sp => sp.GetRequiredService<LadderInterpreter>());
        builder.Services.AddSingleton<IScanStep>(sp => sp.GetRequiredService<ForceTable>().AfterLogic);
        builder.Services.AddSingleton<IScanStep>(sp => sp.GetRequiredService<HmiJumpStep>());
        builder.Services.AddSingleton(sp => new PlcEngine(sp.GetRequiredService<PlcMemory>(), sp.GetServices<IScanStep>()));

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Interwał skanu z konfiguracji (domyślnie 25 ms, typowo dla V350), co najmniej 1 ms.
        builder.Services.AddSingleton(sp => new ScanLoop(
            sp.GetRequiredService<PlcEngine>(),
            TimeSpan.FromMilliseconds(Math.Max(1, sp.GetRequiredService<IOptions<SimulationOptions>>().Value.ScanIntervalMs))));
        builder.Services.AddSingleton(sp => new PcomServer(
            sp.GetRequiredService<PlcMemory>(),
            sp.GetRequiredService<IOptions<PcomServerOptions>>().Value));
        builder.Services.AddSingleton<SimulatorLog>();
        builder.Services.AddSingleton<SimulatorHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulatorHostedService>());

        builder.Services.AddSingleton<IProjectReader>(sp =>
            new VisiLogicProjectReader(sp.GetRequiredService<IOptions<VisiLogicDatabaseOptions>>().Value));
        builder.Services.AddSingleton<IProjectFileDialog, ProjectFileDialog>();
        builder.Services.AddSingleton<IValueEntryDialog, ValueEntryDialog>();
        builder.Services.AddSingleton<HmiPanelViewModel>();
        builder.Services.AddSingleton<OperandTableViewModel>();
        builder.Services.AddSingleton<ForceTableViewModel>();
        builder.Services.AddSingleton<LadderViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

        if (startupProfile.Plant is { } plant)
        {
            _host.Services.GetRequiredService<PlantSimulationStep>().Load(plant);
        }

        var log = _host.Services.GetRequiredService<SimulatorLog>();
        foreach (var message in startupProfile.Messages)
        {
            log.Add(message);
        }

        await _host.StartAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
