using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Simulation.Analysis;

public enum InsightSeverity
{
    Info,
    Caution,
    Warning,
    Critical,
}

public readonly record struct EventAdvisory(string Summary, string SuggestedAction, InsightSeverity Severity);

public static class EventAdvisor
{
    public static EventAdvisory Build(string? activeEventWarning, OrbitType? orbitType, string stabilityLabel, CollisionMode collisionMode)
    {
        if (string.Equals(activeEventWarning, "Collision Imminent", StringComparison.Ordinal))
        {
            return collisionMode switch
            {
                CollisionMode.MergeOnly => new EventAdvisory(
                    "High-energy impact risk with forced merge response.",
                    "Increase periapsis immediately or switch collision mode to Elastic/Fragmentation for analysis.",
                    InsightSeverity.Critical),

                CollisionMode.BounceOnly => new EventAdvisory(
                    "Impact risk with elastic rebound response.",
                    "Reduce relative velocity and increase periapsis to avoid unstable repeated encounters.",
                    InsightSeverity.Critical),

                _ => new EventAdvisory(
                    "Impact risk in realistic collision regime.",
                    "Increase periapsis or reduce approach speed; monitor for fragmentation conditions.",
                    InsightSeverity.Critical),
            };
        }

        if (string.Equals(activeEventWarning, "Escape Trajectory", StringComparison.Ordinal))
        {
            return new EventAdvisory(
                "Body is trending unbound from the reference system.",
                "Apply a retrograde correction of approximately 2-5% near periapsis to preserve capture.",
                InsightSeverity.Warning);
        }

        if (string.Equals(activeEventWarning, "Orbit Decay", StringComparison.Ordinal))
        {
            return new EventAdvisory(
                "Periapsis decay detected with inward radial trend.",
                "Increase tangential velocity by approximately 3% to raise periapsis and stabilize the orbit.",
                InsightSeverity.Warning);
        }

        if (string.Equals(stabilityLabel, "Unstable", StringComparison.Ordinal))
        {
            return new EventAdvisory(
                "Global integration drift indicates unstable regime.",
                "Lower time scale and tighten timestep/integrator configuration before further interpretation.",
                InsightSeverity.Warning);
        }

        if (string.Equals(stabilityLabel, "Marginal", StringComparison.Ordinal))
        {
            return new EventAdvisory(
                "System drift is measurable but not yet critical.",
                "Monitor energy/momentum drift and maintain conservative timestep settings.",
                InsightSeverity.Caution);
        }

        if (orbitType == OrbitType.Hyperbolic)
        {
            return new EventAdvisory(
                "Reference orbit is hyperbolic (escape class).",
                "For capture experiments, plan a retrograde burn centered at periapsis.",
                InsightSeverity.Caution);
        }

        return new EventAdvisory(
            "No active anomalies detected.",
            "Maintain current configuration and continue observation.",
            InsightSeverity.Info);
    }
}