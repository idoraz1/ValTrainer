using System.Text.RegularExpressions;

namespace ValTrainer.Core;

/// <summary>
/// "Auto" language: VALORANT's own language, else the Windows display language, else English.
/// <para>VALORANT's language is read (read-only, shared, once at startup) from the Riot Client's product settings,
/// <c>C:\ProgramData\Riot Games\Metadata\valorant.live\valorant.live.product_settings.yaml</c> → <c>settings: locale:
/// "fr_FR"</c>; the Riot Client's own settings (<c>%LOCALAPPDATA%\Riot Games\Riot Client\Config\RiotClientSettings.yaml</c>)
/// are the fallback. Nothing is ever written and no process is touched.</para>
/// <para>Dev runs with <c>--valorant-dir</c> (every test tool) skip the Riot files and Windows so results don't depend on
/// the PC; <c>--riot-locale fr_FR</c> fakes VALORANT's language.</para>
/// </summary>
public static class LangDetect
{
    /// <summary>Detected language (a shipped code) and its source: "VALORANT", "Windows" or null (English fallback).</summary>
    public static (string Code, string? Source) Detect()
    {
        bool isolated = CmdLine.DevAfter("--valorant-dir") != null;
        string? riot = CmdLine.DevAfter("--riot-locale");
        if (riot == null && !isolated)
        {
            try { riot = RiotLocale(); }
            catch (Exception e) { Log.Error($"VALORANT's language couldn't be read: {e.Message}"); }
        }
        if (riot != null && L.Normalize(Lang(riot)) is { } fromRiot) return (fromRiot, "VALORANT");
        if (!isolated && L.Normalize(Lang(Boot.SystemUiCulture)) is { } fromWindows) return (fromWindows, "Windows");
        return ("en", null);
    }

    /// <summary>Maps a Riot / Windows locale to the catalogue it uses: "fr_FR" → "fr", "es_MX" → "es", "pt_BR" → "pt-BR",
    /// "de_AT" → "de". Portuguese other than Brazil's has no catalogue yet ("pt-PT" stays itself, i.e. not shipped).</summary>
    public static string Lang(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return "";
        var c = locale.Trim().Replace('_', '-');
        int dash = c.IndexOf('-');
        string lang = (dash < 0 ? c : c[..dash]).ToLowerInvariant();
        return lang == "pt" ? (c.Equals("pt-BR", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : c) : lang;
    }

    static string? RiotLocale()
    {
        string pd = System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData);
        if (pd.Length > 0 && ParseProductSettings(ReadShared(Path.Combine(pd, "Riot Games", "Metadata", "valorant.live", "valorant.live.product_settings.yaml"))) is { } l)
            return l;
        string la = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        return la.Length > 0 ? ParseClientSettings(ReadShared(Path.Combine(la, "Riot Games", "Riot Client", "Config", "RiotClientSettings.yaml"))) : null;
    }

    /// <summary>The whole file (small), opened read-only and shared (the Riot Client may hold it open); null if missing.</summary>
    static string? ReadShared(string path)
    {
        if (!File.Exists(path)) return null;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > 1 << 20) return null;
        using var r = new StreamReader(fs);
        return r.ReadToEnd();
    }

    static readonly Regex LocaleLine = new(@"^\s+locale:\s*[""']?([A-Za-z]{2,3}_[A-Za-z]{2,4})[""']?\s*$", RegexOptions.CultureInvariant);

    /// <summary><c>settings:</c> (top level) → its indented <c>locale:</c> line. No YAML library: lines only.</summary>
    public static string? ParseProductSettings(string? text) => IndentedLocale(text, "settings:");

    /// <summary>RiotClientSettings.yaml: <c>install:</c> → a nested <c>locale:</c> (globals: or localization:).</summary>
    public static string? ParseClientSettings(string? text) => IndentedLocale(text, "install:");

    static string? IndentedLocale(string? text, string block)
    {
        if (string.IsNullOrEmpty(text)) return null;
        bool inBlock = false;
        foreach (var raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;
            if (!char.IsWhiteSpace(line[0])) { inBlock = line.TrimEnd() == block; continue; }
            if (inBlock && LocaleLine.Match(line) is { Success: true } m) return m.Groups[1].Value;
        }
        return null;
    }
}
