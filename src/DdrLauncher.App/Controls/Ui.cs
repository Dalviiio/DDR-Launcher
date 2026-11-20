using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows;

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

/// <summary>
/// Rounds a window's corners with a window region instead of a transparent window: a transparent (layered) WPF
/// window is copied back from the graphics card on every redraw, which makes a large window slow. Maximized
/// windows keep square corners, like any Windows window.
/// </summary>
public static class RoundedCorners
{
    public static void Apply(Window window, double radius)
    {
        void Update()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (window.WindowState == WindowState.Maximized || !GetWindowRect(handle, out var bounds))
            {
                SetWindowRgn(handle, IntPtr.Zero, true);
                return;
            }

            var scale = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var diameter = (int)Math.Round(radius * 2 * scale);
            var region = CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1, diameter, diameter);

            // The window owns the region from now on (the system frees it).
            SetWindowRgn(handle, region, true);
        }

        window.SourceInitialized += (_, _) => Update();
        window.SizeChanged += (_, _) => Update();
        window.StateChanged += (_, _) => Update();
        window.DpiChanged += (_, _) => Update();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
}
