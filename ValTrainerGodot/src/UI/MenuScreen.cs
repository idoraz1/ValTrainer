using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Maps;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// Main menu: VALORANT-styled lobby. Left: tier selector (with the map selector on its caption row) and the drill
/// cards, grouped by category (PB at the current tier, best rank badge ever) and packed into balanced columns so the
/// whole list fits without scrolling on 16:9 screens. Agent drills live on the Agents screen; a link card stands in for
/// them. Right: everything imported from Valorant plus a live crosshair preview. Top-right: Warm up / Agents / Coach /
/// Settings / Stats / Quit.
/// </summary>
public partial class MenuScreen : ScreenBase
{
    /// <summary>Category of the per-agent drills: listed on the Agents screen, not in the menu grid.</summary>
    public const string AgentsCategory = "Agents";

    readonly List<(Func<TrainingMode> Make, TrainingMode Info)> modes =
        ModeRegistry.All.Select(f => (Make: f, Info: f())).Where(m => m.Info.Category != AgentsCategory).ToList();
    readonly List<VButton> tierButtons = new();
    readonly List<ModeCard> cards = new();
    int hoverTier = -1;
    DrawBox? tierInfo;
    MapSelector? mapSel;

    /// <summary>Esc closes the map pop-up; otherwise it does nothing on the menu (Quit is a button).</summary>
    protected override void Back()
    {
        if (mapSel?.IsOpen == true) mapSel.Close();
    }

    VBoxContainer? bannerSlot;
    int bannerRev = -1;

