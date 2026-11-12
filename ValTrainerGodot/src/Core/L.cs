using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ValTrainer.Core;

/// <summary>
/// UI language (i18n). The English source text is the key: <c>L.T("SETTINGS")</c>, <c>L.F($"{n:0} of 30 flicks")</c>
/// (key = the composite format "{0:0} of 30 flicks"), <c>L.P(n, $"{n} drill", $"{n} drills")</c>. Catalogues are flat
/// JSON files, res://translations/&lt;code&gt;.json shipped with the game, with &lt;DataDir&gt;\translations\&lt;code&gt;.json merged
/// on top (community fixes without a rebuild). A missing entry shows the English text, never a blank.
/// <para>Numbers keep the invariant culture ('.' decimals) in every language: only the words change.</para>
/// <para>Thread-safe: the table is immutable and swapped as one reference, so the coach's worker threads see the old or
/// the new language, never half of each. Lookups are one dictionary read; English does no lookup at all.</para>
/// <para>The shared draw helpers (<see cref="UI.Gfx"/> Text*, VButton, SectionLabel, VPanel, the Settings rows …) call
/// <see cref="T"/> on what they draw, so plain literals there need no wrapping. Text that must never be looked up
/// (crosshair codes, names, paths, map callouts, key names, release notes) goes through <see cref="Raw"/>.</para>
/// <para>Dev: <c>--lang fr|xx</c> forces a language (xx = pseudo-language: every catalogued string accented and ~35%
/// longer, to find unwrapped text and overflow), <c>--riot-locale fr_FR</c> fakes VALORANT's language,
/// <c>--i18n-missing file</c> writes the strings that had no translation on exit.</para>
/// </summary>
public static class L
{
    /// <summary>A language in the picker. <see cref="Name"/> is the endonym (always shown in its own language).</summary>
    public sealed record Language(string Code, string Name, string English);

    /// <summary>The languages offered in Settings, in picker order ("en" needs no file: the keys are English).</summary>
    public static readonly Language[] Shipped =
    {
        new("en", "English", "English"),
        new("fr", "Français", "French"),
        new("es", "Español", "Spanish"),
        new("pt-BR", "Português (Brasil)", "Portuguese (Brazil)"),
        new("de", "Deutsch", "German"),
    };

    /// <summary>Dev pseudo-language (--lang xx): the English catalogue, accented and ~35% longer, in [brackets].</summary>
    public const string PseudoCode = "xx";

    sealed class Plural
    {
        public required string One, Other;
    }

    sealed class Table
    {
        public required string Code;
        /// <summary>English: no lookups at all.</summary>
        public bool Identity;
        /// <summary>"machine" | "reviewed" | "community" (from the file's _meta.status); "" for English / no file.</summary>
        public string Status = "";
        public FrozenDictionary<string, string> Text = FrozenDictionary<string, string>.Empty;
        public FrozenDictionary<string, Plural> Plurals = FrozenDictionary<string, Plural>.Empty;
        /// <summary>Every translated text: drawing an already translated string looks it up again (a miss, harmless);
        /// these are not reported as missing.</summary>
        public FrozenSet<string> Values = FrozenSet<string>.Empty;
        public CultureInfo Culture = CultureInfo.InvariantCulture;
        /// <summary>French and Brazilian Portuguese use the singular for 0 too (CLDR, integer counts).</summary>
        public bool ZeroIsOne;
    }

    static readonly Table English = new() { Code = "en", Identity = true };
    static volatile Table cur = English;

    /// <summary>The language in use: "en", "fr", "es", "pt-BR", "de" (or "xx" in dev runs).</summary>
    public static string Lang => cur.Code;
    /// <summary>True for English: nothing is looked up and layouts behave exactly as before translations existed.</summary>
    public static bool IsEnglish => cur.Identity;
    /// <summary>"machine" | "reviewed" | "community" for the language in use ("" for English or a missing file).</summary>
    public static string Status => cur.Status;

    /// <summary>Fired on the main thread after <see cref="Set"/> switched the language (screens rebuild on it).</summary>
    public static event Action? Changed;

    /// <summary>What Auto follows: the detected language and where it came from ("VALORANT", "Windows" or null = the
    /// English fallback). Filled by <see cref="Init"/>.</summary>
    public static string AutoCode { get; private set; } = "en";
    public static string? AutoSource { get; private set; }
    /// <summary>Dev --lang: the forced language (null = the setting decides).</summary>
    public static string? Forced { get; private set; }

