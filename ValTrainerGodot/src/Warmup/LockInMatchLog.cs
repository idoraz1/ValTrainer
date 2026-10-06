using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// Personal match log (research F9: there are only a handful of esports warm-up studies, so the player can test the
/// Lock-In on themselves). After a Lock-In, the next time the menu shows within <see cref="AskWithinHours"/> hours (after
/// a restart, or at least <see cref="MinGapMinutes"/> minutes later), a small card asks "How did your first match after
/// Lock-In go?" W / L / D and "Felt locked in?" 1–5. Optional, only on the menu (never during a drill), asked once per
/// Lock-In. Answers are stored on the <see cref="WarmupRecord"/> in warmups.json and never leave the PC.
/// From <see cref="ReadoutMin"/> rated Lock-Ins, <see cref="DrawReadout"/> shows "WHAT WORKS FOR YOU": win rate and
/// "felt locked in" by energy before, with vs without breathing, and full routine vs ended early, with counts and a
/// plain "small personal sample, not proof". Below that it shows progress ("4 of 15 matches rated").
/// <para>Dev: <c>--matchlog-demo [N]</c> adds N made-up rated Lock-Ins (default 24; see <see cref="WarmupStore"/>),
/// <c>--matchlog-prompt</c> shows the card on the first menu, <c>--matchlog-readout</c> opens the readout over the menu.</para>
/// </summary>
public static class LockInMatchLog
{
    /// <summary>Rated Lock-Ins needed before the readout shows numbers.</summary>
    public const int ReadoutMin = 15;
    /// <summary>Groups with fewer rated Lock-Ins than this show "too few" instead of numbers.</summary>
    public const int MinGroup = 3;
    public const double AskWithinHours = 6, MinGapMinutes = 20;

    public const string Question = "How did your first match after Lock-In go?";
    public const string HandoffLine = "Next time you open ValTrainer it asks how your match went (optional).";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    /// <summary>When this run of the app started: a Lock-In that ended before it means the app was closed in between.</summary>
    static readonly DateTime AppStart = DateTime.Now - TimeSpan.FromMilliseconds(Time.GetTicksMsec());
    static bool forcedShown;

    public static bool IsLockIn(WarmupRecord r) => r.Energy > 0; // the check-in always sets 1–9; old warm-ups have 0
    public static bool IsRated(WarmupRecord r) => r.Match.Length > 0 || r.Felt > 0;
    public static DateTime EndOf(WarmupRecord r) => r.When.AddSeconds(r.TotalSeconds > 0 ? r.TotalSeconds : r.PlaySeconds);
    /// <summary>The whole routine was played: no FINISH NOW (Completed) and no skipped drill before the deathmatch
    /// ("SKIP TO DEATHMATCH" saves Completed = true with the skipped steps marked Skipped).</summary>
    public static bool FullRoutine(WarmupRecord r) => r.Completed && !r.Steps.Any(s => s.Skipped && s.Mode != "deathmatch");

    public static List<WarmupRecord> Rated(IEnumerable<WarmupRecord> records) => records.Where(r => r != null && IsLockIn(r) && IsRated(r)).ToList();
    public static int RatedCount(IEnumerable<WarmupRecord> records) => Rated(records).Count;

    // =====================================================================================
    // Asking
    // =====================================================================================

    /// <summary>The Lock-In the menu should ask about right now, or null: only the latest record, a Lock-In that hasn't
    /// been asked about or rated, that ended at most <see cref="AskWithinHours"/> h ago, and either before this run of the
    /// app started or at least <see cref="MinGapMinutes"/> min ago.</summary>
    public static WarmupRecord? Due(WarmupStore store, DateTime now)
    {
        if (CmdLine.Dev && CmdLine.Has("--matchlog-prompt") && !forcedShown)
        {
            forcedShown = true;
            var r = store.Warmups.LastOrDefault(w => IsLockIn(w) && !IsRated(w));
            if (r == null)
            {
                r = new WarmupRecord
                {
                    When = now.AddMinutes(-75), Preset = WarmupPresets.DefaultKey, Tier = Main.I.Tier, Completed = true, Energy = 6, Confidence = 6,
                    PlaySeconds = 600, TotalSeconds = 760, Steps = new() { new WarmupStepResult { Mode = "deathmatch", Seconds = 240, Phase = "Deathmatch" } },
                };
                store.Warmups.Add(r);
            }
            Log.Info("[matchlog] --matchlog-prompt: asking now");
            return r;
        }
        if (CmdLine.Dev && !Main.I.SavesData) return null; // automated runs on read-only data never stop to ask
        var last = store.Warmups.LastOrDefault();
        if (last == null || !IsLockIn(last) || last.MatchAsked || IsRated(last)) return null;
        var end = EndOf(last);
        var age = now - end;
        if (age < TimeSpan.Zero || age.TotalHours > AskWithinHours) return null;
        return end < AppStart || age.TotalMinutes >= MinGapMinutes ? last : null;
    }

