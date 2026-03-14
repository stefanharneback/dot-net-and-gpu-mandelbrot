using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Threading;
using System.Windows;

namespace MandelbrotGpu;

/// <summary>
/// Main application class: creates a window, manages OpenGL rendering,
/// handles input, and orchestrates GPU Mandelbrot computation + 3D visualization.
/// </summary>
public sealed class MandelbrotApp : IDisposable
{
    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;

    // OpenGL resources
    private uint _shaderProgram;
    private uint _vao, _vbo, _ebo;
    private uint _gridShaderProgram;
    private uint _gridVao, _gridVbo;
    private int _indexCount;
    private int _gridVertexCount;


    // Components
    private MandelbrotCompute _compute = null!;
    private readonly Camera _camera = new();

    // Color palette state
    private readonly string[] _paletteNames = ["Vibrant", "Fire", "Ocean", "Neon"];
    private int _currentPalette;
    private float[] _palette = null!;

    // Mouse state
    private bool _leftMouseDown;
    private bool _middleMouseDown;
    private Vector2 _lastMousePos;

    // Mandelbrot navigation state
    private float _heightScale = 0.6f;
    private bool _needsRecompute = true;
    private bool _wireframeMode;
    private int _gridResolution = 512;
    private bool _adaptiveResolution = true;

    public bool IsAdaptiveResolutionEnabled => _adaptiveResolution;

    // HUD
    private float _fps;
    private int _frameCount;
    private double _fpsTimer;

    public void Run()
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(1600, 900);
        options.Title = "🌀 GPU Mandelbrot 3D Explorer — .NET 10 + ILGPU + OpenGL";
        options.VSync = true;
        options.PreferredDepthBufferBits = 24;
        options.Samples = 4; // MSAA

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

