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

    /// <summary>
    /// Generate a smooth, vivid color palette using cosine gradients.
    /// Returns float[size * 3] as RGB triplets in [0..1].
    /// </summary>
    public static float[] GenerateVibrantPalette(int size = 1024)
    {
        float[] palette = new float[size * 3];

        for (int i = 0; i < size; i++)
        {
            float t = (float)i / size;

            // Cosine-based gradient: a + b * cos(2π * (c * t + d))
            // Using carefully tuned coefficients for a rich, multi-hue gradient
            float r = CosineGradient(t, 0.5f, 0.5f, 1.0f, 0.00f);
            float g = CosineGradient(t, 0.5f, 0.5f, 1.0f, 0.33f);
            float b = CosineGradient(t, 0.5f, 0.5f, 1.0f, 0.67f);

            // Apply power curve for richer darks
            r = MathF.Pow(r, 0.8f);
            g = MathF.Pow(g, 0.8f);
            b = MathF.Pow(b, 0.8f);

            palette[i * 3 + 0] = Math.Clamp(r, 0f, 1f);
            palette[i * 3 + 1] = Math.Clamp(g, 0f, 1f);
            palette[i * 3 + 2] = Math.Clamp(b, 0f, 1f);
        }

        return palette;
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
        float[] palette = new float[size * 3];

        for (int i = 0; i < size; i++)
        {
            float t = (float)i / size;

            // Deep blue → cyan → teal → white
            float r = CosineGradient(t, 0.2f, 0.3f, 1.5f, 0.75f);
            float g = CosineGradient(t, 0.3f, 0.5f, 1.2f, 0.45f);
            float b = CosineGradient(t, 0.5f, 0.5f, 0.8f, 0.15f);

            palette[i * 3 + 0] = Math.Clamp(r, 0f, 1f);
            palette[i * 3 + 1] = Math.Clamp(g, 0f, 1f);
            palette[i * 3 + 2] = Math.Clamp(b, 0f, 1f);
        }

        return palette;
    }

    /// <summary>
    /// Generate a neon/synthwave themed palette (electric purple/pink/cyan).
    /// </summary>
    public static float[] GenerateNeonPalette(int size = 1024)
    {
        float[] palette = new float[size * 3];

        for (int i = 0; i < size; i++)
        {
            float t = (float)i / size;

            // Purple → hot pink → cyan → electric blue
            float r = CosineGradient(t, 0.5f, 0.5f, 2.0f, 0.50f);
            float g = CosineGradient(t, 0.2f, 0.4f, 1.5f, 0.10f);
            float b = CosineGradient(t, 0.6f, 0.4f, 1.0f, 0.80f);

            // Boost saturation
            r = MathF.Pow(Math.Clamp(r, 0f, 1f), 0.7f);
            g = MathF.Pow(Math.Clamp(g, 0f, 1f), 0.9f);
            b = MathF.Pow(Math.Clamp(b, 0f, 1f), 0.6f);

            palette[i * 3 + 0] = r;
            palette[i * 3 + 1] = g;
            palette[i * 3 + 2] = b;
        }

        return palette;
    }

    private static float CosineGradient(float t, float a, float b, float c, float d)
    {
        return a + b * MathF.Cos(2f * MathF.PI * (c * t + d));
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
