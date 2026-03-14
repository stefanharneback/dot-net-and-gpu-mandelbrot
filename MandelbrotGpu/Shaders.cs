namespace MandelbrotGpu;

/// <summary>
/// GLSL shader source code for the 3D Mandelbrot terrain visualization.
/// </summary>
public static class Shaders
{
    public const string VertexShader = @"
#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec3 aColor;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uLightDir;

out vec3 vColor;
out vec3 vNormal;
out vec3 vFragPos;
out float vHeight;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    gl_Position = uProjection * uView * worldPos;

    vFragPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vColor = aColor;
    vHeight = aPosition.y;
}
";

    public const string FragmentShader = @"
#version 330 core

in vec3 vColor;
in vec3 vNormal;
in vec3 vFragPos;
in float vHeight;

uniform vec3 uLightDir;
uniform vec3 uViewPos;

out vec4 FragColor;

void main()
{
    // Ambient
    float ambientStrength = 0.15;
    vec3 ambient = ambientStrength * vColor;

    // Diffuse lighting
    vec3 norm = normalize(vNormal);
    vec3 lightDir = normalize(uLightDir);
    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * vColor;

    // Specular highlighting
    float specularStrength = 0.4;
    vec3 viewDir = normalize(uViewPos - vFragPos);
    vec3 halfDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(norm, halfDir), 0.0), 64.0);
    vec3 specular = specularStrength * spec * vec3(1.0, 0.95, 0.9);

    // Rim lighting for edges
    float rim = 1.0 - max(dot(viewDir, norm), 0.0);
    rim = pow(rim, 3.0) * 0.15;
    vec3 rimColor = rim * vColor * 1.5;

    // Height-based fog for depth
    float fogFactor = exp(-vHeight * 0.5);
    vec3 fogColor = vec3(0.02, 0.02, 0.05);

    vec3 result = ambient + diffuse + specular + rimColor;
    result = mix(result, fogColor, fogFactor * 0.1);

    // Tone mapping for HDR feel
    result = result / (result + vec3(1.0));

    // Gamma correction
    result = pow(result, vec3(1.0/2.2));

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
