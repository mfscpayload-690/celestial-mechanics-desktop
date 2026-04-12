using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CelestialMechanics.Desktop.Views;

/// <summary>
/// Code-behind for the tutorial overlay. Manages the orbit animation
/// and step-dependent visual switching.
/// </summary>
public partial class TutorialOverlay : UserControl
{
    private Storyboard? _orbitAnimation;

    public TutorialOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartOrbitAnimation();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ViewModels.TutorialViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.CurrentStep))
                    UpdateVisualForStep(vm.CurrentStep);
            };
            UpdateVisualForStep(vm.CurrentStep);
        }
    }

    private void UpdateVisualForStep(int step)
    {
        // Show integrator table only on step 4
        IntegratorTable.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        OrbitCanvas.Visibility = step != 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartOrbitAnimation()
    {
        // Animate OrbitBody1 in a circle
        var body1AnimX = new DoubleAnimation
        {
            From = 10, To = 62, Duration = TimeSpan.FromSeconds(3),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        var body1AnimY = new DoubleAnimation
        {
            From = 36, To = 36, Duration = TimeSpan.FromSeconds(3),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
        };

        // Simple circular motion via Canvas.Left/Top
        var storyboard = new Storyboard();

        // Body 1: horizontal oscillation
        Storyboard.SetTarget(body1AnimX, OrbitBody1);
        Storyboard.SetTargetProperty(body1AnimX, new PropertyPath(Canvas.LeftProperty));
        storyboard.Children.Add(body1AnimX);

        // Body 1: vertical oscillation (offset)
        var body1Y = new DoubleAnimation
        {
            From = 16, To = 56, Duration = TimeSpan.FromSeconds(3),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            BeginTime = TimeSpan.FromSeconds(0.75)
        };
        Storyboard.SetTarget(body1Y, OrbitBody1);
        Storyboard.SetTargetProperty(body1Y, new PropertyPath(Canvas.TopProperty));
        storyboard.Children.Add(body1Y);

        // Body 2: opposite phase
        var body2X = new DoubleAnimation
        {
            From = 60, To = 12, Duration = TimeSpan.FromSeconds(4),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(body2X, OrbitBody2);
        Storyboard.SetTargetProperty(body2X, new PropertyPath(Canvas.LeftProperty));
        storyboard.Children.Add(body2X);

        var body2Y = new DoubleAnimation
        {
            From = 56, To = 16, Duration = TimeSpan.FromSeconds(4),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            BeginTime = TimeSpan.FromSeconds(1)
        };
        Storyboard.SetTarget(body2Y, OrbitBody2);
        Storyboard.SetTargetProperty(body2Y, new PropertyPath(Canvas.TopProperty));
        storyboard.Children.Add(body2Y);

        _orbitAnimation = storyboard;
        storyboard.Begin();
    }
}
