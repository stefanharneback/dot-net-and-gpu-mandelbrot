using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SilkKey = Silk.NET.Input.Key;

namespace MandelbrotGpu;

public abstract class HudWindowBase : Window
{
    private static readonly SolidColorBrush WindowBackgroundBrush = CreateFrozenBrush(16, 18, 26);
    private static readonly SolidColorBrush CardBackgroundBrush = CreateFrozenBrush(24, 28, 38);
    private static readonly SolidColorBrush CardBorderBrush = CreateFrozenBrush(58, 67, 82);
    private static readonly SolidColorBrush AccentBrushValue = CreateFrozenBrush(0, 190, 255);
    private static readonly SolidColorBrush LabelBrushValue = CreateFrozenBrush(166, 176, 194);
    private static readonly SolidColorBrush ValueBrushValue = CreateFrozenBrush(240, 244, 255);
    private static readonly SolidColorBrush HintBrushValue = CreateFrozenBrush(123, 187, 255);

    private readonly MandelbrotApp _app;
    private bool _allowClose;

    protected HudWindowBase(MandelbrotApp app, string title, double left, double top, double width, double height)
    {
        _app = app;

        Title = title;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Topmost = true;
        ShowInTaskbar = false;
        Background = WindowBackgroundBrush;
        Foreground = ValueBrushValue;
        FontFamily = new FontFamily("Segoe UI");
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

        ApplyWindowBounds(left, top, width, height);
        KeyDown += OnHudKeyDown;
    }

    protected MandelbrotApp App => _app;

    protected static Brush AccentBrush => AccentBrushValue;

    protected static Brush LabelBrush => LabelBrushValue;

    protected static Brush ValueBrush => ValueBrushValue;

    protected static Brush HintBrush => HintBrushValue;

    public void BeginClose()
    {
        _allowClose = true;
        Close();
    }

    public void FocusHud()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            WindowState = WindowState.Normal;
            Activate();
        }), DispatcherPriority.Background);
    }

    public void ApplyWindowBounds(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        MinWidth = Math.Max(320, width * 0.84);
        MinHeight = Math.Max(280, height * 0.74);
    }

    protected static ScrollViewer CreateScrollViewer(UIElement content)
    {
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(18, 16, 18, 16)
        };
    }

    protected static StackPanel CreateRootPanel()
    {
        return new StackPanel { Orientation = Orientation.Vertical };
    }

    protected static Border CreateCard(string title, UIElement content)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            FontSize = 18,
            Margin = new Thickness(0, 0, 0, 14),
            Foreground = AccentBrushValue
        });
        stack.Children.Add(content);

        return new Border
        {
            Background = CardBackgroundBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(14, 12, 14, 14),
            Child = stack
        };
    }

    protected static Grid CreateTableGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        return grid;
    }

    protected static void AddHeaderRow(Grid grid, ref int rowIndex, string left, string middle, string right)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(grid, rowIndex, 0, left, HintBrushValue, false, FontWeights.Bold, 12, new Thickness(0, 0, 12, 10));
        AddCell(grid, rowIndex, 1, middle, HintBrushValue, false, FontWeights.Bold, 12, new Thickness(0, 0, 12, 10));
        AddCell(grid, rowIndex, 2, right, HintBrushValue, true, FontWeights.Bold, 12, new Thickness(0, 0, 0, 10), TextAlignment.Right);
        rowIndex++;
    }

    protected static TextBlock AddValueRow(Grid grid, ref int rowIndex, string label, string keyHint = "", bool monospaceValue = false)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(grid, rowIndex, 0, label, LabelBrushValue, false, FontWeights.Normal, 13, new Thickness(0, 0, 12, 8));
        TextBlock valueBlock = AddCell(grid, rowIndex, 1, "-", ValueBrushValue, monospaceValue, FontWeights.SemiBold, 14, new Thickness(0, 0, 12, 8));
        AddCell(grid, rowIndex, 2, keyHint, HintBrushValue, true, FontWeights.SemiBold, 12, new Thickness(0, 0, 0, 8), TextAlignment.Right);
        rowIndex++;
        return valueBlock;
    }

    protected static void AddStaticRow(Grid grid, ref int rowIndex, string action, string keys, string note = "")
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(grid, rowIndex, 0, action, LabelBrushValue, false, FontWeights.Normal, 13, new Thickness(0, 0, 12, 8));
        AddCell(grid, rowIndex, 1, keys, ValueBrushValue, true, FontWeights.SemiBold, 14, new Thickness(0, 0, 12, 8));
        AddCell(grid, rowIndex, 2, note, HintBrushValue, false, FontWeights.Normal, 12, new Thickness(0, 0, 0, 8), TextAlignment.Right);
        rowIndex++;
    }

    protected static TextBlock CreateNote(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = LabelBrushValue,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                WindowState = WindowState.Normal;
                Activate();
            }), DispatcherPriority.Background);
            return;
        }

        OnHudClosing();
        base.OnClosing(e);
    }

    protected virtual void OnHudClosing()
    {
    }

    private void OnHudKeyDown(object sender, KeyEventArgs e)
    {
        SilkKey mappedKey = e.Key switch
        {
            Key.W => SilkKey.W,
            Key.A => SilkKey.A,
            Key.S => SilkKey.S,
            Key.D => SilkKey.D,
            Key.Up => SilkKey.Up,
            Key.Down => SilkKey.Down,
            Key.Left => SilkKey.Left,
            Key.Right => SilkKey.Right,
            Key.Add => SilkKey.KeypadAdd,
            Key.OemPlus => SilkKey.Equal,
            Key.Subtract => SilkKey.KeypadSubtract,
            Key.OemMinus => SilkKey.Minus,
            Key.I => SilkKey.I,
            Key.K => SilkKey.K,
            Key.C => SilkKey.C,
            Key.F => SilkKey.F,
            Key.G => SilkKey.G,
            Key.H => SilkKey.H,
            Key.L => SilkKey.L,
            Key.M => SilkKey.M,
            Key.N => SilkKey.N,
            Key.O => SilkKey.O,
            Key.P => SilkKey.P,
            Key.R => SilkKey.R,
            Key.T => SilkKey.T,
            Key.V => SilkKey.V,
            Key.PageUp => SilkKey.PageUp,
            Key.PageDown => SilkKey.PageDown,
            Key.D1 => SilkKey.Number1,
            Key.D2 => SilkKey.Number2,
            Key.D3 => SilkKey.Number3,
            Key.D4 => SilkKey.Number4,
            Key.D5 => SilkKey.Number5,
            Key.D6 => SilkKey.Number6,
            Key.Escape => SilkKey.Escape,
            _ => SilkKey.Unknown
        };

        if (mappedKey == SilkKey.Unknown)
            return;

        _app.HandleExternalKeyDown(mappedKey, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private static TextBlock AddCell(
        Grid grid,
        int row,
        int column,
        string text,
        Brush foreground,
        bool monospace,
        FontWeight fontWeight,
        double fontSize,
        Thickness margin,
        TextAlignment textAlignment = TextAlignment.Left)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontFamily = monospace ? new FontFamily("Consolas") : new FontFamily("Segoe UI"),
            FontWeight = fontWeight,
            FontSize = fontSize,
            Margin = margin,
            TextAlignment = textAlignment,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
        return block;
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
