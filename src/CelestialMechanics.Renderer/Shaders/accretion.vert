#version 330 core

// Accretion disk particle vertex shader.
// Uses point sprites with temperature-based sizing and colouring.

layout(location = 0) in vec3 aPosition;
layout(location = 1) in float aTemperature;
layout(location = 2) in float aAge;
layout(location = 3) in float aMaxAge;

uniform mat4 uViewProjection;
uniform float uPointScale;

out float vTemperature;
out float vLifeFraction;

void main()
{
    gl_Position = uViewProjection * vec4(aPosition, 1.0);

    // Life fraction: 0 = just born, 1 = about to expire
    vLifeFraction = clamp(aAge / max(aMaxAge, 0.001), 0.0, 1.0);

    // Temperature-based size: hotter particles are smaller/brighter
    float tempNorm = clamp(log(max(aTemperature, 1000.0)) / log(1e7), 0.0, 1.0);
    float size = mix(4.0, 1.5, tempNorm) * uPointScale;

    // Fade out near end of life
    size *= (1.0 - vLifeFraction * 0.5);

    gl_PointSize = size;
    vTemperature = aTemperature;
}