    protected override void Build()
    {
        tierButtons.Clear(); cards.Clear();
        mapSel = null;
        coachHint = null; coachHintDone = false;
        float k = K;
        var root = Margins(VBox(0), 48 * k, 26 * k, 48 * k, 34 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        col.AddChild(Header());
        col.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 22 * k),
            OnDraw = d =>
            {
                float y = d.Size.Y / 2;
                d.DrawRect(new Rect2(0, y, d.Size.X, 1), new Color(UiTheme.Text, 0.1f));
                d.DrawRect(new Rect2(0, y - 1 * k, 64 * k, 2 * k), UiTheme.Accent);
            },
        });
        // "ValTrainer X is available / downloading / ready" strip (filled when the background update check answers)
        bannerSlot = VBox(0);
        col.AddChild(bannerSlot);
        bannerRev = -1;
        banner = null;
        RefreshBanner();

        // version, bottom-right corner (in the margin under the crosshair panel)
        AddChild(new DrawBox
        {
            AnchorRight = 1, AnchorBottom = 1,
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(11, k);
                string v = "v" + AppInfo.Version + (Main.I.Dev && !Core.CmdLine.Showcase ? " · DEV" : "");
                Gfx.TextR(d, UiTheme.HudWide, v, d.Size.X - 48 * k, d.Size.Y - 13 * k, fs, UiTheme.Faint);
            },
        });

        if (WhatsNew.Pending is { } notes)
            AddChild(new WhatsNewPanel(notes, k, CloseWhatsNew));

        var body = HBox(34 * k);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(body);

        float rightW = 520 * k;
        var left = VBox(8 * k);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(left);
        var vp = GetViewportRect().Size;
        BuildLeft(left, vp.X - 96 * k - rightW - 34 * k, vp.Y / k - GridTop);

        var right = VBox(16 * k);
        right.CustomMinimumSize = new Vector2(rightW, 0);
        body.AddChild(right);
        BuildRight(right);
    }

    /// <summary>Design units above the drill grid: margins, header, divider, tier caption row, tier buttons, tier line.</summary>
    const float GridTop = 26 + 34 + 112 + 22 + 40 + 8 + 66 + 8 + 34 + 8;

    void CloseWhatsNew()
    {
        WhatsNew.Pending = null;
        foreach (var c in GetChildren()) if (c is WhatsNewPanel p) p.QueueFree();
        UiTheme.ClickSound();
    }

    // ---------------- update banner ----------------

    UpdateBanner? banner;

    /// <summary>The strip under the header: "X is available" (DOWNLOAD opens the release page) → "Downloading update
    /// 42%" → "X is ready — RESTART TO UPDATE · WHAT'S NEW · LATER" (see <see cref="Updater"/>). Progress only redraws
    /// the text; the buttons are rebuilt when the stage changes.</summary>
    void RefreshBanner()
    {
        if (bannerSlot == null || bannerRev == UpdateCheck.Revision) return;
        bannerRev = UpdateCheck.Revision;
        string? ver = UpdateCheck.BannerVersion;
        var stage = ver == null ? UpdateBanner.Stage.Available : BannerStage(ver);
        if (banner != null && IsInstanceValid(banner) && ver == banner.Version && stage == banner.Kind) { banner.QueueRedraw(); return; }
        foreach (var c in bannerSlot.GetChildren()) c.QueueFree();
        banner = null;
        if (ver == null) return;
        float k = K;
        banner = new UpdateBanner { K = k, Version = ver, Kind = stage, CustomMinimumSize = new Vector2(0, 46 * k) };
        var row = HBox(10 * k);
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 236 * k; // text is drawn by the banner itself
        row.OffsetRight = -8 * k;
        row.Alignment = BoxContainer.AlignmentMode.End;
        banner.AddChild(row);
        string? url = UpdateCheck.LatestUrl ?? (AppInfo.HasRepo ? AppInfo.LatestReleaseUrl : null);
        bool hasPage = url != null && AppInfo.HasRepo;
        void Add(VButton b)
        {
            b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(b);
        }
        VButton WhatsNew()
        {
            var b = Btn("WHAT'S NEW", VButton.Look.Ghost, () => AppInfo.OpenGitHub(url), 140, 32, 15);
            b.Disabled = !hasPage;
            b.TooltipText = "Opens the release notes in your browser";
            return b;
        }
        switch (stage)
        {
            case UpdateBanner.Stage.Ready:
            {
                var go = Btn("RESTART TO UPDATE", VButton.Look.Primary, Updater.RestartToUpdate, 210, 32, 16);
                go.TooltipText = Updater.NeedsElevation
                    ? "Installs the update and restarts ValTrainer. ValTrainer is installed for all users, so Windows asks for permission"
                    : "Installs the update and restarts ValTrainer (takes a few seconds; settings and stats are kept)";
                Add(go);
                Add(WhatsNew());
                var later = Btn("LATER", VButton.Look.Ghost, () => { UpdateCheck.BannerDismissed = true; bannerRev = -1; RefreshBanner(); }, 100, 32, 15);
                later.TooltipText = Updater.NeedsElevation ? "Hide until next launch" : "Installs the next time you start ValTrainer";
                Add(later);
                break;
            }
            case UpdateBanner.Stage.Applying:
                break;
            default:
            {
                if (stage == UpdateBanner.Stage.Downloading) Add(WhatsNew());
                else
                {
                    var dl = Btn("DOWNLOAD", VButton.Look.Primary, () => AppInfo.OpenGitHub(url), 150, 32, 16);
                    dl.Disabled = !hasPage;
                    dl.TooltipText = dl.Disabled ? "No download page is set for this build" : "Opens the release page in your browser";
                    Add(dl);
                }
                var skip = Btn("SKIP THIS VERSION", VButton.Look.Ghost, () => UpdateCheck.SkipVersion(ver), 170, 32, 15);
                skip.TooltipText = $"Don't remind me about {ver} again (and don't download it)";
                Add(skip);
                var close = Btn("×", VButton.Look.Ghost, () => { UpdateCheck.BannerDismissed = true; bannerRev = -1; RefreshBanner(); }, 36, 32, 24);
                close.TitleFont = UiTheme.Body;
                close.TooltipText = "Hide until next launch";
                Add(close);
                break;
            }
        }
        bannerSlot.AddChild(banner);
        bannerSlot.AddChild(Spacer(0, 8 * k));
    }

    static UpdateBanner.Stage BannerStage(string ver)
    {
        if (Updater.Status == Updater.State.Applying && Updater.Target == ver) return UpdateBanner.Stage.Applying;
        if (Updater.ReadyVersion == ver) return UpdateBanner.Stage.Ready;
        if (Updater.Status == Updater.State.Downloading && Updater.Target == ver) return UpdateBanner.Stage.Downloading;
        return UpdateBanner.Stage.Available;
    }

    // ---------------- header ----------------

    Control Header()
    {
        float k = K;
        // 4:3 / 5:4 windows: slightly smaller nav buttons so all six fit next to the title
        bool compact = GetViewportRect().Size.X / k - 96 < 1800;
        float bw = compact ? 0.86f : 1f, fpx = compact ? 18 : 20;
        var h = HBox(20 * k);
        var title = new DrawBox
        {
            CustomMinimumSize = new Vector2((compact ? 540 : 600) * k, 112 * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(86, k);
                float baseY = 74 * k;
                float w = Gfx.TextW(UiTheme.Display, "VAL", fs);
                Gfx.Text(d, UiTheme.Display, "VAL", 0, baseY, fs, UiTheme.Accent);
                Gfx.Text(d, UiTheme.Display, "TRAINER", w + 2 * k, baseY, fs, UiTheme.Text);
                int ss = UiTheme.Fs(15, k);
                Gfx.Text(d, UiTheme.HudWide, "AIM · MOVEMENT · UTILITY DRILLS — PLAYED WITH YOUR OWN VALORANT SETTINGS", 3 * k, 104 * k, ss, UiTheme.Dim);
            },
        };
        h.AddChild(title);

        var right = VBox(12 * k);
        right.Alignment = BoxContainer.AlignmentMode.Center;
        var nav = HBox(12 * k);
        nav.Alignment = BoxContainer.AlignmentMode.End;
        var warm = Btn("WARM UP", VButton.Look.Primary, Main.I.ShowWarmup, 176 * bw, 48, fpx);
        warm.TooltipText = "Guided warm-up routine (8 / 15 / 25 min) with an optional sens shifter and a lock-in graph";
        nav.AddChild(warm);
        var agents = Btn("AGENTS", VButton.Look.Secondary, Main.I.ShowAgents, 156 * bw, 48, fpx);
        agents.TooltipText = "Agent training: every agent's abilities, signature drills with their own utility, role drills and a 10-minute agent warm-up";
        nav.AddChild(agents);
        var coach = Btn("COACH", VButton.Look.Secondary, Main.I.ShowProfile, 156 * bw, 48, fpx);
        coach.TooltipText = "Aim coach: your estimated rank per skill, what you do wrong, how to fix it and a sens recommendation";
        nav.AddChild(coach);
        nav.AddChild(Btn("SETTINGS", VButton.Look.Secondary, Main.I.ShowSettings, 156 * bw, 48, fpx));
        nav.AddChild(Btn("STATS", VButton.Look.Secondary, Main.I.ShowStats, 156 * bw, 48, fpx));
        nav.AddChild(Btn("QUIT", VButton.Look.Secondary, Main.I.Quit, 116 * bw, 48, fpx));
        right.AddChild(nav);
        coachHint = new CoachHint { K = k, CustomMinimumSize = new Vector2(0, 22 * k), Visible = false };
        coachHint.Pressed += Main.I.ShowProfile;
        right.AddChild(coachHint);
        right.AddChild(Lbl("Fan-made trainer · not endorsed by or affiliated with Riot Games", UiTheme.Body, 13, UiTheme.Faint, HorizontalAlignment.Right));
        h.AddChild(right);
        UpdateCoachHint();
        return h;
    }

    // ---------------- coach hint (top problem one-liner, once the profile is ready) ----------------

    CoachHint? coachHint;
    bool coachHintDone;
    static bool devScreenDone;

    public override void _Ready()
    {
        CoachData.Request(); // background: ready by the time the player looks at the header
        base._Ready();
        if (devScreenDone || !Main.I.Dev) return;
        devScreenDone = true;
        // Dev-only: "--screen profile" opens the coach screen directly (layout checks / screenshots).
        var a = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
        int i = Array.IndexOf(a, "--screen");
        if (i >= 0 && i + 1 < a.Length && a[i + 1].Equals("profile", StringComparison.OrdinalIgnoreCase))
            Callable.From(Main.I.ShowProfile).CallDeferred();
        // Dev-only: "--map-picker" opens the map pop-up once the layout has settled (screenshots).
        if (a.Contains("--map-picker"))
            GetTree().CreateTimer(0.5).Timeout += () => { if (IsInstanceValid(this) && IsInsideTree()) mapSel?.Open(); };
    }

    protected override void Tick(float dt)
    {
        if (!coachHintDone) UpdateCoachHint();
        if (bannerRev != UpdateCheck.Revision) RefreshBanner();
    }

    void UpdateCoachHint()
    {
        if (coachHint == null || CoachData.Ready is not { } p) return;
        coachHintDone = true;
        if (!CoachData.HasData(p)) return;
        coachHint.Rank = CoachData.HasOverall(p) ? CoachRank.RangeText(p.OverallTier, p.OverallConfidence) : null;
        coachHint.RankColor = CoachRank.Col(p.OverallTier);
        coachHint.Problem = p.Problems.FirstOrDefault()?.Title;
        coachHint.TooltipText = "Open the aim coach";
        coachHint.Visible = coachHint.Rank != null || coachHint.Problem != null;
        coachHint.QueueRedraw();
    }

    // ---------------- left: tier, map, drills ----------------

    void BuildLeft(VBoxContainer left, float leftW, float gridH)
    {
        float k = K;
        // caption row: "DIFFICULTY TIER ……… note"  +  the map selector
        var top = HBox(16 * k);
        top.AddChild(new SectionLabel
        {
            Text = "DIFFICULTY TIER", Note = "Bots, target sizes and rank benchmarks scale with the tier", K = k,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        mapSel = new MapSelector(this, k) { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        top.AddChild(mapSel);
        left.AddChild(top);

        var tiers = HBox(10 * k);
        foreach (var t in Difficulty.Tiers)
        {
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = t.Name.ToUpperInvariant(), Sub = t.Ranks, Emblem = t.Id,
                FontPx = 22, SubPx = 13, CustomMinimumSize = new Vector2(0, 66 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Selected = Main.I.Tier == t.Id, TooltipText = TierLine(t),
            };
            int id = t.Id;
            b.Pressed += () => SelectTier(id);
            b.MouseEntered += () => { hoverTier = id; tierInfo?.QueueRedraw(); };
            b.MouseExited += () => { if (hoverTier == id) hoverTier = -1; tierInfo?.QueueRedraw(); };
            tierButtons.Add(b);
            tiers.AddChild(b);
        }
        left.AddChild(tiers);

        tierInfo = new DrawBox { CustomMinimumSize = new Vector2(0, 34 * k), OnDraw = DrawTierInfo };
        left.AddChild(tierInfo);

        BuildGrid(left, leftW, gridH);
    }

    // ---- the drill grid ----

    /// <summary>Card heights (design units): one description line (dense) or two (when everything fits anyway).</summary>
    const float CardOne = 68, CardTwo = 86, CardGap = 8, LabelH = 30, GroupGap = 6 + 2 * CardGap, ColGap = 14, MinCardW = 300;

    static float GroupH(int cards, float cardH) => LabelH + cards * (cardH + CardGap);

    /// <summary>Drills added in a version show a NEW tag until played once, through that version's minor releases.</summary>
    static readonly Dictionary<string, string> AddedIn = new()
    {
        ["microshot"] = "1.4", ["switch"] = "1.4", ["popup"] = "1.4", ["longtaps"] = "1.4", ["jumppeek"] = "1.4",
        ["jigglepeek"] = "1.4", ["postplant"] = "1.4", ["retake"] = "1.4", ["anchor"] = "1.4", ["sound"] = "1.4",
    };

    static bool IsNew(string key)
    {
        if (!AddedIn.TryGetValue(key, out var v) || !SemVer.TryParse(v, out var added) || !SemVer.TryParse(AppInfo.Version, out var app)) return false;
        bool current = added.Major > app.Major || (added.Major == app.Major && added.Minor >= app.Minor);
        return current && !Main.I.Stats.For(key).Any();
    }

    /// <summary>
    /// Category groups (plus the Agents link) packed into columns so the tallest column is as short as possible
    /// (exhaustive search with pruning; there are only ~9 groups). Columns keep the categories' registry order.
    /// </summary>
    void BuildGrid(VBoxContainer left, float leftW, float gridH)
    {
        float k = K;
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        left.AddChild(scroll);
        var colsBox = HBox(ColGap * k);
        colsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(colsBox);

        var groups = modes.GroupBy(m => m.Info.Category).Select(g => (Cat: g.Key, Items: g.ToList())).ToList();
        var counts = groups.Select(g => g.Items.Count).Append(1).ToArray(); // last group: the Agents link card
        int ncol = Math.Clamp((int)((leftW / k + ColGap) / (MinCardW + ColGap)), 2, 5);
        bool two = true;
        var assign = Partition(counts.Select(c => GroupH(c, CardTwo)).ToArray(), ncol, GroupGap, out float tallest);
        if (tallest > gridH)
        {
            two = false;
            assign = Partition(counts.Select(c => GroupH(c, CardOne)).ToArray(), ncol, GroupGap, out _);
        }
        float cardH = two ? CardTwo : CardOne;
        var columns = Enumerable.Range(0, ncol)
            .Select(c => Enumerable.Range(0, counts.Length).Where(g => assign[g] == c).ToList())
            .OrderBy(l => l.Count == 0 ? int.MaxValue : l[0]).ToList();

        foreach (var list in columns)
        {
            var box = VBox(CardGap * k);
            box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            colsBox.AddChild(box);
            foreach (int g in list)
            {
                if (box.GetChildCount() > 0) box.AddChild(Spacer(0, (GroupGap - 2 * CardGap) * k));
                if (g == groups.Count)
                {
                    box.AddChild(new SectionLabel { Text = "AGENTS", Note = $"{Agents.AgentRoster.All.Length} agents", K = k });
                    var link = new AgentsLinkCard { K = k, TwoLines = two, CustomMinimumSize = new Vector2(0, cardH * k), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                    link.Pressed += Main.I.ShowAgents;
                    box.AddChild(link);
                    continue;
                }
                var (cat, items) = groups[g];
                box.AddChild(new SectionLabel { Text = cat.ToUpperInvariant(), Note = items.Count == 1 ? "1 drill" : $"{items.Count} drills", K = k });
                foreach (var (make, info) in items)
                {
                    var card = new ModeCard(info)
                    {
                        K = k, TwoLines = two, IsNew = IsNew(info.Key),
                        CustomMinimumSize = new Vector2(0, cardH * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    };
                    card.Pressed += () => Main.I.StartMode(make);
                    cards.Add(card);
                    box.AddChild(card);
                }
            }
        }
    }

    /// <summary>Assigns groups of heights <paramref name="h"/> to <paramref name="ncol"/> columns minimising the tallest one.</summary>
    static int[] Partition(float[] h, int ncol, float gap, out float tallest)
    {
        int n = h.Length;
        var order = Enumerable.Range(0, n).OrderByDescending(i => h[i]).ToArray();
        var assign = new int[n];
        var best = new int[n];
        var load = new float[ncol];
        var count = new int[ncol];
        float bestMax = float.MaxValue;
        void Place(int idx, float curMax)
        {
            if (curMax >= bestMax) return;
            if (idx == n) { bestMax = curMax; Array.Copy(assign, best, n); return; }
            int g = order[idx];
            for (int c = 0; c < ncol; c++)
            {
                if (c > 0 && count[c] == 0 && count[c - 1] == 0) break; // empty columns are interchangeable
                float add = h[g] + (count[c] > 0 ? gap : 0);
                load[c] += add; count[c]++; assign[g] = c;
                Place(idx + 1, Math.Max(curMax, load[c]));
                load[c] -= add; count[c]--;
            }
        }
        Place(0, 0);
        tallest = bestMax;
        return best;
    }

    static string TierLine(Tier t)
    {
        var (fmin, fmax) = Difficulty.FlickDistance(t.Id);
        return $"Bots react in ~{t.Bot.ReactMs:0} ms · Gridshot targets {Difficulty.GridshotRadius(t.Id) * 200:0} cm wide · "
             + $"head flicks at {fmin:0}–{fmax:0} m · {(t.Bot.RunAndGun ? "bots run-and-gun" : "bots stop before shooting")}";
    }

    void DrawTierInfo(DrawBox d)
    {
        float k = K, cy = d.Size.Y / 2;
        var t = Difficulty.Get(hoverTier >= 0 ? hoverTier : Main.I.Tier);
        var col = UiTheme.TierColor(t.Id);
        RankEmblem.Draw(d, new Vector2(12 * k, cy), 10 * k, t.Id);
        int fs = UiTheme.Fs(17, k), ds = UiTheme.Fs(15, k);
        string head = $"{t.Name.ToUpperInvariant()}  ·  {t.Ranks.ToUpperInvariant()}";
        float w = Gfx.TextW(UiTheme.HudWide, head, fs);
        Gfx.Text(d, UiTheme.HudWide, head, 32 * k, Gfx.Mid(cy, fs), fs, col);
        Gfx.TextFit(d, UiTheme.Body, TierLine(t), 32 * k + w + 18 * k, Gfx.Mid(cy, ds), ds, UiTheme.Dim, d.Size.X - (32 * k + w + 18 * k));
    }

    void SelectTier(int id)
    {
        Main.I.Settings.Tier = id;
        Main.I.Settings.Save();
        for (int i = 0; i < tierButtons.Count; i++) tierButtons[i].SetSelected(Main.I.Tier == i);
        foreach (var c in cards) c.Refresh();
        tierInfo?.QueueRedraw();
    }

    // ---------------- right: imported settings + crosshair ----------------

    void BuildRight(VBoxContainer right)
    {
        float k = K;
        var app = Main.I;
        var v = app.Valorant;

        var panel = new VPanel { Title = "FROM VALORANT", K = k, Caption = v.Found ? "auto-imported" : "defaults" };
        right.AddChild(panel);
        var rows = VBox(0);
        panel.AddChild(rows);

        rows.AddChild(new InfoRow
        {
            K = k, H = 30, Label = "Source",
            Value = () => v.Found ? v.Source.Replace("Account ", "") : "Not found — set your sens in Settings",
            ValueColor = () => v.Found ? UiTheme.Good : UiTheme.Warn,
        });
        string Mode(int m) => m switch { 0 => "Fullscreen", 1 => "Windowed Fullscreen", _ => "Windowed" };
        int stripe = 0;
        void Row(string label, Func<string> value, Func<Color>? color = null, Func<Color?>? swatch = null, Func<string?>? tag = null) =>
            rows.AddChild(new InfoRow { K = k, Label = label, Value = value, ValueColor = color, Swatch = swatch, Tag = tag, Stripe = stripe++ % 2 == 0 });

        var inv = CultureInfo.InvariantCulture;
        Row("Sensitivity", () => app.Sens.ToString("0.###", inv), tag: () => app.Settings.UseSensOverride ? "CUSTOM" : null);
        Row("eDPI / cm per 360", () => $"{app.Sens * app.Settings.Dpi:0} · {PlayerView.Cm360(app.Sens, app.Settings.Dpi):0.0} cm @ {app.Settings.Dpi} DPI");
        Row("Scoped / ADS multiplier", () => $"{v.ZoomedSensMult.ToString("0.###", inv)} / {v.AdsSensMult.ToString("0.###", inv)}");
        Row("Enemy highlight", () => UiTheme.EnemyColorNames[Math.Clamp(app.EnemyColorIndex, 0, 3)], () => app.EnemyColor, () => app.EnemyColor,
            () => app.Settings.EnemyColorOverride >= 0 ? "OVERRIDE" : null);
        Row("Resolution", () => $"{v.ResX}×{v.ResY} · {Mode(app.WindowMode)} · #{app.Monitor + 1}");
        Row("FPS cap / VSync", () => $"{(app.FpsCap <= 0 ? "Unlimited" : app.FpsCap.ToString())} / {(app.VSync ? "On" : "Off")}");
        Row("Anti-aliasing", () => SettingsScreen.AaName(v.AntiAliasing));
        Row("Crosshair profile", () => Valorant.CrosshairCode.Effective.Name);

        // crosshair preview
        var xp = new VPanel { Title = "CROSSHAIR", K = k, Caption = "primary · real pixel size", SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(xp);
        var box = new DrawBox { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 150 * k), ClipContents = true, OnDraw = DrawRange };
        xp.AddChild(box);
        var xh = new CrosshairView { Profile = Valorant.CrosshairCode.Effective, LocalCenter = true };
        box.AddChild(xh);
        xh.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Range-like backdrop for the crosshair preview: wall, floor and a dummy whose head sits under the crosshair.</summary>
    void DrawRange(DrawBox d)
    {
        float k = K;
        var s = d.Size;
        var c = s / 2;
        float floorY = c.Y + 74 * k;
        Gfx.VGradient(d, new Rect2(0, 0, s.X, floorY), Color.Color8(96, 112, 126), Color.Color8(74, 88, 100));
        var seam = new Color(0, 0, 0, 0.12f);
        for (float x = c.X % (90 * k); x < s.X; x += 90 * k) d.DrawRect(new Rect2(x, 0, 1, floorY), seam);
        d.DrawRect(new Rect2(0, floorY - 10 * k, s.X, 10 * k), Color.Color8(60, 70, 80));
        Gfx.VGradient(d, new Rect2(0, floorY, s.X, s.Y - floorY), Color.Color8(58, 66, 76), Color.Color8(40, 46, 54));
        var lane = new Color(1, 1, 1, 0.05f);
        for (int i = -6; i <= 6; i++) d.DrawLine(new Vector2(c.X + i * 30 * k, floorY), new Vector2(c.X + i * 170 * k, s.Y), lane, 1f);

        // dummy (head centred on the crosshair), outlined in the enemy highlight colour
        var enemy = Main.I.EnemyColor;
        var body = Color.Color8(120, 128, 136);
        float hr = 9 * k, ow = 2f * Mathf.Max(1, k);
        var head = c;
        var torso = new Rect2(c.X - 23 * k, c.Y + hr + 5 * k, 46 * k, floorY - (c.Y + hr + 5 * k));
        d.DrawRect(torso.Grow(ow), enemy);
        d.DrawCircle(head, hr + ow, enemy);
        d.DrawRect(new Rect2(c.X - 4 * k, c.Y + hr - 1, 8 * k, 8 * k), body);
        d.DrawRect(torso, body);
        d.DrawCircle(head, hr, body.Lightened(0.08f));

        Gfx.Brackets(d, new Rect2(Vector2.Zero, s).Grow(-1), 14 * k, new Color(UiTheme.Text, 0.35f), Mathf.Max(1, 2 * k));
    }
}

/// <summary>Header one-liner linking to the aim coach: "COACH · GOLD 2 – PLATINUM 1 · FIX FIRST: …  ›" (right-aligned).</summary>
public partial class CoachHint : BaseButton
{
    public float K = 1f;
    public string? Rank, Problem;
    public Color RankColor = UiTheme.Text;
    float hover;

    public CoachHint()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready() => MouseEntered += UiTheme.HoverSound;

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2, x = Size.X;
        int ls = UiTheme.Fs(12, k), ts = UiTheme.Fs(14, k);
        // laid out right-to-left so the line hugs the nav buttons' right edge
        Gfx.Chevron(this, new Vector2(x - 4 * k, cy), 10 * k, 1, new Color(UiTheme.Text, 0.5f + 0.5f * hover), 2.5f * k);
        x -= 16 * k;
        float minX = 0;
        string head = "COACH";
        float headW = 14 * k + Gfx.TextW(UiTheme.HudWide, head, ls) + 12 * k;
        float rankW = Rank != null ? Gfx.TextW(UiTheme.HudWide, Rank, ls) + 18 * k : 0;
        if (Problem != null)
        {
            string p = "Fix first: " + Problem;
            float avail = x - minX - headW - rankW;
            float w = Mathf.Min(Gfx.TextW(UiTheme.Body, p, ts), avail);
            Gfx.TextFit(this, UiTheme.Body, p, x - w, Gfx.Mid(cy, ts), ts, UiTheme.Text.Lerp(Colors.White, hover * 0.3f), w);
            x -= w + 12 * k;
        }
        if (Rank != null)
        {
            Gfx.TextR(this, UiTheme.HudWide, Rank, x, Gfx.Mid(cy, ls), ls, RankColor);
            x -= rankW;
        }
        float hx = x - Gfx.TextW(UiTheme.HudWide, head, ls);
        Gfx.Text(this, UiTheme.HudWide, head, hx, Gfx.Mid(cy, ls), ls, UiTheme.Accent);
        Gfx.Diamond(this, new Vector2(hx - 10 * k, cy), 4 * k, 4 * k, UiTheme.Accent);
        if (hover > 0.01f) DrawRect(new Rect2(hx - 16 * k, Size.Y - 1, Size.X - hx + 16 * k, 1), new Color(UiTheme.Accent, hover));
    }
}

/// <summary>
/// One drill card: name (+ a NEW / SIGNATURE tag), PB at the current tier and best badge ever on the first line, the
/// description (one or two lines; the tooltip has all of it) below. The Agents screen overrides the title, the second
/// line ("Uses Curveball (Q)") and the accent colour.
/// </summary>
public partial class ModeCard : BaseButton
{
    readonly TrainingMode info;
    public float K = 1f;
    /// <summary>Two description lines instead of one.</summary>
    public bool TwoLines;
    /// <summary>Added in this version and never played: shows a NEW tag.</summary>
    public bool IsNew;
    /// <summary>Card title instead of the drill's name.</summary>
    public string? Title;
    /// <summary>Second line instead of the description.</summary>
    public string? Sub;
    /// <summary>Colour of the left bar, the tag and <see cref="Sub"/>; null = the category colour.</summary>
    public Color? Tint;
    /// <summary>Tag after the name (overrides NEW).</summary>
    public string? Tag;
    float hover;
    int pb = -1, badge = -1;
    string pbText = "—";

    public ModeCard(TrainingMode info)
    {
        this.info = info;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = $"{info.Name} — {info.Description}";
    }

    public string Key => info.Key;

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
        Refresh();
    }

    public void Refresh()
    {
        var stats = Main.I.Stats;
        pb = stats.Best(info.Key, Main.I.Tier);
        badge = stats.BestBadge(info.Key);
        pbText = pb >= 0 ? pb.ToString("#,0", CultureInfo.InvariantCulture) : "—";
        QueueRedraw();
    }

    public static Color CategoryColor(string cat) => cat switch
    {
        "Aim" => UiTheme.Accent,
        "Valorant" => UiTheme.Teal,
        "Movement" => UiTheme.Warn,
        "Map" => Color.Color8(110, 170, 255),
        "Utility" => Color.Color8(186, 120, 255),
        "Agents" => Color.Color8(255, 150, 100),
        _ => UiTheme.Dim,
    };

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        bool down = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var fill = new Color(0.075f, 0.115f, 0.155f, 0.9f).Lerp(new Color(0.13f, 0.18f, 0.23f, 0.96f), hover);
        if (down) fill = new Color(0.18f, 0.22f, 0.27f, 0.98f);
        Gfx.Plate(this, r, 12 * k, fill, new Color(UiTheme.Text, 0.08f + 0.3f * hover));
        var cc = Tint ?? CategoryColor(info.Category);
        DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y - 14 * k), new Color(cc, 0.55f + 0.45f * hover));
        if (hover > 0.01f) Gfx.Brackets(this, r.Grow(3 * k), 8 * k * hover, new Color(UiTheme.Text, 0.8f * hover), Mathf.Max(1, 2 * k));

        float pad = 16 * k, cy = 24 * k, right = r.Size.X - 14 * k;
        // best badge ever (top right); none for drills without a meaningful score (Sens Finder)
        if (info.ShowScore)
        {
            float er = 9 * k;
            RankEmblem.Draw(this, new Vector2(right - er, cy), er, badge, badge >= 0 ? 1f : 0.55f);
            right -= er * 2 + 10 * k;
        }

        // name + tag (the name gets the whole first line, so it's never cut)
        string? tag = Tag ?? (IsNew ? "NEW" : null);
        int ns = UiTheme.Fs(23, k), ts = UiTheme.Fs(10, k);
        float tagW = tag != null ? Gfx.TextW(UiTheme.HudWide, tag, ts) + 10 * k : 0;
        string name = (Title ?? info.Name).ToUpperInvariant();
        float nameMax = Mathf.Max(20 * k, right - pad - (tag != null ? tagW + 8 * k : 0));
        float nw = Mathf.Min(Gfx.TextW(UiTheme.Display, name, ns), nameMax);
        Gfx.TextFit(this, UiTheme.Display, name, pad, Gfx.Mid(cy, ns), ns, UiTheme.Text, nameMax);
        if (tag != null)
        {
            var tc = Tag != null ? cc : UiTheme.Accent;
            var tr = new Rect2(pad + nw + 8 * k, cy - 8.5f * k, tagW, 17 * k);
            DrawRect(tr, new Color(tc, Tag != null ? 0.12f : 0.85f));
            if (Tag != null) DrawRect(tr, new Color(tc, 0.7f), false, 1f);
            Gfx.Text(this, UiTheme.HudWide, tag, tr.Position.X + 5 * k, Gfx.Mid(cy, ts), ts, Tag != null ? tc : UiTheme.Text);
        }

        // PB at this tier (bottom right, once there is one), then the description (or the agent page's "Uses …" line)
        int ds = UiTheme.Fs(13.5f, k);
        float lineY = TwoLines ? r.Size.Y - 18 * k : 50 * k, descRight = r.Size.X - pad;
        if (info.ShowScore && pb >= 0)
        {
            int vs = UiTheme.Fs(17, k), ls = UiTheme.Fs(10, k);
            float x = r.Size.X - 14 * k;
            x -= Gfx.TextR(this, UiTheme.Display, pbText, x, Gfx.Mid(lineY, vs), vs, UiTheme.Good) + 6 * k;
            x -= Gfx.TextR(this, UiTheme.HudWide, "PB", x, Gfx.Mid(lineY, ls), ls, UiTheme.Faint);
            descRight = x - 12 * k;
        }
        string desc = Sub ?? info.Description;
        var dc = Sub != null && Tint is { } tint ? tint : UiTheme.Dim;
        if (TwoLines)
            DrawMultilineString(UiTheme.Body, new Vector2(pad, 42 * k + UiTheme.Body.GetAscent(ds)), desc, HorizontalAlignment.Left, descRight - pad, ds, 2, dc);
        else
            Gfx.TextFit(this, UiTheme.Body, desc, pad, Gfx.Mid(lineY, ds), ds, dc, descRight - pad);
    }
}

/// <summary>
/// Compact map control: "▪ MAP  ‹ [▦ ASCENT ▾] ›". The arrows step through the maps, the name opens a grid of every map
/// plus RANDOM (<see cref="MapPickerOverlay"/>). The choice is saved to <see cref="AppSettings.MapKey"/> (map, utility and
/// agent drills read <see cref="Main.MapKey"/>).
/// </summary>
public partial class MapSelector : HBoxContainer
{
    readonly Control host;
    readonly float k;
    readonly MapButton current;
    MapPickerOverlay? overlay;
    /// <summary>Called after the map changes.</summary>
    public Action? Changed;

    /// <summary>RANDOM, then every map once (MapSpots.All order).</summary>
    public static List<(string Key, string Name)> Options()
    {
        var list = new List<(string Key, string Name)> { ("random", "RANDOM") };
        list.AddRange(MapSpots.All.Select(m => (m.Key, m.Map.ToUpperInvariant())).DistinctBy(m => m.Key));
        return list;
    }

    public static string NameOf(string key) =>
        Options().FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)).Name ?? key.ToUpperInvariant();

    /// <param name="host">The screen the pop-up is added to (drawn on top of everything).</param>
    public MapSelector(Control host, float k, bool label = true)
    {
        this.host = host;
        this.k = k;
        AddThemeConstantOverride("separation", (int)(6 * k));
        if (label) AddChild(new DrawBox { CustomMinimumSize = new Vector2(56 * k, 40 * k), OnDraw = DrawLabel });
        var prev = new VButton { Kind = VButton.Look.Arrow, Dir = -1, K = k, CustomMinimumSize = new Vector2(40 * k, 40 * k), TooltipText = "Previous map" };
        prev.Pressed += () => Step(-1);
        AddChild(prev);
        current = new MapButton { K = k, CustomMinimumSize = new Vector2(196 * k, 40 * k), TooltipText = "Map for the map, utility and agent drills · click to see every map" };
        current.Pressed += () => { if (IsOpen) Close(); else Open(); };
        AddChild(current);
        var next = new VButton { Kind = VButton.Look.Arrow, Dir = 1, K = k, CustomMinimumSize = new Vector2(40 * k, 40 * k), TooltipText = "Next map" };
        next.Pressed += () => Step(1);
        AddChild(next);
    }

    void DrawLabel(DrawBox d)
    {
        float cy = d.Size.Y / 2;
        int fs = UiTheme.Fs(14, k);
        d.DrawRect(new Rect2(0, cy - 3 * k, 6 * k, 6 * k), UiTheme.Accent);
        Gfx.Text(d, UiTheme.HudWide, "MAP", 14 * k, Gfx.Mid(cy, fs), fs, UiTheme.Dim);
    }

    public bool IsOpen => overlay != null && IsInstanceValid(overlay) && overlay.IsInsideTree();

    void Step(int dir)
    {
        var opts = Options();
        int i = opts.FindIndex(o => string.Equals(o.Key, Main.I.MapKey, StringComparison.OrdinalIgnoreCase));
        if (i < 0) i = 0;
        Select(opts[(i + dir + opts.Count) % opts.Count].Key);
    }

    public void Select(string key)
    {
        Main.I.Settings.MapKey = key;
        Main.I.Settings.Save();
        current.QueueRedraw();
        Changed?.Invoke();
    }

    public void Open()
    {
        if (IsOpen || !IsInsideTree()) return;
        overlay = new MapPickerOverlay(k, current.GetGlobalRect(), Main.I.MapKey, key => { Select(key); Close(); }, Close);
        host.AddChild(overlay);
        current.Open = true;
        current.QueueRedraw();
    }

    public void Close()
    {
        if (overlay != null && IsInstanceValid(overlay)) overlay.QueueFree();
        overlay = null;
        if (IsInstanceValid(current)) { current.Open = false; current.QueueRedraw(); }
    }

    public override void _ExitTree() => Close();
}

