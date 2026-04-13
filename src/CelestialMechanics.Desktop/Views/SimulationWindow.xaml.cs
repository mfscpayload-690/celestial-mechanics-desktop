using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CelestialMechanics.Desktop.Core;
using CelestialMechanics.Desktop.Infrastructure.Security;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.Services;
using CelestialMechanics.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CelestialMechanics.Desktop.Views;

/// <summary>
/// Lightweight overlay host window.
/// Shows Welcome → Tutorial (first-time) → Project Hub overlays,
/// then opens MainWindow (the existing simulation backend) with the selected project.
/// No IDE shell — just a starfield backdrop + overlay screens.
/// </summary>
public partial class SimulationWindow : Window
{
    private TutorialViewModel? _tutorialVm;
    private ProjectHubViewModel? _projectHubVm;

    // Fixed seed for deterministic star layout across launches
    private static readonly Random _rng = new(42);
    private static readonly Color _starColor = Color.FromRgb(232, 236, 244); // #E8ECF4

    public SimulationWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Scatter procedural twinkling stars for visual backdrop
        ScatterStars(120);

        // Wire welcome overlay completion
        WelcomeOverlayControl.AnimationCompleted += OnWelcomeComplete;
    }

    /// <summary>
    /// Called when the 3.5s welcome moment finishes.
    /// First-time users see tutorial, returning users go to Project Hub.
    /// </summary>
    private async void OnWelcomeComplete()
    {
        WelcomeLayer.Visibility = Visibility.Collapsed;

        bool showTutorial = await TutorialViewModel.ShouldShowTutorial();
        if (showTutorial)
        {
            ShowTutorial();
        }
        else
        {
            ShowProjectHub();
        }
    }

    /// <summary>
    /// Shows the tutorial overlay (first-time users only).
    /// </summary>
    private void ShowTutorial()
    {
        _tutorialVm = new TutorialViewModel();
        _tutorialVm.TutorialCompleted += OnTutorialDismissed;
        _tutorialVm.TutorialSkipped += OnTutorialDismissed;

        TutorialOverlayControl.DataContext = _tutorialVm;
        TutorialLayer.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Called when the tutorial is completed or skipped.
    /// Transitions to the Project Hub.
    /// </summary>
    private void OnTutorialDismissed()
    {
        TutorialLayer.Visibility = Visibility.Collapsed;

        if (_tutorialVm != null)
        {
            _tutorialVm.TutorialCompleted -= OnTutorialDismissed;
            _tutorialVm.TutorialSkipped -= OnTutorialDismissed;
            _tutorialVm = null;
        }

        ShowProjectHub();
    }

    /// <summary>
    /// Displays the Project Hub overlay for project selection or creation.
    /// </summary>
    private void ShowProjectHub()
    {
        var projectService = App.Services.GetRequiredService<ProjectService>();
        _projectHubVm = new ProjectHubViewModel(projectService);
        _projectHubVm.ProjectSelected += OnProjectSelectedFromHub;
        _projectHubVm.ExitRequested += () => Application.Current.Shutdown();

        _projectHubVm.RefreshProjects();

        ProjectHubControl.DataContext = _projectHubVm;
        ProjectHubLayer.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Called when a project is selected or created from the Project Hub.
    /// Opens the existing MainWindow backend with the selected project.
    /// </summary>
    private void OnProjectSelectedFromHub(ProjectInfo project)
    {
        ProjectHubLayer.Visibility = Visibility.Collapsed;

        if (_projectHubVm != null)
        {
            _projectHubVm.ProjectSelected -= OnProjectSelectedFromHub;
            _projectHubVm = null;
        }

        try
        {
            var projectService = App.Services.GetRequiredService<ProjectService>();
            projectService.SetCurrentProject(project);

            var appState = App.Services.GetRequiredService<AppState>();
            appState.SetMode(AppMode.Simulation);

            Close();
        }
        catch (Exception ex)
        {
            CrashLogger.WriteLog(ex);
            MessageBox.Show(
                $"Failed to launch Simulation IDE.\n\n" +
                $"Error: {ex.Message}\n" +
                $"Reference: {CrashLogger.LastErrorId}",
                "Celestial Mechanics — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Populates the starfield backdrop with twinkling ellipses.
    /// Uses a fixed seed for deterministic star placement.
    /// </summary>
    private void ScatterStars(int count)
    {
        double w = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        double h = ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight;

        for (int i = 0; i < count; i++)
        {
            double radius = 0.5 + _rng.NextDouble() * 1.5;
            double baseOpacity = 0.1 + _rng.NextDouble() * 0.5;

            var star = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(Color.FromArgb(
                    (byte)(baseOpacity * 255),
                    _starColor.R, _starColor.G, _starColor.B)),
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(star, _rng.NextDouble() * w);
            Canvas.SetTop(star, _rng.NextDouble() * h);
            StarCanvas.Children.Add(star);

            var twinkle = new DoubleAnimation
            {
                From = 0.2,
                To = 0.7,
                Duration = TimeSpan.FromSeconds(3 + _rng.NextDouble() * 2),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(_rng.NextDouble() * 3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            star.BeginAnimation(UIElement.OpacityProperty, twinkle);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}
