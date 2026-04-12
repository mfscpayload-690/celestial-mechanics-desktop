using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CelestialMechanics.Desktop.Views.Panels;

/// <summary>
/// Analysis panel code-behind.
/// Handles collapse/expand toggle with 250ms QuinticEase animation
/// and lightweight Canvas-based energy chart rendering.
/// </summary>
public partial class AnalysisPanel : UserControl
{
    private bool _isCollapsed;
    private double _expandedHeight = 152; // Content area height when expanded

    public AnalysisPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Toggle collapse/expand with 250ms QuinticEase animation.
    /// </summary>
    private void OnToggleCollapse(object sender, MouseButtonEventArgs e)
    {
        _isCollapsed = !_isCollapsed;

        // Animate chevron rotation
        var rotateAnim = new DoubleAnimation
        {
            To = _isCollapsed ? 180 : 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseInOut }
        };
        CollapseRotation.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

        // Animate content height
        if (_isCollapsed)
        {
            _expandedHeight = AnalysisContent.ActualHeight;
            var heightAnim = new DoubleAnimation
            {
                From = _expandedHeight,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseInOut }
            };
            heightAnim.Completed += (_, _) =>
            {
                AnalysisContent.Visibility = Visibility.Collapsed;
            };
            AnalysisContent.BeginAnimation(HeightProperty, heightAnim);
        }
        else
        {
            AnalysisContent.Visibility = Visibility.Visible;
            AnalysisContent.BeginAnimation(HeightProperty, null); // Clear animation
            var heightAnim = new DoubleAnimation
            {
                From = 0,
                To = _expandedHeight,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseInOut }
            };
            heightAnim.Completed += (_, _) =>
            {
                AnalysisContent.BeginAnimation(HeightProperty, null); // Release to Auto
            };
            AnalysisContent.BeginAnimation(HeightProperty, heightAnim);
        }
    }
}
