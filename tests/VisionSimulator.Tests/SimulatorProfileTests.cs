using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Profile;

namespace VisionSimulator.Tests;

public sealed class SimulatorProfileTests
{
    private static readonly TimeSpan Scan25 = TimeSpan.FromMilliseconds(25);

    [Fact]
    public void Parse_UnknownField_ThrowsInvalidData()
    {
        var ex = Assert.Throws<InvalidDataException>(() => SimulatorProfileLoader.Parse("""{ "name": "x", "unknownField": 1 }"""));

        Assert.Contains("unknownField", ex.Message);
    }

    [Fact]
    public void Parse_AcceptsCommentsTrailingCommasAndCaseInsensitiveNames()
    {
        var profile = SimulatorProfileLoader.Parse("""
            {
              // komentarz
              "Name": "test",
              "MODEL": "v350",
            }
            """);

        Assert.Equal("test", profile.Name);
        Assert.Equal(PlcMemoryLayout.V350, SimulatorProfileLoader.ResolveLayout(profile));
    }

    [Fact]
    public void ResolveLayout_AppliesOnlyGivenOverrides()
    {
        var profile = SimulatorProfileLoader.Parse("""{ "memory": { "memoryIntegers": 10, "timers": 0 } }""");

        var layout = SimulatorProfileLoader.ResolveLayout(profile);

        Assert.Equal(10, layout.MemoryIntegers);
        Assert.Equal(0, layout.Timers);
        Assert.Equal(PlcMemoryLayout.V350 with { MemoryIntegers = 10, Timers = 0 }, layout);
    }

    [Fact]
    public void ResolveLayout_UnknownModelOrOutOfRangeCount_Throws()
    {
        var profile = SimulatorProfileLoader.Parse("""{ "model": "V999", "memory": { "inputs": -1 } }""");

        var ex = Assert.Throws<InvalidDataException>(() => SimulatorProfileLoader.ResolveLayout(profile));

        Assert.Contains("V999", ex.Message);
        Assert.Contains("memory.inputs", ex.Message);
    }

