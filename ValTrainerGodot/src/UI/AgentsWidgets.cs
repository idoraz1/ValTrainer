using System.Globalization;
using Godot;
using ValTrainer.Agents;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>
/// Procedural agent visuals: a glyph per role (duelist arrowhead, initiator flash star, controller orb with an orbit,
/// sentinel shield with a keyhole) and a monogram badge per agent. No Riot portraits, role icons or ability art.
/// </summary>
public static class AgentArt
{
    public static Color RoleColor(AgentRole r) => r switch
    {
        AgentRole.Duelist => Color.Color8(255, 122, 92),
        AgentRole.Initiator => Color.Color8(96, 214, 150),
        AgentRole.Controller => Color.Color8(172, 136, 255),
        _ => Color.Color8(92, 176, 255),
    };

    static readonly Vector2[] Arrow = { new(0, -1f), new(0.74f, 0.66f), new(0, 0.3f), new(-0.74f, 0.66f) };
    static readonly Vector2[] Shield = { new(0, -1f), new(0.8f, -0.64f), new(0.72f, 0.24f), new(0, 1f), new(-0.72f, 0.24f), new(-0.8f, -0.64f) };
    static readonly Color Dark = new(0.06f, 0.09f, 0.12f);

    static void Fill(CanvasItem ci, Vector2 c, float r, ReadOnlySpan<Vector2> unit, Color col)
    {
        Span<Vector2> p = stackalloc Vector2[unit.Length];
        for (int i = 0; i < unit.Length; i++) p[i] = c + unit[i] * r;
        ci.FillPoly(p, col);
    }

    static void Star(CanvasItem ci, Vector2 c, float outer, float inner, int points, Color col, float rot = 0f)
    {
        Span<Vector2> p = stackalloc Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float a = -Mathf.Pi / 2 + rot + i * Mathf.Pi / points;
            p[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? outer : inner);
        }
        ci.FillPoly(p, col);
    }

    /// <summary>The role glyph centred on <paramref name="c"/>, radius <paramref name="r"/>.</summary>
    public static void RoleGlyph(CanvasItem ci, Vector2 c, float r, AgentRole role, Color col)
    {
        var hole = new Color(Dark, col.A);
        switch (role)
        {
            case AgentRole.Duelist:
                Fill(ci, c, r, Arrow, col);
                break;
            case AgentRole.Initiator:
                Star(ci, c, r, r * 0.3f, 4, col);
                Star(ci, c, r * 0.5f, r * 0.2f, 4, new Color(col, col.A * 0.75f), Mathf.Pi / 4);
                break;
            case AgentRole.Controller:
            {
                ci.DrawCircle(c, r * 0.5f, col);
                Span<Vector2> ring = stackalloc Vector2[33];
                float rot = -0.42f, cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                for (int i = 0; i <= 32; i++)
                {
                    float a = i * Mathf.Tau / 32;
                    var e = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.36f);
                    ring[i] = c + new Vector2(e.X * cs - e.Y * sn, e.X * sn + e.Y * cs);
                }
                ci.Polyline(ring, col, Mathf.Max(1f, r * 0.13f), true);
                break;
            }
            default:
                Fill(ci, c, r, Shield, col);
                ci.DrawCircle(c + new Vector2(0, -r * 0.16f), r * 0.17f, hole);
                ci.DrawRect(new Rect2(c.X - r * 0.07f, c.Y - r * 0.12f, r * 0.14f, r * 0.42f), hole);
                break;
        }
    }

    /// <summary>Agent badge: angular plate tinted with the role colour, a faint role glyph and the agent's monogram.</summary>
    public static void Badge(CanvasItem ci, Rect2 r, AgentInfo a, float k, float glow = 0f, float alpha = 1f)
    {
        var col = RoleColor(a.Role);
        float s = Mathf.Min(r.Size.X, r.Size.Y);
        Gfx.Plate(ci, r, s * 0.16f, new Color(0.08f, 0.12f, 0.16f, 0.95f * alpha), new Color(col, (0.45f + 0.4f * glow) * alpha), Mathf.Max(1f, 1.5f * k));
        Gfx.VGradient(ci, new Rect2(r.Position.X + 1, r.Position.Y + r.Size.Y * 0.45f, r.Size.X - 2, r.Size.Y * 0.55f - 1),
            new Color(col, 0f), new Color(col, (0.2f + 0.12f * glow) * alpha));
        RoleGlyph(ci, r.GetCenter(), s * 0.4f, a.Role, new Color(col, (0.16f + 0.08f * glow) * alpha));
        int fs = Math.Max(10, (int)(s * 0.4f));
        Gfx.TextC(ci, UiTheme.Display, a.Mono, r.GetCenter().X, Gfx.Mid(r.GetCenter().Y, fs), fs, new Color(UiTheme.Text, alpha));
    }

    /// <summary>Keyboard key cap for an ability slot (C / Q / E / X); passives get a small diamond.</summary>
    public static void KeyCap(CanvasItem ci, Rect2 r, string slot, Color col, float k)
    {
        ci.DrawRect(r, new Color(col, 0.12f));
        ci.DrawRect(r, new Color(col, 0.8f), false, Mathf.Max(1f, 1.5f * k));
        if (slot.Length == 0) { Gfx.Diamond(ci, r.GetCenter(), r.Size.X * 0.2f, r.Size.Y * 0.26f, col); return; }
        int fs = Math.Max(10, (int)(r.Size.Y * 0.56f));
        Gfx.TextC(ci, UiTheme.Display, slot, r.GetCenter().X, Gfx.Mid(r.GetCenter().Y, fs), fs, UiTheme.Text);
    }
}

