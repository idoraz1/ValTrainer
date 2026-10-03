using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// Career / stats: tier tabs on top; for the selected tier one row per drill with runs, best, average of the
/// last 10, accuracy, kill/reaction time, best badge and a sparkline of the last 20 scores.
/// </summary>
public partial class StatsScreen : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    /// <summary>Remembered across visits within the session.</summary>
    static int selTier = -99;

    readonly List<TrainingMode> modes = ModeRegistry.All.Select(f => f()).ToList();
    readonly List<VButton> tabs = new();
    VBoxContainer? table;
    DrawBox? summary;

    /// <summary>Column layout as fractions of the row width: mode, runs, best, avg10, accuracy, kill/react, badge, sparkline.</summary>
    internal static readonly float[] Cols = { 0f, 0.235f, 0.305f, 0.39f, 0.48f, 0.565f, 0.665f, 0.82f };
    internal static readonly string[] Heads = { "DRILL", "RUNS", "BEST", "AVG · LAST 10", "ACCURACY", "KILL / REACT", "BEST BADGE", "LAST 20 RUNS" };

    protected override void Build()
    {
        float k = K;
        tabs.Clear();
        if (selTier == -99) selTier = Main.I.Tier;
        bool legacy = Main.I.Stats.Runs.Any(r => r.Tier < 0);
        if (selTier < 0 && !legacy) selTier = Main.I.Tier;

        var root = Margins(VBox(16 * k), 48 * k, 30 * k, 48 * k, 34 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        var head = HBox(16 * k);
        var title = Heading("CAREER", "YOUR RUNS PER DRILL AND TIER · SCORES ARE ONLY COMPARABLE WITHIN A TIER");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        summary = new DrawBox { CustomMinimumSize = new Vector2(420 * k, 90 * k), OnDraw = DrawSummary };
        head.AddChild(summary);
        var back = Btn("BACK", VButton.Look.Primary, Main.I.ShowMenu, 180, 52, 22);
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(back);
        col.AddChild(head);

        // tier tabs
        var tabRow = HBox(10 * k);
        foreach (var t in Difficulty.Tiers) AddTab(tabRow, t.Id, t.Name.ToUpperInvariant(), t.Ranks);
        if (legacy) AddTab(tabRow, -1, "LEGACY", "old Easy/Normal/Hard runs");
        col.AddChild(tabRow);

        var panel = new VPanel { K = k, Pad = 18, SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddChild(panel);
        var pv = VBox(4 * k);
        panel.AddChild(pv);
        pv.AddChild(new StatsHeaderRow { K = k, CustomMinimumSize = new Vector2(0, 34 * k) });
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        pv.AddChild(scroll);
        table = VBox(4 * k);
        table.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(table);
        FillTable();
    }

    void AddTab(HBoxContainer row, int tier, string name, string sub)
    {
        int runs = Main.I.Stats.Runs.Count(r => tier < 0 ? r.Tier < 0 : r.Tier == tier);
        var b = new VButton
        {
            Kind = VButton.Look.Select, K = K, Label = name, Sub = $"{sub} · {runs} run{(runs == 1 ? "" : "s")}", Emblem = tier >= 0 ? tier : -1,
            FontPx = 21, SubPx = 12.5f, CustomMinimumSize = new Vector2(0, 62 * K), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Selected = selTier == tier,
        };
        b.Pressed += () =>
        {
            selTier = tier;
            foreach (var t in tabs) t.SetSelected(false);
            b.SetSelected(true);
            FillTable();
            summary?.QueueRedraw();
        };
        tabs.Add(b);
        row.AddChild(b);
    }

    void FillTable()
    {
        if (table == null) return;
        foreach (var c in table.GetChildren()) { table.RemoveChild(c); c.QueueFree(); }
        int i = 0;
        foreach (var (m, label) in Rows())
        {
            var runs = Main.I.Stats.For(m.Key).Where(r => selTier < 0 ? r.Tier < 0 : r.Tier == selTier).ToList();
            table.AddChild(new StatsRow(m, runs) { Label = label, K = K, Legacy = selTier < 0, Stripe = i++ % 2 == 0, CustomMinimumSize = new Vector2(0, 58 * K) });
        }
    }

    /// <summary>
    /// One row per drill. Drills with per-agent variants (<see cref="ModeRegistry.WithArg"/>: "flashpeek:skye", "anchor:killjoy",
    /// "chamber:tdf") get a row per variant ever played ("FLASH &amp; PEEK · SKYE"); one with a plain version (Site Anchor) keeps
    /// its own row too, and one without any runs yet shows a single row.
    /// </summary>
    IEnumerable<(TrainingMode Mode, string? Label)> Rows()
    {
        var played = Main.I.Stats.Runs.Select(r => r.Mode ?? "").Where(k => k.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in modes)
        {
            int c = m.Key.IndexOf(':');
            string b = c > 0 ? m.Key[..c] : m.Key;
            if (!ModeRegistry.WithArg.ContainsKey(b)) { yield return (m, null); continue; }
            var variants = played.Where(k => k.Length > b.Length + 1 && k.StartsWith(b + ":", StringComparison.OrdinalIgnoreCase))
                .Select(k => (Key: k, Arg: Agents.AgentRoster.ArgName(k[(b.Length + 1)..]))).OrderBy(v => v.Arg, StringComparer.OrdinalIgnoreCase).ToList();
            bool plain = c < 0;
            if (plain || variants.Count == 0) yield return (m, null);
            foreach (var (key, arg) in variants)
            {
                TrainingMode v;
                try { v = ModeRegistry.Find(key)?.Invoke() ?? m; } catch { v = m; }
                if (v.Key != key) continue; // the drill no longer knows this argument
                yield return (v, $"{v.Name} · {arg}");
            }
        }
    }

    void DrawSummary(DrawBox d)
    {
        float k = K;
        var all = Main.I.Stats.Runs;
        int atTier = all.Count(r => selTier < 0 ? r.Tier < 0 : r.Tier == selTier);
        int badges = all.Where(r => r.Badge >= 0).Select(r => r.Badge).DefaultIfEmpty(-1).Max();
        (string L, string V, Color C)[] items =
        {
            ("TOTAL RUNS", all.Count.ToString(Inv), UiTheme.Text),
            ("AT THIS TIER", atTier.ToString(Inv), UiTheme.Text),
            ("BEST BADGE", badges >= 0 ? Difficulty.Tiers[badges].Name.ToUpperInvariant() : "—", UiTheme.TierColor(badges)),
        };
        float cw = d.Size.X / items.Length, cy = d.Size.Y / 2;
        int vs = UiTheme.Fs(30, k), ls = UiTheme.Fs(11, k);
        for (int i = 0; i < items.Length; i++)
        {
            float cx = cw * (i + 0.5f);
            Gfx.TextC(d, UiTheme.Display, items[i].V, cx, cy + 4 * k, vs, items[i].C);
            Gfx.TextC(d, UiTheme.HudWide, items[i].L, cx, cy + 24 * k, ls, UiTheme.Dim);
            if (i > 0) d.DrawRect(new Rect2(cw * i, cy - 26 * k, 1, 52 * k), new Color(UiTheme.Text, 0.1f));
        }
    }
}

/// <summary>Column captions of the stats table.</summary>
public partial class StatsHeaderRow : Control
{
    public float K = 1f;
    public StatsHeaderRow() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        float k = K, pad = 16 * k, w = Size.X - pad * 2, cy = Size.Y / 2;
        int fs = UiTheme.Fs(12, k);
        for (int i = 0; i < StatsScreen.Heads.Length; i++)
            Gfx.Text(this, UiTheme.HudWide, StatsScreen.Heads[i], pad + StatsScreen.Cols[i] * w, Gfx.Mid(cy, fs), fs, UiTheme.Dim);
        DrawRect(new Rect2(0, Size.Y - 1, Size.X, 1), new Color(UiTheme.Text, 0.12f));
    }
}

