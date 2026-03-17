using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace MandelbrotGpu;

/// <summary>
/// Main application class: creates a window, manages OpenGL rendering,
/// handles input, and orchestrates GPU fractal computation + 3D visualization.
/// </summary>
public sealed class MandelbrotApp : IDisposable
{
    private const int MaxWindowMsaaSamples = 4;
    private const int RequiredOpenGlMajorVersion = 4;
    private const int RequiredOpenGlMinorVersion = 3;
    private const int LargeIterationThreshold = 1000;
    private const int IterationLinearStep = 128;
    private const int MaxIterationLimit = 2_000_000;
    private const int MinimumHudWidth = 360;
    private const int PreferredHudWidth = 520;
    private const int FallbackHudWidth = 300;

    private readonly ResolutionTier[] _resolutionTiers =
    [
        new(512, 384),
        new(768, 512),
        new(1024, 768),
        new(1536, 1024),
        new(2048, 1536),
        new(3072, 2048),
        new(4096, 2048)
    ];

    private readonly FractalDefinition[] _fractalDefinitions = FractalCatalog.Definitions;
    private readonly Camera _camera = new();
    private readonly ConcurrentQueue<AppCommand> _pendingCommands = new();
    private readonly string[] _paletteNames = ["Vibrant", "Fire", "Ocean", "Neon"];
    private readonly object _hudWindowLock = new();

    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private MandelbrotCompute _compute = null!;
    private HeightFieldRenderer _terrainRenderer = null!;

    private uint _terrainShaderProgram;
    private uint _gridShaderProgram;
    private uint _gridVao;
    private uint _gridVbo;
    private int _gridVertexCount;
    private int _gridViewProjectionLocation;

    private PerformanceSettings _performanceSettings;
    private int _resolutionTierIndex;
    private int _currentFractalIndex;
    private PerformanceMetrics _latestMetrics = PerformanceMetrics.Empty;
    private HeightFieldFrame? _currentFrame;

    private float[] _palette = null!;
    private int _currentPalette;
    private float _paletteCycles = ColorPalette.GetPaletteCycles(0);
    private float _heightScale = 0.6f;
    private bool _needsRecompute = true;
    private bool _paletteDirty = true;
    private bool _wireframeMode;

    private bool _leftMouseDown;
    private bool _middleMouseDown;
    private Vector2 _lastMousePos;

    private float _fps;
    private int _frameCount;
    private double _fpsTimer;

    private StartupLayout _startupLayout;
    private SettingsHUD? _settingsHud;
    private StatusHUD? _statusHud;
    private Application? _hudApplication;
    private Thread? _hudThread;
    private volatile bool _isHudStarting;
    private volatile bool _isClosing;
    private string _glVendor = "Unavailable";
    private string _glRenderer = "Unavailable";
    private string _glVersion = "Unavailable";
    private int _glVersionMajor;
    private int _glVersionMinor;
    private InputSource _lastInputSource = InputSource.MainWindow;
    private string _lastCommandKey = "None";

    public MandelbrotApp()
    {
        _performanceSettings = PerformanceSettings.Create(PerformanceProfile.Latency);
        _resolutionTierIndex = GetDefaultTierIndex(_performanceSettings.Profile);
    }

    public PerformanceSettings CurrentPerformanceSettings => _performanceSettings;

    public PerformanceMetrics LatestPerformanceMetrics => _latestMetrics;

    public bool AdaptiveResolutionEnabled => _performanceSettings.AdaptiveResolutionEnabled;

    public FractalDefinition CurrentFractal => _fractalDefinitions[_currentFractalIndex];

    public string CurrentFractalName => CurrentFractal.DisplayName;

    public string CurrentFractalParameterSummary => CurrentFractal.ParameterSummary;

    public string CurrentPaletteName => _paletteNames[_currentPalette];

    public int ComputeResolution => _compute?.Width ?? _performanceSettings.ComputeResolution;

    public float HeightScale => _heightScale;

    public float FPS => _fps;

    public int RenderMeshResolution => _terrainRenderer?.RenderMeshResolution ?? _performanceSettings.RenderMeshResolution;

    public bool GridVisible => _performanceSettings.ShowGrid;

    public bool WireframeMode => _wireframeMode;

    public PrecisionMode CurrentPrecisionMode => _compute?.CurrentPrecisionMode ?? PrecisionMode.Auto;

    public string CurrentPrecisionStatus => _currentFrame?.PrecisionMode ?? _compute?.PrecisionStatus ?? PrecisionMode.Auto.ToString();

