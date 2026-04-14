using System.Diagnostics;
using System.Windows.Threading;

namespace CelestialMechanics.Desktop.Infrastructure;

public sealed class RenderLoop : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action<double> _renderAction;
    private readonly Stopwatch _clock = new();

    private double _lastSeconds;
    private int _frameCount;
    private double _fpsWindowSeconds;

    public bool IsInitialized { get; private set; }
    public double CurrentFps { get; private set; }
    public double LastRenderTimeMs { get; private set; }
    public Exception? LastError { get; private set; }

    public RenderLoop(Action<double> renderAction, Dispatcher dispatcher)
    {
        _renderAction = renderAction;
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16.0),
        };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        LastError = null;
        _frameCount = 0;
        _fpsWindowSeconds = 0;
        _lastSeconds = 0;
        _clock.Restart();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _clock.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = _lastSeconds <= 0 ? 1.0 / 60.0 : System.Math.Max(1e-4, now - _lastSeconds);
            _lastSeconds = now;

            var frameSw = Stopwatch.StartNew();
            _renderAction(dt);
            frameSw.Stop();

            LastRenderTimeMs = frameSw.Elapsed.TotalMilliseconds;
            _frameCount++;
            _fpsWindowSeconds += dt;

            if (_fpsWindowSeconds >= 0.5)
            {
                CurrentFps = _frameCount / _fpsWindowSeconds;
                _frameCount = 0;
                _fpsWindowSeconds = 0;
            }

            IsInitialized = true;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex;
            Stop();
        }
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        Stop();
    }
}
