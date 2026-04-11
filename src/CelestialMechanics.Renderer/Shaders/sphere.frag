#version 330 core
in vec3 vNormal;
in vec3 vFragPos;
in vec4 vColor;
in vec4 vVisual;
in vec3 vLocalNormal;

uniform vec3 uViewPos;
uniform float uTime;
uniform float uGlobalLuminosity;
uniform float uGlobalGlow;
uniform float uGlobalSaturation;
uniform int uBhQualityTier;      // 0=low, 1=medium, 2=high
uniform int uBhPreset;           // 0=cinematic orange, 1=EHT230, 2=EHT345
uniform float uBhRingThickness;
uniform float uBhLensStrength;
uniform float uBhDopplerBoost;
uniform float uBhOpticalDepth;
uniform float uBhTemperatureScale;
uniform float uBhBloomScale;
uniform int uBhDebugMode;        // 0=none, 1=horizon, 2=ring, 3=warp, 4=optical depth
uniform float uBhParticleHeat;
uniform float uBhParticleDensity;
out vec4 FragColor;

vec3 toneMapAces(vec3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
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
    float s = 0.0;
    for (int i = 0; i < 4; i++)
    {
        s += a * valueNoise3(p);
        p = p * 2.07 + vec3(3.1, 5.7, 1.9);
        a *= 0.5;
    }
    return s;
}

vec3 blackbodyColor(float tempK)
{
    float t = clamp(tempK, 1000.0, 50000.0);
    vec3 color;

    if (t < 3500.0)
    {
        float f = (t - 1000.0) / 2500.0;
        color = mix(vec3(1.0, 0.10, 0.0), vec3(1.0, 0.55, 0.1), f);
    }
    else if (t < 6500.0)
    {
        float f = (t - 3500.0) / 3000.0;
        color = mix(vec3(1.0, 0.55, 0.1), vec3(1.0, 0.95, 0.9), f);
    }
    else if (t < 15000.0)
    {
        float f = (t - 6500.0) / 8500.0;
        color = mix(vec3(1.0, 0.95, 0.9), vec3(0.7, 0.8, 1.0), f);
    }
    else
    {
        float f = clamp((t - 15000.0) / 35000.0, 0.0, 1.0);
        color = mix(vec3(0.7, 0.8, 1.0), vec3(0.4, 0.5, 1.0), f);
    }

    return color;
}

vec3 applyBodyTexture(vec3 baseColor, vec3 n, float visualType)
{
    if (visualType < 0.5)
    {
        float gran = valueNoise3(n * 9.0 + vec3(0.0, uTime * 0.07, 0.0));
        vec3 warm = vec3(0.78, 0.86, 1.0);
        vec3 hot = vec3(0.98, 0.99, 1.0);
        return mix(warm, hot, gran);
    }
    else if (visualType < 1.5)
    {
        float continents = fbm(n * 6.5);
        float clouds = fbm(n * 18.0 + vec3(uTime * 0.01));
        vec3 ocean = baseColor * vec3(0.55, 0.75, 1.0);
        vec3 land = vec3(0.22, 0.45, 0.18);
        vec3 c = mix(ocean, land, smoothstep(0.45, 0.62, continents));
        c = mix(c, vec3(0.95), smoothstep(0.72, 0.86, clouds) * 0.35);
        return c;
    }
    else if (visualType < 2.5)
    {
        float lat = n.y * 0.5 + 0.5;
        float bands = sin((lat * 18.0 + fbm(n * 8.0) * 2.0) * 3.14159);
        float storms = fbm(vec3(n.xz * 15.0, n.y * 5.0 + uTime * 0.03));
        vec3 c1 = vec3(0.90, 0.75, 0.55);
        vec3 c2 = vec3(0.65, 0.45, 0.30);
        vec3 c = mix(c1, c2, bands * 0.5 + 0.5);
        c *= 0.9 + 0.2 * storms;
        return c;
    }
    else if (visualType < 3.5)
    {
        float craters = fbm(n * 22.0);
        vec3 rock = vec3(0.62, 0.62, 0.60);
        return rock * (0.75 + 0.4 * craters);
    }
    else if (visualType < 4.5)
    {
        float rough = fbm(n * 20.0);
        vec3 rock = vec3(0.52, 0.48, 0.42);
        return rock * (0.68 + 0.45 * rough);
    }
    else if (visualType < 5.5)
    {
        float pulse = 0.5 + 0.5 * sin(uTime * 2.2);
        return mix(vec3(0.55, 0.85, 1.0), vec3(0.9, 0.98, 1.0), pulse * 0.45);
    }
    else if (visualType < 6.5)
    {
        float ring = smoothstep(0.1, 0.75, 1.0 - abs(n.z));
        vec3 core = vec3(0.02, 0.02, 0.03);
        vec3 acc = vec3(0.80, 0.55, 1.0);
        return mix(core, acc, ring * 0.22);
    }
    else
    {
        float swirl = 0.5 + 0.5 * sin(dot(n, vec3(9.0, 7.0, 5.0)) + uTime * 3.2);
        float edge = smoothstep(0.25, 1.0, 1.0 - abs(n.y));
        vec3 hot = vec3(1.0, 0.84, 0.62);
        vec3 cool = vec3(0.58, 0.74, 1.0);
        return mix(cool, hot, swirl * 0.65 + edge * 0.35);
    }
}

