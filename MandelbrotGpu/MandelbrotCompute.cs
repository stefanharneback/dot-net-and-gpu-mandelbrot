using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using System.Diagnostics;

namespace MandelbrotGpu;

public enum PrecisionMode
{
    Auto,
    ForceFP32,
    ForceFP64
}

/// <summary>
/// GPU-accelerated Mandelbrot set computation using ILGPU.
/// </summary>
public sealed class MandelbrotCompute : IDisposable
{
    public PrecisionMode CurrentPrecisionMode { get; set; } = PrecisionMode.Auto;
    public bool IsComputing { get; private set; }
    // FP32 and FP64 kernels compiled up front
    private readonly Action<Index1D, ArrayView1D<float, Stride1D.Dense>,
        int, int, float, float, float, float, int> _kernelF32;
    private readonly Action<Index1D, ArrayView1D<float, Stride1D.Dense>,
        int, int, double, double, double, double, int> _kernelF64;

    private readonly Context _context;
    private readonly Accelerator _accelerator;

    // Re-use a single GPU output buffer (avoids allocation every compute frame)
    private MemoryBuffer1D<float, Stride1D.Dense>? _deviceBuffer;
    private int _deviceBufferSize;
    private float[]? _cpuBuffer;

    // Zoom threshold: below this, float is accurate enough (fast), above we need double
    // float has ~7 decimal digits of precision; at zoom ~1e5 the pixel spacing is ~4e-5/width
    // which is about 2e-8 for 512px → exceeds float epsilon (~1.2e-7) around zoom ~1e6
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

    public MandelbrotCompute(int width, int height)
    {
        Width  = width;
        Height = height;

        _context = Context.Create(builder => builder.Default().EnableAlgorithms());

        // Try to get a GPU accelerator; robust fallback to CPU
        Device? bestDevice = _context.GetPreferredDevice(preferCPU: false);
        if (bestDevice == null)
        {
            Console.WriteLine("Warning: No preferred device found. Defaulting to first available.");
            bestDevice = _context.Devices.FirstOrDefault();
            if (bestDevice == null)
                throw new NotSupportedException("No supported ILGPU devices found on this machine.");
        }

        try
        {
            _accelerator = bestDevice.CreateAccelerator(_context);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create accelerator on {bestDevice.Name}: {ex.Message}");
            Console.WriteLine("Falling back to CPU Accelerator.");
            var cpuDevice = _context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.CPU)
                            ?? throw new NotSupportedException("No CPU accelerator available.");
            _accelerator = cpuDevice.CreateAccelerator(_context);
        }

        Console.WriteLine($"Using accelerator: {_accelerator.Name} ({_accelerator.AcceleratorType})");

        // Compile both kernels up front so there's no JIT lag during rendering
        _kernelF32 = _accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView1D<float, Stride1D.Dense>,
            int, int, float, float, float, float, int>(MandelbrotKernelF32);

