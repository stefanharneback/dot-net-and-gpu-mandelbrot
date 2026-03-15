using System.Diagnostics;
using System.Numerics;
using Silk.NET.OpenGL;

namespace MandelbrotGpu;

public sealed unsafe class HeightFieldRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _shaderProgram;

    private readonly int _modelLocation;
    private readonly int _viewLocation;
    private readonly int _projectionLocation;
    private readonly int _lightDirLocation;
    private readonly int _viewPosLocation;
    private readonly int _heightScaleLocation;
    private readonly int _texelSizeLocation;
    private readonly int _gridStepLocation;
    private readonly int _paletteCyclesLocation;
    private readonly int _shadingModeLocation;

    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly uint _heightTexture;
    private readonly uint _paletteTexture;

    private int _heightTextureWidth;
    private int _heightTextureHeight;
    private Vector2 _gridStep;
    private int _indexCount;

    public HeightFieldRenderer(GL gl, uint shaderProgram)
    {
        _gl = gl;
        _shaderProgram = shaderProgram;

        _modelLocation = _gl.GetUniformLocation(_shaderProgram, "uModel");
        _viewLocation = _gl.GetUniformLocation(_shaderProgram, "uView");
        _projectionLocation = _gl.GetUniformLocation(_shaderProgram, "uProjection");
        _lightDirLocation = _gl.GetUniformLocation(_shaderProgram, "uLightDir");
        _viewPosLocation = _gl.GetUniformLocation(_shaderProgram, "uViewPos");
        _heightScaleLocation = _gl.GetUniformLocation(_shaderProgram, "uHeightScale");
        _texelSizeLocation = _gl.GetUniformLocation(_shaderProgram, "uTexelSize");
        _gridStepLocation = _gl.GetUniformLocation(_shaderProgram, "uGridStep");
        _paletteCyclesLocation = _gl.GetUniformLocation(_shaderProgram, "uPaletteCycles");
        _shadingModeLocation = _gl.GetUniformLocation(_shaderProgram, "uShadingMode");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();
        _heightTexture = _gl.GenTexture();
        _paletteTexture = _gl.GenTexture();

        ConfigureHeightTexture();
        ConfigurePaletteTexture();

        _gl.UseProgram(_shaderProgram);
        int heightFieldLocation = _gl.GetUniformLocation(_shaderProgram, "uHeightField");
        int paletteLocation = _gl.GetUniformLocation(_shaderProgram, "uPalette");
        if (heightFieldLocation >= 0)
            _gl.Uniform1(heightFieldLocation, 0);
        if (paletteLocation >= 0)
            _gl.Uniform1(paletteLocation, 1);
    }

    public int RenderMeshResolution { get; private set; }

    public double LastGridBuildMs { get; private set; }

    public void SetRenderMeshResolution(int resolution)
    {
        if (resolution == RenderMeshResolution)
        {
            LastGridBuildMs = 0;
            return;
        }

        long buildStart = Stopwatch.GetTimestamp();
        TerrainGridMesh mesh = TerrainGridCache.Build(resolution);
        LastGridBuildMs = Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;

        RenderMeshResolution = resolution;
        _gridStep = mesh.GridStep;
        _indexCount = mesh.Indices.Length;

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* vertices = mesh.Vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(mesh.Vertices.Length * sizeof(float)),
                vertices,
                BufferUsageARB.StaticDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* indices = mesh.Indices)
        {
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(mesh.Indices.Length * sizeof(uint)),
                indices,
                BufferUsageARB.StaticDraw);
        }

        int stride = TerrainGridCache.FloatsPerVertex * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)(2 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    public void UploadPalette(float[] palette)
    {
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _paletteTexture);

        fixed (float* paletteData = palette)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                (int)InternalFormat.Rgb32f,
                (uint)(palette.Length / 3),
                1,
                0,
                PixelFormat.Rgb,
                PixelType.Float,
                paletteData);
        }
    }

    public double UploadHeightField(HeightFieldFrame frame)
    {
        long uploadStart = Stopwatch.GetTimestamp();

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _heightTexture);

        fixed (float* data = frame.Data)
        {
            if (frame.Width != _heightTextureWidth || frame.Height != _heightTextureHeight)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    (int)InternalFormat.R32f,
                    (uint)frame.Width,
                    (uint)frame.Height,
                    0,
                    PixelFormat.Red,
                    PixelType.Float,
                    data);

                _heightTextureWidth = frame.Width;
                _heightTextureHeight = frame.Height;
            }
            else
            {
                _gl.TexSubImage2D(
                    TextureTarget.Texture2D,
                    0,
                    0,
                    0,
                    (uint)frame.Width,
                    (uint)frame.Height,
                    PixelFormat.Red,
                    PixelType.Float,
                    data);
            }
        }

        frame.TextureHandle = _heightTexture;
        frame.TextureUploadMs = Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
        return frame.TextureUploadMs;
    }

    public void Render(
        Matrix4x4 model,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 lightDir,
        Vector3 viewPosition,
        HeightFieldFrame frame,
        float heightScale,
        float paletteCycles,
        ShadingMode shadingMode,
        bool wireframeMode)
    {
        _gl.UseProgram(_shaderProgram);
        SetUniformMatrix4(_modelLocation, model);
        SetUniformMatrix4(_viewLocation, view);
        SetUniformMatrix4(_projectionLocation, projection);

        if (_lightDirLocation >= 0)
            _gl.Uniform3(_lightDirLocation, lightDir.X, lightDir.Y, lightDir.Z);
        if (_viewPosLocation >= 0)
            _gl.Uniform3(_viewPosLocation, viewPosition.X, viewPosition.Y, viewPosition.Z);
        if (_heightScaleLocation >= 0)
            _gl.Uniform1(_heightScaleLocation, heightScale);
        if (_texelSizeLocation >= 0)
            _gl.Uniform2(_texelSizeLocation, 1f / frame.Width, 1f / frame.Height);
        if (_gridStepLocation >= 0)
            _gl.Uniform2(_gridStepLocation, _gridStep.X, _gridStep.Y);
        if (_paletteCyclesLocation >= 0)
            _gl.Uniform1(_paletteCyclesLocation, paletteCycles);
        if (_shadingModeLocation >= 0)
            _gl.Uniform1(_shadingModeLocation, shadingMode == ShadingMode.Full ? 1 : 0);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _heightTexture);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _paletteTexture);

        if (wireframeMode)
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null);

        if (wireframeMode)
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_heightTexture);
        _gl.DeleteTexture(_paletteTexture);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }

    private void ConfigureHeightTexture()
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _heightTexture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    private void ConfigurePaletteTexture()
    {
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _paletteTexture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    private void SetUniformMatrix4(int location, Matrix4x4 matrix)
    {
        if (location < 0)
            return;

        _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
    }
}
