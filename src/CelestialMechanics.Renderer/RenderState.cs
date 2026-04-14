using System.Numerics;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Astrophysics;
using CelestialMechanics.Simulation;

namespace CelestialMechanics.Renderer;

public struct RenderBody
{
    public int Id;
    public float Mass;
    public Vector3 Position;
    public float Radius;
    public Vector4 Color;
    public int BodyType;
    public Vector4 VisualParams;
    public float TextureLayer;
    public float StarTemperatureK;
    public bool IsSelected;
}

public class RenderState
{
    public RenderBody[] Bodies { get; set; } = Array.Empty<RenderBody>();
    public int BodyCount { get; set; }
    public float InterpolationAlpha { get; set; }

    // ── Phase 6: Black hole tracking for lensing ──────────────────────────
    public Vector3[] BlackHolePositions { get; set; } = new Vector3[8];
    public float[] BlackHoleMasses { get; set; } = new float[8];
    public int BlackHoleCount { get; set; }

    public void UpdateFrom(SimulationEngine engine, ReferenceFrameManager? referenceFrame = null, int selectedBodyId = -1)
    {
        var physicsBodies = engine.Bodies;
        if (physicsBodies == null || physicsBodies.Length == 0)
        {
            BodyCount = 0;
            BlackHoleCount = 0;
            return;
        }

        var frameOrigin = referenceFrame?.ComputeOrigin(physicsBodies) ?? CelestialMechanics.Math.Vec3d.Zero;

        if (Bodies.Length < physicsBodies.Length)
            Bodies = new RenderBody[physicsBodies.Length];

        int activeCount = 0;
        int bhCount = 0;
        float alpha = (float)engine.InterpolationAlpha;
        InterpolationAlpha = alpha;

        for (int i = 0; i < physicsBodies.Length; i++)
        {
            ref var body = ref physicsBodies[i];
            if (!body.IsActive) continue;

            var framePos = body.Position - frameOrigin;
            var pos = new Vector3((float)framePos.X, (float)framePos.Y, (float)framePos.Z);

            Bodies[activeCount++] = new RenderBody
            {
                Id = body.Id,
                Mass = (float)System.Math.Max(body.Mass, 1e-6),
                Position = pos,
                Radius = MathF.Max(0.01f, (float)body.Radius),
                Color = GetBodyColor(body.Type),
                BodyType = (int)body.Type,
                VisualParams = GetVisualParams(body),
                TextureLayer = GetTextureLayer(body),
                StarTemperatureK = GetStarTemperatureK(body),
                IsSelected = body.Id == selectedBodyId,
            };

            // Track black holes for lensing (max 8)
            if (body.Type == BodyType.BlackHole && bhCount < 8)
            {
                BlackHolePositions[bhCount] = pos;
                BlackHoleMasses[bhCount] = (float)body.Mass;
                bhCount++;
            }
        }
        BodyCount = activeCount;
        BlackHoleCount = bhCount;
    }

    private static Vector4 GetBodyColor(BodyType type) => type switch
    {
        BodyType.Star => new Vector4(1.0f, 0.9f, 0.3f, 1.0f),
        BodyType.Planet or BodyType.RockyPlanet => new Vector4(0.2f, 0.4f, 0.8f, 1.0f),
        BodyType.GasGiant => new Vector4(0.8f, 0.7f, 0.5f, 1.0f),
        BodyType.Moon => new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
        BodyType.Asteroid or BodyType.Comet => new Vector4(0.5f, 0.5f, 0.4f, 1.0f),
        BodyType.NeutronStar => new Vector4(0.5f, 0.8f, 1.0f, 1.0f),
        BodyType.BlackHole => new Vector4(0.02f, 0.02f, 0.02f, 1.0f), // Opaque black core
        _ => new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
    };

    // x = visual type id, y = luminosity, z = glow intensity, w = atmosphere/rim strength
    private static Vector4 GetVisualParams(PhysicsBody body)
    {
        float baseVisualType = body.Type switch
        {
            BodyType.Star => 0f,
            BodyType.Planet => 1f,
            BodyType.RockyPlanet => 1f,
            BodyType.GasGiant => 2f,
            BodyType.Moon => 3f,
            BodyType.Asteroid => 4f,
            BodyType.Comet => 4f,
            BodyType.NeutronStar => 5f,
            BodyType.BlackHole => 7f,
            _ => 1f,
        };

        double luminousPower = System.Math.Max(body.Luminosity, 0.0);
        float luminosity = (float)System.Math.Clamp(Units.RenderScale(luminousPower / 1.0e20), 0.01, 4.5);

        if (body.Type == BodyType.Star)
            luminosity = System.MathF.Max(luminosity, 0.85f);
        else if (body.Type == BodyType.NeutronStar)
            luminosity = System.MathF.Max(luminosity, 1.25f);
        else if (body.Type == BodyType.BlackHole)
            luminosity = System.MathF.Max(luminosity, 0.1f);

        float glow = body.Type switch
        {
            BodyType.Star => 1.0f + 0.18f * luminosity,
            BodyType.NeutronStar => 1.3f + 0.2f * luminosity,
            BodyType.BlackHole => 0.45f,
            _ => 0.02f + 0.04f * luminosity,
        };

        float rim = body.Type switch
        {
            BodyType.Planet or BodyType.RockyPlanet => 0.35f,
            BodyType.GasGiant => 0.24f,
            BodyType.BlackHole => 0.55f,
            BodyType.Star => 0.22f,
            BodyType.NeutronStar => 0.28f,
            _ => 0.12f,
        };

        return new Vector4(baseVisualType, luminosity, glow, rim);
    }

    private static float GetTextureLayer(PhysicsBody body)
    {
        int selector = System.Math.Abs(body.Id) % 4;

        return body.Type switch
        {
            BodyType.Planet or BodyType.RockyPlanet => selector switch
            {
                0 => ProceduralAlbedoAtlas.EarthLikeLayer,
                1 => ProceduralAlbedoAtlas.MarsLikeLayer,
                2 => ProceduralAlbedoAtlas.IceWorldLayer,
                _ => ProceduralAlbedoAtlas.LavaWorldLayer,
            },
            BodyType.GasGiant => (System.Math.Abs(body.Id) % 2) == 0
                ? ProceduralAlbedoAtlas.JupiterLikeLayer
                : ProceduralAlbedoAtlas.GoldenGasLayer,
            BodyType.Moon => ProceduralAlbedoAtlas.MoonLikeLayer,
            BodyType.Asteroid or BodyType.Comet => ProceduralAlbedoAtlas.RockyLayer,
            _ => ProceduralAlbedoAtlas.EarthLikeLayer,
        };
    }

    private static float GetStarTemperatureK(PhysicsBody body)
    {
        if (body.Type != BodyType.Star && body.Type != BodyType.NeutronStar)
            return 0f;

        if (body.Temperature > 0.0)
            return (float)System.Math.Clamp(body.Temperature, 1200.0, 50000.0);

        if (body.Type == BodyType.NeutronStar)
            return 12000f;

        const double solarRadiusAu = 0.00465047;
        double massSolar = System.Math.Max(body.Mass, 0.08);
        double radiusSolar = System.Math.Max(body.Radius / solarRadiusAu, 0.08);

        double luminosity = System.Math.Pow(massSolar, 3.4);
        double temp = 5772.0 * System.Math.Pow(luminosity / (radiusSolar * radiusSolar), 0.25);
        temp = System.Math.Clamp(temp, 2400.0, 45000.0);
        return (float)temp;
    }
}