/// <summary>The selector's middle button: a small map postcard, the map name and a ▾.</summary>
public partial class MapButton : BaseButton
{
    public float K = 1f;
    public bool Open;
    float hover;

    public MapButton()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
    }

    public override void _Process(double delta)
    {
        float t = IsHovered() || Open ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        string key = Main.I.MapKey;
        DrawRect(r, new Color(0.06f, 0.1f, 0.14f, 0.78f).Lerp(new Color(0.93f, 0.91f, 0.88f, 0.14f), hover));
        DrawRect(r, Open ? UiTheme.Text : new Color(UiTheme.Text, 0.2f + 0.45f * hover), false, 1f);
        if (Open) DrawRect(new Rect2(0, r.Size.Y - 3 * k, r.Size.X, 3 * k), UiTheme.Accent);
        var sw = new Rect2(6 * k, 6 * k, 34 * k, r.Size.Y - 12 * k);
        MapSwatch.Draw(this, sw, key, k);
        DrawRect(sw, new Color(0, 0, 0, 0.35f), false, 1f);
        int fs = UiTheme.Fs(19, k);
        Gfx.TextFit(this, UiTheme.Display, MapSelector.NameOf(key), 50 * k, Gfx.Mid(r.Size.Y / 2, fs), fs, UiTheme.Text, r.Size.X - 50 * k - 30 * k);
        Gfx.ChevronDown(this, new Vector2(r.Size.X - 16 * k, r.Size.Y / 2), 12 * k, 7 * k, 2.5f * k, new Color(UiTheme.Text, 0.6f + 0.4f * hover));
    }
}

