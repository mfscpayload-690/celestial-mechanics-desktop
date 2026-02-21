using System.Text.Json.Serialization;

namespace CelestialMechanics.AppCore.Serialization.DTO;

// ── Component DTO hierarchy ───────────────────────────────────────────────────
// System.Text.Json polymorphic dispatch (.NET 7+).
// No reflection needed at runtime — the discriminator field is written as a
// plain string into the JSON output.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "componentType")]
[JsonDerivedType(typeof(PhysicsComponentDTO),           "Physics")]
[JsonDerivedType(typeof(StellarEvolutionComponentDTO),  "StellarEvolution")]
[JsonDerivedType(typeof(ExplosionComponentDTO),         "Explosion")]
[JsonDerivedType(typeof(RelativisticComponentDTO),      "Relativistic")]
[JsonDerivedType(typeof(ExpansionComponentDTO),         "Expansion")]
public abstract class ComponentDTO
{
    /// <summary>Discriminator label. Set by each derived class.</summary>
    public abstract string ComponentType { get; }
}

// ── Physics ───────────────────────────────────────────────────────────────────

public sealed class PhysicsComponentDTO : ComponentDTO
{
    public override string ComponentType => "Physics";

    public double Mass        { get; set; }
    public double Radius      { get; set; }
    public double Density     { get; set; }
    public bool   IsCollidable { get; set; } = true;

    // Vec3d fields serialised as flat doubles for readability
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }

    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double VelocityZ { get; set; }

    public double AccelerationX { get; set; }
    public double AccelerationY { get; set; }
    public double AccelerationZ { get; set; }
}

// ── Stellar evolution ─────────────────────────────────────────────────────────

public sealed class StellarEvolutionComponentDTO : ComponentDTO
{
    public override string ComponentType => "StellarEvolution";

    public double Age                { get; set; }
    public double LifeExpectancy     { get; set; }
    public string EvolutionaryStage  { get; set; } = "MainSequence";
    public double LuminosityFactor   { get; set; } = 1.0;
    public double TemperatureKelvin  { get; set; } = 5778.0;
}

// ── Explosion ─────────────────────────────────────────────────────────────────

public sealed class ExplosionComponentDTO : ComponentDTO
{
    public override string ComponentType => "Explosion";

    public double CurrentRadius  { get; set; }
    public double MaxRadius      { get; set; }
    public double ExpansionRate  { get; set; }
    public bool   IsActive       { get; set; }
}

// ── Relativistic ──────────────────────────────────────────────────────────────

public sealed class RelativisticComponentDTO : ComponentDTO
{
    public override string ComponentType => "Relativistic";

    public double SpinParameter    { get; set; }  // dimensionless Kerr a/M
    public double SchwarzschildRadius { get; set; }
    public bool   HasAccretionDisk { get; set; }
    public bool   IsBlackHole      { get; set; }
}

// ── Expansion ─────────────────────────────────────────────────────────────────

public sealed class ExpansionComponentDTO : ComponentDTO
{
    public override string ComponentType => "Expansion";

    public double ExpansionRate  { get; set; }
    public bool   IsEnabled      { get; set; }
}