    /// <summary>Picks the language once at startup (dev --lang, else the setting, else Auto). Main thread, after the
    /// settings loaded and before the first screen.</summary>
    public static void Init(AppSettings s)
    {
        (AutoCode, AutoSource) = LangDetect.Detect();
        if (CmdLine.DevAfter("--i18n-missing") != null) missing = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        Forced = CmdLine.DevAfter("--lang") is { Length: > 0 } f ? Normalize(f, allowPseudo: true) : null;
        Apply(Forced ?? s.Language);
        if (cur.Code != "en") Log.Info($"UI language {cur.Code}{(Forced != null ? " (dev --lang)" : s.Language == null ? $" (auto: {AutoSource ?? "default"})" : "")}");
    }

    /// <summary>Switches the language (null = Auto; Settings → Language) and fires <see cref="Changed"/>. Main thread
    /// (reads res://). Replaces a dev --lang.</summary>
    public static void Set(string? code)
    {
        Forced = null;
        Apply(code);
    }

    static void Apply(string? code)
    {
        string c = Resolve(code);
        cur = c == "en" ? English : Load(c);
        Changed?.Invoke();
    }

    /// <summary>The effective language for a setting value (null = Auto).</summary>
    public static string Resolve(string? code) => code == null ? AutoCode : Normalize(code, CmdLine.Dev) ?? "en";

    /// <summary>"fr_FR" / "FR" / "pt-br" → a shipped code ("fr", "pt-BR"); null when not shipped.</summary>
    public static string? Normalize(string code, bool allowPseudo = false)
    {
        var c = code.Trim().Replace('_', '-');
        if (allowPseudo && c.Equals(PseudoCode, StringComparison.OrdinalIgnoreCase)) return PseudoCode;
        foreach (var l in Shipped)
            if (l.Code.Equals(c, StringComparison.OrdinalIgnoreCase)) return l.Code;
        return null;
    }

    public static Language? Info(string code) => Array.Find(Shipped, l => l.Code == code);

    // =====================================================================================================
    // Lookups
    // =====================================================================================================

    /// <summary>Plain text: the translation of <paramref name="en"/>, or <paramref name="en"/> itself.</summary>
    [return: NotNullIfNotNull(nameof(en))]
    public static string? T(string? en)
    {
        var t = cur;
        if (t.Identity || string.IsNullOrEmpty(en)) return en;
        if (rawCount > 0 && raw.TryGetValue(en, out _)) return en;
        if (t.Text.TryGetValue(en, out var v)) return v;
        Miss(en);
        return en;
    }

    /// <summary>"Translatable, but translated later where it is shown" (gettext's N_): returns <paramref name="en"/>
    /// unchanged and only marks it for the extractor. For English data tables and values that reach a draw helper
    /// (which translates) or an L.T later: <c>static readonly string[] Modes = { L.N("Windowed"), … }</c>. Safe in static
    /// initializers (unlike L.T, which would freeze the startup language there).</summary>
    public static string N(string en) => en;

    /// <summary>A short word that needs a context to translate ("toggle|ON"); falls back to <see cref="T"/>.</summary>
    public static string C(string ctx, string en)
    {
        var t = cur;
        if (t.Identity) return en;
        return t.Text.TryGetValue(ctx + "|" + en, out var v) ? v : T(en);
    }

