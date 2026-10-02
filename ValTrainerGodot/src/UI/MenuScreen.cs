using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Maps;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// Main menu: VALORANT-styled lobby. Left: tier selector, map selector and the drill cards (grouped by category,
/// with PB at the current tier and the best rank badge ever). Right: everything imported from Valorant plus a
/// live crosshair preview. Top-right: Settings / Stats / Quit.
/// </summary>
public partial class MenuScreen : ScreenBase
{
    readonly List<(Func<TrainingMode> Make, TrainingMode Info)> modes = ModeRegistry.All.Select(f => (f, f())).ToList();
    readonly List<VButton> tierButtons = new();
    readonly List<(VButton B, string Key)> mapButtons = new();
    readonly List<ModeCard> cards = new();
    int hoverTier = -1;
    DrawBox? tierInfo;

    protected override void Back() { } // Esc does nothing on the menu (Quit is a button)

    VBoxContainer? bannerSlot;
    int bannerRev = -1;

    protected override void Build()
    {
        tierButtons.Clear(); mapButtons.Clear(); cards.Clear();
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
        BuildLeft(left, GetViewportRect().Size.X - 96 * k - rightW - 34 * k);

        var right = VBox(16 * k);
        right.CustomMinimumSize = new Vector2(rightW, 0);
        body.AddChild(right);
        BuildRight(right);
    }

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
        var h = HBox(20 * k);
        var title = new DrawBox
        {
            CustomMinimumSize = new Vector2(600 * k, 112 * k),
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
        var warm = Btn("WARM UP", VButton.Look.Primary, Main.I.ShowWarmup, 176, 48, 20);
        warm.TooltipText = "Guided warm-up routine (8 / 15 / 25 min) with an optional sens shifter and a lock-in graph";
        nav.AddChild(warm);
        var coach = Btn("COACH", VButton.Look.Secondary, Main.I.ShowProfile, 156, 48, 20);
        coach.TooltipText = "Aim coach: your estimated rank per skill, what you do wrong, how to fix it and a sens recommendation";
        nav.AddChild(coach);
        nav.AddChild(Btn("SETTINGS", VButton.Look.Secondary, Main.I.ShowSettings, 156, 48, 20));
        nav.AddChild(Btn("STATS", VButton.Look.Secondary, Main.I.ShowStats, 156, 48, 20));
        nav.AddChild(Btn("QUIT", VButton.Look.Secondary, Main.I.Quit, 116, 48, 20));
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

    void BuildLeft(VBoxContainer left, float leftW)
    {
        float k = K;
        left.AddChild(new SectionLabel { Text = "DIFFICULTY TIER", Note = "Bots, target sizes and rank benchmarks scale with the tier", K = k });

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

        // map selector
        var maps = HBox(10 * k);
        maps.AddChild(new SectionLabel { Text = "MAP", K = k, CustomMinimumSize = new Vector2(84 * k, 0) });
        var list = new List<(string Key, string Name)> { ("random", "RANDOM") };
        list.AddRange(MapSpots.All.Select(m => (m.Key, m.Map.ToUpperInvariant())).DistinctBy(m => m.Key));
        foreach (var (key, name) in list)
        {
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = name, FontPx = 17, CustomMinimumSize = new Vector2(112 * k, 40 * k),
                Selected = string.Equals(Main.I.MapKey, key, StringComparison.OrdinalIgnoreCase),
                TooltipText = key == "random" ? "Map drills pick a random map each run" : $"Map drills use {name[..1]}{name[1..].ToLowerInvariant()}",
            };
            string kk = key;
            b.Pressed += () => SelectMap(kk);
            mapButtons.Add((b, key));
            maps.AddChild(b);
        }
        var note = Lbl("used by map & utility drills", UiTheme.Body, 13, UiTheme.Faint);
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        maps.AddChild(Spacer(4 * k, 0));
        maps.AddChild(note);
        left.AddChild(maps);
        left.AddChild(Spacer(0, 10 * k));

        // drill cards in balanced category columns
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        left.AddChild(scroll);
        var colsBox = HBox(14 * k);
        colsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(colsBox);

        int ncol = leftW / k >= 1120 ? 3 : 2;
        var groups = modes.GroupBy(m => m.Info.Category).Select(g => (Cat: g.Key, Items: g.ToList())).ToList();
        var colBoxes = new VBoxContainer[ncol];
        var heights = new float[ncol];
        for (int i = 0; i < ncol; i++)
        {
            colBoxes[i] = VBox(10 * k);
            colBoxes[i].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            colsBox.AddChild(colBoxes[i]);
        }
        foreach (var (cat, items) in groups)
        {
            int c = 0;
            for (int i = 1; i < ncol; i++) if (heights[i] < heights[c] - 0.01f) c = i;
            heights[c] += 0.35f + items.Count;
            var box = colBoxes[c];
            if (box.GetChildCount() > 0) box.AddChild(Spacer(0, 6 * k));
            box.AddChild(new SectionLabel { Text = cat.ToUpperInvariant(), Note = items.Count == 1 ? "1 drill" : $"{items.Count} drills", K = k });
            foreach (var (make, info) in items)
            {
                var card = new ModeCard(info) { K = k, CustomMinimumSize = new Vector2(0, 118 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                card.Pressed += () => Main.I.StartMode(make);
                cards.Add(card);
                box.AddChild(card);
            }
        }
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

    void SelectMap(string key)
    {
        Main.I.Settings.MapKey = key;
        Main.I.Settings.Save();
        foreach (var (b, k) in mapButtons) b.SetSelected(string.Equals(Main.I.MapKey, k, StringComparison.OrdinalIgnoreCase));
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

/// <summary>One drill card: name, description, PB at the current tier, best badge ever and category tag.</summary>
public partial class ModeCard : BaseButton
{
    readonly TrainingMode info;
    public float K = 1f;
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
        Gfx.Plate(this, r, 14 * k, fill, new Color(UiTheme.Text, 0.08f + 0.3f * hover));
        DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y - 16 * k), new Color(UiTheme.Accent, 0.45f + 0.55f * hover));
        if (hover > 0.01f) Gfx.Brackets(this, r.Grow(3 * k), 8 * k * hover, new Color(UiTheme.Text, 0.8f * hover), Mathf.Max(1, 2 * k));

        float pad = 20 * k;
        // category tag (top-right, clear of the cut corner)
        int ts = UiTheme.Fs(11, k);
        string cat = info.Category.ToUpperInvariant();
        float tw = Gfx.TextW(UiTheme.HudWide, cat, ts) + 12 * k;
        var cc = CategoryColor(info.Category);
        var tr = new Rect2(r.Size.X - 22 * k - tw, 15 * k, tw, 19 * k);
        DrawRect(tr, new Color(cc, 0.12f));
        DrawRect(tr, new Color(cc, 0.65f), false, 1f);
        Gfx.Text(this, UiTheme.HudWide, cat, tr.Position.X + 6 * k, Gfx.Mid(tr.GetCenter().Y, ts), ts, cc);

        // name + description
        int ns = UiTheme.Fs(27, k);
        Gfx.TextFit(this, UiTheme.Display, info.Name.ToUpperInvariant(), pad, Gfx.Mid(25 * k, ns), ns, UiTheme.Text, tr.Position.X - pad - 8 * k);
        int ds = UiTheme.Fs(14.5f, k);
        DrawMultilineString(UiTheme.Body, new Vector2(pad, 43 * k + UiTheme.Body.GetAscent(ds)), info.Description, HorizontalAlignment.Left,
            r.Size.X - pad * 2, ds, 2, UiTheme.Dim);

        // bottom row: PB at tier (left), best badge (right); none for drills without a meaningful score (Sens Finder)
        if (!info.ShowScore) return;
        float by = r.Size.Y - 16 * k;
        int ls = UiTheme.Fs(11, k), vs = UiTheme.Fs(22, k);
        float x = pad;
        x += Gfx.TextW(UiTheme.HudWide, "PB", ls) + 8 * k;
        Gfx.Text(this, UiTheme.HudWide, "PB", pad, Gfx.Mid(by, ls), ls, UiTheme.Faint);
        Gfx.Text(this, UiTheme.Display, pbText, x, Gfx.Mid(by, vs), vs, pb >= 0 ? UiTheme.Good : UiTheme.Faint);
        x += Gfx.TextW(UiTheme.Display, pbText, vs) + 8 * k;

        float er = 11 * k, ex = r.Size.X - 24 * k - er;
        RankEmblem.Draw(this, new Vector2(ex, by), er, badge, badge >= 0 ? 1f : 0.6f);
        string bl = badge >= 0 ? $"BEST {Difficulty.Tiers[badge].Name.ToUpperInvariant()}" : "NO BADGE YET";
        float blw = Gfx.TextR(this, UiTheme.HudWide, bl, ex - er - 8 * k, Gfx.Mid(by, ls), ls, badge >= 0 ? UiTheme.TierColor(badge) : UiTheme.Faint);
        string tierName = Difficulty.Get(Main.I.Tier).Name.ToUpperInvariant();
        if (x + Gfx.TextW(UiTheme.HudWide, "AT " + tierName, ls) < ex - er - 8 * k - blw - 12 * k)
            Gfx.Text(this, UiTheme.HudWide, "AT " + tierName, x, Gfx.Mid(by, ls), ls, UiTheme.Faint);
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