/// <summary>One agent in the roster: badge, name and a diamond per signature drill.</summary>
public partial class AgentTile : BaseButton
{
    public readonly AgentInfo Agent;
    public float K = 1f;
    public bool Selected;
    public int SignatureCount;
    float hover;

    public AgentTile(AgentInfo a)
    {
        Agent = a;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = $"{a.Name} · {a.Role}\n{a.Summary}";
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
    }

    public void SetSelected(bool on)
    {
        if (Selected == on) return;
        Selected = on;
        QueueRedraw();
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
        var col = AgentArt.RoleColor(Agent.Role);
        float nameH = 24 * k;
        var art = new Rect2(0, 0, r.Size.X, r.Size.Y - nameH);
        AgentArt.Badge(this, art, Agent, k, Selected ? 1f : hover);
        if (Selected)
        {
            DrawRect(new Rect2(0, r.Size.Y - nameH, r.Size.X, nameH), UiTheme.Text);
            DrawRect(new Rect2(0, r.Size.Y - 3 * k, r.Size.X, 3 * k), UiTheme.Accent);
        }
        int ns = UiTheme.Fs(11.5f, k);
        string name = Agent.Name.ToUpperInvariant();
        // long names (BRIMSTONE) drop the letter spacing instead of being cut
        var font = Gfx.TextW(UiTheme.HudWide, name, ns) <= r.Size.X - 6 ? UiTheme.HudWide : UiTheme.Hud;
        Gfx.TextFit(this, font, name, 2, Gfx.Mid(r.Size.Y - nameH / 2 - (Selected ? 1 * k : 0), ns), ns,
            Selected ? UiTheme.Bg : UiTheme.Text.Lerp(Colors.White, hover * 0.3f), r.Size.X - 4, HorizontalAlignment.Center);
        for (int i = 0; i < Math.Min(3, SignatureCount); i++)
            Gfx.Diamond(this, new Vector2(r.Size.X - 11 * k - i * 10 * k, 11 * k), 3.5f * k, 4.5f * k, col);
        if (hover > 0.01f && !Selected) Gfx.Brackets(this, r.Grow(2 * k), 7 * k * hover, new Color(UiTheme.Text, 0.85f * hover), Mathf.Max(1, 2 * k));
    }
}

/// <summary>Roster group caption: role glyph, "DUELISTS", a hairline and "8 agents".</summary>
public partial class RoleHeader : Control
{
    public AgentRole Role;
    public int Count;
    /// <summary>Agents left after the search / filter (-1 = all of them).</summary>
    public int Shown = -1;
    public float K = 1f;

