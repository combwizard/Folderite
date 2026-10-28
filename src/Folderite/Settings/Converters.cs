using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Folderite.Menu;

namespace Folderite.Settings;

/// <summary>The app icon (assets/icon.ico, a WPF resource) as an image source.</summary>
public static class AppIcon
{
    // The 32 px frame: shown at 16 logical px it stays crisp at 100-200 % scaling without tracking DPI.
    public static ImageSource Medium { get; } = Frame(32);

    private static ImageSource Frame(int minSize)
    {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Folderite;component/Assets/icon.ico"),
            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderBy(f => f.PixelWidth).FirstOrDefault(f => f.PixelWidth >= minSize)
            ?? decoder.Frames.OrderBy(f => f.PixelWidth).Last();
        frame.Freeze();
        return frame;
    }
}

/// <summary>Attached properties used by the SettingRow style.</summary>
public static class Setting
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Setting), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.RegisterAttached("Title", typeof(string), typeof(Setting), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.RegisterAttached("Description", typeof(string), typeof(Setting), new PropertyMetadata(null));

    public static string GetIcon(DependencyObject o) => (string)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string v) => o.SetValue(IconProperty, v);
    public static string GetTitle(DependencyObject o) => (string)o.GetValue(TitleProperty);
    public static void SetTitle(DependencyObject o, string v) => o.SetValue(TitleProperty, v);
    public static string? GetDescription(DependencyObject o) => (string?)o.GetValue(DescriptionProperty);
    public static void SetDescription(DependencyObject o, string? v) => o.SetValue(DescriptionProperty, v);
}

/// <summary>Visible when the value is non-null (and not an empty string); Invert flips it.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool present = value is not null && value is not string { Length: 0 };
        return present ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value is the number 0 (empty-state placeholders).</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Folder path → its shell icon. Fetched at 32 px and scaled down, so it stays crisp at high DPI.</summary>
public sealed class PathToIconConverter : IValueConverter
{
    private static readonly IconCache Icons = new();
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } path) return null;
        path = Environment.ExpandEnvironmentVariables(path);
        if (Cache.TryGetValue(path, out var cached)) return cached;

        ImageSource? image = null;
        var icon = Icons.GetIcon(path, 32);
        if (icon != 0)
        {
            image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
        }
        if (Cache.Count > 512) Cache.Clear();
        Cache[path] = image;
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
