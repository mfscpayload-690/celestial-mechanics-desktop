using System.IO;
using CelestialMechanics.Desktop.Models;

namespace CelestialMechanics.Desktop.Infrastructure.Security;

/// <summary>
/// Validates deserialized simulation save states (SEC-02).
/// All values from loaded files are treated as untrusted input.
/// </summary>
public static class SaveStateValidator
{
    public static void Validate(SimulationSaveState state)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        if (state.Bodies is null || state.Bodies.Count > 10_000)
            throw new InvalidDataException("Invalid body count in save file.");

        foreach (var body in state.Bodies)
        {
            ValidateBody(body);
        }

        ValidateConfig(state.Config);
    }

    private static void ValidateBody(BodySaveData b)
    {
        if (!PhysicsInputValidator.IsFinite(b.PositionX) ||
            !PhysicsInputValidator.IsFinite(b.PositionY) ||
            !PhysicsInputValidator.IsFinite(b.PositionZ))
            throw new InvalidDataException($"Body has non-finite position.");

        if (!PhysicsInputValidator.IsFinite(b.VelocityX) ||
            !PhysicsInputValidator.IsFinite(b.VelocityY) ||
            !PhysicsInputValidator.IsFinite(b.VelocityZ))
            throw new InvalidDataException($"Body has non-finite velocity.");

        if (!PhysicsInputValidator.IsFinite(b.Mass) || b.Mass <= 0)
            throw new InvalidDataException($"Body has invalid mass.");

        if (b.Radius <= 0)
            throw new InvalidDataException($"Body has invalid radius.");
    }

    private static void ValidateConfig(ConfigSaveData c)
    {
        if (c is null) return;

        if (c.TimeStep is <= 0 or > 1.0)
            throw new InvalidDataException("Timestep out of valid range.");
    }
}