    public RoleHeader() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2;
        var col = AgentArt.RoleColor(Role);
        AgentArt.RoleGlyph(this, new Vector2(8 * k, cy), 7.5f * k, Role, col);
        int fs = UiTheme.Fs(14, k), ns = UiTheme.Fs(13, k);
        string t = Role.ToString().ToUpperInvariant() + "S";
        float w = Gfx.TextW(UiTheme.HudWide, t, fs);
        Gfx.Text(this, UiTheme.HudWide, t, 24 * k, Gfx.Mid(cy, fs), fs, UiTheme.Dim);
        string note = Shown >= 0 && Shown < Count ? $"{Shown} of {Count} agents" : Count == 1 ? "1 agent" : $"{Count} agents";
        float nw = Gfx.TextR(this, UiTheme.Body, note, Size.X, Gfx.Mid(cy, ns), ns, UiTheme.Faint);
        float x0 = 24 * k + w + 14 * k, x1 = Size.X - nw - 14 * k;
        if (x1 > x0) DrawRect(new Rect2(x0, cy, x1 - x0, 1), new Color(UiTheme.Text, 0.08f));
    }
}

/// <summary>Square roster filter chip: ALL or a role glyph (VALORANT's tab look: light fill and red underline when on).</summary>
public partial class RoleChip : BaseButton
{
    public readonly AgentRole? Role;
    public float K = 1f;
    public bool Selected;
    float hover;

    public RoleChip(AgentRole? role)
    {
        Role = role;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = role == null ? "Every agent" : $"Only {role.Value.ToString().ToLowerInvariant()}s";
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
    }

    public void SetSelected(bool on)
    {
        if (Selected == on) return;
        Selected = on;
        QueueRedraw();
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
        if (Selected)
        {
            DrawRect(r, UiTheme.Text);
            DrawRect(new Rect2(0, r.Size.Y - 3 * k, r.Size.X, 3 * k), UiTheme.Accent);
        }
        else
        {
            DrawRect(r, new Color(0.06f, 0.1f, 0.14f, 0.72f).Lerp(new Color(0.93f, 0.91f, 0.88f, 0.14f), hover));
            DrawRect(r, new Color(UiTheme.Text, 0.14f + 0.4f * hover), false, 1f);
        }
        var c = r.GetCenter() - new Vector2(0, Selected ? 1.5f * k : 0);
        if (Role is { } role)
            AgentArt.RoleGlyph(this, c, r.Size.Y * 0.3f, role, Selected ? AgentArt.RoleColor(role).Darkened(0.35f) : AgentArt.RoleColor(role));
        else
        {
            int fs = UiTheme.Fs(17, k);
            Gfx.TextC(this, UiTheme.Display, "ALL", c.X, Gfx.Mid(c.Y, fs), fs, Selected ? UiTheme.Bg : UiTheme.Text);
        }
    }
}

/// <summary>An ability on an agent page: key, name, our one-line summary and the drills that practise it.</summary>
public partial class AbilityCard : Control
{
    readonly AgentInfo agent;
    readonly AgentAbility ability;
    readonly List<(string Name, bool Signature)> drills;
    public float K = 1f;

    public AbilityCard(AgentInfo a, AgentAbility ab, List<string> trainedBy, IReadOnlyCollection<string> signature)
    {
        agent = a;
        ability = ab;
        drills = trainedBy.Select(k => (AgentDrills.Title(k), signature.Contains(k))).ToList();
        MouseFilter = MouseFilterEnum.Pass;
        string slot = ab.Slot.Length > 0 ? $"[{ab.Slot}] " : "[passive] ";
        TooltipText = $"{slot}{ab.Name}\n{ab.Summary}\n" +
                      (drills.Count > 0 ? "Trained by: " + string.Join(", ", drills.Select(d => d.Name)) : "No drill trains this one yet: practise it in VALORANT.");
    }

