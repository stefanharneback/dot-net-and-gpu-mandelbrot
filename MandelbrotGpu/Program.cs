using MandelbrotGpu;

Console.WriteLine("🌀 Starting GPU Mandelbrot 3D Explorer...");
Console.WriteLine("   Built with .NET 10 + ILGPU + Silk.NET/OpenGL");
Console.WriteLine();

using var app = new MandelbrotApp();
app.Run();
