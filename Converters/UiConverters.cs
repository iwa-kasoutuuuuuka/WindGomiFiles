using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Converters;

public class RatioToWidthConverter : IValueConverter
{
    public double MaxWidth { get; set; } = 165.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double ratio)
        {
            return Math.Max(0.0, Math.Min(1.0, ratio)) * MaxWidth;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class CategoryToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush TempBrush = CreateFrozenBrush(0x4D, 0xF5, 0x9E, 0x0B);       // Amber
    private static readonly SolidColorBrush CacheBrush = CreateFrozenBrush(0x4D, 0x38, 0xBD, 0xF8);      // Sky Blue
    private static readonly SolidColorBrush DevCacheBrush = CreateFrozenBrush(0x4D, 0xA8, 0x55, 0xF7);   // Purple
    private static readonly SolidColorBrush LogDumpBrush = CreateFrozenBrush(0x4D, 0xEC, 0x48, 0x99);    // Pink
    private static readonly SolidColorBrush ArchiveBrush = CreateFrozenBrush(0x4D, 0x10, 0xB9, 0x81);    // Emerald
    private static readonly SolidColorBrush ProtectedBrush = CreateFrozenBrush(0x4D, 0xEF, 0x44, 0x44);  // Red Shield
    private static readonly SolidColorBrush NormalBrush = CreateFrozenBrush(0x22, 0x94, 0xA3, 0xB8);     // Subtle Slate

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ItemCategory cat)
        {
            return cat switch
            {
                ItemCategory.TempOrJunk => TempBrush,
                ItemCategory.Cache => CacheBrush,
                ItemCategory.DevCache => DevCacheBrush,
                ItemCategory.LogOrDump => LogDumpBrush,
                ItemCategory.InstallerOrArchive => ArchiveBrush,
                ItemCategory.SystemProtected => ProtectedBrush,
                _ => NormalBrush
            };
        }
        return NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

public class CategoryToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush TempFg = CreateFrozenBrush(0xFB, 0xBF, 0x24);
    private static readonly SolidColorBrush CacheFg = CreateFrozenBrush(0x7D, 0xD3, 0xFC);
    private static readonly SolidColorBrush DevCacheFg = CreateFrozenBrush(0xD8, 0xB4, 0xFE);
    private static readonly SolidColorBrush LogDumpFg = CreateFrozenBrush(0xF4, 0x72, 0xB6);
    private static readonly SolidColorBrush ArchiveFg = CreateFrozenBrush(0x6E, 0xE7, 0xB7);
    private static readonly SolidColorBrush ProtectedFg = CreateFrozenBrush(0xFC, 0xA5, 0xA5);
    private static readonly SolidColorBrush NormalFg = CreateFrozenBrush(0x94, 0xA3, 0xB8);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ItemCategory cat)
        {
            return cat switch
            {
                ItemCategory.TempOrJunk => TempFg,
                ItemCategory.Cache => CacheFg,
                ItemCategory.DevCache => DevCacheFg,
                ItemCategory.LogOrDump => LogDumpFg,
                ItemCategory.InstallerOrArchive => ArchiveFg,
                ItemCategory.SystemProtected => ProtectedFg,
                _ => NormalFg
            };
        }
        return NormalFg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
