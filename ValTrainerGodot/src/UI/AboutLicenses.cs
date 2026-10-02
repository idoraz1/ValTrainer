using System.Text;
using Godot;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>
/// The open-source notices shown in Settings → About → Licenses, and written by <c>--dev --print-licenses &lt;file&gt;</c>
/// (tools\build-release.ps1 builds THIRD-PARTY-NOTICES.txt from it): ValTrainer's GPL-3.0 notice, the Godot Engine
/// license, copyright and license texts of Godot's third-party components (straight from the engine, so they always
/// match the shipped build), the .NET runtime license and the CC0 asset credits (res://CREDITS.md).
/// </summary>
public static class AboutLicenses
{
    public const string GplNotice =
        "ValTrainer — a free, fan-made VALORANT-style aim trainer.\n" +
        "Copyright (C) 2026 ValTrainer contributors\n\n" +
        "This program is free software: you can redistribute it and/or modify it under the terms of the GNU General " +
        "Public License as published by the Free Software Foundation, either version 3 of the License, or (at your " +
        "option) any later version.\n\n" +
        "This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the " +
        "implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License " +
        "for more details.\n\n" +
        "You should have received a copy of the GNU General Public License along with this program (LICENSE.txt next " +
        "to ValTrainer.exe). If not, see <https://www.gnu.org/licenses/>.\n\n" +
        "ValTrainer is not affiliated with or endorsed by Riot Games. VALORANT is a trademark of Riot Games, Inc. " +
        "ValTrainer contains no Riot Games assets.";

    public const string DotNetLicense =
        "The MIT License (MIT)\n\n" +
        "Copyright (c) .NET Foundation and Contributors\n\n" +
        "All rights reserved.\n\n" +
        "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated " +
        "documentation files (the \"Software\"), to deal in the Software without restriction, including without " +
        "limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the " +
        "Software, and to permit persons to whom the Software is furnished to do so, subject to the following " +
        "conditions:\n\n" +
        "The above copyright notice and this permission notice shall be included in all copies or substantial portions " +
        "of the Software.\n\n" +
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED " +
        "TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL " +
        "THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF " +
        "CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER " +
        "DEALINGS IN THE SOFTWARE.";

    public enum Part { ValTrainer, Godot, GodotThirdParty, DotNet, Assets }

    static string? text;
    static readonly Dictionary<Part, int> lines = new();

    /// <summary>All notices as plain text (built once).</summary>
    public static string Text
    {
        get
        {
            if (text == null) Build();
            return text!;
        }
    }

    /// <summary>First line of a part in <see cref="Text"/> (for the jump buttons).</summary>
    public static int LineOf(Part p)
    {
        if (text == null) Build();
        return lines.TryGetValue(p, out var l) ? l : 0;
    }

    static void Build()
    {
        var sb = new StringBuilder();
        void Heading(Part p, string title)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            lines[p] = Count(sb);
            sb.Append(new string('=', 64)).Append('\n').Append(title).Append('\n').Append(new string('=', 64)).Append("\n\n");
        }

        Heading(Part.ValTrainer, $"VALTRAINER {AppInfo.Version} — GNU GENERAL PUBLIC LICENSE v3");
        sb.Append(GplNotice);
        if (AppInfo.HasRepo) sb.Append("\n\nSource code: ").Append(AppInfo.RepoUrl);

        Heading(Part.Godot, $"GODOT ENGINE {Safe(() => Engine.GetVersionInfo()["string"].AsString())} — MIT LICENSE (https://godotengine.org/license)");
        sb.Append(Safe(Engine.GetLicenseText).Trim());

        Heading(Part.GodotThirdParty, "THIRD-PARTY COMPONENTS INCLUDED IN THE GODOT ENGINE");
        AppendCopyright(sb);

        Heading(Part.DotNet, ".NET RUNTIME — MIT LICENSE (https://github.com/dotnet/runtime)");
        sb.Append(DotNetLicense);
        sb.Append("\n\nThe .NET runtime includes third-party components; their notices are in THIRD-PARTY-NOTICES.txt next to " +
                  "ValTrainer.exe and at https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT");

