using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Windows;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace MandelbrotGpu;

/// <summary>
/// Main application class: creates a window, manages OpenGL rendering,
/// handles input, and orchestrates GPU Mandelbrot computation + 3D visualization.
/// </summary>
public sealed class MandelbrotApp : IDisposable
{
    private const int MaxWindowMsaaSamples = 4;
    private const int LargeIterationThreshold = 1000;
    private const int IterationLinearStep = 128;
    private const int MaxIterationLimit = 2_000_000;

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

    private readonly Camera _camera = new();
    private readonly string[] _paletteNames = ["Vibrant", "Fire", "Ocean", "Neon"];
    private readonly object _settingsWindowLock = new();

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
    private PerformanceMetrics _latestMetrics = PerformanceMetrics.Empty;
    private HeightFieldFrame? _currentFrame;

    private float[] _palette = null!;
    private int _currentPalette;
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

    private SettingsHUD? _settingsHud;
    private Thread? _settingsThread;
    private volatile bool _isHudStarting;

    public MandelbrotApp()
    {
        _performanceSettings = PerformanceSettings.Create(PerformanceProfile.Latency);
        _resolutionTierIndex = GetDefaultTierIndex(_performanceSettings.Profile);
    }

    public PerformanceSettings CurrentPerformanceSettings => _performanceSettings;

    public PerformanceMetrics LatestPerformanceMetrics => _latestMetrics;

    public bool AdaptiveResolutionEnabled => _performanceSettings.AdaptiveResolutionEnabled;

    public string CurrentPaletteName => _paletteNames[_currentPalette];

    public float HeightScale => _heightScale;

    public float FPS => _fps;

    public int RenderMeshResolution => _terrainRenderer?.RenderMeshResolution ?? _performanceSettings.RenderMeshResolution;

    public bool GridVisible => _performanceSettings.ShowGrid;

    public bool WireframeMode => _wireframeMode;

    public PrecisionMode CurrentPrecisionMode => _compute?.CurrentPrecisionMode ?? PrecisionMode.Auto;

    public void Run()
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(1600, 900);
        options.Title = "GPU Mandelbrot 3D Explorer";
        options.VSync = _performanceSettings.VSyncEnabled;
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
        _input = _window.CreateInput();

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.02f, 0.02f, 0.05f, 1.0f);

        _compute = new MandelbrotCompute(_performanceSettings.ComputeResolution, _performanceSettings.ComputeResolution);
        _palette = ColorPalette.GeneratePalette(_currentPalette);

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
        RecomputeMandelbrot();
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
        HandleKey(key, shift);
    }

    public void HandleExternalKeyDown(Key key, bool shiftPressed)
    {
        HandleKey(key, shiftPressed);
    }

    private void HandleKey(Key key, bool shift)
    {
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
                FocusSettingsWindow();
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
                    $"Coords: X: {_compute.CenterX}, Y: {_compute.CenterY} | Zoom: {_compute.Zoom} | Iter: {_compute.MaxIterations} | " +
                    $"Profile: {_performanceSettings.Profile} | Compute: {_compute.Width} | Render: {RenderMeshResolution}");
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
                _compute.CenterX = -0.5;
                _compute.CenterY = 0.0;
                _compute.Zoom = 1.0;
                _compute.MaxIterations = 256;
                _heightScale = 0.6f;
                MarkDirty();
                Console.WriteLine("View reset");
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
        Console.WriteLine($"Location set -> X: {centerX}, Y: {centerY} | Zoom: {zoom}x | Iter: {maxIterations}");
    }

    private void OnUpdate(double deltaTime)
    {
        _frameCount++;
        _fpsTimer += deltaTime;
        if (_fpsTimer >= 1.0)
        {
            _fps = (float)(_frameCount / _fpsTimer);
            _window.Title =
                $"Mandelbrot 3D | {_fps:F0} FPS | {_performanceSettings.Profile} | Compute {_compute.Width} | Render {RenderMeshResolution} | " +
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
            RecomputeMandelbrot();
    }

    private void OnRender(double deltaTime)
    {
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

    private void RecomputeMandelbrot()
    {
        long recomputeStart = Stopwatch.GetTimestamp();
        long allocatedBefore = GC.GetTotalAllocatedBytes(false);

        HeightFieldFrame frame = _compute.Compute();
        _terrainRenderer.UploadHeightField(frame);
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
        if (_compute == null)
            return;

        if (_performanceSettings.HudEnabled)
            ShowSettingsWindow();
        else
            CloseSettingsWindow();
    }

    private void FocusSettingsWindow()
    {
        _performanceSettings = _performanceSettings with { HudEnabled = true };
        ShowSettingsWindow();
        Console.WriteLine("HUD focused");
    }

    private void ShowSettingsWindow()
    {
        lock (_settingsWindowLock)
        {
            if (_settingsHud != null)
            {
                SettingsHUD hud = _settingsHud;
                hud.Dispatcher.BeginInvoke(new Action(() => hud.Activate()));
                return;
            }

            if (_isHudStarting)
                return;

            _isHudStarting = true;
            _settingsThread = new Thread(() =>
            {
                try
                {
                    var app = new Application();
                    var hud = new SettingsHUD(this, _compute);
                    hud.Closed += (_, _) =>
                    {
                        lock (_settingsWindowLock)
                        {
                            _settingsHud = null;
                            _settingsThread = null;
                            _isHudStarting = false;
                        }
                    };

                    lock (_settingsWindowLock)
                    {
                        _settingsHud = hud;
                        _isHudStarting = false;
                    }

                    app.Run(hud);
                }
                catch (Exception ex)
                {
                    lock (_settingsWindowLock)
                    {
                        _settingsHud = null;
                        _settingsThread = null;
                        _isHudStarting = false;
                    }

                    Console.WriteLine($"HUD failed to start: {ex.Message}");
                }
            });

            _settingsThread.SetApartmentState(ApartmentState.STA);
            _settingsThread.IsBackground = true;
            _settingsThread.Start();
        }
    }

    private void CloseSettingsWindow()
    {
        SettingsHUD? hud;
        lock (_settingsWindowLock)
            hud = _settingsHud;

        if (hud == null)
            return;

        hud.Dispatcher.BeginInvoke(new Action(hud.BeginClose));
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
        _gl.Viewport(size);
    }

    private void OnClosing()
    {
        CloseSettingsWindow();
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

    private void MarkDirty()
    {
        _needsRecompute = true;
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

    private static void PrintControls()
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              GPU Mandelbrot 3D Explorer                  ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ Mouse: Left drag orbit | Middle drag pan | Wheel zoom    ║");
        Console.WriteLine("║ Move fractal: W/A/S/D or Arrow keys                      ║");
        Console.WriteLine("║ Zoom fractal: +/- (hold Shift for fine control)          ║");
        Console.WriteLine("║ Iterations: I / K                                        ║");
        Console.WriteLine("║ Height scale: PageUp / PageDown                          ║");
        Console.WriteLine("║ Palette: C | Wireframe: F | Resolution tier: G           ║");
        Console.WriteLine("║ Precision: M | Profile: N | Shading: L                   ║");
        Console.WriteLine("║ Adaptive res: O | HUD focus: H | VSync: V                ║");
        Console.WriteLine("║ Print coords: P                                          ║");
        Console.WriteLine("║ Presets: 1..6 | Reset: R | Exit: Esc                     ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    public void Dispose()
    {
        CloseSettingsWindow();
        _compute?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }
}
