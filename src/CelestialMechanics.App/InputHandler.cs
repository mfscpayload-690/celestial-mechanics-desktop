using Silk.NET.Input;
using CelestialMechanics.Renderer;

namespace CelestialMechanics.App;

public class InputHandler
{
    private readonly Camera _camera;
    private bool _leftMouseDown;
    private bool _rightMouseDown;
    private System.Numerics.Vector2 _lastMousePos;

    public event Action? OnToggleSimulation;  // Space
    public event Action? OnStepSimulation;    // Right arrow
    public event Action? OnResetSimulation;   // R key

    public InputHandler(IInputContext input, Camera camera)
    {
        _camera = camera;

        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }

        foreach (var mouse in input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnScroll;
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        switch (key)
        {
            case Key.Space: OnToggleSimulation?.Invoke(); break;
            case Key.Right: OnStepSimulation?.Invoke(); break;
            case Key.R: OnResetSimulation?.Invoke(); break;
            case Key.G: // Toggle grid - needs to be wired
                break;
        }
    }

    // Mouse handlers for camera orbit/pan/zoom
    // Left drag -> orbit, Right drag -> pan, Scroll -> zoom

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _leftMouseDown = true;
            _lastMousePos = new System.Numerics.Vector2(mouse.Position.X, mouse.Position.Y);
        }
        else if (button == MouseButton.Right)
        {
            _rightMouseDown = true;
            _lastMousePos = new System.Numerics.Vector2(mouse.Position.X, mouse.Position.Y);
        }
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMouseDown = false;
        else if (button == MouseButton.Right)
            _rightMouseDown = false;
    }

    private void OnMouseMove(IMouse mouse, System.Numerics.Vector2 position)
    {
        var currentPos = new System.Numerics.Vector2(position.X, position.Y);
        var delta = currentPos - _lastMousePos;
        _lastMousePos = currentPos;

        // Skip if ImGui wants mouse input
        if (ImGuiNET.ImGui.GetIO().WantCaptureMouse)
            return;

        if (_leftMouseDown)
        {
            // Orbit: rotate camera around the target
            _camera.ProcessMouseOrbit(delta.X, delta.Y);
        }
        else if (_rightMouseDown)
        {
            // Pan: translate camera laterally
            _camera.ProcessMousePan(delta.X, delta.Y);
        }
    }

    private void OnScroll(IMouse mouse, ScrollWheel scroll)
    {
        // Skip if ImGui wants mouse input
        if (ImGuiNET.ImGui.GetIO().WantCaptureMouse)
            return;

        // Zoom: move camera closer/further from target
        _camera.ProcessMouseZoom(scroll.Y);
    }

    public void Update(float deltaTime)
    {
        _camera.Update(deltaTime);
    }
}
