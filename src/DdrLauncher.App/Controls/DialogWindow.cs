using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace DdrLauncher.App.Controls;

/// <summary>Kind of message: picks the icon and its color.</summary>
public enum DialogKind
{
    None,
    Info,
    Warning,
    Error,
}

/// <summary>
/// The manager's dialogs, in the same style as the main window: rounded, dark, a title row that drags the window
/// and buttons at the bottom right. Messages are selectable plain text (they can be copied, never rendered).
/// </summary>
public sealed class DialogWindow : Window
{
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

    public DialogWindow(string title, UIElement body, DialogKind kind = DialogKind.None, double width = 520)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        Background = Brush("Brush.Surface");
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = (FontFamily)Application.Current.FindResource("Font.Main");
        FontSize = 14;
        Foreground = Brush("Brush.Text");
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
        RoundedCorners.Apply(this, 20);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14), Background = Brushes.Transparent };
        if (kind != DialogKind.None)
        {
            var (icon, color) = kind switch
            {
                DialogKind.Warning => ("Icon.Warning", "Brush.Warning"),
                DialogKind.Error => ("Icon.Error", "Brush.Danger"),
                _ => ("Icon.Info", "Brush.Info"),
            };
            header.Children.Add(new System.Windows.Shapes.Path
            {
                Data = (Geometry)Application.Current.FindResource(icon),
                Fill = Brush(color),
                Style = (Style)Application.Current.FindResource("Icon"),
                Margin = new Thickness(0, 0, 10, 0),
            });
        }

        header.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("Text.Title"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };

        _buttons.Margin = new Thickness(0, 18, 0, 0);
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_buttons, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(_buttons);
        layout.Children.Add(body);

        Content = new Border
        {
            Background = Brush("Brush.Surface"),
            BorderBrush = Brush("Brush.BorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(22, 18, 22, 18),
            Child = layout,
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>Adds a button; the first primary one is the default (Enter), a cancel one closes with Escape.</summary>
    public Button AddButton(string text, bool? result, bool primary = false, bool isCancel = false)
    {
        var button = new Button { Content = text, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsCancel = isCancel, IsDefault = primary };
        if (primary)
        {
            button.Style = (Style)Application.Current.FindResource("PrimaryButton");
            button.Margin = new Thickness(8, 0, 0, 0);
        }

        button.Click += (_, _) =>
        {
            if (result is null)
            {
                Close();
            }
            else
            {
                DialogResult = result;
            }
        };
        _buttons.Children.Add(button);
        return button;
    }

    /// <summary>A message as selectable text that scrolls when it is long.</summary>
    public static TextBox MessageText(string message) => new()
    {
        Text = message,
        Style = (Style)Application.Current.FindResource("SelectableText"),
        Foreground = Brush("Brush.Text"),
        TextWrapping = TextWrapping.Wrap,
        MaxHeight = 440,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