        _kernelF64 = _accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView1D<float, Stride1D.Dense>,
            int, int, double, double, double, double, int>(MandelbrotKernelF64);
    }

    // -------------------------------------------------------------------------
    // KERNEL — 32-bit float (fast, good up to ~500k zoom)
    // -------------------------------------------------------------------------
    /// <summary>
    /// Fast FP32 Mandelbrot kernel with cardioid/bulb pre-check and loop unrolling.
    /// </summary>
    private static void MandelbrotKernelF32(
        Index1D index,
        ArrayView1D<float, Stride1D.Dense> output,
        int width, int height,
        float xMin, float yMin, float xMax, float yMax,
        int maxIterations)
    {
        int px = index % width;
        int py = index / width;

        float x0 = xMin + (xMax - xMin) * px / (width  - 1);
        float y0 = yMin + (yMax - yMin) * py / (height - 1);

        // --- Optimization 1: Cardioid and period-2 bulb rejection ---
        // These checks skip the black interior pixels instantly (often 30–50% of all pixels)
        float q = (x0 - 0.25f) * (x0 - 0.25f) + y0 * y0;
        if (q * (q + (x0 - 0.25f)) <= 0.25f * y0 * y0)
        {
            output[index] = 0.0f; // inside main cardioid
            return;
        }
        float bx = x0 + 1.0f;
        if (bx * bx + y0 * y0 <= 0.0625f)
        {
            output[index] = 0.0f; // inside period-2 bulb
            return;
        }

        float x = 0f, y = 0f;
        float xOld = 0f, yOld = 0f;
        int iteration = 0;
        int periodCheck = 0;

        // --- Optimization 2: Loop Unrolling & Periodicity Checking ---
        int unrolledLimit = maxIterations - 4;
        while (iteration < unrolledLimit)
        {
            float xx = x * x, yy = y * y;
            if (xx + yy > 4f) goto done;
            y = 2f * x * y + y0; x = xx - yy + x0; iteration++;

            xx = x * x; yy = y * y;
            if (xx + yy > 4f) goto done;
            y = 2f * x * y + y0; x = xx - yy + x0; iteration++;

            xx = x * x; yy = y * y;
            if (xx + yy > 4f) goto done;
            y = 2f * x * y + y0; x = xx - yy + x0; iteration++;

            xx = x * x; yy = y * y;
            if (xx + yy > 4f) goto done;
            y = 2f * x * y + y0; x = xx - yy + x0; iteration++;

            // Periodicity check: check if we've circled back to a previous point
            // Every 20 iterations, record or compare
            if (++periodCheck > 20)
            {
                if (XMath.Abs(x - xOld) < 1e-7f && XMath.Abs(y - yOld) < 1e-7f)
                {
                    iteration = maxIterations;
                    goto done;
                }
                xOld = x; yOld = y;
                periodCheck = 0;
            }
        }
        // Remaining iterations
        while (x * x + y * y <= 4f && iteration < maxIterations)
        {
            float xTemp = x * x - y * y + x0;
            y = 2f * x * y + y0;
            x = xTemp;
            iteration++;
        }

        done:
        if (iteration < maxIterations)
        {
            float zn     = x * x + y * y;
            float log2zn = XMath.Log2(zn) * 0.5f;
            float nu     = XMath.Log2(log2zn);
            output[index] = (iteration + 1f - nu) / maxIterations;
        }
        else
        {
            output[index] = 0.0f;
        }
    }

    // -------------------------------------------------------------------------
    // KERNEL — 64-bit double (precise, for deep zooms > ~500k)
    // -------------------------------------------------------------------------
    /// <summary>
    /// High-precision FP64 Mandelbrot kernel with cardioid/bulb pre-check and loop unrolling.
    /// </summary>
    private static void MandelbrotKernelF64(
        Index1D index,
        ArrayView1D<float, Stride1D.Dense> output,
        int width, int height,
        double xMin, double yMin, double xMax, double yMax,
        int maxIterations)
    {
        int px = index % width;
        int py = index / width;

        double x0 = xMin + (xMax - xMin) * px / (width  - 1);
        double y0 = yMin + (yMax - yMin) * py / (height - 1);

        // --- Optimization 1: Cardioid and period-2 bulb rejection ---
        double q = (x0 - 0.25) * (x0 - 0.25) + y0 * y0;
        if (q * (q + (x0 - 0.25)) <= 0.25 * y0 * y0)
        {
            output[index] = 0.0f;
            return;
        }
        double bx = x0 + 1.0;
        if (bx * bx + y0 * y0 <= 0.0625)
        {
            output[index] = 0.0f;
            return;
        }

        double x = 0.0, y = 0.0;
        double xOld = 0.0, yOld = 0.0;
        int iteration = 0;
        int periodCheck = 0;

        // --- Optimization 2: Loop unrolling & Periodicity Checking ---
        int unrolledLimit = maxIterations - 4;
        while (iteration < unrolledLimit)
        {
            double xx = x * x, yy = y * y;
            if (xx + yy > 4.0) goto done;
            y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

            xx = x * x; yy = y * y;
            if (xx + yy > 4.0) goto done;
            y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

            xx = x * x; yy = y * y;
            if (xx + yy > 4.0) goto done;
            y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

            xx = x * x; yy = y * y;
            if (xx + yy > 4.0) goto done;
            y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

            if (++periodCheck > 20)
            {
                if (XMath.Abs(x - xOld) < 1e-13 && XMath.Abs(y - yOld) < 1e-13)
                {
                    iteration = maxIterations;
                    goto done;
                }
                xOld = x; yOld = y;
                periodCheck = 0;
            }
        }
        while (x * x + y * y <= 4.0 && iteration < maxIterations)
        {
            double xTemp = x * x - y * y + x0;
            y = 2.0 * x * y + y0;
            x = xTemp;
            iteration++;
        }

        done:
        if (iteration < maxIterations)
        {
            double zn     = x * x + y * y;
            double log2zn = XMath.Log2((float)zn) * 0.5;
            double nu     = XMath.Log2((float)log2zn);
            output[index] = (float)((iteration + 1.0 - nu) / maxIterations);
        }
        else
        {
            output[index] = 0.0f;
        }
    }

    // -------------------------------------------------------------------------
    // Host-side Compute() — selects kernel and manages buffer
    // -------------------------------------------------------------------------
    /// <summary>
    /// Computes the Mandelbrot set, automatically selecting the fastest
    /// kernel (FP32 or FP64) based on the current zoom level.
    /// </summary>
    public HeightFieldFrame Compute()
    {
        IsComputing = true;
        try
        {
            long allocatedBefore = GC.GetTotalAllocatedBytes(false);
            int totalPixels = Width * Height;

            // Re-use or allocate GPU output buffer
            if (_deviceBuffer == null || _deviceBufferSize != totalPixels)
            {
                _deviceBuffer?.Dispose();
                _deviceBuffer     = _accelerator.Allocate1D<float>(totalPixels);
                _deviceBufferSize = totalPixels;
                _cpuBuffer = new float[totalPixels];
            }

            double aspectRatio = (double)Width / Height;
            double rangeY = 2.0 / Zoom;
            double rangeX = rangeY * aspectRatio;

            double xMin = CenterX - rangeX;
            double xMax = CenterX + rangeX;
            double yMin = CenterY - rangeY;
            double yMax = CenterY + rangeY;

            // --- Optimization 3: Precision Selection ---
            bool useF32 = CurrentPrecisionMode switch
            {
                PrecisionMode.ForceFP32 => true,
                PrecisionMode.ForceFP64 => false,
                _ => Zoom < FloatPrecisionZoomLimit
            };

            long dispatchStart = Stopwatch.GetTimestamp();
            if (useF32)
            {
                _kernelF32(totalPixels, _deviceBuffer.View,
                    Width, Height,
                    (float)xMin, (float)yMin, (float)xMax, (float)yMax,
                    MaxIterations);
            }
            else
            {
                _kernelF64(totalPixels, _deviceBuffer.View,
                    Width, Height,
                    xMin, yMin, xMax, yMax,
                    MaxIterations);
            }

            long synchronizeStart = Stopwatch.GetTimestamp();
            _accelerator.Synchronize();
            long readbackStart = Stopwatch.GetTimestamp();

            _deviceBuffer.CopyToCPU(_cpuBuffer);
            long end = Stopwatch.GetTimestamp();

            return new HeightFieldFrame(
                _cpuBuffer!,
                Width,
                Height,
                PrecisionStatus,
                Stopwatch.GetElapsedTime(dispatchStart, synchronizeStart).TotalMilliseconds,
                Stopwatch.GetElapsedTime(synchronizeStart, readbackStart).TotalMilliseconds,
                Stopwatch.GetElapsedTime(readbackStart, end).TotalMilliseconds,
                GC.GetTotalAllocatedBytes(false) - allocatedBefore);
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
        // Force buffer reallocate on next compute
        _deviceBuffer?.Dispose();
        _deviceBuffer = null;
    }

    public void Dispose()
    {
        _deviceBuffer?.Dispose();
        _accelerator.Dispose();
        _context.Dispose();
    }
}
