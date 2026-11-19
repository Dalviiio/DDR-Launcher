using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DdrLauncher.App;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class FileSizeConverter : IValueConverter
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0 GB", CultureInfo.InvariantCulture),
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0 MB", CultureInfo.InvariantCulture),
        >= 1L << 10 => (bytes / 1024.0).ToString("0 KB", CultureInfo.InvariantCulture),
        _ => bytes + " B",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is long bytes ? Format(bytes) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