/// <summary>Map pop-up: dims the screen and shows every map (+ RANDOM) as a grid of postcards; click outside or Esc closes.</summary>
public partial class MapPickerOverlay : Control
{
    readonly float k;
    readonly Rect2 anchor;
    readonly string current;
    readonly Action<string> pick;
    readonly Action close;
    Rect2 panel;
    float headH;

    public MapPickerOverlay(float k, Rect2 anchor, string current, Action<string> pick, Action close)
    {
        this.k = k;
        this.anchor = anchor;
        this.current = current;
        this.pick = pick;
        this.close = close;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var opts = MapSelector.Options();
        var vp = GetViewportRect().Size;
        float tw = 150 * k, th = 82 * k, gap = 8 * k, pad = 18 * k;
        headH = 50 * k;
        int cols = Math.Min(7, opts.Count);
        float W(int c) => pad * 2 + c * tw + (c - 1) * gap;
        while (cols > 3 && W(cols) > vp.X - 16 * k) cols--;
        int rows = (opts.Count + cols - 1) / cols;
        float w = W(cols), h = pad + headH + rows * th + (rows - 1) * gap + pad;
        float x = Mathf.Clamp(anchor.End.X - w, 8 * k, Mathf.Max(8 * k, vp.X - w - 8 * k));
        float y = anchor.End.Y + 8 * k;
        if (y + h > vp.Y - 8 * k) y = Mathf.Max(8 * k, anchor.Position.Y - h - 8 * k);
        panel = new Rect2(x, y, w, h);
        for (int i = 0; i < opts.Count; i++)
        {
            var (key, name) = opts[i];
            var t = new MapTile(key, name)
            {
                K = k, Selected = string.Equals(key, current, StringComparison.OrdinalIgnoreCase),
                Position = new Vector2(x + pad + (i % cols) * (tw + gap), y + pad + headH + (i / cols) * (th + gap)),
                Size = new Vector2(tw, th),
            };
            string kk = key;
            t.Pressed += () => pick(kk);
            AddChild(t);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb && !panel.HasPoint(mb.Position))
        {
            AcceptEvent();
            close();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.04f, 0.06f, 0.45f));
        Gfx.Plate(this, panel, 16 * k, new Color(0.06f, 0.095f, 0.13f, 0.98f), new Color(UiTheme.Text, 0.16f));
        DrawRect(new Rect2(panel.Position, new Vector2(34 * k, 2 * k)), UiTheme.Accent);
        float pad = 18 * k, cy = panel.Position.Y + pad + 14 * k;
        int fs = UiTheme.Fs(19, k), ns = UiTheme.Fs(13, k);
        DrawRect(new Rect2(panel.Position.X + pad, cy - 3 * k, 6 * k, 6 * k), UiTheme.Accent);
        Gfx.Text(this, UiTheme.DisplayWide, "MAP", panel.Position.X + pad + 16 * k, Gfx.Mid(cy, fs), fs, UiTheme.Text);
        Gfx.TextR(this, UiTheme.Body, "map, utility and agent drills · RANDOM picks a new one every run", panel.End.X - pad, Gfx.Mid(cy, ns), ns, UiTheme.Dim);
        DrawRect(new Rect2(panel.Position.X + pad, panel.Position.Y + pad + headH - 14 * k, panel.Size.X - pad * 2, 1), new Color(UiTheme.Text, 0.09f));
    }
}

