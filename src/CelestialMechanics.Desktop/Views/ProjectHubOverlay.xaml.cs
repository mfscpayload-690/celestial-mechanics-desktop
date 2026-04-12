using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.ViewModels;

namespace CelestialMechanics.Desktop.Views;

/// <summary>
/// Code-behind for the Project Hub overlay.
/// Handles fade-in animation, project item click routing, and Enter key support.
/// </summary>
public partial class ProjectHubOverlay : UserControl
{
    public ProjectHubOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Play the fade-in storyboard
        try
        {
            var sb = (Storyboard)Resources["FadeInStoryboard"];
            sb.Begin(this);
        }
        catch
        {
            // If storyboard fails, just force visible
            HubRoot.Opacity = 1;
        }

        // Focus the project name input for immediate typing
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ProjectNameInput.Focus();
            Keyboard.Focus(ProjectNameInput);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Handles Enter key in the project name TextBox — creates the project.
    /// </summary>
    public void OnProjectNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ProjectHubViewModel vm && vm.CanCreateProject)
        {
            vm.CreateProjectCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles click on a project item in the list.
    /// Routes to the ViewModel's OpenProjectCommand.
    /// </summary>
    private void OnProjectClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is ProjectInfo project)
        {
            if (DataContext is ProjectHubViewModel vm)
            {
                vm.OpenProjectCommand.Execute(project);
            }
        }
    }
}
