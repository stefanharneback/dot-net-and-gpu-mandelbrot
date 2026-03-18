using System.Windows.Controls;
using System.Windows.Threading;

namespace MandelbrotGpu;

public sealed class StatusHUD : HudWindowBase
{
    private readonly MandelbrotCompute _compute;
    private readonly DispatcherTimer _timer;

    private readonly TextBlock _centerXValue;
    private readonly TextBlock _centerYValue;
    private readonly TextBlock _zoomValue;
    private readonly TextBlock _fractalValue;
    private readonly TextBlock _presetValue;
    private readonly TextBlock _parameterValue;
    private readonly TextBlock _fpsValue;
    private readonly TextBlock _precisionValue;
    private readonly TextBlock _iterationsValue;
    private readonly TextBlock _paletteValue;
    private readonly TextBlock _heightScaleValue;

    private readonly TextBlock _profileValue;
    private readonly TextBlock _computeGridValue;
    private readonly TextBlock _renderMeshValue;
    private readonly TextBlock _adaptiveValue;
    private readonly TextBlock _shadingValue;
    private readonly TextBlock _wireframeValue;
    private readonly TextBlock _gridValue;
    private readonly TextBlock _vsyncValue;
    private readonly TextBlock _msaaValue;

    private readonly TextBlock _kernelValue;
    private readonly TextBlock _gpuSyncValue;
    private readonly TextBlock _readbackValue;
    private readonly TextBlock _uploadValue;
    private readonly TextBlock _gridBuildValue;
    private readonly TextBlock _drawValue;
    private readonly TextBlock _latencyValue;
    private readonly TextBlock _allocValue;

