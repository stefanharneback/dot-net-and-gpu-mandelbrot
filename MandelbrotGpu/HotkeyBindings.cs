using WpfKey = System.Windows.Input.Key;
using SilkKey = Silk.NET.Input.Key;

namespace MandelbrotGpu;

internal sealed record ControlLegend(string Action, string Input, string Note = "");

internal static class HotkeyBindings
{
    public const string PerformanceProfile = "N";
    public const string FractalSet = "T";
    public const string PresetSelection = "1 .. 6 / R";
    public const string Precision = "M";
    public const string Iterations = "I / K";
    public const string Palette = "C";
    public const string HeightScale = "PageUp / PageDown";
    public const string Wireframe = "F";
    public const string ResolutionPath = "G / O / N";
    public const string AdaptiveResolution = "O";
    public const string Shading = "L";
    public const string VSync = "V";
    public const string FocusHud = "H";
    public const string FractalPan = "W / A / S / D";
    public const string FractalPanAlt = "Arrow keys";
    public const string FractalZoom = "+ / -";
    public const string StateDump = "P";
    public const string ResetView = "R";
    public const string Exit = "Esc";

    public static readonly IReadOnlyList<ControlLegend> CameraControls =
    [
        new("Orbit camera", "Left drag"),
        new("Pan camera", "Middle drag"),
        new("Zoom camera", "Mouse wheel"),
        new("Focus HUD windows", FocusHud)
    ];

    public static readonly IReadOnlyList<ControlLegend> FractalNavigationControls =
    [
        new("Pan fractal", FractalPan, $"or {FractalPanAlt}"),
        new("Zoom fractal", FractalZoom, "Shift = fine"),
        new("Print state dump", StateDump, "preset + GL info"),
        new("Reset to preset 1", ResetView, "current fractal")
    ];

    public static readonly IReadOnlyList<ControlLegend> UtilityControls =
    [
        new("Curated presets", "1 .. 6", "active fractal"),
        new("Exit application", Exit)
    ];

    public static readonly string[] ConsoleLegendLines =
    [
        "Mouse: Left drag orbit | Middle drag pan | Wheel zoom",
        $"Move fractal: {FractalPan} or {FractalPanAlt}",
        $"Zoom fractal: {FractalZoom} (hold Shift for fine control)",
        $"Iterations: {Iterations} | Height scale: {HeightScale}",
        $"Fractal set: {FractalSet} | Presets: 1..6 | Reset: {ResetView}",
        $"Palette: {Palette} | Wireframe: {Wireframe} | Shading: {Shading}",
        $"Resolution tier: G | Precision: {Precision} | Profile: {PerformanceProfile}",
        $"Adaptive res: {AdaptiveResolution} | HUDs: {FocusHud} | VSync: {VSync} | Dump: {StateDump}",
        $"T switches sets and lands on preset 1 | Exit: {Exit}"
    ];

    public static string FractalCycleNote =>
        "T cycles Mandelbrot, Julia, Burning Ship, Tricorn, and Celtic, always landing on preset 1 for the newly selected set. " +
        "Presets 1..6 are curated hotspots for the active fractal. Hold Shift while using +/- for finer zoom adjustments. " +
        "H focuses both HUD windows.";

    private static readonly IReadOnlyDictionary<WpfKey, SilkKey> HudKeyMap = new Dictionary<WpfKey, SilkKey>
    {
        [WpfKey.W] = SilkKey.W,
        [WpfKey.A] = SilkKey.A,
        [WpfKey.S] = SilkKey.S,
        [WpfKey.D] = SilkKey.D,
        [WpfKey.Up] = SilkKey.Up,
        [WpfKey.Down] = SilkKey.Down,
        [WpfKey.Left] = SilkKey.Left,
        [WpfKey.Right] = SilkKey.Right,
        [WpfKey.Add] = SilkKey.KeypadAdd,
        [WpfKey.OemPlus] = SilkKey.Equal,
        [WpfKey.Subtract] = SilkKey.KeypadSubtract,
        [WpfKey.OemMinus] = SilkKey.Minus,
        [WpfKey.I] = SilkKey.I,
        [WpfKey.K] = SilkKey.K,
        [WpfKey.C] = SilkKey.C,
        [WpfKey.F] = SilkKey.F,
        [WpfKey.G] = SilkKey.G,
        [WpfKey.H] = SilkKey.H,
        [WpfKey.L] = SilkKey.L,
        [WpfKey.M] = SilkKey.M,
        [WpfKey.N] = SilkKey.N,
        [WpfKey.O] = SilkKey.O,
        [WpfKey.P] = SilkKey.P,
        [WpfKey.R] = SilkKey.R,
        [WpfKey.T] = SilkKey.T,
        [WpfKey.V] = SilkKey.V,
        [WpfKey.PageUp] = SilkKey.PageUp,
        [WpfKey.PageDown] = SilkKey.PageDown,
        [WpfKey.D1] = SilkKey.Number1,
        [WpfKey.D2] = SilkKey.Number2,
        [WpfKey.D3] = SilkKey.Number3,
        [WpfKey.D4] = SilkKey.Number4,
        [WpfKey.D5] = SilkKey.Number5,
        [WpfKey.D6] = SilkKey.Number6,
        [WpfKey.Escape] = SilkKey.Escape
    };

    public static bool TryMapHudKey(WpfKey key, out SilkKey mappedKey) => HudKeyMap.TryGetValue(key, out mappedKey);
}
