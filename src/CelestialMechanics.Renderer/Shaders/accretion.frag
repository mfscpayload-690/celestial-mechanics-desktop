#version 330 core

// Accretion disk particle fragment shader.
// Maps temperature to an approximate blackbody colour ramp.

in float vTemperature;
in float vLifeFraction;

out vec4 fragColor;

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

    // Smooth circular edge
    float alpha = 1.0 - smoothstep(0.5, 1.0, dist);

    // Fade out near end of life
    alpha *= 1.0 - vLifeFraction * vLifeFraction;

    vec3 color = blackbodyColor(vTemperature);

    // Increase brightness for very hot particles
    float brightness = 1.0 + clamp(log(max(vTemperature, 1000.0) / 5000.0), 0.0, 2.0);
    color *= brightness;

    fragColor = vec4(color, alpha * 0.8);
}
