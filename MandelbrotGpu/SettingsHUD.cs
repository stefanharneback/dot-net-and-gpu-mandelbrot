using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; // Added for Key handling
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
    private readonly TextBlock _lblStatus; // Combined status
    private readonly TextBlock _lblComputing; // GPU activity

    public SettingsHUD(MandelbrotApp app, MandelbrotCompute compute)
    {
        _app = app;
        _compute = compute;

        Title = "🌀 Settings & Controls HUD";
        Width = 500; // Increased width
        Height = 700; // Increased height
        WindowStyle = WindowStyle.ToolWindow;
        Topmost = true;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(25, 25, 30));
        Foreground = Brushes.White;

        Left = 50;
        Top = 50;

        // --- Fix for focus-disconnected keys ---
        KeyDown += OnHudKeyDown; 

        var scrollViewer = new ScrollViewer 
        { 
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(15)
        };

        var mainStack = new StackPanel();
        scrollViewer.Content = mainStack;

        // --- Status Section ---
        AddHeader(mainStack, "Live Status");
        _lblComputing = AddText(mainStack, "");
        _lblComputing.FontWeight = FontWeights.Bold;
        _lblComputing.FontSize = 14;
        
        _lblCoords = AddText(mainStack, "");
        _lblZoom = AddText(mainStack, "");
        _lblIters = AddText(mainStack, "");
        _lblStatus = AddText(mainStack, "");

        mainStack.Children.Add(new Separator { Background = Brushes.DimGray, Margin = new Thickness(0, 15, 0, 15) });

        // --- Controls Guide ---
        AddHeader(mainStack, "Navigation Controls");
        
        AddText(mainStack, "⌨️ Mandelbrot:\n" +
                          "  • W/A/S/D / Arrows: Pan fractal\n" +
                          "  • +/- : Zoom fractal\n" +
                          "  • SHIFT: Precise movement\n" +
                          "  • I / K: Max Iterations\n" +
                          "  • M: Cycle Precision (Auto/FP32/FP64)\n" +
                          "  • O: Toggle Adaptive Res\n" +
                          "  • G: Cycle Grid Res manually\n" +
                          "  • C: Cycle Colors\n" +
                          "  • F: Toggle Wireframe\n" +
                          "  • R: Reset View\n" +
                          "  • 1..6: Location Presets\n");

        AddText(mainStack, "🖱️ Mouse:\n" +
                          "  • Left Drag: Orbit\n" +
                          "  • Middle Drag: Pan camera\n" +
                          "  • Scroll: Zoom camera\n");

        Content = scrollViewer;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += UpdateData!;
        _timer.Start();
    }

    private void OnHudKeyDown(object sender, KeyEventArgs e)
    {
        // Convert WPF key to Silk.NET key and pass to main app
        // This is a simplified bridge
        var silkKey = MapToSilkKey(e.Key);
        if (silkKey != Silk.NET.Input.Key.Unknown)
        {
            _app.HandleExternalKeyDown(silkKey);
        }
    }

    private Silk.NET.Input.Key MapToSilkKey(Key wpfKey)
    {
        return wpfKey switch
        {
            Key.W => Silk.NET.Input.Key.W,
            Key.A => Silk.NET.Input.Key.A,
            Key.S => Silk.NET.Input.Key.S,
            Key.D => Silk.NET.Input.Key.D,
            Key.Up => Silk.NET.Input.Key.Up,
            Key.Down => Silk.NET.Input.Key.Down,
            Key.Left => Silk.NET.Input.Key.Left,
            Key.Right => Silk.NET.Input.Key.Right,
            Key.Add => Silk.NET.Input.Key.KeypadAdd,
            Key.OemPlus => Silk.NET.Input.Key.Equal,
            Key.Subtract => Silk.NET.Input.Key.KeypadSubtract,
            Key.OemMinus => Silk.NET.Input.Key.Minus,
            Key.I => Silk.NET.Input.Key.I,
            Key.K => Silk.NET.Input.Key.K,
            Key.M => Silk.NET.Input.Key.M,
            Key.O => Silk.NET.Input.Key.O,
            Key.G => Silk.NET.Input.Key.G,
            Key.C => Silk.NET.Input.Key.C,
            Key.F => Silk.NET.Input.Key.F,
            Key.R => Silk.NET.Input.Key.R,
            Key.P => Silk.NET.Input.Key.P,
            Key.D1 => Silk.NET.Input.Key.Number1,
            Key.D2 => Silk.NET.Input.Key.Number2,
            Key.D3 => Silk.NET.Input.Key.Number3,
            Key.D4 => Silk.NET.Input.Key.Number4,
            Key.D5 => Silk.NET.Input.Key.Number5,
            Key.D6 => Silk.NET.Input.Key.Number6,
            Key.Escape => Silk.NET.Input.Key.Escape,
            _ => Silk.NET.Input.Key.Unknown
        };
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
        if (_compute.IsComputing)
        {
            _lblComputing.Text = "⚡ GPU: CALCULATING...";
            _lblComputing.Foreground = Brushes.Lime;
        }
        else
        {
            _lblComputing.Text = "💤 GPU: IDLE";
            _lblComputing.Foreground = Brushes.Gray;
        }

        _lblCoords.Text = $"📍 X = {_compute.CenterX:F15}\n    Y = {_compute.CenterY:F15}";
        _lblZoom.Text = $"🔍 Zoom: {_compute.Zoom:0.##e+0}x";
        _lblIters.Text = $"🔄 Max Iterations: {_compute.MaxIterations:N0}";
        
        _lblStatus.Text = $"📏 Resolution: {_compute.Width}x{_compute.Height}\n" +
                          $"🛠️ Adaptive Res: {(_app.IsAdaptiveResolutionEnabled ? "ON" : "OFF")}\n" +
                          $"⚡ Precision: {_compute.PrecisionStatus}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _timer.Stop();
        base.OnClosing(e);
    }
}
