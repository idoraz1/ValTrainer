using System.Globalization;
using System.Text.Json;

namespace ValTrainer.Core;

/// <summary>
/// Dev self-test (<c>--dev --headless --i18n-check</c>, works on an exported build too): every shipped catalogue loads
/// and every entry formats with dummy arguments, plurals pick the right form per language, the VALORANT locale parser
/// handles the file's variations, numbers keep '.' in every language, and lookups stay consistent while another thread
/// switches the language. Prints coverage per language; exit code = failures (CultureCheck style).
/// </summary>
public static class I18nCheck
{
    static int failures;

    static void Check(string what, bool ok, string detail = "")
    {
        if (!ok) failures++;
        Godot.GD.Print($"[i18n-check] {(ok ? "ok  " : "FAIL")} {what}{(detail.Length > 0 ? " — " + detail : "")}");
    }

    public static int Run()
    {
        failures = 0;
        string start = L.Lang;

        // ---- VALORANT / Riot locale parsing ----
        Check("product settings: quoted", LangDetect.ParseProductSettings("locale_data:\n    default_locale: \"en_US\"\nsettings:\n    create_shortcut: false\n    locale: \"fr_FR\"\n    region: \"EU\"\n") == "fr_FR");
        Check("product settings: CRLF + BOM + unquoted", LangDetect.ParseProductSettings("﻿settings:\r\n    locale: pt_BR\r\n") == "pt_BR");
        Check("product settings: default_locale only is not a choice", LangDetect.ParseProductSettings("locale_data:\n    default_locale: \"de_DE\"\n") == null);
        Check("product settings: no settings block", LangDetect.ParseProductSettings("foo: 1\n") == null && LangDetect.ParseProductSettings("") == null);
        Check("riot client settings", LangDetect.ParseClientSettings("install:\n    globals:\n        locale: \"es_MX\"\n") == "es_MX");
        Check("locale mapping", LangDetect.Lang("fr_FR") == "fr" && LangDetect.Lang("es-MX") == "es" && LangDetect.Lang("pt_BR") == "pt-BR"
                                && L.Normalize(LangDetect.Lang("pt_PT")) == null && L.Normalize(LangDetect.Lang("ja_JP")) == null
                                && L.Normalize(LangDetect.Lang("de_AT")) == "de" && L.Normalize(LangDetect.Lang("en_GB")) == "en");

        // ---- every shipped catalogue ----
        foreach (var lang in L.Shipped.Where(l => l.Code != "en"))
        {
            string json = Godot.FileAccess.GetFileAsString($"res://translations/{lang.Code}.json");
            if (json.Length == 0) { Godot.GD.Print($"[i18n-check] --   {lang.Code}: no catalogue shipped yet (shows English)"); continue; }
            int entries = 0, bad = 0;
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Name.StartsWith('_')) continue;
                    entries++;
                    var args = Enumerable.Range(0, 10).Select(i => (object)(i + 0.5)).ToArray();
                    IEnumerable<string> forms = p.Value.ValueKind == JsonValueKind.Object
                        ? p.Value.EnumerateObject().Where(f => f.Value.ValueKind == JsonValueKind.String).Select(f => f.Value.GetString() ?? "")
                        : new[] { p.Value.GetString() ?? "" };
                    foreach (var f in forms)
                    {
                        if (f.Length == 0 || !p.Name.Contains('{')) continue; // plain text: never formatted
                        try { string.Format(CultureInfo.InvariantCulture, f, args); }
                        catch (FormatException) { bad++; Godot.GD.Print($"[i18n-check]      {lang.Code}: bad placeholders in \"{p.Name}\": \"{f}\""); }
                    }
                }
                Check($"{lang.Code}: {entries} entries parse and format", bad == 0, bad > 0 ? $"{bad} broken" : "");
            }
            catch (JsonException e) { Check($"{lang.Code}: valid JSON", false, e.Message); }
        }

        // ---- lookups, plurals, numbers per language ----
        L.Set("fr");
        if (!L.IsEnglish)
        {
            Check("fr: plural 0 and 1 are singular", L.P(0, $"{0} drill", $"{0} drills") == L.P(1, $"{0} drill", $"{0} drills") || !HasKey("{0} drill|{0} drills"));
            double v = 0.35;
            Check("fr: numbers keep '.'", L.F($"Sensitivity {v:0.00}").Contains("0.35"), L.F($"Sensitivity {v:0.00}"));
            Check("fr: a missing key shows English", L.T("zz no such key zz") == "zz no such key zz");
        }
        L.Set("en");
        Check("en: identity", L.T("SETTINGS") == "SETTINGS" && L.P(1, $"{1} drill", $"{1} drills") == "1 drill" && L.P(0, $"{0} drill", $"{0} drills") == "0 drills");

        // ---- pseudo-language ----
        Check("pseudo keeps holes", L.Pseudo("{0:0.0} of {1} flicks {{x}}").Contains("{0:0.0}") && L.Pseudo("{0:0.0} of {1} flicks {{x}}").Contains("{1}")
                                    && L.Pseudo("{0:0.0} of {1} flicks {{x}}").Contains("{{"), L.Pseudo("{0:0.0} of {1} flicks {{x}}"));
        Check("pseudo is longer", L.Pseudo("Settings").Length >= "Settings".Length * 1.3, L.Pseudo("Settings"));

        // ---- thread safety: worker lookups while the main thread switches the language ----
        int errors = 0;
        var worker = Task.Run(() =>
        {
            for (int i = 0; i < 200000; i++)
            {
                string t = L.F($"Bots react in ~{i % 300:0} ms");
                if (string.IsNullOrEmpty(t)) Interlocked.Increment(ref errors);
            }
        });
        for (int i = 0; i < 40 && !worker.IsCompleted; i++) L.Set(i % 2 == 0 ? "fr" : "en");
        worker.Wait();
        Check("lookups during language switches", errors == 0 && worker.Exception == null);

        L.Set(start == "en" ? "en" : start);
        Godot.GD.Print($"[i18n-check] {(failures == 0 ? "PASS" : $"{failures} FAILURE(S)")}");
        return failures;
    }

    static bool HasKey(string key)
    {
        string json = Godot.FileAccess.GetFileAsString("res://translations/fr.json");
        return json.Contains(JsonSerializer.Serialize(key), StringComparison.Ordinal);
    }
}
