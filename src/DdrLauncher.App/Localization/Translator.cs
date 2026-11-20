using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace DdrLauncher.App.Localization;

/// <summary>A language the launcher speaks: its code and its name, written in that language.</summary>
public sealed record Language(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The launcher's texts in the language in use. The English text is the key: each language is a JSON file
/// (Localization/&lt;code&gt;.json, built into the exe) from English texts to translated ones. A text missing there stays in English.
/// Changing the language updates every text on screen at once.
/// </summary>
public sealed class Translator : INotifyPropertyChanged
{
    /// <summary>The game's languages.</summary>
    public static readonly IReadOnlyList<Language> Languages =
    [
        new("en", "English"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("it", "Italiano"),
        new("pt", "Português"),
        new("ru", "Русский"),
        new("ja", "日本語"),
        new("zh", "中文（简体）"),
    ];

    private Dictionary<string, string> _texts = [];

    public static Translator Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public Language Current { get; private set; } = Languages[0];

    /// <summary>Changes on every switch, so bindings to it read their text again.</summary>
    public int Version { get; private set; }

    /// <summary>A known language code, else the Windows language when the launcher speaks it, else English.</summary>
    public static Language Pick(string? code) =>
        Languages.FirstOrDefault(l => l.Code == code)
        ?? Languages.FirstOrDefault(l => l.Code == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        ?? Languages[0];

    /// <summary>The text in the language in use; <paramref name="english"/> itself when it has no translation.</summary>
    public static string T(string english) => Instance._texts.GetValueOrDefault(english, english);

    /// <summary>A text with values: "{0} mods added".</summary>
    public static string T(string english, params object?[] values) => string.Format(CultureInfo.CurrentCulture, T(english), values);

    /// <summary>"1 team", "3 teams": the singular or plural text, with the count.</summary>
    /// shortcut: one plural form only, as in English; Russian has three, add them if Russian players ask.
    public static string Count(int count, string one, string many) => T(count == 1 ? one : many, count);

    public void Use(Language language)
    {
        _texts = Load(language.Code);
        Current = language;
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(language.Code);
        Version++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>The translations of a language, as built into the exe; empty for English.</summary>
    internal static Dictionary<string, string> Load(string code)
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Localization/{code}.json"));
        if (code == "en" || resource is null)
        {
            return [];
        }

        using var stream = resource.Stream;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}

/// <summary>A translated text in XAML: <c>Text="{l:T 'Import mods'}"</c>. It follows the language when it changes.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension(string text) : MarkupExtension
{
    private static readonly IValueConverter Converter = new TextConverter();

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(Translator.Version)) { Source = Translator.Instance, Mode = BindingMode.OneWay, Converter = Converter, ConverterParameter = text }
            .ProvideValue(serviceProvider);

    private sealed class TextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Translator.T((string)parameter);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
