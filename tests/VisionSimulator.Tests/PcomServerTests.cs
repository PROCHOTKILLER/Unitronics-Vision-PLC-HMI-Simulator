using System.Net;
using System.Net.Sockets;
using System.Text;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Pcom;

namespace VisionSimulator.Tests;

public sealed class PcomServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(0, true)]
    [InlineData(20256, true)]
    [InlineData(65535, true)]
    [InlineData(-1, false)]
    [InlineData(65536, false)]
    public void Options_ValidatePortRange(int port, bool valid)
    {
        var options = new PcomServerOptions { Port = port };

        if (valid)
        {
            Assert.Equal(port, options.ToEndPoint().Port);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(options.ToEndPoint);
        }
    }

    [Fact]
    public async Task Server_AnswersIdentifyOverLoopback()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var client = await fixture.ConnectAsync();

        var response = await ExchangeAsync(client, PcomAsciiCodec.BuildRequest("00", "ID", string.Empty));

        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "ID", "virtual"), response);
        Assert.Equal(1, fixture.Server.ConnectedClients);
    }

    [Fact]
    public async Task Server_WritesAndReadsMemory_UnderMemoryLock()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var client = await fixture.ConnectAsync();

        await ExchangeAsync(client, PcomAsciiCodec.BuildRequest("00", "WO", "05000001" + "0102"));
        var read = await ExchangeAsync(client, PcomAsciiCodec.BuildRequest("00", "RO", "05000001"));

        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "RO", "0102"), read);
        lock (fixture.Memory.SyncRoot)
        {
            Assert.Equal(0x0102, fixture.Memory.GetNumber(new OperandAddress(OperandType.MI, 0)));
        }
    }

    [Fact]
    public async Task Server_ReturnsChecksumError_AndKeepsConnection()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var client = await fixture.ConnectAsync();

        var bad = await ExchangeAsync(client, "/00ID00\r");
        var good = await ExchangeAsync(client, PcomAsciiCodec.BuildRequest("00", "ID", string.Empty));

        Assert.Equal(PcomAsciiCodec.BuildError("00", PcomErrorCode.BadChecksum), bad);
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "ID", "virtual"), good);
    }

    [Fact]
    public async Task Server_RejectsProgramDownloadAndUpload()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var client = await fixture.ConnectAsync();

        var download = await ExchangeAsync(client, PcomAsciiCodec.BuildRequest("00", "DP", "00"));
        var upload = await ExchangeAsync(client, PcomAsciiCodec.BuildRequest("00", "UP", "00"));

        Assert.Equal(PcomAsciiCodec.BuildError("00", PcomErrorCode.Unsupported), download);
        Assert.Equal(PcomAsciiCodec.BuildError("00", PcomErrorCode.Unsupported), upload);
    }

    [Fact]
    public async Task Server_EchoesTcpHeaderWithNewLength()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var client = await fixture.ConnectAsync();
        var stream = client.GetStream();
        var payload = Encoding.ASCII.GetBytes(PcomAsciiCodec.BuildRequest("00", "ID", string.Empty));
        var request = new byte[] { 0x12, 0x34, 0x65, 0x00, (byte)payload.Length, 0x00 }.Concat(payload).ToArray();
        using var cts = new CancellationTokenSource(Timeout);

        await stream.WriteAsync(request, cts.Token);
        var header = new byte[6];
        await stream.ReadExactlyAsync(header, cts.Token);
        var expected = Encoding.ASCII.GetBytes(PcomAsciiCodec.BuildResponse("00", "ID", "virtual"));
        var body = new byte[header[4] | (header[5] << 8)];
        await stream.ReadExactlyAsync(body, cts.Token);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x65, 0x00 }, header[..4]);
        Assert.Equal(expected.Length, body.Length);
        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task Server_ClosesConnection_WhenFrameExceedsLimit()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var client = await fixture.ConnectAsync();
        var stream = client.GetStream();
        using var cts = new CancellationTokenSource(Timeout);

        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("/" + new string('A', PcomServerOptions.MaxFrameLength + 100)), cts.Token);
        }
        catch (IOException)
        {
            // Serwer mógł zamknąć połączenie, zanim klient skończył wysyłać.
        }

        Assert.True(await IsClosedAsync(stream, cts.Token));
    }

    [Fact]
    public async Task Server_RejectsClientsBeyondLimit()
    {
        await using var fixture = await ServerFixture.StartAsync(new PcomServerOptions { MaxClients = 1 });
        using var first = await fixture.ConnectAsync();
        await ExchangeAsync(first, PcomAsciiCodec.BuildRequest("00", "ID", string.Empty));
        using var second = await fixture.ConnectAsync();
        using var cts = new CancellationTokenSource(Timeout);

        Assert.True(await IsClosedAsync(second.GetStream(), cts.Token));
        Assert.Equal(1, fixture.Server.ConnectedClients);
    }

    [Fact]
    public async Task Server_ClosesIdleConnections()
    {
        await using var fixture = await ServerFixture.StartAsync(new PcomServerOptions { IdleTimeoutSeconds = 1 });
        using var client = await fixture.ConnectAsync();
        using var cts = new CancellationTokenSource(Timeout);

        Assert.True(await IsClosedAsync(client.GetStream(), cts.Token));
    }

    [Fact]
    public async Task Server_ForwardsControlRequests()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var received = new TaskCompletionSource<PcomControlCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Server.ControlRequested += (_, command) => received.TrySetResult(command);
        using var client = await fixture.ConnectAsync();

        await ExchangeAsync(client, PcomAsciiCodec.BuildRequest("00", "ST", "02"));

        Assert.Equal(PcomControlCommand.Reset, await received.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task StopAsync_ClosesClientsAndReleasesPort()
    {
        var fixture = await ServerFixture.StartAsync();
        using var client = await fixture.ConnectAsync();
        await ExchangeAsync(client, PcomAsciiCodec.BuildRequest("00", "ID", string.Empty));
        using var cts = new CancellationTokenSource(Timeout);

        await fixture.Server.StopAsync();

        Assert.True(await IsClosedAsync(client.GetStream(), cts.Token));
        Assert.Equal(0, fixture.Server.ConnectedClients);
        Assert.False(fixture.Server.IsRunning);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_Throws()
    {
        await using var fixture = await ServerFixture.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Server.StartAsync());
    }

    private static async Task<string> ExchangeAsync(TcpClient client, string request)
    {
        var stream = client.GetStream();
        using var cts = new CancellationTokenSource(Timeout);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cts.Token);

        var received = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one, cts.Token);
            Assert.True(read > 0, "Serwer zamknął połączenie przed końcem odpowiedzi.");
            received.Add(one[0]);
            if (one[0] == (byte)'\r')
            {
                return Encoding.ASCII.GetString([.. received]);
            }
        }
    }

    private static async Task<bool> IsClosedAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        try
        {
            return await stream.ReadAsync(buffer, cancellationToken) == 0;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private sealed class ServerFixture : IAsyncDisposable
    {
        private ServerFixture(PcomServer server, PlcMemory memory)
        {
            Server = server;
            Memory = memory;
        }

        public PcomServer Server { get; }

        public PlcMemory Memory { get; }

        public static async Task<ServerFixture> StartAsync(PcomServerOptions? options = null)
        {
            options ??= new PcomServerOptions();
            options.BindAddress = "127.0.0.1";
            options.Port = 0;

            var memory = new PlcMemory(PlcMemoryLayout.V350);
            var server = new PcomServer(memory, options);
            await server.StartAsync(CancellationToken.None);
            return new ServerFixture(server, memory);
        }

        public async Task<TcpClient> ConnectAsync()
        {
            var client = new TcpClient();
            try
            {
                var endpoint = Server.LocalEndPoint ?? throw new InvalidOperationException("Serwer nie nasłuchuje.");
                using var cts = new CancellationTokenSource(Timeout);
                await client.ConnectAsync(IPAddress.Loopback, endpoint.Port, cts.Token);
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync() => await Server.DisposeAsync();
    }
}
