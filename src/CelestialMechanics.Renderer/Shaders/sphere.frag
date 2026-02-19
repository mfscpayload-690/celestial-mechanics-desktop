#version 330 core
in vec3 vNormal;
in vec3 vFragPos;
in vec4 vColor;

uniform vec3 uViewPos;
out vec4 FragColor;

void main()
{
    vec3 lightDir = normalize(vec3(0.3, 1.0, 0.5));
    vec3 norm = normalize(vNormal);

    // Ambient
    float ambient = 0.15;

    // Diffuse
    float diff = max(dot(norm, lightDir), 0.0);

    // Specular (Blinn-Phong)
    vec3 viewDir = normalize(uViewPos - vFragPos);
    vec3 halfDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(norm, halfDir), 0.0), 64.0);

    // Emissive component for stars (flagged via alpha > 0.9)
    float emissive = vColor.a > 0.9 ? 0.5 : 0.0;

    vec3 result = (ambient + diff + spec * 0.3 + emissive) * vColor.rgb;
    FragColor = vec4(result, 1.0);
}