    [Fact]
    public void Compile_CollectsAllErrorsInOneException()
    {
        var profile = SimulatorProfileLoader.Parse("""
            {
              "memory": { "memoryIntegers": 10 },
              "initialValues": [
                { "address": "MI 20", "value": 1 },
                { "address": "MB 0", "value": 2 },
                { "address": "T 0", "value": 1 },
                { "address": "bzdura", "value": 1 }
              ],
              "digitalFeedback": [
                { "source": "O 0", "target": "MI 0" },
                { "source": "O 0", "target": "I 0", "onDelayMs": -5 }
              ],
              "analogFollowers": [
                { "target": "MI 0" },
                { "target": "MB 0", "sourceBit": "O 0" },
                { "target": "MI 1", "sourceBit": "O 0", "min": 10, "max": 0 }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidDataException>(() => SimulatorProfileLoader.Compile(profile));

        Assert.Contains("initialValues[0].address", ex.Message);
        Assert.Contains("initialValues[1].value", ex.Message);
        Assert.Contains("initialValues[2].address", ex.Message);
        Assert.Contains("initialValues[3].address", ex.Message);
        Assert.Contains("digitalFeedback[0].target", ex.Message);
        Assert.Contains("digitalFeedback[1].onDelayMs", ex.Message);
        Assert.Contains("analogFollowers[0]: wymagane jest dokładnie jedno źródło", ex.Message);
        Assert.Contains("analogFollowers[1].target", ex.Message);
        Assert.Contains("analogFollowers[2]: wymagane min ≤ max", ex.Message);
    }

    [Fact]
    public void Compile_ValidatesAgainstRunningLayoutWhenGiven()
    {
        var profile = SimulatorProfileLoader.Parse("""
            { "memory": { "memoryIntegers": 10 }, "initialValues": [ { "address": "MI 5", "value": 1 } ] }
            """);
        var running = PlcMemoryLayout.V350 with { MemoryIntegers = 4 };

        var ex = Assert.Throws<InvalidDataException>(() => SimulatorProfileLoader.Compile(profile, running));
        var plant = SimulatorProfileLoader.Compile(profile);

        Assert.Contains("MI 5", ex.Message);
        Assert.Equal(10, plant.Layout.MemoryIntegers);
        Assert.Single(plant.InitialValues);
    }

    [Fact]
    public void InitialValues_AreWrittenOnFirstScanAndAfterReset()
    {
        var (engine, _) = Build("""
            { "initialValues": [ { "address": "MI 5", "value": 42 }, { "address": "MB 3", "value": 1 }, { "address": "MF 0", "value": 1.5 } ] }
            """);
        var mi5 = OperandAddress.Parse("MI 5");

        engine.Scan(Scan25);
        Assert.Equal(42, engine.Memory.GetNumber(mi5));
        Assert.True(engine.Memory.GetBit(OperandAddress.Parse("MB 3")));
        Assert.Equal(1.5f, engine.Memory.GetFloat(0));

        engine.Memory.SetNumber(mi5, 7);
        engine.Scan(Scan25);
        Assert.Equal(7, engine.Memory.GetNumber(mi5));

        engine.Reset();
        engine.Run();
        engine.Scan(Scan25);
        Assert.Equal(42, engine.Memory.GetNumber(mi5));
    }

    [Fact]
    public void DigitalFeedback_RespectsOnAndOffDelay()
    {
        var (engine, _) = Build("""
            { "digitalFeedback": [ { "source": "O 0", "target": "I 0", "onDelayMs": 100, "offDelayMs": 50 } ] }
            """);
        var output = OperandAddress.Parse("O 0");
        var input = OperandAddress.Parse("I 0");

        engine.Memory.SetBit(output, true);
        for (var i = 0; i < 3; i++)
        {
            engine.Scan(Scan25);
            Assert.False(engine.Memory.GetBit(input));
        }

        engine.Scan(Scan25);
        Assert.True(engine.Memory.GetBit(input));

        engine.Memory.SetBit(output, false);
        engine.Scan(Scan25);
        Assert.True(engine.Memory.GetBit(input));
        engine.Scan(Scan25);
        Assert.False(engine.Memory.GetBit(input));
    }

    [Fact]
    public void DigitalFeedback_InvertCopiesNegatedSource()
    {
        var (engine, _) = Build("""
            { "digitalFeedback": [ { "source": "O 0", "target": "I 1", "invert": true } ] }
            """);

        engine.Scan(Scan25);

        Assert.True(engine.Memory.GetBit(OperandAddress.Parse("I 1")));
    }

    [Fact]
    public void AnalogFollower_FirstOrderInertiaTowardsBitSetpoint()
    {
        var (engine, _) = Build("""
            { "analogFollowers": [ { "target": "MF 0", "sourceBit": "O 1", "onValue": 100, "offValue": 0, "timeConstantMs": 1000 } ] }
            """);
        engine.Memory.SetBit(OperandAddress.Parse("O 1"), true);

        engine.Scan(TimeSpan.FromSeconds(1));

        Assert.Equal(100 * (1 - Math.Exp(-1)), engine.Memory.GetFloat(0), 3);
    }

    [Fact]
    public void AnalogFollower_SmallStepsAccumulateInIntegerRegister()
    {
        // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
        // Przy τ = 10 s i skanie 25 ms przyrost na skan od 0 do 10 wynosi ok. 0,025 — bez stanu w double rejestr stałby na 0.
        var (engine, _) = Build("""
            { "analogFollowers": [ { "target": "MI 0", "sourceBit": "O 1", "onValue": 10, "timeConstantMs": 10000 } ] }
            """);
        engine.Memory.SetBit(OperandAddress.Parse("O 1"), true);

        for (var i = 0; i < 400; i++)
        {
            engine.Scan(Scan25);
        }

        Assert.Equal(6, engine.Memory.GetNumber(OperandAddress.Parse("MI 0")));
    }

    [Fact]
    public void AnalogFollower_SaturatesToRegisterRangeAndClampsMinMax()
    {
        var (engine, _) = Build("""
            {
              "analogFollowers": [
                { "target": "MI 0", "sourceRegister": "ML 0", "gain": 1000 },
                { "target": "MI 1", "sourceRegister": "ML 0", "gain": 1, "offset": 5, "max": 30 }
              ]
            }
            """);
        engine.Memory.SetNumber(OperandAddress.Parse("ML 0"), 40);

        engine.Scan(Scan25);

        Assert.Equal(short.MaxValue, engine.Memory.GetNumber(OperandAddress.Parse("MI 0")));
        Assert.Equal(30, engine.Memory.GetNumber(OperandAddress.Parse("MI 1")));
    }

    [Fact]
    public void AnalogFollower_ResynchronizesAfterExternalWrite()
    {
        var (engine, _) = Build("""
            { "analogFollowers": [ { "target": "MF 0", "sourceBit": "O 1", "onValue": 100, "timeConstantMs": 1000 } ] }
            """);
        engine.Memory.SetBit(OperandAddress.Parse("O 1"), true);
        var k = 1 - Math.Exp(-1);

        engine.Scan(TimeSpan.FromSeconds(1));
        engine.Memory.SetFloat(0, 10f);
        engine.Scan(TimeSpan.FromSeconds(1));

        Assert.Equal(10 + (90 * k), engine.Memory.GetFloat(0), 3);
    }

    [Fact]
    public void PlantStep_UnloadStopsSimulationAndAddressesOutsideMemoryAreSkipped()
    {
        var profile = SimulatorProfileLoader.Parse("""
            { "memory": { "memoryIntegers": 100 }, "initialValues": [ { "address": "MI 50", "value": 1 } ],
              "digitalFeedback": [ { "source": "O 0", "target": "I 0" } ] }
            """);
        var plant = SimulatorProfileLoader.Compile(profile);
        var memory = new PlcMemory(PlcMemoryLayout.V350 with { MemoryIntegers = 10 });
        var step = new PlantSimulationStep();
        step.Load(plant);
        var engine = new PlcEngine(memory, [step]);
        engine.Run();

        engine.Scan(Scan25);
        Assert.False(memory.Contains(OperandAddress.Parse("MI 50")));

        step.Unload();
        memory.SetBit(OperandAddress.Parse("O 0"), true);
        engine.Scan(Scan25);

        Assert.Null(step.Model);
        Assert.False(memory.GetBit(OperandAddress.Parse("I 0")));
    }

    [Fact]
    public void ExampleProfile_CompilesWithoutErrors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "examples", "profile-example.json");

        var plant = SimulatorProfileLoader.Compile(SimulatorProfileLoader.Load(path));

        Assert.True(plant.HasBehaviour);
    }

    private static (PlcEngine Engine, PlantSimulationStep Step) Build(string json)
    {
        var plant = SimulatorProfileLoader.Compile(SimulatorProfileLoader.Parse(json));
        var step = new PlantSimulationStep();
        step.Load(plant);
        var engine = new PlcEngine(new PlcMemory(plant.Layout), [step]);
        engine.Run();
        return (engine, step);
    }
}
