using System.ComponentModel;
using CelestialMechanics.Desktop.Services;

namespace CelestialMechanics.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly NavigationService _navigationService;
    private object _currentView;

    public object CurrentView
    {
        get => _currentView;
        private set
        {
            if (ReferenceEquals(_currentView, value))
            {
                return;
            }

            _currentView = value;
            OnPropertyChanged(nameof(CurrentView));
        }
    }

    public MainWindowViewModel(NavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.ViewChanged += OnViewChanged;

        _currentView = _navigationService.CurrentView;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnViewChanged(object view)
    {
        CurrentView = view;
    }

    private void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
