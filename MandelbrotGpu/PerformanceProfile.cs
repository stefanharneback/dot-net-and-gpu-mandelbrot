namespace MandelbrotGpu;

public enum PerformanceProfile
{
    Latency,
    Balanced,
    Quality,
    Screenshot
}

public enum ShadingMode
{
    Simplified,
    Full
}

public readonly record struct PerformanceSettings(
    PerformanceProfile Profile,
    int ComputeResolution,
    int RenderMeshResolution,
    bool VSyncEnabled,
    int MsaaSamples,
    bool HudEnabled,
    bool ShowGrid,
    ShadingMode ShadingMode,
    bool AdaptiveResolutionEnabled)
{
    public static PerformanceSettings Create(PerformanceProfile profile) => profile switch
    {
        PerformanceProfile.Latency => new(profile, 768, 512, false, 0, true, false, ShadingMode.Simplified, true),
        PerformanceProfile.Balanced => new(profile, 1024, 768, true, 4, true, true, ShadingMode.Full, true),
        PerformanceProfile.Quality => new(profile, 1536, 1024, true, 4, true, true, ShadingMode.Full, true),
        PerformanceProfile.Screenshot => new(profile, 2048, 1536, false, 4, true, false, ShadingMode.Full, false),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };
}

internal readonly record struct ResolutionTier(int ComputeResolution, int RenderMeshResolution);
