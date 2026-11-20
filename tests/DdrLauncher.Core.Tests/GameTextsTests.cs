using System.Text;
using DdrLauncher.Core.Data;

namespace DdrLauncher.Core.Tests;

public sealed class GameTextsTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "ddr-texts-" + Guid.NewGuid().ToString("N"));

    public GameTextsTests()
    {
        Write(GameTexts.MessagesFile, "1<!sp!>1<!sp!>MAIN MENU", "4204<!sp!>2<!sp!>team: AUDI SPORT");
        Write(GameTexts.LanguagesFile, "3204<!sp!>1<!sp!>1<!sp!>MAIN MENU<!sp!>", "23229<!sp!>4204<!sp!>1<!sp!>AUDI SPORT<!sp!>",
            "27428<!sp!>4204<!sp!>3<!sp!>AUDI SPORT<!sp!>");
    }

    public void Dispose() => Directory.Delete(_cache, recursive: true);

    [Fact]
    public void NewNamesGetTheNextIdsInEveryLanguage()
    {
        var texts = GameTexts.Open(_cache);

        Assert.Equal(4205, texts.Add("John Doe"));
        Assert.Equal(4206, texts.Add("Bad<!sp!>Name\r\n"));

        var files = texts.Files();
        var messages = Encoding.UTF8.GetString(files[GameTexts.MessagesFile]);
        var languages = Encoding.UTF8.GetString(files[GameTexts.LanguagesFile]);
        Assert.StartsWith("\uFEFF", messages);
        Assert.EndsWith("4205<!sp!>2<!sp!>John Doe\r\n4206<!sp!>2<!sp!>Bad Name\r\n", messages);
        Assert.Contains("27429<!sp!>4205<!sp!>1<!sp!>John Doe<!sp!>\r\n27430<!sp!>4205<!sp!>3<!sp!>John Doe<!sp!>\r\n", languages);
    }

    private void Write(string file, params string[] lines)
    {
        var path = Path.Combine(_cache, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
    }
}
