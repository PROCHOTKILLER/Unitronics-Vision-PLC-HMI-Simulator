using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Pcom;

/// <summary>
/// Serwer TCP protokołu PCOM (ASCII) udostępniający pamięć symulatora zewnętrznym narzędziom.
/// </summary>
/// <remarks>
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// PCOM nie ma uwierzytelnienia — domyślny nasłuch na 127.0.0.1 (zob. <see cref="PcomServerOptions.BindAddress"/>).
/// Limity: <see cref="PcomServerOptions.MaxClients"/> klientów, ramka do <see cref="PcomServerOptions.MaxFrameLength"/> bajtów,
/// zamknięcie po <see cref="PcomServerOptions.IdleTimeoutSeconds"/> sekundach bezczynności.
/// </remarks>
public sealed class PcomServer : IAsyncDisposable
{
    private readonly PcomServerOptions _options;
    private readonly PcomAsciiCommandHandler _handler;
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private readonly Lock _stateGate = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _clientCount;
    private int _clientSequence;

    public PcomServer(PlcMemory memory, PcomServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _handler = new PcomAsciiCommandHandler(memory, options.PlcName);
        _handler.ControlRequested += (_, command) => ControlRequested?.Invoke(this, command);
    }

    /// <summary>Komunikaty diagnostyczne (połączenia, błędy protokołu); wywoływane z wątków puli.</summary>
    public event EventHandler<string>? Log;

    /// <summary>Żądanie Stop/Run/Reset z polecenia CCS/CCR/CCI lub ST; serwer nie ma dostępu do silnika, więc reaguje właściciel.</summary>
    public event EventHandler<PcomControlCommand>? ControlRequested;

    /// <summary>Adres, na którym serwer nasłuchuje (po starcie; zawiera port przydzielony przy Port = 0).</summary>
    public IPEndPoint? LocalEndPoint { get; private set; }

    public int ConnectedClients => Volatile.Read(ref _clientCount);

    public bool IsRunning
    {
        get
        {
            lock (_stateGate)
            {
                return _listener is not null;
            }
        }
    }

    /// <summary>Rozpoczyna nasłuch. Token służy tylko do przerwania samego startu — czas życia serwera kończy <see cref="StopAsync"/>.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateGate)
        {
            if (_listener is not null)
            {
                throw new InvalidOperationException("Serwer PCOM już działa.");
            }

            var listener = new TcpListener(_options.ToEndPoint());
            listener.Start();

            var cts = new CancellationTokenSource();
            var token = cts.Token;
            _cts = cts;
            _listener = listener;
            LocalEndPoint = (IPEndPoint)listener.LocalEndpoint;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, token), CancellationToken.None);
        }

        Raise($"PCOM: nasłuch na {LocalEndPoint}.");
        return Task.CompletedTask;
    }

    /// <summary>Zatrzymuje nasłuch, zamyka klientów i czeka na zakończenie ich zadań.</summary>
    public async Task StopAsync()
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        Task? acceptLoop;
        lock (_stateGate)
        {
            listener = _listener;
            cts = _cts;
            acceptLoop = _acceptLoop;
            _listener = null;
            _cts = null;
            _acceptLoop = null;
        }

        if (listener is null)
        {
            return;
        }

        await cts!.CancelAsync().ConfigureAwait(false);
        listener.Stop();

        if (acceptLoop is not null)
        {
            await acceptLoop.ConfigureAwait(false);
        }

        await Task.WhenAll(_clients.Values).ConfigureAwait(false);
        _clients.Clear();
        cts.Dispose();
        LocalEndPoint = null;
        Raise("PCOM: serwer zatrzymany.");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        var maxClients = Math.Max(1, _options.MaxClients);

        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
                // Błąd pojedynczego accept (np. reset w trakcie handshake) nie może zabić nasłuchu; krótka pauza chroni przed pętlą.
                Raise($"PCOM: błąd accept: {ex.SocketErrorCode}.");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            if (Interlocked.Increment(ref _clientCount) > maxClients)
            {
                Interlocked.Decrement(ref _clientCount);
                Raise($"PCOM: odrzucono połączenie — limit klientów ({maxClients}).");
                client.Dispose();
                continue;
            }

            var id = Interlocked.Increment(ref _clientSequence);
            var task = Task.Run(() => HandleClientAsync(client, token), CancellationToken.None);
            _clients[id] = task;
            _ = task.ContinueWith(_ => _clients.TryRemove(id, out Task? _), TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client)
        {
            var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
            Raise($"PCOM: połączono {remote}.");
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                using var buffered = new BufferedStream(stream, PcomServerOptions.MaxFrameLength);

                while (!serverToken.IsCancellationRequested)
                {
                    PcomFrame? frame;
                    using (var idle = CancellationTokenSource.CreateLinkedTokenSource(serverToken))
                    {
                        if (_options.IdleTimeoutSeconds > 0)
                        {
                            idle.CancelAfter(TimeSpan.FromSeconds(_options.IdleTimeoutSeconds));
                        }

                        frame = await PcomFrameReader.ReadAsync(buffered, PcomServerOptions.MaxFrameLength, idle.Token).ConfigureAwait(false);
                    }

                    if (frame is null)
                    {
                        break;
                    }

                    var response = _handler.Handle(frame.Text);
                    await stream.WriteAsync(frame.BuildResponse(response), serverToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!serverToken.IsCancellationRequested)
            {
                Raise($"PCOM: {remote} rozłączony po {_options.IdleTimeoutSeconds} s bezczynności.");
            }
            catch (OperationCanceledException)
            {
                // [INTENCJA I POWÓD DZIAŁANIA]
                // Anulowanie tokenu serwera to normalne zatrzymanie (StopAsync) — bez wpisu o błędzie.
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or SocketException)
            {
                Raise($"PCOM: {remote} — {ex.Message}");
            }
#pragma warning disable CA1031 // Błąd jednego klienta nie może przerwać serwera ani zadania zatrzymania.
            catch (Exception ex)
            {
                Raise($"PCOM: {remote} — nieoczekiwany błąd: {ex.Message}");
            }
#pragma warning restore CA1031
            finally
            {
                Interlocked.Decrement(ref _clientCount);
                Raise($"PCOM: rozłączono {remote}.");
            }
        }
    }

    private void Raise(string message) => Log?.Invoke(this, message);
}
