#version 330 core

const vec2 verts[3] = vec2[](
    vec2(-1.0, -1.0),
    vec2( 3.0, -1.0),
    vec2(-1.0,  3.0)
);

out vec2 vUv;

void main()
{
    vec2 p = verts[gl_VertexID];
    gl_Position = vec4(p, 0.0, 1.0);
    vUv = p * 0.5 + 0.5;
}
