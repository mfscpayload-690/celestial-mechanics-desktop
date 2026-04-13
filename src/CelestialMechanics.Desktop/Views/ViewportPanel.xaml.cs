using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CelestialMechanics.Desktop.Infrastructure;
using CelestialMechanics.Desktop.Services;
using CelestialMechanics.Desktop.ViewModels;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Renderer;

namespace CelestialMechanics.Desktop.Views;

public partial class ViewportPanel : UserControl
{
    private const double VelocityScale = 0.5;

    private readonly Dictionary<int, Point> _screenCenters = new();
    private readonly Dictionary<int, double> _screenRadii = new();

    private GLRenderer? _renderer;
    private SimulationService? _simService;
    private bool _isInitialized;

    private Point _screenCenter;
    private double _pixelsPerWorldUnit = 1.0;
    private double _halfExtentWorld = 1.0;

    public MainWindowViewModel? ViewModel { get; set; }
    public RenderLoop RenderLoop { get; }

    public ViewportPanel()
    {
        InitializeComponent();

        RenderLoop = new RenderLoop(RenderFrame, Dispatcher);

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += OnMouseRightButtonDown;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void Initialize(GLRenderer renderer, SimulationService simService)
    {
        _renderer = renderer;
        _simService = simService;
        _isInitialized = true;

        Focus();
        RenderLoop.Start();
    }

    public void Shutdown()
    {
        _isInitialized = false;
        RenderLoop.Stop();
        ViewportCanvas.Children.Clear();
        _screenCenters.Clear();
        _screenRadii.Clear();
    }

    private void RenderFrame(double deltaSeconds)
    {
        if (!_isInitialized || _simService == null)
        {
            return;
        }

        var bodies = _simService.GetBodies();

        if (ActualWidth < 4 || ActualHeight < 4)
        {
            return;
        }

        ComputeWorldToScreenTransform(bodies);
        DrawFrame(bodies);

        if (ViewModel != null)
        {
            SyncRendererPlacementPreview(ViewModel);
        }
    }

    private void ComputeWorldToScreenTransform(PhysicsBody[] bodies)
    {
        _screenCenter = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

        double maxRadius = 1.0;
        foreach (var body in bodies)
        {
            if (!body.IsActive)
            {
                continue;
            }

            double extent = System.Math.Max(System.Math.Abs(body.Position.X), System.Math.Abs(body.Position.Y)) + body.Radius * 2.0;
            maxRadius = System.Math.Max(maxRadius, extent);
        }

        _halfExtentWorld = maxRadius * 1.25;
        double usableX = System.Math.Max(ActualWidth * 0.45, 1.0);
        double usableY = System.Math.Max(ActualHeight * 0.45, 1.0);
        _pixelsPerWorldUnit = System.Math.Min(usableX / _halfExtentWorld, usableY / _halfExtentWorld);
    }

    private void DrawFrame(PhysicsBody[] bodies)
    {
        ViewportCanvas.Children.Clear();
        _screenCenters.Clear();
        _screenRadii.Clear();

        if (ViewModel?.ShowGrid != false)
        {
            DrawGrid();
        }

        int? selectedBodyId = GetSelectedBodyId();

        foreach (var body in bodies)
        {
            if (!body.IsActive)
            {
                continue;
            }

            DrawBody(body, selectedBodyId == body.Id);

            if (ViewModel?.ShowVelocityArrows == true)
            {
                DrawVelocityArrow(body);
            }
        }

        if (ViewModel != null)
        {
            DrawPlacementOverlay(ViewModel);
        }
    }

    private void DrawGrid()
    {
        const int linesPerSide = 10;
        double step = _halfExtentWorld / linesPerSide;

        for (int i = -linesPerSide; i <= linesPerSide; i++)
        {
            double xWorld = i * step;
            var top = WorldToScreen(xWorld, _halfExtentWorld);
            var bottom = WorldToScreen(xWorld, -_halfExtentWorld);

            var vLine = new Line
            {
                X1 = top.X,
                Y1 = top.Y,
                X2 = bottom.X,
                Y2 = bottom.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(28, 130, 150, 175)),
                StrokeThickness = i == 0 ? 1.4 : 1.0,
                SnapsToDevicePixels = true,
            };
            ViewportCanvas.Children.Add(vLine);

            double yWorld = i * step;
            var left = WorldToScreen(-_halfExtentWorld, yWorld);
            var right = WorldToScreen(_halfExtentWorld, yWorld);

            var hLine = new Line
            {
                X1 = left.X,
                Y1 = left.Y,
                X2 = right.X,
                Y2 = right.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(28, 130, 150, 175)),
                StrokeThickness = i == 0 ? 1.4 : 1.0,
                SnapsToDevicePixels = true,
            };
            ViewportCanvas.Children.Add(hLine);
        }
    }

    private void DrawBody(PhysicsBody body, bool isSelected)
    {
        var center = WorldToScreen(body.Position.X, body.Position.Y);
        double radiusPx = System.Math.Max(2.0, body.Radius * _pixelsPerWorldUnit);

        var ellipse = new Ellipse
        {
            Width = radiusPx * 2.0,
            Height = radiusPx * 2.0,
            Fill = BodyBrush(body.Type),
            Stroke = isSelected ? new SolidColorBrush(Color.FromRgb(235, 250, 173)) : new SolidColorBrush(Color.FromArgb(90, 20, 20, 20)),
            StrokeThickness = isSelected ? 2.0 : 0.8,
        };

        Canvas.SetLeft(ellipse, center.X - radiusPx);
        Canvas.SetTop(ellipse, center.Y - radiusPx);

        ViewportCanvas.Children.Add(ellipse);
        _screenCenters[body.Id] = center;
        _screenRadii[body.Id] = radiusPx;
    }

    private void DrawVelocityArrow(PhysicsBody body)
    {
        var velocityLength = body.Velocity.Length;
        if (velocityLength < 1e-6)
        {
            return;
        }

        double endX = body.Position.X + (body.Velocity.X * 0.25);
        double endY = body.Position.Y + (body.Velocity.Y * 0.25);

        var start = WorldToScreen(body.Position.X, body.Position.Y);
        var end = WorldToScreen(endX, endY);

        var line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush(Color.FromArgb(180, 0, 230, 130)),
            StrokeThickness = 1.2,
        };

        ViewportCanvas.Children.Add(line);
    }

    private void DrawPlacementOverlay(MainWindowViewModel vm)
    {
        if (!vm.IsAddMode || vm.PlacementPhase == PlacementPhase.Inactive)
        {
            return;
        }

        if (vm.PlacementPhase == PlacementPhase.ChoosingPosition)
        {
            DrawGhostBody(vm.GhostX, vm.GhostY, vm.SelectedBodyType, 0.45, dashed: true);
            return;
        }

        DrawGhostBody(vm.PlacedX, vm.PlacedY, vm.SelectedBodyType, 0.95, dashed: false);

        var start = WorldToScreen(vm.PlacedX, vm.PlacedY);
        var end = WorldToScreen(vm.VelocityEndX, vm.VelocityEndY);

        var velocityLine = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush(Color.FromArgb(220, 61, 233, 178)),
            StrokeThickness = 2.0,
            StrokeDashArray = new DoubleCollection { 3, 2 },
        };
        ViewportCanvas.Children.Add(velocityLine);

        var velocity = new Vec3d(
            (vm.VelocityEndX - vm.PlacedX) * VelocityScale,
            (vm.VelocityEndY - vm.PlacedY) * VelocityScale,
            (vm.VelocityEndZ - vm.PlacedZ) * VelocityScale);

        var preview = vm.ComputeTrajectoryPreview(
            new Vec3d(vm.PlacedX, vm.PlacedY, vm.PlacedZ),
            velocity,
            36);

        var points = new PointCollection(preview
            .Select(p => WorldToScreen(p.X, p.Y))
            .ToList());

        if (points.Count > 1)
        {
            var polyline = new Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 0, 212, 255)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 2, 2 },
            };
            ViewportCanvas.Children.Add(polyline);
        }
    }

    private void DrawGhostBody(double x, double y, BodyType bodyType, double alpha, bool dashed)
    {
        var center = WorldToScreen(x, y);
        double radiusPx = System.Math.Max(2.5, EstimateBodyRadius(bodyType) * _pixelsPerWorldUnit);

        byte fillAlpha = (byte)System.Math.Clamp(alpha * 150.0, 0, 255);
        byte strokeAlpha = (byte)System.Math.Clamp(alpha * 255.0, 0, 255);

        var fillColor = ((SolidColorBrush)BodyBrush(bodyType)).Color;

        var ghost = new Ellipse
        {
            Width = radiusPx * 2,
            Height = radiusPx * 2,
            Fill = new SolidColorBrush(Color.FromArgb(fillAlpha, fillColor.R, fillColor.G, fillColor.B)),
            Stroke = new SolidColorBrush(Color.FromArgb(strokeAlpha, 115, 229, 255)),
            StrokeThickness = 1.8,
        };

        if (dashed)
        {
            ghost.StrokeDashArray = new DoubleCollection { 3, 2 };
        }

        Canvas.SetLeft(ghost, center.X - radiusPx);
        Canvas.SetTop(ghost, center.Y - radiusPx);
        ViewportCanvas.Children.Add(ghost);
    }

    private void SyncRendererPlacementPreview(MainWindowViewModel vm)
    {
        if (_renderer == null)
        {
            return;
        }

        if (!vm.IsAddMode || vm.PlacementPhase == PlacementPhase.Inactive)
        {
            _renderer.ClearPlacementPreview();
            return;
        }

        if (vm.PlacementPhase == PlacementPhase.ChoosingPosition)
        {
            _renderer.SetPlacementPreview(
                CreateGhostBody(vm.GhostX, vm.GhostY, vm.GhostZ, vm.SelectedBodyType, 0.45f),
                null,
                null,
                null);
            return;
        }

        var velocity = new Vec3d(
            (vm.VelocityEndX - vm.PlacedX) * VelocityScale,
            (vm.VelocityEndY - vm.PlacedY) * VelocityScale,
            (vm.VelocityEndZ - vm.PlacedZ) * VelocityScale);

        var preview = vm.ComputeTrajectoryPreview(
            new Vec3d(vm.PlacedX, vm.PlacedY, vm.PlacedZ),
            velocity,
            36)
            .Select(p => new System.Numerics.Vector3(p.X, p.Y, p.Z))
            .ToList();

        _renderer.SetPlacementPreview(
            CreateGhostBody(vm.PlacedX, vm.PlacedY, vm.PlacedZ, vm.SelectedBodyType, 0.95f),
            new System.Numerics.Vector3(vm.PlacedX, vm.PlacedY, vm.PlacedZ),
            new System.Numerics.Vector3(vm.VelocityEndX, vm.VelocityEndY, vm.VelocityEndZ),
            preview);
    }

    private RenderBody CreateGhostBody(float x, float y, float z, BodyType bodyType, float alpha)
    {
        var brush = (SolidColorBrush)BodyBrush(bodyType);
        var color = brush.Color;

        return new RenderBody
        {
            Id = -1,
            Position = new System.Numerics.Vector3(x, y, z),
            Radius = (float)EstimateBodyRadius(bodyType),
            Color = new System.Numerics.Vector4(color.R / 255f, color.G / 255f, color.B / 255f, alpha),
            BodyType = (int)bodyType,
            VisualParams = new System.Numerics.Vector4(1f, 0.25f, 0.18f, 0.2f),
            TextureLayer = 0,
            StarTemperatureK = bodyType == BodyType.Star ? 5772f : 0f,
            IsSelected = false,
        };
    }

    private int? HitTestBody(Point point)
    {
        int? bestId = null;
        double bestDistance = double.MaxValue;

        foreach (var (id, center) in _screenCenters)
        {
            double radius = _screenRadii.TryGetValue(id, out var r) ? r : 6.0;
            double maxHitDistance = radius + 8.0;
            double dist = (center - point).Length;

            if (dist <= maxHitDistance && dist < bestDistance)
            {
                bestDistance = dist;
                bestId = id;
            }
        }

        return bestId;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel == null || !ViewModel.IsAddMode)
        {
            return;
        }

        var (worldX, worldY) = ScreenToWorld(e.GetPosition(ViewportCanvas));

        if (ViewModel.PlacementPhase == PlacementPhase.ChoosingPosition)
        {
            ViewModel.UpdateGhostPosition((float)worldX, (float)worldY, 0f);
        }
        else if (ViewModel.PlacementPhase == PlacementPhase.ChoosingVelocity)
        {
            ViewModel.UpdateVelocityEndpoint((float)worldX, (float)worldY, 0f);
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();

        if (ViewModel == null)
        {
            return;
        }

        var mousePos = e.GetPosition(ViewportCanvas);
        var (worldX, worldY) = ScreenToWorld(mousePos);

        if (ViewModel.IsAddMode)
        {
            if (ViewModel.PlacementPhase == PlacementPhase.ChoosingPosition)
            {
                ViewModel.UpdateGhostPosition((float)worldX, (float)worldY, 0f);
                ViewModel.ConfirmPosition();
            }
            else if (ViewModel.PlacementPhase == PlacementPhase.ChoosingVelocity)
            {
                ViewModel.UpdateVelocityEndpoint((float)worldX, (float)worldY, 0f);
                ViewModel.ConfirmVelocityAndPlace();
            }

            e.Handled = true;
            return;
        }

        var bodyId = HitTestBody(mousePos);
        if (bodyId.HasValue)
        {
            ViewModel.SelectBodyById(bodyId.Value);
        }
        else
        {
            ViewModel.DeselectBody();
        }
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.IsAddMode == true && ViewModel.PlacementPhase == PlacementPhase.ChoosingVelocity)
        {
            ViewModel.CancelVelocityPhase();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            ViewModel.CancelPlacement();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && ViewModel.IsAddMode && ViewModel.PlacementPhase == PlacementPhase.ChoosingVelocity)
        {
            ViewModel.PlaceWithZeroVelocity();
            e.Handled = true;
        }
    }

    private Point WorldToScreen(double worldX, double worldY)
    {
        return new Point(
            _screenCenter.X + worldX * _pixelsPerWorldUnit,
            _screenCenter.Y - worldY * _pixelsPerWorldUnit);
    }

    private (double x, double y) ScreenToWorld(Point screen)
    {
        double x = (screen.X - _screenCenter.X) / _pixelsPerWorldUnit;
        double y = -((screen.Y - _screenCenter.Y) / _pixelsPerWorldUnit);
        return (x, y);
    }

    private int? GetSelectedBodyId()
    {
        if (ViewModel == null)
        {
            return null;
        }

        var selectedNodeId = ViewModel.SceneService.SelectionManager.SelectedEntity;
        if (!selectedNodeId.HasValue)
        {
            return null;
        }

        return ViewModel.SceneService.GetBodyIdForNode(selectedNodeId.Value);
    }

    private static double EstimateBodyRadius(BodyType type) => type switch
    {
        BodyType.Star => 0.10,
        BodyType.Planet => 0.03,
        BodyType.GasGiant => 0.05,
        BodyType.RockyPlanet => 0.02,
        BodyType.Moon => 0.01,
        BodyType.Asteroid => 0.005,
        BodyType.NeutronStar => 0.02,
        BodyType.BlackHole => 0.08,
        BodyType.Comet => 0.003,
        _ => 0.04,
    };

    private static Brush BodyBrush(BodyType type) => type switch
    {
        BodyType.Star => new SolidColorBrush(Color.FromRgb(255, 203, 82)),
        BodyType.Planet => new SolidColorBrush(Color.FromRgb(82, 140, 255)),
        BodyType.GasGiant => new SolidColorBrush(Color.FromRgb(208, 164, 102)),
        BodyType.RockyPlanet => new SolidColorBrush(Color.FromRgb(170, 112, 84)),
        BodyType.Moon => new SolidColorBrush(Color.FromRgb(173, 182, 194)),
        BodyType.Asteroid => new SolidColorBrush(Color.FromRgb(119, 127, 132)),
        BodyType.NeutronStar => new SolidColorBrush(Color.FromRgb(122, 214, 255)),
        BodyType.BlackHole => new SolidColorBrush(Color.FromRgb(30, 34, 45)),
        BodyType.Comet => new SolidColorBrush(Color.FromRgb(95, 201, 204)),
        _ => new SolidColorBrush(Color.FromRgb(190, 190, 190)),
    };
}
