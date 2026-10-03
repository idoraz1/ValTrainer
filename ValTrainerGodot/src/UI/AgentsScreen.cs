using Godot;
using ValTrainer.Agents;
using ValTrainer.Core;
using ValTrainer.Modes;
using ValTrainer.Warmup;

namespace ValTrainer.UI;

/// <summary>
/// AGENTS: agent training hub. Left: every agent grouped by role, with a search box (names, abilities, what they do)
/// and role filter chips. Right: the selected agent's page (abilities and what trains them, signature drills played with
/// the agent's own utility, role drills, and a ~10-minute agent warm-up run by the warm-up system), or a role overview.
/// Drills and routines started here come back here afterwards. Esc closes the page, then goes back to the menu.
/// Dev: --screen agents [--agent jett] [--agent-search smoke] [--agent-role sentinel] [--agent-go routine|&lt;drill key&gt;].
/// </summary>
public partial class AgentsScreen : ScreenBase
{
    // remembered for the session, so coming back from a drill lands on the same page
    static string? selKey;
    static string search = "";
    static AgentRole? roleFilter;
    static bool devDone;

    readonly List<AgentTile> tiles = new();
    readonly List<(AgentRole Role, Control Head, Control Grid)> groups = new();
    readonly List<RoleChip> chips = new();
    VBoxContainer? page;
    ScrollContainer? pageScroll;
    LineEdit? searchBox;
    MapSelector? mapSel;
    Control? noMatch;
    bool narrow;

    const float TileW = 88, TileH = 104, TileGap = 8;