/// <summary>One drill's stats at the selected tier (values precomputed; drawn in code).</summary>
public partial class StatsRow : Control
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly TrainingMode mode;
    readonly int runs, badge;
    readonly string best = "—", avg10 = "—", acc = "—", kill = "—";
    readonly List<float> spark;
    public float K = 1f;
    public bool Stripe;
    /// <summary>Old Easy/Normal/Hard runs were never rated.</summary>
    public bool Legacy;
    /// <summary>Row title instead of the drill name (per-agent variants: "Flash &amp; Peek · Skye").</summary>
    public string? Label;
    float hover;

    public StatsRow(TrainingMode m, List<RunRecord> list)
    {
        mode = m;
        MouseFilter = MouseFilterEnum.Pass;
        runs = list.Count;
        badge = list.Select(r => r.Badge).DefaultIfEmpty(-1).Max();
        spark = list.TakeLast(20).Select(r => (float)r.Score).ToList();
        if (runs == 0) return;
        var last10 = list.TakeLast(10).ToList();
        best = list.Max(r => r.Score).ToString("#,0", Inv);
        avg10 = last10.Average(r => r.Score).ToString("#,0", Inv);
        var accs = last10.Where(r => r.Accuracy >= 0).ToList();
        if (accs.Count > 0) acc = $"{accs.Average(r => r.Accuracy) * 100:0.0}%";
        var kt = last10.Where(r => r.AvgKillMs > 0).ToList();
        if (kt.Count > 0) kill = $"{kt.Average(r => r.AvgKillMs):0} ms";
    }

    public override void _Process(double delta)
    {
        float t = GetGlobalRect().HasPoint(GetGlobalMousePosition()) ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K, pad = 16 * k, w = Size.X - pad * 2, cy = Size.Y / 2;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(1, 1, 1, (Stripe ? 0.03f : 0.012f) + 0.03f * hover));
        DrawRect(new Rect2(0, 0, 3 * k, Size.Y), new Color(MenuCardColor(), 0.35f + 0.65f * hover));
        float X(int c) => pad + StatsScreen.Cols[c] * w;
        float ColW(int c) => (c + 1 < StatsScreen.Cols.Length ? StatsScreen.Cols[c + 1] : 1f) * w - StatsScreen.Cols[c] * w - 10 * k;

        int ns = UiTheme.Fs(22, k), cs = UiTheme.Fs(11, k), vs = UiTheme.Fs(22, k);
        Gfx.TextFit(this, UiTheme.Display, (Label ?? mode.Name).ToUpperInvariant(), X(0), Gfx.Mid(cy - 8 * k, ns), ns, UiTheme.Text, ColW(0));
        Gfx.Text(this, UiTheme.HudWide, mode.Category.ToUpperInvariant(), X(0), Gfx.Mid(cy + 13 * k, cs), cs, ModeCard.CategoryColor(mode.Category));

        if (runs == 0)
        {
            int fs = UiTheme.Fs(15, k);
            Gfx.Text(this, UiTheme.Body, "Not played at this tier yet", X(1), Gfx.Mid(cy, fs), fs, UiTheme.Faint);
            return;
        }
        Gfx.Text(this, UiTheme.Display, runs.ToString(Inv), X(1), Gfx.Mid(cy, vs), vs, UiTheme.Text);
        Gfx.Text(this, UiTheme.Display, best, X(2), Gfx.Mid(cy, vs), vs, UiTheme.Good);
        Gfx.Text(this, UiTheme.Display, avg10, X(3), Gfx.Mid(cy, vs), vs, UiTheme.Text);
        Gfx.Text(this, UiTheme.Display, acc, X(4), Gfx.Mid(cy, vs), vs, UiTheme.Text);
        Gfx.Text(this, UiTheme.Display, kill, X(5), Gfx.Mid(cy, vs), vs, UiTheme.Text);

        float er = 12 * k;
        RankEmblem.Draw(this, new Vector2(X(6) + er, cy), er, badge, badge >= 0 ? 1f : 0.6f);
        int bs = UiTheme.Fs(14, k);
        Gfx.TextFit(this, UiTheme.HudWide, badge >= 0 ? Difficulty.Tiers[badge].Name.ToUpperInvariant() : Legacy ? "NOT RATED" : "BELOW ROOKIE", X(6) + er * 2 + 8 * k, Gfx.Mid(cy, bs), bs,
            UiTheme.TierColor(badge), ColW(6) - er * 2 - 8 * k);

        var sr = new Rect2(X(7), cy - 16 * k, Size.X - pad - X(7), 30 * k);
        Sparkline.Draw(this, sr, spark, k, UiTheme.Accent);
    }

    Color MenuCardColor() => ModeCard.CategoryColor(mode.Category);
}