vec3 shadeBlackHole(vec3 norm, vec3 viewDir, vec3 localN, out float horizonMask, out float ringMask, out float warpMask, out float opticalDepthMask)
{
    float edge = 1.0 - abs(dot(norm, viewDir));
    float equator = 1.0 - abs(localN.y);
    float equatorBand = exp(-pow(localN.y / 0.14, 2.0));

    // Event horizon mask: suppress center and preserve near-rim emission.
    horizonMask = smoothstep(0.18, 0.92, edge);

    float ringCenter = 0.68 + 0.08 * clamp(uBhLensStrength, 0.0, 2.5);
    float ringWidth = max(0.03, uBhRingThickness * 0.16);
    ringMask = exp(-pow((edge - ringCenter) / ringWidth, 2.0)) * (0.4 + 0.6 * equator);

    float arcSep = (uBhPreset == 2) ? 0.24 : ((uBhPreset == 1) ? 0.16 : 0.20);
    float arcWidth = (uBhQualityTier == 0) ? 0.24 : ((uBhQualityTier == 1) ? 0.18 : 0.13);
    float upperArc = exp(-pow((localN.y - arcSep) / arcWidth, 2.0));
    float lowerArc = exp(-pow((localN.y + arcSep) / arcWidth, 2.0));
    warpMask = clamp((upperArc + lowerArc) * edge * uBhLensStrength, 0.0, 1.0);

    // Approximate orbital tangent around Y axis for Doppler asymmetry.
    vec3 tangent = normalize(vec3(-localN.z, 0.0, localN.x));
    float beta = dot(tangent, viewDir) * 0.32 * uBhDopplerBoost;
    float gFactor = clamp(1.0 + beta, 0.25, 2.8);

    float presetTemp = (uBhPreset == 0) ? 7600.0 : ((uBhPreset == 1) ? 6200.0 : 9100.0);
    float particleGain = 0.8 + 0.5 * clamp(uBhParticleHeat, 0.0, 1.0);
    float temp = presetTemp * uBhTemperatureScale * particleGain * (0.62 + 0.85 * ringMask + 0.55 * warpMask + 0.65 * equatorBand);

    opticalDepthMask = clamp(uBhOpticalDepth * (0.55 + 0.45 * equator) * (0.8 + 0.7 * uBhParticleDensity), 0.0, 4.0);
    float transmittance = exp(-opticalDepthMask);

    vec3 source = blackbodyColor(temp) * pow(gFactor, 3.0);
    vec3 ringGlow = source * (ringMask * 0.95 + warpMask * 0.55 + equatorBand * 0.85);
    vec3 scattered = source * (1.0 - transmittance) * (0.55 + 0.45 * ringMask);

    vec3 core = vec3(0.004, 0.004, 0.006);
    float shadow = 1.0 - horizonMask;
    vec3 result = mix(ringGlow + scattered, core, shadow * 0.95);

    if (uBhQualityTier >= 1)
    {
        float secondaryRing = exp(-pow((edge - (ringCenter + 0.12)) / (ringWidth * 1.4), 2.0));
        result += source * secondaryRing * 0.20;
    }

    if (uBhQualityTier >= 2)
    {
        float halo = pow(edge, 1.4) * (0.2 + 0.35 * warpMask);
        result += source * halo * 0.18;
    }

    result += source * pow(edge, 1.1) * 0.12 * uBhBloomScale;
    return result;
}