        // Configure OpenGL
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Multisample);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.02f, 0.02f, 0.05f, 1.0f); // Dark background

        // Initialize compute
        _compute = new MandelbrotCompute(_gridResolution, _gridResolution);

        // Initialize palette
        _palette = ColorPalette.GenerateVibrantPalette();

        // Create shaders
        _shaderProgram = CreateShaderProgram(Shaders.VertexShader, Shaders.FragmentShader);
        _gridShaderProgram = CreateShaderProgram(Shaders.GridVertexShader, Shaders.GridFragmentShader);

        // Create mesh VAO/VBO/EBO
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        // Create grid overlay
        CreateGridOverlay();

        // Setup input handlers
        SetupInput();

        // Initial compute
        RecomputeMandelbrot();

        // Start the detached settings HUD Window
        StartSettingsWindow();

        PrintControls();
    }

    private void StartSettingsWindow()
    {
        // Must run WPF in a separate STA thread since Silk.NET takes the main thread
        var thread = new Thread(() =>
        {
            var app = new Application();
            app.Run(new SettingsHUD(this, _compute));
        });
        thread.SetApartmentState(ApartmentState.STA); // Essential for WPF
        thread.IsBackground = true; // Close when main app closes
        thread.Start();
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
        {
            keyboard.KeyDown += OnKeyDown;
        }
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left) _leftMouseDown = true;
        if (button == MouseButton.Middle) _middleMouseDown = true;
        _lastMousePos = mouse.Position;
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left) _leftMouseDown = false;
        if (button == MouseButton.Middle) _middleMouseDown = false;
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        Vector2 delta = position - _lastMousePos;
        _lastMousePos = position;

        if (_leftMouseDown)
        {
            _camera.Orbit(delta.X, -delta.Y);
        }
        else if (_middleMouseDown)
        {
            _camera.Pan(delta.X, delta.Y);
        }
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        _camera.Zoom(scroll.Y);
    }

    public void HandleExternalKeyDown(Key key)
    {
        // Simple shim to allow HUD to pass keys back to logic
        OnKeyDown(null!, key, 0);
    }

    private void OnKeyDown(IKeyboard? keyboard, Key key, int scancode)
    {
        bool shift = keyboard?.IsKeyPressed(Key.ShiftLeft) == true || keyboard?.IsKeyPressed(Key.ShiftRight) == true;
        double panAmount = shift ? 0.005 : 0.05;
        double zoomFactor = shift ? 1.05 : 1.3;

        switch (key)
        {
            // Mandelbrot navigation
            case Key.Left:
            case Key.A:
                _compute.CenterX -= panAmount / _compute.Zoom;
                _needsRecompute = true;
                break;
            case Key.Right:
            case Key.D:
                _compute.CenterX += panAmount / _compute.Zoom;
                _needsRecompute = true;
                break;
            case Key.Up:
            case Key.W:
                _compute.CenterY += panAmount / _compute.Zoom;
                _needsRecompute = true;
                break;
            case Key.Down:
            case Key.S:
                _compute.CenterY -= panAmount / _compute.Zoom;
                _needsRecompute = true;
                break;

            // Zoom into Mandelbrot set
            case Key.Equal: // +
            case Key.KeypadAdd:
                _compute.Zoom *= zoomFactor;
                _needsRecompute = true;
                break;
            case Key.Minus:
            case Key.KeypadSubtract:
                _compute.Zoom /= zoomFactor;
                _needsRecompute = true;
                break;

            // Height scale
            case Key.PageUp:
                _heightScale = Math.Min(_heightScale + 0.1f, 3.0f);
                _needsRecompute = true;
                break;
            case Key.PageDown:
                _heightScale = Math.Max(_heightScale - 0.1f, 0.05f);
                _needsRecompute = true;
                break;

            // Iteration count
            case Key.I:
                _compute.MaxIterations = _compute.MaxIterations < 1000 ? _compute.MaxIterations + 128 : _compute.MaxIterations * 2;
                if (_compute.MaxIterations > 2_000_000) _compute.MaxIterations = 2_000_000;
                _needsRecompute = true;
                break;
            case Key.K:
                _compute.MaxIterations = _compute.MaxIterations < 1000 ? Math.Max(32, _compute.MaxIterations - 128) : _compute.MaxIterations / 2;
                _needsRecompute = true;
                break;

            // Color palette cycling
            case Key.C:
                _currentPalette = (_currentPalette + 1) % _paletteNames.Length;
                _palette = _currentPalette switch
                {
                    0 => ColorPalette.GenerateVibrantPalette(),
                    1 => ColorPalette.GenerateFirePalette(),
                    2 => ColorPalette.GenerateOceanPalette(),
                    3 => ColorPalette.GenerateNeonPalette(),
                    _ => _palette
                };
                Console.WriteLine($"Palette: {_paletteNames[_currentPalette]}");
                _needsRecompute = true;
                break;

            // Wireframe toggle
            case Key.F:
                _wireframeMode = !_wireframeMode;
                Console.WriteLine($"Wireframe: {(_wireframeMode ? "ON" : "OFF")}");
                break;

            // Grid resolution manual cycle
            case Key.G:
                CycleGridResolution();
                break;

            // Toggle Adaptive Resolution
            case Key.O:
                _adaptiveResolution = !_adaptiveResolution;
                Console.WriteLine($"Adaptive Resolution: {(_adaptiveResolution ? "ON" : "OFF")}");
                break;

            // Print coordinates
            case Key.P:
                Console.WriteLine($"Coords: X: {_compute.CenterX}, Y: {_compute.CenterY} | Zoom: {_compute.Zoom} | Iter: {_compute.MaxIterations}");
                break;

            // Presets
            case Key.Number1: // Default
                SetLocation(-0.5, 0.0, 1.0, 256); break;
            case Key.Number2: // Seahorse Valley
                SetLocation(-0.743643887037151, 0.13182590420533, 10000.0, 1000); break;
            case Key.Number3: // Elephant Valley
                SetLocation(0.27322626, 0.595153338, 2000.0, 1000); break;
            case Key.Number4: // Triple Spiral
                SetLocation(-0.088, 0.654, 50.0, 500); break;
            case Key.Number5: // Deep Zoom
                SetLocation(-0.7436447860, 0.1318252536, 1000000.0, 2000); break;
            case Key.Number6: // Extremely Deep Zoom
                SetLocation(-0.7436447860, 0.1318252536, 100000000000.0, 10000); break;

            // Precision cycling
            case Key.M:
                _compute.CurrentPrecisionMode = _compute.CurrentPrecisionMode switch
                {
                    PrecisionMode.Auto => PrecisionMode.ForceFP32,
                    PrecisionMode.ForceFP32 => PrecisionMode.ForceFP64,
                    _ => PrecisionMode.Auto
                };
                Console.WriteLine($"Precision Mode: {_compute.CurrentPrecisionMode}");
                _needsRecompute = true;
                break;

            // Reset view
            case Key.R:
                _compute.CenterX = -0.5;
                _compute.CenterY = 0.0;
                _compute.Zoom = 1.0;
                _compute.MaxIterations = 256;
                _heightScale = 0.6f;
                _needsRecompute = true;
                Console.WriteLine("View reset");
                break;

            // Escape to close
            case Key.Escape:
                _window.Close();
                break;
        }
    }

    private void SetLocation(double cx, double cy, double zoom, int maxIter)
    {
        _compute.CenterX = cx;
        _compute.CenterY = cy;
        _compute.Zoom = zoom;
        _compute.MaxIterations = maxIter;
        _needsRecompute = true;
        Console.WriteLine($"Location set -> X: {cx}, Y: {cy} | Zoom: {zoom}x | Iter: {maxIter}");
    }

    private void RecomputeMandelbrot()
    {
        float[] data = _compute.Compute();
        var (vertices, indices) = MeshBuilder.BuildTerrainMesh(data, _compute.Width, _compute.Height, _palette, _heightScale);

        _indexCount = indices.Length;
        UploadMesh(vertices, indices);

        _needsRecompute = false;
    }

    private unsafe void UploadMesh(float[] vertices, uint[] indices)
    {
        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* v = vertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.DynamicDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* i = indices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), i, BufferUsageARB.DynamicDraw);
        }

        int stride = MeshBuilder.FloatsPerVertex * sizeof(float);

        // Position attribute (location = 0)
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);

        // Normal attribute (location = 1)
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(3 * sizeof(float)));

        // Color attribute (location = 2)
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(6 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    private unsafe void CreateGridOverlay()
    {
        // Create a subtle grid at y=0
        const int gridSize = 10;
        const float gridStep = 0.2f;
        var gridVertices = new List<float>();

        for (int i = -gridSize; i <= gridSize; i++)
        {
            float pos = i * gridStep;
            float alpha = 1f - MathF.Abs(i) / gridSize;
            float c = 0.15f * alpha;

            // Line along X
            gridVertices.AddRange([pos, -0.01f, -gridSize * gridStep, c, c, c * 1.5f]);
            gridVertices.AddRange([pos, -0.01f, gridSize * gridStep, c, c, c * 1.5f]);

            // Line along Z
            gridVertices.AddRange([-gridSize * gridStep, -0.01f, pos, c, c, c * 1.5f]);
            gridVertices.AddRange([gridSize * gridStep, -0.01f, pos, c, c, c * 1.5f]);
        }

        float[] gridArray = gridVertices.ToArray();
        _gridVertexCount = gridArray.Length / 6;

        _gridVao = _gl.GenVertexArray();
        _gridVbo = _gl.GenBuffer();

        _gl.BindVertexArray(_gridVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);

        fixed (float* v = gridArray)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(gridArray.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    private void OnUpdate(double deltaTime)
    {
        // FPS counter
        _frameCount++;
        _fpsTimer += deltaTime;
        if (_fpsTimer >= 1.0)
        {
            _fps = (float)(_frameCount / _fpsTimer);
            _window.Title = $"🌀 Mandelbrot 3D — {_fps:F0} FPS | Zoom: {_compute.Zoom:0.##e+0}x | {_compute.PrecisionStatus} | Res: {_compute.Width}x{_compute.Height}";
            _frameCount = 0;
            _fpsTimer = 0;
        }

        if (_adaptiveResolution)
        {
            UpdateAdaptiveResolution();
        }

        if (_needsRecompute)
        {
            RecomputeMandelbrot();
        }
    }

    private void CycleGridResolution()
    {
        _adaptiveResolution = false; // Stop auto-pilot when user takes manual control
        _gridResolution = _gridResolution switch
        {
            128 => 256,
            256 => 512,
            512 => 768,
            768 => 1024,
            1024 => 2048,
            2048 => 4096,
            _ => 128
        };
        _compute.Resize(_gridResolution, _gridResolution);
        _needsRecompute = true;
    }

    private void UpdateAdaptiveResolution()
    {
        // Increase detail as we zoom in
        double logZoom = Math.Log10(Math.Max(1.0, _compute.Zoom));
        int targetRes = 512 + (int)(logZoom * 300);
        targetRes = Math.Clamp(targetRes, 512, 4096); 

        // Only resize if significantly different to prevent continuous recomputes
        if (Math.Abs(targetRes - _gridResolution) > 128)
        {
            _gridResolution = targetRes;
            _compute.Resize(_gridResolution, _gridResolution);
            _needsRecompute = true;
        }
    }


    private void OnRender(double deltaTime)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspect = (float)_window.Size.X / _window.Size.Y;
        Matrix4x4 view = _camera.GetViewMatrix();
        Matrix4x4 projection = Camera.GetProjectionMatrix(aspect);
        Matrix4x4 model = Matrix4x4.Identity;

        // Render terrain
        _gl.UseProgram(_shaderProgram);

        SetUniformMatrix4(_shaderProgram, "uModel", model);
        SetUniformMatrix4(_shaderProgram, "uView", view);
        SetUniformMatrix4(_shaderProgram, "uProjection", projection);
        SetUniformVec3(_shaderProgram, "uLightDir", Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.3f)));
        SetUniformVec3(_shaderProgram, "uViewPos", _camera.Position);

        if (_wireframeMode)
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);

        _gl.BindVertexArray(_vao);
        unsafe { _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null); }

        if (_wireframeMode)
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        // Render grid
        _gl.UseProgram(_gridShaderProgram);
        Matrix4x4 vp = view * projection;
        SetUniformMatrix4(_gridShaderProgram, "uViewProjection", vp);

        _gl.BindVertexArray(_gridVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);
    }

    private void OnResize(Vector2D<int> size)
    {
        _gl.Viewport(size);
    }

    private void OnClosing()
    {
        // Cleanup
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_gridVao);
        _gl.DeleteBuffer(_gridVbo);
        _gl.DeleteProgram(_shaderProgram);
        _gl.DeleteProgram(_gridShaderProgram);
    }

    // --- OpenGL Helper Methods ---

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

    private unsafe void SetUniformMatrix4(uint program, string name, Matrix4x4 matrix)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
        {
            _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
        }
    }

    private void SetUniformVec3(uint program, string name, Vector3 vec)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
        {
            _gl.Uniform3(location, vec.X, vec.Y, vec.Z);
        }
    }

    private static void PrintControls()
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       🌀 GPU Mandelbrot 3D Explorer — Controls 🌀       ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  🖱️  Mouse Controls:                                     ║");
        Console.WriteLine("║    Left Drag    — Orbit / rotate camera                  ║");
        Console.WriteLine("║    Middle Drag  — Pan camera                             ║");
        Console.WriteLine("║    Scroll Wheel — Zoom camera in/out                     ║");
        Console.WriteLine("║                                                          ║");
        Console.WriteLine("║  ⌨️  Mandelbrot Navigation:                               ║");
        Console.WriteLine("║    W/A/S/D or Arrow Keys — Pan the fractal               ║");
        Console.WriteLine("║    +/- (or Numpad)       — Zoom into fractal             ║");
        Console.WriteLine("║    I/K                   — Increase/decrease iterations   ║");
        Console.WriteLine("║    PageUp/PageDown       — Adjust height scale            ║");
        Console.WriteLine("║                                                          ║");
        Console.WriteLine("║  🎨 Display:                                              ║");
        Console.WriteLine("║    C — Cycle color palette                                ║");
        Console.WriteLine("║    F — Toggle wireframe mode                              ║");
        Console.WriteLine("║    G — Cycle grid resolution (up to 4096)                 ║");
        Console.WriteLine("║    P — Print current coordinates to console               ║");
        Console.WriteLine("║    1-5 — Jump to location presets                         ║");
        Console.WriteLine("║    (Hold Shift for fine pan and zoom)                     ║");
        Console.WriteLine("║    R — Reset view                                         ║");
        Console.WriteLine("║    Esc — Exit                                             ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    public void Dispose()
    {
        _compute?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }
}
