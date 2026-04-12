using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CelestialMechanics.Desktop.Converters;

/// <summary>Inverts a bool then converts to Visibility.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>Null → Collapsed, non-null → Visible.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a double to a specified number of decimal places.</summary>
public class DoubleToFormattedStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            var format = parameter as string ?? "F6";
            return d.ToString(format, CultureInfo.InvariantCulture);
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return result;
        return DependencyProperty.UnsetValue;
    }
}

/// <summary>Maps BodyType enum to a Unicode glyph icon.</summary>
public class BodyTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CelestialMechanics.Physics.Types.BodyType bt
            ? Models.SceneNodeItem.GetIconForBodyType(bt)
            : "●";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats mass in scientific notation for display.</summary>
public class MassToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double mass)
        {
            if (mass >= 1e24) return $"{mass / 1e24:F2} ×10²⁴ kg";
            if (mass >= 1e18) return $"{mass / 1e18:F2} ×10¹⁸ kg";
            if (mass >= 1e12) return $"{mass / 1e12:F2} ×10¹² kg";
            return $"{mass:G4} kg";
        }
        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Engine state enum → status color brush.</summary>
public class EngineStateToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Running = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)));
    private static readonly SolidColorBrush Paused = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)));
    private static readonly SolidColorBrush Stopped = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Running" or "RUNNING" => Running,
            "Paused" or "PAUSED" => Paused,
            _ => Stopped
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}

/// <summary>Engine state enum → display text with dot prefix.</summary>
public class EngineStateToPillTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Running" or "RUNNING" => "● RUNNING",
            "Paused" or "PAUSED" => "● PAUSED",
            "Stopped" or "STOPPED" => "● STOPPED",
            _ => "● UNKNOWN"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps SimEventLogEntry type to a hex color string for badge.</summary>
public class StatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { }
        }
        return new SolidColorBrush(Color.FromRgb(0x7A, 0x8B, 0xA8));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Timestep double → formatted display string.</summary>
public class TimestepToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d.ToString("G4", CultureInfo.InvariantCulture) : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return result;
        return DependencyProperty.UnsetValue;
    }
}

/// <summary>Bool → Opacity (true=1.0, false=0.4).</summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.4;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a nullable — null→true, non-null→false.</summary>
public class InverseNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a Vec3d (X, Y, Z) as a readable string.</summary>
public class Vec3ToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CelestialMechanics.Math.Vec3d v)
            return $"({v.X:F4}, {v.Y:F4}, {v.Z:F4})";
        return "(0, 0, 0)";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps energy series name to the correct stroke color brush.</summary>
public class EnergyToStrokeConverter : IValueConverter
{
    private static readonly SolidColorBrush Total = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6D, 0x1F)));    // AccentOrange
    private static readonly SolidColorBrush Kinetic = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)));   // AccentAmber
    private static readonly SolidColorBrush Potential = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF))); // AccentCyan

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString()?.ToLowerInvariant() switch
        {
            "total" => Total,
            "kinetic" => Kinetic,
            "potential" => Potential,
            _ => Total
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
