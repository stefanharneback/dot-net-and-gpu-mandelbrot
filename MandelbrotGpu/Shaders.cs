namespace MandelbrotGpu;

/// <summary>
/// GLSL shader source code for the 3D Mandelbrot terrain visualization.
/// </summary>
public static class Shaders
{
    public const string TerrainVertexShader = @"
#version 430 core

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
#version 430 core

in vec3 vNormal;
in vec3 vFragPos;
in float vValue;
in float vHeight;

layout(std430, binding = 1) buffer MinMaxBuffer {
    uint bMinVal;
    uint bMaxVal;
};

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

    float normalizedValue = clamp(value, 0.0, 1.0);
    float emphasizedValue = mix(normalizedValue, sqrt(normalizedValue), 0.68);
    float primaryBand = emphasizedValue * uPaletteCycles;
    float secondaryBand = (emphasizedValue * emphasizedValue * 0.75 + normalizedValue * 1.6) * (uPaletteCycles * 0.45);
    float paletteT = fract(mix(fract(primaryBand), fract(secondaryBand), 0.24));

    vec3 paletteColor = texture(uPalette, vec2(paletteT, 0.5)).rgb;
    float contour = smoothstep(0.12, 0.88, fract(primaryBand * 2.1 + secondaryBand));
    paletteColor *= 0.9 + contour * 0.16;

    return clamp(paletteColor, 0.0, 1.0);
}