    /// <summary>The card is on screen: never ask about this Lock-In again.</summary>
    public static void MarkAsked(WarmupRecord r)
    {
        if (r.MatchAsked) return;
        r.MatchAsked = true;
        WarmupStore.I.Save();
        Log.Info($"[matchlog] asking about the Lock-In of {r.When.ToString("yyyy-MM-dd HH:mm", Inv)}");
    }

    /// <summary>Stores one answer ("W" / "L" / "D" and/or 1–5) right away.</summary>
    public static void Answer(WarmupRecord r, string? match, int felt = 0)
    {
        if (match is "W" or "L" or "D") r.Match = match;
        if (felt > 0) r.Felt = Math.Clamp(felt, 1, 5);
        r.MatchAsked = true;
        WarmupStore.I.Save();
        Log.Info($"[matchlog] {r.When.ToString("yyyy-MM-dd HH:mm", Inv)}: match {(r.Match.Length > 0 ? r.Match : "-")}, " +
                 $"felt locked in {(r.Felt > 0 ? r.Felt.ToString(Inv) : "-")} ({RatedCount(WarmupStore.I.Warmups)} rated)");
    }

    // =====================================================================================
    // Readout: "WHAT WORKS FOR YOU"
    // =====================================================================================

    readonly record struct Group(string Label, int N, int Games, int Wins, int Draws, int FeltN, float Felt);

    static Group Stat(string label, IEnumerable<WarmupRecord> rs)
    {
        var l = rs.ToList();
        var games = l.Where(r => r.Match is "W" or "L" or "D").ToList();
        var felt = l.Where(r => r.Felt > 0).ToList();
        return new Group(label, l.Count, games.Count, games.Count(r => r.Match == "W"), games.Count(r => r.Match == "D"),
            felt.Count, felt.Count > 0 ? (float)felt.Average(r => r.Felt) : -1);
    }

    static (string Section, Group[] Rows)[] Sections(List<WarmupRecord> rated) => new[]
    {
        ("ENERGY", new[]
        {
            Stat("Low (1–3)", rated.Where(r => r.Energy <= 3)),
            Stat("Mid (4–6)", rated.Where(r => r.Energy is >= 4 and <= 6)),
            Stat("High (7–9)", rated.Where(r => r.Energy >= 7)),
        }),
        ("BREATHING", new[] { Stat("With", rated.Where(r => r.Breathed)), Stat("Without", rated.Where(r => !r.Breathed)) }),
        ("ROUTINE", new[] { Stat("Full routine", rated.Where(FullRoutine)), Stat("Ended early", rated.Where(r => !FullRoutine(r))) }),
    };

    /// <summary>Height <see cref="DrawReadout"/> needs for these records (progress: short; the readout: taller).</summary>
    public static float ReadoutHeight(IEnumerable<WarmupRecord> records, float k) => RatedCount(records) >= ReadoutMin ? 280 * k : 92 * k;