/// <summary>A map in the pop-up: postcard in the map's palette, its name, selected = light frame + red underline.</summary>
public partial class MapTile : BaseButton
{
    readonly string key, name;
    public float K = 1f;
    public bool Selected;
    float hover;

    public MapTile(string key, string name)
    {
        this.key = key;
        this.name = name;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = key == "random" ? "Map drills pick a random map each run" : $"Map drills use {name[..1]}{name[1..].ToLowerInvariant()}";
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
    }

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        MapSwatch.Draw(this, r, key, k);
        Gfx.VGradient(this, new Rect2(0, r.Size.Y * 0.45f, r.Size.X, r.Size.Y * 0.55f), new Color(0.03f, 0.05f, 0.07f, 0f), new Color(0.03f, 0.05f, 0.07f, 0.85f));
        if (hover > 0.01f) DrawRect(r, new Color(1, 1, 1, 0.08f * hover));
        int fs = UiTheme.Fs(20, k);
        Gfx.TextFit(this, UiTheme.Display, name, 10 * k, r.Size.Y - 11 * k, fs, UiTheme.Text, r.Size.X - 20 * k);
        if (Selected)
        {
            DrawRect(r, UiTheme.Text, false, Mathf.Max(1, 2 * k));
            DrawRect(new Rect2(0, r.Size.Y - 3 * k, r.Size.X, 3 * k), UiTheme.Accent);
        }
        else DrawRect(r, new Color(UiTheme.Text, 0.14f + 0.5f * hover), false, 1f);
        if (hover > 0.01f) Gfx.Brackets(this, r.Grow(3 * k), 7 * k * hover, new Color(UiTheme.Text, 0.85f * hover), Mathf.Max(1, 2 * k));
    }
}

