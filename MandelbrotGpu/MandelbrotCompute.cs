using Silk.NET.OpenGL;
using System.Diagnostics;

namespace MandelbrotGpu;

public enum PrecisionMode
{
    Auto,
    ForceFP32,
    ForceFP64
}

/// <summary>
/// GPU-accelerated Mandelbrot set computation using OpenGL Compute Shaders.
/// </summary>
public sealed class MandelbrotCompute : IDisposable
{
    public PrecisionMode CurrentPrecisionMode { get; set; } = PrecisionMode.Auto;
    public bool IsComputing { get; private set; }
    
    // Zoom threshold: below this, float is accurate enough (fast), above we need double
    public const double FloatPrecisionZoomLimit = 500_000.0;

    public double CenterX { get; set; } = -0.5;
    public double CenterY { get; set; } = 0.0;
    public double Zoom    { get; set; } = 1.0;
    public int MaxIterations { get; set; } = 256;
    public int Width  { get; private set; }
    public int Height { get; private set; }

    // Reported mode so UI can display it
    public string PrecisionStatus => CurrentPrecisionMode switch
    {
        PrecisionMode.ForceFP32 => "FORCED FP32 (Fast)",
        PrecisionMode.ForceFP64 => "FORCED FP64 (Precise)",
        _ => Zoom < FloatPrecisionZoomLimit ? "AUTO: FP32 (Fast)" : "AUTO: FP64 (Precise)"
    };

    private readonly GL _gl;
    private readonly uint _computeProgramF32;
    private readonly uint _computeProgramF64;
    private readonly uint _ssboMinMax;

    // Uniform locations for F32 shader
    private readonly int _uWidthF32;
    private readonly int _uHeightF32;
    private readonly int _uXMinF32;
    private readonly int _uYMinF32;
    private readonly int _uXMaxF32;
    private readonly int _uYMaxF32;
    private readonly int _uMaxIterationsF32;

    // Uniform locations for F64 shader
    private readonly int _uWidthF64;
    private readonly int _uHeightF64;
    private readonly int _uXMinF64;
    private readonly int _uYMinF64;
    private readonly int _uXMaxF64;
    private readonly int _uYMaxF64;
    private readonly int _uMaxIterationsF64;

