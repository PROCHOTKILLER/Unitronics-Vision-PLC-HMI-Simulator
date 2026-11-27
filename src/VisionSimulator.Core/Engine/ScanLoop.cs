using System.Diagnostics;

namespace VisionSimulator.Core.Engine;

/// <summary>
/// Pętla skanu na osobnym zadaniu w tle: okresowo wywołuje <see cref="PlcEngine.Scan"/> z rzeczywistym czasem,
/// który upłynął od poprzedniego skanu.
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Każdy skan wykonuje się pod <c>lock (engine.Memory.SyncRoot)</c>. Ta sama blokada obowiązuje wszystkich pozostałych
/// użytkowników pamięci: UI, serwer PCOM oraz <see cref="PlcEngine.Reset"/> i każdy inny dostęp do
/// <see cref="PlcEngine.Memory"/> spoza wątku skanu.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Zdarzenie <see cref="PlcEngine.ScanCompleted"/> jest zgłaszane z wątku skanu wewnątrz blokady — jego obsługa
/// nie może blokować ani przejmować blokad w odwrotnej kolejności.
/// </remarks>
public sealed class ScanLoop : IAsyncDisposable
{
    private readonly PlcEngine _engine;
    private readonly TimeSpan _interval;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _task;
    private volatile bool _isRunning;
    private bool _disposed;

    public ScanLoop(PlcEngine engine, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _engine = engine;
        _interval = interval;
    }

    /// <summary>Czy zadanie skanu działa.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Wyjątek w skanie zatrzymuje pętlę i silnik, a następnie jest zgłaszany tym zdarzeniem
    /// (z wątku w tle) zamiast zabijać proces. Obsługa nie powinna synchronicznie czekać na <see cref="StopAsync"/>
    /// (zdarzenie jest zgłaszane z zadania, na które czeka StopAsync).
    /// </summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>Przełącza silnik w RUN i uruchamia pętlę; kolejne wywołania w trakcie pracy są ignorowane.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isRunning)
            {
                return;
            }

            lock (_engine.Memory.SyncRoot)
            {
                _engine.Run();
            }

            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Po awarii (Faulted) poprzednie źródło anulowania nie zostało zwolnione przez StopAsync.
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _isRunning = true;
            var token = _cts.Token;
            _task = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
    }

    /// <summary>Zatrzymuje pętlę, czeka na zakończenie bieżącego skanu i przełącza silnik w STOP.</summary>
    public async Task StopAsync()
    {
        Task? task;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            task = _task;
            cts = _cts;
            _task = null;
            _cts = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        if (task is not null)
        {
            await task.ConfigureAwait(false);
        }

        cts?.Dispose();
        _isRunning = false;
        lock (_engine.Memory.SyncRoot)
        {
            _engine.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            var clock = Stopwatch.StartNew();
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var elapsed = clock.Elapsed;
                clock.Restart();
                lock (_engine.Memory.SyncRoot)
                {
                    _engine.Scan(elapsed);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // [INTENCJA I POWÓD DZIAŁANIA]
            // Zatrzymanie na żądanie (StopAsync) — to nie jest błąd.
        }
#pragma warning disable CA1031 // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)] Dowolny wyjątek ze skanu musi trafić do Faulted, a nie zabić proces.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _isRunning = false;
            lock (_engine.Memory.SyncRoot)
            {
                _engine.Stop();
            }

            Faulted?.Invoke(this, ex);
        }
    }
}