void main()
{
    vec3 lightDir = normalize(vec3(0.3, 1.0, 0.5));
    vec3 norm = normalize(vNormal);
    vec3 localN = normalize(vLocalNormal);

    // Ambient
    float ambient = 0.15;

    // Diffuse
    float diff = max(dot(norm, lightDir), 0.0);

    // Specular (Blinn-Phong)
    vec3 viewDir = normalize(uViewPos - vFragPos);
    vec3 halfDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(norm, halfDir), 0.0), 64.0);

    float visualType = vVisual.x;
    float luminosity = vVisual.y;
    float glowStrength = vVisual.z;
    float atmosphere = vVisual.w;

    if (visualType >= 6.5)
    {
        float horizonMask;
        float ringMask;
        float warpMask;
        float opticalDepthMask;

        vec3 bhColor = shadeBlackHole(norm, viewDir, localN, horizonMask, ringMask, warpMask, opticalDepthMask);

        if (uBhDebugMode == 1)
        {
            FragColor = vec4(vec3(horizonMask), 1.0);
            return;
        }
        if (uBhDebugMode == 2)
        {
            FragColor = vec4(vec3(ringMask), 1.0);
            return;
        }
        if (uBhDebugMode == 3)
        {
            FragColor = vec4(warpMask, 1.0 - warpMask, 0.0, 1.0);
            return;
        }
        if (uBhDebugMode == 4)
        {
            FragColor = vec4(vec3(opticalDepthMask / 4.0), 1.0);
            return;
        }

        vec3 result = toneMapAces(bhColor);
        result = pow(result, vec3(1.0 / 2.2));
        FragColor = vec4(result, clamp(vColor.a, 0.0, 1.0));
        return;
    }

    vec3 albedo = applyBodyTexture(vColor.rgb, localN, visualType);

    float rim = pow(1.0 - max(dot(viewDir, norm), 0.0), 2.8);
    vec3 rimColor = mix(albedo * 0.55, vec3(0.9, 0.95, 1.0), 0.35);

    float starPulse = 0.92 + 0.08 * sin(uTime * 1.7 + localN.y * 8.0);
    float emissive = luminosity * (visualType < 0.5 ? starPulse : 1.0);

    vec3 lit = (ambient + diff) * albedo + spec * 0.35;
    vec3 glow = rimColor * (rim * glowStrength + atmosphere * rim * 0.45) * uGlobalGlow;

    vec3 result = lit + emissive * uGlobalLuminosity * albedo + glow;

    float bloom = 0.0;
    if (visualType < 0.5)
        bloom = pow(rim, 1.24) * 1.08 + smoothstep(0.84, 1.0, diff) * 0.12;
    else if (visualType < 5.5 && luminosity > 0.7)
        bloom = pow(rim, 1.45) * 0.75;
    else if (visualType >= 6.5)
        bloom = pow(rim, 1.05) * 1.35;

    vec3 bloomTint = visualType >= 6.5 ? vec3(0.66, 0.80, 1.0) : vec3(0.72, 0.84, 1.0);
    result += bloomTint * bloom * luminosity * uGlobalGlow;

    float luma = dot(result, vec3(0.2126, 0.7152, 0.0722));
    result = mix(vec3(luma), result, clamp(uGlobalSaturation, 0.0, 2.0));
    result = toneMapAces(result);
    result = pow(result, vec3(1.0 / 2.2));

    FragColor = vec4(result, clamp(vColor.a, 0.0, 1.0));
}
