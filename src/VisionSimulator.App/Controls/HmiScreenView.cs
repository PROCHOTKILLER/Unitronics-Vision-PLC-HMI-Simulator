using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VisionSimulator.Core.Hmi;

namespace VisionSimulator.App.Controls;

/// <summary>Rysuje migawkę ekranu HMI (logiczny rozmiar 320x240) i przekazuje dotyk jako polecenia.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Kontrolka ma stały rozmiar logiczny; skalowanie (także przy DPI 125/150%) zapewnia otaczający <c>Viewbox</c>.
/// Dotyk trafia do <see cref="PressCommand"/> / <see cref="ReleaseCommand"/>, wpis wartości do <see cref="EditCommand"/>
/// (zmienna z klawiaturą: kliknięcie, pozostałe zmienne liczbowe: dwuklik), a kliknięcie elementu bez funkcji do
/// <see cref="ProbeCommand"/> (diagnostyka). Parametrem jest <see cref="HmiRenderItem"/>; kontrolka nie zna pamięci sterownika.
/// </remarks>
public sealed class HmiScreenView : FrameworkElement
{
    public static readonly DependencyProperty EditCommandProperty = DependencyProperty.Register(
        nameof(EditCommand), typeof(ICommand), typeof(HmiScreenView), new PropertyMetadata(null));

    public static readonly DependencyProperty ProbeCommandProperty = DependencyProperty.Register(
        nameof(ProbeCommand), typeof(ICommand), typeof(HmiScreenView), new PropertyMetadata(null));

