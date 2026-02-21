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

        // Convert angular deflection to UV offset
        vec2 direction = -normalize(delta);
        totalDeflection += direction * alpha;

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
    sceneColor.rgb *= (1.0 - totalDarkening * 0.95);

    // Event horizon glow (accretion emission approximation)
    if (totalDarkening > 0.5)
    {
        float glowFactor = (totalDarkening - 0.5) * 2.0 * uEventHorizonGlow;
        vec3 glowColor = vec3(1.0, 0.6, 0.2) * glowFactor;
        sceneColor.rgb += glowColor;
    }

    fragColor = sceneColor;
}
