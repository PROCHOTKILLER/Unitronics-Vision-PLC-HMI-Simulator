using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Tests;

public sealed class ScanLoopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Start_RunsScansUntilStopped()
    {
        var engine = new PlcEngine(new PlcMemory(PlcMemoryLayout.V350), []);
        await using var loop = new ScanLoop(engine, TimeSpan.FromMilliseconds(5));

        loop.Start();
        await WaitUntilAsync(() => ReadScanCount(engine) >= 3);
        Assert.True(loop.IsRunning);
        Assert.Equal(PlcRunState.Run, engine.State);

        await loop.StopAsync();
        var stoppedAt = ReadScanCount(engine);
        await Task.Delay(50);

        Assert.False(loop.IsRunning);
        Assert.Equal(PlcRunState.Stop, engine.State);
        Assert.Equal(stoppedAt, ReadScanCount(engine));
    }

    [Fact]
    public async Task Start_WhenAlreadyRunning_IsIgnored()
    {
        var engine = new PlcEngine(new PlcMemory(PlcMemoryLayout.V350), []);
        await using var loop = new ScanLoop(engine, TimeSpan.FromMilliseconds(5));

        loop.Start();
        var exception = Record.Exception(loop.Start);

        Assert.Null(exception);
    }

    [Fact]
    public async Task FailingStep_StopsEngineAndRaisesFaulted()
    {
        var engine = new PlcEngine(new PlcMemory(PlcMemoryLayout.V350), [new ThrowingStep()]);
        await using var loop = new ScanLoop(engine, TimeSpan.FromMilliseconds(5));
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        loop.Faulted += (_, ex) => faulted.TrySetResult(ex);

        loop.Start();
        var exception = await faulted.Task.WaitAsync(Timeout);

        Assert.IsType<InvalidOperationException>(exception);
        Assert.False(loop.IsRunning);
        Assert.Equal(PlcRunState.Stop, engine.State);
    }

    [Fact]
    public async Task DisposeAsync_StopsLoop_AndBlocksRestart()
    {
        var engine = new PlcEngine(new PlcMemory(PlcMemoryLayout.V350), []);
        var loop = new ScanLoop(engine, TimeSpan.FromMilliseconds(5));
        loop.Start();

        await loop.DisposeAsync();

        Assert.False(loop.IsRunning);
        Assert.Throws<ObjectDisposedException>(loop.Start);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveInterval()
    {
        var engine = new PlcEngine(new PlcMemory(PlcMemoryLayout.V350), []);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanLoop(engine, TimeSpan.Zero));
    }

    private static long ReadScanCount(PlcEngine engine)
    {
        lock (engine.Memory.SyncRoot)
        {
            return engine.ScanCount;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Warunek nie został spełniony w limicie czasu.");
            await Task.Delay(5);
        }
    }

    private sealed class ThrowingStep : IScanStep
    {
        public void Execute(PlcMemory memory, ScanContext context) => throw new InvalidOperationException("awaria testowa");
    }
}
