using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.Services;
using CelestialMechanics.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CelestialMechanics.Desktop.Views;

public partial class HomeView : UserControl
{
    private readonly ProjectService _projectService;
    private readonly SimulationPanelLauncher _simulationPanelLauncher;
    private readonly ModeSelectViewModel _modeVm;
    private bool _isLaunchingSimulation;

    private TutorialViewModel? _tutorialVm;
    private ProjectHubViewModel? _projectHubVm;
    private WelcomeOverlay? _welcomeOverlay;
    private TutorialOverlay? _tutorialOverlay;
    private ProjectHubOverlay? _projectHubOverlay;

    private static readonly Random Rng = new(42);
    private static readonly Color StarColor = Color.FromRgb(232, 236, 244);

    public HomeView()
    {
        InitializeComponent();

        _projectService = App.Services.GetRequiredService<ProjectService>();
        _simulationPanelLauncher = App.Services.GetRequiredService<SimulationPanelLauncher>();

        _modeVm = new ModeSelectViewModel();
        ModeLayer.DataContext = _modeVm;

        _modeVm.SimulationLaunched += OnSimulationLaunched;
        _modeVm.ExitConfirmed += () => Application.Current.Shutdown();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLetterSpacing(SplashTitleBlock, 4.0);
        ApplyLetterSpacing(SplashTaglineBlock, 2.0);
        ApplyLetterSpacing(TopBarTitle, 2.0);

        LoadNebulaBackground();

        WireCardHover(SimCard, FindResource("BorderAccentBrush") as Brush, FindResource("BgSurfaceHoverBrush") as Brush);
        WireCardHover(ExitCard, FindResource("StatusRedBrush") as Brush, FindResource("BgSurfaceHoverBrush") as Brush);

        ExitCard.MouseLeftButtonUp += OnExitCardClicked;
        EmailLink.MouseLeftButtonUp += OnEmailLinkClicked;

        SplashStarCanvas.Children.Clear();
        ScatterStars(SplashStarCanvas, 120);

        StartSplashSequence();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _modeVm.SimulationLaunched -= OnSimulationLaunched;

        ExitCard.MouseLeftButtonUp -= OnExitCardClicked;
        EmailLink.MouseLeftButtonUp -= OnEmailLinkClicked;

        if (_welcomeOverlay != null)
        {
            _welcomeOverlay.AnimationCompleted -= OnWelcomeComplete;
        }

        if (_tutorialVm != null)
        {
            _tutorialVm.TutorialCompleted -= OnTutorialDismissed;
            _tutorialVm.TutorialSkipped -= OnTutorialDismissed;
            _tutorialVm = null;
        }

        if (_projectHubVm != null)
        {
            _projectHubVm.ProjectSelected -= OnProjectSelected;
            _projectHubVm.ExitRequested -= OnProjectHubExitRequested;
            _projectHubVm = null;
        }
    }

    private void StartSplashSequence()
    {
        SplashLayer.Visibility = Visibility.Visible;
        SplashLayer.Opacity = 1;
        ModeLayer.Visibility = Visibility.Collapsed;
        ModeLayer.Opacity = 0;
        LaunchLayer.Visibility = Visibility.Collapsed;

        var splash = (Storyboard)Resources["SplashStoryboard"];
        splash.Completed -= OnSplashCompleted;
        splash.Completed += OnSplashCompleted;
        splash.Begin(this, true);
    }

    private void OnSplashCompleted(object? sender, EventArgs e)
    {
        SplashLayer.Visibility = Visibility.Collapsed;
        ModeLayer.Visibility = Visibility.Visible;

        var fadeIn = (Storyboard)Resources["ModeFadeIn"];
        fadeIn.Begin(this, true);
    }

    private void OnSimulationLaunched()
    {
        ModeLayer.Visibility = Visibility.Collapsed;
        LaunchLayer.Visibility = Visibility.Visible;

        LaunchStarCanvas.Children.Clear();
        ScatterStars(LaunchStarCanvas, 120);

        ShowWelcomeOverlay();
    }