    /// <summary>
    /// Draws "WHAT WORKS FOR YOU" into <paramref name="r"/> (top-aligned; see <see cref="ReadoutHeight"/>): below
    /// <see cref="ReadoutMin"/> rated Lock-Ins a progress bar ("4 OF 15 MATCHES RATED"), from then on win rate and "felt
    /// locked in" by energy before, breathing and routine, with counts. No red/green: plain numbers and neutral bars.
    /// </summary>
    public static void DrawReadout(CanvasItem c, Rect2 r, IEnumerable<WarmupRecord> records, float k)
    {
        var rated = Rated(records);
        float x0 = r.Position.X, w = r.Size.X, y = r.Position.Y;
        int hs = UiTheme.Fs(13, k), ls = UiTheme.Fs(11, k);
        float hy = Gfx.Mid(y + 10 * k, hs);
        Gfx.Text(c, UiTheme.HudWide, "WHAT WORKS FOR YOU", x0, hy, hs, UiTheme.Text);

        if (rated.Count < ReadoutMin)
        {
            Gfx.TextR(c, UiTheme.HudWide, $"{rated.Count} OF {ReadoutMin} MATCHES RATED", x0 + w, hy, ls, UiTheme.Dim);
            float cg = 3 * k, cw = (w - cg * (ReadoutMin - 1)) / ReadoutMin;
            for (int i = 0; i < ReadoutMin; i++)
                c.DrawRect(new Rect2(x0 + i * (cw + cg), y + 26 * k, cw, 7 * k), i < rated.Count ? UiTheme.Teal : new Color(UiTheme.Text, 0.08f));
            string line = rated.Count == 0
                ? "After a Lock-In, the menu asks how your first match went. One tap, optional."
                : "Rate your first match after a Lock-In when the menu asks.";
            Gfx.TextFit(c, UiTheme.Body, line, x0, y + 60 * k, UiTheme.Fs(14, k), UiTheme.Dim, w);
            Gfx.TextFit(c, UiTheme.Body, $"At {ReadoutMin}, you see what goes with your better matches.",
                x0, y + 82 * k, UiTheme.Fs(13, k), UiTheme.Faint, w);
            return;
        }

        int wins = rated.Count(q => q.Match == "W"), losses = rated.Count(q => q.Match == "L"), draws = rated.Count(q => q.Match == "D");
        Gfx.TextR(c, UiTheme.HudWide, $"{rated.Count} RATED · {wins}W {losses}L {draws}D", x0 + w, hy, ls, UiTheme.Dim);

        // columns sized from the text (the panel can be narrow): section · group · matches · won · felt locked in
        var sections = Sections(rated);
        int fs = UiTheme.Fs(15, k), ns = UiTheme.Fs(17, k), ss = UiTheme.Fs(12, k);
        float gap = 10 * k;
        float secW = sections.Max(s => Gfx.TextW(UiTheme.HudWide, s.Section, ls));
        float grpX = x0 + secW + gap;
        float grpW = sections.SelectMany(s => s.Rows).Max(g => Gfx.TextW(UiTheme.Body, g.Label, fs));
        float nRight = grpX + grpW + gap + Gfx.TextW(UiTheme.HudWide, "MATCHES", ls);
        float pctW = Gfx.TextW(UiTheme.Display, "100%", ns), fracW = Gfx.TextW(UiTheme.Body, "10/10", ss), feltW = Gfx.TextW(UiTheme.Display, "5.0", ns);
        float wonX = nRight + 14 * k;
        float barW = Mathf.Clamp((x0 + w - wonX - pctW - fracW - feltW - 6 * k * 3 - 16 * k) / 2, 14 * k, 70 * k);
        float feltX = x0 + w - feltW - 6 * k - barW;
        float colY = y + 38 * k;
        Gfx.TextR(c, UiTheme.HudWide, "MATCHES", nRight, colY, ls, UiTheme.Faint);
        Gfx.Text(c, UiTheme.HudWide, "WON", wonX, colY, ls, UiTheme.Faint);
        Gfx.TextR(c, UiTheme.HudWide, "FELT LOCKED IN", x0 + w, colY, ls, UiTheme.Faint);
        float rh = 24 * k;
        y += 48 * k;
        int row = 0;
        foreach (var (section, groups) in sections)
        {
            Gfx.Text(c, UiTheme.HudWide, section, x0, Gfx.Mid(y + rh / 2, ls), ls, UiTheme.Dim);
            foreach (var g in groups)
            {
                float cy = y + rh / 2;
                if (row++ % 2 == 0) c.DrawRect(new Rect2(grpX - 6 * k, y, x0 + w - grpX + 6 * k, rh), new Color(1, 1, 1, 0.03f));
                Gfx.Text(c, UiTheme.Body, g.Label, grpX, Gfx.Mid(cy, fs), fs, UiTheme.Text);
                Gfx.TextR(c, UiTheme.Display, g.N.ToString(Inv), nRight, Gfx.Mid(cy, ns), ns, g.N < MinGroup ? UiTheme.Faint : UiTheme.Text);
                if (g.N < MinGroup)
                {
                    Gfx.Text(c, UiTheme.Body, g.N == 0 ? "none yet" : "too few to say", wonX, Gfx.Mid(cy, ss), ss, UiTheme.Faint);
                    y += rh;
                    continue;
                }
                if (g.Games > 0)
                {
                    float rate = (float)g.Wins / g.Games;
                    Gfx.TextR(c, UiTheme.Display, $"{rate * 100:0}%", wonX + pctW, Gfx.Mid(cy, ns), ns, UiTheme.Text);
                    Bar(c, new Rect2(wonX + pctW + 6 * k, cy - 3 * k, barW, 6 * k), rate, UiTheme.Teal);
                    Gfx.Text(c, UiTheme.Body, $"{g.Wins}/{g.Games}", wonX + pctW + barW + 12 * k, Gfx.Mid(cy, ss), ss, UiTheme.Faint);
                }
                else Gfx.Text(c, UiTheme.Body, "—", wonX, Gfx.Mid(cy, ss), ss, UiTheme.Faint);
                if (g.FeltN > 0)
                {
                    Gfx.TextR(c, UiTheme.Display, g.Felt.ToString("0.0", Inv), feltX + feltW, Gfx.Mid(cy, ns), ns, UiTheme.Text);
                    Bar(c, new Rect2(feltX + feltW + 6 * k, cy - 3 * k, barW, 6 * k), (g.Felt - 1) / 4f, UiTheme.Text);
                }
                else Gfx.TextR(c, UiTheme.Body, "—", x0 + w, Gfx.Mid(cy, ss), ss, UiTheme.Faint);
                y += rh;
            }
            y += 6 * k;
        }
        int ns2 = UiTheme.Fs(13, k);
        Gfx.TextFit(c, UiTheme.Body, "A small personal sample, not proof.", x0, y + 14 * k, ns2, UiTheme.Dim, w);
        Gfx.TextFit(c, UiTheme.Body, "A match depends on much more than a warm-up.", x0, y + 32 * k, ns2, UiTheme.Faint, w);
    }

