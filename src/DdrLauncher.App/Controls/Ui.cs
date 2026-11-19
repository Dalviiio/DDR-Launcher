using System.Windows;
using System.Windows.Media;

namespace DdrLauncher.App.Controls;

/// <summary>Attached properties used by the theme's templates (Themes/Theme.xaml).</summary>
public static class Ui
{
    /// <summary>Icon drawn before a button's or a tab's text (a 24x24 geometry from Themes/Icons.xaml).</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(Ui), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static Geometry? GetIcon(DependencyObject element) => (Geometry?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, Geometry? value) => element.SetValue(IconProperty, value);
}