    private void ShowWelcomeOverlay()
    {
        WelcomeLayer.Children.Clear();
        TutorialLayer.Children.Clear();
        ProjectHubLayer.Children.Clear();

        WelcomeLayer.Visibility = Visibility.Visible;
        TutorialLayer.Visibility = Visibility.Collapsed;
        ProjectHubLayer.Visibility = Visibility.Collapsed;

        _welcomeOverlay = new WelcomeOverlay();
        _welcomeOverlay.AnimationCompleted += OnWelcomeComplete;
        WelcomeLayer.Children.Add(_welcomeOverlay);
    }

    private async void OnWelcomeComplete()
    {
        WelcomeLayer.Visibility = Visibility.Collapsed;

        bool showTutorial = await TutorialViewModel.ShouldShowTutorial();
        if (showTutorial)
        {
            ShowTutorialOverlay();
        }
        else
        {
            ShowProjectHubOverlay();
        }
    }

    private void ShowTutorialOverlay()
    {
        TutorialLayer.Children.Clear();
        TutorialLayer.Visibility = Visibility.Visible;

        _tutorialVm = new TutorialViewModel();
        _tutorialVm.TutorialCompleted += OnTutorialDismissed;
        _tutorialVm.TutorialSkipped += OnTutorialDismissed;

        _tutorialOverlay = new TutorialOverlay
        {
            DataContext = _tutorialVm,
        };

        TutorialLayer.Children.Add(_tutorialOverlay);
    }

    private void OnTutorialDismissed()
    {
        TutorialLayer.Visibility = Visibility.Collapsed;
        TutorialLayer.Children.Clear();

        if (_tutorialVm != null)
        {
            _tutorialVm.TutorialCompleted -= OnTutorialDismissed;
            _tutorialVm.TutorialSkipped -= OnTutorialDismissed;
            _tutorialVm = null;
        }

        ShowProjectHubOverlay();
    }

    private void ShowProjectHubOverlay()
    {
        ProjectHubLayer.Children.Clear();
        ProjectHubLayer.Visibility = Visibility.Visible;

        _projectHubVm = new ProjectHubViewModel(_projectService);
        _projectHubVm.ProjectSelected += OnProjectSelected;
        _projectHubVm.ExitRequested += OnProjectHubExitRequested;
        _projectHubVm.RefreshProjects();

        _projectHubOverlay = new ProjectHubOverlay
        {
            DataContext = _projectHubVm,
        };

        ProjectHubLayer.Children.Add(_projectHubOverlay);
    }

