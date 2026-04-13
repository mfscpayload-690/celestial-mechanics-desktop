using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CelestialMechanics.Desktop.Core;
using CelestialMechanics.Desktop.Infrastructure.Security;
using CelestialMechanics.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CelestialMechanics.Desktop.Views;

/// <summary>
/// Mode Select screen — presented after the splash. Shows three mode cards.
/// Clicking LAUNCH on Simulation navigates to the project creation flow
/// before entering the simulation IDE.
/// </summary>
public partial class ModeSelectWindow : Window
{
    private readonly ModeSelectViewModel _vm;

    public ModeSelectWindow()
    {
        InitializeComponent();

        _vm = new ModeSelectViewModel();
        DataContext = _vm;

        _vm.SimulationLaunched += OnSimulationLaunched;
        _vm.ExitConfirmed += () => Application.Current.Shutdown();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Load nebula background image (with graceful fallback)
        LoadNebulaBackground();

        // Apply letter-spacing to top bar title (+2px)
        ApplyLetterSpacing(TopBarTitle, 2.0);

        // Play window fade-in
        try
        {
            var sb = (Storyboard)Resources["WindowFadeIn"];
            sb.Begin(this);
        }
        catch
        {
            // If storyboard fails, just force visible
            RootGrid.Opacity = 1;
        }

        // Wire card hover border color transitions (can't do ColorAnimation in XAML EventTriggers easily)
        WireCardHover(SimCard, FindResource("BorderAccentBrush") as Brush, FindResource("BgSurfaceHoverBrush") as Brush);
        WireCardHover(ExitCard, FindResource("StatusRedBrush") as Brush, FindResource("BgSurfaceHoverBrush") as Brush);

        // Wire exit card click (whole card is clickable)
        ExitCard.MouseLeftButtonUp += (_, _) => _vm.ExitAppCommand.Execute(null);

        // Wire email click
        EmailLink.MouseLeftButtonUp += (_, _) => _vm.CopyEmailCommand.Execute(null);
    }

    /// <summary>
    /// Wire hover border/background transitions for mode cards.
    /// </summary>
    private void WireCardHover(Border card, Brush? hoverBorder, Brush? hoverBg)
    {
        var defaultBorder = card.BorderBrush;
        var defaultBg = card.Background;

        card.MouseEnter += (_, _) =>
        {
            if (hoverBorder != null) card.BorderBrush = hoverBorder;
            if (hoverBg != null) card.Background = hoverBg;
        };
        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = defaultBorder;
            card.Background = defaultBg;
        };
    }

    /// <summary>
    /// Simulates letter-spacing by decomposing text into individual Runs
    /// with transparent spacer Runs between them.
    /// </summary>
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

    /// <summary>
    /// Loads the nebula background image. If the file is missing,
    /// the XAML fallback RadialGradientBrush is already visible.
    /// </summary>
    private void LoadNebulaBackground()
    {
        var imagePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Assets", "nebula_bg.png");

        // Also try .jpg extension
        if (!File.Exists(imagePath))
            imagePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Assets", "nebula_bg.jpg");

        if (File.Exists(imagePath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                NebulaImage.Source = bitmap;
            }
            catch
            {
                // Fallback gradient already showing — no action needed
            }
        }
    }

    /// <summary>
    /// When LAUNCH is clicked, open the existing MainWindow (which hosts
    /// the project creation flow → simulation IDE).
    /// </summary>
    private void OnSimulationLaunched()
    {
        try
        {
            var appState = App.Services.GetRequiredService<AppState>();
            appState.SetMode(AppMode.Home);
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
}