/// <summary>Little "postcard" of a map drawn from its palette (sky, walls, trim, floor, a crate); RANDOM is a strip of several.</summary>
public static class MapSwatch
{
    static int Seed(string s)
    {
        int h = 17;
        foreach (char c in s) h = h * 31 + c;
        return h & 0x7fffffff;
    }

    public static void Draw(CanvasItem ci, Rect2 r, string key, float k)
    {
        if (key == "random")
        {
            var maps = MapSpots.All;
            int n = Math.Min(5, maps.Length);
            for (int i = 0; i < n; i++)
            {
                var m = maps[(i * 3 + 1) % maps.Length];
                Scene(ci, new Rect2(r.Position.X + r.Size.X * i / n, r.Position.Y, r.Size.X / n + 0.5f, r.Size.Y), m.Palette, Seed(m.Key));
            }
            ci.DrawRect(r, new Color(0.05f, 0.08f, 0.11f, 0.35f));
            int fs = Math.Max(10, (int)(r.Size.Y * 0.62f));
            Gfx.TextC(ci, UiTheme.Display, "?", r.GetCenter().X, Gfx.Mid(r.Position.Y + r.Size.Y * 0.42f, fs), fs, new Color(UiTheme.Text, 0.9f));
            return;
        }
        var map = MapSpots.All.FirstOrDefault(m => m.Key == key) ?? MapSpots.Ascent;
        Scene(ci, r, map.Palette, Seed(map.Key));
    }

