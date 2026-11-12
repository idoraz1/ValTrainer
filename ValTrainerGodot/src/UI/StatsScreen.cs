using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// Career / stats: tier tabs on top; for the selected tier one row per drill with runs, best, average of the
/// last 10, accuracy, kill/reaction time, best badge and a sparkline of the last 20 scores. Once more than one gun was used,
/// a GUN chip row filters the table by gun (ALL GUNS: the best across guns, with the gun under the score when it isn't the
/// drill's default). Runs from before 1.18 count as the drill's default gun. Badges count rated guns only
/// (<see cref="GunRating"/>). Dev: <c>--stats-gun Phantom</c> opens with that filter.
/// </summary>
public partial class StatsScreen : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    /// <summary>Remembered across visits within the session.</summary>
    static int selTier = -99;
    /// <summary>Gun filter (null = all guns), remembered within the session.</summary>
    static WeaponKind? selGun = Weapons.Parse(CmdLine.DevAfter("--stats-gun"));

    readonly List<TrainingMode> modes = ModeRegistry.All.Select(f => f()).ToList();
    readonly List<VButton> tabs = new();
    readonly List<(VButton B, WeaponKind? Gun)> gunChips = new();
    VBoxContainer? table;
    DrawBox? summary;

    /// <summary>Every gun with at least one run, in buy-menu order (old runs: their drill's default).</summary>
    static List<WeaponKind> GunsUsed()
    {
        var used = Main.I.Stats.Runs.Where(r => r != null).Select(DrillGuns.GunOf).Where(g => g != WeaponKind.None).ToHashSet();
        return Weapons.All.Select(w => w.Kind).Where(used.Contains).ToList();
    }

    void AddGunChip(HBoxContainer row, WeaponKind? gun, string label)
    {
        int n = Main.I.Stats.Runs.Count(r => r != null && (gun == null || DrillGuns.GunOf(r) == gun));
        var b = new VButton
        {
            Kind = VButton.Look.Select, K = K, Label = label, FontPx = 14, Selected = selGun == gun, TooltipText = L.P(n, $"{n} run", $"{n} runs"),
            CustomMinimumSize = new Vector2(Mathf.Max(96, 30 + label.Length * 9) * K, 34 * K),
        };
        b.Pressed += () =>
        {
            selGun = gun;
            foreach (var (c, g) in gunChips) c.SetSelected(g == gun);
            FillTable();
            summary?.QueueRedraw();
        };
        gunChips.Add((b, gun));
        row.AddChild(b);
    }

    /// <summary>Column layout as fractions of the row width: mode, runs, best, avg10, accuracy, kill/react, badge, sparkline.</summary>
    internal static readonly float[] Cols = { 0f, 0.235f, 0.305f, 0.39f, 0.48f, 0.565f, 0.665f, 0.82f };
    internal static readonly string[] Heads = { L.N("DRILL"), L.N("RUNS"), L.N("BEST"), L.N("AVG · LAST 10"), L.N("ACCURACY"), L.N("KILL / REACT"), L.N("BEST BADGE"), L.N("LAST 20 RUNS") };

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
        foreach (var t in Difficulty.Tiers) AddTab(tabRow, t.Id, L.Upper(L.T(t.Name)), L.T(t.Ranks));
        if (legacy) AddTab(tabRow, -1, L.T("LEGACY"), L.T("old Easy/Normal/Hard runs"));
        col.AddChild(tabRow);

        // gun filter: only once more than one gun was ever used (ALL = the best across guns)
        gunChips.Clear();
        var guns = GunsUsed();
        if (selGun is { } sg && !guns.Contains(sg)) selGun = null;
        if (guns.Count > 1)
        {
            var gunRow = HBox(6 * k);
            gunRow.AddChild(new SectionLabel { Text = "GUN", K = k, CustomMinimumSize = new Vector2(70 * k, 34 * k) });
            AddGunChip(gunRow, null, L.T("ALL GUNS"));
            foreach (var g in guns) AddGunChip(gunRow, g, L.Raw(GunPicker.GunName(g).ToUpperInvariant())); // gun names stay English
            col.AddChild(gunRow);
        }

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

    /// <param name="name">Translated tab title.</param><param name="sub">Translated second line.</param>
    void AddTab(HBoxContainer row, int tier, string name, string sub)
    {
        int runs = Main.I.Stats.Runs.Count(r => tier < 0 ? r.Tier < 0 : r.Tier == tier);
        var b = new VButton
        {
            Kind = VButton.Look.Select, K = K, Label = name, Sub = sub + " · " + L.P(runs, $"{runs} run", $"{runs} runs"), Emblem = tier >= 0 ? tier : -1,
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
            var runs = Main.I.Stats.For(m.Key).Where(r => (selTier < 0 ? r.Tier < 0 : r.Tier == selTier)
                && (selGun == null || StatsStore.GunOf(r, m.Weapon) == selGun)).ToList();
            table.AddChild(new StatsRow(m, runs, selGun) { Label = label, K = K, Legacy = selTier < 0, Stripe = i++ % 2 == 0, CustomMinimumSize = new Vector2(0, 58 * K) });
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
                yield return (v, $"{v.DisplayName} · {arg}"); // translated drill name, agent name as it is
            }
        }
    }

    void DrawSummary(DrawBox d)
    {
        float k = K;
        var all = Main.I.Stats.Runs;
        int atTier = all.Count(r => selTier < 0 ? r.Tier < 0 : r.Tier == selTier);
        int badges = all.Where(r => r != null && r.Badge >= 0 && DrillGuns.IsRated(r)).Select(r => r.Badge).DefaultIfEmpty(-1).Max();
        (string Cap, string V, Color C)[] items =
        {
            (L.N("TOTAL RUNS"), all.Count.ToString(Inv), UiTheme.Text),
            (L.N("AT THIS TIER"), atTier.ToString(Inv), UiTheme.Text),
            (L.N("BEST BADGE"), badges >= 0 ? L.Upper(L.T(Difficulty.Tiers[badges].Name)) : "—", UiTheme.TierColor(badges)),
        };
        float cw = d.Size.X / items.Length, cy = d.Size.Y / 2;
        int vs = UiTheme.Fs(30, k), ls = UiTheme.Fs(11, k);
        for (int i = 0; i < items.Length; i++)
        {
            // centred in its third (translated tier names and captions can be longer)
            float cx = cw * i + 4 * k, cmax = cw - 8 * k;
            Gfx.TextFit(d, UiTheme.Display, items[i].V, cx, cy + 4 * k, vs, items[i].C, cmax, HorizontalAlignment.Center);
            Gfx.TextFit(d, UiTheme.HudWide, items[i].Cap, cx, cy + 24 * k, ls, UiTheme.Dim, cmax, HorizontalAlignment.Center);
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
        var cols = StatsScreen.Cols;
        for (int i = 0; i < StatsScreen.Heads.Length; i++)
        {
            // translations run longer: each caption stays inside its column (English draws as designed)
            float cw = L.IsEnglish ? float.MaxValue : ((i + 1 < cols.Length ? cols[i + 1] : 1f) - cols[i]) * w - 10 * k;
            Gfx.TextFit(this, UiTheme.HudWide, StatsScreen.Heads[i], pad + cols[i] * w, Gfx.Mid(cy, fs), fs, UiTheme.Dim, cw);
        }
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
    readonly string? bestGunName;
    readonly bool unrated;
    readonly List<float> spark;
    public float K = 1f;
    public bool Stripe;
    /// <summary>Old Easy/Normal/Hard runs were never rated.</summary>
    public bool Legacy;
    /// <summary>Row title instead of the drill name (per-agent variants: "Flash &amp; Peek · Skye"), already translated.</summary>
    public string? Label;
    float hover;

    public StatsRow(TrainingMode m, List<RunRecord> list, WeaponKind? gunFilter = null)
    {
        mode = m;
        MouseFilter = MouseFilterEnum.Pass;
        runs = list.Count;
        // badges: rated guns only (old runs count as the drill's default gun, which is always rated)
        bool hasGun = m.Weapon != WeaponKind.None;
        unrated = hasGun && gunFilter is { } gf && !GunRating.IsRated(m, gf);
        badge = list.Where(r => !hasGun || GunRating.IsRated(m, StatsStore.GunOf(r, m.Weapon))).Select(r => r.Badge).DefaultIfEmpty(-1).Max();
        spark = list.TakeLast(20).Select(r => (float)r.Score).ToList();
        if (runs == 0) return;
        var last10 = list.TakeLast(10).ToList();
        var top = list.MaxBy(r => r.Score)!;
        best = top.Score.ToString("#,0", Inv);
        // all guns: name the gun of the best when the row mixes guns or the best isn't the drill's default
        if (hasGun && gunFilter == null)
        {
            var bestGun = StatsStore.GunOf(top, m.Weapon);
            bool mixed = list.Select(r => StatsStore.GunOf(r, m.Weapon)).Distinct().Skip(1).Any();
            if ((mixed || bestGun != m.Weapon) && bestGun != WeaponKind.None) bestGunName = L.Raw(GunPicker.GunName(bestGun).ToUpperInvariant()); // gun names stay English
        }
        avg10 = last10.Average(r => r.Score).ToString("#,0", Inv);
        var accs = last10.Where(r => r.Accuracy >= 0).ToList();
        if (accs.Count > 0) acc = $"{accs.Average(r => r.Accuracy) * 100:0.0}%";
        var kt = last10.Where(r => r.AvgKillMs > 0).ToList();
        if (kt.Count > 0) kill = L.F($"{kt.Average(r => r.AvgKillMs):0} ms");
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
        Gfx.TextFit(this, UiTheme.Display, L.Upper(Label ?? mode.DisplayName), X(0), Gfx.Mid(cy - 8 * k, ns), ns, UiTheme.Text, ColW(0));
        Gfx.Text(this, UiTheme.HudWide, L.Upper(mode.DisplayCategory), X(0), Gfx.Mid(cy + 13 * k, cs), cs, ModeCard.CategoryColor(mode.Category));

        if (runs == 0)
        {
            int fs = UiTheme.Fs(15, k);
            Gfx.Text(this, UiTheme.Body, "Not played at this tier yet", X(1), Gfx.Mid(cy, fs), fs, UiTheme.Faint);
            return;
        }
        Gfx.Text(this, UiTheme.Display, runs.ToString(Inv), X(1), Gfx.Mid(cy, vs), vs, UiTheme.Text);
        Gfx.Text(this, UiTheme.Display, best, X(2), Gfx.Mid(bestGunName != null ? cy - 6 * k : cy, vs), vs, UiTheme.Good);
        if (bestGunName != null) Gfx.TextFit(this, UiTheme.HudWide, bestGunName, X(2), Gfx.Mid(cy + 15 * k, cs), cs, UiTheme.Dim, ColW(2));
        Gfx.Text(this, UiTheme.Display, avg10, X(3), Gfx.Mid(cy, vs), vs, UiTheme.Text);
        Gfx.Text(this, UiTheme.Display, acc, X(4), Gfx.Mid(cy, vs), vs, UiTheme.Text);
        Gfx.Text(this, UiTheme.Display, kill, X(5), Gfx.Mid(cy, vs), vs, UiTheme.Text);

        float er = 12 * k;
        int bs = UiTheme.Fs(14, k);
        if (unrated)
        {
            RankEmblem.Draw(this, new Vector2(X(6) + er, cy), er, -1, 0.45f);
            Gfx.TextFit(this, UiTheme.HudWide, "UNRATED GUN", X(6) + er * 2 + 8 * k, Gfx.Mid(cy, bs), bs, UiTheme.Faint, ColW(6) - er * 2 - 8 * k);
        }
        else
        {
            RankEmblem.Draw(this, new Vector2(X(6) + er, cy), er, badge, badge >= 0 ? 1f : 0.6f);
            Gfx.TextFit(this, UiTheme.HudWide, badge >= 0 ? L.Upper(L.T(Difficulty.Tiers[badge].Name)) : Legacy ? L.N("NOT RATED") : L.N("BELOW ROOKIE"), X(6) + er * 2 + 8 * k, Gfx.Mid(cy, bs), bs,
                UiTheme.TierColor(badge), ColW(6) - er * 2 - 8 * k);
        }

        var sr = new Rect2(X(7), cy - 16 * k, Size.X - pad - X(7), 30 * k);
        Sparkline.Draw(this, sr, spark, k, UiTheme.Accent);
    }

    Color MenuCardColor() => ModeCard.CategoryColor(mode.Category);
}
