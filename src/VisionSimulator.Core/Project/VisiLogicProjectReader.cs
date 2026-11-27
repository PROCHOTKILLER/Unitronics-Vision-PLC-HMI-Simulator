using System.Data;
using System.Data.OleDb;
using System.Globalization;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Project;

/// <summary>Czyta projekt VisiLogic z bazy Jet 4.0 (x86: provider Jet, x64: provider ACE).</summary>
public sealed class VisiLogicProjectReader : IProjectReader
{
    /// <summary>Tabele, które czyta symulator; pozostałe trafiają do ostrzeżeń i eksportu schematu.</summary>
    public static readonly IReadOnlySet<string> ReadTables = new HashSet<string>(
        ["HW_Elements", "LdrOperands", "LdrFunctions", "LdrStrips", "LdrElements", "LdrElmsOprs", "Pages", "HMI_Elements", "HMI_Operands", "HMIJumps"],
        StringComparer.OrdinalIgnoreCase);

    private readonly VisiLogicDatabaseOptions _options;

    public VisiLogicProjectReader(VisiLogicDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Task<VisiLogicProject> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // [ARCHITEKTURA I ADR]
        // Provider OLE DB nie ma prawdziwego API asynchronicznego; odczyt idzie na wątek puli,
        // żeby nie blokować UI. Anulowanie jest sprawdzane między tabelami.
        return Task.Run(() => Read(path, cancellationToken), cancellationToken);
    }

    private VisiLogicProject Read(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Nie znaleziono pliku projektu.", path);
        }

        var warnings = new List<string>();
        using var database = TemporaryDatabase.Create(path);
        using var connection = _options.OpenConnection(database.FilePath, readOnly: true);

        var (controller, modules) = TryRead<(string, IReadOnlyList<HardwareModule>)>(
            warnings, "HW_Elements", () => ReadHardware(connection), (string.Empty, Array.Empty<HardwareModule>()));
        cancellationToken.ThrowIfCancellationRequested();

        var operands = TryRead(warnings, "LdrOperands", () => ReadOperandRows(connection), []);
        cancellationToken.ThrowIfCancellationRequested();

        var subroutines = TryRead(warnings, "LdrFunctions/LdrStrips/LdrElements/LdrElmsOprs", () => ReadLadder(connection, operands, warnings), []);
        cancellationToken.ThrowIfCancellationRequested();

        var displays = TryRead(warnings, "Pages/HMI_Elements", () => ReadDisplays(connection, warnings), []);
        cancellationToken.ThrowIfCancellationRequested();

        // [ZADANIA DO WYKONANIA (TODO)]
        // Data Tables (receptury): tabele rozpoznawane z eksportu schematu, odczyt w kolejnym kroku etapu 1.
        var schema = TryRead(warnings, "schemat bazy", () => ReadSchema(connection), []);
        var unread = schema.Where(t => !t.IsReadBySimulator).Select(t => t.Name).ToList();
        if (unread.Count > 0)
        {
            warnings.Add($"Tabele nieobsługiwane przez symulator ({unread.Count}): {string.Join(", ", unread)}.");
        }