    public static readonly DependencyProperty ScreenProperty = DependencyProperty.Register(
        nameof(Screen),
        typeof(HmiScreen),
        typeof(HmiScreenView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PressCommandProperty = DependencyProperty.Register(
        nameof(PressCommand), typeof(ICommand), typeof(HmiScreenView), new PropertyMetadata(null));

    public static readonly DependencyProperty ReleaseCommandProperty = DependencyProperty.Register(
        nameof(ReleaseCommand), typeof(ICommand), typeof(HmiScreenView), new PropertyMetadata(null));

    private const double FallbackBackgroundLuminance = 0.5;
    private const uint ActiveBorderArgb = 0xFF00E676;
    private const uint IdleBorderArgb = 0xFF7A8696;
    private const uint KeypadBorderArgb = 0xFF2563EB;

    private readonly Dictionary<uint, SolidColorBrush> _brushes = [];
    private HmiRenderItem? _pressed;

    public HmiScreenView()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public HmiScreen? Screen
    {
        get => (HmiScreen?)GetValue(ScreenProperty);
        set => SetValue(ScreenProperty, value);
    }

    public ICommand? PressCommand
    {
        get => (ICommand?)GetValue(PressCommandProperty);
        set => SetValue(PressCommandProperty, value);
    }

    public ICommand? ReleaseCommand
    {
        get => (ICommand?)GetValue(ReleaseCommandProperty);
        set => SetValue(ReleaseCommandProperty, value);
    }

    public ICommand? EditCommand
    {
        get => (ICommand?)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public ICommand? ProbeCommand
    {
        get => (ICommand?)GetValue(ProbeCommandProperty);
        set => SetValue(ProbeCommandProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(HmiScreenBuilder.ScreenWidth, HmiScreenBuilder.ScreenHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        var screen = Screen;
        var surface = new Rect(0, 0, HmiScreenBuilder.ScreenWidth, HmiScreenBuilder.ScreenHeight);
        drawingContext.DrawRectangle(BrushFor(screen?.BackgroundArgb ?? HmiColor.DefaultBackground), null, surface);
        if (screen is null)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var item in screen.Items)
        {
            DrawItem(drawingContext, item, screen.BackgroundArgb, pixelsPerDip);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var point = e.GetPosition(this);
        if (HitItem(point, static i => i.TouchAddress is not null) is { } touch)
        {
            _pressed = touch;
            CaptureMouse();
            Execute(PressCommand, touch);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (HitItem(point, static i => i.EditAddress is not null) is { } edit && (edit.IsKeypadEntry || e.ClickCount >= 2))
        {
            // [OSTRZEŻENIA I KONSEKWENCJE]
            // Okno modalne otwieramy po zakończeniu obsługi zdarzenia myszy, żeby nie przejęło niedokończonego kliknięcia.
            Dispatcher.InvokeAsync(() => Execute(EditCommand, edit));
            e.Handled = true;
            return;
        }

        var probe = HitItem(point, static i => i.Kind == HmiElementKind.TouchButton) ?? HitItem(point, static _ => true);
        if (probe is not null)
        {
            Execute(ProbeCommand, probe);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleasePressed();
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        ReleasePressed();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = HitItem(e.GetPosition(this), static i => i.TouchAddress is not null || i.EditAddress is not null) is null
            ? null
            : Cursors.Hand;
    }

    private void ReleasePressed()
    {
        var pressed = _pressed;
        _pressed = null;
        if (pressed is not null)
        {
            Execute(ReleaseCommand, pressed);
            InvalidateVisual();
        }
    }

    private static void Execute(ICommand? command, object parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    /// <summary>Najwyższy (ostatni na liście) element spełniający warunek pod punktem.</summary>
    private HmiRenderItem? HitItem(Point point, Func<HmiRenderItem, bool> predicate)
    {
        var items = Screen?.Items;
        if (items is null)
        {
            return null;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (predicate(item) && ItemRect(item).Contains(point))
            {
                return item;
            }
        }

        return null;
    }

    private static Rect ItemRect(HmiRenderItem item) => new(item.X, item.Y, item.Width, item.Height);

    private void DrawItem(DrawingContext dc, HmiRenderItem item, uint backgroundArgb, double pixelsPerDip)
    {
        var rect = ItemRect(item);
        var isPressed = _pressed is not null && _pressed.ElementId == item.ElementId;
        var fill = item.FillArgb;
        var border = item.ForegroundArgb;
        double borderThickness = 1;

        switch (item.Kind)
        {
            case HmiElementKind.TouchButton:
                if (isPressed)
                {
                    fill = Blend(fill, 0xFF000000, 0.25);
                }
                else if (item.IsActive)
                {
                    fill = Blend(fill, 0xFFFFFFFF, 0.35);
                    border = ActiveBorderArgb;
                    borderThickness = 2;
                }

                break;
            case HmiElementKind.BitIndicator:
            case HmiElementKind.BinaryIndicator:
                if (item.IsActive)
                {
                    fill = Blend(fill, 0xFFFFFFFF, 0.45);
                    border = ActiveBorderArgb;
                    borderThickness = 2;
                }
                else
                {
                    border = IdleBorderArgb;
                }

                break;
            case HmiElementKind.StaticText:
            case HmiElementKind.Unknown:
                border = HmiColor.Transparent;
                break;
        }

        if (item.TouchAddress is not null && item.Kind != HmiElementKind.TouchButton && isPressed)
        {
            fill = Blend(fill >> 24 == 0 ? 0xFFFFFFFF : fill, 0xFF000000, 0.25);
        }

        if (item.IsKeypadEntry)
        {
            border = KeypadBorderArgb;
        }

        var pen = border >> 24 == 0 ? null : PenFor(border, borderThickness);
        if (fill >> 24 != 0 || pen is not null)
        {
            dc.DrawRectangle(fill >> 24 == 0 ? null : BrushFor(fill), pen, rect);
        }

        if (item.Kind == HmiElementKind.Bargraph && item.Ratio is { } ratio && rect.Width > 2 && rect.Height > 2)
        {
            var bar = new Rect(rect.X + 1, rect.Y + 1, (rect.Width - 2) * ratio, rect.Height - 2);
            if (bar.Width > 0)
            {
                dc.DrawRectangle(BrushFor(item.ForegroundArgb), null, bar);
            }
        }

        DrawText(dc, item, rect, fill, backgroundArgb, pixelsPerDip);
    }

    private void DrawText(DrawingContext dc, HmiRenderItem item, Rect rect, uint fill, uint backgroundArgb, double pixelsPerDip)
    {
        if (item.Text.Length == 0)
        {
            return;
        }

        var surface = fill >> 24 == 0 ? backgroundArgb : fill;
        var foreground = item.Kind == HmiElementKind.Bargraph
            ? ReadableOn(0xFFB8C2CE, item.ForegroundArgb)
            : ReadableOn(surface, item.ForegroundArgb);

        var alignment = item.Kind is HmiElementKind.StaticText or HmiElementKind.Unknown or HmiElementKind.Frame
            ? TextAlignment.Left
            : TextAlignment.Center;
        var fontSize = Math.Clamp(rect.Height * 0.55, 9, 11);
        var weight = item.Kind == HmiElementKind.TouchButton ? FontWeights.SemiBold : FontWeights.Normal;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal);

        var textWidth = Math.Max(1, rect.Width - 4);
        var formatted = new FormattedText(
            item.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            BrushFor(foreground),
            pixelsPerDip)
        {
            MaxTextWidth = textWidth,
            MaxTextHeight = Math.Max(1, rect.Height),
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };

        var top = rect.Y + Math.Max(0, (rect.Height - formatted.Height) / 2);
        dc.PushClip(new RectangleGeometry(rect));
        dc.DrawText(formatted, new Point(rect.X + 2, top));
        dc.Pop();
    }

    private SolidColorBrush BrushFor(uint argb)
    {
        if (!_brushes.TryGetValue(argb, out var brush))
        {
            brush = new SolidColorBrush(ToColor(argb));
            brush.Freeze();
            _brushes[argb] = brush;
        }

        return brush;
    }

    private Pen PenFor(uint argb, double thickness)
    {
        var pen = new Pen(BrushFor(argb), thickness);
        pen.Freeze();
        return pen;
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static double Luminance(uint argb) =>
        (0.299 * ((argb >> 16) & 0xFF) + 0.587 * ((argb >> 8) & 0xFF) + 0.114 * (argb & 0xFF)) / 255.0;

    // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
    // Jeśli kolor tekstu i tło mają podobną jasność (różnica poniżej 0,35), tekst przyjmuje biały albo czarny
    // zależnie od jasności tła — projekt nie gwarantuje kontrastu, a nieczytelny tekst utrudnia testy HMI.
    private static uint ReadableOn(uint backgroundArgb, uint foregroundArgb)
    {
        var background = Luminance(backgroundArgb);
        if (Math.Abs(background - Luminance(foregroundArgb)) >= 0.35)
        {
            return foregroundArgb;
        }

        return background < FallbackBackgroundLuminance ? 0xFFFFFFFF : 0xFF000000;
    }

    private static uint Blend(uint from, uint to, double amount)
    {
        static uint Channel(uint a, uint b, double t, int shift) =>
            (uint)Math.Round((((a >> shift) & 0xFF) * (1 - t)) + (((b >> shift) & 0xFF) * t));

        return 0xFF000000u
            | (Channel(from, to, amount, 16) << 16)
            | (Channel(from, to, amount, 8) << 8)
            | Channel(from, to, amount, 0);
    }
}
