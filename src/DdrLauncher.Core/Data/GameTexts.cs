using System.Globalization;
using System.Text;

namespace DdrLauncher.Core.Data;

/// <summary>
/// The game's own text database: rows of the data tables show the text whose id is in their NameId field
/// (FFMessageTranslation lists the ids, FFMessageLanguage holds the text in each language).
/// New vehicles, drivers and teams get new ids here, so the game shows the names the user typed.
/// </summary>
internal sealed class GameTexts
{
    public const string MessagesFile = "Dakar2Game/Content/Database/FFTranslation/FFMessageTranslation.txt";
    public const string LanguagesFile = "Dakar2Game/Content/Database/FFTranslation/FFMessageLanguage.txt";

    private const string Separator = "<!sp!>";
    private const string TeamSelectionScreen = "2";

    private readonly List<string> _messages;
    private readonly List<string> _languages;
    private readonly IReadOnlyList<string> _languageIds;
    private int _lastMessage;
    private int _lastLanguageRow;

    private GameTexts(List<string> messages, List<string> languages)
    {
        _messages = messages;
        _languages = languages;
        _lastMessage = messages.Max(l => Column(l, 0));
        _lastLanguageRow = languages.Max(l => Column(l, 0));
        _languageIds = languages.Select(l => l.Split(Separator)[2]).Distinct().ToList();
    }

    public bool Changed { get; private set; }

    public static GameTexts Open(string cacheFolder) => new(Lines(Path.Combine(cacheFolder, MessagesFile)), Lines(Path.Combine(cacheFolder, LanguagesFile)));

    /// <summary>Adds a text, the same in every language (names are not translated), and returns its id for a NameId field.</summary>
    public int Add(string text)
    {
        // A name never breaks the file: no line breaks, no column separator.
        text = text.Replace(Separator, " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Trim();
        var id = ++_lastMessage;
        _messages.Add(string.Join(Separator, Num(id), TeamSelectionScreen, text));
        foreach (var language in _languageIds)
        {
            _languages.Add(string.Join(Separator, Num(++_lastLanguageRow), Num(id), language, text) + Separator);
        }

        Changed = true;
        return id;
    }

    /// <summary>Both files as the game reads them: UTF-8 with a byte order mark, CRLF line ends.</summary>
    public Dictionary<string, byte[]> Files() => new()
    {
        [MessagesFile] = Bytes(_messages),
        [LanguagesFile] = Bytes(_languages),
    };

    private static List<string> Lines(string path) =>
        File.ReadAllText(path, Encoding.UTF8).Split("\r\n").Where(l => l.Length > 0).ToList();

    private static byte[] Bytes(IEnumerable<string> lines) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")];

    private static int Column(string line, int index) =>
        int.TryParse(line.Split(Separator)[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
}