    public override void _Ready()
    {
        string? go = null;
        if (!devDone && Main.I.Dev)
        {
            devDone = true;
            if (AgentRoster.ByKey(CmdLine.After("--agent")) is { } a) selKey = a.Key;
            if (CmdLine.After("--agent-search") is { } s) search = s;
            if (Enum.TryParse<AgentRole>(CmdLine.After("--agent-role") ?? "", true, out var r)) roleFilter = r;
            go = CmdLine.After("--agent-go");
        }
        base._Ready();
        if (Main.I.Dev && CmdLine.Has("--map-picker"))
            GetTree().CreateTimer(0.6).Timeout += () => { if (IsInstanceValid(this) && IsInsideTree()) mapSel?.Open(); };
        if (go != null)
            GetTree().CreateTimer(1.5).Timeout += () =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree() || AgentRoster.ByKey(selKey) is not { } a) return;
                if (go.Equals("routine", StringComparison.OrdinalIgnoreCase)) AgentRoutines.Start(a);
                else StartDrill(go);
            };
    }

    protected override void Back()
    {
        if (mapSel?.IsOpen == true) { mapSel.Close(); return; }
        if (searchBox != null && searchBox.HasFocus()) { searchBox.ReleaseFocus(); return; }
        if (selKey != null) { Select(null); return; }
        Main.I.ShowMenu();
    }

    protected override void Build()
    {
        tiles.Clear(); groups.Clear(); chips.Clear();
        page = null; pageScroll = null; searchBox = null; mapSel = null; noMatch = null;
        float k = K;
        var vp = GetViewportRect().Size;
        float contentW = vp.X / k - 96;
        int rosterCols = contentW >= 1700 ? 8 : 6;
        float rosterW = rosterCols * TileW + (rosterCols - 1) * TileGap;
        narrow = contentW - rosterW - 30 < 960;

        var root = Margins(VBox(16 * k), 48 * k, 26 * k, 48 * k, 34 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        // ---- header: title, tier in effect, map, back ----
        var head = HBox(16 * k);
        var title = VBox(2 * k);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.AddChild(Lbl("AGENTS", UiTheme.Display, 64, UiTheme.Text));
        var sub = Lbl("SIGNATURE DRILLS WITH EACH AGENT'S OWN UTILITY · ROLE DRILLS · A 10-MINUTE AGENT WARM-UP", UiTheme.HudWide, 15, UiTheme.Dim);
        sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        sub.ClipText = true;
        title.AddChild(sub);
        head.AddChild(title);
        var tier = new DrawBox { CustomMinimumSize = new Vector2(190 * k, 52 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter, OnDraw = DrawTier };
        tier.MouseFilter = MouseFilterEnum.Pass;
        tier.TooltipText = "Drills run at the tier picked on the menu";
        head.AddChild(tier);
        mapSel = new MapSelector(this, k) { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        head.AddChild(mapSel);
        head.AddChild(Spacer(4 * k, 0));
        var back = Btn("BACK", VButton.Look.Primary, Main.I.ShowMenu, 160, 52, 22);
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(back);
        col.AddChild(head);

        var body = HBox(30 * k);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(body);

        // ---- left: search + role chips, roster, how it works ----
        var left = VBox(12 * k);
        left.CustomMinimumSize = new Vector2(rosterW * k, 0);
        body.AddChild(left);
        BuildRoster(left, rosterCols);

        // ---- right: the agent page (or the role overview) ----
        pageScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddChild(pageScroll);
        page = VBox(14 * k);
        page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.SizeFlagsVertical = SizeFlags.ExpandFill;
        pageScroll.AddChild(page);

        FillPage();
        ApplyFilter();
    }

    void DrawTier(DrawBox d)
    {
        float k = K, cy = d.Size.Y / 2;
        var t = Difficulty.Get(Main.I.Tier);
        RankEmblem.Draw(d, new Vector2(16 * k, cy), 14 * k, t.Id);
        int fs = UiTheme.Fs(17, k), ss = UiTheme.Fs(12, k);
        Gfx.TextFit(d, UiTheme.HudWide, t.Name.ToUpperInvariant() + " TIER", 40 * k, Gfx.Mid(cy - 9 * k, fs), fs, UiTheme.TierColor(t.Id), d.Size.X - 40 * k);
        Gfx.TextFit(d, UiTheme.Body, "set on the menu", 40 * k, Gfx.Mid(cy + 11 * k, ss), ss, UiTheme.Faint, d.Size.X - 40 * k);
    }

    // ---------------- roster ----------------

    void BuildRoster(VBoxContainer left, int cols)
    {
        float k = K;
        var filter = HBox(6 * k);
        searchBox = new LineEdit
        {
            Text = search, PlaceholderText = "Search agents, abilities or utility", ClearButtonEnabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 42 * k),
        };
        StyleSearch(searchBox, k);
        searchBox.TextChanged += t => { search = t; ApplyFilter(); };
        searchBox.TextSubmitted += _ =>
        {
            var shown = tiles.Where(t => t.Visible).ToList();
            if (shown.Count == 1) Select(shown[0].Agent.Key);
        };
        filter.AddChild(searchBox);
        foreach (AgentRole? r in new AgentRole?[] { null }.Concat(AgentRoles.All.Select(x => (AgentRole?)x)))
        {
            var chip = new RoleChip(r) { K = k, CustomMinimumSize = new Vector2((r == null ? 56 : 42) * k, 42 * k) };
            AgentRole? rr = r;
            chip.Pressed += () => SetRole(rr == roleFilter ? null : rr);
            chips.Add(chip);
            filter.AddChild(chip);
        }
        left.AddChild(filter);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(scroll);
        var roster = VBox(8 * k);
        roster.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(roster);
        foreach (var role in AgentRoles.All)
        {
            var agents = AgentRoster.OfRole(role).ToList();
            var rh = new RoleHeader { Role = role, Count = agents.Count, K = k, CustomMinimumSize = new Vector2(0, 26 * k) };
            var grid = new GridContainer { Columns = cols };
            grid.AddThemeConstantOverride("h_separation", (int)(TileGap * k));
            grid.AddThemeConstantOverride("v_separation", (int)(TileGap * k));
            foreach (var a in agents)
            {
                var t = new AgentTile(a)
                {
                    K = k, Selected = a.Key == selKey, SignatureCount = AgentDrills.Signature(a).Count,
                    CustomMinimumSize = new Vector2(TileW * k, TileH * k),
                };
                string key = a.Key;
                t.Pressed += () => Select(key);
                tiles.Add(t);
                grid.AddChild(t);
            }
            if (groups.Count > 0) roster.AddChild(Spacer(0, 4 * k));
            roster.AddChild(rh);
            roster.AddChild(grid);
            groups.Add((role, rh, grid));
        }
        var none = new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 60 * k), Visible = false,
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(16, k);
                Gfx.TextFit(d, UiTheme.Body, $"No agent or ability matches \"{search.Trim()}\"{(roleFilter is { } rf ? $" among the {rf.ToString().ToLowerInvariant()}s" : "")}.",
                    0, Gfx.Mid(20 * k, fs), fs, UiTheme.Dim, d.Size.X);
                Gfx.TextFit(d, UiTheme.Body, "Try a name (Sova), an ability (Updraft) or what it does (smoke, flash, wall).", 0, Gfx.Mid(46 * k, UiTheme.Fs(14, k)),
                    UiTheme.Fs(14, k), UiTheme.Faint, d.Size.X);
            },
        };
        roster.AddChild(none);
        noMatch = none;

        if (narrow) return; // 4:3 / 5:4: the roster needs the room
        var how = new VPanel { Title = "HOW IT WORKS", K = k, Caption = "everything runs at your tier and map" };
        left.AddChild(how);
        how.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 128 * k), OnDraw = DrawHow });
    }

    static readonly (string Head, string Text)[] How =
    {
        ("SIGNATURE", "drills played with the agent's own utility: flash and swing, recon and clear, smoke and execute, dash in, anchor a site."),
        ("ROLE", "drills for the gunfights the role takes most, from opening duels to post-plant holds."),
        ("WARM-UP", "aim, role and signature drills and a deathmatch in about 10 minutes, with the sens shifter if you use it."),
    };

    void DrawHow(DrawBox d)
    {
        float k = K, y = 4 * k;
        int hs = UiTheme.Fs(12, k), ts = UiTheme.Fs(14, k);
        float hw = 0;
        foreach (var (h, _) in How) hw = Mathf.Max(hw, Gfx.TextW(UiTheme.HudWide, h, hs));
        foreach (var (h, t) in How)
        {
            Gfx.Diamond(d, new Vector2(4 * k, y + 10 * k), 3.5f * k, 4.5f * k, UiTheme.Accent);
            Gfx.Text(d, UiTheme.HudWide, h, 16 * k, Gfx.Mid(y + 10 * k, hs), hs, UiTheme.Text);
            float x = 16 * k + hw + 12 * k;
            d.DrawMultilineString(UiTheme.Body, new Vector2(x, y + 10 * k + ts * 0.36f), t, HorizontalAlignment.Left, d.Size.X - x, ts, 2, UiTheme.Dim);
            float lines = UiTheme.Body.GetMultilineStringSize(t, HorizontalAlignment.Left, d.Size.X - x, ts, 2).Y;
            y += Mathf.Max(24 * k, lines) + 12 * k;
        }
    }

    static void StyleSearch(LineEdit e, float k)
    {
        StyleBoxFlat Box(Color border, bool fill) => new()
        {
            BgColor = new Color(0.06f, 0.1f, 0.14f, 0.85f), DrawCenter = fill, BorderColor = border,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 36 * k, ContentMarginRight = 8 * k, ContentMarginTop = 4 * k, ContentMarginBottom = 4 * k,
        };
        e.AddThemeStyleboxOverride("normal", Box(new Color(UiTheme.Text, 0.2f), true));
        e.AddThemeStyleboxOverride("focus", Box(UiTheme.Accent, false));
        e.AddThemeStyleboxOverride("read_only", Box(new Color(UiTheme.Text, 0.1f), true));
        e.AddThemeFontOverride("font", UiTheme.Body);
        e.AddThemeFontSizeOverride("font_size", UiTheme.Fs(16, k));
        e.AddThemeColorOverride("font_color", UiTheme.Text);
        e.AddThemeColorOverride("font_placeholder_color", UiTheme.Faint);
        e.AddThemeColorOverride("caret_color", UiTheme.Accent);
        e.AddThemeColorOverride("selection_color", new Color(UiTheme.Accent, 0.35f));
        e.AddThemeColorOverride("clear_button_color", UiTheme.Dim);
        e.AddThemeColorOverride("clear_button_color_pressed", UiTheme.Text);
        var icon = new DrawBox
        {
            AnchorTop = 0, AnchorBottom = 1, OffsetLeft = 0, OffsetRight = 34 * k,
            OnDraw = d =>
            {
                var c = new Vector2(17 * k, d.Size.Y / 2 - 1.5f * k);
                float r = 6 * k, w = Mathf.Max(1.5f, 2 * k);
                d.DrawArc(c, r, 0, Mathf.Tau, 24, UiTheme.Dim, w, true);
                d.DrawLine(c + new Vector2(r * 0.7f, r * 0.7f), c + new Vector2(r * 1.6f, r * 1.6f), UiTheme.Dim, w, true);
            },
        };
        e.AddChild(icon);
    }

    static bool Matches(AgentInfo a, string q)
    {
        if (q.Length == 0) return true;
        bool Has(string s) => s.Contains(q, StringComparison.OrdinalIgnoreCase);
        if (Has(a.Name) || Has(a.Role.ToString()) || a.Abilities.Any(ab => Has(ab.Name))) return true;
        if (q.Length < 3) return false;
        // what they do ("smoke", "flash", "wall", "teleport") and their signature drills ("smoke execute")
        return a.Abilities.Any(ab => Has(ab.Summary)) || AgentDrills.Signature(a).Any(k => Has(AgentDrills.Name(k)));
    }

    void ApplyFilter()
    {
        string q = search.Trim();
        int shown = 0;
        foreach (var t in tiles)
        {
            bool ok = (roleFilter == null || t.Agent.Role == roleFilter) && Matches(t.Agent, q);
            t.Visible = ok;
            if (ok) shown++;
        }
        foreach (var (role, head, grid) in groups)
        {
            int n = tiles.Count(t => t.Visible && t.Agent.Role == role);
            head.Visible = n > 0;
            grid.Visible = n > 0;
            if (head is RoleHeader rh) { rh.Shown = n; rh.QueueRedraw(); }
        }
        foreach (var c in chips) c.SetSelected(c.Role == roleFilter);
        if (noMatch != null) { noMatch.Visible = shown == 0; noMatch.QueueRedraw(); }
    }

    void SetRole(AgentRole? r)
    {
        roleFilter = r;
        ApplyFilter();
    }

    void Select(string? key)
    {
        selKey = key;
        foreach (var t in tiles) t.SetSelected(t.Agent.Key == key);
        FillPage();
    }

    static void StartDrill(string key)
    {
        if (ModeRegistry.Find(key) is not { } make) { Log.Error($"[agents] unknown drill '{key}'"); return; }
        Main.I.ReturnToAgents = true; // results → MENU (or pause → back to menu) comes back here
        Main.I.StartMode(make);
    }

    // ---------------- right: page ----------------

    void FillPage()
    {
        if (page == null) return;
        foreach (var c in page.GetChildren()) { page.RemoveChild(c); c.QueueFree(); }
        if (pageScroll != null) pageScroll.ScrollVertical = 0;
        if (AgentRoster.ByKey(selKey) is { } a) BuildAgentPage(page, a);
        else BuildOverview(page);
    }

    void BuildOverview(VBoxContainer p)
    {
        float k = K;
        p.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 118 * k),
            OnDraw = d =>
            {
                int ts = UiTheme.Fs(56, k), ss = UiTheme.Fs(17, k);
                Gfx.Text(d, UiTheme.Display, "CHOOSE AN AGENT", 0, 54 * k, ts, UiTheme.Text);
                d.DrawMultilineString(UiTheme.Body, new Vector2(0, 76 * k + UiTheme.Body.GetAscent(ss)),
                    "Every agent has a page: what each ability does and which drill trains it, signature drills played with that agent's utility, " +
                    "the drills for the role, and a 10-minute warm-up. Pick a role below to filter the list.",
                    HorizontalAlignment.Left, d.Size.X, ss, 2, UiTheme.Dim);
            },
        });
        var grid = new GridContainer { Columns = narrow ? 1 : 2, SizeFlagsVertical = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", (int)(16 * k));
        grid.AddThemeConstantOverride("v_separation", (int)(16 * k));
        p.AddChild(grid);
        foreach (var r in AgentRoles.All)
        {
            var agents = AgentRoster.OfRole(r).ToList();
            int withSig = agents.Count(a => AgentDrills.Signature(a).Count > 0);
            string names = string.Join(" · ", agents.Select(a => a.Name));
            var sigDrills = agents.SelectMany(AgentDrills.Signature).GroupBy(AgentDrills.Base).Select(g => AgentDrills.Name(g.First())).Distinct().ToList();
            string drills = string.Join(" · ", AgentRoles.Drills(r).Where(AgentDrills.Exists).Select(AgentDrills.Name));
            var sections = new List<(string, string)> { ("AGENTS", names) };
            if (sigDrills.Count > 0) sections.Add(("SIGNATURE DRILLS", string.Join(" · ", sigDrills)));
            sections.Add(("ROLE DRILLS", drills));
            var card = new RoleCard(r, sections.ToArray(), agents.Count, withSig)
            {
                K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 280 * k),
            };
            AgentRole rr = r;
            card.Pressed += () => SetRole(rr);
            grid.AddChild(card);
        }
    }

    void BuildAgentPage(VBoxContainer p, AgentInfo a)
    {
        float k = K;
        var sig = AgentDrills.Signature(a);
        var role = AgentDrills.Role(a).Take(6).ToList();
        var col = AgentArt.RoleColor(a.Role);

        p.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 128 * k), OnDraw = d => DrawBanner(d, a, sig) });

        // abilities: one card each, VALORANT-style row
        p.AddChild(new SectionLabel { Text = "ABILITIES", Note = "what each one does · the drills that train it", K = k });
        var strip = new GridContainer { Columns = narrow ? Math.Min(3, a.Abilities.Length) : a.Abilities.Length };
        strip.AddThemeConstantOverride("h_separation", (int)(10 * k));
        strip.AddThemeConstantOverride("v_separation", (int)(10 * k));
        foreach (var ab in a.Abilities)
            strip.AddChild(new AbilityCard(a, ab, AgentDrills.TrainedBy(a, ab), sig)
            {
                K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 172 * k),
            });
        p.AddChild(strip);

        // drills + warm-up
        BoxContainer row = narrow ? VBox(14 * k) : HBox(16 * k);
        row.SizeFlagsVertical = SizeFlags.ExpandFill;
        p.AddChild(row);

        var drills = new VPanel
        {
            Title = "DRILLS", K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.55f,
            Caption = sig.Count > 0 ? $"{sig.Count} signature · {role.Count} {a.Role.ToString().ToLowerInvariant()}" : $"{role.Count} {a.Role.ToString().ToLowerInvariant()} drills",
        };
        row.AddChild(drills);
        var dv = VBox(8 * k);
        drills.AddChild(dv);
        dv.AddChild(new SectionLabel { Text = "SIGNATURE", Note = sig.Count > 0 ? $"with {a.Name}'s own utility" : "none yet", K = k });
        if (sig.Count > 0) dv.AddChild(CardGrid(sig, a, true));
        else
            dv.AddChild(new DrawBox
            {
                CustomMinimumSize = new Vector2(0, 68 * k),
                OnDraw = d =>
                {
                    Gfx.Plate(d, new Rect2(Vector2.Zero, d.Size), 12 * k, new Color(0.07f, 0.11f, 0.15f, 0.6f), new Color(UiTheme.Text, 0.08f));
                    int fs = UiTheme.Fs(15, k), ss = UiTheme.Fs(13.5f, k);
                    Gfx.TextFit(d, UiTheme.Body, $"No signature drill for {a.Name} yet.", 16 * k, Gfx.Mid(24 * k, fs), fs, UiTheme.Text, d.Size.X - 32 * k);
                    Gfx.TextFit(d, UiTheme.Body, $"The {a.Role.ToString().ToLowerInvariant()} drills below train the fights that {a.Name}'s utility sets up.",
                        16 * k, Gfx.Mid(48 * k, ss), ss, UiTheme.Dim, d.Size.X - 32 * k);
                },
            });
        dv.AddChild(new SectionLabel { Text = $"ROLE · {a.Role.ToString().ToUpperInvariant()}", Note = "general drills for the role", K = k });
        dv.AddChild(CardGrid(role, a, false));

        var wu = new VPanel { Title = "AGENT WARM-UP", K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };
        row.AddChild(wu);
        BuildWarmup(wu, a, sig, col);
    }

    GridContainer CardGrid(List<string> keys, AgentInfo a, bool signature)
    {
        float k = K;
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", (int)(10 * k));
        grid.AddThemeConstantOverride("v_separation", (int)(8 * k));
        foreach (var key in keys)
        {
            if (AgentDrills.Proto(key) is not { } info) continue;
            var card = new ModeCard(info) { K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 68 * k) };
            if (signature)
            {
                // gun variants are titled by the gun ("TOUR DE FORCE", "Chamber Guns · uses …"); the rest by the drill
                bool variant = AgentDrills.Arg(key).Length > 0 && AgentDrills.Arg(key) != a.Key;
                string uses = AgentDrills.UsesLine(a, key) ?? $"With {a.Name}'s utility";
                card.Title = variant ? AgentRoster.ArgName(AgentDrills.Arg(key)) : info.Name;
                card.Sub = variant ? $"{info.Name} · {uses}" : uses;
                card.Tint = AgentArt.RoleColor(a.Role);
                card.TooltipText = $"{AgentDrills.Title(key)} as {a.Name} — {info.Description}\n{uses}";
            }
            string kk = key;
            card.Pressed += () => StartDrill(kk);
            grid.AddChild(card);
        }
        return grid;
    }

    void BuildWarmup(VPanel wu, AgentInfo a, List<string> sig, Color col)
    {
        float k = K;
        var plan = AgentRoutines.Plan(a);
        var store = WarmupStore.I;
        wu.Caption = $"≈{WarmupPlan.Minutes(plan.EstimatedSeconds).ToLowerInvariant()} · {plan.Steps.Count} drills";
        var v = VBox(10 * k);
        wu.AddChild(v);
        v.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 40 * k),
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(14, k);
                string text = plan.Preset.Blurb + ". Every drill runs at its normal length except the deathmatch.";
                d.DrawMultilineString(UiTheme.Body, new Vector2(0, UiTheme.Body.GetAscent(fs)), text, HorizontalAlignment.Left, d.Size.X, fs, 2, UiTheme.Dim);
            },
        });
        v.AddChild(new RoutineList(plan, sig, col) { K = k, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, plan.Steps.Count * 26 * k) });

        var cap = new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 20 * k),
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(12, k), ns = UiTheme.Fs(12.5f, k);
                Gfx.Text(d, UiTheme.HudWide, "SENS SHIFTER", 0, Gfx.Mid(d.Size.Y / 2, fs), fs, UiTheme.Dim);
                string note = plan.ShiftedCount > 0 ? $"first {plan.ShiftedCount} drills {plan.ShiftLabel}" : "every drill at your sens";
                Gfx.TextR(d, UiTheme.Body, note, d.Size.X, Gfx.Mid(d.Size.Y / 2, ns), ns, plan.ShiftedCount > 0 ? WarmupCharts.ShiftCol : UiTheme.Faint);
            },
        };
        v.AddChild(cap);
        var shift = HBox(6 * k);
        void ShiftChip(string label, ShiftMode m, string tip)
        {
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = label, FontPx = 16, Selected = store.ShiftMode == m, TooltipText = tip,
                CustomMinimumSize = new Vector2(0, 36 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            b.Pressed += () => { store.ShiftMode = m; store.Save(); Callable.From(FillPage).CallDeferred(); };
            shift.AddChild(b);
        }
        ShiftChip("OFF", ShiftMode.Off, "Every drill at your sens");
        ShiftChip("HIGHER", ShiftMode.HighToNormal, "Start above your sens and step back to it (your sens feels slower after)");
        ShiftChip("LOWER", ShiftMode.LowToNormal, "Start below your sens and step back to it (big arm motions first)");
        string sign = store.ShiftMode == ShiftMode.LowToNormal ? "−" : "+";
        var pct = new VButton
        {
            Kind = VButton.Look.Select, K = k, Label = $"{sign}{store.ShiftPct}%", FontPx = 16, Disabled = store.ShiftMode == ShiftMode.Off,
            CustomMinimumSize = new Vector2(70 * k, 36 * k), TooltipText = "Start offset: click to cycle " + string.Join(" / ", WarmupPresets.ShiftOptions.Select(o => o + "%")),
        };
        pct.Pressed += () =>
        {
            var opts = WarmupPresets.ShiftOptions;
            int i = Array.IndexOf(opts, store.ShiftPct);
            store.ShiftPct = opts[(i + 1 + opts.Length) % opts.Length];
            store.Save();
            Callable.From(FillPage).CallDeferred();
        };
        shift.AddChild(pct);
        v.AddChild(shift);

        var start = Btn($"START {a.Name.ToUpperInvariant()} WARM-UP", VButton.Look.Primary, () => AgentRoutines.Start(a), 0, 56, 21);
        start.TooltipText = $"{plan.Preset.Blurb} · same warm-up system as WARM UP: step cards, lock-in graph and summary";
        v.AddChild(start);
    }

    void DrawBanner(DrawBox d, AgentInfo a, List<string> sig)
    {
        float k = K, h = d.Size.Y, w = d.Size.X;
        var col = AgentArt.RoleColor(a.Role);
        Gfx.HGradient(d, new Rect2(0, 0, w, h), new Color(col, 0.13f), new Color(col, 0f));
        d.DrawRect(new Rect2(0, h - 1, w, 1), new Color(col, 0.35f));
        AgentArt.RoleGlyph(d, new Vector2(w - h * 0.7f, h / 2), h * 0.6f, a.Role, new Color(col, 0.06f));
        float bs = h - 16 * k;
        AgentArt.Badge(d, new Rect2(8 * k, 8 * k, bs, bs), a, k, 1f);
        float x = bs + 34 * k;
        int ns = UiTheme.Fs(64, k), rs = UiTheme.Fs(14, k), ss = UiTheme.Fs(17, k);
        Gfx.TextFit(d, UiTheme.Display, a.Name.ToUpperInvariant(), x, 60 * k, ns, UiTheme.Text, w - x);
        AgentArt.RoleGlyph(d, new Vector2(x + 8 * k, 84 * k), 8 * k, a.Role, col);
        string sigText = sig.Count switch { 0 => "NO SIGNATURE DRILL YET", 1 => "1 SIGNATURE DRILL", _ => $"{sig.Count} SIGNATURE DRILLS" };
        Gfx.TextFit(d, UiTheme.HudWide, $"{a.Role.ToString().ToUpperInvariant()}  ·  {sigText}", x + 24 * k, Gfx.Mid(84 * k, rs), rs, col, w - x - 24 * k);
        Gfx.TextFit(d, UiTheme.Body, a.Summary, x, Gfx.Mid(112 * k, ss), ss, UiTheme.Text, w - x - 8 * k);
    }
}
