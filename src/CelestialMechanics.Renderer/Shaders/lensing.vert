#version 330 core

// Full-screen quad vertex shader for gravitational lensing post-process.
// Emits a screen-filling triangle strip using gl_VertexID.

out vec2 vTexCoord;

void main()
{
    // Generate full-screen triangle positions from vertex ID (0, 1, 2, 3)
    // Using a trick: 2 triangles from 4 vertices as a triangle strip.
    float x = float((gl_VertexID & 1) * 2 - 1);  // -1 or 1
    float y = float((gl_VertexID >> 1) * 2 - 1);  // -1 or 1

    gl_Position = vec4(x, y, 0.0, 1.0);
    vTexCoord = vec2(x * 0.5 + 0.5, y * 0.5 + 0.5);
}
