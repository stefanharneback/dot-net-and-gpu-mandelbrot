using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;

namespace MandelbrotGpu;

/// <summary>
/// GPU-accelerated Mandelbrot set computation using ILGPU.
/// Computes iteration counts for a grid of complex-plane coordinates.
/// </summary>
public sealed class MandelbrotCompute : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly Action<Index1D, ArrayView1D<float, Stride1D.Dense>, int, int, double, double, double, double, int> _kernel;

    // Current parameters
    public double CenterX { get; set; } = -0.5;
    public double CenterY { get; set; } = 0.0;
    public double Zoom { get; set; } = 1.0;
    public int MaxIterations { get; set; } = 256;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public MandelbrotCompute(int width, int height)
    {
        Width = width;
        Height = height;

        _context = Context.Create(builder => builder.Default().EnableAlgorithms());

        // Try to get a GPU accelerator; fall back to CPU
        Device? bestDevice = _context.GetPreferredDevice(preferCPU: false);
        if (bestDevice == null)
        {
            Console.WriteLine("Warning: No preferred GPU/CPU device found by ILGPU. Defaulting to first available.");
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
            
            var cpuDevice = _context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.CPU);
            if (cpuDevice == null) throw new NotSupportedException("No CPU accelerator fallback available.");
            
            _accelerator = cpuDevice.CreateAccelerator(_context);
        }

        Console.WriteLine($"Using accelerator: {_accelerator.Name} ({_accelerator.AcceleratorType})");

        _kernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView1D<float, Stride1D.Dense>,
            int, int,
            double, double, double, double,
            int>(MandelbrotKernel);
    }

    /// <summary>
    /// ILGPU kernel: computes Mandelbrot iteration count for each pixel.
    /// Normalized to [0..1] range for easy height-mapping.
    /// </summary>
    private static void MandelbrotKernel(
        Index1D index,
        ArrayView1D<float, Stride1D.Dense> output,
        int width, int height,
        double xMin, double yMin, double xMax, double yMax,
        int maxIterations)
    {
        int px = index % width;
        int py = index / width;

        double x0 = xMin + (xMax - xMin) * px / (width - 1);
        double y0 = yMin + (yMax - yMin) * py / (height - 1);

        double x = 0.0;
        double y = 0.0;
        int iteration = 0;

        while (x * x + y * y <= 4.0 && iteration < maxIterations)
        {
            double xTemp = x * x - y * y + x0;
            y = 2.0 * x * y + y0;
            x = xTemp;
            iteration++;
        }

        // Smooth coloring using continuous iteration count
        if (iteration < maxIterations)
        {
            double zn = x * x + y * y;
            double log2zn = XMath.Log2((float)zn) / 2.0;
            double nu = XMath.Log2((float)log2zn);
            double smooth = iteration + 1.0 - nu;
            output[index] = (float)(smooth / maxIterations);
        }
        else
        {
            output[index] = 0.0f; // Inside the set
        }
    }

    /// <summary>
    /// Compute the Mandelbrot set on the GPU and return iteration data.
    /// </summary>
    public float[] Compute()
    {
        int totalPixels = Width * Height;

        double aspectRatio = (double)Width / Height;
        double rangeY = 2.0 / Zoom;
        double rangeX = rangeY * aspectRatio;

        double xMin = CenterX - rangeX;
        double xMax = CenterX + rangeX;
        double yMin = CenterY - rangeY;
        double yMax = CenterY + rangeY;

        using var deviceOutput = _accelerator.Allocate1D<float>(totalPixels);

        _kernel(totalPixels, deviceOutput.View, Width, Height, xMin, yMin, xMax, yMax, MaxIterations);
        _accelerator.Synchronize();

        float[] result = new float[totalPixels];
        deviceOutput.CopyToCPU(result);

        return result;
    }

    /// <summary>
    /// Updates grid resolution (for window resize).
    /// </summary>
    public void Resize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        _accelerator.Dispose();
        _context.Dispose();
    }
}
