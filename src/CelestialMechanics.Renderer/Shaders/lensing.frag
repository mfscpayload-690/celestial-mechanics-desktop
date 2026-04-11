#version 330 core

// Gravitational lensing post-processing fragment shader.
//
// Implements approximate Schwarzschild lensing for up to 8 black holes.
// Deflection angle: α = 4GM / (c²b) where b = impact parameter.
// Near the event horizon, UV distortion is clamped to prevent singularities.
//
// The scene is first rendered to an off-screen FBO, then this shader
// distorts the UV coordinates based on nearby massive objects.

in vec2 vTexCoord;
out vec4 fragColor;

uniform sampler2D uSceneTexture;
uniform vec2 uResolution;

// Black hole data: up to 8 compact objects
// Each entry: (screenX, screenY, schwarzschildRadius_screen, mass_scaled)
// schwarzschildRadius_screen is Rs projected to screen space
// mass_scaled = 4GM / c² in screen-space units
const int MAX_BLACK_HOLES = 8;
uniform int  uBlackHoleCount;
uniform vec4 uBlackHoles[MAX_BLACK_HOLES]; // xy=screen pos, z=Rs_screen, w=lensStrength

// Visual tunables
uniform float uLensIntensity;    // Overall strength multiplier (default 1.0)
uniform float uEventHorizonGlow; // Glow intensity at event horizon (default 0.5)
uniform int uQualityTier;        // 0=low, 1=medium, 2=high
uniform int uVisualPreset;       // 0=cinematic orange, 1=EHT230, 2=EHT345
uniform float uDopplerBoost;
uniform float uOpticalDepth;
uniform int uDebugMode;          // 0=none, 1=horizon, 2=ring, 3=warp, 4=optical depth

void main()
{
    vec2 uv = vTexCoord;
    vec2 pixelPos = uv * uResolution;

    vec2 totalDeflection = vec2(0.0);
    float totalDarkening = 0.0;

    // Unrolled loop for performance — no dynamic branching
    for (int i = 0; i < MAX_BLACK_HOLES; i++)
    {
        // Early termination without dynamic branch
        if (i >= uBlackHoleCount) break;

        vec2  bhPos      = uBlackHoles[i].xy;
        float rsScreen   = uBlackHoles[i].z;
        float lensStr    = uBlackHoles[i].w;

        vec2  delta      = pixelPos - bhPos;
        float dist       = length(delta);

        // Skip if too far away (no visible effect beyond 50× Rs)
        if (dist > rsScreen * 50.0) continue;

        // Impact parameter (clamped above event horizon)
        float b = max(dist, rsScreen * 1.5);

        // Deflection angle: α = lensStrength / b
        // Direction: toward the black hole
        float alpha = lensStr * uLensIntensity / b;

        if (uQualityTier >= 1)
            alpha *= 1.0 + 0.12 * sin(dist * 0.015);
        if (uQualityTier >= 2)
            alpha *= 1.0 + 0.10 * cos(dist * 0.025 + float(i));

        // Convert angular deflection to UV offset
        vec2 direction = -normalize(delta);
        float asymmetry = 1.0 + direction.x * 0.35 * uDopplerBoost;
        totalDeflection += direction * alpha * asymmetry;

        // Event horizon darkening: smooth falloff at Rs
        float horizonFactor = smoothstep(rsScreen * 0.8, rsScreen * 2.0, dist);
        totalDarkening += (1.0 - horizonFactor);

        // Einstein ring: brighten at the Einstein radius
        // R_E ≈ sqrt(4GM·D_LS / (c² · D_L · D_S)) ≈ sqrt(lensStr)
        float einsteinRadius = sqrt(lensStr * rsScreen);
        float ringDist = abs(dist - einsteinRadius);
        float ringBright = exp(-ringDist * ringDist / (rsScreen * rsScreen * 4.0));
        totalDarkening -= ringBright * 0.3; // Brighten near Einstein ring
    }

    // Apply UV deflection
    vec2 deflectedUV = uv + totalDeflection / uResolution;

    // Clamp to valid texture coordinates
    deflectedUV = clamp(deflectedUV, vec2(0.001), vec2(0.999));

    // Sample scene with deflected coordinates
    vec4 sceneColor = texture(uSceneTexture, deflectedUV);

    // Apply darkening near event horizons
    totalDarkening = clamp(totalDarkening, 0.0, 1.0);
    float tau = clamp(uOpticalDepth * (0.35 + totalDarkening), 0.0, 4.0);
    float transmittance = exp(-tau);
    sceneColor.rgb = sceneColor.rgb * transmittance + sceneColor.rgb * (1.0 - transmittance) * 0.45;
    sceneColor.rgb *= (1.0 - totalDarkening * 0.95);

    // Event horizon glow (accretion emission approximation)
    if (totalDarkening > 0.5)
    {
        float glowFactor = (totalDarkening - 0.5) * 2.0 * uEventHorizonGlow;
        vec3 glowColor = (uVisualPreset == 0) ? vec3(1.0, 0.55, 0.15) : ((uVisualPreset == 1) ? vec3(0.95, 0.72, 0.50) : vec3(0.92, 0.98, 1.0));
        glowColor *= glowFactor;
        sceneColor.rgb += glowColor;
    }

    if (uDebugMode == 1)
    {
        fragColor = vec4(vec3(totalDarkening), 1.0);
        return;
    }
    if (uDebugMode == 2)
    {
        float ringMask = clamp(length(totalDeflection) * 350.0, 0.0, 1.0);
        fragColor = vec4(vec3(ringMask), 1.0);
        return;
    }
    if (uDebugMode == 3)
    {
        vec2 warp = clamp(totalDeflection * 120.0, vec2(-1.0), vec2(1.0));
        fragColor = vec4(warp * 0.5 + 0.5, 0.0, 1.0);
        return;
    }
    if (uDebugMode == 4)
    {
        fragColor = vec4(vec3(clamp(tau / 4.0, 0.0, 1.0)), 1.0);
        return;
    }

    fragColor = sceneColor;
}
