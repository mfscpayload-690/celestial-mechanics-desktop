#version 330 core

// Accretion disk particle fragment shader.
// Adds preset-driven spectral behavior, optical depth, and debug visualizations.

in float vTemperature;
in float vLifeFraction;

out vec4 fragColor;

uniform int uQualityTier;      // 0=low, 1=medium, 2=high
uniform int uVisualPreset;     // 0=cinematic orange, 1=EHT230, 2=EHT345
uniform float uDopplerBoost;
uniform float uOpticalDepth;
uniform float uTemperatureScale;
uniform float uBloomScale;
uniform int uDebugMode;        // 0=none, 1=horizon, 2=ring, 3=warp, 4=optical depth

// Approximate blackbody colour from temperature (K)
// 1000K → deep red, 5000K → yellow-white, 10000K → blue-white, 50000K+ → blue
vec3 blackbodyColor(float tempK)
{
    float t = clamp(tempK, 1000.0, 50000.0);

    // Piece-wise approximation of blackbody radiation curve
    vec3 color;

    if (t < 3500.0)
    {
        // Red to orange
        float f = (t - 1000.0) / 2500.0;
        color = mix(vec3(1.0, 0.1, 0.0), vec3(1.0, 0.55, 0.1), f);
    }
    else if (t < 6500.0)
    {
        // Orange to white
        float f = (t - 3500.0) / 3000.0;
        color = mix(vec3(1.0, 0.55, 0.1), vec3(1.0, 0.95, 0.9), f);
    }
    else if (t < 15000.0)
    {
        // White to pale blue
        float f = (t - 6500.0) / 8500.0;
        color = mix(vec3(1.0, 0.95, 0.9), vec3(0.7, 0.8, 1.0), f);
    }
    else
    {
        // Pale blue to deep blue
        float f = clamp((t - 15000.0) / 35000.0, 0.0, 1.0);
        color = mix(vec3(0.7, 0.8, 1.0), vec3(0.4, 0.5, 1.0), f);
    }

    return color;
}

void main()
{
    // Point sprite: circular falloff
    vec2 coord = gl_PointCoord * 2.0 - 1.0;
    float dist = dot(coord, coord);
    if (dist > 1.0) discard;

    float radial = sqrt(dist);
    float edgeMask = 1.0 - smoothstep(0.45, 1.0, dist);
    float alpha = edgeMask;

    // Fade out near end of life
    alpha *= 1.0 - vLifeFraction * vLifeFraction;

    float baseTemp = vTemperature * max(uTemperatureScale, 0.25);
    float presetTempBias = (uVisualPreset == 0) ? 0.92 : ((uVisualPreset == 1) ? 0.78 : 1.08);
    float tempK = baseTemp * presetTempBias;

    // Disk-side asymmetry: left/right intensity skew approximates relativistic beaming.
    float azimuth = clamp(coord.x * 0.8, -1.0, 1.0);
    float gFactor = clamp(1.0 + azimuth * 0.45 * uDopplerBoost, 0.25, 2.8);
    float beaming = pow(gFactor, 3.0);

    // Thickness differs by preset: EHT230 thicker than EHT345.
    float presetDepth = (uVisualPreset == 0) ? 0.85 : ((uVisualPreset == 1) ? 1.25 : 0.62);
    float tau = max(0.0, uOpticalDepth) * presetDepth * (0.35 + 0.65 * (1.0 - radial)) * (1.0 - 0.5 * vLifeFraction);
    float transmittance = exp(-tau);

    vec3 source = blackbodyColor(tempK) * beaming;
    vec3 color = source * (1.0 - transmittance) + source * transmittance * 0.35;

    if (uQualityTier >= 1)
    {
        // Subtle ring accent to avoid flat sprites.
        float ring = exp(-pow((radial - 0.72) / 0.17, 2.0));
        color += source * ring * 0.18;
    }

    if (uQualityTier >= 2)
    {
        // Slightly hotter compact center for high tier.
        float core = exp(-pow(radial / 0.35, 2.0));
        color += blackbodyColor(tempK * 1.15) * core * 0.14;
    }

    color *= 1.0 + uBloomScale * 0.25;

    if (uDebugMode == 1)
    {
        float horizonMask = smoothstep(0.15, 0.95, radial);
        fragColor = vec4(vec3(horizonMask), 1.0);
        return;
    }
    if (uDebugMode == 2)
    {
        float ringMask = exp(-pow((radial - 0.72) / 0.14, 2.0));
        fragColor = vec4(vec3(ringMask), 1.0);
        return;
    }
    if (uDebugMode == 3)
    {
        float warpMask = 0.5 + 0.5 * azimuth;
        fragColor = vec4(warpMask, 1.0 - warpMask, 0.0, 1.0);
        return;
    }
    if (uDebugMode == 4)
    {
        float depthMask = clamp(tau / 4.0, 0.0, 1.0);
        fragColor = vec4(vec3(depthMask), 1.0);
        return;
    }

    fragColor = vec4(color, alpha * 0.8);
}
