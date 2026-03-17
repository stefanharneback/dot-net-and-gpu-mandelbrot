namespace MandelbrotGpu;

public enum FractalKind
{
    Mandelbrot,
    Julia,
    BurningShip,
    Tricorn,
    Celtic
}

public sealed record FractalDefinition(
    FractalKind Kind,
    string DisplayName,
    double DefaultCenterX,
    double DefaultCenterY,
    double DefaultZoom,
    int DefaultIterations,
    double JuliaConstantX = 0.0,
    double JuliaConstantY = 0.0,
    float DefaultHeightScale = 0.6f)
{
    public bool UsesJuliaConstant => Kind == FractalKind.Julia;

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
        new(FractalKind.Mandelbrot, "Mandelbrot", -0.5, 0.0, 1.0, 256),
        new(FractalKind.Julia, "Julia", 0.0, 0.0, 1.15, 384, -0.8, 0.156),
        new(FractalKind.BurningShip, "Burning Ship", -0.45, -0.5, 1.15, 384),
        new(FractalKind.Tricorn, "Tricorn", 0.0, 0.0, 1.1, 320),
        new(FractalKind.Celtic, "Celtic", -0.4, 0.0, 1.1, 320)
    ];
}
