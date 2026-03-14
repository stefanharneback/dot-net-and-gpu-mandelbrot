namespace MandelbrotGpu;

/// <summary>
/// Builds a 3D terrain mesh from Mandelbrot iteration data.
/// Each pixel becomes a vertex with height proportional to its iteration count.
/// Includes vertex positions, normals, and colors for rich 3D rendering.
/// </summary>
public static class MeshBuilder
{
    /// <summary>
    /// Vertex data: position (3), normal (3), color (3) = 9 floats per vertex.
    /// </summary>
    public const int FloatsPerVertex = 9;

    /// <summary>
    /// Build a 3D terrain mesh from Mandelbrot data.
    /// </summary>
    /// <param name="data">Normalized iteration values [0..1], size = width * height</param>
    /// <param name="width">Grid width</param>
    /// <param name="height">Grid height</param>
    /// <param name="palette">Color palette (RGB triplets)</param>
    /// <param name="heightScale">How much to exaggerate the height</param>
    /// <returns>Tuple of (vertices, indices)</returns>
    public static (float[] vertices, uint[] indices) BuildTerrainMesh(
        float[] data, int width, int height, float[] palette, float heightScale = 0.6f)
    {
        // Subsample if the resolution is extremely high to maintain FPS
        int step = 1;
        if (width > 2048 || height > 2048)
            step = Math.Max(width, height) / 2048 + 1;

        int meshW = (width + step - 1) / step;
        int meshH = (height + step - 1) / step;

        float[] vertices = new float[meshW * meshH * FloatsPerVertex];
        uint[] indices = new uint[(meshW - 1) * (meshH - 1) * 6];

        float halfW = meshW / 2f;
        float halfH = meshH / 2f;
        float scale = 2f / Math.Max(meshW, meshH); // Normalize to [-1, 1] range

        // Build vertices in parallel
        Parallel.For(0, meshH, gy =>
        {
            for (int gx = 0; gx < meshW; gx++)
            {
                int srcX = Math.Min(gx * step, width - 1);
                int srcY = Math.Min(gy * step, height - 1);
                float value = data[srcY * width + srcX];

                float px = (gx - halfW) * scale;
                float pz = (gy - halfH) * scale;
                float py = value * heightScale;

                // Sample color from palette
                var (r, g, b) = ColorPalette.SamplePalette(palette, value);

                int vIdx = (gy * meshW + gx) * FloatsPerVertex;
                vertices[vIdx + 0] = px;        // position x
                vertices[vIdx + 1] = py;        // position y (height)
                vertices[vIdx + 2] = pz;        // position z
                vertices[vIdx + 3] = 0f;        // normal x (placeholder)
                vertices[vIdx + 4] = 1f;        // normal y (placeholder)
                vertices[vIdx + 5] = 0f;        // normal z (placeholder)
                vertices[vIdx + 6] = r;          // color r
                vertices[vIdx + 7] = g;          // color g
                vertices[vIdx + 8] = b;          // color b
            }
        });

        // Compute normals using central differences in parallel
        Parallel.For(0, meshH, gy =>
        {
            for (int gx = 0; gx < meshW; gx++)
            {
                int idx = gy * meshW + gx;

                float hL = gx > 0 ? vertices[((gy * meshW) + gx - 1) * FloatsPerVertex + 1] : vertices[idx * FloatsPerVertex + 1];
                float hR = gx < meshW - 1 ? vertices[((gy * meshW) + gx + 1) * FloatsPerVertex + 1] : vertices[idx * FloatsPerVertex + 1];
                float hD = gy > 0 ? vertices[((gy - 1) * meshW + gx) * FloatsPerVertex + 1] : vertices[idx * FloatsPerVertex + 1];
                float hU = gy < meshH - 1 ? vertices[((gy + 1) * meshW + gx) * FloatsPerVertex + 1] : vertices[idx * FloatsPerVertex + 1];

                float nx = hL - hR;
                float nz = hD - hU;
                float ny = 2f * scale;

                // Normalize
                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 0.0001f)
                {
                    nx /= len;
                    ny /= len;
                    nz /= len;
                }
                else
                {
                    nx = 0; ny = 1; nz = 0;
                }

                vertices[idx * FloatsPerVertex + 3] = nx;
                vertices[idx * FloatsPerVertex + 4] = ny;
                vertices[idx * FloatsPerVertex + 5] = nz;
            }
        });

        // Build triangle indices
        int iIdx = 0;
        for (int gy = 0; gy < meshH - 1; gy++)
        {
            for (int gx = 0; gx < meshW - 1; gx++)
            {
                uint topLeft = (uint)(gy * meshW + gx);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)((gy + 1) * meshW + gx);
                uint bottomRight = bottomLeft + 1;

                // Triangle 1
                indices[iIdx++] = topLeft;
                indices[iIdx++] = bottomLeft;
                indices[iIdx++] = topRight;

                // Triangle 2
                indices[iIdx++] = topRight;
                indices[iIdx++] = bottomLeft;
                indices[iIdx++] = bottomRight;
            }
        }

        return (vertices, indices);
    }
}
