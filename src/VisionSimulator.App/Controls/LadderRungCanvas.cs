using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VisionSimulator.Core.Ladder;

namespace VisionSimulator.App.Controls;

/// <summary>
/// Rysuje jeden szczebel drabinki LAD: lewą szynę, przewody, styki, cewki i bloki funkcyjne w kolorach zasilenia
/// z ostatniego skanu, a pod symbolami bieżące wartości operandów.
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Kontrolka zna wyłącznie niezmienny <see cref="LadderRungLayout"/> i migawkę <see cref="LadderRungState"/>; nie
/// czyta pamięci sterownika. Siatkę VisiLogic odwzorowuje 1:1 w poziomie (stała szerokość pola); w pionie wiersze
/// powstają z unikalnych współrzędnych Y elementów, więc puste pasy siatki nie zajmują miejsca.
/// </remarks>
public sealed class LadderRungCanvas : FrameworkElement
{
    public static readonly DependencyProperty RungProperty = DependencyProperty.Register(
        nameof(Rung),
        typeof(LadderRungLayout),
        typeof(LadderRungCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(LadderRungState),
        typeof(LadderRungCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double CellWidth = 11;
    private const double LeftMargin = 14;
    private const double RightMargin = 16;
    private const double AxisOffset = 26;
    private const double MinRowHeight = 50;
    private const double LineHeight = 12;
    private const double TextSize = 11;

    private static readonly Brush PoweredBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)));
    private static readonly Brush IdleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)));
    private static readonly Brush ValueBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)));
    private static readonly Brush BlockFillBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)));
    private static readonly Brush ConductingFillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x16, 0xA3, 0x4A)));
    private static readonly Pen PoweredPen = Freeze(new Pen(PoweredBrush, 2.5));
    private static readonly Pen IdlePen = Freeze(new Pen(IdleBrush, 1.5));
    private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface TitleTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface ValueTypeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly List<(Rect Bounds, int Index)> _hitAreas = [];
    private readonly ToolTip _toolTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private Dictionary<int, int> _rowOf = [];
    private double[] _rowTop = [];
    private double _height;
    private int _hoverIndex = -1;

    public LadderRungCanvas()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetShowDuration(this, 60_000);
    }

    public LadderRungLayout? Rung
    {
        get => (LadderRungLayout?)GetValue(RungProperty);
        set => SetValue(RungProperty, value);
    }

    public LadderRungState? State
    {
        get => (LadderRungState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rung = Rung;
        BuildRows(rung);
        var width = rung is null ? 0 : LeftMargin + (rung.Width * CellWidth) + RightMargin;
        return new Size(width, _height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        _hitAreas.Clear();
        var rung = Rung;
        if (rung is null || rung.Elements.Count == 0)
        {
            return;
        }

        if (_rowTop.Length == 0)
        {
            BuildRows(rung);
        }

        var state = State is { } current && current.Elements.Count == rung.Elements.Count ? current : null;
        var executed = state is { Executed: true };
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Lewa szyna jest zasilona zawsze, gdy szczebel był wykonany w ostatnim skanie.
        var railPen = executed ? PoweredPen : IdlePen;
        drawingContext.DrawLine(railPen, new Point(LeftMargin, 2), new Point(LeftMargin, _height - 2));

        foreach (var element in rung.Elements)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Szczebel niewykonany (np. za zasilonym RET albo w niewywołanym podprogramie): wartości operandów są
            // bieżące, ale zasilenie rysujemy jako brak.
            var elementState = state?.Elements[element.Index];
            if (elementState is not null && !executed)
            {
                elementState = elementState with { Input = false, Output = false, Conducts = false };
            }

            DrawElement(drawingContext, element, elementState, dip);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ArgumentNullException.ThrowIfNull(e);

        var point = e.GetPosition(this);
        var index = -1;
        foreach (var (bounds, elementIndex) in _hitAreas)
        {
            if (bounds.Contains(point))
            {
                index = elementIndex;
            }
        }

        if (index == _hoverIndex)
        {
            return;
        }

        _hoverIndex = index;
        if (index < 0 || Rung is not { } rung)
        {
            _toolTip.IsOpen = false;
            ToolTip = null;
            return;
        }

        var state = State is { } current && current.Elements.Count > index ? current : null;
        _toolTip.Content = Describe(rung.Elements[index], state?.Elements[index], state?.Executed ?? false);
        ToolTip = _toolTip;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        _toolTip.IsOpen = false;
        ToolTip = null;
    }

    // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
    // Wiersze = posortowane unikalne Y elementów (oraz końce przewodów pionowych). Wysokość wiersza wynika z
    // najwyższego bloku funkcyjnego w nim: tytuł + jedna linia na operand.
    private void BuildRows(LadderRungLayout? rung)
    {
        _rowOf = [];
        _rowTop = [];
        _height = 0;
        if (rung is null || rung.Elements.Count == 0)
        {
            return;
        }

        var ys = new SortedSet<int>();
        foreach (var element in rung.Elements)
        {
            ys.Add(element.Y);
            if (element.Shape == LadderShape.VerticalWire)
            {
                ys.Add(element.Y + element.Height);
            }
        }

        var rows = ys.ToArray();
        var heights = new double[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            _rowOf[rows[i]] = i;
            heights[i] = MinRowHeight;
        }

        foreach (var element in rung.Elements)
        {
            if (element.Shape is LadderShape.Compare or LadderShape.Block)
            {
                var row = _rowOf[element.Y];
                heights[row] = Math.Max(heights[row], AxisOffset + BlockBottom(element.Operands.Count) + 8);
            }
        }

        _rowTop = new double[rows.Length];
        var top = 0.0;
        for (var i = 0; i < rows.Length; i++)
        {
            _rowTop[i] = top;
            top += heights[i];
        }

        _height = top;
    }

    private static double BlockBottom(int operandCount) => 6 + (Math.Max(1, operandCount) * LineHeight);

    private double Axis(int y) => _rowOf.TryGetValue(y, out var row) ? _rowTop[row] + AxisOffset : AxisOffset;

    private static double X(int cell) => LeftMargin + (cell * CellWidth);

    private void DrawElement(DrawingContext dc, LadderElementLayout element, LadderElementState? state, double dip)
    {
        var input = state?.Input ?? false;
        var output = state?.Output ?? false;
        var conducts = state?.Conducts ?? false;
        var axis = Axis(element.Y);
        var x0 = X(element.X);
        var x1 = X(element.X + element.Width);
        var center = (x0 + x1) / 2;

        switch (element.Shape)
        {
            case LadderShape.HorizontalWire:
                dc.DrawLine(PenFor(input), new Point(x0, axis), new Point(x1, axis));
                AddHit(new Rect(x0, axis - 4, Math.Max(4, x1 - x0), 8), element.Index);
                break;

            case LadderShape.VerticalWire:
                var bottom = Axis(element.Y + element.Height);
                dc.DrawLine(PenFor(input), new Point(x0, axis), new Point(x0, bottom));
                AddHit(new Rect(x0 - 4, axis, 8, Math.Max(4, bottom - axis)), element.Index);
                break;

            case LadderShape.Contact:
                DrawContact(dc, element, state, axis, x0, x1, center, input, output, conducts, dip);
                break;

            case LadderShape.Coil:
                DrawCoil(dc, element, state, axis, x0, x1, center, input, dip);
                break;

            case LadderShape.Return:
                dc.DrawLine(PenFor(input), new Point(x0, axis), new Point(center - 14, axis));
                dc.DrawRectangle(input ? ConductingFillBrush : BlockFillBrush, PenFor(input), new Rect(center - 14, axis - 9, 28, 18));
                DrawText(dc, "RET", TitleTypeface, TextBrush, new Point(center, axis - 7), 28, dip, centered: true);
                AddHit(new Rect(x0, axis - 10, x1 - x0, 20), element.Index);
                break;

            default:
                DrawBlock(dc, element, state, axis, x0, x1, input, output, dip);
                break;
        }
    }

    private void DrawContact(
        DrawingContext dc,
        LadderElementLayout element,
        LadderElementState? state,
        double axis,
        double x0,
        double x1,
        double center,
        bool input,
        bool output,
        bool conducts,
        double dip)
    {
        const double gap = 5;
        const double half = 9;
        var barPen = conducts ? PoweredPen : IdlePen;

        dc.DrawLine(PenFor(input), new Point(x0, axis), new Point(center - gap, axis));
        dc.DrawLine(PenFor(output), new Point(center + gap, axis), new Point(x1, axis));
        if (conducts)
        {
            dc.DrawRectangle(ConductingFillBrush, null, new Rect(center - gap, axis - half, gap * 2, half * 2));
        }

        dc.DrawLine(barPen, new Point(center - gap, axis - half), new Point(center - gap, axis + half));
        dc.DrawLine(barPen, new Point(center + gap, axis - half), new Point(center + gap, axis + half));

        switch (element.Opcode)
        {
            case 201:
                dc.DrawLine(barPen, new Point(center - gap - 2, axis + half), new Point(center + gap + 2, axis - half));
                break;
            case 202:
                DrawText(dc, "P", TitleTypeface, TextBrush, new Point(center, axis - 7), 10, dip, centered: true);
                break;
            case 203:
                DrawText(dc, "N", TitleTypeface, TextBrush, new Point(center, axis - 7), 10, dip, centered: true);
                break;
        }

        DrawOperandCaption(dc, element, state, axis, x0, x1, dip);
        AddHit(new Rect(x0, axis - AxisOffset + 2, x1 - x0, AxisOffset + 22), element.Index);
    }

    private void DrawCoil(
        DrawingContext dc,
        LadderElementLayout element,
        LadderElementState? state,
        double axis,
        double x0,
        double x1,
        double center,
        bool input,
        double dip)
    {
        const double radius = 9;
        var pen = PenFor(input);
        dc.DrawLine(pen, new Point(x0, axis), new Point(center - radius, axis));
        dc.DrawEllipse(input ? ConductingFillBrush : null, pen, new Point(center, axis), radius, radius);

        var letter = element.Opcode switch
        {
            205 => "N",
            206 => "S",
            207 => "R",
            212 => "T",
            _ => string.Empty,
        };
        if (letter.Length > 0)
        {
            DrawText(dc, letter, TitleTypeface, TextBrush, new Point(center, axis - 7), 12, dip, centered: true);
        }

        DrawOperandCaption(dc, element, state, axis, x0, x1, dip);
        AddHit(new Rect(x0, axis - AxisOffset + 2, x1 - x0, AxisOffset + 22), element.Index);
    }

    // [INTENCJA I POWÓD DZIAŁANIA]
    // Styk i cewka: adres nad symbolem, bieżąca wartość pod nim; opis operandu w podpowiedzi (brak miejsca w siatce).
    private static void DrawOperandCaption(
        DrawingContext dc,
        LadderElementLayout element,
        LadderElementState? state,
        double axis,
        double x0,
        double x1,
        double dip)
    {
        var width = Math.Max(CellWidth * 2, x1 - x0);
        var center = (x0 + x1) / 2;
        var caption = element.Operands.Count > 0 ? element.Operands[0].Text : element.Mnemonic;
        DrawText(dc, caption, LabelTypeface, TextBrush, new Point(center, axis - AxisOffset + 2), width, dip, centered: true);

        var value = state is not null && state.Values.Count > 0 ? state.Values[0] : string.Empty;
        if (value.Length > 0)
        {
            DrawText(dc, value, ValueTypeface, ValueBrush, new Point(center, axis + 10), width, dip, centered: true);
        }
    }

    private void DrawBlock(
        DrawingContext dc,
        LadderElementLayout element,
        LadderElementState? state,
        double axis,
        double x0,
        double x1,
        bool input,
        bool output,
        double dip)
    {
        const double inset = 3;
        var box = new Rect(x0 + inset, axis - 10, Math.Max(CellWidth, x1 - x0 - (inset * 2)), 10 + BlockBottom(element.Operands.Count));
        dc.DrawLine(PenFor(input), new Point(x0, axis), new Point(box.Left, axis));
        dc.DrawLine(PenFor(output), new Point(box.Right, axis), new Point(x1, axis));
        dc.DrawRectangle(output ? ConductingFillBrush : BlockFillBrush, PenFor(input), box);

        var textWidth = box.Width - 6;
        DrawText(dc, element.Mnemonic, TitleTypeface, TextBrush, new Point(box.Left + 3, box.Top + 1), textWidth, dip, centered: false);

        for (var i = 0; i < element.Operands.Count; i++)
        {
            var operand = element.Operands[i];
            var value = state is not null && i < state.Values.Count ? state.Values[i] : string.Empty;
            var line = value.Length > 0 ? $"{operand.Text}={value}" : operand.Text;
            var brush = value.Length > 0 ? ValueBrush : TextBrush;
            DrawText(dc, line, ValueTypeface, brush, new Point(box.Left + 3, box.Top + 14 + (i * LineHeight)), textWidth, dip, centered: false);
        }

        AddHit(new Rect(x0, box.Top, x1 - x0, box.Height), element.Index);
    }

    private static void DrawText(
        DrawingContext dc,
        string text,
        Typeface typeface,
        Brush brush,
        Point origin,
        double maxWidth,
        double dip,
        bool centered)
    {
        if (text.Length == 0 || maxWidth <= 1)
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            TextSize,
            brush,
            dip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
        };

        var x = centered ? origin.X - (maxWidth / 2) : origin.X;
        dc.DrawText(formatted, new Point(x, origin.Y));
    }

    private static string Describe(LadderElementLayout element, LadderElementState? state, bool executed)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.CurrentCulture, $"{element.Mnemonic} (typ {element.Opcode}, ID {element.Id})");
        foreach (var (operand, i) in element.Operands.Select((o, i) => (o, i)))
        {
            text.AppendLine();
            text.Append(operand.Text);
            if (state is not null && i < state.Values.Count && state.Values[i].Length > 0)
            {
                text.Append(" = ").Append(state.Values[i]);
            }

            if (operand.Description.Length > 0)
            {
                text.Append(" — ").Append(operand.Description);
            }
        }

        text.AppendLine();
        text.Append(state is null || !executed
            ? "Szczebel nie był wykonany w ostatnim skanie."
            : $"Wejście: {OnOff(state.Input)}, wyjście: {OnOff(state.Output)}" +
              (element.Shape is LadderShape.Contact or LadderShape.Compare ? $", warunek: {(state.Conducts ? "spełniony" : "niespełniony")}" : string.Empty));
        return text.ToString();
    }

    private static string OnOff(bool value) => value ? "zasilone" : "brak";

    private void AddHit(Rect bounds, int index) => _hitAreas.Add((bounds, index));

    private static Pen PenFor(bool powered) => powered ? PoweredPen : IdlePen;

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
