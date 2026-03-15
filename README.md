# 🌀 GPU Mandelbrot 3D Explorer

A real-time, GPU-accelerated 3D Mandelbrot set visualization built with **.NET 10**, **ILGPU**, and **Silk.NET/OpenGL**.

![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)
![ILGPU](https://img.shields.io/badge/GPU-ILGPU-green)
![OpenGL](https://img.shields.io/badge/Rendering-OpenGL-blue)

## Features

- **GPU-Accelerated Computation** — Mandelbrot iteration counts computed massively parallel on your GPU via ILGPU
- **Height-Field Terrain Renderer** — A static terrain grid is displaced in the vertex shader from a streamed height texture
- **GPU-Side Lighting & Palette Lookup** — Normals and palette colors are derived in shaders instead of CPU mesh generation
- **Performance Profiles** — `Latency`, `Balanced`, `Quality`, and `Screenshot` modes tune compute resolution, render mesh density, shading, HUD, and VSync
- **Dual HUD Windows** — Separate live-status/performance and controls/settings windows stay readable during exploration
- **4 Color Palettes** — Vibrant, Fire, Ocean, and Neon palettes tuned for stronger deep-zoom contrast and detail separation
- **Interactive Camera** — Orbit, zoom, and pan with mouse; navigate the fractal with keyboard
- **Real-time Fractal Exploration** — Pan and zoom into the Mandelbrot set in real-time
- **Wireframe Mode** — Toggle wireframe rendering to see the mesh structure
- **Timing Instrumentation** — Kernel dispatch, GPU sync, readback, texture upload, draw submit, and interaction latency are exposed in the HUD

## Requirements

- .NET 10 SDK
- A GPU with OpenGL 3.3+ support (NVIDIA, AMD, or Intel integrated)
- ILGPU-compatible GPU for acceleration (falls back to CPU if unavailable)

## Quick Start

```bash
cd MandelbrotGpu
dotnet run
```

### Release Build From Repo Root

From the repository root you can publish a release build directly into the root `bin/` folder:

```powershell
.\build-release.ps1
```

If you prefer `cmd.exe`:

```bat
build-release.cmd
```

This is intentionally a wrapper around `dotnet publish MandelbrotGpu/MandelbrotGpu.csproj -c Release -o .\bin` so normal `dotnet build` output paths stay conventional, while the repo still has a single root-level release command.

## Controls

### 🖱️ Mouse
| Action | Control |
|--------|---------|
| Orbit / Rotate camera | Left mouse drag |
| Pan camera | Middle mouse drag |
| Zoom camera | Scroll wheel |

### ⌨️ Keyboard — Fractal Navigation
| Action | Key |
|--------|-----|
| Pan fractal | `W`/`A`/`S`/`D` or Arrow keys |
| Zoom into fractal | `+`/`-` (or numpad) |
| Increase iterations | `I` |
| Decrease iterations | `K` |
| Adjust height scale | `PageUp` / `PageDown` |

### 🎨 Display
| Action | Key |
|--------|-----|
| Cycle color palette | `C` |
| Toggle wireframe | `F` |
| Cycle manual resolution tier | `G` |
| Cycle precision mode | `M` |
| Cycle performance profile | `N` |
| Toggle shading mode | `L` |
| Toggle adaptive resolution | `O` |
| Focus HUD windows | `H` |
| Toggle VSync | `V` |
| Reset view | `R` |
| Exit | `Esc` |

## Architecture

```
MandelbrotGpu/
├── Program.cs           # Entry point
├── MandelbrotApp.cs     # Main application (windowing, rendering, input)
├── MandelbrotCompute.cs # GPU kernel for Mandelbrot computation (ILGPU)
├── HeightFieldRenderer.cs # Static grid renderer + height/palette texture uploads
├── TerrainGridCache.cs    # Builds reusable XY terrain grids and index buffers
├── HeightFieldFrame.cs    # Compute result + timing metadata for the current height field
├── HudWindowBase.cs       # Shared WPF HUD styling, key forwarding, and persistence behavior
├── StatusHUD.cs           # Live parameters, render state, and performance HUD window
├── PerformanceProfile.cs  # Performance profiles, shading modes, and runtime settings
├── PerformanceMetrics.cs  # Latest timing/instrumentation snapshot
├── Camera.cs            # Orbital camera with spherical coordinates
├── ColorPalette.cs      # High-contrast palette generator and palette-cycle tuning
└── Shaders.cs           # GLSL vertex + fragment shaders
```

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10 |
| GPU Compute | ILGPU 1.5.1 |
| Windowing & OpenGL | Silk.NET 2.22.0 |
| Shaders | GLSL 330 Core |
| Math | System.Numerics |
