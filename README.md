# 🌀 GPU Mandelbrot 3D Explorer

A real-time, GPU-accelerated 3D Mandelbrot set visualization built with **.NET 10**, **ILGPU**, and **Silk.NET/OpenGL**.

![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)
![ILGPU](https://img.shields.io/badge/GPU-ILGPU-green)
![OpenGL](https://img.shields.io/badge/Rendering-OpenGL-blue)

## Features

- **GPU-Accelerated Computation** — Mandelbrot iteration counts computed massively parallel on your GPU via ILGPU
- **3D Terrain Visualization** — Iteration counts become terrain heights, rendered as a 3D mesh with OpenGL
- **Blinn-Phong Lighting** — Full lighting model with ambient, diffuse, specular, and rim lighting
- **4 Color Palettes** — Vibrant, Fire, Ocean, and Neon palettes using cosine gradients
- **Interactive Camera** — Orbit, zoom, and pan with mouse; navigate the fractal with keyboard
- **Real-time Fractal Exploration** — Pan and zoom into the Mandelbrot set in real-time
- **Wireframe Mode** — Toggle wireframe rendering to see the mesh structure
- **Adjustable Parameters** — Grid resolution, height scale, iteration count, all configurable at runtime

## Requirements

- .NET 10 SDK
- A GPU with OpenGL 3.3+ support (NVIDIA, AMD, or Intel integrated)
- ILGPU-compatible GPU for acceleration (falls back to CPU if unavailable)

## Quick Start

```bash
cd MandelbrotGpu
dotnet run
```

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
| Cycle grid resolution | `G` |
| Reset view | `R` |
| Exit | `Esc` |

## Architecture

```
MandelbrotGpu/
├── Program.cs           # Entry point
├── MandelbrotApp.cs     # Main application (windowing, rendering, input)
├── MandelbrotCompute.cs # GPU kernel for Mandelbrot computation (ILGPU)
├── MeshBuilder.cs       # Converts iteration data to 3D terrain mesh
├── Camera.cs            # Orbital camera with spherical coordinates
├── ColorPalette.cs      # Cosine-gradient color palette generator
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