    public StatusHUD(MandelbrotApp app, MandelbrotCompute compute)
        : base(app, "Live Status HUD", 36, 36, 440, 760)
    {
        _compute = compute;

        var root = CreateRootPanel();

        Grid statusGrid = CreateTableGrid();
        int statusRow = 0;
        AddHeaderRow(statusGrid, ref statusRow, "PARAMETER", "CURRENT VALUE", "");
        _centerXValue = AddValueRow(statusGrid, ref statusRow, "Center X", monospaceValue: true);
        _centerYValue = AddValueRow(statusGrid, ref statusRow, "Center Y", monospaceValue: true);
        _zoomValue = AddValueRow(statusGrid, ref statusRow, "Zoom", monospaceValue: true);
        _fractalValue = AddValueRow(statusGrid, ref statusRow, "Fractal set");
        _presetValue = AddValueRow(statusGrid, ref statusRow, "Active preset");
        _parameterValue = AddValueRow(statusGrid, ref statusRow, "Set parameter", monospaceValue: true);
        _fpsValue = AddValueRow(statusGrid, ref statusRow, "FPS", monospaceValue: true);
        _precisionValue = AddValueRow(statusGrid, ref statusRow, "Active precision");
        _iterationsValue = AddValueRow(statusGrid, ref statusRow, "Iterations", monospaceValue: true);
        _paletteValue = AddValueRow(statusGrid, ref statusRow, "Palette");
        _heightScaleValue = AddValueRow(statusGrid, ref statusRow, "Height scale", monospaceValue: true);
        root.Children.Add(CreateCard("Fractal Status", statusGrid));

        Grid renderStateGrid = CreateTableGrid();
        int renderStateRow = 0;
        AddHeaderRow(renderStateGrid, ref renderStateRow, "RENDER STATE", "CURRENT VALUE", "");
        _profileValue = AddValueRow(renderStateGrid, ref renderStateRow, "Profile");
        _computeGridValue = AddValueRow(renderStateGrid, ref renderStateRow, "Compute grid", monospaceValue: true);
        _renderMeshValue = AddValueRow(renderStateGrid, ref renderStateRow, "Render mesh", monospaceValue: true);
        _adaptiveValue = AddValueRow(renderStateGrid, ref renderStateRow, "Adaptive resolution");
        _shadingValue = AddValueRow(renderStateGrid, ref renderStateRow, "Shading");
        _wireframeValue = AddValueRow(renderStateGrid, ref renderStateRow, "Wireframe");
        _gridValue = AddValueRow(renderStateGrid, ref renderStateRow, "Grid overlay");
        _vsyncValue = AddValueRow(renderStateGrid, ref renderStateRow, "VSync");
        _msaaValue = AddValueRow(renderStateGrid, ref renderStateRow, "MSAA");
        root.Children.Add(CreateCard("Render State", renderStateGrid));

        Grid performanceGrid = CreateTableGrid();
        int performanceRow = 0;
        AddHeaderRow(performanceGrid, ref performanceRow, "METRIC", "LATEST SAMPLE", "");
        _kernelValue = AddValueRow(performanceGrid, ref performanceRow, "Kernel dispatch", monospaceValue: true);
        _gpuSyncValue = AddValueRow(performanceGrid, ref performanceRow, "GPU synchronize", monospaceValue: true);
        _readbackValue = AddValueRow(performanceGrid, ref performanceRow, "Readback", monospaceValue: true);
        _uploadValue = AddValueRow(performanceGrid, ref performanceRow, "Texture upload", monospaceValue: true);
        _gridBuildValue = AddValueRow(performanceGrid, ref performanceRow, "Grid rebuild", monospaceValue: true);
        _drawValue = AddValueRow(performanceGrid, ref performanceRow, "Draw submit", monospaceValue: true);
        _latencyValue = AddValueRow(performanceGrid, ref performanceRow, "Interaction latency", monospaceValue: true);
        _allocValue = AddValueRow(performanceGrid, ref performanceRow, "Managed alloc", monospaceValue: true);
        root.Children.Add(CreateCard("Performance", performanceGrid));

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
        PerformanceMetrics metrics = App.LatestPerformanceMetrics;

        _centerXValue.Text = _compute.CenterX.ToString("G17");
        _centerYValue.Text = _compute.CenterY.ToString("G17");
        _zoomValue.Text = $"{_compute.Zoom:0.###e+0}x";
        _fractalValue.Text = App.CurrentFractalName;
        _presetValue.Text = App.CurrentPresetName;
        _parameterValue.Text = App.CurrentFractalParameterSummary;
        _fpsValue.Text = $"{App.FPS:F0}";
        _precisionValue.Text = App.CurrentPrecisionStatus;
        _iterationsValue.Text = _compute.MaxIterations.ToString("N0");
        _paletteValue.Text = App.CurrentPaletteName;
        _heightScaleValue.Text = App.HeightScale.ToString("F2");

        _profileValue.Text = settings.Profile.ToString();
        _computeGridValue.Text = $"{App.ComputeResolution} x {App.ComputeResolution}";
        _renderMeshValue.Text = $"{App.RenderMeshResolution} x {App.RenderMeshResolution}";
        _adaptiveValue.Text = App.AdaptiveResolutionEnabled ? "ON" : "OFF";
        _shadingValue.Text = settings.ShadingMode.ToString();
        _wireframeValue.Text = App.WireframeMode ? "ON" : "OFF";
        _gridValue.Text = App.GridVisible ? "ON" : "OFF";
        _vsyncValue.Text = settings.VSyncEnabled ? "ON" : "OFF";
        _msaaValue.Text = settings.MsaaSamples > 0 ? $"{settings.MsaaSamples}x" : "OFF";

        _kernelValue.Text = $"{metrics.KernelDispatchMs:F2} ms";
        _gpuSyncValue.Text = $"{metrics.GpuSynchronizeMs:F2} ms";
        _readbackValue.Text = $"{metrics.ReadbackMs:F2} ms";
        _uploadValue.Text = $"{metrics.TextureUploadMs:F2} ms";
        _gridBuildValue.Text = $"{metrics.GridBuildMs:F2} ms";
        _drawValue.Text = $"{metrics.DrawMs:F2} ms";
        _latencyValue.Text = $"{metrics.InteractionLatencyMs:F2} ms";
        _allocValue.Text = $"{metrics.ManagedAllocatedBytes / (1024d * 1024d):F2} MB";
    }
}
