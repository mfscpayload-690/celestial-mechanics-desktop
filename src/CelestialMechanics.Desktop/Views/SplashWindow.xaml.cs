using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CelestialMechanics.Desktop.Infrastructure.Security;

namespace CelestialMechanics.Desktop.Views;

/// <summary>
/// Splash screen window. Displays for ~5 seconds with procedural starfield
/// and staggered title/tagline animations, then transitions to Mode Select.
/// All animations run on the WPF animation system — no Thread.Sleep.
/// </summary>
public partial class SplashWindow : Window
{
    // Fixed seed for deterministic star layout across launches
    private static readonly Random _rng = new(42);

    // TextPrimary color for stars — pulled from design token
    private static readonly Color _starColor = Color.FromRgb(232, 236, 244); // #E8ECF4

    public SplashWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 1. Apply letter-spacing (WPF has no XAML CharacterSpacing property)
        ApplyLetterSpacing(TitleBlock, 4.0);
        ApplyLetterSpacing(TaglineBlock, 2.0);

        // 2. Scatter procedural stars (pre-populated before storyboard)
        ScatterStars(120);

        // 3. Start the orchestrated storyboard
        var sb = (Storyboard)Resources["SplashStoryboard"];
        sb.Completed += (_, _) => TransitionToModeSelect();
        sb.Begin(this);
    }

    /// <summary>
    /// Simulates letter-spacing by setting TextBlock.CharacterSpacing
    /// (available in .NET 8 WPF via Typography). Falls back to manual
    /// character spacing via TextBlock property if available.
    /// </summary>
    private static void ApplyLetterSpacing(TextBlock textBlock, double spacingPx)
    {
        // WPF TextBlock doesn't have a built-in CharacterSpacing property.
        // We approximate by wrapping each character in a Run with trailing spacing.
        // For splash screen, this is acceptable since the text is static.
        string text = textBlock.Text;
        textBlock.Text = null;
        textBlock.Inlines.Clear();

        for (int i = 0; i < text.Length; i++)
        {
            var run = new System.Windows.Documents.Run(text[i].ToString())
            {
                FontSize = textBlock.FontSize,
                FontWeight = textBlock.FontWeight,
                FontFamily = textBlock.FontFamily,
                Foreground = textBlock.Foreground,
            };

            textBlock.Inlines.Add(run);

            // Add spacing after each character except the last
            if (i < text.Length - 1)
            {
                var spacer = new System.Windows.Documents.Run(" ")
                {
                    FontSize = spacingPx * 0.8, // Approximate px spacing via font size
                    Foreground = Brushes.Transparent,
                };
                textBlock.Inlines.Add(spacer);
            }
        }
    }

    /// <summary>
    /// Populates the starfield canvas with 120 small twinkling ellipses.
    /// Uses a fixed seed for deterministic star placement.
    /// Each star gets an independent sinusoidal opacity animation (3–5s period).
    /// </summary>
    private void ScatterStars(int count)
    {
        double w = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        double h = ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight;

        for (int i = 0; i < count; i++)
        {
            double radius = 0.5 + _rng.NextDouble() * 1.5; // 1–2px radius
            double baseOpacity = 0.1 + _rng.NextDouble() * 0.5; // 0.1–0.6

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

            // Twinkling animation — staggered start, 3–5s period, sinusoidal
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

    /// <summary>
    /// Opens the Mode Select window and closes this splash.
    /// </summary>
    private async void TransitionToModeSelect()
    {
        try
        {
            var modeSelect = new ModeSelectWindow();
            modeSelect.Show();

            // Wait for ModeSelectWindow's fade-in to start rendering
            // before destroying the splash — prevents brief desktop flash
            await Task.Delay(500);

            modeSelect.Activate();
            modeSelect.Focus();

            // Update application MainWindow reference before closing splash
            Application.Current.MainWindow = modeSelect;

            Close();
        }
        catch (Exception ex)
        {
            CrashLogger.WriteLog(ex);
            MessageBox.Show(
                $"Failed to open Mode Select window.\n\n" +
                $"Error: {ex.Message}\n" +
                $"Reference: {CrashLogger.LastErrorId}\n\n" +
                $"The application will now close.",
                "Celestial Mechanics — Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown(1);
        }
    }
}
