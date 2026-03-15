namespace MandelbrotGpu;

/// <summary>
/// Rich multi-color palette generator for Mandelbrot visualization.
/// Uses cosine-based color gradients for smooth, vibrant transitions.
/// </summary>
public static class ColorPalette
{
    public static float[] GeneratePalette(int paletteIndex, int size = 1024) => paletteIndex switch
    {
        0 => GenerateVibrantPalette(size),
        1 => GenerateFirePalette(size),
        2 => GenerateOceanPalette(size),
        3 => GenerateNeonPalette(size),
        _ => GenerateVibrantPalette(size)
    };

    public static float GetPaletteCycles(int paletteIndex) => paletteIndex switch
    {
        0 => 18f,
        1 => 10f,
        2 => 22f,
        3 => 26f,
        _ => 14f
    };

    /// <summary>
    /// Generate a smooth, vivid color palette using cosine gradients.
    /// Returns float[size * 3] as RGB triplets in [0..1].
    /// </summary>
    public static float[] GenerateVibrantPalette(int size = 1024)
    {
        return GenerateGradientPalette(
            size,
            (0.00f, 0.02f, 0.02f, 0.07f),
            (0.12f, 0.12f, 0.05f, 0.32f),
            (0.24f, 0.05f, 0.26f, 0.78f),
            (0.38f, 0.02f, 0.72f, 0.92f),
            (0.52f, 0.16f, 0.90f, 0.42f),
            (0.66f, 0.92f, 0.84f, 0.18f),
            (0.80f, 0.98f, 0.42f, 0.14f),
            (0.92f, 0.82f, 0.10f, 0.45f),
            (1.00f, 0.98f, 0.94f, 0.98f));
    }

    /// <summary>
    /// Generate a fire/lava themed palette (great for Mandelbrot).
    /// </summary>
    public static float[] GenerateFirePalette(int size = 1024)
    {
        float[] palette = new float[size * 3];

        for (int i = 0; i < size; i++)
        {
            float t = (float)i / size;

            // Dark → red → orange → yellow → white
            float r, g, b;
            if (t < 0.25f)
            {
                float s = t / 0.25f;
                r = s * 0.6f;
                g = 0f;
                b = s * 0.15f;
            }
            else if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                r = 0.6f + s * 0.4f;
                g = s * 0.4f;
                b = 0.15f * (1f - s);
            }
            else if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                r = 1.0f;
                g = 0.4f + s * 0.5f;
                b = s * 0.2f;
            }
            else
            {
                float s = (t - 0.75f) / 0.25f;
                r = 1.0f;
                g = 0.9f + s * 0.1f;
                b = 0.2f + s * 0.8f;
            }

            palette[i * 3 + 0] = r;
            palette[i * 3 + 1] = g;
            palette[i * 3 + 2] = b;
        }

        return palette;
    }

    /// <summary>
    /// Generate an ocean/ice themed palette.
    /// </summary>
    public static float[] GenerateOceanPalette(int size = 1024)
    {
        return GenerateGradientPalette(
            size,
            (0.00f, 0.01f, 0.03f, 0.09f),
            (0.16f, 0.02f, 0.12f, 0.32f),
            (0.32f, 0.02f, 0.36f, 0.62f),
            (0.48f, 0.07f, 0.72f, 0.84f),
            (0.62f, 0.18f, 0.90f, 0.70f),
            (0.76f, 0.92f, 0.90f, 0.56f),
            (0.88f, 0.98f, 0.62f, 0.34f),
            (1.00f, 0.96f, 0.96f, 0.99f));
    }

    /// <summary>
    /// Generate a neon/synthwave themed palette (electric purple/pink/cyan).
    /// </summary>
    public static float[] GenerateNeonPalette(int size = 1024)
    {
        return GenerateGradientPalette(
            size,
            (0.00f, 0.02f, 0.01f, 0.06f),
            (0.10f, 0.18f, 0.02f, 0.34f),
            (0.24f, 0.54f, 0.08f, 0.80f),
            (0.38f, 0.98f, 0.14f, 0.72f),
            (0.52f, 1.00f, 0.44f, 0.22f),
            (0.66f, 0.95f, 0.92f, 0.22f),
            (0.80f, 0.08f, 0.98f, 0.88f),
            (0.92f, 0.10f, 0.48f, 1.00f),
            (1.00f, 0.99f, 0.99f, 1.00f));
    }

    private static float CosineGradient(float t, float a, float b, float c, float d)
    {
        return a + b * MathF.Cos(2f * MathF.PI * (c * t + d));
    }

    private static float[] GenerateGradientPalette(int size, params (float Position, float R, float G, float B)[] stops)
    {
        float[] palette = new float[size * 3];

        for (int i = 0; i < size; i++)
        {
            float t = i / (float)(size - 1);
            int stopIndex = 0;

            while (stopIndex < stops.Length - 2 && t > stops[stopIndex + 1].Position)
                stopIndex++;

            var start = stops[stopIndex];
            var end = stops[Math.Min(stopIndex + 1, stops.Length - 1)];
            float span = Math.Max(0.0001f, end.Position - start.Position);
            float localT = Math.Clamp((t - start.Position) / span, 0f, 1f);

            float r = Lerp(start.R, end.R, localT);
            float g = Lerp(start.G, end.G, localT);
            float b = Lerp(start.B, end.B, localT);

            palette[i * 3 + 0] = Math.Clamp(r, 0f, 1f);
            palette[i * 3 + 1] = Math.Clamp(g, 0f, 1f);
            palette[i * 3 + 2] = Math.Clamp(b, 0f, 1f);
        }

        return palette;
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }

    /// <summary>
    /// Map a normalized Mandelbrot value [0..1] to RGB color from palette.
    /// </summary>
    public static (float r, float g, float b) SamplePalette(float[] palette, float t)
    {
        int paletteSize = palette.Length / 3;

        if (t <= 0f)
            return (0f, 0f, 0f); // Inside the set = black

        // Cycle through palette with nice looping
        float scaledT = t * 8f; // Repeat the palette a few times for visual richness
        scaledT -= MathF.Floor(scaledT);

        float index = scaledT * (paletteSize - 1);
        int i0 = (int)index;
        int i1 = Math.Min(i0 + 1, paletteSize - 1);
        float frac = index - i0;

        // Linearly interpolate between adjacent palette entries
        float r = palette[i0 * 3 + 0] * (1f - frac) + palette[i1 * 3 + 0] * frac;
        float g = palette[i0 * 3 + 1] * (1f - frac) + palette[i1 * 3 + 1] * frac;
        float b = palette[i0 * 3 + 2] * (1f - frac) + palette[i1 * 3 + 2] * frac;

        return (r, g, b);
    }
}
