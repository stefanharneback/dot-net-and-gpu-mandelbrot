namespace MandelbrotGpu;

public readonly record struct PerformanceMetrics(
    double KernelDispatchMs,
    double GpuSynchronizeMs,
    double ReadbackMs,
    double TextureUploadMs,
    double GridBuildMs,
    double DrawMs,
    double InteractionLatencyMs,
    long ManagedAllocatedBytes)
{
    public static PerformanceMetrics Empty => new();
}