    public override void _Draw()
    {
        float k = K, pad = 12 * k;
        var r = new Rect2(Vector2.Zero, Size);
        var col = AgentArt.RoleColor(agent.Role);
        bool trained = drills.Count > 0;
        Gfx.Plate(this, r, 10 * k, new Color(0.07f, 0.11f, 0.15f, 0.88f), new Color(UiTheme.Text, 0.09f));
        DrawRect(new Rect2(0, 0, r.Size.X - 10 * k, 2 * k), new Color(trained ? col : UiTheme.Faint, trained ? 0.8f : 0.4f));

        float cap = 30 * k;
        AgentArt.KeyCap(this, new Rect2(pad, pad + 2 * k, cap, cap), ability.Slot, trained ? col : UiTheme.Dim, k);
        // name: shrink to fit, else two lines ("ASTRAL FORM / COSMIC DIVIDE")
        float nx = pad + cap + 10 * k, nw = r.Size.X - nx - pad, top = pad + 2 * k;
        string name = ability.Name.ToUpperInvariant();
        int ns = UiTheme.Fs(19, k), minNs = UiTheme.Fs(15, k);
        while (ns > minNs && Gfx.TextW(UiTheme.Display, name, ns) > nw) ns--;
        float below = pad + cap; // where the summary area starts
        if (Gfx.TextW(UiTheme.Display, name, ns) <= nw)
            Gfx.Text(this, UiTheme.Display, name, nx, Gfx.Mid(top + cap / 2, ns), ns, UiTheme.Text);
        else
        {
            DrawMultilineString(UiTheme.Display, new Vector2(nx, top + UiTheme.Display.GetAscent(ns) - 2 * k), name, HorizontalAlignment.Left, nw, ns, 2, UiTheme.Text);
            below = Mathf.Max(below, top + UiTheme.Display.GetMultilineStringSize(name, HorizontalAlignment.Left, nw, ns, 2).Y);
        }
        if (ability.Slot.Length == 0)
        {
            int ps = UiTheme.Fs(10, k);
            Gfx.Text(this, UiTheme.HudWide, "PASSIVE", nx, below + 12 * k, ps, UiTheme.Faint);
            below += 10 * k;
        }

        // trained by (bottom), then as many summary lines as fit above it
        int ls = UiTheme.Fs(10, k), ts = UiTheme.Fs(13.5f, k), ds = UiTheme.Fs(13.5f, k);
        float lineH = ts * 1.32f;
        float by = r.Size.Y - pad - Math.Max(1, Math.Min(2, drills.Count)) * lineH;
        float sy = below + 16 * k;
        int lines = Math.Clamp((int)((by - 6 * k - ls - 6 * k - sy) / (UiTheme.Body.GetHeight(ds))), 1, 3);
        DrawMultilineString(UiTheme.Body, new Vector2(pad, sy + UiTheme.Body.GetAscent(ds)), ability.Summary, HorizontalAlignment.Left, r.Size.X - pad * 2, ds, lines, UiTheme.Dim);
        Gfx.Text(this, UiTheme.HudWide, "TRAINED BY", pad, by - 6 * k, ls, UiTheme.Faint);
        if (!trained)
        {
            Gfx.TextFit(this, UiTheme.Body, "No drill yet", pad, by + ts, ts, UiTheme.Faint, r.Size.X - pad * 2);
            return;
        }
        for (int i = 0; i < Math.Min(2, drills.Count); i++)
        {
            var (drill, sig) = drills[i];
            float y = by + ts + i * lineH;
            Gfx.Chevron(this, new Vector2(pad + 3 * k, y - ts * 0.36f), 9 * k, 1, sig ? col : UiTheme.Dim, 2 * k);
            string text = i == 1 && drills.Count > 2 ? $"{drill} +{drills.Count - 2}" : drill;
            Gfx.TextFit(this, UiTheme.Body, text, pad + 14 * k, y, ts, sig ? col : UiTheme.Text, r.Size.X - pad * 2 - 14 * k);
        }
    }
}

/// <summary>Overview card for a role (no agent selected): glyph, what the role does, its agents, signature and role drills;
/// clicking filters the roster.</summary>
public partial class RoleCard : BaseButton
{
    readonly AgentRole role;
    readonly (string Label, string Text)[] sections;
    readonly int agents, withSignature;
    public float K = 1f;
    float hover;