    public string BuildDiagnosticSnapshot()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"GL vendor: {_glVendor}");
        builder.AppendLine($"GL renderer: {_glRenderer}");
        builder.AppendLine($"GL version: {_glVersion}");
        builder.AppendLine($"GL version parsed: {_glVersionMajor}.{_glVersionMinor}");
        builder.AppendLine($"Current fractal: {CurrentFractalName}");
        builder.AppendLine($"Fractal parameter: {CurrentFractalParameterSummary}");
        builder.AppendLine($"Performance profile: {_performanceSettings.Profile}");
        builder.AppendLine($"Precision mode: {CurrentPrecisionMode}");
        builder.AppendLine($"Precision status: {CurrentPrecisionStatus}");
        builder.AppendLine($"Compute resolution: {(_compute != null ? $"{_compute.Width} x {_compute.Height}" : "not initialized")}");
        builder.AppendLine($"Render resolution: {RenderMeshResolution} x {RenderMeshResolution}");
        builder.AppendLine($"Iterations: {(_compute != null ? _compute.MaxIterations.ToString("N0") : "not initialized")}");
        builder.AppendLine($"Zoom: {(_compute != null ? _compute.Zoom.ToString("G17") : "not initialized")}");
        builder.AppendLine($"Center: {(_compute != null ? $"{_compute.CenterX:G17}, {_compute.CenterY:G17}" : "not initialized")}");
        builder.AppendLine($"Last input source: {_lastInputSource}");
        builder.AppendLine($"Last command key: {_lastCommandKey}");
        builder.AppendLine($"Queued external commands: {_pendingCommands.Count}");
        builder.AppendLine($"Is closing: {_isClosing}");
        if (_window != null)
            builder.AppendLine($"Window size: {_window.Size.X} x {_window.Size.Y}");

        return builder.ToString().TrimEnd();
    }

    public void Run()
    {
        _startupLayout = CalculateStartupLayout();

        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(_startupLayout.MainSize, _startupLayout.MainSize);
        options.Position = new Vector2D<int>(_startupLayout.MainLeft, _startupLayout.MainTop);
        options.Title = "GPU Fractal 3D Explorer";
        options.VSync = _performanceSettings.VSyncEnabled;
        options.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(RequiredOpenGlMajorVersion, RequiredOpenGlMinorVersion));
        options.PreferredDepthBufferBits = 24;
        options.Samples = MaxWindowMsaaSamples;

        _window = Silk.NET.Windowing.Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;

        _window.Run();
    }

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        CaptureGraphicsInfo();
        ValidateGraphicsCapabilities();
        _input = _window.CreateInput();

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.02f, 0.02f, 0.05f, 1.0f);

        _compute = new MandelbrotCompute(_gl, _performanceSettings.ComputeResolution, _performanceSettings.ComputeResolution);
        ApplyFractalDefinition(_currentFractalIndex, logToConsole: false);
        _palette = ColorPalette.GeneratePalette(_currentPalette);
        _paletteCycles = ColorPalette.GetPaletteCycles(_currentPalette);

        _terrainShaderProgram = CreateShaderProgram(Shaders.TerrainVertexShader, Shaders.TerrainFragmentShader);
        _gridShaderProgram = CreateShaderProgram(Shaders.GridVertexShader, Shaders.GridFragmentShader);
        _gridViewProjectionLocation = _gl.GetUniformLocation(_gridShaderProgram, "uViewProjection");

        _terrainRenderer = new HeightFieldRenderer(_gl, _terrainShaderProgram);
        _terrainRenderer.SetRenderMeshResolution(_performanceSettings.RenderMeshResolution);
        _terrainRenderer.UploadPalette(_palette);
        _paletteDirty = false;

        CreateGridOverlay();
        SetupInput();
        ApplyRuntimePerformanceSettings(logToConsole: false);
        RecomputeFractal();
        UpdateHudVisibility();

        PrintControls();
    }

    private void SetupInput()
    {
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }

        foreach (var keyboard in _input.Keyboards)
            keyboard.KeyDown += OnKeyDown;
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMouseDown = true;

        if (button == MouseButton.Middle)
            _middleMouseDown = true;

        _lastMousePos = mouse.Position;
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMouseDown = false;

        if (button == MouseButton.Middle)
            _middleMouseDown = false;
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        Vector2 delta = position - _lastMousePos;
        _lastMousePos = position;

        if (_leftMouseDown)
            _camera.Orbit(delta.X, -delta.Y);
        else if (_middleMouseDown)
            _camera.Pan(delta.X, delta.Y);
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        _camera.Zoom(scroll.Y);
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
        HandleCommand(new AppCommand(key, shift, InputSource.MainWindow));
    }

    public void HandleExternalKeyDown(Key key, bool shiftPressed)
    {
        if (_isClosing)
            return;

        _pendingCommands.Enqueue(new AppCommand(key, shiftPressed, InputSource.Hud));
    }

    private void HandleCommand(AppCommand command)
    {
        if (_isClosing)
            return;

        _lastInputSource = command.Source;
        _lastCommandKey = command.Key.ToString();

        Key key = command.Key;
        bool shift = command.Shift;
        double panAmount = shift ? 0.005 : 0.05;
        double zoomFactor = shift ? 1.05 : 1.3;

        switch (key)
        {
            case Key.Left:
            case Key.A:
                _compute.CenterX -= panAmount / _compute.Zoom;
                MarkDirty();
                break;
            case Key.Right:
            case Key.D:
                _compute.CenterX += panAmount / _compute.Zoom;
                MarkDirty();
                break;
            case Key.Up:
            case Key.W:
                _compute.CenterY += panAmount / _compute.Zoom;
                MarkDirty();
                break;
            case Key.Down:
            case Key.S:
                _compute.CenterY -= panAmount / _compute.Zoom;
                MarkDirty();
                break;

            case Key.Equal:
            case Key.KeypadAdd:
                _compute.Zoom *= zoomFactor;
                MarkDirty();
                break;
            case Key.Minus:
            case Key.KeypadSubtract:
                _compute.Zoom /= zoomFactor;
                MarkDirty();
                break;

            case Key.PageUp:
                _heightScale = Math.Min(_heightScale + 0.1f, 3.0f);
                Console.WriteLine($"Height scale: {_heightScale:F2}");
                break;
            case Key.PageDown:
                _heightScale = Math.Max(_heightScale - 0.1f, 0.05f);
                Console.WriteLine($"Height scale: {_heightScale:F2}");
                break;

            case Key.I:
                _compute.MaxIterations = _compute.MaxIterations < LargeIterationThreshold
                    ? Math.Min(_compute.MaxIterations + IterationLinearStep, MaxIterationLimit)
                    : Math.Min(_compute.MaxIterations * 2, MaxIterationLimit);
                MarkDirty();
                Console.WriteLine($"Max iterations: {_compute.MaxIterations:N0}");
                break;
            case Key.K:
                _compute.MaxIterations = _compute.MaxIterations <= LargeIterationThreshold
                    ? Math.Max(32, _compute.MaxIterations - IterationLinearStep)
                    : Math.Max(LargeIterationThreshold, _compute.MaxIterations / 2);
                MarkDirty();
                Console.WriteLine($"Max iterations: {_compute.MaxIterations:N0}");
                break;

            case Key.C:
                _currentPalette = (_currentPalette + 1) % _paletteNames.Length;
                _palette = ColorPalette.GeneratePalette(_currentPalette);
                _paletteCycles = ColorPalette.GetPaletteCycles(_currentPalette);
                _paletteDirty = true;
                Console.WriteLine($"Palette: {_paletteNames[_currentPalette]}");
                break;

            case Key.F:
                _wireframeMode = !_wireframeMode;
                Console.WriteLine($"Wireframe: {(_wireframeMode ? "ON" : "OFF")}");
                break;

            case Key.G:
                CycleResolutionTier();
                break;

            case Key.H:
                FocusHudWindows();
                break;

            case Key.L:
                CycleShadingMode();
                break;

            case Key.M:
                CyclePrecisionMode();
                break;

            case Key.N:
                CyclePerformanceProfile();
                break;

            case Key.O:
                SetAdaptiveResolution(!_performanceSettings.AdaptiveResolutionEnabled);
                break;

            case Key.P:
                Console.WriteLine(
                    $"Fractal: {CurrentFractalName} | {CurrentFractalParameterSummary} | " +
                    $"Coords: X: {_compute.CenterX}, Y: {_compute.CenterY} | Zoom: {_compute.Zoom} | Iter: {_compute.MaxIterations} | " +
                    $"Profile: {_performanceSettings.Profile} | Compute: {_compute.Width} | Render: {RenderMeshResolution}");
                break;

            case Key.T:
                CycleFractalSet();
                break;

            case Key.V:
                SetVSyncEnabled(!_performanceSettings.VSyncEnabled);
                break;

            case Key.Number1:
                SetLocation(-0.5, 0.0, 1.0, 256);
                break;
            case Key.Number2:
                SetLocation(-0.743643887037151, 0.13182590420533, 10000.0, 1000);
                break;
            case Key.Number3:
                SetLocation(0.27322626, 0.595153338, 2000.0, 1000);
                break;
            case Key.Number4:
                SetLocation(-0.088, 0.654, 50.0, 500);
                break;
            case Key.Number5:
                SetLocation(-0.7436447860, 0.1318252536, 1000000.0, 2000);
                break;
            case Key.Number6:
                SetLocation(-0.7436447860, 0.1318252536, 100000000000.0, 10000);
                break;

            case Key.R:
                ResetCurrentFractalView();
                break;

            case Key.Escape:
                _window.Close();
                break;
        }
    }

    private void SetLocation(double centerX, double centerY, double zoom, int maxIterations)
    {
        _compute.CenterX = centerX;
        _compute.CenterY = centerY;
        _compute.Zoom = zoom;
        _compute.MaxIterations = maxIterations;
        MarkDirty();
        Console.WriteLine(
            $"Location set -> {CurrentFractalName} | X: {centerX}, Y: {centerY} | Zoom: {zoom}x | Iter: {maxIterations}");
    }

    private void OnUpdate(double deltaTime)
    {
        DrainPendingCommands();

        _frameCount++;
        _fpsTimer += deltaTime;
        if (_fpsTimer >= 1.0)
        {
            _fps = (float)(_frameCount / _fpsTimer);
            _window.Title =
                $"{CurrentFractalName} 3D | {_fps:F0} FPS | {_performanceSettings.Profile} | Compute {_compute.Width} | Render {RenderMeshResolution} | " +
                $"{(_currentFrame?.PrecisionMode ?? _compute.PrecisionStatus)} | Latency {_latestMetrics.InteractionLatencyMs:F1} ms";
            _frameCount = 0;
            _fpsTimer = 0;
        }

        if (_paletteDirty)
        {
            _terrainRenderer.UploadPalette(_palette);
            _paletteDirty = false;
        }

        if (_performanceSettings.AdaptiveResolutionEnabled)
            UpdateAdaptiveResolution();

        if (_needsRecompute)
            RecomputeFractal();
    }

    private void OnRender(double deltaTime)
    {
        if (_isClosing)
            return;

        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_currentFrame == null)
            return;

        long drawStart = Stopwatch.GetTimestamp();

        float aspect = (float)_window.Size.X / _window.Size.Y;
        Matrix4x4 view = _camera.GetViewMatrix();
        Matrix4x4 projection = Camera.GetProjectionMatrix(aspect);
        Matrix4x4 model = Matrix4x4.Identity;

        _terrainRenderer.Render(
            model,
            view,
            projection,
            Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.3f)),
            _camera.Position,
            _currentFrame,
            _heightScale,
            _paletteCycles,
            _performanceSettings.ShadingMode,
            _wireframeMode);

        if (_performanceSettings.ShowGrid)
        {
            _gl.UseProgram(_gridShaderProgram);
            Matrix4x4 viewProjection = view * projection;
            SetUniformMatrix4(_gridViewProjectionLocation, viewProjection);

            _gl.BindVertexArray(_gridVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);
        }

        _latestMetrics = _latestMetrics with
        {
            DrawMs = Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds
        };
    }

    private void RecomputeFractal()
    {
        long recomputeStart = Stopwatch.GetTimestamp();
        long allocatedBefore = GC.GetTotalAllocatedBytes(false);

        _terrainRenderer.EnsureTextureSize(_compute.Width, _compute.Height);
        HeightFieldFrame frame = _compute.Compute(_terrainRenderer.HeightTexture);
        _currentFrame = frame;

        _latestMetrics = _latestMetrics with
        {
            KernelDispatchMs = frame.KernelDispatchMs,
            GpuSynchronizeMs = frame.GpuSynchronizeMs,
            ReadbackMs = frame.ReadbackMs,
            TextureUploadMs = frame.TextureUploadMs,
            GridBuildMs = _terrainRenderer.LastGridBuildMs,
            InteractionLatencyMs = Stopwatch.GetElapsedTime(recomputeStart).TotalMilliseconds,
            ManagedAllocatedBytes = GC.GetTotalAllocatedBytes(false) - allocatedBefore
        };

        _needsRecompute = false;
    }

    private void CycleFractalSet()
    {
        int nextIndex = (_currentFractalIndex + 1) % _fractalDefinitions.Length;
        ApplyFractalDefinition(nextIndex);
    }

    private void CyclePerformanceProfile()
    {
        PerformanceProfile nextProfile = _performanceSettings.Profile switch
        {
            PerformanceProfile.Latency => PerformanceProfile.Balanced,
            PerformanceProfile.Balanced => PerformanceProfile.Quality,
            PerformanceProfile.Quality => PerformanceProfile.Screenshot,
            _ => PerformanceProfile.Latency
        };

        ApplyPerformanceProfile(nextProfile);
    }

    private void CyclePrecisionMode()
    {
        _compute.CurrentPrecisionMode = _compute.CurrentPrecisionMode switch
        {
            PrecisionMode.Auto => PrecisionMode.ForceFP32,
            PrecisionMode.ForceFP32 => PrecisionMode.ForceFP64,
            _ => PrecisionMode.Auto
        };

        MarkDirty();
        Console.WriteLine($"Precision Mode: {_compute.CurrentPrecisionMode}");
    }

    private void ApplyFractalDefinition(int fractalIndex, bool logToConsole = true)
    {
        _currentFractalIndex = fractalIndex;
        FractalDefinition definition = CurrentFractal;

        _compute.FractalType = definition.Kind;
        _compute.JuliaConstantX = definition.JuliaConstantX;
        _compute.JuliaConstantY = definition.JuliaConstantY;

        ResetCurrentFractalView(logToConsole: false);

        if (logToConsole)
            Console.WriteLine($"Fractal set: {definition.DisplayName} | {definition.ParameterSummary}");
    }

    private void ResetCurrentFractalView(bool logToConsole = true)
    {
        FractalDefinition definition = CurrentFractal;

        _compute.CenterX = definition.DefaultCenterX;
        _compute.CenterY = definition.DefaultCenterY;
        _compute.Zoom = definition.DefaultZoom;
        _compute.MaxIterations = definition.DefaultIterations;
        _heightScale = definition.DefaultHeightScale;
        MarkDirty();

        if (logToConsole)
        {
            Console.WriteLine(
                $"View reset -> {definition.DisplayName} | X: {_compute.CenterX}, Y: {_compute.CenterY} | " +
                $"Zoom: {_compute.Zoom}x | Iter: {_compute.MaxIterations}");
        }
    }

    private void ApplyPerformanceProfile(PerformanceProfile profile)
    {
        _performanceSettings = PerformanceSettings.Create(profile);
        _resolutionTierIndex = GetDefaultTierIndex(profile);
        ApplyResolutionTier(_resolutionTierIndex);
        ApplyRuntimePerformanceSettings(logToConsole: false);

        Console.WriteLine(
            $"Profile: {profile} | Compute: {_performanceSettings.ComputeResolution} | Render: {_performanceSettings.RenderMeshResolution} | " +
            $"Shading: {_performanceSettings.ShadingMode} | HUD: {(_performanceSettings.HudEnabled ? "ON" : "OFF")}");
    }

    private void CycleResolutionTier()
    {
        if (_performanceSettings.AdaptiveResolutionEnabled)
            _performanceSettings = _performanceSettings with { AdaptiveResolutionEnabled = false };

        var bounds = GetTierBounds(_performanceSettings.Profile);
        int nextTier = _resolutionTierIndex + 1;
        if (nextTier > bounds.Max)
            nextTier = bounds.Min;

        ApplyResolutionTier(nextTier);
        Console.WriteLine(
            $"Adaptive Resolution: OFF | Compute: {_performanceSettings.ComputeResolution} | Render: {_performanceSettings.RenderMeshResolution}");
    }

    private void ApplyResolutionTier(int tierIndex)
    {
        ResolutionTier tier = _resolutionTiers[tierIndex];
        bool computeChanged = _compute != null &&
            (_compute.Width != tier.ComputeResolution || _compute.Height != tier.ComputeResolution);
        bool renderChanged = _terrainRenderer != null &&
            _terrainRenderer.RenderMeshResolution != tier.RenderMeshResolution;

        _resolutionTierIndex = tierIndex;
        _performanceSettings = _performanceSettings with
        {
            ComputeResolution = tier.ComputeResolution,
            RenderMeshResolution = tier.RenderMeshResolution
        };

        if (renderChanged)
            _terrainRenderer!.SetRenderMeshResolution(tier.RenderMeshResolution);

        if (computeChanged)
        {
            _compute!.Resize(tier.ComputeResolution, tier.ComputeResolution);
            MarkDirty();
        }
    }

    private void UpdateAdaptiveResolution(bool force = false)
    {
        if (_performanceSettings.Profile == PerformanceProfile.Screenshot)
            return;

        var bounds = GetTierBounds(_performanceSettings.Profile);
        if (bounds.Max <= bounds.Default)
            return;

        double logZoom = Math.Log10(Math.Max(1.0, _compute.Zoom));
        double zoomFactor = Math.Clamp(logZoom / 3.0, 0.0, 1.0);
        int desiredTier = bounds.Default + (int)Math.Round((bounds.Max - bounds.Default) * zoomFactor);
        desiredTier = Math.Clamp(desiredTier, bounds.Min, bounds.Max);

        if (desiredTier == _resolutionTierIndex)
            return;

        int currentCompute = _performanceSettings.ComputeResolution;
        int desiredCompute = _resolutionTiers[desiredTier].ComputeResolution;
        bool hysteresisTriggered = desiredCompute >= currentCompute * 1.25 || desiredCompute <= currentCompute * 0.75;
        if (force || hysteresisTriggered)
        {
            ApplyResolutionTier(desiredTier);
            Console.WriteLine($"Adaptive resolution -> Compute: {_performanceSettings.ComputeResolution} | Render: {_performanceSettings.RenderMeshResolution}");
        }
    }

    private void SetAdaptiveResolution(bool enabled)
    {
        _performanceSettings = _performanceSettings with { AdaptiveResolutionEnabled = enabled };
        Console.WriteLine($"Adaptive Resolution: {(enabled ? "ON" : "OFF")}");

        if (enabled)
            UpdateAdaptiveResolution(force: true);
    }

    private void CycleShadingMode()
    {
        ShadingMode nextMode = _performanceSettings.ShadingMode == ShadingMode.Full
            ? ShadingMode.Simplified
            : ShadingMode.Full;

        _performanceSettings = _performanceSettings with { ShadingMode = nextMode };
        Console.WriteLine($"Shading: {nextMode}");
    }

    private void SetVSyncEnabled(bool enabled)
    {
        _performanceSettings = _performanceSettings with { VSyncEnabled = enabled };
        if (_window != null)
            _window.VSync = enabled;

        Console.WriteLine($"VSync: {(enabled ? "ON" : "OFF")}");
    }

    private void SetHudEnabled(bool enabled)
    {
        _performanceSettings = _performanceSettings with { HudEnabled = enabled };
        UpdateHudVisibility();
        Console.WriteLine($"HUD: {(enabled ? "ON" : "OFF")}");
    }

    private void ApplyRuntimePerformanceSettings(bool logToConsole = true)
    {
        if (_isClosing)
            return;

        if (_window != null)
            _window.VSync = _performanceSettings.VSyncEnabled;

        if (_gl != null)
        {
            if (_performanceSettings.MsaaSamples > 0)
                _gl.Enable(EnableCap.Multisample);
            else
                _gl.Disable(EnableCap.Multisample);
        }

        UpdateHudVisibility();

        if (logToConsole)
        {
            Console.WriteLine(
                $"Runtime settings -> VSync: {(_performanceSettings.VSyncEnabled ? "ON" : "OFF")} | " +
                $"MSAA: {(_performanceSettings.MsaaSamples > 0 ? $"{_performanceSettings.MsaaSamples}x" : "OFF")} | " +
                $"Grid: {(_performanceSettings.ShowGrid ? "ON" : "OFF")}");
        }
    }

    private void UpdateHudVisibility()
    {
        if (_isClosing || _compute == null)
            return;

        if (_performanceSettings.HudEnabled)
            ShowHudWindows();
        else
            CloseHudWindows();
    }

    private void FocusHudWindows()
    {
        if (_isClosing)
            return;

        _performanceSettings = _performanceSettings with { HudEnabled = true };
        ShowHudWindows();
        Console.WriteLine("HUD windows focused");
    }

    private void ShowHudWindows()
    {
        if (_isClosing || _compute == null)
            return;

        lock (_hudWindowLock)
        {
            if (_settingsHud != null && _statusHud != null)
            {
                _statusHud.FocusHud();
                _settingsHud.FocusHud();
                return;
            }

            if (_isHudStarting)
                return;

            _isHudStarting = true;
            _hudThread = new Thread(() =>
            {
                try
                {
                    RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

                    var app = new Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };

                    var statusHud = new StatusHUD(this, _compute);
                    var settingsHud = new SettingsHUD(this, _compute);
                    ApplyHudLayout(statusHud, settingsHud);

                    void HandleWindowClosed()
                    {
                        bool shouldShutdown;
                        lock (_hudWindowLock)
                        {
                            if (ReferenceEquals(_statusHud, statusHud))
                                _statusHud = null;

                            if (ReferenceEquals(_settingsHud, settingsHud))
                                _settingsHud = null;

                            shouldShutdown = _statusHud == null && _settingsHud == null;
                            if (shouldShutdown)
                            {
                                _hudApplication = null;
                                _hudThread = null;
                                _isHudStarting = false;
                            }
                        }

                        if (shouldShutdown && !app.Dispatcher.HasShutdownStarted)
                            app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }

                    statusHud.Closed += (_, _) => HandleWindowClosed();
                    settingsHud.Closed += (_, _) => HandleWindowClosed();

                    lock (_hudWindowLock)
                    {
                        _hudApplication = app;
                        _statusHud = statusHud;
                        _settingsHud = settingsHud;
                        _hudThread = Thread.CurrentThread;
                        _isHudStarting = false;
                    }

                    statusHud.Show();
                    settingsHud.Show();
                    app.Run();
                }
                catch (Exception ex)
                {
                    lock (_hudWindowLock)
                    {
                        _hudApplication = null;
                        _statusHud = null;
                        _settingsHud = null;
                        _hudThread = null;
                        _isHudStarting = false;
                    }

                    Console.WriteLine($"HUD failed to start: {ex.Message}");
                }
            });

            _hudThread.SetApartmentState(ApartmentState.STA);
            _hudThread.IsBackground = true;
            _hudThread.Start();
        }
    }

    private void CloseHudWindows()
    {
        if (_isClosing && _hudApplication == null)
            return;

        Application? hudApplication;
        StatusHUD? statusHud;
        SettingsHUD? settingsHud;
        lock (_hudWindowLock)
        {
            hudApplication = _hudApplication;
            statusHud = _statusHud;
            settingsHud = _settingsHud;
        }

        if (hudApplication == null || hudApplication.Dispatcher.HasShutdownStarted)
            return;

        hudApplication.Dispatcher.BeginInvoke(new Action(() =>
        {
            statusHud?.BeginClose();
            settingsHud?.BeginClose();
        }));
    }

    private unsafe void CreateGridOverlay()
    {
        const int gridSize = 10;
        const float gridStep = 0.2f;
        var gridVertices = new List<float>();

        for (int i = -gridSize; i <= gridSize; i++)
        {
            float pos = i * gridStep;
            float alpha = 1f - MathF.Abs(i) / gridSize;
            float c = 0.15f * alpha;

            gridVertices.AddRange([pos, -0.01f, -gridSize * gridStep, c, c, c * 1.5f]);
            gridVertices.AddRange([pos, -0.01f, gridSize * gridStep, c, c, c * 1.5f]);
            gridVertices.AddRange([-gridSize * gridStep, -0.01f, pos, c, c, c * 1.5f]);
            gridVertices.AddRange([gridSize * gridStep, -0.01f, pos, c, c, c * 1.5f]);
        }

        float[] gridArray = gridVertices.ToArray();
        _gridVertexCount = gridArray.Length / 6;

        _gridVao = _gl.GenVertexArray();
        _gridVbo = _gl.GenBuffer();

        _gl.BindVertexArray(_gridVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);

        fixed (float* vertices = gridArray)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(gridArray.Length * sizeof(float)), vertices, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    private void OnResize(Vector2D<int> size)
    {
        if (_isClosing)
            return;

        _gl.Viewport(size);
    }

    private void OnClosing()
    {
        _isClosing = true;
        CloseHudWindows();
        _terrainRenderer?.Dispose();

        _gl.DeleteVertexArray(_gridVao);
        _gl.DeleteBuffer(_gridVbo);
        _gl.DeleteProgram(_terrainShaderProgram);
        _gl.DeleteProgram(_gridShaderProgram);
    }

    private uint CreateShaderProgram(string vertexSource, string fragmentSource)
    {
        uint vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        uint fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetProgramInfoLog(program);
            throw new Exception($"Shader program link failed: {info}");
        }

        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);

        return program;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetShaderInfoLog(shader);
            throw new Exception($"{type} compilation failed: {info}");
        }

        return shader;
    }

    private unsafe void SetUniformMatrix4(int location, Matrix4x4 matrix)
    {
        if (location >= 0)
            _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
    }

    private void DrainPendingCommands()
    {
        while (!_isClosing && _pendingCommands.TryDequeue(out AppCommand command))
            HandleCommand(command);
    }

    private void CaptureGraphicsInfo()
    {
        _glVendor = _gl.GetStringS(StringName.Vendor) ?? "Unavailable";
        _glRenderer = _gl.GetStringS(StringName.Renderer) ?? "Unavailable";
        _glVersion = _gl.GetStringS(StringName.Version) ?? "Unavailable";

        try
        {
            _gl.GetInteger(GetPName.MajorVersion, out _glVersionMajor);
            _gl.GetInteger(GetPName.MinorVersion, out _glVersionMinor);
        }
        catch
        {
            _glVersionMajor = 0;
            _glVersionMinor = 0;
        }

        if (_glVersionMajor > 0)
            return;

        Match versionMatch = Regex.Match(_glVersion, @"(?<major>\d+)\.(?<minor>\d+)");
        if (versionMatch.Success)
        {
            _glVersionMajor = int.Parse(versionMatch.Groups["major"].Value);
            _glVersionMinor = int.Parse(versionMatch.Groups["minor"].Value);
        }
    }

    private void ValidateGraphicsCapabilities()
    {
        bool supported = _glVersionMajor > RequiredOpenGlMajorVersion ||
            (_glVersionMajor == RequiredOpenGlMajorVersion && _glVersionMinor >= RequiredOpenGlMinorVersion);

        if (supported)
            return;

        throw new InvalidOperationException(
            $"GPU Fractal 3D Explorer requires an OpenGL {RequiredOpenGlMajorVersion}.{RequiredOpenGlMinorVersion} core context or newer. " +
            $"Detected {_glVersion} on {_glVendor} / {_glRenderer}.");
    }

    private void MarkDirty()
    {
        _needsRecompute = true;
    }

    private StartupLayout CalculateStartupLayout()
    {
        Rect workArea = SystemParameters.WorkArea;
        double screenWidth = workArea.Width;
        double screenHeight = workArea.Height;
        double outerMargin = Math.Max(16, Math.Round(screenHeight * 0.02));
        double gap = Math.Max(14, Math.Round(screenHeight * 0.015));

        double preferredHudWidth = Math.Clamp(screenWidth * 0.27, MinimumHudWidth, PreferredHudWidth);
        double desiredMainSize = Math.Min(screenHeight * 0.8, screenHeight - (2 * outerMargin));
        double centeredMainLimit = screenWidth - (2 * (preferredHudWidth + gap + outerMargin));
        double mainSize = Math.Min(desiredMainSize, centeredMainLimit);

        if (mainSize < 440)
            mainSize = Math.Min(desiredMainSize, screenWidth - (2 * (FallbackHudWidth + gap + outerMargin)));

        mainSize = Math.Max(420, Math.Min(mainSize, screenHeight - (2 * outerMargin)));

        double sideSpace = (screenWidth - mainSize) / 2.0;
        double maxHudWidth = sideSpace - outerMargin - gap;
        double hudWidth = Math.Min(preferredHudWidth, maxHudWidth);
        if (hudWidth < FallbackHudWidth)
        {
            mainSize = Math.Min(mainSize, screenWidth - (2 * (FallbackHudWidth + gap + outerMargin)));
            sideSpace = (screenWidth - mainSize) / 2.0;
            maxHudWidth = sideSpace - outerMargin - gap;
            hudWidth = Math.Max(240, maxHudWidth);
        }

        double mainLeft = workArea.Left + ((workArea.Width - mainSize) / 2.0);
        double mainTop = workArea.Top + ((workArea.Height - mainSize) / 2.0);
        double hudLeft = Math.Max(workArea.Left + outerMargin, mainLeft - gap - hudWidth);
        double totalHudHeight = workArea.Height - (2 * outerMargin);
        double upperHudHeight = Math.Floor((totalHudHeight - gap) * 0.5);
        double lowerHudTop = workArea.Top + outerMargin + upperHudHeight + gap;
        double lowerHudHeight = totalHudHeight - upperHudHeight - gap;

        return new StartupLayout(
            (int)Math.Round(mainLeft),
            (int)Math.Round(mainTop),
            (int)Math.Round(mainSize),
            (int)Math.Round(hudLeft),
            (int)Math.Round(workArea.Top + outerMargin),
            (int)Math.Round(hudWidth),
            (int)Math.Round(upperHudHeight),
            (int)Math.Round(lowerHudTop),
            (int)Math.Round(lowerHudHeight));
    }

    private void ApplyHudLayout(StatusHUD statusHud, SettingsHUD settingsHud)
    {
        statusHud.ApplyWindowBounds(
            _startupLayout.HudLeft,
            _startupLayout.StatusHudTop,
            _startupLayout.HudWidth,
            _startupLayout.StatusHudHeight);

        settingsHud.ApplyWindowBounds(
            _startupLayout.HudLeft,
            _startupLayout.SettingsHudTop,
            _startupLayout.HudWidth,
            _startupLayout.SettingsHudHeight);
    }

    private (int Min, int Default, int Max) GetTierBounds(PerformanceProfile profile) => profile switch
    {
        PerformanceProfile.Latency => (0, 1, 2),
        PerformanceProfile.Balanced => (1, 2, 3),
        PerformanceProfile.Quality => (2, 3, 4),
        PerformanceProfile.Screenshot => (4, 4, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };

    private int GetDefaultTierIndex(PerformanceProfile profile) => GetTierBounds(profile).Default;

    private readonly record struct StartupLayout(
        int MainLeft,
        int MainTop,
        int MainSize,
        int HudLeft,
        int StatusHudTop,
        int HudWidth,
        int StatusHudHeight,
        int SettingsHudTop,
        int SettingsHudHeight);

    private static void PrintControls()
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                GPU Fractal 3D Explorer                   ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ Mouse: Left drag orbit | Middle drag pan | Wheel zoom    ║");
        Console.WriteLine("║ Move fractal: W/A/S/D or Arrow keys                      ║");
        Console.WriteLine("║ Zoom fractal: +/- (hold Shift for fine control)          ║");
        Console.WriteLine("║ Iterations: I / K                                        ║");
        Console.WriteLine("║ Height scale: PageUp / PageDown                          ║");
        Console.WriteLine("║ Fractal set: T | Palette: C | Wireframe: F               ║");
        Console.WriteLine("║ Resolution tier: G | Precision: M | Profile: N           ║");
        Console.WriteLine("║ Shading: L | Adaptive res: O | HUDs: H | VSync: V        ║");
        Console.WriteLine("║ Print coords: P                                          ║");
        Console.WriteLine("║ Presets: 1..6 | Reset: R | Exit: Esc                     ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    public void Dispose()
    {
        _isClosing = true;
        CloseHudWindows();
        _compute?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }
}