        return new VisiLogicProject(
            path,
            Path.GetFileNameWithoutExtension(path),
            controller,
            modules,
            subroutines,
            operands.Where(o => !VisiLogicOperandCodes.IsConstant(o.TypeCode)).ToList(),
            displays,
            schema,
            warnings);
    }

    private static List<DatabaseTable> ReadSchema(OleDbConnection connection)
    {
        // [INTENCJA I POWÓD DZIAŁANIA]
        // Schemat (nazwy tabel, kolumn, typy, liczby wierszy) pozwala rozpoznać nieopisane tabele VisiLogic
        // (np. Data Tables) bez eksportowania wartości z projektu klienta.
        var tables = new List<DatabaseTable>();
        using var tableInfo = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, [null, null, null, "TABLE"]);
        if (tableInfo is null)
        {
            return tables;
        }

        foreach (DataRow row in tableInfo.Rows)
        {
            var name = Convert.ToString(row["TABLE_NAME"], CultureInfo.InvariantCulture) ?? string.Empty;

            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Nazwa trafia do SQL w nawiasach [ ]; Jet nie ma sekwencji ucieczki dla ']', więc taką tabelę pomijamy.
            if (name.Length == 0 || name.Contains(']', StringComparison.Ordinal))
            {
                continue;
            }

            tables.Add(new DatabaseTable(name, CountRows(connection, name), ReadTables.Contains(name), ReadColumns(connection, name)));
        }

        return tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<DatabaseColumn> ReadColumns(OleDbConnection connection, string table)
    {
        using var columnInfo = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, [null, null, table, null]);
        if (columnInfo is null)
        {
            return [];
        }

        return columnInfo.Rows
            .Cast<DataRow>()
            .OrderBy(r => Convert.ToInt64(r["ORDINAL_POSITION"], CultureInfo.InvariantCulture))
            .Select(r => new DatabaseColumn(
                Convert.ToString(r["COLUMN_NAME"], CultureInfo.InvariantCulture) ?? string.Empty,
                ((OleDbType)Convert.ToInt32(r["DATA_TYPE"], CultureInfo.InvariantCulture)).ToString()))
            .ToList();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Nazwa tabeli pochodzi ze schematu bazy, nie od użytkownika; nazwy z ']' są odrzucane w ReadSchema.")]
    private static int CountRows(OleDbConnection connection, string table)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (OleDbException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Tabela bez uprawnień odczytu lub połączona zewnętrznie: schemat zostaje, liczba wierszy nieznana.
            return -1;
        }
    }

    private static T TryRead<T>(List<string> warnings, string tables, Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (OleDbException ex)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Plik .vlx (sam podprogram) nie ma tabel sprzętu ani ekranów; brak tabeli to ostrzeżenie, nie błąd.
            warnings.Add($"{tables}: {ex.Message}");
            return fallback;
        }
    }

    private static (string Controller, IReadOnlyList<HardwareModule> Modules) ReadHardware(OleDbConnection connection)
    {
        var controller = string.Empty;
        var modules = new List<HardwareModule>();
        using var reader = Execute(connection, "SELECT ElementModel, ElementPosition, RailIndex FROM HW_Elements");
        while (reader.Read())
        {
            var model = Text(reader, "ElementModel");
            var position = Int(reader, "ElementPosition", -1);
            if (model.Length == 0)
            {
                continue;
            }

            if (position == -1)
            {
                controller = model;
            }
            else
            {
                modules.Add(new HardwareModule(position, Int(reader, "RailIndex"), model));
            }
        }

        return (controller, modules.OrderBy(m => m.Position).ToList());
    }

    private static List<OperandInfo> ReadOperandRows(OleDbConnection connection)
    {
        var operands = new List<OperandInfo>();
        using var reader = Execute(connection, "SELECT OprndType, OprndAddress, OprndDescription, OprndPowerUp, OprndEnablePowerUp FROM LdrOperands");
        while (reader.Read())
        {
            var code = Int(reader, "OprndType");
            var address = Int(reader, "OprndAddress");
            operands.Add(new OperandInfo(
                code,
                address,
                ToMemory(code, address),
                Text(reader, "OprndDescription"),
                Text(reader, "OprndPowerUp"),
                Int(reader, "OprndEnablePowerUp") != 0));
        }

        return operands;
    }

    private static List<LadderSubroutine> ReadLadder(OleDbConnection connection, List<OperandInfo> operandRows, List<string> warnings)
    {
        var constants = new Dictionary<(int Code, int Address), long>();
        foreach (var row in operandRows.Where(r => VisiLogicOperandCodes.IsConstant(r.TypeCode)))
        {
            if (VisiLogicOperandCodes.TryParsePowerUp(row.PowerUpRaw, out var value))
            {
                constants[(row.TypeCode, row.Address)] = value;
            }
        }

        var functions = new List<(int Id, string Name)>();
        using (var reader = Execute(connection, "SELECT FuncID, FuncName FROM LdrFunctions ORDER BY FuncID"))
        {
            while (reader.Read())
            {
                functions.Add((Int(reader, "FuncID"), Text(reader, "FuncName")));
            }
        }

        var strips = new List<(int StripId, int FuncId, int Sequence, string Comment, bool IsReturn)>();
        using (var reader = Execute(connection, "SELECT StripID, FuncID, SeqNum, Comment, IsReturnStrip FROM LdrStrips ORDER BY FuncID, SeqNum"))
        {
            while (reader.Read())
            {
                strips.Add((Int(reader, "StripID"), Int(reader, "FuncID"), Int(reader, "SeqNum"), Text(reader, "Comment"), Int(reader, "IsReturnStrip") != 0));
            }
        }

        var operandsByElement = new Dictionary<int, List<ElementOperand>>();
        var unresolvedConstants = 0;
        var unknownCodes = new SortedSet<int>();
        using (var reader = Execute(connection, "SELECT ElmntID, Elmnt_Oprnd_Type, Elmnt_Oprnd_Adrs, OprndOutIdx, OprndInIdx FROM LdrElmsOprs ORDER BY ElmntID, OprndOutIdx, OprndInIdx"))
        {
            while (reader.Read())
            {
                var code = Int(reader, "Elmnt_Oprnd_Type");
                var address = Int(reader, "Elmnt_Oprnd_Adrs");
                var kind = VisiLogicOperandCodes.Classify(code);
                long? constant = kind switch
                {
                    ElementOperandKind.HmiPage or ElementOperandKind.Subroutine => address,
                    ElementOperandKind.Constant when constants.TryGetValue((code, address), out var value) => value,
                    _ => null,
                };

                if (kind == ElementOperandKind.Constant && constant is null)
                {
                    unresolvedConstants++;
                }
                else if (kind == ElementOperandKind.Unknown)
                {
                    unknownCodes.Add(code);
                }

                var elementId = Int(reader, "ElmntID");
                if (!operandsByElement.TryGetValue(elementId, out var list))
                {
                    operandsByElement[elementId] = list = [];
                }

                list.Add(new ElementOperand(code, address, Int(reader, "OprndOutIdx"), Int(reader, "OprndInIdx"), kind, ToMemory(code, address), constant));
            }
        }

        var elementsByStrip = new Dictionary<int, List<LadderElement>>();
        using (var reader = Execute(connection, "SELECT ElmntID, StripID, ElmntType, Xs, Ys, Dx, Dy, MoreParamsStr, MoreParamsLng FROM LdrElements ORDER BY StripID, Ys, Xs"))
        {
            while (reader.Read())
            {
                var elementId = Int(reader, "ElmntID");
                var stripId = Int(reader, "StripID");
                if (!elementsByStrip.TryGetValue(stripId, out var list))
                {
                    elementsByStrip[stripId] = list = [];
                }

                list.Add(new LadderElement(
                    elementId,
                    Int(reader, "ElmntType"),
                    Int(reader, "Xs"),
                    Int(reader, "Ys"),
                    Int(reader, "Dx"),
                    Int(reader, "Dy"),
                    Text(reader, "MoreParamsStr"),
                    Int(reader, "MoreParamsLng"),
                    operandsByElement.TryGetValue(elementId, out var ops) ? ops : []));
            }
        }

        var knownFunctions = functions.Select(f => f.Id).ToHashSet();
        var orphanStrips = strips.Count(s => !knownFunctions.Contains(s.FuncId));
        var knownStrips = strips.Select(s => s.StripId).ToHashSet();
        var orphanElements = elementsByStrip.Where(p => !knownStrips.Contains(p.Key)).Sum(p => p.Value.Count);

        if (unresolvedConstants > 0)
        {
            warnings.Add($"LdrElmsOprs: {unresolvedConstants} stałych bez wartości w LdrOperands.");
        }

        if (unknownCodes.Count > 0)
        {
            warnings.Add($"LdrElmsOprs: nieznane kody typów operandów: {string.Join(", ", unknownCodes)}.");
        }

        if (orphanStrips > 0 || orphanElements > 0)
        {
            warnings.Add($"LAD: {orphanStrips} rungów bez podprogramu, {orphanElements} elementów bez rungu (pominięte).");
        }

        return functions
            .Select(f => new LadderSubroutine(
                f.Id,
                f.Name,
                strips
                    .Where(s => s.FuncId == f.Id)
                    .Select(s => new LadderRung(
                        s.StripId,
                        s.Sequence,
                        s.Comment,
                        s.IsReturn,
                        elementsByStrip.TryGetValue(s.StripId, out var elements) ? elements : []))
                    .ToList()))
            .ToList();
    }

    private static List<HmiDisplay> ReadDisplays(OleDbConnection connection, List<string> warnings)
    {
        // [ARCHITEKTURA I ADR]
        // HMI_Operands i HMIJumps czytamy osobno: brak którejś z nich (starszy format, eksport .vld) daje ostrzeżenie,
        // a ekrany nadal się wczytują — dotyk wtedy jako MB z HMI_Elements, bez skoków ekranów.
        var touchOperands = TryRead(warnings, "HMI_Operands", () => ReadTouchOperands(connection), new Dictionary<int, (int Type, int Address)>());
        var jumpsByDisplay = TryRead(warnings, "HMIJumps", () => ReadJumps(connection), new Dictionary<int, List<HmiJump>>());

        var elementsByDisplay = new Dictionary<int, List<HmiElement>>();
        using (var reader = Execute(
            connection,
            "SELECT ID, DisplayID, Type, VarType, Text, Xs, Ys, Dx, Dy, ColorBack, ColorFore, VarOprndType, VarOprndAddr, " +
            "TouchOprndAddr, VisibleOpAddr, VarKeyPadEntry FROM HMI_Elements ORDER BY DisplayID, ID"))
        {
            while (reader.Read())
            {
                var displayId = Int(reader, "DisplayID");
                if (!elementsByDisplay.TryGetValue(displayId, out var list))
                {
                    elementsByDisplay[displayId] = list = [];
                }

                var elementId = Int(reader, "ID");
                var touchAddress = Int(reader, "TouchOprndAddr", -1);
                var touchType = HmiElement.DefaultTouchOperandType;
                if (touchOperands.TryGetValue(elementId, out var link))
                {
                    // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
                    // HMI_Operands (rola 2 = dotyk) ma pierwszeństwo: podaje typ operandu, a adres uzupełnia brak w HMI_Elements.
                    touchType = link.Type;
                    touchAddress = link.Address;
                }

                list.Add(new HmiElement(
                    elementId,
                    Int(reader, "Type", 1),
                    Int(reader, "VarType"),
                    Text(reader, "Text"),
                    Int(reader, "Xs"),
                    Int(reader, "Ys"),
                    Int(reader, "Dx"),
                    Int(reader, "Dy"),
                    Int(reader, "ColorBack"),
                    Int(reader, "ColorFore"),
                    Int(reader, "VarOprndType"),
                    Int(reader, "VarOprndAddr"),
                    touchType,
                    touchAddress,
                    Int(reader, "VisibleOpAddr", -1),
                    Int(reader, "VarKeyPadEntry") != 0));
            }
        }

        var displays = new List<HmiDisplay>();
        using (var reader = Execute(connection, "SELECT Id, Description, ColorBack FROM Pages ORDER BY Id"))
        {
            while (reader.Read())
            {
                var id = Int(reader, "Id");
                displays.Add(new HmiDisplay(
                    id,
                    Text(reader, "Description"),
                    Int(reader, "ColorBack"),
                    elementsByDisplay.TryGetValue(id, out var elements) ? elements : [],
                    jumpsByDisplay.TryGetValue(id, out var jumps) ? jumps : []));
            }
        }

        return displays;
    }

    private static Dictionary<int, (int Type, int Address)> ReadTouchOperands(OleDbConnection connection)
    {
        const int TouchPurpose = 2;
        var links = new Dictionary<int, (int Type, int Address)>();
        using var reader = Execute(connection, "SELECT ElementID, OprndType, OprndAddr, OprndPurpose FROM HMI_Operands");
        while (reader.Read())
        {
            var address = Int(reader, "OprndAddr", -1);
            if (Int(reader, "OprndPurpose") == TouchPurpose && address >= 0)
            {
                links.TryAdd(Int(reader, "ElementID"), (Int(reader, "OprndType", HmiElement.DefaultTouchOperandType), address));
            }
        }

        return links;
    }

    private static Dictionary<int, List<HmiJump>> ReadJumps(OleDbConnection connection)
    {
        var jumps = new Dictionary<int, List<HmiJump>>();
        using var reader = Execute(
            connection,
            "SELECT PageNum, JumpType, JumpOpType, JumpOpAddress, DispOpType, DispOpAddress FROM HMIJumps ORDER BY PageNum, JumpType");
        while (reader.Read())
        {
            var displayId = Int(reader, "PageNum");
            if (!jumps.TryGetValue(displayId, out var list))
            {
                jumps[displayId] = list = [];
            }

            list.Add(new HmiJump(
                displayId,
                Int(reader, "JumpType"),
                Int(reader, "JumpOpType"),
                Int(reader, "JumpOpAddress", -1),
                Int(reader, "DispOpType"),
                Int(reader, "DispOpAddress", -1)));
        }

        return jumps;
    }

    private static OleDbDataReader Execute(OleDbConnection connection, string sql)
    {
        // [OSTRZEŻENIA I KONSEKWENCJE]
        // Zapytania są stałe (bez danych wejściowych), więc nie ma ryzyka SQL Injection.
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteReader();
    }

    private static OperandAddress? ToMemory(int code, int address) =>
        VisiLogicOperandCodes.TryGetOperandType(code, out var type) && address >= 0 ? new OperandAddress(type, address) : null;

    private static int Int(IDataRecord record, string column, int fallback = 0)
    {
        var value = record[column];
        return value is DBNull ? fallback : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string Text(IDataRecord record, string column)
    {
        var value = record[column];
        return value is DBNull
            ? string.Empty
            : (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
    }
}
