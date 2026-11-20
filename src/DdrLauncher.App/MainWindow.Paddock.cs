using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DdrLauncher.App.Controls;
using DdrLauncher.App.ViewModels;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App;

/// <summary>The Paddock's add, delete and reset actions, with their small forms.</summary>
public partial class MainWindow
{
    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        switch (_vm.Database.Tab)
        {
            case "Drivers":
                await AddDriver();
                break;
            default:
                await AddTeam();
                break;
        }
    }

    private async Task AddDriver()
    {
        var db = _vm.Database;
        var form = new Form();
        var name = form.Text("NAME");
        var country = form.Pick(T("COUNTRY"), db.Countries.ToList(), "Value", null);
        form.Validate = () =>
            string.IsNullOrWhiteSpace(name.Text) ? T("Enter a name")
            : country.SelectedItem is null ? T("Choose a country")
            : null;

        if (form.Show(this, T("Add a driver"), T("Add")))
        {
            await db.AddPilotAsync(name.Text, ((KeyValuePair<string, string>)country.SelectedItem!).Key);
        }
    }

    private async Task AddTeam()
    {
        var db = _vm.Database;
        var form = new Form();
        var editions = db.Editions.Where(e => !e.IsDefault).ToList();
        var edition = form.Pick(T("EDITION"), editions, "Name", editions.FirstOrDefault(e => e.Key == db.Edition) ?? editions.LastOrDefault(e => e.Caption == "DAKAR"));
        var vehicle = form.Pick(T("VEHICLE"), Array.Empty<VehicleItem>(), "DisplayName", null);
        var skin = form.Pick("SKIN", Array.Empty<CrewItem>(), "SkinName", null, (DataTemplate)FindResource("SkinItem"));
        var top = form.Row(2);
        var name = form.Text("TEAM NAME", into: top);
        var number = form.Text("NUMBER", db.NextFreeNumber().ToString(CultureInfo.InvariantCulture), top);
        var driver = form.Pick(T("DRIVER"), db.PilotOptions, "Name", null);
        var coDriver = form.Pick(T("CO-DRIVER"), db.PilotOptions, "Name", null);
        form.Hint(T("The team is added to the edition; no existing team changes. Skins come from teams of that edition that drive the same vehicle. Bikes and quads have no co-driver. Numbers from 790 up are free."));

        string? Table() => edition.SelectedItem is EditionChip e ? db.TableOf(e.Key) : null;
        void Vehicles() => vehicle.ItemsSource = Table() is { } t ? db.VehiclesWithSkins(t) : [];
        void Skins()
        {
            skin.ItemsSource = Table() is { } t && vehicle.SelectedItem is VehicleItem v ? db.Skins(t, v) : [];
            skin.SelectedIndex = skin.Items.Count > 0 ? 0 : -1;
        }

        edition.SelectionChanged += (_, _) => Vehicles();
        vehicle.SelectionChanged += (_, _) => Skins();
        Vehicles();
        form.Validate = () =>
            edition.SelectedItem is null ? T("Choose an edition")
            : vehicle.SelectedItem is null ? T("Choose a vehicle")
            : skin.SelectedItem is null ? T("Choose a skin")
            : string.IsNullOrWhiteSpace(name.Text) ? T("Enter a team name")
            : Int(number.Text) is not (> 0 and < 10000) ? T("Enter a number from 1 to 9999")
            : driver.SelectedItem is null ? T("Choose a driver")
            : null;

        if (form.Show(this, T("Add a team"), T("Add")))
        {
            await db.AddTeamAsync((CrewItem)skin.SelectedItem!, (VehicleItem)vehicle.SelectedItem!, Int(number.Text)!.Value, name.Text,
                (PilotItem)driver.SelectedItem!, coDriver.SelectedItem as PilotItem);
        }
    }

    private async void OnDeletePilot(object sender, RoutedEventArgs e)
    {
        if (_vm.Database.SelectedPilot is { IsAdded: true } pilot && Confirm(T("Delete {0}?", pilot.Name), T("They are removed from the launcher now, and from the game at the next Apply."), T("Delete")))
        {
            await _vm.Database.DeletePilotAsync(pilot);
        }
    }

    private async void OnDeleteTeam(object sender, RoutedEventArgs e)
    {
        if (_vm.Database.SelectedCrew is { IsAdded: true } crew && Confirm(T("Delete {0}?", crew.Team.Name), T("It is removed from the launcher now, and from the game at the next Apply."), T("Delete")))
        {
            await _vm.Database.DeleteTeamAsync(crew);
        }
    }

    private async void OnNewProfile(object sender, RoutedEventArgs e)
    {
        var db = _vm.Database;
        var form = new Form();
        var name = form.Text(T("NAME"), db.FreeProfileName(db.CanEdit ? db.ActiveProfile.Name : T("My lineup")));
        form.Hint(db.CanEdit ? T("Starts as a copy of {0}, which stays as it is.", db.ActiveProfile.Name) : T("Starts from the game as it ships."));
        form.Validate = () => string.IsNullOrWhiteSpace(name.Text) ? T("Enter a name") : null;
        if (form.Show(this, T("New profile"), T("Create")))
        {
            await db.NewProfileAsync(name.Text);
        }
    }

    private void OnRenameProfile(object sender, RoutedEventArgs e)
    {
        var db = _vm.Database;
        var form = new Form();
        var name = form.Text("NAME", db.ActiveProfile.Name);
        form.Validate = () => string.IsNullOrWhiteSpace(name.Text) ? T("Enter a name") : null;
        if (form.Show(this, T("Rename profile"), T("Rename")))
        {
            db.RenameProfile(name.Text);
        }
    }

    private async void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (Confirm(T("Delete {0}?", _vm.Database.ActiveProfile.Name), T("Its changes are lost. What is in the game stays there until you apply another profile."), T("Delete")))
        {
            await _vm.Database.DeleteProfileAsync();
        }
    }

    private async void OnResetEverything(object sender, RoutedEventArgs e)
    {
        if (Confirm(T("Back to the original game?"),
                T("The launcher removes its pak from the game now and switches to Default. Your profiles stay."),
                T("Go back")))
        {
            await _vm.Database.ResetEverythingAsync();
        }
    }

    internal bool Confirm(string title, string message, string action)
    {
        var dialog = new DialogWindow(title, DialogWindow.MessageText(message), DialogKind.Warning) { Owner = this };
        dialog.AddButton(T("Cancel"), false, isCancel: true);
        dialog.AddButton(action, true, primary: true);
        return dialog.ShowDialog() == true;
    }

    private static int? Int(string text) => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>A stack of labelled fields; the Add button stays off and the reason shows until <see cref="Validate"/> passes.</summary>
    private sealed class Form
    {
        private readonly StackPanel _panel = new();
        private readonly TextBlock _error = new() { Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Brush.Warning"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        private Button? _ok;

        public Func<string?> Validate { get; set; } = () => null;

        public TextBox Text(string label, string value = "", Panel? into = null)
        {
            var box = new TextBox { Text = value };
            box.TextChanged += (_, _) => Check();
            Add(label, box, into);
            return box;
        }

        /// <param name="template">How to show each item; without it, the <paramref name="display"/> field is shown as text.</param>
        public ComboBox Pick(string label, IEnumerable items, string display, object? selected, DataTemplate? template = null)
        {
            // WPF takes either a field to show or a template, never both.
            var box = new ComboBox { ItemsSource = items, SelectedItem = selected, MaxDropDownHeight = 320 };
            if (template is null)
            {
                box.DisplayMemberPath = display;
            }
            else
            {
                box.ItemTemplate = template;
            }

            TextSearch.SetTextPath(box, display);
            box.SelectionChanged += (_, _) => Check();
            Add(label, box, null);
            return box;
        }

        public UniformGrid Row(int columns)
        {
            var row = new UniformGrid { Columns = columns };
            _panel.Children.Add(row);
            return row;
        }

        public void Hint(string text) =>
            _panel.Children.Add(new TextBlock { Text = text, Style = (Style)Application.Current.FindResource("Text.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        public bool Show(Window owner, string title, string action)
        {
            _panel.Children.Add(_error);
            var dialog = new DialogWindow(title, _panel, width: 480) { Owner = owner };
            dialog.AddButton(T("Cancel"), false, isCancel: true);
            _ok = dialog.AddButton(action, true, primary: true);
            Check();
            return dialog.ShowDialog() == true;
        }

        private void Add(string label, Control control, Panel? into)
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, into is null ? 0 : 8, 12) };
            field.Children.Add(new TextBlock { Text = label, Style = (Style)Application.Current.FindResource("Text.Label"), Margin = new Thickness(0, 0, 0, 6) });
            field.Children.Add(control);
            (into ?? _panel).Children.Add(field);
        }

        private void Check()
        {
            var problem = Validate();
            _error.Text = problem ?? "";
            if (_ok is not null)
            {
                _ok.IsEnabled = problem is null;
            }
        }
    }
}
