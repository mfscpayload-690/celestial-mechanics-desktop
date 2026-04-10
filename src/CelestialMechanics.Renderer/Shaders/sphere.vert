#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
// Per-instance: position.xyz + radius, color, visual params
layout(location = 2) in vec4 instancePosRadius;
layout(location = 3) in vec4 instanceColor;
layout(location = 4) in vec4 instanceVisual;

uniform mat4 uView;
uniform mat4 uProjection;

out vec3 vNormal;
out vec3 vFragPos;
out vec4 vColor;
out vec4 vVisual;
out vec3 vLocalNormal;

void main()
{
    vec3 world = aPosition * instancePosRadius.w + instancePosRadius.xyz;
    vec4 worldPos = vec4(world, 1.0);
    gl_Position = uProjection * uView * worldPos;
    vNormal = normalize(aNormal);
    vFragPos = worldPos.xyz;
    vColor = instanceColor;
    vVisual = instanceVisual;
    vLocalNormal = normalize(aNormal);
}
