using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Tests;

public sealed class TimerCounterTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void Timer_CountsDownToZeroAndSetsOutput()
    {
        var timer = new PlcTimer();
        timer.LoadPreset(3);

        timer.Update(true, Tick);
        timer.Update(true, Tick);
        Assert.Equal(1, timer.Current);
        Assert.False(timer.Output);

        timer.Update(true, Tick);
        Assert.Equal(0, timer.Current);
        Assert.True(timer.Output);
    }

    [Fact]
    public void Timer_AccumulatesSubUnitRemainders()
    {
        var timer = new PlcTimer();
        timer.LoadPreset(1);

        timer.Update(true, TimeSpan.FromMilliseconds(4));
        timer.Update(true, TimeSpan.FromMilliseconds(4));
        Assert.False(timer.Output);

        timer.Update(true, TimeSpan.FromMilliseconds(4));
        Assert.True(timer.Output);
    }

    [Fact]
    public void Timer_LosingPower_RestoresPreset()
    {
        var timer = new PlcTimer();
        timer.LoadPreset(2);
        timer.Update(true, Tick);

        timer.Update(false, Tick);

        Assert.Equal(2, timer.Current);
        Assert.False(timer.Output);
        Assert.False(timer.Enabled);
    }

    [Fact]
    public void Counter_CountsOnlyRisingEdges_AndSetsOutputAtPreset()
    {
        var counter = new PlcCounter { Preset = 2 };

        counter.Count(true);
        counter.Count(true);
        Assert.Equal(1, counter.Current);
        Assert.False(counter.Output);

        counter.Count(false);
        counter.Count(true);
        Assert.Equal(2, counter.Current);
        Assert.True(counter.Output);

        counter.Reset();
        Assert.Equal(0, counter.Current);
        Assert.False(counter.Output);
    }

    [Fact]
    public void Counter_WithZeroPreset_NeverSetsOutput()
    {
        var counter = new PlcCounter();

        counter.Count(true);

        Assert.Equal(1, counter.Current);
        Assert.False(counter.Output);
    }

    [Fact]
    public void Memory_ExposesTimersAndCountersAsBitsAndNumbers()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var timer = new OperandAddress(OperandType.T, 2);
        var counter = new OperandAddress(OperandType.C, 1);

        memory.GetTimer(2).Current = 40;
        memory.GetTimer(2).Output = true;
        memory.SetNumber(counter, 7);
        memory.SetBit(counter, true);

        Assert.Equal(40, memory.GetNumber(timer));
        Assert.True(memory.GetBit(timer));
        Assert.Equal(7, memory.GetCounter(1).Current);
        Assert.True(memory.GetBit(counter));
    }

    [Fact]
    public void Memory_Clear_ResetsTimersAndCounters()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        memory.GetTimer(0).LoadPreset(10);
        memory.GetCounter(0).Preset = 5;
        memory.GetCounter(0).Count(true);

        memory.Clear();

        Assert.Equal(0, memory.GetTimer(0).Preset);
        Assert.Equal(0, memory.GetCounter(0).Preset);
        Assert.Equal(0, memory.GetCounter(0).Current);
    }

    [Fact]
    public void Memory_ContainsAndCapacity_FollowLayout()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);

        Assert.Equal(PlcMemoryLayout.V350.Timers, memory.GetCapacity(OperandType.T));
        Assert.True(memory.Contains(new OperandAddress(OperandType.T, PlcMemoryLayout.V350.Timers - 1)));
        Assert.False(memory.Contains(new OperandAddress(OperandType.T, PlcMemoryLayout.V350.Timers)));
        Assert.False(memory.Contains(new OperandAddress(OperandType.MI, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.GetTimer(PlcMemoryLayout.V350.Timers));
    }

    [Fact]
    public void HmiState_RaisesEventOnlyOnChange()
    {
        var hmi = new HmiState();
        var changes = 0;
        hmi.DisplayChanged += (_, _) => changes++;

        hmi.ShowDisplay(4);
        hmi.ShowDisplay(4);
        hmi.ShowDisplay(9);

        Assert.Equal(9, hmi.CurrentDisplayId);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void SystemBitsStep_TogglesClockBitsPerHalfPeriod()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var engine = new PlcEngine(memory, [new SystemBitsStep()]);
        var oneSecond = new OperandAddress(OperandType.SB, 3);
        var hundredMs = new OperandAddress(OperandType.SB, 7);
        engine.Run();

        engine.Scan(TimeSpan.FromMilliseconds(500));
        Assert.True(memory.GetBit(oneSecond));
        Assert.False(memory.GetBit(hundredMs));

        engine.Scan(TimeSpan.FromMilliseconds(500));
        Assert.False(memory.GetBit(oneSecond));

        engine.Scan(TimeSpan.FromMilliseconds(50));
        Assert.True(memory.GetBit(hundredMs));
    }
}
