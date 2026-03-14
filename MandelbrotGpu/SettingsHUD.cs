using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MandelbrotGpu;

/// <summary>
/// A detached WPF Window that displays controls and real-time settings.
/// </summary>
public class SettingsHUD : Window
{
    private readonly MandelbrotApp _app;
    private readonly MandelbrotCompute _compute;
    private readonly DispatcherTimer _timer;

    private readonly TextBlock _lblCoords;
    private readonly TextBlock _lblZoom;
    private readonly TextBlock _lblIters;
    private readonly TextBlock _lblAdaptive;

    public SettingsHUD(MandelbrotApp app, MandelbrotCompute compute)
    {
        _app = app;
        _compute = compute;

        Title = "🌀 Settings & Controls HUD";
        Width = 450;
        Height = 550;
        WindowStyle = WindowStyle.ToolWindow;
        Topmost = true;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(25, 25, 30));
        Foreground = Brushes.White;

        Left = 50;
        Top = 50;

        var scrollViewer = new ScrollViewer 
        { 
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(15)
        };

        var mainStack = new StackPanel();
        scrollViewer.Content = mainStack;

        // --- Status Section ---
        AddHeader(mainStack, "Live Status");
        _lblCoords = AddText(mainStack, "");
        _lblZoom = AddText(mainStack, "");
        _lblIters = AddText(mainStack, "");
        _lblAdaptive = AddText(mainStack, "");

        mainStack.Children.Add(new Separator { Background = Brushes.DimGray, Margin = new Thickness(0, 15, 0, 15) });

        // --- Controls Guide ---
        AddHeader(mainStack, "Navigation Controls");
        AddText(mainStack, "🖱️ Mouse:\n" +
                          "  • Left Drag: Orbit\n" +
                          "  • Middle Drag: Pan camera\n" +
                          "  • Scroll: Zoom camera\n");
        
        AddText(mainStack, "⌨️ Mandelbrot:\n" +
                          "  • W/A/S/D / Arrows: Pan fractal\n" +
                          "  • +/- : Zoom fractal\n" +
                          "  • SHIFT: Precise movement\n" +
                          "  • I / K: Iterations (+/-)\n" +
                          "  • O: Toggle Adaptive Res\n" +
                          "  • G: Cycle Grid Res manually\n" +
                          "  • C: Cycle Colors\n" +
                          "  • F: Toggle Wireframe\n" +
                          "  • R: Reset View\n" +
                          "  • 1..6: Location Presets");

        Content = scrollViewer;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += UpdateData!;
        _timer.Start();
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
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 5),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };
        panel.Children.Add(tb);
        return tb;
    }

    private void UpdateData(object sender, EventArgs e)
    {
        _lblCoords.Text = $"📍 Coords: X={_compute.CenterX:F15}\n           Y={_compute.CenterY:F15}";
        _lblZoom.Text = $"🔍 Zoom: {_compute.Zoom:N2}x";
        _lblIters.Text = $"🔄 Max Iterations: {_compute.MaxIterations}";
        
        // Use reflection or a delegate to get private field if needed, 
        // but let's just show the grid resolution from _compute
        _lblAdaptive.Text = $"📏 Grid Resolution: {_compute.Width}x{_compute.Height}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _timer.Stop();
        base.OnClosing(e);
    }
}
