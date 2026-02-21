namespace CelestialMechanics.AppCore.Modes;

/// <summary>
/// Defines the behavioral contract for an application operating mode.
///
/// The rendering call (<see cref="Render"/>) is a no-op at the AppCore level.
/// Future renderer layers override or wrap this to issue actual OpenGL/Vulkan commands.
/// </summary>
public interface IAppMode
{
    /// <summary>Human-readable mode identifier (e.g. "Simulation", "Observation").</summary>
    string ModeName { get; }

    /// <summary>Called once when this mode becomes active.</summary>
    void Initialize();

    /// <summary>Called every frame/tick with the elapsed wall-clock delta in seconds.</summary>
    void Update(double deltaTime);

    /// <summary>Render hook — no-op at AppCore level; bridges to renderer layer.</summary>
    void Render();

    /// <summary>Called when the mode is deactivated. Release mode-local resources.</summary>
    void Dispose();
}