    static void Bar(CanvasItem c, Rect2 r, float frac, Color col)
    {
        if (r.Size.X < 4) return;
        c.DrawRect(r, new Color(UiTheme.Text, 0.08f));
        c.DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Mathf.Clamp(frac, 0, 1), r.Size.Y)), new Color(col, 0.8f));
    }

    /// <summary>The readout as a control whose minimum height follows the data, e.g. for the Lock-In setup screen's
    /// "YOUR LOCK-INS" panel. <paramref name="records"/> defaults to the saved Lock-Ins (read on every redraw).</summary>
    public static DrawBox ReadoutBox(float k, Func<IEnumerable<WarmupRecord>>? records = null)
    {
        var src = records ?? (() => WarmupStore.I.Warmups);
        return new DrawBox
        {
            CustomMinimumSize = new Vector2(0, ReadoutHeight(src(), k)),
            OnDraw = d => DrawReadout(d, new Rect2(Vector2.Zero, d.Size), src(), k),
        };
    }

    // =====================================================================================
    // Dev
    // =====================================================================================

    /// <summary>Dev "--matchlog-demo [N]": N made-up rated Lock-Ins over the last weeks (never saved by dev runs).</summary>
    public static void AddDemo(WarmupStore s, int n)
    {
        n = Math.Clamp(n, 0, 150);
        var rng = new Random(11);
        var now = DateTime.Now;
        for (int i = 0; i < n; i++)
        {
            int energy = 1 + rng.Next(9);
            bool breathed = energy >= 7 && rng.NextDouble() < 0.7;
            bool full = rng.NextDouble() < 0.75;
            double pWin = 0.42 + (energy is >= 4 and <= 6 ? 0.1 : 0) + (full ? 0.05 : 0), roll = rng.NextDouble();
            string match = roll < 0.06 ? "D" : roll < 0.06 + pWin ? "W" : "L";
            int felt = Math.Clamp((int)Math.Round(2.6 + (energy is >= 4 and <= 6 ? 0.8 : 0) + (match == "W" ? 0.6 : 0) + rng.NextDouble() * 1.6 - 0.8), 1, 5);
            s.Warmups.Add(new WarmupRecord
            {
                When = now.AddDays(-(n - i) * 1.3).AddHours(-rng.Next(6)), Preset = WarmupPresets.DefaultKey, Tier = 2, Completed = full,
                Energy = energy, Confidence = 3 + rng.Next(6), Breathed = breathed, BreathSeconds = breathed ? 90 : 0,
                PlaySeconds = 600, TotalSeconds = 760, Readiness = 90 + (float)rng.NextDouble() * 20, ReadinessKind = "7d",
                Steps = new() { new WarmupStepResult { Mode = "deathmatch", Seconds = 240, Phase = "Deathmatch" } },
                Match = rng.NextDouble() < 0.08 ? "" : match, Felt = felt, MatchAsked = true,
            });
        }
        s.Warmups.Sort((a, b) => a.When.CompareTo(b.When));
        Log.Info($"[matchlog] --matchlog-demo: {n} made-up rated Lock-Ins ({RatedCount(s.Warmups)} rated in total)");
    }
}

