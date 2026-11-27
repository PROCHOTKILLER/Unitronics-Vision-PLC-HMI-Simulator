using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Pcom;

namespace VisionSimulator.App.Services;

/// <summary>Stan serwera PCOM widoczny w pasku stanu.</summary>
public enum PcomState
{
    Disabled,
    Stopped,
    Running,
    Failed,
}

/// <summary>Uruchamia serwer PCOM razem z hostem i zatrzymuje skan oraz PCOM przy zamknięciu aplikacji.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Pętla skanu nie startuje sama — RUN wydaje użytkownik albo klient PCOM. Zatrzymanie: najpierw skan,
/// potem PCOM, aby żaden klient nie zapisywał do pamięci po zakończeniu skanu.
/// </remarks>
public sealed class SimulatorHostedService : IHostedService
{
    private readonly PcomServer _pcom;
    private readonly ScanLoop _scanLoop;
    private readonly PcomServerOptions _options;
    private readonly SimulatorLog _log;
    private volatile PcomState _state = PcomState.Stopped;

    public SimulatorHostedService(PcomServer pcom, ScanLoop scanLoop, IOptions<PcomServerOptions> options, SimulatorLog log)
    {
        ArgumentNullException.ThrowIfNull(pcom);
        ArgumentNullException.ThrowIfNull(scanLoop);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _pcom = pcom;
        _scanLoop = scanLoop;
        _options = options.Value;
        _log = log;

        _pcom.Log += (_, message) => _log.Add(message);
        _scanLoop.Faulted += (_, ex) => _log.Add($"Skan: awaria — {ex.Message}");
    }

    public PcomState State => _state;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _state = PcomState.Disabled;
            _log.Add("PCOM: wyłączony w konfiguracji (Pcom:Enabled = false).");
            return;
        }

        try
        {
            await _pcom.StartAsync(cancellationToken).ConfigureAwait(false);
            _state = PcomState.Running;
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Zajęty port albo błędny adres w konfiguracji nie może zatrzymać aplikacji; symulator działa bez PCOM.
            _state = PcomState.Failed;
            _log.Add($"PCOM: nie uruchomiono serwera — {ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _scanLoop.StopAsync().ConfigureAwait(false);
        await _pcom.StopAsync().ConfigureAwait(false);
        if (_state == PcomState.Running)
        {
            _state = PcomState.Stopped;
        }
    }
}