        Heading(Part.Assets, "MODELS, TEXTURES AND SOUNDS — CC0 1.0 (PUBLIC DOMAIN)");
        sb.Append(Credits());
        text = sb.ToString();
    }

    static int Count(StringBuilder sb)
    {
        int n = 0;
        for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') n++;
        return n;
    }

    static void AppendCopyright(StringBuilder sb)
    {
        try
        {
            foreach (var item in Engine.GetCopyrightInfo())
            {
                sb.Append("- ").Append(item["name"].AsString()).Append('\n');
                var parts = item["parts"].AsGodotArray();
                foreach (var partV in parts)
                {
                    var part = partV.AsGodotDictionary();
                    if (parts.Count > 1 && part.ContainsKey("files"))
                        sb.Append("    Files: ").Append(string.Join(", ", part["files"].AsStringArray())).Append('\n');
                    foreach (var c in part["copyright"].AsStringArray()) sb.Append("    © ").Append(c).Append('\n');
                    sb.Append("    License: ").Append(part["license"].AsString()).Append('\n');
                }
            }
            sb.Append("\nLicense texts:\n");
            var info = Engine.GetLicenseInfo();
            foreach (var key in info.Keys.Select(k => k.AsString()).OrderBy(k => k, StringComparer.Ordinal))
                sb.Append("\n----- ").Append(key).Append(" -----\n\n").Append(info[key].AsString().Trim()).Append('\n');
        }
        catch (Exception e)
        {
            sb.Append($"(couldn't read the engine's copyright information: {e.Message}; see https://godotengine.org/license)");
        }
    }

    /// <summary>CREDITS.md (exported with the game) as plain text: markdown links, emphasis and tables simplified.</summary>
    static string Credits()
    {
        string md = "";
        try { if (Godot.FileAccess.FileExists("res://CREDITS.md")) md = Godot.FileAccess.GetFileAsString("res://CREDITS.md"); } catch { }
        if (md.Length == 0) return "See CREDITS.md in the source code.";
        var sb = new StringBuilder();
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            var l = raw.TrimEnd();
            if (l.StartsWith("|---") || l.StartsWith("| ---")) continue;
            if (l.StartsWith('|'))
            {
                var cells = l.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (cells.Length >= 2 && cells[0] == "asset") continue;
                l = cells.Length >= 2 ? $"- {cells[0]} by {cells[1]}" : l;
            }
            l = l.TrimStart('#').TrimStart();
            l = Markdown.Plain(l);
            sb.Append(l).Append('\n');
        }
        return sb.ToString().Trim();
    }

    static string Safe(Func<string> f)
    {
        try { return f() ?? ""; } catch { return ""; }
    }

    /// <summary><c>--dev --print-licenses &lt;file&gt;</c> (works with --headless): writes <see cref="Text"/> as UTF-8.</summary>
    public static bool WriteFile(string path)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(full, Text.Replace("\n", "\r\n"), new UTF8Encoding(false));
            Log.Info($"Wrote license notices to {full}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"--print-licenses failed: {e.Message}");
            return false;
        }
    }
}

/// <summary>Tiny inline-markdown cleanup for plain-text display (links, bold, code).</summary>
public static class Markdown
{
    static readonly System.Text.RegularExpressions.Regex Link = new(@"\[([^\]]+)\]\(([^)\s]+)\)");
    static readonly System.Text.RegularExpressions.Regex Auto = new(@"<(https?://[^>\s]+)>");
    static readonly System.Text.RegularExpressions.Regex Bold = new(@"\*\*([^*]+)\*\*|__([^_]+)__");
    static readonly System.Text.RegularExpressions.Regex Code = new(@"`([^`]+)`");

    /// <summary>"[text](url)" → "text (url)" when <paramref name="keepUrls"/>, else "text"; drops ** __ and backticks.</summary>
    public static string Plain(string s, bool keepUrls = true)
    {
        s = Link.Replace(s, m => keepUrls && m.Groups[2].Value.StartsWith("http") ? $"{m.Groups[1].Value} ({m.Groups[2].Value})" : m.Groups[1].Value);
        s = Auto.Replace(s, "$1");
        s = Bold.Replace(s, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        s = Code.Replace(s, "$1");
        return s;
    }
}
