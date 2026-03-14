using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SilkKey = Silk.NET.Input.Key;

namespace MandelbrotGpu;

/// <summary>
/// A detached WPF Window that displays controls, live settings, and performance data.
/// </summary>
public class SettingsHUD : Window
{
    private readonly MandelbrotApp _app;
    private readonly MandelbrotCompute _compute;
    private readonly DispatcherTimer _timer;
    private bool _allowClose;

    private readonly TextBlock _lblCoords;
    private readonly TextBlock _lblZoom;
    private readonly TextBlock _lblIterations;
    private readonly TextBlock _lblPerformance;
    private readonly TextBlock _lblTimings;

    public SettingsHUD(MandelbrotApp app, MandelbrotCompute compute)
    {
        _app = app;
        _compute = compute;

        Title = "Settings & Controls HUD";
        Width = 480;
        Height = 660;
        WindowStyle = WindowStyle.ToolWindow;
        Topmost = true;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(25, 25, 30));
        Foreground = Brushes.White;
        Left = 50;
        Top = 50;
        KeyDown += OnHudKeyDown;

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(15)
        };

        var mainStack = new StackPanel();
        scrollViewer.Content = mainStack;

        AddHeader(mainStack, "Live Status");
        _lblCoords = AddText(mainStack, "");
        _lblZoom = AddText(mainStack, "");
        _lblIterations = AddText(mainStack, "");

        mainStack.Children.Add(new Separator { Background = Brushes.DimGray, Margin = new Thickness(0, 15, 0, 15) });

        AddHeader(mainStack, "Current Settings");
        _lblPerformance = AddText(mainStack, "");

        mainStack.Children.Add(new Separator { Background = Brushes.DimGray, Margin = new Thickness(0, 15, 0, 15) });

        AddHeader(mainStack, "Performance");
        _lblTimings = AddText(mainStack, "");

        mainStack.Children.Add(new Separator { Background = Brushes.DimGray, Margin = new Thickness(0, 15, 0, 15) });

        AddHeader(mainStack, "Controls");
        AddText(
            mainStack,
            "Mouse:\n" +
            "  • Left Drag: Orbit camera\n" +
            "  • Middle Drag: Pan camera\n" +
            "  • Scroll: Zoom camera\n\n" +
            "Fractal:\n" +
            "  • W/A/S/D or Arrows: Pan fractal\n" +
            "  • +/- : Zoom fractal\n" +
            "  • I / K: Iterations (+/-)\n" +
            "  • PageUp / PageDown: Height scale\n" +
            "  • C: Cycle palette\n" +
            "  • F: Toggle wireframe\n" +
            "  • G: Manual resolution tier\n" +
            "  • M: Cycle precision mode\n" +
            "  • N: Cycle performance profile\n" +
            "  • L: Toggle shading mode\n" +
            "  • O: Toggle adaptive resolution\n" +
            "  • H: Focus HUD\n" +
            "  • V: Toggle VSync\n" +
            "  • 1..6: Location presets\n" +
            "  • R: Reset view");

        Content = scrollViewer;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += UpdateData!;
        _timer.Start();
    }

    public void BeginClose()
    {
        _allowClose = true;
        Close();
    }

    private void AddHeader(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 255))
        });
    }

    private TextBlock AddText(StackPanel panel, string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 5),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };

        panel.Children.Add(textBlock);
        return textBlock;
    }

    private void UpdateData(object sender, EventArgs e)
    {
        PerformanceSettings settings = _app.CurrentPerformanceSettings;
        PerformanceMetrics metrics = _app.LatestPerformanceMetrics;

        _lblCoords.Text =
            $"X = {_compute.CenterX:F15}\n" +
            $"Y = {_compute.CenterY:F15}";

        _lblZoom.Text =
            $"Zoom: {_compute.Zoom:0.##e+0}x\n" +
            $"FPS: {_app.FPS:F0}\n" +
            $"Precision: {_compute.PrecisionStatus}";

        _lblIterations.Text =
            $"Iterations: {_compute.MaxIterations:N0}\n" +
            $"Palette: {_app.CurrentPaletteName}\n" +
            $"Height scale: {_app.HeightScale:F2}\n" +
            $"Wireframe: {(_app.WireframeMode ? "ON" : "OFF")}";

        _lblPerformance.Text =
            $"Profile: {settings.Profile}\n" +
            $"Precision selector: {_app.CurrentPrecisionMode}\n" +
            $"Adaptive: {(_app.AdaptiveResolutionEnabled ? "ON" : "OFF")}\n" +
            $"Compute resolution: {_compute.Width}x{_compute.Height}\n" +
            $"Render mesh: {_app.RenderMeshResolution}x{_app.RenderMeshResolution}\n" +
            $"Shading: {settings.ShadingMode}\n" +
            $"VSync: {(settings.VSyncEnabled ? "ON" : "OFF")}\n" +
            $"MSAA: {(settings.MsaaSamples > 0 ? $"{settings.MsaaSamples}x" : "OFF")}\n" +
            $"Grid overlay: {(_app.GridVisible ? "ON" : "OFF")}";

        _lblTimings.Text =
            $"Kernel dispatch: {metrics.KernelDispatchMs:F2} ms\n" +
            $"GPU synchronize: {metrics.GpuSynchronizeMs:F2} ms\n" +
            $"Readback: {metrics.ReadbackMs:F2} ms\n" +
            $"Texture upload: {metrics.TextureUploadMs:F2} ms\n" +
            $"Grid rebuild: {metrics.GridBuildMs:F2} ms\n" +
            $"Draw submit: {metrics.DrawMs:F2} ms\n" +
            $"Interaction latency: {metrics.InteractionLatencyMs:F2} ms\n" +
            $"Managed alloc: {metrics.ManagedAllocatedBytes / 1024f / 1024f:F2} MB";
    }

    private void OnHudKeyDown(object sender, KeyEventArgs e)
    {
        SilkKey silkKey = e.Key switch
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

        if (silkKey == SilkKey.Unknown)
            return;

        _app.HandleExternalKeyDown(silkKey, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(() => Activate()), DispatcherPriority.Background);
            return;
        }

        _timer.Stop();
        base.OnClosing(e);
    }
}
