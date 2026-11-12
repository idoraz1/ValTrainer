using Godot;
using ValTrainer.Core;
using ValTrainer.Modes;
using ValTrainer.Progression;

namespace ValTrainer.UI;

/// <summary>
/// Progress (progression-design-1.19.md §10.2): left, the account card (level, XP bar with rested XP, stars and where they
/// come from, the weekly streak with its rest tokens, the coach rank's placement / confirmed status); centre, the 3 daily
/// and 3 weekly challenges (PLAY starts the drill, one REROLL a day); right, the next unlocks with their requirement and
/// progress, the recent unlocks and OPEN COLLECTION; bottom, the level track. Opened from the menu's account strip.
/// Dev: <c>--screen progress</c> (with <c>--progress-level N --progress-stars N</c>, <c>--progress-demo</c> for made-up
/// challenges / streak / coach status, <c>--fakecoach full</c>).
/// </summary>
public partial class ProgressScreen : ScreenBase
{
    ProgressView view = new();
    LevelTrack? track;

    protected override void Build()
    {
        float k = K;
        view = ProgressView.Now();
        if (view.Demo) view = demoView ??= view; // dev demo: a reroll survives rebuilds
        ProgressToasts.Ensure();
        SkinSwatches.Warm(); // rendered swatches for the unlock rows and the level track (flat colours until they arrive)
        swatchRev = SkinSwatches.Revision;
        var root = Margins(VBox(14 * k), 48 * k, 28 * k, 48 * k, 28 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        var head = HBox(16 * k);
        var title = Heading("PROGRESS", L.T("XP FROM EVERY RUN · STARS FROM REAL IMPROVEMENT · SKINS ARE COSMETIC ONLY")
                                        + (view.Demo && !CmdLine.Showcase ? " · " + L.T("DEMO DATA") : ""));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var back = Btn("BACK", VButton.Look.Primary, Main.I.ShowMenu, 180, 52, 22);
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(back);
        col.AddChild(head);

        var body = HBox(20 * k);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(body);
        body.AddChild(AccountPanel());
        body.AddChild(ChallengesPanel());
        body.AddChild(UnlocksPanel());
        col.AddChild(TrackPanel());
    }

    int swatchRev;

    static void RedrawAll(Node n)
    {
        if (n is CanvasItem ci) ci.QueueRedraw();
        foreach (var c in n.GetChildren()) RedrawAll(c);
    }

    protected override void Tick(float dt)
    {
        if (SkinSwatches.Revision != swatchRev) { swatchRev = SkinSwatches.Revision; RedrawAll(this); }
        if (dirty && !GetChildren().Any(c => c is SkinPicker)) { dirty = false; Rebuild(); return; }
        // reset countdowns tick by the minute; cheap redraw of the captions
        if ((captionT -= dt) <= 0) { captionT = 20; foreach (var l in resetLabels) l.QueueRedraw(); }
    }
    float captionT = 20;
    readonly List<SectionLabel> resetLabels = new();

    public override void _EnterTree() { base._EnterTree(); Progress.Changed += OnProgressChanged; }
    public override void _ExitTree() { base._ExitTree(); Progress.Changed -= OnProgressChanged; }
    // XP / challenges / seen state changed: rebuild, but never under an open skin picker (it is our child)
    void OnProgressChanged() => dirty = true;
    bool dirty;

    // ---------------- left: account ----------------

    Control AccountPanel()
    {
        float k = K;
        var panel = new VPanel { Title = "ACCOUNT", K = k, CustomMinimumSize = new Vector2(400 * k, 0), Pad = 20 };
        panel.AddChild(new DrawBox { OnDraw = DrawAccount, SizeFlagsVertical = SizeFlags.ExpandFill });
        return panel;
    }

    void DrawAccount(DrawBox d)
    {
        float k = K, w = d.Size.X;
        // the blocks are laid out on a 560-unit column, squeezed when the panel is shorter
        float sy = Mathf.Min(1f, d.Size.Y / (576 * k));
        float Y(float design) => design * k * sy;
        int level = Progress.Level, cap = UiTheme.Fs(13, k), body = UiTheme.Fs(14, k);

        // level + XP
        float er = 40 * k;
        ProgressDraw.LevelEmblem(d, new Vector2(er + 2 * k, Y(46)), er, level, k, 1f, 0.35f);
        float tx = er * 2 + 20 * k, tw = w - tx;
        Gfx.TextFit(d, UiTheme.Display, L.F($"LEVEL {level}"), tx, Y(44), UiTheme.Fs(40, k), UiTheme.Text, tw);
        int need = Progress.XpForLevel, into = Progress.XpIntoLevel;
        Gfx.TextFit(d, UiTheme.Body, L.F($"{ProgressText.Num(into)} / {ProgressText.Num(need)} XP to level {level + 1}"), tx, Y(74), body, UiTheme.Dim, tw);
        ProgressDraw.XpBar(d, new Rect2(0, Y(100), w, 10 * k), into / (float)Math.Max(1, need), view.Rested / (float)Math.Max(1, need), UiTheme.Accent);
        if (view.Rested > 0)
        {
            d.DrawRect(new Rect2(0, Y(128) - 5 * k, 10 * k, 10 * k), new Color(ProgressText.RestedColor, 0.6f));
            Gfx.TextFit(d, UiTheme.Body, L.F($"Rested bonus: {ProgressText.Num(view.Rested)} XP (runs earn double)"), 16 * k, Gfx.Mid(Y(128), UiTheme.Fs(13, k)), UiTheme.Fs(13, k), UiTheme.Dim, w - 16 * k);
        }
        else Gfx.TextFit(d, UiTheme.Body, L.F($"{ProgressText.Num(Progress.Xp)} XP in total"), 0, Gfx.Mid(Y(128), UiTheme.Fs(13, k)), UiTheme.Fs(13, k), UiTheme.Faint, w);

        // stars
        Divider(d, Y(152), w);
        Caption(d, L.N("STARS"), Y(174), cap);
        ProgressDraw.Star(d, new Vector2(16 * k, Y(206)), 15 * k, ProgressText.StarColor);
        string st = Progress.Stars.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Gfx.TextRaw(d, UiTheme.Display, st, 40 * k, Gfx.Mid(Y(206), UiTheme.Fs(40, k)), UiTheme.Fs(40, k), ProgressText.StarColor);
        float sx = 40 * k + Gfx.RawW(UiTheme.Display, st, UiTheme.Fs(40, k)) + 14 * k;
        Gfx.TextFit(d, UiTheme.Body, "Only real improvement earns stars:", sx, Gfx.Mid(Y(206), UiTheme.Fs(13, k)), UiTheme.Fs(13, k), UiTheme.Dim, w - sx);
        var rows = new (int N, string Text)[]
        {
            (1, L.T("Real personal best (2% better, after 3 runs)")),
            (1, L.T("Badge rank-up in a drill")),
            (1, L.T("Weekly \"beat your baseline\" challenge")),
            (2, L.T("Confirmed skill rank-up (coach)")),
            (5, L.T("Confirmed overall rank-up (coach)")),
        };
        int rs = UiTheme.Fs(13, k);
        for (int i = 0; i < rows.Length; i++)
        {
            float ry = Y(240 + i * 22);
            if (i % 2 == 0) d.DrawRect(new Rect2(0, ry - 11 * k * sy, w, 22 * k * sy), new Color(1, 1, 1, 0.025f));
            Gfx.TextRaw(d, UiTheme.HudWide, "+" + rows[i].N, 8 * k, Gfx.Mid(ry, rs), rs, ProgressText.StarColor);
            ProgressDraw.Star(d, new Vector2(34 * k, ry), 5 * k, ProgressText.StarColor);
            Gfx.TextFit(d, UiTheme.Body, rows[i].Text, 48 * k, Gfx.Mid(ry, rs), rs, UiTheme.Text, w - 52 * k);
        }

        // weekly streak
        Divider(d, Y(360), w);
        Caption(d, L.N("WEEKLY STREAK"), Y(382), cap);
        string weeks = L.P(view.StreakWeeks, $"{view.StreakWeeks} week", $"{view.StreakWeeks} weeks");
        int ws = UiTheme.Fs(26, k);
        Gfx.TextFit(d, UiTheme.Display, L.Upper(weeks), 0, Gfx.Mid(Y(414), ws), ws, view.StreakWeeks > 0 ? UiTheme.Text : UiTheme.Dim, w * 0.5f);
        float wx = Mathf.Min(w * 0.5f, Gfx.RawW(UiTheme.Display, L.Upper(weeks), ws)) + 14 * k;
        Gfx.TextFit(d, UiTheme.Body, L.F($"best {view.StreakBest}"), wx, Gfx.Mid(Y(414), body), body, UiTheme.Dim, w - wx - 90 * k);
        // rest tokens: two slots, filled = held
        for (int i = 0; i < 2; i++)
        {
            var c = new Vector2(w - 14 * k - i * 30 * k, Y(414));
            bool held = i < view.StreakTokens;
            Shield(d, c, 11 * k, held ? UiTheme.Teal : new Color(UiTheme.Text, 0.18f), held);
        }
        CoachDraw.Para(d, UiTheme.Body, L.T("A week counts with 2 training days. Missed weeks use a rest token automatically."),
            0, Y(432), w, UiTheme.Fs(12.5f, k), UiTheme.Faint, 2);

        // coach
        Divider(d, Y(478), w);
        Caption(d, L.N("COACH RANK"), Y(500), cap);
        int ls = UiTheme.Fs(13, k);
        if (view.Recalibrated)
        {
            CoachDraw.Para(d, UiTheme.Body, L.T("The coach was recalibrated in 1.19. Your coach rank is being re-placed; nothing you earned is lost."),
                0, Y(516), w, ls, UiTheme.Dim, 3);
        }
        else if (view.CoachHigh < 0)
        {
            Gfx.TextFit(d, UiTheme.Display, "NOT PLACED YET", 0, Gfx.Mid(Y(530), UiTheme.Fs(22, k)), UiTheme.Fs(22, k), UiTheme.Dim, w);
            Gfx.TextFit(d, UiTheme.Body, L.F($"Placement: 3 days with a confident coach estimate ({Math.Min(view.CoachDays, 3)}/3)"), 0, Gfx.Mid(Y(558), ls), ls, UiTheme.Dim, w);
        }
        else
        {
            int hi = Math.Clamp(view.CoachHigh, 0, CoachRank.Names.Length - 1);
            string rank = L.Upper(L.T(CoachRank.Names[hi]));
            int rsz = UiTheme.Fs(24, k);
            Gfx.TextFit(d, UiTheme.Display, rank, 0, Gfx.Mid(Y(530), rsz), rsz, CoachRank.Colors[hi], w * 0.5f);
            float rx = Mathf.Min(w * 0.5f, Gfx.RawW(UiTheme.Display, rank, rsz)) + 12 * k;
            Gfx.TextFit(d, UiTheme.Body, L.F($"confirmed · rank-ups {view.OverallUps} overall, {view.SkillUps} skill"), rx, Gfx.Mid(Y(530), ls), ls, UiTheme.Dim, w - rx);
            string next = hi < CoachRank.Names.Length - 1
                ? L.F($"Next rank-up: 5 confident days at {L.T(CoachRank.Names[hi + 1])} or higher")
                : L.T("Top rank confirmed: skill rank-ups still earn stars");
            Gfx.TextFit(d, UiTheme.Body, next, 0, Gfx.Mid(Y(558), ls), ls, UiTheme.Faint, w);
        }
    }

    static void Divider(CanvasItem d, float y, float w) => d.DrawRect(new Rect2(0, y, w, 1), new Color(UiTheme.Text, 0.08f));

    void Caption(CanvasItem d, string text, float cy, int fs)
    {
        d.DrawRect(new Rect2(0, cy - 3 * K, 6 * K, 6 * K), UiTheme.Accent);
        Gfx.Text(d, UiTheme.HudWide, text, 14 * K, Gfx.Mid(cy, fs), fs, UiTheme.Dim);
    }

    static void Shield(CanvasItem d, Vector2 c, float r, Color col, bool filled)
    {
        Span<Vector2> p = stackalloc Vector2[6];
        p[0] = c + new Vector2(-r * 0.8f, -r); p[1] = c + new Vector2(r * 0.8f, -r); p[2] = c + new Vector2(r * 0.8f, 0);
        p[3] = c + new Vector2(0, r); p[4] = c + new Vector2(-r * 0.8f, 0); p[5] = p[0];
        if (filled) d.FillPoly(p[..5], new Color(col, 0.35f));
        d.Polyline(p, col, Mathf.Max(1.2f, r * 0.14f), true);
    }

    // ---------------- centre: challenges ----------------

    Control ChallengesPanel()
    {
        float k = K;
        resetLabels.Clear();
        var panel = new VPanel { Title = "CHALLENGES", Caption = L.T("THEY COMPLETE ON THEIR OWN · NOTHING TO CLAIM"), K = k, Pad = 18 };
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var v = VBox(8 * k);
        panel.AddChild(v);
        Section(v, L.F($"DAILY · {view.DailyDone}/{Math.Max(3, view.Daily.Count)} DONE"), false);
        Cards(v, view.Daily, false);
        v.AddChild(Spacer(0, 4 * k));
        Section(v, L.T("WEEKLY"), true);
        Cards(v, view.Weekly, true);
        return panel;
    }

    void Section(VBoxContainer v, string text, bool weekly)
    {
        var s = new ResetLabel { Text = text, Weekly = weekly, K = K };
        resetLabels.Add(s);
        v.AddChild(s);
    }

    /// <summary>A SectionLabel whose note is the live "Resets in …" countdown.</summary>
    partial class ResetLabel : SectionLabel
    {
        public bool Weekly;
        public override void _Draw() { Note = ProgressText.ResetText(Weekly); base._Draw(); }
    }

    void Cards(VBoxContainer v, List<ProgressView.Challenge> list, bool weekly)
    {
        float k = K;
        if (list.Count == 0)
        {
            v.AddChild(new DrawBox
            {
                SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 70 * k),
                OnDraw = d =>
                {
                    Gfx.Plate(d, new Rect2(Vector2.Zero, d.Size), 10 * k, new Color(0.08f, 0.12f, 0.16f, 0.6f), new Color(UiTheme.Text, 0.08f));
                    int fs = UiTheme.Fs(15, k);
                    Gfx.TextFit(d, UiTheme.Body, weekly ? "This week's challenges appear after your next run" : "Today's challenges appear after your next run",
                        0, Gfx.Mid(d.Size.Y / 2, fs), fs, UiTheme.Dim, d.Size.X, HorizontalAlignment.Center);
                },
            });
            return;
        }
        foreach (var c in list)
        {
            var make = c.Drill != null ? ModeRegistry.Find(c.Drill) : null;
            Action? play = c.Kind switch
            {
                "lockin" => Main.I.ShowLockIn,
                _ when make != null => () => Main.I.StartMode(make),
                _ => null,
            };
            var card = new ChallengeCard
            {
                K = k, C = c, Weekly = weekly, OnPlay = play,
                OnReroll = weekly ? null : () => Reroll(c.Slot),
                CanReroll = view.RerollsLeft > 0,
                SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 64 * k),
            };
            if (make != null) card.TooltipText = L.F($"PLAY starts {ProgressText.DrillName(c.Drill) ?? c.Drill}");
            v.AddChild(card);
        }
    }

    static int demoRerolls;
    static ProgressView? demoView;

    void Reroll(int slot)
    {
        if (view.Demo)
        {
            // dev demo: swap the slot for another made-up challenge (nothing is stored)
            if (demoRerolls++ > 0) return;
            var it = new ChallengeItem { Kind = "time", Target = 10, Xp = 100 };
            view.Daily[slot] = new ProgressView.Challenge(slot, it.Kind, Progress.ChallengeTitle(it), null, 0, 10, false, 100, 0, null, Progress.ChallengeProgress(it));
            view.RerollsLeft = 0;
            Rebuild();
            return;
        }
        if (Progress.CanReroll && Progress.RerollDaily(slot)) Rebuild();
    }

    // ---------------- right: unlocks ----------------

    Control UnlocksPanel()
    {
        float k = K;
        var panel = new VPanel { Title = "NEXT UNLOCKS", K = k, CustomMinimumSize = new Vector2(430 * k, 0), Pad = 18 };
        var v = VBox(8 * k);
        panel.AddChild(v);
        var next = Progress.NextUnlocks(3);
        if (next.Count == 0)
            v.AddChild(new DrawBox
            {
                CustomMinimumSize = new Vector2(0, 90 * k),
                OnDraw = d => CoachDraw.Para(d, UiTheme.Body, L.T("Everything is unlocked. Levels and stars keep counting."), 0, 10 * k, d.Size.X, UiTheme.Fs(15, k), UiTheme.Dim, 3),
            });
        foreach (var s in next)
        {
            var row = new UnlockRow { K = k, S = s, CustomMinimumSize = new Vector2(0, 84 * k) };
            row.Pressed += () => { UiTheme.ClickSound(); SkinPicker.Open(this, K); };
            v.AddChild(row);
        }
        v.AddChild(Spacer(0, 6 * k));
        var recent = Progress.RecentUnlocks(5);
        v.AddChild(new SectionLabel { Text = "RECENT", K = k, Note = recent.Count == 0 ? L.T("nothing yet") : null });
        foreach (var u in recent)
        {
            var info = u;
            v.AddChild(new DrawBox
            {
                CustomMinimumSize = new Vector2(0, 28 * k),
                OnDraw = d =>
                {
                    float cy = d.Size.Y / 2;
                    var col = ProgressText.ItemColor(info.ItemId);
                    d.DrawRect(new Rect2(0, cy - 4 * k, 8 * k, 8 * k), col);
                    int fs = UiTheme.Fs(14, k), ds = UiTheme.Fs(12, k);
                    string when = Ago(info.When);
                    float ww = Gfx.RawW(UiTheme.HudWide, when, ds);
                    Gfx.TextRaw(d, UiTheme.HudWide, when, d.Size.X - ww, Gfx.Mid(cy, ds), ds, UiTheme.Faint);
                    Gfx.TextFit(d, UiTheme.Body, ProgressText.ItemName(info.ItemId), 18 * k, Gfx.Mid(cy, fs), fs, UiTheme.Text, d.Size.X - ww - 30 * k);
                },
            });
        }
        v.AddChild(Expander());
        var open = Btn("OPEN COLLECTION", VButton.Look.Secondary, () => SkinPicker.Open(this, K), 0, 50, 19);
        open.TooltipText = L.T("Every skin line, its levels and how to unlock them");
        v.AddChild(open);
        return panel;
    }

    static string Ago(DateTime when)
    {
        int days = (int)(DateTime.Now.Date - when.Date).TotalDays;
        return days <= 0 ? L.T("TODAY") : days == 1 ? L.T("YESTERDAY") : L.P(days, $"{days} DAY AGO", $"{days} DAYS AGO");
    }

    // ---------------- bottom: level track ----------------

    Control TrackPanel()
    {
        float k = K;
        var panel = new VPanel { Title = "LEVEL TRACK", Caption = L.T("SKINS ON THE WAY · STARS NEEDED TOO · SCROLL FOR MORE"), K = k, Pad = 16, CustomMinimumSize = new Vector2(0, 228 * k) };
        var h = HBox(10 * k);
        panel.AddChild(h);
        track = new LevelTrack { K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 140 * k) };
        var prev = new VButton { Kind = VButton.Look.Arrow, Dir = -1, K = k, CustomMinimumSize = new Vector2(40 * k, 40 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        prev.Pressed += () => track?.Shift(-5);
        var next = new VButton { Kind = VButton.Look.Arrow, Dir = 1, K = k, CustomMinimumSize = new Vector2(40 * k, 40 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        next.Pressed += () => track?.Shift(5);
        h.AddChild(prev);
        h.AddChild(track);
        h.AddChild(next);
        return panel;
    }
}
