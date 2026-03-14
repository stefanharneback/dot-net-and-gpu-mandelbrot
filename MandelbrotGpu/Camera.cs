using System.Numerics;

namespace MandelbrotGpu;

/// <summary>
/// Orbital camera that allows rotation around a target, zooming, and panning.
/// Uses spherical coordinates for intuitive mouse-drag orbiting.
/// </summary>
public class Camera
{
    // Spherical coordinates
    private float _azimuth = -90f;    // Horizontal angle (degrees)
    private float _elevation = 45f;   // Vertical angle (degrees)
    private float _distance = 3.5f;   // Distance from target

    // Target point the camera looks at
    public Vector3 Target { get; set; } = Vector3.Zero;

    // Camera constraints
    private const float MinElevation = 5f;
    private const float MaxElevation = 89f;
    private const float MinDistance = 0.3f;
    private const float MaxDistance = 20f;

    // Sensitivity
    public float RotationSensitivity { get; set; } = 0.3f;
    public float ZoomSensitivity { get; set; } = 0.15f;
    public float PanSensitivity { get; set; } = 0.005f;

    public Vector3 Position
    {
        get
        {
            float azRad = MathF.PI / 180f * _azimuth;
            float elRad = MathF.PI / 180f * _elevation;

            float x = _distance * MathF.Cos(elRad) * MathF.Cos(azRad);
            float z = _distance * MathF.Cos(elRad) * MathF.Sin(azRad);
            float y = _distance * MathF.Sin(elRad);

            return Target + new Vector3(x, y, z);
        }
    }

    public Vector3 Up => Vector3.UnitY;

    /// <summary>
    /// Rotate the camera around the target.
    /// </summary>
    public void Orbit(float deltaX, float deltaY)
    {
        _azimuth += deltaX * RotationSensitivity;
        _elevation += deltaY * RotationSensitivity;
        _elevation = Math.Clamp(_elevation, MinElevation, MaxElevation);
    }

    /// <summary>
    /// Zoom in/out.
    /// </summary>
    public void Zoom(float delta)
    {
        _distance *= 1f - delta * ZoomSensitivity;
        _distance = Math.Clamp(_distance, MinDistance, MaxDistance);
    }

    /// <summary>
    /// Pan the camera target.
    /// </summary>
    public void Pan(float deltaX, float deltaY)
    {
        // Calculate right and up vectors relative to view
        Vector3 forward = Vector3.Normalize(Target - Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Up));
        Vector3 up = Vector3.Cross(right, forward);

        Target += right * (-deltaX * PanSensitivity * _distance) + up * (deltaY * PanSensitivity * _distance);
    }

    /// <summary>
    /// Build a view matrix (look-at).
    /// </summary>
    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, Up);
    }

    /// <summary>
    /// Build a perspective projection matrix.
    /// </summary>
    public static Matrix4x4 GetProjectionMatrix(float aspectRatio, float fovDegrees = 60f, float near = 0.01f, float far = 100f)
    {
        float fovRad = MathF.PI / 180f * fovDegrees;
        return Matrix4x4.CreatePerspectiveFieldOfView(fovRad, aspectRatio, near, far);
    }
}