    public MandelbrotCompute(GL gl, int width, int height)
    {
        _gl = gl;
        Width  = width;
        Height = height;

        _computeProgramF32 = CreateComputeProgram(Shaders.MandelbrotComputeShaderF32);
        _computeProgramF64 = CreateComputeProgram(Shaders.MandelbrotComputeShaderF64);

        // Fetch variable locations for FP32
        _uWidthF32 = _gl.GetUniformLocation(_computeProgramF32, "uWidth");
        _uHeightF32 = _gl.GetUniformLocation(_computeProgramF32, "uHeight");
        _uXMinF32 = _gl.GetUniformLocation(_computeProgramF32, "uXMin");
        _uYMinF32 = _gl.GetUniformLocation(_computeProgramF32, "uYMin");
        _uXMaxF32 = _gl.GetUniformLocation(_computeProgramF32, "uXMax");
        _uYMaxF32 = _gl.GetUniformLocation(_computeProgramF32, "uYMax");
        _uMaxIterationsF32 = _gl.GetUniformLocation(_computeProgramF32, "uMaxIterations");

        // Fetch variable locations for FP64
        _uWidthF64 = _gl.GetUniformLocation(_computeProgramF64, "uWidth");
        _uHeightF64 = _gl.GetUniformLocation(_computeProgramF64, "uHeight");
        _uXMinF64 = _gl.GetUniformLocation(_computeProgramF64, "uXMin");
        _uYMinF64 = _gl.GetUniformLocation(_computeProgramF64, "uYMin");
        _uXMaxF64 = _gl.GetUniformLocation(_computeProgramF64, "uXMax");
        _uYMaxF64 = _gl.GetUniformLocation(_computeProgramF64, "uYMax");
        _uMaxIterationsF64 = _gl.GetUniformLocation(_computeProgramF64, "uMaxIterations");

        // Create SSBO for min/max tracking (2 uints)
        _ssboMinMax = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _ssboMinMax);
        // Allocate 8 bytes (2x uint)
        unsafe { _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, 2 * sizeof(uint), null, BufferUsageARB.DynamicCopy); }
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);

        Console.WriteLine("Mandelbrot compute initialized with OpenGL Compute Shaders.");
    }

    private uint CreateComputeProgram(string source)
    {
        uint shader = _gl.CreateShader(ShaderType.ComputeShader);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compileStatus);
        if (compileStatus == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            throw new Exception($"Compute shader compilation failed: {infoLog}");
        }

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, shader);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(program);
            throw new Exception($"Compute program linking failed: {infoLog}");
        }

        _gl.DeleteShader(shader);
        return program;
    }

    /// <summary>
    /// Computes the Mandelbrot set into the given texture using the fastest
    /// compute shader (FP32 or FP64) based on the current zoom level.
    /// </summary>
    public HeightFieldFrame Compute(uint targetTextureHandle)
    {
        IsComputing = true;
        try
        {
            long allocatedBefore = GC.GetTotalAllocatedBytes(false);
            
            double aspectRatio = (double)Width / Height;
            double rangeY = 2.0 / Zoom;
            double rangeX = rangeY * aspectRatio;

            double xMin = CenterX - rangeX;
            double xMax = CenterX + rangeX;
            double yMin = CenterY - rangeY;
            double yMax = CenterY + rangeY;

            bool useF32 = CurrentPrecisionMode switch
            {
                PrecisionMode.ForceFP32 => true,
                PrecisionMode.ForceFP64 => false,
                _ => Zoom < FloatPrecisionZoomLimit
            };

            long dispatchStart = Stopwatch.GetTimestamp();

            // Clear the MinMax SSBO [0xFFFFFFFF, 0] so atomic limits can work correctly.
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _ssboMinMax);
            ReadOnlySpan<uint> initialMinMax = stackalloc uint[] { 0xFFFFFFFFu, 0u };
            unsafe
            {
                fixed (uint* ptr = initialMinMax)
                {
                    _gl.BufferSubData(BufferTargetARB.ShaderStorageBuffer, 0, 2 * sizeof(uint), ptr);
                }
            }
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, _ssboMinMax);

            // Bind the texture as an image so the compute shader can write to it
            _gl.BindImageTexture(0, targetTextureHandle, 0, false, 0, GLEnum.WriteOnly, GLEnum.R32f);

            if (useF32)
            {
                _gl.UseProgram(_computeProgramF32);
                _gl.Uniform1(_uWidthF32, Width);
                _gl.Uniform1(_uHeightF32, Height);
                _gl.Uniform1(_uXMinF32, (float)xMin);
                _gl.Uniform1(_uYMinF32, (float)yMin);
                _gl.Uniform1(_uXMaxF32, (float)xMax);
                _gl.Uniform1(_uYMaxF32, (float)yMax);
                _gl.Uniform1(_uMaxIterationsF32, MaxIterations);
            }
            else
            {
                _gl.UseProgram(_computeProgramF64);
                _gl.Uniform1(_uWidthF64, Width);
                _gl.Uniform1(_uHeightF64, Height);
                _gl.Uniform1(_uXMinF64, xMin);
                _gl.Uniform1(_uYMinF64, yMin);
                _gl.Uniform1(_uXMaxF64, xMax);
                _gl.Uniform1(_uYMaxF64, yMax);
                _gl.Uniform1(_uMaxIterationsF64, MaxIterations);
            }

            // Local workgroup size is 16x16, calculate total groups needed
            uint numGroupsX = (uint)Math.Ceiling(Width / 16.0);
            uint numGroupsY = (uint)Math.Ceiling(Height / 16.0);

            _gl.DispatchCompute(numGroupsX, numGroupsY, 1);
            
            long synchronizeStart = Stopwatch.GetTimestamp();
            
            // Ensure the computation finishes and data is available for rendering
            _gl.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit);
            
            long end = Stopwatch.GetTimestamp();

            // Calculate duration of phases. Readback phase is now 0 as it's purely on-GPU
            double kernelDispatchMs = Stopwatch.GetElapsedTime(dispatchStart, synchronizeStart).TotalMilliseconds;
            double synchronizeMs = Stopwatch.GetElapsedTime(synchronizeStart, end).TotalMilliseconds;

            return new HeightFieldFrame(
                null, // No CPU data buffer anymore!
                Width,
                Height,
                PrecisionStatus,
                kernelDispatchMs,
                synchronizeMs,
                0.0, // readback is exactly 0.0 ms now!
                GC.GetTotalAllocatedBytes(false) - allocatedBefore)
                { TextureHandle = targetTextureHandle, BoundsBufferHandle = _ssboMinMax };
        }
        finally
        {
            IsComputing = false;
        }
    }

    public void Resize(int width, int height)
    {
        Width  = width;
        Height = height;
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_computeProgramF32);
        _gl.DeleteProgram(_computeProgramF64);
        _gl.DeleteBuffer(_ssboMinMax);
    }
}
