namespace CelestialMechanics.Simulation.Systems;

/// <summary>
/// Manages simulation time scaling. Affects integration, expansion,
/// stellar evolution, and explosion timing. Does NOT alter gravitational constants.
/// </summary>
public sealed class TimeManager
{
    public double SimulationTime { get; private set; }
    public double TimeScale { get; set; } = 1.0;

    public double GetEffectiveDelta(double baseDt)
    {
        return baseDt * TimeScale;
    }

    public void AdvanceTime(double effectiveDt)
    {
        SimulationTime += effectiveDt;
    }

    public void Reset()
    {
        SimulationTime = 0.0;
    }
}
