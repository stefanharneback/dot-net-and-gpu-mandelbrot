namespace MandelbrotGpu;

/// <summary>
/// GLSL shader source code for the 3D Mandelbrot terrain visualization.
/// </summary>
public static class Shaders
{
    public const string TerrainVertexShader = @"
#version 330 core

layout(location = 0) in vec2 aBaseXZ;
layout(location = 1) in vec2 aUv;

uniform sampler2D uHeightField;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform float uHeightScale;
uniform vec2 uTexelSize;
uniform vec2 uGridStep;

out vec3 vNormal;
out vec3 vFragPos;
out float vValue;
out float vHeight;

float sampleHeight(vec2 uv)
{
    vec2 clampedUv = clamp(uv, uTexelSize * 0.5, vec2(1.0) - (uTexelSize * 0.5));
    return texture(uHeightField, clampedUv).r;
}

void main()
{
    float value = sampleHeight(aUv);
    float height = value * uHeightScale;

    float leftH = sampleHeight(aUv - vec2(uTexelSize.x, 0.0)) * uHeightScale;
    float rightH = sampleHeight(aUv + vec2(uTexelSize.x, 0.0)) * uHeightScale;
    float downH = sampleHeight(aUv - vec2(0.0, uTexelSize.y)) * uHeightScale;
    float upH = sampleHeight(aUv + vec2(0.0, uTexelSize.y)) * uHeightScale;

    vec3 tangentX = vec3(uGridStep.x * 2.0, rightH - leftH, 0.0);
    vec3 tangentZ = vec3(0.0, upH - downH, uGridStep.y * 2.0);
    vec3 localNormal = normalize(cross(tangentZ, tangentX));

    vec4 worldPos = uModel * vec4(aBaseXZ.x, height, aBaseXZ.y, 1.0);
    gl_Position = uProjection * uView * worldPos;

    vFragPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * localNormal;
    vValue = value;
    vHeight = height;
}
";

    public const string TerrainFragmentShader = @"
#version 330 core

in vec3 vNormal;
in vec3 vFragPos;
in float vValue;
in float vHeight;

uniform sampler2D uPalette;
uniform vec3 uLightDir;
uniform vec3 uViewPos;
uniform float uPaletteCycles;
uniform int uShadingMode;

out vec4 FragColor;

vec3 samplePalette(float value)
{
    if (value <= 0.0)
        return vec3(0.0);

    float scaledT = fract(value * uPaletteCycles);
    return texture(uPalette, vec2(scaledT, 0.5)).rgb;
}

void main()
{
    vec3 baseColor = samplePalette(vValue);
    vec3 norm = normalize(vNormal);
    vec3 lightDir = normalize(uLightDir);
    vec3 viewDir = normalize(uViewPos - vFragPos);

    vec3 ambient = 0.18 * baseColor;
    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * baseColor * 0.85;

    vec3 result = ambient + diffuse;

    if (uShadingMode == 1)
    {
        vec3 halfDir = normalize(lightDir + viewDir);
        float spec = pow(max(dot(norm, halfDir), 0.0), 64.0);
        vec3 specular = 0.4 * spec * vec3(1.0, 0.95, 0.9);

        float rim = 1.0 - max(dot(viewDir, norm), 0.0);
        rim = pow(rim, 3.0) * 0.15;
        vec3 rimColor = rim * baseColor * 1.5;

        float fogFactor = exp(-vHeight * 0.5);
        vec3 fogColor = vec3(0.02, 0.02, 0.05);

        result += specular + rimColor;
        result = mix(result, fogColor, fogFactor * 0.1);
    }

    result = result / (result + vec3(1.0));
    result = pow(result, vec3(1.0 / 2.2));

    FragColor = vec4(result, 1.0);
}
";

    /// <summary>
    /// Simple shader for the grid/axes overlay.
    /// </summary>
    public const string GridVertexShader = @"
#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aColor;

uniform mat4 uViewProjection;

out vec3 vColor;

void main()
{
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
    vColor = aColor;
}
";

    public const string GridFragmentShader = @"
#version 330 core

in vec3 vColor;
out vec4 FragColor;

void main()
{
    FragColor = vec4(vColor, 0.4);
}
";
}
