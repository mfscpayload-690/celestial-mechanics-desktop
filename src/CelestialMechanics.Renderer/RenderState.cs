using System.Numerics;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation;

namespace CelestialMechanics.Renderer;

public struct RenderBody
{
    public int Id;
    public Vector3 Position;
    public float Radius;
    public Vector4 Color;
    public int BodyType;
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

    public void UpdateFrom(SimulationEngine engine)
    {
        var physicsBodies = engine.Bodies;
        if (physicsBodies == null || physicsBodies.Length == 0)
        {
            BodyCount = 0;
            BlackHoleCount = 0;
            return;
        }

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

            var pos = new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);

            Bodies[activeCount++] = new RenderBody
            {
                Id = body.Id,
                Position = pos,
                Radius = MathF.Max(0.01f, (float)body.Radius),
                Color = GetBodyColor(body.Type),
                BodyType = (int)body.Type
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
        BodyType.BlackHole => new Vector4(0.1f, 0.0f, 0.1f, 0.7f), // Darker, slightly transparent
        _ => new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
    };
}
