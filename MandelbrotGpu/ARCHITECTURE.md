# Fractal 3D Explorer Architecture

This application generates and visualizes multiple complex-plane fractal sets in real-time 3D by strictly separating the workload: the **CPU manages the application state and orchestrates commands**, while the **GPU performs 100% of the mathematical calculations and rendering**.

> [!NOTE]
> This application requires a GPU supporting **OpenGL 4.3** or higher for dynamic color range normalization via SSBOs.

## The CPU Role (The Orchestrator)
The CPU acts as the brain of the application. It handles logic that doesn't require massive parallelization:

1. **Windowing & Input:** Listens to keyboard/mouse events (panning, zooming) using Silk.NET and updates the `Camera` position.
2. **State Management:** Tracks configuration variables such as fractal selection, zoom level, coordinates, Julia parameters, precision modes (FP32/FP64), and performance profiles.
3. **GPU Dispatching:** Tells the GPU *what* to do and *when* to do it. The CPU issues commands to set coordinate uniforms, select the active fractal formula, and fire off the mathematical computations. It does not calculate the fractal itself.
4. **UI Threading:** Runs the secondary WPF HUD window on a separate STA thread so the GUI does not stall the strict OpenGL rendering loop.

## The GPU Role (The Number Cruncher)
The GPU handles the unimaginable scale of math required for real-time 3D fractals. It does this in two identical but separate pipelines:

### 1. The Compute Pipeline (Math)
We wrote raw GLSL Compute Shaders (`MandelbrotComputeShaderF32` and `F64`) to calculate the active fractal. A lightweight uniform-based selector switches between Mandelbrot, Julia, Burning Ship, Tricorn, and Celtic formulas without changing the surrounding render pipeline.
- *Why Compute Shaders?* A GPU has thousands of cores. 1080p means calculating 2,073,600 pixels. The GPU groups these pixels into 16x16 blocks and calculates thousands of them simultaneously.
- *The Output:* It writes the final 'iteration count' (the height of the fractal at that pixel) directly into an empty slot in its own Video RAM (an OpenGL `Texture2D`). 

### 2. The Render Pipeline (Visuals)
Immediately after the Compute Shader finishes, the Render Pipeline takes over.
- *The Input:* It reads the `Texture2D` stored in VRAM from the compute step.
- *Vertex Shader:* Displaces a flat grid into 3D mountains by pushing vertices up or down based on the height values in the texture. It also computes real-time normals for lighting.
- *Fragment Shader:* Applies the color palette and calculates real-time diffuse/specular lighting using the view and light direction.

## Why this is the "Best" Approach
1. **Zero-Copy Memory:** Earlier versions calculated the math on the GPU, copied the massive result array back to the CPU via the PCIe bus, and then the CPU sent it *back* to the GPU to draw. This was a massive bottleneck. Now, the data is generated in VRAM, stays in VRAM, and is drawn from VRAM.
2. **Adaptive Precision Switching:** GPUs process 32-bit floats ("FP32") extremely fast, but they lose accuracy when zooming very deep. They process 64-bit doubles ("FP64") slower, but allow extreme zooming. The CPU actively monitors the zoom height and swaps the underlying GPU GLSL shader automatically to guarantee the fastest framerates without visual artifacting.
3. **Formula-Specific Shortcuts:** The shared compute path keeps loop unrolling and periodicity checks for all supported fractals, and Mandelbrot mode still gets its extra cardioid/bulb rejection so the classic view remains fast.
