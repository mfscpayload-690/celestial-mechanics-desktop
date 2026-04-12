#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec4 aColor;

uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uGridOffset;

out vec4 vColor;

void main()
{
    vec3 pos = aPosition + vec3(uGridOffset.x, 0.0, uGridOffset.z);
    gl_Position = uProjection * uView * vec4(pos, 1.0);
    vColor = aColor;
}
