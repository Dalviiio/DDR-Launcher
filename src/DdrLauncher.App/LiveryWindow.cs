using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DdrLauncher.App.Controls;
using DdrLauncher.App.ViewModels;
using DdrLauncher.Core.Data;
using DdrLauncher.Core.Paks;
using DdrLauncher.Core.Safety;
using Microsoft.Win32;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App;

/// <summary>
/// A team's livery on its car in 3D. Export writes each part's picture and its wireframe for painting; Import puts a painted
/// picture on a part; Save keeps the painted parts in the profile (Apply puts them in the game). Default only shows and exports.
/// </summary>
internal sealed class LiveryWindow
{
    // Not drawn: the x-ray outline, the far-away version and the inside of the car, which only slow the view down.
    private static readonly string[] HiddenSlots = ["M_Xray", "M_VehicleLowLOD", "M_InteriorLod0", "M_Interior", "M_Dashboard", "M_Seatbelts", "M_Engine", "M_Mechanics"];

    private readonly DatabaseViewModel _db;
    private readonly CrewItem _crew;
    private readonly List<Part> _parts = [];
    private readonly DialogWindow _window;
    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 40 };
    private readonly ListBox _list = new() { MinHeight = 160, MaxHeight = 260 };
    private readonly TextBlock _status = new() { Style = Style("Text.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _loading = new() { Text = T("Loading the car..."), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Brush.TextMuted") };
    private readonly Button _import;
    private readonly Button _reset;
    private readonly Button _export;
    private readonly Button _save;
    private CarModel? _model;
    private Point3D _center;
    private double _yaw = 35, _pitch = 15, _distance = 600;
    private Point? _drag;
    private bool _changed;
    private string? _message;

    private LiveryWindow(Window owner, DatabaseViewModel db, CrewItem crew)
    {
        _db = db;
        _crew = crew;
        _viewport.Camera = _camera;

        var stage = new Grid { Background = Brush("Brush.Showroom") };
        stage.Children.Add(_viewport);
        stage.Children.Add(_loading);
        stage.Children.Add(new TextBlock
        {
            Text = T("Drag to turn, scroll to zoom"),
            Style = Style("Text.Hint"),
            Margin = new Thickness(14, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        stage.MouseLeftButtonDown += (_, e) => { _drag = e.GetPosition(stage); stage.CaptureMouse(); };
        stage.MouseLeftButtonUp += (_, _) => { _drag = null; stage.ReleaseMouseCapture(); };
        stage.MouseMove += (_, e) => Turn(e.GetPosition(stage));
        stage.MouseWheel += (_, e) => { _distance = Math.Clamp(_distance * (e.Delta > 0 ? 0.9 : 1.1), 50, 5000); PlaceCamera(); };

        _list.Style = Style("CardList");
        _list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        _list.ItemContainerStyle = new Style(typeof(ListBoxItem), Style("StageCard")) { Setters = { new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6)) } };
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Part.Text)));
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 9, 14, 9));
        _list.ItemTemplate = new DataTemplate { VisualTree = text };
        _list.SelectionChanged += (_, _) => { _message = null; Refresh(); };
        _export = new Button { Content = T("Export"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = T("Save every part's picture and wireframe in a folder, to paint them") };
        _import = new Button { Content = T("Import"), Style = Style("PrimaryButton"), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = T("Put a painted picture on this part") };
        _reset = new Button { Content = T("Reset part"), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        Ui.SetIcon(_export, (Geometry)Application.Current.FindResource("Icon.Folder"));
        Ui.SetIcon(_import, (Geometry)Application.Current.FindResource("Icon.Add"));
        Ui.SetIcon(_reset, (Geometry)Application.Current.FindResource("Icon.Undo"));
        _export.Click += async (_, _) => await ExportAsync();
        _import.Click += (_, _) => Import();
        _reset.Click += (_, _) => ResetPart();

        var side = new StackPanel { Width = 280, Margin = new Thickness(18, 0, 0, 0) };
        side.Children.Add(new TextBlock { Text = T("PARTS"), Style = Style("Text.Label"), Margin = new Thickness(0, 0, 0, 6) });
        side.Children.Add(_list);
        side.Children.Add(_import);
        side.Children.Add(_reset);
        side.Children.Add(_export);
        side.Children.Add(_status);

        var body = new DockPanel { Height = 600 };
        DockPanel.SetDock(side, Dock.Right);
        body.Children.Add(side);
        body.Children.Add(new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = stage });

        _window = new DialogWindow(T("{0} livery", crew.Team.Name), body, width: 1180) { Owner = owner };
        _window.AddButton(T("Close"), null, isCancel: true);
        _save = _window.AddButton(T("Save"), true, primary: true);
        _save.IsEnabled = false;
        _save.Visibility = db.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        _window.Loaded += async (_, _) => await LoadAsync();
    }

    /// <summary>Shows the livery of <paramref name="crew"/>; saves the painted parts in the profile when the user saves.</summary>
    public static void Show(Window owner, DatabaseViewModel db, CrewItem crew)
    {
        var editor = new LiveryWindow(owner, db, crew);
        if (editor._window.ShowDialog() == true && db.CanEdit)
        {
            var painted = editor._parts.Where(p => p.Image is not null).ToList();
            db.SavePaints(crew, painted.ToDictionary(p => p.Texture, p => p.Image!), painted.ToDictionary(p => p.Texture, p => p.File));
        }
    }

    private Part? Selected => _list.SelectedItem as Part;

    private async Task LoadAsync()
    {
        var parts = _crew.LiveryParts;
        if (parts.Count == 0)
        {
            _loading.Text = T("This team's livery is not in the game's data.");
            return;
        }

        try
        {
            var files = await _db.FetchTexturesAsync(parts.Select(p => p.Texture).Distinct().ToList());
            var paints = _db.PaintsOf(_crew).ToDictionary(p => p.Texture, p => p.Image);
            foreach (var group in parts.GroupBy(p => p.Texture).Where(g => files.ContainsKey(g.Key)))
            {
                var file = files[group.Key];
                var top = await Task.Run(() => TextureDecoder.Layout(Liveries.Open(DatabaseViewModel.TextureFolder, file).Uexp)?.Mips[0]);
                if (top is null)
                {
                    continue;
                }

                var part = new Part(group.Key, file, group.Select(p => p.Slot).ToHashSet(), Label(group.Select(p => p.Slot)), top.Width, top.Height)
                {
                    Image = paints.GetValueOrDefault(group.Key),
                };
                await ShowPictureAsync(part);
                _parts.Add(part);
            }

            _list.ItemsSource = _parts;
            _list.SelectedIndex = _parts.Count > 0 ? 0 : -1;
            _model = await _db.CarAsync(_crew.Vehicle.Model.Blueprint);
            if (_model is null)
            {
                _loading.Text = T("The 3D model of this car cannot be read. Export and Import still work.");
                return;
            }

            _loading.Visibility = Visibility.Collapsed;
            BuildScene(_model);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or OodleMissingException)
        {
            _loading.Text = T("The livery cannot be read: {0}", e.Message);
        }

        Refresh();
    }

    // The part's picture on the car: the painted one if there is one, else the game's.
    private async Task ShowPictureAsync(Part part)
    {
        var image = await Task.Run(() => part.Image is { } painted
            ? Images.Load(Path.Combine(DatabaseViewModel.LiveryFolder, painted))
            : DatabaseViewModel.GameTexture(part.File, 2048));
        part.Brush.ImageSource = image is null ? null : Images.Preview(image, 2048);
    }

    private void BuildScene(CarModel model)
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(0x80, 0x80, 0x88)));
        group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.6, -1, -0.4)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0x60, 0x60, 0x70), new Vector3D(0.7, -0.3, 0.8)));
        var plain = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x5C)));
        plain.Freeze();

        var (min, max) = (new Point3D(double.MaxValue, double.MaxValue, double.MaxValue), new Point3D(double.MinValue, double.MinValue, double.MinValue));
        foreach (var section in model.Sections.Where(s => !HiddenSlots.Contains(s.Slot)))
        {
            // The game is Z up and left-handed; WPF is Y up and right-handed: swapping Y and Z does both.
            var mesh = new MeshGeometry3D();
            var local = new Dictionary<int, int>();
            foreach (var vertex in section.Triangles)
            {
                if (!local.TryGetValue(vertex, out var index))
                {
                    index = local[vertex] = mesh.Positions.Count;
                    var p = new Point3D(model.Positions[vertex * 3], model.Positions[vertex * 3 + 2], model.Positions[vertex * 3 + 1]);
                    mesh.Positions.Add(p);
                    mesh.TextureCoordinates.Add(new Point(model.Uvs[vertex * 2], model.Uvs[vertex * 2 + 1]));
                    (min, max) = (new Point3D(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z)), new Point3D(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z)));
                }

                mesh.TriangleIndices.Add(index);
            }

            mesh.Freeze();
            var material = _parts.FirstOrDefault(p => p.Slots.Contains(section.Slot))?.Material ?? plain;
            group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }

        _viewport.Children.Add(new ModelVisual3D { Content = group });
        _center = new Point3D((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        _distance = (max - min).Length * 1.5;
        PlaceCamera();
    }

    private void Turn(Point at)
    {
        if (_drag is not { } from)
        {
            return;
        }

        _yaw += (at.X - from.X) * 0.4;
        _pitch = Math.Clamp(_pitch + (at.Y - from.Y) * 0.3, -10, 80);
        _drag = at;
        PlaceCamera();
    }

    private void PlaceCamera()
    {
        var (yaw, pitch) = (_yaw * Math.PI / 180, _pitch * Math.PI / 180);
        var offset = new Vector3D(Math.Cos(pitch) * Math.Cos(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Sin(yaw)) * _distance;
        _camera.Position = _center + offset;
        _camera.LookDirection = -offset;
        _camera.UpDirection = new Vector3D(0, 1, 0);
        _camera.NearPlaneDistance = _distance / 100;
        _camera.FarPlaneDistance = _distance * 10;
    }

    private void Import()
    {
        if (Selected is not { } part || !_db.CanEdit)
        {
            return;
        }

        var dialog = new OpenFileDialog { Title = T("Picture for {0}", part.Label), Filter = T("Pictures") + " (*.png, *.jpg)|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(_window) != true)
        {
            return;
        }

        try
        {
            // Stored at the texture's size, named by its content: the same picture is kept once.
            Directory.CreateDirectory(DatabaseViewModel.LiveryFolder);
            var temp = Path.Combine(DatabaseViewModel.LiveryFolder, Guid.NewGuid().ToString("N") + ".tmp");
            Images.SavePng(Images.Load(dialog.FileName, part.Width, part.Height), temp);
            var name = FileHasher.Sha256(temp) + ".png";
            File.Move(temp, Path.Combine(DatabaseViewModel.LiveryFolder, name), overwrite: true);
            part.Image = name;
            _ = ShowPictureAsync(part);
            _changed = true;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            _message = T("This picture cannot be read: {0}", e.Message);
            return;
        }

        Refresh();
    }

    private void ResetPart()
    {
        if (Selected is { Image: not null } part)
        {
            part.Image = null;
            _ = ShowPictureAsync(part);
            _changed = true;
            Refresh();
        }
    }

    private async Task ExportAsync()
    {
        var dialog = new OpenFolderDialog { Title = T("Folder for the livery pictures") };
        if (_parts.Count == 0 || dialog.ShowDialog(_window) != true)
        {
            return;
        }

        _export.IsEnabled = false;
        _message = T("Exporting...");
        Refresh();
        try
        {
            var folder = dialog.FolderName;
            var model = _model;
            await Task.Run(() =>
            {
                foreach (var part in _parts)
                {
                    var name = string.Concat(part.Label.Split(Path.GetInvalidFileNameChars()));
                    var picture = part.Image is { } painted
                        ? Images.Load(Path.Combine(DatabaseViewModel.LiveryFolder, painted))
                        : DatabaseViewModel.GameTexture(part.File, int.MaxValue);
                    if (picture is not null)
                    {
                        Images.SavePng(picture, Path.Combine(folder, name + ".png"));
                    }

                    if (model is not null)
                    {
                        Images.SavePng(model.Wireframe(part.Slots, part.Width, part.Height), Path.Combine(folder, name + "_wireframe.png"));
                    }
                }
            });
            _message = T("Exported to {0}. Paint over a part's picture (its wireframe shows where each panel is), then Import it here.", folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _message = T("Export failed: {0}", e.Message);
        }
        finally
        {
            _export.IsEnabled = true;
            Refresh();
        }
    }

    private void Refresh()
    {
        _list.Items.Refresh();
        _import.Visibility = _reset.Visibility = _db.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        _import.IsEnabled = Selected is not null;
        _reset.IsEnabled = Selected?.Image is not null;
        _save.IsEnabled = _changed;
        _status.Text = _message
            ?? (!_db.CanEdit ? T("Default is the game as it ships. To paint this livery, make a profile. Export works on Default too.")
                : Selected is { } part ? T("{0} takes {1} x {2} pictures. Other sizes are resized.", part.Label, part.Width, part.Height) : "");
    }

    // "M_Wheels_FL", "M_SpareWheel_L"... -> "Wheels, SpareWheel"
    private static string Label(IEnumerable<string> slots) => string.Join(", ", slots
        .Select(s => s.StartsWith("M_", StringComparison.Ordinal) ? s[2..] : s)
        .Select(s => s.Split('_') is { Length: > 1 } parts && parts[^1] is "FL" or "FR" or "RL" or "RR" or "L" or "R" ? string.Join("_", parts[..^1]) : s)
        .Distinct());

    private static Style Style(string key) => (Style)Application.Current.FindResource(key);

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>One colour texture of the livery and the slots that wear it.</summary>
    private sealed class Part
    {
        public Part(string texture, string file, HashSet<string> slots, string label, int width, int height)
        {
            (Texture, File, Slots, Label, Width, Height) = (texture, file, slots, label, width, height);

            // Textures repeat outside 0..1, as in the game.
            Brush = new ImageBrush { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), TileMode = TileMode.Tile };
            Material = new DiffuseMaterial(Brush);
        }

        public string Texture { get; }

        public string File { get; }

        public HashSet<string> Slots { get; }

        public string Label { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>The painted picture's file name; null keeps the game's.</summary>
        public string? Image { get; set; }

        public ImageBrush Brush { get; }

        public DiffuseMaterial Material { get; }

        public string Text => Image is null ? Label : Label + "  " + T("(painted)");
    }
}
