#version 330 core

in vec2 vUv;
out vec4 FragColor;

uniform float uTime;
uniform vec2 uResolution;
uniform vec3 uCameraPos;
uniform float uExposure;
uniform float uStarEmissionMultiplier;
uniform float uNebulaEmissionMultiplier;
uniform float uFogDensity;
uniform vec3 uFogColor;

float hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float starField(vec2 uv, float scale, float threshold)
{
    vec2 g = uv * scale;
    vec2 cell = floor(g);
    vec2 f = fract(g);

    float h = hash21(cell);
    if (h < threshold)
        return 0.0;

    vec2 starPos = vec2(hash21(cell + 1.7), hash21(cell + 8.3));
    float d = length(f - starPos);
    float core = smoothstep(0.06, 0.0, d);
    float twinkle = 0.8 + 0.2 * sin(uTime * (0.8 + h * 2.5) + h * 60.0);
    return core * twinkle;
}

float hash31(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float valueNoise3(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = hash31(i + vec3(0,0,0));
    float n100 = hash31(i + vec3(1,0,0));
    float n010 = hash31(i + vec3(0,1,0));
    float n110 = hash31(i + vec3(1,1,0));
    float n001 = hash31(i + vec3(0,0,1));
    float n101 = hash31(i + vec3(1,0,1));
    float n011 = hash31(i + vec3(0,1,1));
    float n111 = hash31(i + vec3(1,1,1));

    float n00 = mix(n000, n100, f.x);
    float n10 = mix(n010, n110, f.x);
    float n01 = mix(n001, n101, f.x);
    float n11 = mix(n011, n111, f.x);
    float n0 = mix(n00, n10, f.y);
    float n1 = mix(n01, n11, f.y);
    return mix(n0, n1, f.z);
}

float fbm(vec3 p)
{
    float a = 0.5;
    float v = 0.0;
    for (int i = 0; i < 5; i++)
    {
        v += a * valueNoise3(p);
        p = p * 2.03 + vec3(2.1, 4.7, 1.3);
        a *= 0.5;
    }
    return v;
}

void main()
{
    vec2 uv = vUv;
    vec2 camOffsetNear = uCameraPos.xz * 0.0012;
    vec2 camOffsetFar = uCameraPos.xz * 0.00035;
    vec2 camOffsetNebula = uCameraPos.xz * 0.00018;
    vec2 p = (uv - 0.5) * vec2(uResolution.x / max(uResolution.y, 1.0), 1.0);

    vec3 top = vec3(0.02, 0.03, 0.07);
    vec3 bot = vec3(0.005, 0.008, 0.02);
    float grad = smoothstep(-0.8, 1.0, p.y);
    vec3 col = mix(bot, top, grad);

    float stars1 = starField(uv + camOffsetNear, 220.0, 0.996);
    float stars2 = starField(uv + vec2(0.13, 0.29) + camOffsetFar, 380.0, 0.9982);
    float stars3 = starField(uv + vec2(0.47, 0.11) + camOffsetNear * 0.6, 120.0, 0.9925);

    vec3 starColor = (vec3(0.95, 0.98, 1.0) * stars1
                   + vec3(0.75, 0.84, 1.0) * stars2
                   + vec3(1.0, 0.9, 0.78) * stars3) * max(uStarEmissionMultiplier, 0.0);

    vec3 noiseCoord = vec3((uv + camOffsetNebula) * 3.0, uTime * 0.02);
    float gasLayer = fbm(noiseCoord);
    float dustLayer = fbm(noiseCoord * 1.7 + vec3(8.0, 3.0, 1.0));
    float plasmaLayer = fbm(noiseCoord * 2.4 + vec3(-5.0, 2.0, 3.0));

    float gasAlpha = smoothstep(0.38, 0.92, gasLayer);
    float dustAlpha = smoothstep(0.52, 0.95, dustLayer) * 0.7;
    float plasmaAlpha = smoothstep(0.61, 0.98, plasmaLayer) * 0.85;

    vec3 nebulaColor = vec3(0.25, 0.14, 0.38) * gasAlpha
                     + vec3(0.08, 0.06, 0.10) * dustAlpha
                     + vec3(0.36, 0.22, 0.44) * plasmaAlpha;
    nebulaColor *= max(uNebulaEmissionMultiplier, 0.0);

    float lightInfluence = clamp((stars1 + stars2 + stars3) * 3.0, 0.0, 1.0);
    nebulaColor += nebulaColor * lightInfluence * 0.45;

    col += starColor;
    col += nebulaColor;

    float fog = clamp(uFogDensity * 8.0, 0.0, 0.95);
    col = mix(col, uFogColor, fog);

    vec3 hdr = col * max(uExposure, 0.01);
    col = vec3(1.0) - exp(-hdr);

    FragColor = vec4(col, 1.0);
}
