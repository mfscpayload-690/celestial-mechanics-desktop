using Silk.NET.OpenGL;

namespace CelestialMechanics.Renderer;

public sealed class ProceduralAlbedoAtlas : IDisposable
{
    public const int EarthLikeLayer = 0;
    public const int MarsLikeLayer = 1;
    public const int JupiterLikeLayer = 2;
    public const int MoonLikeLayer = 3;
    public const int IceWorldLayer = 4;
    public const int GoldenGasLayer = 5;
    public const int LavaWorldLayer = 6;
    public const int RockyLayer = 7;
    public const int LayerCount = 8;

    private readonly GL _gl;
    public uint Handle { get; private set; }

    public ProceduralAlbedoAtlas(GL gl)
    {
        _gl = gl;
    }

    public unsafe void Initialize(int width = 256, int height = 128)
    {
        if (Handle != 0)
            return;

        Handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, Handle);

        _gl.TexImage3D(
            TextureTarget.Texture2DArray,
            0,
            InternalFormat.Rgba8,
            (uint)width,
            (uint)height,
            LayerCount,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            null);

        for (int layer = 0; layer < LayerCount; layer++)
        {
            byte[] pixels = GenerateLayer(layer, width, height);
            fixed (byte* ptr = pixels)
            {
                _gl.TexSubImage3D(
                    TextureTarget.Texture2DArray,
                    0,
                    0,
                    0,
                    (int)(uint)layer,
                    (uint)width,
                    (uint)height,
                    1,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    ptr);
            }
        }

        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.GenerateMipmap(TextureTarget.Texture2DArray);
    }

    private static byte[] GenerateLayer(int layer, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        int seed = 173 + layer * 101;

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            float lat = 1f - System.MathF.Abs(v * 2f - 1f);

            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                var c = EvaluateLayerColor(layer, u, v, lat, seed);

                int idx = (y * width + x) * 4;
                pixels[idx + 0] = ToByte(c.r);
                pixels[idx + 1] = ToByte(c.g);
                pixels[idx + 2] = ToByte(c.b);
                pixels[idx + 3] = 255;
            }
        }

        return pixels;
    }

    private static (float r, float g, float b) EvaluateLayerColor(int layer, float u, float v, float lat, int seed)
    {
        float n1 = Fbm(u * 5.5f, v * 2.8f, seed);
        float n2 = Fbm(u * 17.0f, v * 9.0f, seed + 13);

        return layer switch
        {
            EarthLikeLayer => EarthLike(u, lat, n1, n2),
            MarsLikeLayer => MarsLike(lat, n1, n2),
            JupiterLikeLayer => JupiterLike(u, v, lat, n1, n2),
            MoonLikeLayer => MoonLike(n1, n2),
            IceWorldLayer => IceWorld(lat, n1, n2),
            GoldenGasLayer => GoldenGas(u, v, lat, n1, n2),
            LavaWorldLayer => LavaWorld(n1, n2),
            RockyLayer => Rocky(n1, n2),
            _ => (0.5f, 0.5f, 0.5f),
        };
    }

    private static (float r, float g, float b) EarthLike(float u, float lat, float n1, float n2)
    {
        float continent = Smoothstep(0.47f, 0.63f, n1);
        float cloud = Smoothstep(0.70f, 0.88f, n2);

        float oceanDepth = 0.35f + 0.65f * lat;
        var ocean = (0.05f, 0.19f + 0.15f * oceanDepth, 0.45f + 0.25f * oceanDepth);
        var land = (0.19f + 0.12f * n2, 0.38f + 0.24f * lat, 0.12f);

        var baseColor = Lerp(ocean, land, continent);
        baseColor = Lerp(baseColor, (0.95f, 0.95f, 0.95f), cloud * 0.45f);

        float coast = Smoothstep(0.44f, 0.52f, n1) * (1f - Smoothstep(0.52f, 0.60f, n1));
        baseColor = Lerp(baseColor, (0.85f, 0.78f, 0.62f), coast * 0.25f);

        return baseColor;
    }

    private static (float r, float g, float b) MarsLike(float lat, float n1, float n2)
    {
        var baseColor = (0.52f + 0.22f * n1, 0.25f + 0.13f * n1, 0.14f + 0.08f * n1);
        float dust = Smoothstep(0.72f, 0.90f, n2);
        baseColor = Lerp(baseColor, (0.78f, 0.56f, 0.35f), dust * 0.33f);

        float polar = Smoothstep(0.88f, 0.99f, 1f - lat);
        baseColor = Lerp(baseColor, (0.90f, 0.87f, 0.82f), polar * 0.65f);

        return baseColor;
    }

    private static (float r, float g, float b) JupiterLike(float u, float v, float lat, float n1, float n2)
    {
        float bands = 0.5f + 0.5f * System.MathF.Sin((v * 26f + n1 * 5f) * System.MathF.PI);
        var c1 = (0.82f, 0.68f, 0.50f);
        var c2 = (0.62f, 0.44f, 0.30f);
        var color = Lerp(c1, c2, bands);

        float spotU = System.MathF.Abs(u - 0.72f);
        float spotV = System.MathF.Abs(v - 0.57f);
        float redSpot = 1f - Smoothstep(0.045f, 0.085f, System.MathF.Sqrt(spotU * spotU + spotV * spotV));
        color = Lerp(color, (0.80f, 0.43f, 0.28f), redSpot);

        color = Lerp(color, (0.88f, 0.82f, 0.70f), Smoothstep(0.80f, 0.95f, n2) * 0.12f);
        return color;
    }

    private static (float r, float g, float b) MoonLike(float n1, float n2)
    {
        float g = 0.46f + 0.30f * n1;
        float crater = Smoothstep(0.72f, 0.95f, n2);
        g *= 1f - crater * 0.25f;
        return (g, g, g * 0.98f);
    }

    private static (float r, float g, float b) IceWorld(float lat, float n1, float n2)
    {
        var baseColor = (0.72f + 0.14f * n1, 0.82f + 0.10f * lat, 0.92f + 0.05f * n1);
        float crevasse = Smoothstep(0.70f, 0.90f, n2);
        return Lerp(baseColor, (0.40f, 0.56f, 0.72f), crevasse * 0.42f);
    }

    private static (float r, float g, float b) GoldenGas(float u, float v, float lat, float n1, float n2)
    {
        float bands = 0.5f + 0.5f * System.MathF.Sin((v * 18f + n1 * 4f) * System.MathF.PI);
        var baseColor = Lerp((0.88f, 0.76f, 0.50f), (0.66f, 0.54f, 0.34f), bands);
        float storms = Smoothstep(0.76f, 0.93f, n2);
        return Lerp(baseColor, (0.95f, 0.88f, 0.72f), storms * 0.20f);
    }

    private static (float r, float g, float b) LavaWorld(float n1, float n2)
    {
        var crust = (0.10f + 0.10f * n1, 0.08f + 0.07f * n1, 0.08f + 0.06f * n1);
        float lava = Smoothstep(0.58f, 0.82f, n2);
        return Lerp(crust, (1.0f, 0.42f, 0.08f), lava * 0.95f);
    }

    private static (float r, float g, float b) Rocky(float n1, float n2)
    {
        var rock = (0.32f + 0.22f * n1, 0.29f + 0.18f * n1, 0.25f + 0.15f * n1);
        float metal = Smoothstep(0.82f, 0.95f, n2);
        return Lerp(rock, (0.62f, 0.57f, 0.52f), metal * 0.15f);
    }

    private static (float r, float g, float b) Lerp((float r, float g, float b) a, (float r, float g, float b) b, float t)
    {
        t = Clamp01(t);
        return (
            a.r + (b.r - a.r) * t,
            a.g + (b.g - a.g) * t,
            a.b + (b.b - a.b) * t);
    }

    private static float Fbm(float x, float y, int seed)
    {
        float sum = 0f;
        float amp = 0.55f;
        float fx = x;
        float fy = y;

        for (int i = 0; i < 4; i++)
        {
            sum += amp * SmoothNoise(fx, fy, seed + i * 37);
            fx = fx * 2.03f + 5.17f;
            fy = fy * 2.11f + 3.71f;
            amp *= 0.5f;
        }

        return Clamp01(sum);
    }

    private static float SmoothNoise(float x, float y, int seed)
    {
        int x0 = (int)System.MathF.Floor(x);
        int y0 = (int)System.MathF.Floor(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        float tx = x - x0;
        float ty = y - y0;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

        float n00 = Hash2(x0, y0, seed);
        float n10 = Hash2(x1, y0, seed);
        float n01 = Hash2(x0, y1, seed);
        float n11 = Hash2(x1, y1, seed);

        float nx0 = n00 + (n10 - n00) * tx;
        float nx1 = n01 + (n11 - n01) * tx;
        return nx0 + (nx1 - nx0) * ty;
    }

    private static float Hash2(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437 + 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0x00FFFFFFu) / 16777215.0f;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

    private static byte ToByte(float x)
    {
        int v = (int)(Clamp01(x) * 255f + 0.5f);
        return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            _gl.DeleteTexture(Handle);
            Handle = 0;
        }
    }
}
