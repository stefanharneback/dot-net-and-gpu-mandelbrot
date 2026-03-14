namespace MandelbrotGpu;

public sealed class HeightFieldFrame
{
    public HeightFieldFrame(
        float[] data,
        int width,
        int height,
        string precisionMode,
        double kernelDispatchMs,
        double gpuSynchronizeMs,
        double readbackMs,
        long managedAllocatedBytes)
    {
        Data = data;
        Width = width;
        Height = height;
        PrecisionMode = precisionMode;
        KernelDispatchMs = kernelDispatchMs;
        GpuSynchronizeMs = gpuSynchronizeMs;
        ReadbackMs = readbackMs;
        ManagedAllocatedBytes = managedAllocatedBytes;
    }

    public float[] Data { get; }

    public int Width { get; }

    public int Height { get; }

    public string PrecisionMode { get; }

    public double KernelDispatchMs { get; }

    public double GpuSynchronizeMs { get; }

    public double ReadbackMs { get; }

    public long ManagedAllocatedBytes { get; }

    public uint TextureHandle { get; internal set; }

    public double TextureUploadMs { get; internal set; }
}
