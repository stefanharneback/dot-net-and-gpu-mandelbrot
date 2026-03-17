using System.Windows;
using MandelbrotGpu;

Console.WriteLine("🌀 Starting GPU Fractal 3D Explorer...");
Console.WriteLine("   Built with .NET 10 + ILGPU + Silk.NET/OpenGL");
Console.WriteLine();

using var app = new MandelbrotApp();
try
{
    app.Run();
}
catch (Exception ex)
{
    string diagnostics = app.BuildDiagnosticSnapshot();
    string? logPath = AppDiagnostics.TryWriteFatalError(ex, diagnostics);
    AppDiagnostics.ShowFatalError("GPU Fractal 3D Explorer", ex, diagnostics, logPath);
    Environment.ExitCode = 1;
}
