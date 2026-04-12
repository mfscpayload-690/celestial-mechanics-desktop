using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.ViewModels;

namespace CelestialMechanics.Desktop.Views;

/// <summary>
/// Code-behind for MainWindow.
/// Responsibilities: ViewModel creation, OpenGL viewport init, background video playback, and cleanup.
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel _vm = null!;
    private bool _videoAvailable;

    /// <summary>
    /// When set before showing, the window skips its internal navigation
    /// and enters the simulation IDE directly with this project.
    /// </summary>
    public ProjectInfo? ProjectToOpen { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // 1. Create the top-level ViewModel (owns services + child VMs)
        _vm = new MainWindowViewModel(Dispatcher);
        DataContext = _vm;

        // 2. Initialize the OpenGL viewport (requires HwndHost — View concern)
        Viewport.Initialize(_vm.Renderer, _vm.SimService);
        Viewport.ViewModel = _vm;

        // Wire render loop for FPS metrics
        _vm.ActiveRenderLoop = Viewport.RenderLoop;

        // 3. Setup background video playback
        SetupBackgroundVideo();

        // 4. Listen for navigation changes to manage video lifecycle
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // 5. If a project was passed (from SimulationWindow/ProjectHub),
        //    skip internal navigation and enter IDE directly
        if (ProjectToOpen != null)
        {
            try { System.IO.Directory.CreateDirectory(ProjectToOpen.Path); }
            catch { /* non-critical */ }
            _vm.ForceOpenProject(ProjectToOpen);
        }
    }

    /// <summary>
    /// Attempts to load and play the background simulation video.
    /// Falls back gracefully if the video file doesn't exist.
    /// </summary>
    private void SetupBackgroundVideo()
    {
        var videoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Videos", "background_simulation.mp4");

        if (File.Exists(videoPath))
        {
            try
            {
                BackgroundVideo.Source = new Uri(videoPath, UriKind.Absolute);
                BackgroundVideo.MediaEnded += OnBackgroundVideoEnded;
                BackgroundVideo.MediaFailed += OnBackgroundVideoFailed;
                BackgroundVideo.Play();
                _videoAvailable = true;
            }
            catch
            {
                _videoAvailable = false;
            }
        }
        else
        {
            _videoAvailable = false;
            BackgroundVideo.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Loop the video when it reaches the end.</summary>
    private void OnBackgroundVideoEnded(object? sender, RoutedEventArgs e)
    {
        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
    }

    /// <summary>Hide the video element if playback fails.</summary>
    private void OnBackgroundVideoFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _videoAvailable = false;
        BackgroundVideo.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Stop video playback when entering IDE mode, restart when returning to modals.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.NavState)) return;

        if (_vm.IsIdeActive)
        {
            // Entering IDE — stop video to save resources
            if (_videoAvailable)
            {
                BackgroundVideo.Stop();
            }
        }
        else
        {
            // Returning to modals — restart video
            if (_videoAvailable)
            {
                BackgroundVideo.Position = TimeSpan.Zero;
                BackgroundVideo.Play();
            }
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_videoAvailable)
        {
            BackgroundVideo.Stop();
            BackgroundVideo.Close();
        }

        Viewport.Shutdown();
        _vm?.Dispose();

        // With ShutdownMode=OnExplicitShutdown, we must explicitly shut down
        Application.Current.Shutdown();
    }
}
