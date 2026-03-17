using System.Windows.Controls;
using System.Windows.Threading;

namespace MandelbrotGpu;

public sealed class SettingsHUD : HudWindowBase
{
    private readonly MandelbrotCompute _compute;
    private readonly DispatcherTimer _timer;

    private readonly TextBlock _profileValue;
    private readonly TextBlock _fractalValue;
    private readonly TextBlock _precisionModeValue;
    private readonly TextBlock _iterationsValue;
    private readonly TextBlock _paletteValue;
    private readonly TextBlock _heightScaleValue;
    private readonly TextBlock _wireframeValue;
    private readonly TextBlock _resolutionValue;
    private readonly TextBlock _adaptiveValue;
    private readonly TextBlock _shadingValue;
    private readonly TextBlock _vsyncValue;

    public SettingsHUD(MandelbrotApp app, MandelbrotCompute compute)
        : base(app, "Controls & Settings HUD", 492, 36, 620, 760)
    {
        _compute = compute;

        var root = CreateRootPanel();

        Grid settingsGrid = CreateTableGrid();
        int settingsRow = 0;
        AddHeaderRow(settingsGrid, ref settingsRow, "SETTING", "CURRENT VALUE", "KEYS");
        _profileValue = AddValueRow(settingsGrid, ref settingsRow, "Performance profile", "N");
        _fractalValue = AddValueRow(settingsGrid, ref settingsRow, "Fractal set", "T");
        _precisionModeValue = AddValueRow(settingsGrid, ref settingsRow, "Precision selector", "M");
        _iterationsValue = AddValueRow(settingsGrid, ref settingsRow, "Iterations", "I / K", monospaceValue: true);
        _paletteValue = AddValueRow(settingsGrid, ref settingsRow, "Palette", "C");
        _heightScaleValue = AddValueRow(settingsGrid, ref settingsRow, "Height scale", "PageUp / PageDown", monospaceValue: true);
        _wireframeValue = AddValueRow(settingsGrid, ref settingsRow, "Wireframe", "F");
        _resolutionValue = AddValueRow(settingsGrid, ref settingsRow, "Resolution path", "G / O / N", monospaceValue: true);
        _adaptiveValue = AddValueRow(settingsGrid, ref settingsRow, "Adaptive resolution", "O");
        _shadingValue = AddValueRow(settingsGrid, ref settingsRow, "Shading", "L");
        _vsyncValue = AddValueRow(settingsGrid, ref settingsRow, "VSync", "V");

        var settingsPanel = new StackPanel { Orientation = Orientation.Vertical };
        settingsPanel.Children.Add(CreateNote("T cycles Mandelbrot, Julia, Burning Ship, Tricorn, and Celtic defaults. Hold Shift while using +/- for finer zoom adjustments. H focuses both HUD windows."));
        settingsPanel.Children.Add(settingsGrid);
        root.Children.Add(CreateCard("Adjustable Settings", settingsPanel));

        Grid cameraGrid = CreateTableGrid();
        int cameraRow = 0;
        AddHeaderRow(cameraGrid, ref cameraRow, "ACTION", "INPUT", "NOTE");
        AddStaticRow(cameraGrid, ref cameraRow, "Orbit camera", "Left drag");
        AddStaticRow(cameraGrid, ref cameraRow, "Pan camera", "Middle drag");
        AddStaticRow(cameraGrid, ref cameraRow, "Zoom camera", "Mouse wheel");
        AddStaticRow(cameraGrid, ref cameraRow, "Focus HUD windows", "H");
        root.Children.Add(CreateCard("Camera & Window Controls", cameraGrid));

        Grid fractalGrid = CreateTableGrid();
        int fractalRow = 0;
        AddHeaderRow(fractalGrid, ref fractalRow, "ACTION", "INPUT", "NOTE");
        AddStaticRow(fractalGrid, ref fractalRow, "Pan fractal", "W / A / S / D", "or Arrow keys");
        AddStaticRow(fractalGrid, ref fractalRow, "Zoom fractal", "+ / -", "Shift = fine");
        AddStaticRow(fractalGrid, ref fractalRow, "Print coordinates", "P");
        AddStaticRow(fractalGrid, ref fractalRow, "Reset view", "R");
        root.Children.Add(CreateCard("Fractal Navigation", fractalGrid));

        Grid utilityGrid = CreateTableGrid();
        int utilityRow = 0;
        AddHeaderRow(utilityGrid, ref utilityRow, "ACTION", "INPUT", "NOTE");
        AddStaticRow(utilityGrid, ref utilityRow, "Location presets", "1 .. 6");
        AddStaticRow(utilityGrid, ref utilityRow, "Exit application", "Esc");
        root.Children.Add(CreateCard("Presets & Utility", utilityGrid));

        Content = CreateScrollViewer(root);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += UpdateData;
        _timer.Start();
        UpdateData(this, EventArgs.Empty);
    }

    protected override void OnHudClosing()
    {
        _timer.Stop();
    }

    private void UpdateData(object? sender, EventArgs e)
    {
        PerformanceSettings settings = App.CurrentPerformanceSettings;

        _profileValue.Text = settings.Profile.ToString();
        _fractalValue.Text = App.CurrentFractalName;
        _precisionModeValue.Text = App.CurrentPrecisionMode.ToString();
        _iterationsValue.Text = _compute.MaxIterations.ToString("N0");
        _paletteValue.Text = App.CurrentPaletteName;
        _heightScaleValue.Text = App.HeightScale.ToString("F2");
        _wireframeValue.Text = App.WireframeMode ? "ON" : "OFF";
        _resolutionValue.Text = $"{App.ComputeResolution} -> {App.RenderMeshResolution}";
        _adaptiveValue.Text = App.AdaptiveResolutionEnabled ? "ON" : "OFF";
        _shadingValue.Text = settings.ShadingMode.ToString();
        _vsyncValue.Text = settings.VSyncEnabled ? "ON" : "OFF";
    }
}