    static void Scene(CanvasItem ci, Rect2 r, MapPalette p, int seed)
    {
        float x0 = r.Position.X, y0 = r.Position.Y, w = r.Size.X, h = r.Size.Y;
        float horizon = y0 + h * 0.62f;
        Gfx.VGradient(ci, new Rect2(x0, y0, w, horizon - y0), p.Sky.Lightened(0.12f), p.Sky.Darkened(0.08f));
        // two building blocks of the wall colour (heights vary per map), a darker one behind
        var rng = new Random(seed);
        float bx = x0 - w * 0.1f;
        while (bx < x0 + w)
        {
            float bw = w * (0.22f + (float)rng.NextDouble() * 0.3f);
            float top = y0 + h * (0.12f + (float)rng.NextDouble() * 0.3f);
            var col = rng.Next(3) == 0 ? p.Elevated : p.Wall;
            float l = Mathf.Max(x0, bx), rr = Mathf.Min(x0 + w, bx + bw);
            if (rr > l)
            {
                ci.DrawRect(new Rect2(l, top, rr - l, horizon - top), col.Darkened(0.12f));
                ci.DrawRect(new Rect2(l, top, rr - l, Mathf.Max(1, h * 0.05f)), p.Trim);
                ci.DrawRect(new Rect2(l, top, Mathf.Min(rr - l, Mathf.Max(1, w * 0.02f)), horizon - top), new Color(0, 0, 0, 0.18f));
            }
            bx += bw + w * (0.02f + (float)rng.NextDouble() * 0.08f);
        }
        ci.DrawRect(new Rect2(x0, horizon - Mathf.Max(1, h * 0.04f), w, Mathf.Max(1, h * 0.04f)), p.Trim.Darkened(0.2f));
        Gfx.VGradient(ci, new Rect2(x0, horizon, w, y0 + h - horizon), p.Floor, p.Floor.Darkened(0.4f));
        float cx = x0 + w * (0.55f + (float)rng.NextDouble() * 0.25f);
        float cw = w * 0.16f, ch = h * 0.16f;
        float l2 = Mathf.Max(x0, cx), r2 = Mathf.Min(x0 + w, cx + cw);
        if (r2 > l2) ci.DrawRect(new Rect2(l2, horizon - ch * 0.4f, r2 - l2, ch), p.Cover.Darkened(0.05f));
    }
}

