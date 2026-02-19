#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
// Per-instance (locations 2-5 for mat4, 6 for color)
layout(location = 2) in mat4 instanceModel;
layout(location = 6) in vec4 instanceColor;

uniform mat4 uView;
uniform mat4 uProjection;

out vec3 vNormal;
out vec3 vFragPos;
out vec4 vColor;

void main()
{
    vec4 worldPos = instanceModel * vec4(aPosition, 1.0);
    gl_Position = uProjection * uView * worldPos;
    vNormal = mat3(transpose(inverse(instanceModel))) * aNormal;
    vFragPos = worldPos.xyz;
    vColor = instanceColor;
}
