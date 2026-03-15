# Distributing Mandelbrot Explorer

The most reliable way to share this application is as a **Self-Contained Folder**. While a single `.exe` is convenient, it often fails with high-performance graphics libraries (Silk.NET/OpenGL) because the native drivers (`glfw3.dll`) cannot be extracted and loaded fast enough from inside the bundle.

## 1. Build the Stable Package
Run this command in your terminal from the `MandelbrotGpu` folder:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false --output ./bin/publish
```

### What this command does:
- `-c Release`: Optimizes the code for maximum speed.
- `-r win-x64`: Targets 64-bit Windows.
- `--self-contained true`: Includes the .NET 10 runtime.
- `-p:PublishSingleFile=false`: **This is key.** Keeping the files separate ensures the GPU drivers and native libraries load instantly and correctly.
- `--output ./bin/publish`: Puts the release in the gitignored `bin` folder.

## 2. How to Share (The "Zip" Method)
1. Go to the `MandelbrotGpu/bin/publish` folder.
2. Select all files, right-click, and **Compress to ZIP file**.
3. Send this ZIP to your friends.
4. They just need to **Extract All** and run the `MandelbrotGpu.exe` inside the folder.

## 3. Requirements for your Friends
- **OS**: Windows 10/11 (64-bit).
- **GPU**: Must support **OpenGL 4.3** (Standard on most GPUs from the last 10 years).
- **Drivers**: They should have up-to-date GPU drivers installed.

## Why avoid the single `.exe`?
Standalone single-file executables in .NET have to "unzip" themselves to a temporary folder on the user's hard drive every time they run. Many Antivirus programs block this behavior, and it often fails to find the Silk.NET windowing drivers. The "Folder + Zip" approach is the industry standard for distributing PC games and high-performance graphics apps.
