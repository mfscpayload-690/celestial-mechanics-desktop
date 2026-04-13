namespace CelestialMechanics.Desktop.Core;

public sealed class AppState
{
    private AppMode _currentMode;

    public AppMode CurrentMode
    {
        get => _currentMode;
        private set
        {
            _currentMode = value;
            ModeChanged?.Invoke(value);
        }
    }

    public event Action<AppMode>? ModeChanged;

    public void SetMode(AppMode mode)
    {
        CurrentMode = mode;
    }
}
