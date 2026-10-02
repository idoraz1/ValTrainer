using System.Text.RegularExpressions;
using Godot;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>One "## [x.y.z] - date" section of CHANGELOG.md (Keep a Changelog 1.1.0).</summary>
public sealed record ChangelogSection(string Version, string? Date, List<string> Lines);

/// <summary>
/// "What's new" after an update: on the first launch of a newer version than the one last seen (never on a fresh
/// install) the menu shows the CHANGELOG sections since then. The changelog ships inside the game as
/// res://CHANGELOG.md (tools\build-release.ps1 copies the repo's CHANGELOG.md in before exporting); runs without it
/// (from source) skip the panel silently. Dev: <c>--dev --whats-new [fromVersion]</c> pretends the last seen version
/// was older (default 0.0.0).
/// </summary>
public static class WhatsNew
{
    /// <summary>Sections to show on the menu (newest first); null = nothing to show / already dismissed.</summary>
    public static List<ChangelogSection>? Pending;

    const int MaxSections = 6;

    /// <summary>Called once from Main._Ready (after the settings are loaded).</summary>
    public static void Prepare(AppSettings s)
    {
        string cur = AppInfo.Version;
        string? from = s.LastSeenVersion;
        bool force = CmdLine.Dev && CmdLine.Has("--whats-new");
        if (force) from = SemVer.Normalize(CmdLine.After("--whats-new")) ?? "0.0.0";
        // Automated runs on the real settings (read-only): only on request. A dev run with its own writable data folder
        // (--data-dir … --write-data) behaves like a normal launch, so updates can be tested end to end.
        else if (CmdLine.Dev && !Main.I.SavesData) return;
        else
        {
            if (s.IsNew)
            {
                // fresh install: nothing is "new" yet
                s.LastSeenVersion = cur;
                s.Save();
                return;
            }
            if (from == cur) return;
            s.LastSeenVersion = cur;
            s.Save();
            if (from != null && SemVer.Compare(cur, from) <= 0) return; // same or older build: nothing to announce
        }
        // Updated (or a settings file from before versions were tracked: show this version's notes only).
        var md = Read();
        if (md == null) return;
        var list = Parse(md)
            .Where(x => SemVer.Compare(x.Version, cur) <= 0 && (from == null ? SemVer.Compare(x.Version, cur) == 0 : SemVer.Compare(x.Version, from) > 0))
            .Where(x => x.Lines.Any(l => l.Trim().Length > 0))
            .OrderByDescending(x => x.Version, Comparer<string>.Create(SemVer.Compare))
            .Take(MaxSections)
            .ToList();
        if (list.Count > 0) Pending = list;
    }

    static string? Read()
    {
        try
        {
            return Godot.FileAccess.FileExists("res://CHANGELOG.md") ? Godot.FileAccess.GetFileAsString("res://CHANGELOG.md") : null;
        }
        catch { return null; }
    }

    static readonly Regex VersionHeading = new(@"^##\s+\[?v?([0-9][^\]\s]*)\]?\s*(?:[-–—]\s*(.+?))?\s*$");
    static readonly Regex LinkRef = new(@"^\[[^\]]+\]:\s*\S+");

    /// <summary>Version sections of a Keep-a-Changelog file ("Unreleased" and the link references are skipped).</summary>
    public static List<ChangelogSection> Parse(string md)
    {
        var result = new List<ChangelogSection>();
        ChangelogSection? cur = null;
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            if (raw.StartsWith("## ") || raw.StartsWith("##\t"))
            {
                var m = VersionHeading.Match(raw.Trim());
                cur = m.Success && SemVer.TryParse(m.Groups[1].Value, out _)
                    ? new ChangelogSection(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null, new List<string>())
                    : null;
                if (cur != null) result.Add(cur);
                continue;
            }
            if (raw.StartsWith("# ") || LinkRef.IsMatch(raw)) { cur = null; continue; }
            cur?.Lines.Add(raw.TrimEnd());
        }
        return result;
    }
}

/// <summary>Modal "What's new" panel over the menu (dim backdrop, scrollable notes, GOT IT / Esc closes it).</summary>
public partial class WhatsNewPanel : Control
{
    readonly List<ChangelogSection> sections;
    readonly float k;
    readonly Action onClose;

    public WhatsNewPanel(List<ChangelogSection> sections, float k, Action onClose)
    {
        this.sections = sections;
        this.k = k;
        this.onClose = onClose;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the menu underneath doesn't get clicks
    }

