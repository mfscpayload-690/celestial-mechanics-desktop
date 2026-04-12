#version 330 core

in vec2 vUv;
out vec4 FragColor;

uniform float uTime;
uniform vec2 uResolution;
uniform vec3 uCameraPos;

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

    vec3 starColor = vec3(0.95, 0.98, 1.0) * stars1
                   + vec3(0.75, 0.84, 1.0) * stars2
                   + vec3(1.0, 0.9, 0.78) * stars3;

    float nebula = 0.0;
    vec2 q = (uv + camOffsetNebula) * 3.0;
    nebula += sin((q.x + uTime * 0.01) * 2.5) * 0.06;
    nebula += sin((q.y - uTime * 0.008) * 2.1) * 0.05;
    nebula = max(nebula, 0.0);

    col += starColor;
    col += vec3(0.15, 0.08, 0.22) * nebula;

    FragColor = vec4(col, 1.0);
}
