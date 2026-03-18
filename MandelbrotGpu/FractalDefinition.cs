namespace MandelbrotGpu;

public enum FractalKind
{
    Mandelbrot,
    Julia,
    BurningShip,
    Tricorn,
    Celtic
}

public sealed record FractalPreset(
    string Name,
    double CenterX,
    double CenterY,
    double Zoom,
    int Iterations,
    float HeightScale);

public sealed record FractalDefinition(
    FractalKind Kind,
    string DisplayName,
    IReadOnlyList<FractalPreset> Presets,
    double JuliaConstantX = 0.0,
    double JuliaConstantY = 0.0)
{
    public bool UsesJuliaConstant => Kind == FractalKind.Julia;

    public FractalPreset DefaultPreset => Presets[0];

    public double DefaultCenterX => DefaultPreset.CenterX;

    public double DefaultCenterY => DefaultPreset.CenterY;

    public double DefaultZoom => DefaultPreset.Zoom;

    public int DefaultIterations => DefaultPreset.Iterations;

    public float DefaultHeightScale => DefaultPreset.HeightScale;

    public string ParameterSummary => UsesJuliaConstant
        ? $"c = {FormatComplex(JuliaConstantX, JuliaConstantY)}"
        : "c = mapped plane";

    private static string FormatComplex(double real, double imaginary)
    {
        string sign = imaginary >= 0 ? "+" : "-";
        return $"{real:G6} {sign} {Math.Abs(imaginary):G6}i";
    }
}

public static class FractalCatalog
{
    public static readonly FractalDefinition[] Definitions =
    [
        new(
            FractalKind.Mandelbrot,
            "Mandelbrot",
            [
                new("Heartline Overview", -0.5, 0.0, 1.0, 256, 0.60f),
                new("Seahorse Valley", -0.745, 0.105, 180.0, 640, 0.72f),
                new("Spiral Shelf", -0.743643887037151, 0.13182590420533, 10000.0, 1200, 0.82f),
                new("Elephant Ridge", 0.27322626, 0.595153338, 2000.0, 1100, 0.78f),
                new("Mini Mandelbrot", -1.25066, 0.02012, 9000.0, 1500, 0.90f),
                new("Atomic Filigree", -0.743644786, 0.1318252536, 2500000.0, 3200, 1.02f)
            ]),
        new(
            FractalKind.Julia,
            "Julia",
            [
                new("Glass Bloom", 0.0, 0.0, 1.15, 384, 0.62f),
                new("Coral Wing", -0.45, 0.57, 2.8, 512, 0.68f),
                new("Twin Petals", 0.31, -0.04, 5.2, 640, 0.74f),
                new("Silk Spine", -0.62, -0.43, 9.5, 768, 0.80f),
                new("Lace Basin", 0.12, 0.74, 18.0, 896, 0.86f),
                new("Frost Nerve", 0.36, 0.10, 36.0, 1100, 0.94f)
            ],
            -0.8,
            0.156),
        new(
            FractalKind.BurningShip,
            "Burning Ship",
            [
                new("Ember Hull", -0.45, -0.5, 1.15, 384, 0.68f),
                new("Ember Cliffs", -1.75, -0.03, 8.0, 576, 0.78f),
                new("Furnace Bay", -1.758, -0.034, 36.0, 832, 0.88f),
                new("Smoke Pillars", -1.7705, -0.0426, 120.0, 1152, 0.96f),
                new("Cinder Cathedral", -1.7862, -0.0318, 320.0, 1500, 1.04f),
                new("Ash Filaments", -1.7693, -0.0405, 900.0, 1900, 1.14f)
            ]),
        new(
            FractalKind.Tricorn,
            "Tricorn",
            [
                new("Mirror Basin", 0.0, 0.0, 1.1, 320, 0.60f),
                new("Raven Wing", -0.46, 0.0, 3.4, 448, 0.68f),
                new("Triple Node", 0.22, -0.52, 6.5, 576, 0.74f),
                new("Ice Fork", -0.08, 0.72, 11.0, 704, 0.80f),
                new("Obsidian Split", 0.31, -0.54, 22.0, 928, 0.88f),
                new("Needle Lace", -0.14, 0.76, 40.0, 1152, 0.96f)
            ]),
        new(
            FractalKind.Celtic,
            "Celtic",
            [
                new("Knot Garden", -0.4, 0.0, 1.1, 320, 0.62f),
                new("Braided Reach", -0.74, 0.12, 16.0, 448, 0.72f),
                new("Glass Knot", -0.39, 0.57, 7.0, 576, 0.76f),
                new("Woven Chamber", -1.06, 0.23, 32.0, 832, 0.86f),
                new("Ribbon Vault", -0.75097, 0.10779, 220.0, 1280, 0.96f),
                new("Filigree Vault", -0.75097, 0.10779, 1500.0, 1800, 1.06f)
            ])
    ];
}
