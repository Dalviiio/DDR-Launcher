using System.Text.Json;
using System.Text.RegularExpressions;

namespace DdrLauncher.Core.Tests;

/// <summary>Every text the launcher shows has a translation in every language, with the same "{0}" places.</summary>
public sealed partial class TranslationTests
{
    private static readonly string App = Path.Combine(Root(), "src", "DdrLauncher.App");

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Root(), "src", "DdrLauncher.App", "Localization"), "*.json"))
        {
            data.Add(Path.GetFileNameWithoutExtension(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryTextOnScreenIsTranslated(string language)
    {
        var texts = Load(language);
        var missing = UsedTexts().Where(t => !texts.ContainsKey(t)).ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void TranslationsKeepTheirPlaces(string language)
    {
        foreach (var (english, translated) in Load(language))
        {
            Assert.True(Places(english).SetEquals(Places(translated)), $"{language}: \"{translated}\" for \"{english}\"");
        }
    }

    [Fact]
    public void EveryLanguageHasTheSameTexts()
    {
        var french = Load("fr").Keys.ToHashSet();
        foreach (var file in Directory.GetFiles(Path.Combine(App, "Localization"), "*.json"))
        {
            var language = Path.GetFileNameWithoutExtension(file);
            Assert.True(french.SetEquals(Load(language).Keys), language);
        }
    }

    // Texts in XAML ({l:T '...'}) and in code (T("...") and the two texts of Count).
    private static HashSet<string> UsedTexts()
    {
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(App, "*.xaml", SearchOption.AllDirectories).Where(Source))
        {
            used.UnionWith(XamlText().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value.Replace("\\'", "'", StringComparison.Ordinal)));
        }

        foreach (var file in Directory.EnumerateFiles(App, "*.cs", SearchOption.AllDirectories).Where(Source))
        {
            var code = File.ReadAllText(file);
            used.UnionWith(CodeText().Matches(code).Select(m => m.Groups[1].Value.Replace(@"\n", "\n", StringComparison.Ordinal)));
            used.UnionWith(CountText().Matches(code).SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value }));
        }

        return used;
    }

    private static bool Source(string path) => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(App, "Localization", language + ".json")))!;

    private static HashSet<string> Places(string text) => PlaceMark().Matches(text).Select(m => m.Value).ToHashSet();

    private static string Root()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Directory.Build.props")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [GeneratedRegex(@"\{l:T '((?:[^'\\]|\\.)*)'\}")]
    private static partial Regex XamlText();

    [GeneratedRegex("""\bT\("((?:[^"\\]|\\.)*)"[,)]""")]
    private static partial Regex CodeText();

    [GeneratedRegex("""\bCount\([^,()]+, "([^"]*)", "([^"]*)"\)""")]
    private static partial Regex CountText();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceMark();
}