    public RoleCard(AgentRole role, (string Label, string Text)[] sections, int agents, int withSignature)
    {
        this.role = role;
        this.sections = sections;
        this.agents = agents;
        this.withSignature = withSignature;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = $"Show only the {role.ToString().ToLowerInvariant()}s";
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
        float k = K, pad = 22 * k;
        var r = new Rect2(Vector2.Zero, Size);
        var col = AgentArt.RoleColor(role);
        Gfx.Plate(this, r, 16 * k, new Color(0.075f, 0.115f, 0.155f, 0.9f).Lerp(new Color(0.13f, 0.18f, 0.23f, 0.96f), hover), new Color(UiTheme.Text, 0.08f + 0.3f * hover));
        DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y - 18 * k), new Color(col, 0.6f + 0.4f * hover));
        Gfx.VGradient(this, new Rect2(1, r.Size.Y * 0.55f, r.Size.X - 2, r.Size.Y * 0.45f - 1), new Color(col, 0f), new Color(col, 0.08f + 0.05f * hover));
        if (hover > 0.01f) Gfx.Brackets(this, r.Grow(3 * k), 8 * k * hover, new Color(UiTheme.Text, 0.8f * hover), Mathf.Max(1, 2 * k));

        AgentArt.RoleGlyph(this, new Vector2(pad + 26 * k, pad + 26 * k), 24 * k, role, col);
        int ts = UiTheme.Fs(40, k);
        Gfx.Text(this, UiTheme.Display, role.ToString().ToUpperInvariant() + "S", pad + 66 * k, Gfx.Mid(pad + 26 * k, ts), ts, UiTheme.Text);
        int cs = UiTheme.Fs(12, k);
        string count = $"{agents} AGENTS · {withSignature} WITH SIGNATURE DRILLS";
        Gfx.TextFit(this, UiTheme.HudWide, count, pad + 66 * k, pad + 62 * k, cs, col, r.Size.X - pad * 2 - 66 * k);

        int ss = UiTheme.Fs(16, k);
        float w = r.Size.X - pad * 2, y = pad + 88 * k;
        DrawMultilineString(UiTheme.Body, new Vector2(pad, y + UiTheme.Body.GetAscent(ss)), AgentRoles.Summary(role), HorizontalAlignment.Left, w, ss, 2, UiTheme.Text);
        y += UiTheme.Body.GetMultilineStringSize(AgentRoles.Summary(role), HorizontalAlignment.Left, w, ss, 2).Y + 18 * k;
        int ls = UiTheme.Fs(11, k), ds = UiTheme.Fs(14, k);
        foreach (var (label, text) in sections)
        {
            if (y + ls + ds > r.Size.Y - pad) break;
            Gfx.Text(this, UiTheme.HudWide, label, pad, y + ls, ls, UiTheme.Faint);
            y += ls + 8 * k;
            DrawMultilineString(UiTheme.Body, new Vector2(pad, y + UiTheme.Body.GetAscent(ds)), text, HorizontalAlignment.Left, w, ds, 2, UiTheme.Dim);
            y += UiTheme.Body.GetMultilineStringSize(text, HorizontalAlignment.Left, w, ds, 2).Y + 14 * k;
        }
    }
}

/// <summary>Menu grid card that opens the Agents screen (agent drills aren't listed in the main grid).</summary>
public partial class AgentsLinkCard : BaseButton
{
    public float K = 1f;
    public bool TwoLines;
    float hover;