void main()
{
    float minVal = uintBitsToFloat(bMinVal);
    float maxVal = uintBitsToFloat(bMaxVal);

    float range = max(0.000001, maxVal - minVal);
    float normValue = clamp((vValue - minVal) / range, 0.0, 1.0);

    vec3 baseColor = samplePalette(normValue);
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

    public const string MandelbrotComputeShaderF32 = @"
#version 430 core

layout(local_size_x = 16, local_size_y = 16) in;
layout(binding = 0, r32f) writeonly uniform image2D uOutputImage;

layout(std430, binding = 1) buffer MinMaxBuffer {
    uint bMinVal;
    uint bMaxVal;
};

uniform int uWidth;
uniform int uHeight;
uniform float uXMin;
uniform float uYMin;
uniform float uXMax;
uniform float uYMax;
uniform int uMaxIterations;

shared uint localMin;
shared uint localMax;

void main()
{
    // Initialize shared variables for the local workgroup
    if (gl_LocalInvocationIndex == 0)
    {
        localMin = 0xFFFFFFFFu;
        localMax = 0u;
    }
    barrier();

    ivec2 pixelCoords = ivec2(gl_GlobalInvocationID.xy);
    if (pixelCoords.x >= uWidth || pixelCoords.y >= uHeight)
        return;

    float px = float(pixelCoords.x) / float(uWidth - 1);
    float py = float(pixelCoords.y) / float(uHeight - 1);

    float x0 = uXMin + (uXMax - uXMin) * px;
    float y0 = uYMin + (uYMax - uYMin) * py;

    // --- Optimization 1: Cardioid and period-2 bulb rejection ---
    float q = (x0 - 0.25) * (x0 - 0.25) + y0 * y0;
    if (q * (q + (x0 - 0.25)) <= 0.25 * y0 * y0)
    {
        imageStore(uOutputImage, pixelCoords, vec4(0.0));
        return;
    }
    float bx = x0 + 1.0;
    if (bx * bx + y0 * y0 <= 0.0625)
    {
        imageStore(uOutputImage, pixelCoords, vec4(0.0));
        return;
    }

    float x = 0.0;
    float y = 0.0;
    float xOld = 0.0;
    float yOld = 0.0;
    int iteration = 0;
    int periodCheck = 0;

    // --- Optimization 2: Loop Unrolling & Periodicity Checking ---
    int unrolledLimit = uMaxIterations - 4;
    while (iteration < unrolledLimit)
    {
        float xx = x * x, yy = y * y;
        if (xx + yy > 4.0) break;
        y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

        xx = x * x; yy = y * y;
        if (xx + yy > 4.0) break;
        y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

        xx = x * x; yy = y * y;
        if (xx + yy > 4.0) break;
        y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

        xx = x * x; yy = y * y;
        if (xx + yy > 4.0) break;
        y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

        if (++periodCheck > 20)
        {
            if (abs(x - xOld) < 1e-7 && abs(y - yOld) < 1e-7)
            {
                iteration = uMaxIterations;
                break;
            }
            xOld = x; yOld = y;
            periodCheck = 0;
        }
    }

    while (x * x + y * y <= 4.0 && iteration < uMaxIterations)
    {
        float xTemp = x * x - y * y + x0;
        y = 2.0 * x * y + y0;
        x = xTemp;
        iteration++;
    }

    if (iteration < uMaxIterations)
    {
        float zn = x * x + y * y;
        float log2zn = log2(zn) * 0.5;
        float nu = log2(log2zn);
        float value = (float(iteration) + 1.0 - nu) / float(uMaxIterations);
        imageStore(uOutputImage, pixelCoords, vec4(value, 0.0, 0.0, 0.0));
        
        uint uVal = floatBitsToUint(value);
        atomicMin(localMin, uVal);
        atomicMax(localMax, uVal);
    }
    else
    {
        imageStore(uOutputImage, pixelCoords, vec4(0.0));
    }

    // Wait for all threads in the workgroup to finish tracking
    barrier();
    
    // Commit the local extrema to the global SSBO
    if (gl_LocalInvocationIndex == 0)
    {
        atomicMin(bMinVal, localMin);
        atomicMax(bMaxVal, localMax);
    }
}
";

    public const string MandelbrotComputeShaderF64 = @"
#version 430 core

layout(local_size_x = 16, local_size_y = 16) in;
layout(binding = 0, r32f) writeonly uniform image2D uOutputImage;

layout(std430, binding = 1) buffer MinMaxBuffer {
    uint bMinVal;
    uint bMaxVal;
};

uniform int uWidth;
uniform int uHeight;
uniform double uXMin;
uniform double uYMin;
uniform double uXMax;
uniform double uYMax;
uniform int uMaxIterations;

shared uint localMin;
shared uint localMax;

void main()
{
    // Initialize shared variables for the local workgroup
    if (gl_LocalInvocationIndex == 0)
    {
        localMin = 0xFFFFFFFFu;
        localMax = 0u;
    }
    barrier();

    ivec2 pixelCoords = ivec2(gl_GlobalInvocationID.xy);
    if (pixelCoords.x >= uWidth || pixelCoords.y >= uHeight)
        return;

    double px = double(pixelCoords.x) / double(uWidth - 1);
    double py = double(pixelCoords.y) / double(uHeight - 1);

    double x0 = uXMin + (uXMax - uXMin) * px;
    double y0 = uYMin + (uYMax - uYMin) * py;

    // --- Optimization 1: Cardioid and period-2 bulb rejection ---
    double q = (x0 - 0.25) * (x0 - 0.25) + y0 * y0;
    if (q * (q + (x0 - 0.25)) <= 0.25 * y0 * y0)
    {
        imageStore(uOutputImage, pixelCoords, vec4(0.0));
        return;
    }
    double bx = x0 + 1.0;
    if (bx * bx + y0 * y0 <= 0.0625)
    {
        imageStore(uOutputImage, pixelCoords, vec4(0.0));
        return;
    }

    double x = 0.0;
    double y = 0.0;
    double xOld = 0.0;
    double yOld = 0.0;
    int iteration = 0;
    int periodCheck = 0;

    // --- Optimization 2: Loop Unrolling & Periodicity Checking ---
    int unrolledLimit = uMaxIterations - 4;
    while (iteration < unrolledLimit)
    {
        double xx = x * x, yy = y * y;
        if (xx + yy > 4.0) break;
        y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

        xx = x * x; yy = y * y;
        if (xx + yy > 4.0) break;
        y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

        xx = x * x; yy = y * y;
        if (xx + yy > 4.0) break;
        y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

        xx = x * x; yy = y * y;
        if (xx + yy > 4.0) break;
        y = 2.0 * x * y + y0; x = xx - yy + x0; iteration++;

        if (++periodCheck > 20)
        {
            if (abs(x - xOld) < 1e-13 && abs(y - yOld) < 1e-13)
            {
                iteration = uMaxIterations;
                break;
            }
            xOld = x; yOld = y;
            periodCheck = 0;
        }
    }

    while (x * x + y * y <= 4.0 && iteration < uMaxIterations)
    {
        double xTemp = x * x - y * y + x0;
        y = 2.0 * x * y + y0;
        x = xTemp;
        iteration++;
    }

    if (iteration < uMaxIterations)
    {
        double zn = x * x + y * y;
        // fallback to float for log operations as double logic isn't strictly necessary for shading calculation
        double log2zn = log2(float(zn)) * 0.5;
        double nu = log2(float(log2zn));
        float value = float((double(iteration) + 1.0 - nu) / double(uMaxIterations));
        imageStore(uOutputImage, pixelCoords, vec4(value, 0.0, 0.0, 0.0));
        
        uint uVal = floatBitsToUint(value);
        atomicMin(localMin, uVal);
        atomicMax(localMax, uVal);
    }
    else
    {
        imageStore(uOutputImage, pixelCoords, vec4(0.0));
    }

    // Wait for all threads in the workgroup to finish tracking
    barrier();
    
    // Commit the local extrema to the global SSBO
    if (gl_LocalInvocationIndex == 0)
    {
        atomicMin(bMinVal, localMin);
        atomicMax(bMaxVal, localMax);
    }
}
";
}
