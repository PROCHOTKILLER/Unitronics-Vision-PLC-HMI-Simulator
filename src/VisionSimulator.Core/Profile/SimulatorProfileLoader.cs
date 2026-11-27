using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Profile;

/// <summary>Wczytuje profil JSON, wyznacza układ pamięci i kompiluje symulację obiektu z pełną walidacją.</summary>
/// <remarks>
/// [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
/// Wszystkie błędy profilu są zbierane i zgłaszane razem jednym <see cref="InvalidDataException"/>, aby użytkownik
/// poprawił plik w jednym podejściu. Nieznane pola JSON są błędem (literówka w nazwie pola nie może przejść po cichu).
/// </remarks>
public static class SimulatorProfileLoader
{
    public const long MaxFileBytes = 4L * 1024 * 1024;
    public const int MaxOperandCount = 65536;
    public const double MaxDelayMs = 24 * 60 * 60 * 1000.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
    };

    private static readonly Dictionary<string, PlcMemoryLayout> KnownModels = new(StringComparer.OrdinalIgnoreCase)
    {
        [PlcMemoryLayout.V350.Model] = PlcMemoryLayout.V350,
    };

    /// <summary>Odczytuje plik profilu (limit rozmiaru <see cref="MaxFileBytes"/>).</summary>
    public static SimulatorProfile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Brak pliku profilu: {path}", path);
        }

        if (info.Length > MaxFileBytes)
        {
            throw new InvalidDataException($"Plik profilu przekracza {MaxFileBytes} bajtów.");
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Deserializuje profil z tekstu JSON.</summary>
    public static SimulatorProfile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<SimulatorProfile>(json, JsonOptions)
                ?? throw new InvalidDataException("Profil JSON jest pusty (null).");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Niepoprawny JSON profilu: {ex.Message}", ex);
        }
    }

    /// <summary>Układ pamięci: model bazowy z nadpisaniami z profilu.</summary>
    public static PlcMemoryLayout ResolveLayout(SimulatorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();
        var layout = ResolveLayout(profile, errors);
        ThrowIfAny(errors);
        return layout;
    }

    /// <summary>
    /// Kompiluje profil. Adresy są sprawdzane względem <paramref name="memoryLayout"/> (układ pamięci, który faktycznie
    /// działa w procesie); gdy null — względem układu wyznaczonego z profilu.
    /// </summary>
    public static PlantModel Compile(SimulatorProfile profile, PlcMemoryLayout? memoryLayout = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<string>();
        var profileLayout = ResolveLayout(profile, errors);
        var capacity = new PlcMemory(memoryLayout ?? profileLayout);

        var initialValues = new List<InitialValue>();
        foreach (var (definition, index) in (profile.InitialValues ?? Array.Empty<InitialValueDefinition>()).Select((d, i) => (d, i)))
        {
            var where = $"initialValues[{index}]";
            if (definition is null)
            {
                errors.Add($"{where}: pusty wpis.");
                continue;
            }

            if (TryAddress(definition.Address, $"{where}.address", capacity, errors, out var address))
            {
                if (address.Type.IsTimerOrCounter())
                {
                    errors.Add($"{where}.address: timery i liczniki nie mają wartości początkowej w profilu ({address}).");
                }
                else if (!double.IsFinite(definition.Value))
                {
                    errors.Add($"{where}.value: wartość musi być skończoną liczbą.");
                }
                else if (address.Type.IsBit() && definition.Value is not (0.0 or 1.0))
                {
                    errors.Add($"{where}.value: operand bitowy {address} przyjmuje tylko 0 lub 1.");
                }
                else
                {
                    initialValues.Add(new InitialValue(address, definition.Value));
                }
            }
        }

        var feedback = new List<DigitalFeedback>();
        foreach (var (definition, index) in (profile.DigitalFeedback ?? Array.Empty<DigitalFeedbackDefinition>()).Select((d, i) => (d, i)))
        {
            var where = $"digitalFeedback[{index}]";
            if (definition is null)
            {
                errors.Add($"{where}: pusty wpis.");
                continue;
            }

            var sourceOk = TryBit(definition.Source, $"{where}.source", capacity, errors, allowTimers: true, out var source);
            var targetOk = TryBit(definition.Target, $"{where}.target", capacity, errors, allowTimers: false, out var target);
            var onOk = TryDelay(definition.OnDelayMs, $"{where}.onDelayMs", errors, out var onDelay);
            var offOk = TryDelay(definition.OffDelayMs, $"{where}.offDelayMs", errors, out var offDelay);
            if (sourceOk && targetOk && onOk && offOk)
            {
                feedback.Add(new DigitalFeedback(source, target, onDelay, offDelay, definition.Invert));
            }
        }

        var analogs = new List<AnalogFollower>();
        foreach (var (definition, index) in (profile.AnalogFollowers ?? Array.Empty<AnalogFollowerDefinition>()).Select((d, i) => (d, i)))
        {
            var where = $"analogFollowers[{index}]";
            if (definition is null)
            {
                errors.Add($"{where}: pusty wpis.");
                continue;
            }

            if (CompileAnalog(definition, where, capacity, errors) is { } analog)
            {
                analogs.Add(analog);
            }
        }

        ThrowIfAny(errors);
        return new PlantModel(
            profile.Name ?? string.Empty,
            profileLayout,
            initialValues,
            feedback,
            analogs);
    }

    private static AnalogFollower? CompileAnalog(AnalogFollowerDefinition definition, string where, PlcMemory capacity, List<string> errors)
    {
        var errorCount = errors.Count;
        var targetOk = TryRegister(definition.Target, $"{where}.target", capacity, errors, out var target);

        var hasBit = !string.IsNullOrWhiteSpace(definition.SourceBit);
        var hasRegister = !string.IsNullOrWhiteSpace(definition.SourceRegister);
        OperandAddress? sourceBit = null;
        OperandAddress? sourceRegister = null;
        if (hasBit == hasRegister)
        {
            errors.Add($"{where}: wymagane jest dokładnie jedno źródło — sourceBit albo sourceRegister.");
        }
        else if (hasBit && TryBit(definition.SourceBit, $"{where}.sourceBit", capacity, errors, allowTimers: true, out var bit))
        {
            sourceBit = bit;
        }
        else if (hasRegister && TryRegister(definition.SourceRegister, $"{where}.sourceRegister", capacity, errors, out var register))
        {
            sourceRegister = register;
        }

        foreach (var (value, name) in new[]
                 {
                     (definition.OnValue, "onValue"), (definition.OffValue, "offValue"),
                     (definition.Gain, "gain"), (definition.Offset, "offset"),
                 })
        {
            if (!double.IsFinite(value))
            {
                errors.Add($"{where}.{name}: wartość musi być skończoną liczbą.");
            }
        }

        TryDelay(definition.TimeConstantMs, $"{where}.timeConstantMs", errors, out var timeConstant);

        var min = definition.Min ?? double.NegativeInfinity;
        var max = definition.Max ?? double.PositiveInfinity;
        if (double.IsNaN(min) || double.IsNaN(max) || min > max)
        {
            errors.Add($"{where}: wymagane min ≤ max.");
        }

        if (!targetOk || errors.Count != errorCount)
        {
            return null;
        }

        return new AnalogFollower(
            target,
            sourceBit,
            definition.OnValue,
            definition.OffValue,
            sourceRegister,
            definition.Gain,
            definition.Offset,
            timeConstant,
            min,
            max);
    }

    private static PlcMemoryLayout ResolveLayout(SimulatorProfile profile, List<string> errors)
    {
        var modelName = string.IsNullOrWhiteSpace(profile.Model) ? PlcMemoryLayout.V350.Model : profile.Model.Trim();
        if (!KnownModels.TryGetValue(modelName, out var layout))
        {
            errors.Add($"model: nieznany model '{modelName}'. Dostępne: {string.Join(", ", KnownModels.Keys)}.");
            layout = PlcMemoryLayout.V350;
        }

        if (profile.Memory is not { } memory)
        {
            return layout;
        }

        int Pick(int? value, int fallback, string name)
        {
            if (value is null)
            {
                return fallback;
            }

            if (value is < 0 or > MaxOperandCount)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"memory.{name}: dozwolony zakres 0..{MaxOperandCount}."));
                return fallback;
            }

            return value.Value;
        }

        return layout with
        {
            Inputs = Pick(memory.Inputs, layout.Inputs, "inputs"),
            Outputs = Pick(memory.Outputs, layout.Outputs, "outputs"),
            MemoryBits = Pick(memory.MemoryBits, layout.MemoryBits, "memoryBits"),
            SystemBits = Pick(memory.SystemBits, layout.SystemBits, "systemBits"),
            FastBits = Pick(memory.FastBits, layout.FastBits, "fastBits"),
            MemoryIntegers = Pick(memory.MemoryIntegers, layout.MemoryIntegers, "memoryIntegers"),
            SystemIntegers = Pick(memory.SystemIntegers, layout.SystemIntegers, "systemIntegers"),
            FastIntegers = Pick(memory.FastIntegers, layout.FastIntegers, "fastIntegers"),
            MemoryLongs = Pick(memory.MemoryLongs, layout.MemoryLongs, "memoryLongs"),
            SystemLongs = Pick(memory.SystemLongs, layout.SystemLongs, "systemLongs"),
            FastLongs = Pick(memory.FastLongs, layout.FastLongs, "fastLongs"),
            DoubleWords = Pick(memory.DoubleWords, layout.DoubleWords, "doubleWords"),
            SystemDoubleWords = Pick(memory.SystemDoubleWords, layout.SystemDoubleWords, "systemDoubleWords"),
            FastDoubleWords = Pick(memory.FastDoubleWords, layout.FastDoubleWords, "fastDoubleWords"),
            MemoryFloats = Pick(memory.MemoryFloats, layout.MemoryFloats, "memoryFloats"),
            Timers = Pick(memory.Timers, layout.Timers, "timers"),
            Counters = Pick(memory.Counters, layout.Counters, "counters"),
        };
    }

    private static bool TryAddress(string? text, string where, PlcMemory capacity, List<string> errors, out OperandAddress address)
    {
        if (!OperandAddress.TryParse(text, out address))
        {
            errors.Add($"{where}: niepoprawny adres operandu '{text}' (przykład: \"MI 10\").");
            return false;
        }

        if (!capacity.Contains(address))
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{where}: adres {address} poza układem pamięci (0..{capacity.GetCapacity(address.Type) - 1})."));
            return false;
        }

        return true;
    }

    private static bool TryBit(string? text, string where, PlcMemory capacity, List<string> errors, bool allowTimers, out OperandAddress address)
    {
        if (!TryAddress(text, where, capacity, errors, out address))
        {
            return false;
        }

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Źródłem może być bit timera/licznika (odczyt), ale celem tylko zwykły bit — stan T/C należy do drabinki.
        if (address.Type.IsBit() || (allowTimers && address.Type.IsTimerOrCounter()))
        {
            return true;
        }

        errors.Add($"{where}: {address} nie jest operandem bitowym.");
        return false;
    }

    private static bool TryRegister(string? text, string where, PlcMemory capacity, List<string> errors, out OperandAddress address)
    {
        if (!TryAddress(text, where, capacity, errors, out address))
        {
            return false;
        }

        if (address.Type.IsBit() || address.Type.IsTimerOrCounter())
        {
            errors.Add($"{where}: {address} nie jest rejestrem (dozwolone MI/SI/XI/ML/SL/XL/DW/SDW/XDW/MF).");
            return false;
        }

        return true;
    }

    private static bool TryDelay(double milliseconds, string where, List<string> errors, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        if (!double.IsFinite(milliseconds) || milliseconds < 0 || milliseconds > MaxDelayMs)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{where}: dozwolony zakres 0..{MaxDelayMs} ms."));
            return false;
        }

        delay = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    private static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new InvalidDataException("Błędy profilu:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e)));
        }
    }
}