    public AgentsLinkCard()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = $"Agent training: abilities, signature drills, role drills and a Lock-In for each of the {AgentRoster.All.Length} agents";
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
        bool down = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var fill = new Color(0.075f, 0.115f, 0.155f, 0.9f).Lerp(new Color(0.13f, 0.18f, 0.23f, 0.96f), hover);
        if (down) fill = new Color(0.18f, 0.22f, 0.27f, 0.98f);
        Gfx.Plate(this, r, 12 * k, fill, new Color(UiTheme.Text, 0.08f + 0.3f * hover));
        DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y - 14 * k), new Color(ModeCard.CategoryColor("Agents"), 0.55f + 0.45f * hover));
        if (hover > 0.01f) Gfx.Brackets(this, r.Grow(3 * k), 8 * k * hover, new Color(UiTheme.Text, 0.8f * hover), Mathf.Max(1, 2 * k));

        float pad = 16 * k;
        // the four role glyphs on the right
        float gr = 8.5f * k, gx = r.Size.X - 14 * k - gr;
        for (int i = AgentRoles.All.Length - 1; i >= 0; i--, gx -= gr * 2 + 5 * k)
            AgentArt.RoleGlyph(this, new Vector2(gx, 24 * k), gr, AgentRoles.All[i], AgentArt.RoleColor(AgentRoles.All[i]));
        int ns = UiTheme.Fs(23, k);
        Gfx.TextFit(this, UiTheme.Display, "AGENT TRAINING", pad, Gfx.Mid(24 * k, ns), ns, UiTheme.Text, gx - pad);
        int ds = UiTheme.Fs(13.5f, k);
        string d = $"All {AgentRoster.All.Length} agents: signature drills with their own utility, role drills and a 10-minute Lock-In.";
        if (TwoLines)
            DrawMultilineString(UiTheme.Body, new Vector2(pad, 42 * k + UiTheme.Body.GetAscent(ds)), d, HorizontalAlignment.Left, r.Size.X - pad * 2, ds, 2, UiTheme.Dim);
        else
            Gfx.TextFit(this, UiTheme.Body, d, pad, Gfx.Mid(50 * k, ds), ds, UiTheme.Dim, r.Size.X - pad * 2);
    }
}

/// <summary>The agent page's Lock-In schedule: one row per drill with its length and what's special about it (easy start,
/// adaptive, easier last seconds).</summary>
public partial class RoutineList : Control
{
    readonly Warmup.WarmupPlan plan;
    readonly HashSet<string> signature;
    readonly Color sigCol;
    public float K = 1f;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public RoutineList(Warmup.WarmupPlan plan, IEnumerable<string> signature, Color sigCol)
    {
        this.plan = plan;
        this.signature = new HashSet<string>(signature, StringComparer.OrdinalIgnoreCase);
        this.sigCol = sigCol;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float k = K, w = Size.X;
        int n = plan.Steps.Count;
        if (n == 0) return;
        float rh = Mathf.Min(34 * k, Size.Y / n);
        int fs = UiTheme.Fs(rh >= 26 * k ? 15 : 13.5f, k), ls = UiTheme.Fs(11, k);
        for (int i = 0; i < n; i++)
        {
            var s = plan.Steps[i];
            float y = i * rh, cy = y + rh / 2;
            if (i % 2 == 0) DrawRect(new Rect2(0, y, w, rh), new Color(1, 1, 1, 0.025f));
            bool sig = signature.Contains(s.Mode);
            Gfx.Text(this, UiTheme.HudWide, (i + 1).ToString(Inv), 6 * k, Gfx.Mid(cy, ls), ls, UiTheme.Faint);
            float x = 30 * k;
            if (sig) { Gfx.Diamond(this, new Vector2(x + 4 * k, cy), 4 * k, 5 * k, sigCol); x += 14 * k; }
            var (sens, tagCol) = s.Shifted ? ($"{s.OffsetPct:+0;-0}%", Warmup.WarmupCharts.ShiftCol)
                : s.Adaptive ? ("ADAPTIVE", Warmup.WarmupCharts.PhaseColor(Warmup.Phase.Calibration))
                : s.Easier ? ("EASY", Warmup.WarmupCharts.PhaseColor(Warmup.Phase.Activation))
                : s.EaseLast > 0 ? ("EASIER END", UiTheme.Good) : ("", UiTheme.Dim);
            float right = w - 6 * k;
            float tw = Gfx.TextR(this, UiTheme.HudWide, Warmup.WarmupPlan.Clock(s.Seconds), right, Gfx.Mid(cy, ls), ls, UiTheme.Dim);
            float sw = sens.Length > 0 ? Gfx.TextR(this, UiTheme.HudWide, sens, right - tw - 12 * k, Gfx.Mid(cy, ls), ls, tagCol) + 12 * k : 0;
            Gfx.TextFit(this, UiTheme.Body, AgentDrills.Title(s.Mode), x, Gfx.Mid(cy, fs), fs, sig ? sigCol : UiTheme.Text, right - tw - sw - 14 * k - x);
        }
    }
}