/// <summary>Slim strip under the menu header: "VALTRAINER 1.2.0 IS AVAILABLE · you have 1.1.0" / "Downloading update 42%" /
/// "VALTRAINER 1.2.0 IS READY · restart to update" plus its buttons (children laid out by <see cref="MenuScreen"/>).</summary>
public partial class UpdateBanner : Control
{
    public enum Stage { Available, Downloading, Ready, Applying }

    public float K = 1f;
    public string Version = "";
    public Stage Kind;

    public UpdateBanner() => MouseFilter = MouseFilterEnum.Pass;

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2;
        var r = new Rect2(Vector2.Zero, Size);
        bool ready = Kind is Stage.Ready or Stage.Applying;
        Color acc = ready ? UiTheme.Good : UiTheme.Accent;
        Gfx.Plate(this, r, 10 * k, new Color(acc, 0.1f), new Color(acc, 0.45f));
        if (Kind == Stage.Downloading)
        {
            // thin progress bar along the bottom edge
            float p = Updater.Progress;
            DrawRect(new Rect2(4 * k, r.Size.Y - 3 * k, (r.Size.X - 8 * k) * p, 2 * k), new Color(acc, 0.8f));
        }
        DrawRect(new Rect2(0, 0, 4 * k, r.Size.Y - 6 * k), acc);
        Gfx.Diamond(this, new Vector2(24 * k, cy), 6 * k, 6 * k, acc);
        int fs = UiTheme.Fs(15, k), ss = UiTheme.Fs(14, k);
        string v = Version.ToUpperInvariant();
        string head = Kind switch
        {
            Stage.Ready => $"VALTRAINER {v} IS READY",
            Stage.Applying => $"INSTALLING VALTRAINER {v}…",
            _ => $"VALTRAINER {v} IS AVAILABLE",
        };
        string sub = Kind switch
        {
            Stage.Downloading => $"Downloading update {Updater.Progress * 100:0}%",
            Stage.Ready => "restart to update · you have " + AppInfo.Version + (Updater.NeedsElevation ? " · Windows will ask for permission" : ""),
            Stage.Applying => "ValTrainer restarts by itself",
            _ => $"you have {AppInfo.Version}",
        };
        Gfx.Text(this, UiTheme.HudWide, head, 42 * k, Gfx.Mid(cy, fs), fs, UiTheme.Text);
        float x = 42 * k + Gfx.TextW(UiTheme.HudWide, head, fs) + 14 * k;
        Gfx.Text(this, UiTheme.Body, sub, x, Gfx.Mid(cy, ss), ss, Kind == Stage.Ready ? UiTheme.Good : UiTheme.Dim);
    }
}
