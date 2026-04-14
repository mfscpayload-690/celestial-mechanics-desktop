using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace CelestialMechanics.App;

public static class Program
{
    public static void Main(string[] args)
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1600, 900),
            Title = "Celestial Mechanics — Scientific Simulation Engine",
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3)),
            VSync = true,
            ShouldSwapAutomatically = true,
            IsVisible = false,
        };

        var window = Window.Create(options);
        var app = new Application(window);

        window.Load += app.OnLoad;
        window.Update += app.OnUpdate;
        window.Render += app.OnRender;
        window.Closing += app.OnClose;
        window.Resize += app.OnResize;

        window.Run();
    }
}
