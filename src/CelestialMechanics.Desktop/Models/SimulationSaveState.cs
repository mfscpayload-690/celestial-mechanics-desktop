using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.Models;

public sealed class SimulationSaveState
{
    public List<BodySaveData> Bodies { get; set; } = new();
    public ConfigSaveData Config { get; set; } = new();
}

public sealed class BodySaveData
{
    public int Id { get; set; }
    public double Mass { get; set; }
    public double Radius { get; set; }
    public double Density { get; set; }

    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }

    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double VelocityZ { get; set; }

    public BodyType Type { get; set; } = BodyType.Star;
    public bool IsActive { get; set; } = true;
    public bool IsCollidable { get; set; } = true;
}

public sealed class ConfigSaveData
{
    public string IntegratorName { get; set; } = "Verlet";
    public double TimeStep { get; set; } = 0.001;
    public double MinDt { get; set; } = 1e-6;
    public double MaxDt { get; set; } = 0.01;

    public bool DeterministicMode { get; set; } = true;
    public bool UseParallelComputation { get; set; }
    public bool UseSimd { get; set; }
    public bool UseSoAPath { get; set; } = true;

    public bool UseBarnesHut { get; set; }
    public double Theta { get; set; } = 0.5;

    public bool EnableCollisions { get; set; } = true;
    public bool UseAdaptiveTimestep { get; set; }

    public bool EnablePostNewtonian { get; set; }
    public bool EnableGravitationalLensing { get; set; }
    public bool EnableAccretionDisks { get; set; }
    public bool EnableGravitationalWaves { get; set; }
    public bool EnableJetEmission { get; set; }

    public double SofteningEpsilon { get; set; } = 1e-4;
    public string SofteningMode { get; set; } = "Constant";
}