    /// <summary>An interpolated string: <c>L.F($"{n:0} of 30 flicks")</c>. The key is the composite format
    /// ("{0:0} of 30 flicks"); numbers are formatted in the invariant culture ('.' decimals) like the rest of the app.
    /// English text inside the holes needs its own <c>L.T</c>. A broken translation (bad placeholder) shows English.</summary>
    public static string F(FormattableString s)
    {
        var t = cur;
        if (!t.Identity)
        {
            if (t.Text.TryGetValue(s.Format, out var tr))
            {
                try { return string.Format(CultureInfo.InvariantCulture, tr, s.GetArguments()); }
                catch (FormatException) { BadFormat(s.Format, tr); }
            }
            else Miss(s.Format);
        }
        return s.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A count with singular / plural forms: <c>L.P(n, $"{n} drill", $"{n} drills")</c>. Both forms must have
    /// the same holes (write the number as a hole in the singular too). Key = one.Format + "|" + other.Format.</summary>
    public static string P(long n, FormattableString one, FormattableString other)
    {
        var t = cur;
        if (!t.Identity)
        {
            string key = one.Format + "|" + other.Format;
            if (t.Plurals.TryGetValue(key, out var pl))
            {
                bool single = n == 1 || (n == 0 && t.ZeroIsOne);
                var args = other.ArgumentCount >= one.ArgumentCount ? other.GetArguments() : one.GetArguments();
                try { return string.Format(CultureInfo.InvariantCulture, single ? pl.One : pl.Other, args); }
                catch (FormatException) { BadFormat(key, single ? pl.One : pl.Other); }
            }
            else Miss(key);
        }
        return (n == 1 ? one : other).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Upper case for the UI language (Turkish i → İ); never touches the process culture. Use it on translated
    /// display text instead of ToUpperInvariant: <c>L.Upper(L.T(mode.Name))</c>.</summary>
    public static string Upper(string s) => s.ToUpper(cur.Culture);

    /// <summary>Marks text that must never be translated (crosshair codes, player / crosshair names, paths, map callouts,
    /// key names, release notes) and returns the same text (use the returned string: for an interned literal it is a marked
    /// copy): the draw helpers and <see cref="T"/> then leave it alone.</summary>
    public static string Raw(string s)
    {
        if (string.IsNullOrEmpty(s) || raw.TryGetValue(s, out _)) return s;
        // An interned literal ("VALORANT default", "Custom code" as a crosshair name) is the same instance as every other
        // use of that text: marking it would stop its translation everywhere (as a source tag, a note, a drill name). Mark
        // a private copy instead (one per text, so the lookup above finds it next time).
        if (ReferenceEquals(string.IsInterned(s), s))
        {
            s = rawCopies.GetOrAdd(s, x => new string(x.AsSpan()));
            if (raw.TryGetValue(s, out _)) return s;
        }
        raw.AddOrUpdate(s, Marker);
        Interlocked.Increment(ref rawCount);
        return s;
    }

    public static bool IsRaw(string s) => rawCount > 0 && raw.TryGetValue(s, out _);

    // ConditionalWeakTable: per string instance, collected with it; safe from any thread.
    static readonly ConditionalWeakTable<string, object> raw = new();
    static readonly object Marker = new();
    static readonly ConcurrentDictionary<string, string> rawCopies = new(StringComparer.Ordinal);
    static int rawCount;

    // =====================================================================================================
    // Loading
    // =====================================================================================================

    static Table Load(string code)
    {
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        var plurals = new Dictionary<string, Plural>(StringComparer.Ordinal);
        string status = "";
        bool pseudo = code == PseudoCode;
        // xx: every key of the extracted English catalogue (translations/en.json), pseudo-translated
        string file = pseudo ? "en" : code;
        foreach (var json in Sources(file))
            status = Merge(json, text, plurals, pseudo) ?? status;
        if (pseudo)
        {
            foreach (var k in text.Keys.ToList()) text[k] = Pseudo(text[k]);
            foreach (var k in plurals.Keys.ToList()) plurals[k] = new Plural { One = Pseudo(plurals[k].One), Other = Pseudo(plurals[k].Other) };
            status = "dev";
        }
        if (text.Count == 0 && plurals.Count == 0) Log.Error($"No translations found for \"{code}\" (translations/{file}.json): showing English");
        CultureInfo culture;
        try { culture = pseudo ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
        return new Table
        {
            Code = code, Status = status, Culture = culture, ZeroIsOne = code is "fr" or "pt-BR",
            Text = text.ToFrozenDictionary(StringComparer.Ordinal), Plurals = plurals.ToFrozenDictionary(StringComparer.Ordinal),
            Values = missing == null ? FrozenSet<string>.Empty : text.Values.ToFrozenSet(StringComparer.Ordinal),
        };
    }

    /// <summary>The shipped catalogue, then the player's override file (merged on top).</summary>
    static IEnumerable<string> Sources(string code)
    {
        string shipped = "";
        try { shipped = Godot.FileAccess.GetFileAsString($"res://translations/{code}.json"); }
        catch (Exception e) { Log.Error($"translations/{code}.json couldn't be read: {e.Message}"); }
        if (shipped.Length > 0) yield return shipped;
        string over = "";
        try
        {
            var p = Path.Combine(Paths.DataDir, "translations", code + ".json");
            if (File.Exists(p)) over = File.ReadAllText(p);
        }
        catch (Exception e) { Log.Error($"Translation override for {code} couldn't be read: {e.Message}"); }
        if (over.Length > 0) yield return over;
    }

    /// <summary>Adds a catalogue's entries (an empty value = "use English" and is skipped). Returns _meta.status.</summary>
    static string? Merge(string json, Dictionary<string, string> text, Dictionary<string, Plural> plurals, bool keepEmpty)
    {
        string? status = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Name == "_meta")
                {
                    if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
                        status = st.GetString();
                    continue;
                }
                if (p.Name.StartsWith('_')) continue;
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    var v = p.Value.GetString();
                    if (!string.IsNullOrEmpty(v)) text[p.Name] = v;
                    else if (keepEmpty) text[p.Name] = p.Name;
                }
                else if (p.Value.ValueKind == JsonValueKind.Object
                         && p.Value.TryGetProperty("one", out var one) && one.ValueKind == JsonValueKind.String
                         && p.Value.TryGetProperty("other", out var other) && other.ValueKind == JsonValueKind.String
                         && one.GetString() is { Length: > 0 } o && other.GetString() is { Length: > 0 } x)
                    plurals[p.Name] = new Plural { One = o, Other = x };
            }
        }
        catch (JsonException e) { Log.Error($"A translation file is not valid JSON ({e.Message}): its entries are skipped"); }
        return status;
    }

    // =====================================================================================================
    // Pseudo-language (dev --lang xx)
    // =====================================================================================================

    const string PseudoFrom = "acdeghijklnorstuwyzACDEGHIJKLNORSTUWYZ";
    const string PseudoTo = "àçďéĝĥîĵķĺñöŕšţûŵýžÀÇĎÉĜĤÎĴĶĹÑÖŔŠŢÛŴÝŽ";

    /// <summary>"Settings" → "[Šéţţîñĝš Šéţ]": accented (only glyphs every UI font has) and ~35% longer, with
    /// {0:0.0} holes and doubled braces kept intact. Text without letters stays as it is.</summary>
    public static string Pseudo(string s)
    {
        var sb = new StringBuilder(s.Length * 2 + 4);
        var letters = new StringBuilder();
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (depth == 0 && (c == '{' || c == '}') && i + 1 < s.Length && s[i + 1] == c) { sb.Append(c).Append(c); i++; continue; }
            if (c == '{') depth++;
            if (depth > 0) { sb.Append(c); if (c == '}') depth--; continue; }
            int k = PseudoFrom.IndexOf(c);
            char o = k >= 0 ? PseudoTo[k] : c;
            sb.Append(o);
            if (char.IsLetter(c)) letters.Append(o);
        }
        if (letters.Length == 0) return s;
        int pad = Math.Max(1, (int)MathF.Ceiling(letters.Length * 0.35f) - 2);
        sb.Append(' ');
        for (int i = 0; i < pad; i++) sb.Append(letters[i % letters.Length]);
        return "[" + sb + "]";
    }

    // =====================================================================================================
    // Diagnostics
    // =====================================================================================================

    static ConcurrentDictionary<string, byte>? missing;
    static readonly ConcurrentDictionary<string, byte> badFormats = new(StringComparer.Ordinal);

    static void Miss(string en)
    {
        if (missing == null || missing.Count > 50000 || cur.Values.Contains(en)) return;
        foreach (char c in en)
            if (char.IsLetter(c)) { missing.TryAdd(en, 0); return; }
    }

    static void BadFormat(string key, string tr)
    {
        if (badFormats.TryAdd(key, 0)) Log.Error($"[i18n] {cur.Code}: broken placeholders in the translation of \"{key}\": \"{tr}\" (showing English)");
    }

    /// <summary>Dev --i18n-missing &lt;file&gt;: writes every string drawn or looked up this run that had no translation
    /// (sorted). Called on exit.</summary>
    public static void WriteMissing()
    {
        if (missing == null || CmdLine.DevAfter("--i18n-missing") is not { } file) return;
        try
        {
            File.WriteAllLines(Path.GetFullPath(file), missing.Keys.OrderBy(x => x, StringComparer.Ordinal));
            Log.Info($"[i18n] {missing.Count} untranslated strings ({cur.Code}) written to {file}");
        }
        catch (Exception e) { Log.Error($"[i18n] couldn't write {file}: {e.Message}"); }
    }
}
