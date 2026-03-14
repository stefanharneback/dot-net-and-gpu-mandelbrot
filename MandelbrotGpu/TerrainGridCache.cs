using System.Numerics;

namespace MandelbrotGpu;

public readonly record struct TerrainGridMesh(float[] Vertices, uint[] Indices, Vector2 GridStep);

public static class TerrainGridCache
{
    public const int FloatsPerVertex = 4;

    public static TerrainGridMesh Build(int resolution)
    {
        if (resolution < 2)
            throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be at least 2.");

        int width = resolution;
        int height = resolution;

        float[] vertices = new float[width * height * FloatsPerVertex];
        uint[] indices = new uint[(width - 1) * (height - 1) * 6];

        float xStep = 2f / (width - 1);
        float zStep = 2f / (height - 1);

        for (int gy = 0; gy < height; gy++)
        {
            float z = -1f + gy * zStep;
            float v = gy / (float)(height - 1);

            for (int gx = 0; gx < width; gx++)
            {
                float x = -1f + gx * xStep;
                float u = gx / (float)(width - 1);

                int vertexIndex = (gy * width + gx) * FloatsPerVertex;
                vertices[vertexIndex + 0] = x;
                vertices[vertexIndex + 1] = z;
                vertices[vertexIndex + 2] = u;
                vertices[vertexIndex + 3] = v;
            }
        }

        int indexOffset = 0;
        for (int gy = 0; gy < height - 1; gy++)
        {
            for (int gx = 0; gx < width - 1; gx++)
            {
                uint topLeft = (uint)(gy * width + gx);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)((gy + 1) * width + gx);
                uint bottomRight = bottomLeft + 1;

                indices[indexOffset++] = topLeft;
                indices[indexOffset++] = bottomLeft;
                indices[indexOffset++] = topRight;

                indices[indexOffset++] = topRight;
                indices[indexOffset++] = bottomLeft;
                indices[indexOffset++] = bottomRight;
            }
        }

        return new TerrainGridMesh(vertices, indices, new Vector2(xStep, zStep));
    }
}