    private async void OnProjectSelected(ProjectInfo project)
    {
        if (_isLaunchingSimulation)
        {
            return;
        }

        _isLaunchingSimulation = true;
        ShowLaunchTransition();
        _projectService.SetCurrentProject(project);

        var launchResult = await _simulationPanelLauncher.TryLaunchAsync(project);
        if (!launchResult.Success)
        {
            HideLaunchTransition();
            _isLaunchingSimulation = false;
            MessageBox.Show(
                launchResult.Error ?? "Failed to launch the simulation panel.",
                "Celestial Mechanics — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        await Task.Delay(250);

        bool simulationReady = await WaitForSimulationWindowAsync(launchResult.Process, TimeSpan.FromSeconds(8));
        if (!simulationReady)
        {
            if (launchResult.Process is { HasExited: true })
            {
                HideLaunchTransition();
                _isLaunchingSimulation = false;
                MessageBox.Show(
                    "Simulation process exited during startup.",
                    "Celestial Mechanics — Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        await Task.Delay(300);

        Application.Current.Shutdown();
    }

    private static void OnProjectHubExitRequested()
    {
        Application.Current.Shutdown();
    }

    private void ShowLaunchTransition()
    {
        LaunchTransitionLayer.Visibility = Visibility.Visible;
        ProjectHubLayer.IsHitTestVisible = false;
    }

    private void HideLaunchTransition()
    {
        LaunchTransitionLayer.Visibility = Visibility.Collapsed;
        ProjectHubLayer.IsHitTestVisible = true;
    }

    private static async Task<bool> WaitForSimulationWindowAsync(Process? launchedProcess, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (launchedProcess != null)
            {
                try
                {
                    launchedProcess.Refresh();
                    if (launchedProcess.HasExited)
                    {
                        return false;
                    }

                    if (launchedProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Continue scanning by process name if direct handle check fails.
                }
            }

            var runningApps = Process.GetProcessesByName("CelestialMechanics.App");
            if (runningApps.Any(p => p.MainWindowHandle != IntPtr.Zero))
            {
                return true;
            }

            await Task.Delay(80);
        }

        return false;
    }

    private void OnExitCardClicked(object sender, MouseButtonEventArgs e)
    {
        _modeVm.ExitAppCommand.Execute(null);
    }

    private void OnEmailLinkClicked(object sender, MouseButtonEventArgs e)
    {
        _modeVm.CopyEmailCommand.Execute(null);
    }

    private static void ApplyLetterSpacing(TextBlock textBlock, double spacingPx)
    {
        string text = textBlock.Text;
        var foreground = textBlock.Foreground;
        textBlock.Text = null;
        textBlock.Inlines.Clear();

        for (int i = 0; i < text.Length; i++)
        {
            var run = new System.Windows.Documents.Run(text[i].ToString())
            {
                FontSize = textBlock.FontSize,
                FontWeight = textBlock.FontWeight,
                FontFamily = textBlock.FontFamily,
                Foreground = foreground,
            };
            textBlock.Inlines.Add(run);

            if (i < text.Length - 1)
            {
                var spacer = new System.Windows.Documents.Run(" ")
                {
                    FontSize = spacingPx * 0.8,
                    Foreground = Brushes.Transparent,
                };
                textBlock.Inlines.Add(spacer);
            }
        }
    }

    private void LoadNebulaBackground()
    {
        var imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "nebula_bg.png");
        if (!File.Exists(imagePath))
        {
            imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "nebula_bg.jpg");
        }

        if (!File.Exists(imagePath))
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new System.Uri(imagePath, System.UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            NebulaImage.Source = bitmap;
        }
        catch
        {
            // Fallback gradient remains visible.
        }
    }

    private static void WireCardHover(Border card, Brush? hoverBorder, Brush? hoverBg)
    {
        var defaultBorder = card.BorderBrush;
        var defaultBg = card.Background;

        card.MouseEnter += (_, _) =>
        {
            if (hoverBorder != null)
            {
                card.BorderBrush = hoverBorder;
            }

            if (hoverBg != null)
            {
                card.Background = hoverBg;
            }
        };

        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = defaultBorder;
            card.Background = defaultBg;
        };
    }

    private static void ScatterStars(Canvas canvas, int count)
    {
        double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : SystemParameters.PrimaryScreenWidth;
        double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : SystemParameters.PrimaryScreenHeight;

        for (int i = 0; i < count; i++)
        {
            double radius = 0.5 + Rng.NextDouble() * 1.5;
            double baseOpacity = 0.1 + Rng.NextDouble() * 0.5;

            var star = new System.Windows.Shapes.Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(Color.FromArgb(
                    (byte)(baseOpacity * 255),
                    StarColor.R,
                    StarColor.G,
                    StarColor.B)),
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(star, Rng.NextDouble() * w);
            Canvas.SetTop(star, Rng.NextDouble() * h);
            canvas.Children.Add(star);

            var twinkle = new DoubleAnimation
            {
                From = 0.2,
                To = 0.7,
                Duration = TimeSpan.FromSeconds(3 + Rng.NextDouble() * 2),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(Rng.NextDouble() * 3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };

            star.BeginAnimation(UIElement.OpacityProperty, twinkle);
        }
    }
}