    public override void _Ready()
    {
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.72f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var vs = GetViewportRect().Size;
        var panel = new VPanel
        {
            Title = "WHAT'S NEW", Caption = $"ValTrainer {AppInfo.Version}", K = k, Fill = new Color(0.07f, 0.11f, 0.15f, 0.97f),
            CustomMinimumSize = new Vector2(Mathf.Min(960 * k, vs.X - 80 * k), Mathf.Min(760 * k, vs.Y - 80 * k)),
        };
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(14 * k));
        panel.AddChild(col);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", (int)(6 * k));
        scroll.AddChild(body);
        bool first = true;
        foreach (var s in sections)
        {
            if (!first) body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14 * k) });
            first = false;
            body.AddChild(VersionHeader(s));
            Render(body, s.Lines);
        }

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(14 * k));
        if (AppInfo.HasRepo)
        {
            var all = new VButton { Label = "ALL RELEASES ON GITHUB", Kind = VButton.Look.Ghost, K = k, FontPx = 16, CustomMinimumSize = new Vector2(230 * k, 40 * k),
                SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Opens the releases page in your browser" };
            all.Pressed += () => AppInfo.OpenGitHub(AppInfo.ReleasesUrl);
            foot.AddChild(all);
        }
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var ok = new VButton { Label = "GOT IT", Kind = VButton.Look.Primary, K = k, FontPx = 20, CustomMinimumSize = new Vector2(180 * k, 50 * k) };
        ok.Pressed += () => onClose();
        foot.AddChild(ok);
        col.AddChild(foot);
    }

    Control VersionHeader(ChangelogSection s)
    {
        var d = new DrawBox { CustomMinimumSize = new Vector2(0, 46 * k) };
        string ver = s.Version, date = s.Date ?? "";
        d.OnDraw = b =>
        {
            int fs = UiTheme.Fs(34, k), ds = UiTheme.Fs(14, k);
            float y = b.Size.Y - 12 * k;
            Gfx.Text(b, UiTheme.Display, ver, 0, y, fs, UiTheme.Text);
            float w = Gfx.TextW(UiTheme.Display, ver, fs);
            if (date.Length > 0) Gfx.Text(b, UiTheme.HudWide, date.ToUpperInvariant(), w + 14 * k, y, ds, UiTheme.Dim);
            b.DrawRect(new Rect2(0, b.Size.Y - 2 * k, b.Size.X, 1), new Color(UiTheme.Text, 0.09f));
            b.DrawRect(new Rect2(0, b.Size.Y - 3 * k, 40 * k, 2 * k), UiTheme.Accent);
        };
        return d;
    }

    static Color KindColor(string h) => h.ToLowerInvariant() switch
    {
        "added" => UiTheme.Good,
        "fixed" => UiTheme.Warn,
        "removed" or "security" => UiTheme.Accent,
        "deprecated" => UiTheme.Dim,
        _ => UiTheme.Teal,
    };

    /// <summary>Minimal markdown: ### subheadings, -/* bullets (indented = nested, wrapped lines continue the bullet), paragraphs.</summary>
    void Render(VBoxContainer body, List<string> lines)
    {
        Label? last = null;
        foreach (var raw in lines)
        {
            if (raw.Trim().Length == 0) { last = null; continue; }
            string t = raw.TrimStart();
            int indent = raw.Length - t.Length;
            if (t.StartsWith('#'))
            {
                string h = Markdown.Plain(t.TrimStart('#').Trim(), false);
                var col = KindColor(h);
                string cap = h.ToUpperInvariant();
                body.AddChild(new DrawBox
                {
                    CustomMinimumSize = new Vector2(0, 34 * k),
                    OnDraw = b =>
                    {
                        float cy = b.Size.Y / 2 + 3 * k;
                        int fs = UiTheme.Fs(14, k);
                        b.DrawRect(new Rect2(0, cy - 3 * k, 6 * k, 6 * k), col);
                        float w = Gfx.TextW(UiTheme.HudWide, cap, fs);
                        Gfx.Text(b, UiTheme.HudWide, cap, 14 * k, Gfx.Mid(cy, fs), fs, col);
                        float lx = 14 * k + w + 14 * k;
                        if (b.Size.X > lx) b.DrawRect(new Rect2(lx, cy, b.Size.X - lx, 1), new Color(UiTheme.Text, 0.08f));
                    },
                });
                last = null;
                continue;
            }
            bool bullet = t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ ");
            if (!bullet && last != null)
            {
                last.Text += " " + Markdown.Plain(t, false); // wrapped continuation of the previous line
                continue;
            }
            string txt = Markdown.Plain(bullet ? t[2..].Trim() : t, false);
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", (int)(10 * k));
            float pad = (bullet ? 12 : 0) * k + Mathf.Min(indent, 8) / 2 * 18 * k;
            row.AddChild(new Control { CustomMinimumSize = new Vector2(pad, 0), MouseFilter = MouseFilterEnum.Ignore });
            if (bullet)
            {
                var dot = new DrawBox { CustomMinimumSize = new Vector2(8 * k, 22 * k) };
                bool nested = indent > 0;
                dot.OnDraw = b => Gfx.Diamond(b, new Vector2(4 * k, 12 * k), 3.5f * k, 3.5f * k, nested ? UiTheme.Dim : UiTheme.Accent);
                row.AddChild(dot);
            }
            var lbl = new Label
            {
                Text = txt, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                LabelSettings = UiTheme.Label(UiTheme.Body, 16, k, bullet ? UiTheme.Text : UiTheme.Dim), MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(40 * k, 0),
            };
            row.AddChild(lbl);
            body.AddChild(row);
            last = lbl;
        }
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.Enter or Key.KpEnter })
        {
            GetViewport().SetInputAsHandled();
            onClose();
        }
    }
}