/// <summary>
/// The menu's small match-log card: "How did your first match after Lock-In go?" W / L / D and "Felt locked in?" 1–5.
/// One tap each, saved right away; SKIP (or DONE) closes it and it never comes back for that Lock-In. It sits in the
/// menu's right column, so nothing is covered and nothing waits for it.
/// </summary>
public partial class MatchLogPrompt : Control
{
    public const float DesignH = 146;
    readonly WarmupRecord rec;
    readonly float k;
    readonly Action onClose;
    readonly List<(VButton B, string Key)> results = new();
    readonly List<VButton> felt = new();
    VButton skip = null!;
    bool closing;

    public MatchLogPrompt(WarmupRecord rec, float k, Action onClose)
    {
        this.rec = rec;
        this.k = k;
        this.onClose = onClose;
        CustomMinimumSize = new Vector2(0, DesignH * k);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        foreach (var (key, tip) in new[] { ("W", "Won"), ("L", "Lost"), ("D", "Draw") })
        {
            var b = new VButton { Kind = VButton.Look.Select, K = k, Label = key, FontPx = 20, Selected = rec.Match == key, TooltipText = tip };
            b.Pressed += () => Pick(key, 0);
            AddChild(b);
            results.Add((b, key));
        }
        for (int i = 1; i <= 5; i++)
        {
            int v = i;
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = i.ToString(CultureInfo.InvariantCulture), FontPx = 19, Selected = rec.Felt == i,
                TooltipText = i == 1 ? "1: not at all" : i == 5 ? "5: completely" : $"{i} of 5",
            };
            b.Pressed += () => Pick(null, v);
            AddChild(b);
            felt.Add(b);
        }
        skip = new VButton { Kind = VButton.Look.Ghost, K = k, Label = "SKIP", FontPx = 14, TooltipText = "Don't ask about this Lock-In again" };
        skip.Pressed += Close;
        AddChild(skip);
        Layout();
        // Dev "--matchlog-answer W,4" (or "skip"): answers the card after 2 s (automated tests of saving)
        if (CmdLine.DevAfter("--matchlog-answer")?.Split(',') is { } ans)
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree()) return;
                Log.Info($"[matchlog] --matchlog-answer {string.Join(",", ans)}");
                if (ans[0].Equals("skip", StringComparison.OrdinalIgnoreCase)) { Close(); return; }
                Pick(ans[0].ToUpperInvariant(), 0);
                if (ans.Length > 1 && int.TryParse(ans[1], out int f)) Pick(null, f);
            };
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && skip != null) Layout();
    }

    float FeltX => Size.X - 20 * k - (5 * 42 + 4 * 6) * k;

    void Layout()
    {
        float pad = 20 * k, by = 64 * k, bh = 42 * k;
        for (int i = 0; i < results.Count; i++) Place(results[i].B, new Rect2(pad + i * 58 * k, by, 50 * k, bh));
        for (int i = 0; i < felt.Count; i++) Place(felt[i], new Rect2(FeltX + i * 48 * k, by, 42 * k, bh));
        Place(skip, new Rect2(Size.X - pad - 86 * k, 10 * k, 90 * k, 30 * k));
        QueueRedraw();
    }

    static void Place(Control c, Rect2 r)
    {
        c.Position = r.Position;
        c.Size = r.Size;
    }

    void Pick(string? match, int f)
    {
        if (closing) return;
        LockInMatchLog.Answer(rec, match, f);
        foreach (var (b, key) in results) b.SetSelected(rec.Match == key);
        for (int i = 0; i < felt.Count; i++) felt[i].SetSelected(rec.Felt == i + 1);
        skip.Label = "DONE";
        skip.TooltipText = "Close";
        skip.QueueRedraw();
        QueueRedraw();
        if (rec.Match.Length > 0 && rec.Felt > 0) GetTree().CreateTimer(1.2).Timeout += () => { if (IsInstanceValid(this)) Close(); };
    }

    void Close()
    {
        if (closing) return;
        closing = true;
        if (!LockInMatchLog.IsRated(rec)) Log.Info("[matchlog] skipped");
        onClose();
    }

    public override void _Draw()
    {
        float pad = 20 * k, w = Size.X;
        Gfx.Plate(this, new Rect2(Vector2.Zero, Size), 14 * k, new Color(0.07f, 0.11f, 0.15f, 0.94f), new Color(UiTheme.Text, 0.14f));
        DrawRect(new Rect2(0, 0, 34 * k, 2 * k), UiTheme.Accent);
        int qs = UiTheme.Fs(17, k), ls = UiTheme.Fs(11, k), fs = UiTheme.Fs(13, k);
        Gfx.TextFit(this, UiTheme.Body, LockInMatchLog.Question, pad, Gfx.Mid(26 * k, qs), qs, UiTheme.Text, w - pad * 2 - 96 * k);
        Gfx.Text(this, UiTheme.HudWide, "RESULT", pad, Gfx.Mid(54 * k, ls), ls, UiTheme.Faint);
        Gfx.Text(this, UiTheme.HudWide, "FELT LOCKED IN?", FeltX, Gfx.Mid(54 * k, ls), ls, UiTheme.Faint);
        Gfx.TextR(this, UiTheme.HudWide, "1 – 5", w - pad, Gfx.Mid(54 * k, ls), ls, UiTheme.Faint);
        bool done = rec.Match.Length > 0 && rec.Felt > 0;
        string foot = done ? "Saved. Thanks." : "Optional. Saved on this PC only.";
        Gfx.Text(this, UiTheme.Body, foot, pad, Gfx.Mid(128 * k, fs), fs, done ? UiTheme.Text : UiTheme.Faint);
        int n = LockInMatchLog.RatedCount(WarmupStore.I.Warmups);
        string prog = n >= LockInMatchLog.ReadoutMin ? $"{n} RATED · SEE LOCK IN" : $"{n} OF {LockInMatchLog.ReadoutMin} RATED";
        Gfx.TextR(this, UiTheme.HudWide, prog, w - pad, Gfx.Mid(128 * k, ls), ls, UiTheme.Faint);
    }
}

/// <summary>Dev "--matchlog-readout": the readout in a panel over the menu (click or Esc closes it).</summary>
public partial class MatchLogDevPanel : Control
{
    readonly float k;

    public MatchLogDevPanel(float k)
    {
        this.k = k;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.72f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new VPanel
        {
            Title = "YOUR LOCK-INS", Caption = "dev preview · match-log readout", K = k, Fill = new Color(0.07f, 0.11f, 0.15f, 0.97f),
            CustomMinimumSize = new Vector2(500 * k, 0),
        };
        center.AddChild(panel);
        panel.AddChild(LockInMatchLog.ReadoutBox(k));
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true }) QueueFree();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            QueueFree();
        }
    }
}
