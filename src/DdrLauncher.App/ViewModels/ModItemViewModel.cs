using System.Globalization;
using DdrLauncher.App.Mvvm;
using DdrLauncher.Core.Mods;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App.ViewModels;

public sealed class ModItemViewModel(ModEntry model, MainViewModel owner) : ObservableObject
{
    public ModEntry Model { get; } = model;

    private int _number;

    public string Name => Model.Name;

    /// <summary>Position in the library, shown as a rally stage number ("01").</summary>
    public int Number
    {
        get => _number;
        set
        {
            if (Set(ref _number, value))
            {
                Raise(nameof(StageText));
            }
        }
    }

    public string StageText => Number.ToString("00", CultureInfo.InvariantCulture);

    public IReadOnlyList<ModFile> Files => Model.Files;

    public string FilesText => Count(Model.Files.Count, "{0} file", "{0} files");

    public string AddedText => Model.AddedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentUICulture);

    public string SizeText => FileSizeConverter.Format(Model.TotalSize);

    /// <summary>Bound to the ON box: ticking it puts the mod in the game, unticking takes it out.</summary>
    public bool IsEnabled
    {
        get => Model.Enabled;
        set
        {
            if (value != Model.Enabled)
            {
                _ = ApplyAsync(value);
            }
        }
    }

    private async Task ApplyAsync(bool value)
    {
        await owner.SetEnabledAsync(this, value);

        // Puts the box back if the change was refused.
        Raise(nameof(IsEnabled));
    }
}
