using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CelestialMechanics.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CelestialMechanics.Desktop.Views;

/// <summary>
/// Hosts the real simulation panel (renderer + simulation services) inside the shell ContentControl.
/// </summary>
public partial class SimulationView : UserControl
{
    private SimulationViewModel? _vm;
    private bool _videoAvailable;

    public SimulationView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = App.Services.GetRequiredService<SimulationViewModel>();
        DataContext = _vm;

        Viewport.Initialize(_vm.Renderer, _vm.SimService);
        Viewport.ViewModel = _vm;
        _vm.ActiveRenderLoop = Viewport.RenderLoop;

        SetupBackgroundVideo();
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.LoadCurrentProjectIfAny();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_videoAvailable)
        {
            BackgroundVideo.Stop();
            BackgroundVideo.Close();
        }

        Viewport.Shutdown();
    }

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

    private void OnBackgroundVideoEnded(object? sender, RoutedEventArgs e)
    {
        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
    }

    private void OnBackgroundVideoFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _videoAvailable = false;
        BackgroundVideo.Visibility = Visibility.Collapsed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null || e.PropertyName != nameof(SimulationViewModel.NavState))
        {
            return;
        }

        if (_vm.IsIdeActive)
        {
            if (_videoAvailable)
            {
                BackgroundVideo.Stop();
            }
        }
        else
        {
            if (_videoAvailable)
            {
                BackgroundVideo.Position = TimeSpan.Zero;
                BackgroundVideo.Play();
            }
        }
    }
}
