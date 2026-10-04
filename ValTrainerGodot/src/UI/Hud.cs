using System.Globalization;
using Godot;
using ValTrainer.Analysis;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Modes;
using ValTrainer.Valorant;

namespace ValTrainer.UI;

/// <summary>
/// In-game HUD in VALORANT's layout: top-centre timer flanked by score (teal) and kills/accuracy (red), drill
/// info top-left, health + shield and ammo + reserve bottom-centre, reload progress, speed meter for movement
/// drills, damage direction arc + vignette, kill banner, session banners, countdown, pause menu and results
/// (with a procedurally drawn rank emblem). Reads everything from the session; no game logic here.
/// </summary>
public partial class Hud : Control
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly GameSession s;
    public Hud(GameSession session) => s = session;

    // overlay (pause / results) state
    GameSession.St lastState = (GameSession.St)(-1);
    TrainingMode? lastMode;
    DrawBox? overlay;
    Vector2 overlaySize;
    readonly List<VButton> overlayButtons = new();
    VButton? resetSens;
    float overlayAt;
    List<(string Label, string Value)> resultLines = new();

    // aim coach: cue shown during the countdown (from a "Practise" button or the last run's review)
    CoachData.Focus? focus, nextFocus;

    // animation bookkeeping (real time, so it keeps moving while paused)
    float lastStopSeen = -2f, stopPopAt = -99f;

    // tiny string caches so steady-state frames don't allocate
    sealed class Cache
    {
        long key = long.MinValue;
        string val = "";
        public string Get(long k, Func<long, string> f)
        {
            if (k != key) { key = k; val = f(k); }
            return val;
        }
    }
    readonly Cache cClock = new(), cScore = new(), cRight = new(), cRightLabel = new(), cFps = new(), cHp = new(), cShield = new(),
        cAmmo = new(), cReserve = new(), cSpeed = new(), cStop = new(), cTier = new(), cCount = new();

    static float RealNow => Time.GetTicksMsec() / 1000f;

    // uppercase labels cached per mode / subtitle / weapon (avoid per-frame string allocations)
    string nameUpper = "", subSrc = "", subUpper = "", gunSrc = "", gunUpper = "";

    string Upper(ref string src, ref string upper, string value)
    {
        if (!ReferenceEquals(src, value) && src != value) { src = value; upper = value.ToUpperInvariant(); }
        return upper;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
        if (s.Mode == null) return;
        var size = GetViewportRect().Size;
        if (s.State != lastState || s.Mode != lastMode || (overlay != null && size != overlaySize))
        {
            bool modeChanged = s.Mode != lastMode;
            lastState = s.State;
            lastMode = s.Mode;
            if (modeChanged)
            {
                lastStopSeen = -2f;
                nameUpper = s.Mode.Name.ToUpperInvariant();
                focus = CoachData.TakeFocus(s.Mode.Key) ?? nextFocus;
                nextFocus = null;
            }
            RebuildOverlay(size);
        }
        if (overlay != null)
        {
            overlay.QueueRedraw();
            overlay.Modulate = new Color(1, 1, 1, Gfx.EaseOut((RealNow - overlayAt) / 0.2f));
            if (resetSens != null) resetSens.Visible = Main.I.Settings.UseSensOverride;
        }
    }

    /// <summary>
    /// Overlay buttons are activated here, before <see cref="GameSession"/> sees the click: otherwise the press would
    /// leave a pending "fire" edge that shoots on resume. Presses are swallowed while an overlay is up.
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (overlay == null) return;
        if (e is InputEventMouseButton { Pressed: true } mb && mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            GetViewport().SetInputAsHandled();
            if (mb.ButtonIndex != MouseButton.Left) return;
            foreach (var b in overlayButtons)
                if (b.IsVisibleInTree() && !b.Disabled && b.GetGlobalRect().HasPoint(mb.Position)) { b.Click(); break; }
        }
    }

    // =====================================================================================
    // In-game HUD
    // =====================================================================================

    public override void _Draw()
    {
        var m = s.Mode;
        if (m == null) return;
        var size = GetViewportRect().Size;
        float k = UiTheme.S(size);

        if (m.Is2D) m.Draw2D(this, size);
        if (s.State == GameSession.St.Results) return;
        if (!m.Is2D) m.Draw2D(this, size); // 3D drills can add their own overlay (e.g. the spray chart)

        if (!m.Is2D) DrawDamage(size, k);
        DrawTopBar(size, k, m);
        DrawModeInfo(size, k, m);
        if (Main.I.Valorant.ShowFps)
        {
            int fps = (int)Engine.GetFramesPerSecond();
            int fs = UiTheme.Fs(16, k);
            TR(UiTheme.Hud, cFps.Get(fps, v => $"{v} FPS"), size.X - 24 * k, 30 * k, fs, new Color(UiTheme.Text, 0.85f));
        }
        if (!m.Is2D)
        {
            if (m.ShowScore) DrawVitals(size, k);
            if (s.Gun != null && !m.InfiniteAmmo) DrawAmmo(size, k);
            if (m.Movement) DrawSpeed(size, k);
            DrawReload(size, k);
        }
        if (m.Prompt is { } p) DrawPrompt(size, k, Main.I.Valorant.Binds.Hint(p));
        if (s.BannerTime > 0 && s.BannerText.Length > 0) DrawBanner(size, k);
        if (m.KillBanner.Time > 0 && m.KillBanner.Count > 0) DrawKillBanner(size, k, m.KillBanner.Count, m.KillBanner.Time);
        if (s.State == GameSession.St.Countdown) DrawCountdown(size, k, m);
        if (focus != null && (s.State == GameSession.St.Countdown || (s.State == GameSession.St.Running && s.Now < 2f))) DrawFocus(size, k, focus);
    }

    /// <summary>"COACH FOCUS" plate: the problem this run should work on and its first fix cue (fades out after the start).</summary>
    void DrawFocus(Vector2 size, float k, CoachData.Focus f)
    {
        float a = s.State == GameSession.St.Running ? Mathf.Clamp(1f - s.Now / 2f, 0, 1) : Mathf.Clamp((3f - s.Countdown) / 0.3f, 0, 1);
        if (a <= 0.01f) return;
        float cx = size.X / 2, cy = size.Y * 0.74f;
        int cs = UiTheme.Fs(12, k), ts = UiTheme.Fs(25, k), qs = UiTheme.Fs(18, k);
        float w = Mathf.Max(Gfx.TextW(UiTheme.Display, f.Title, ts), Gfx.TextW(UiTheme.Body, f.Cue, qs)) + 90 * k;
        w = Mathf.Min(w, size.X * 0.72f);
        float h = f.Cue.Length > 0 ? 100 * k : 72 * k;
        var r = new Rect2(cx - w / 2, cy - h / 2, w, h);
        DrawRect(r, new Color(0.03f, 0.05f, 0.08f, 0.7f * a));
        DrawRect(new Rect2(r.Position, new Vector2(4 * k, h)), new Color(UiTheme.Accent, a));
        Gfx.Brackets(this, r, 10 * k, new Color(UiTheme.Text, 0.35f * a), Mathf.Max(1, 2 * k));
        float y = r.Position.Y;
        TC(UiTheme.HudWide, "COACH FOCUS", cx, Gfx.Mid(y + 20 * k, cs), cs, new Color(UiTheme.Accent, a));
        float inner = w - 50 * k;
        Gfx.TextFit(this, UiTheme.Display, f.Title, cx - inner / 2, Gfx.Mid(y + 48 * k, ts), ts, new Color(UiTheme.Text, a), inner, HorizontalAlignment.Center);
        if (f.Cue.Length > 0)
            Gfx.TextFit(this, UiTheme.Body, f.Cue, cx - inner / 2, Gfx.Mid(y + 78 * k, qs), qs, new Color(UiTheme.Text, 0.85f * a), inner, HorizontalAlignment.Center);
    }

    void DrawTopBar(Vector2 size, float k, TrainingMode m)
    {
        float cx = size.X / 2, top = 14 * k, bh = 56 * k, tw = 128 * k, sw = 168 * k, gap = 6 * k;

        // timer
        float remain = m.Timed ? Mathf.Max(0, m.Duration - s.Now) : s.Now;
        int secs = m.Timed ? Mathf.CeilToInt(remain) : (int)remain;
        var tr = new Rect2(cx - tw / 2, top, tw, bh);
        DrawRect(tr, UiTheme.Plate);
        DrawRect(new Rect2(tr.Position.X, tr.End.Y - 2 * k, tw, 2 * k), new Color(UiTheme.Text, 0.18f));
        bool hurry = m.Timed && remain < 10f && s.State == GameSession.St.Running;
        int fs = UiTheme.Fs(42, k);
        string clock = cClock.Get(secs, v => $"{v / 60}:{v % 60:00}");
        float sc = 1f;
        if (hurry) { float f = remain - Mathf.Floor(remain); sc = 1f + 0.14f * Mathf.Max(0, (f - 0.7f) / 0.3f); }
        var clockCol = hurry ? UiTheme.Accent : m.Timed ? UiTheme.Text : UiTheme.Dim;
        ScaledTextC(UiTheme.Display, clock, new Vector2(cx, Gfx.Mid(top + bh / 2, fs)), fs, clockCol, sc);
        if (m.Timed)
        {
            float frac = m.Duration > 0 ? remain / m.Duration : 0;
            DrawRect(new Rect2(tr.Position.X, tr.End.Y - 2 * k, tw * frac, 2 * k), hurry ? UiTheme.Accent : UiTheme.Text);
        }

        if (!m.ShowScore) return; // drills without a meaningful score/accuracy (Sens Finder): clock only

        // score (teal) — left
        var lr = new Rect2(tr.Position.X - gap - sw, top, sw, bh);
        Gfx.Slant(this, lr, 20 * k, true, new Color(UiTheme.Teal, 0.85f));
        int vs = UiTheme.Fs(40, k), ls = UiTheme.Fs(12, k);
        TC(UiTheme.Display, cScore.Get(m.Score, v => v.ToString("#,0", Inv)), lr.GetCenter().X + 8 * k, Gfx.Mid(top + bh / 2, vs), vs, UiTheme.Text);
        TC(UiTheme.HudWide, "SCORE", lr.GetCenter().X + 8 * k, top + bh + 18 * k, ls, UiTheme.Teal);

        // kills / accuracy (red) — right
        var rr = new Rect2(tr.End.X + gap, top, sw, bh);
        Gfx.Slant(this, rr, 20 * k, false, new Color(UiTheme.Accent, 0.85f));
        int accTenths = (int)(m.Accuracy * 1000);
        string right = m.UsesHeadshots ? cRight.Get(m.Kills, v => v.ToString(Inv)) : cRight.Get(accTenths + 1_000_000L, v => $"{(v - 1_000_000) / 10f:0}%");
        TC(UiTheme.Display, right, rr.GetCenter().X - 8 * k, Gfx.Mid(top + bh / 2, vs), vs, UiTheme.Text);
        string rl = m.UsesHeadshots ? cRightLabel.Get(accTenths / 10, v => $"KILLS · {v}% ACC") : "ACCURACY";
        TC(UiTheme.HudWide, rl, rr.GetCenter().X - 8 * k, top + bh + 18 * k, ls, UiTheme.Accent);
    }

    void DrawModeInfo(Vector2 size, float k, TrainingMode m)
    {
        float x = 30 * k, y = 22 * k;
        DrawRect(new Rect2(x, y, 4 * k, 36 * k), UiTheme.Accent);
        int ns = UiTheme.Fs(34, k);
        TL(UiTheme.Display, nameUpper, x + 14 * k, Gfx.Mid(y + 18 * k, ns), ns, UiTheme.Text);
        y += 56 * k;
        var tier = Difficulty.Get(s.Tier);
        int ts = UiTheme.Fs(13, k);
        if (m.ShowScore) RankEmblem.Draw(this, new Vector2(x + 9 * k, y), 9 * k, tier.Id);
        if (m.ShowScore) TL(UiTheme.HudWide, cTier.Get(tier.Id, v => $"{Difficulty.Get((int)v).Name.ToUpperInvariant()} · {Difficulty.Get((int)v).Ranks.ToUpperInvariant()}"),
            x + 26 * k, Gfx.Mid(y, ts), ts, UiTheme.TierColor(tier.Id));
        if (m.Subtitle is { } sub)
        {
            y += 24 * k;
            TL(UiTheme.HudWide, Upper(ref subSrc, ref subUpper, sub), x + 26 * k, Gfx.Mid(y, ts), ts, new Color(UiTheme.Text, 0.85f));
        }
        y += 34 * k;
        int ls = UiTheme.Fs(18, k);
        foreach (var rawLine in m.HudLines())
        {
            var line = Main.I.Valorant.Binds.Hint(rawLine);
            // movement drills show their last stop under the speed meter instead
            if (m.Movement && line.StartsWith("Last stop", StringComparison.Ordinal)) continue;
            float w = Gfx.TextW(UiTheme.Body, line, ls);
            DrawRect(new Rect2(x, y - 14 * k, w + 26 * k, 28 * k), new Color(0.04f, 0.07f, 0.1f, 0.5f));
            DrawRect(new Rect2(x, y - 14 * k, 2 * k, 28 * k), new Color(UiTheme.Accent, 0.8f));
            TL(UiTheme.Body, line, x + 13 * k, Gfx.Mid(y, ls), ls, UiTheme.Text);
            y += 32 * k;
        }
    }

    void DrawVitals(Vector2 size, float k)
    {
        float cx = size.X / 2, bh = 74 * k, by = size.Y - 30 * k - bh, bw = 300 * k;
        var r = new Rect2(cx - 140 * k - bw, by, bw, bh);
        Gfx.Plate(this, r, 12 * k, UiTheme.Plate, new Color(UiTheme.Text, 0.08f));
        float hp = Mathf.Max(0, s.Player.Hp), sh = Mathf.Max(0, s.Player.Shield);
        bool low = hp < 30;

        // shield icon + value
        float sx = r.Position.X + 26 * k, sy = r.Position.Y + 30 * k, ss = 12 * k;
        Span<Vector2> shield = stackalloc Vector2[5];
        shield[0] = new(sx - ss, sy - ss); shield[1] = new(sx + ss, sy - ss); shield[2] = new(sx + ss, sy + ss * 0.2f);
        shield[3] = new(sx, sy + ss * 1.25f); shield[4] = new(sx - ss, sy + ss * 0.2f);
        this.FillPoly(shield, new Color(UiTheme.Text, sh > 0 ? 0.8f : 0.25f));
        int svs = UiTheme.Fs(24, k);
        TL(UiTheme.Hud, cShield.Get(Mathf.CeilToInt(sh), v => v.ToString(Inv)), sx + ss + 10 * k, Gfx.Mid(sy, svs), svs, sh > 0 ? UiTheme.Text : UiTheme.Faint);

        // health (big, right side of the plate)
        int hs = UiTheme.Fs(66, k);
        TR(UiTheme.Display, cHp.Get(Mathf.CeilToInt(hp), v => v.ToString(Inv)), r.End.X - 22 * k, Gfx.Mid(r.Position.Y + 32 * k, hs), hs,
            low ? UiTheme.Accent : UiTheme.Text);

        // bars: shield (thin) over health
        float bx = r.Position.X + 16 * k, bwid = r.Size.X - 32 * k, hy = r.End.Y - 11 * k;
        DrawRect(new Rect2(bx, hy, bwid, 4 * k), new Color(1, 1, 1, 0.12f));
        DrawRect(new Rect2(bx, hy, bwid * Mathf.Clamp(hp / 100f, 0, 1), 4 * k), low ? UiTheme.Accent : UiTheme.Text);
        if (sh > 0) DrawRect(new Rect2(bx, hy - 4 * k, bwid * Mathf.Clamp(sh / 50f, 0, 1), 2 * k), new Color(0.62f, 0.8f, 1f, 0.85f));
    }

    void DrawAmmo(Vector2 size, float k)
    {
        var gun = s.Gun!;
        float cx = size.X / 2, bh = 74 * k, by = size.Y - 30 * k - bh, bw = 300 * k;
        var r = new Rect2(cx + 140 * k, by, bw, bh);
        Gfx.Plate(this, r, 12 * k, UiTheme.Plate, new Color(UiTheme.Text, 0.08f));
        bool low = gun.Ammo <= Math.Max(1, gun.Def.Mag / 5);
        int as_ = UiTheme.Fs(66, k), rs = UiTheme.Fs(26, k), ns = UiTheme.Fs(12, k);
        string ammo = cAmmo.Get(gun.Ammo, v => v.ToString(Inv));
        float x = r.Position.X + 22 * k, baseY = Gfx.Mid(r.Position.Y + 32 * k, as_);
        var ac = gun.Reloading ? UiTheme.Faint : low ? UiTheme.Accent : UiTheme.Text;
        TL(UiTheme.Display, ammo, x, baseY, as_, ac);
        x += Gfx.TextW(UiTheme.Display, ammo, as_) + 10 * k;
        TL(UiTheme.Hud, cReserve.Get(gun.Reserve, v => $"/ {v}"), x, baseY, rs, UiTheme.Dim);
        TR(UiTheme.HudWide, Upper(ref gunSrc, ref gunUpper, gun.Def.Name), r.End.X - 22 * k, r.Position.Y + 24 * k, ns, UiTheme.Dim);
        // magazine bar
        float bx = r.Position.X + 16 * k, bwid = r.Size.X - 32 * k, hy = r.End.Y - 11 * k;
        DrawRect(new Rect2(bx, hy, bwid, 4 * k), new Color(1, 1, 1, 0.12f));
        float frac = gun.Reloading ? 1f - gun.ReloadLeft / Mathf.Max(0.01f, gun.Def.Reload) : gun.Ammo / (float)Math.Max(1, gun.Def.Mag);
        DrawRect(new Rect2(bx, hy, bwid * Mathf.Clamp(frac, 0, 1), 4 * k), gun.Reloading ? UiTheme.Warn : low ? UiTheme.Accent : UiTheme.Text);
    }

    void DrawReload(Vector2 size, float k)
    {
        var gun = s.Gun;
        if (gun == null || !gun.Reloading) return;
        var c = size / 2;
        float p = Mathf.Clamp(1f - gun.ReloadLeft / Mathf.Max(0.01f, gun.Def.Reload), 0, 1);
        int fs = UiTheme.Fs(14, k);
        TC(UiTheme.HudWide, "RELOADING", c.X, c.Y + 72 * k, fs, UiTheme.Text);
        float w = 150 * k;
        DrawRect(new Rect2(c.X - w / 2, c.Y + 82 * k, w, 4 * k), new Color(0, 0, 0, 0.45f));
        DrawRect(new Rect2(c.X - w / 2, c.Y + 82 * k, w * p, 4 * k), UiTheme.Text);
    }

    void DrawSpeed(Vector2 size, float k)
    {
        var mv = s.Mover;
        float cx = size.X / 2, by = size.Y - 30 * k - 74 * k;
        var plate = new Rect2(cx - 128 * k, by + 16 * k, 256 * k, 58 * k);
        DrawRect(plate, UiTheme.Plate);
        var r = plate.Grow(-14 * k);
        int ls = UiTheme.Fs(12, k), vs = UiTheme.Fs(17, k);
        TL(UiTheme.HudWide, "SPEED", r.Position.X, r.Position.Y + 10 * k, ls, UiTheme.Dim);
        TR(UiTheme.Hud, cSpeed.Get((int)(mv.Speed * 10), v => $"{v / 10f:0.0} m/s"), r.End.X, r.Position.Y + 11 * k, vs,
            mv.Accurate ? UiTheme.Good : UiTheme.Text);
        float barY = r.Position.Y + 20 * k, bh = 8 * k;
        DrawRect(new Rect2(r.Position.X, barY, r.Size.X, bh), new Color(0, 0, 0, 0.5f));
        DrawRect(new Rect2(r.Position.X, barY, r.Size.X * Mathf.Clamp(mv.Speed / Mover.RunSpeed, 0, 1), bh), mv.Accurate ? UiTheme.Good : UiTheme.Accent);
        float tx = r.Position.X + r.Size.X * (Mover.AccurateSpeed / Mover.RunSpeed);
        DrawRect(new Rect2(tx - 1 * k, barY - 4 * k, 2 * k, bh + 8 * k), UiTheme.Text);

        // last stop feedback (pops when a new stop is measured)
        if (mv.LastStopMs != lastStopSeen)
        {
            if (mv.LastStopMs >= 0 && lastStopSeen > -2f) stopPopAt = RealNow;
            lastStopSeen = mv.LastStopMs;
        }
        if (mv.LastStopMs >= 0)
        {
            long key = (long)mv.LastStopMs * 2 + (mv.LastStopWasCounter ? 1 : 0);
            string t = cStop.Get(key, v => $"LAST STOP {v / 2} MS · {((v & 1) == 1 ? "COUNTER-STRAFE" : "RELEASED KEYS")}");
            float age = RealNow - stopPopAt, sc = age < 0.22f ? 1.25f - 0.25f * Gfx.EaseOut(age / 0.22f) : 1f;
            int fs = UiTheme.Fs(17, k);
            ScaledTextC(UiTheme.HudWide, t, new Vector2(cx, plate.Position.Y - 22 * k), fs, mv.LastStopWasCounter ? UiTheme.Good : UiTheme.Warn, sc);
        }
    }

    void DrawDamage(Vector2 size, float k)
    {
        float hp = s.Player.Hp;
        if (hp < 30)
        {
            Gfx.EdgeVignette(this, size, 240 * k, new Color(0.7f, 0.02f, 0.06f, hp <= 0 ? 0.5f : 0.26f));
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0.55f, 0f, 0.02f, hp <= 0 ? 0.12f : 0.05f));
        }
        float since = s.Now - s.Player.LastHitAt;
        if (since < 0 || since >= 0.6f) return;
        float a = 1f - since / 0.6f;
        Gfx.EdgeVignette(this, size, 150 * k, new Color(0.85f, 0.04f, 0.08f, 0.38f * a));
        var d = s.Player.LastHitFrom - s.View.Eye;
        if (d.X * d.X + d.Z * d.Z < 1e-4f) return;
        float yawTo = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
        float rel = Mathf.Wrap(yawTo - s.View.ViewYaw, -180f, 180f); // + = to the right
        float ang = Mathf.DegToRad(rel) - Mathf.Pi / 2;
        var c = size / 2;
        float rad = 120 * k;
        DrawArc(c, rad, ang - 0.44f, ang + 0.44f, 28, new Color(UiTheme.Accent, 0.28f * a), 16 * k, true);
        DrawArc(c, rad, ang - 0.38f, ang + 0.38f, 28, new Color(1f, 0.22f, 0.26f, 0.95f * a), 5 * k, true);
        // pointer notch
        var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        var perp = new Vector2(-dir.Y, dir.X);
        Span<Vector2> tri = stackalloc Vector2[3];
        tri[0] = c + dir * (rad + 18 * k); tri[1] = c + dir * (rad + 4 * k) + perp * 9 * k; tri[2] = c + dir * (rad + 4 * k) - perp * 9 * k;
        this.FillPoly(tri, new Color(1f, 0.22f, 0.26f, 0.95f * a));
    }

    void DrawPrompt(Vector2 size, float k, string p)
    {
        int fs = UiTheme.Fs(24, k);
        float w = Gfx.TextW(UiTheme.Body, p, fs), cy = size.Y * 0.64f;
        var r = new Rect2(size.X / 2 - w / 2 - 22 * k, cy - 22 * k, w + 44 * k, 44 * k);
        Gfx.HGradient(this, new Rect2(r.Position.X - 60 * k, r.Position.Y, 60 * k, r.Size.Y), new Color(0, 0, 0, 0), new Color(0.03f, 0.05f, 0.08f, 0.6f));
        DrawRect(r, new Color(0.03f, 0.05f, 0.08f, 0.6f));
        Gfx.HGradient(this, new Rect2(r.End.X, r.Position.Y, 60 * k, r.Size.Y), new Color(0.03f, 0.05f, 0.08f, 0.6f), new Color(0, 0, 0, 0));
        TC(UiTheme.Body, p, size.X / 2, Gfx.Mid(cy, fs), fs, UiTheme.Text);
    }

    void DrawBanner(Vector2 size, float k)
    {
        const float life = 1.4f;
        float age = life - s.BannerTime;
        float alpha = Mathf.Clamp(s.BannerTime / 0.35f, 0, 1) * Mathf.Clamp(age / 0.06f, 0, 1);
        float sc = age < 0.2f ? 1.35f - 0.35f * Gfx.EaseOut(age / 0.2f) : 1f;
        float stripe = Mathf.Clamp(age / 0.18f, 0, 1);
        var col = s.BannerColor;
        int fs = UiTheme.Fs(56, k);
        float cy = size.Y * 0.30f, cx = size.X / 2;
        float w = Gfx.TextW(UiTheme.Display, s.BannerText, fs) + 220 * k, h = 78 * k;
        float half = w / 2 * Gfx.EaseOut(stripe);
        var dark = new Color(0.03f, 0.05f, 0.08f, 0.62f * alpha);
        var clear = new Color(0.03f, 0.05f, 0.08f, 0);
        Gfx.HGradient(this, new Rect2(cx - half, cy - h / 2, half, h), clear, dark);
        Gfx.HGradient(this, new Rect2(cx, cy - h / 2, half, h), dark, clear);
        var line = new Color(col, 0.85f * alpha);
        var lineClear = new Color(col, 0);
        Gfx.HGradient(this, new Rect2(cx - half, cy - h / 2, half, 2 * k), lineClear, line);
        Gfx.HGradient(this, new Rect2(cx, cy - h / 2, half, 2 * k), line, lineClear);
        Gfx.HGradient(this, new Rect2(cx - half, cy + h / 2 - 2 * k, half, 2 * k), lineClear, line);
        Gfx.HGradient(this, new Rect2(cx, cy + h / 2 - 2 * k, half, 2 * k), line, lineClear);
        Gfx.Diamond(this, new Vector2(cx, cy - h / 2), 6 * k, 6 * k, line);
        ScaledTextC(UiTheme.Display, s.BannerText, new Vector2(cx, Gfx.Mid(cy, fs)), fs, new Color(col, alpha), sc);
    }

    void DrawKillBanner(Vector2 size, float k, int count, float t)
    {
        const float life = 1.4f;
        float age = life - t;
        float pop = age < 0.14f ? 1.15f - 0.15f * (age / 0.14f) : 1f;
        float alpha = Mathf.Clamp(t / 0.35f, 0, 1);
        var c = new Vector2(size.X / 2, size.Y - 262 * k);
        float r = 36 * k;
        var red = new Color(UiTheme.Accent, alpha);
        var gold = new Color(UiTheme.TierColor(4), alpha);
        var main = count >= 5 ? gold : red;

        DrawSetTransform(c, 0, new Vector2(pop, pop));
        var o = Vector2.Zero;
        if (age < 0.45f)
        {
            float g = age / 0.45f;
            DrawArc(o, r * (1.1f + g * 1.4f), 0, Mathf.Tau, 48, new Color(main, (1 - g) * 0.55f * alpha), 3 * k, true);
        }
        // side blades
        Span<Vector2> blade = stackalloc Vector2[4];
        for (int side = -1; side <= 1; side += 2)
        {
            blade[0] = new(side * r * 1.08f, -r * 0.34f); blade[1] = new(side * r * 1.9f, 0);
            blade[2] = new(side * r * 1.08f, r * 0.34f); blade[3] = new(side * r * 1.3f, 0);
            this.FillPoly(blade, main);
        }
        Gfx.Diamond(this, o, r, r, new Color(0.05f, 0.08f, 0.11f, 0.88f * alpha));
        Gfx.DiamondLine(this, o, r, r, main, 3 * k);
        Gfx.Diamond(this, o, r * 0.44f, r * 0.44f, main);
        Gfx.Diamond(this, o, r * 0.14f, r * 0.14f, new Color(1, 1, 1, 0.9f * alpha));
        for (int i = 0; i < count; i++)
        {
            float x = (i - (count - 1) / 2f) * 22 * k;
            Gfx.ChevronDown(this, new Vector2(x, r + 18 * k), 18 * k, 11 * k, 4 * k, main);
        }
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    void DrawCountdown(Vector2 size, float k, TrainingMode m)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(s.Countdown));
        float frac = Mathf.Clamp(s.Countdown - (n - 1), 0, 1); // 1 → 0 within each second
        var c = new Vector2(size.X / 2, size.Y * 0.3f);
        float r = 104 * k;
        DrawCircle(c, r + 18 * k, new Color(0.03f, 0.05f, 0.08f, 0.45f));
        DrawArc(c, r, 0, Mathf.Tau, 72, new Color(UiTheme.Text, 0.12f), 3 * k, true);
        DrawArc(c, r, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * frac, 72, UiTheme.Accent, 4 * k, true);
        float pulse = frac > 0.78f ? (frac - 0.78f) / 0.22f : 0f;
        float sc = 1f + 0.4f * pulse * pulse;
        int fs = UiTheme.Fs(150, k);
        ScaledTextC(UiTheme.Display, cCount.Get(n, v => v.ToString(Inv)), new Vector2(c.X, Gfx.Mid(c.Y, fs)), fs, new Color(UiTheme.Text, 1f - 0.5f * pulse), sc);
        int ls = UiTheme.Fs(14, k);
        TC(UiTheme.HudWide, "GET READY", c.X, c.Y - r - 34 * k, ls, UiTheme.Dim);
        int ns = UiTheme.Fs(30, k), ds = UiTheme.Fs(16, k);
        TC(UiTheme.Display, nameUpper, c.X, c.Y + r + 54 * k, ns, UiTheme.Text);
        TC(UiTheme.Body, m.Description, c.X, c.Y + r + 82 * k, ds, UiTheme.Dim);
    }

    // ---- outlined text (readable over bright scenes, like VALORANT's shadowed HUD text) ----

    static Color Shadow(Color c) => new(0.02f, 0.03f, 0.05f, 0.5f * c.A);

    void TL(Font f, string t, float x, float baseline, int fs, Color c)
    {
        var pos = new Vector2(x, baseline);
        DrawStringOutline(f, pos, t, HorizontalAlignment.Left, -1, fs, Math.Max(2, fs / 9), Shadow(c));
        DrawString(f, pos, t, HorizontalAlignment.Left, -1, fs, c);
    }

    float TC(Font f, string t, float cx, float baseline, int fs, Color c)
    {
        float w = Gfx.TextW(f, t, fs);
        TL(f, t, cx - w / 2, baseline, fs, c);
        return w;
    }

    float TR(Font f, string t, float right, float baseline, int fs, Color c)
    {
        float w = Gfx.TextW(f, t, fs);
        TL(f, t, right - w, baseline, fs, c);
        return w;
    }

    /// <summary>Centred text scaled around its own centre (pop animations).</summary>
    void ScaledTextC(Font f, string t, Vector2 baselineCentre, int fs, Color c, float scale)
    {
        float w = Gfx.TextW(f, t, fs);
        if (Mathf.Abs(scale - 1f) < 0.001f)
        {
            TL(f, t, baselineCentre.X - w / 2, baselineCentre.Y, fs, c);
            return;
        }
        // scale about the visual centre of the caps (baseline - 0.36 em)
        var pivot = new Vector2(baselineCentre.X, baselineCentre.Y - fs * 0.36f);
        DrawSetTransform(pivot, 0, new Vector2(scale, scale));
        TL(f, t, -w / 2, fs * 0.36f, fs, c);
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    // =====================================================================================
    // Pause & results overlays (child controls with real buttons)
    // =====================================================================================

    void RebuildOverlay(Vector2 size)
    {
        if (overlay != null) { RemoveChild(overlay); overlay.QueueFree(); overlay = null; }
        overlayButtons.Clear();
        resetSens = null;
        if (s.State is not (GameSession.St.Paused or GameSession.St.Results)) return;

        overlaySize = size;
        overlayAt = RealNow;
        float k = UiTheme.S(size);
        overlay = new DrawBox { MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        overlay.Theme = UiTheme.MakeTheme(k);
        AddChild(overlay);
        if (s.State == GameSession.St.Paused) BuildPause(size, k);
        else BuildResults(size, k);
    }

    VButton OverlayButton(string label, VButton.Look look, Rect2 r, float k, Action a, float fontPx = 22)
    {
        var b = new VButton { Label = label, Kind = look, K = k, FontPx = fontPx, Position = r.Position, Size = r.Size, CustomMinimumSize = r.Size };
        b.Pressed += a;
        overlay!.AddChild(b);
        overlayButtons.Add(b);
        return b;
    }

    static void Dimmer(CanvasItem ci, Vector2 size, float k, float a)
    {
        ci.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.03f, 0.05f, 0.07f, a));
        var line = new Color(1, 1, 1, 0.018f);
        float step = 46 * k;
        for (float x = -size.Y; x < size.X; x += step) ci.DrawLine(new Vector2(x, size.Y), new Vector2(x + size.Y * 0.58f, 0), line, 1f);
    }

    // ---------------- pause ----------------

    void BuildPause(Vector2 size, float k)
    {
        var m = s.Mode;
        var app = Main.I;
        var panel = new Rect2(size.X / 2 - 290 * k, size.Y / 2 - 300 * k, 580 * k, 600 * k);
        float bx = panel.Position.X + 90 * k, bw = panel.Size.X - 180 * k, bh = 56 * k;
        float y = panel.Position.Y + 156 * k;
        OverlayButton("RESUME", VButton.Look.Primary, new Rect2(bx, y, bw, bh), k, s.Resume);
        y += 74 * k;
        OverlayButton("RESTART", VButton.Look.Secondary, new Rect2(bx, y, bw, bh), k, s.Restart);
        y += 92 * k;
        float sensY = y;
        float ab = 44 * k;
        void Adjust(int d)
        {
            var st = app.Settings;
            if (!st.UseSensOverride) { st.UseSensOverride = true; st.SensOverride = app.Valorant.Sensitivity; }
            float step = Input.IsKeyPressed(Key.Shift) ? 0.001f : 0.01f;
            st.SensOverride = MathF.Max(0.001f, MathF.Round((st.SensOverride + d * step) * 1000f) / 1000f);
            st.Save();
        }
        OverlayButton("", VButton.Look.Arrow, new Rect2(bx, sensY, ab, ab), k, () => Adjust(-1)).Dir = -1;
        OverlayButton("", VButton.Look.Arrow, new Rect2(bx + bw - ab, sensY, ab, ab), k, () => Adjust(1)).Dir = 1;
        // Full column width: at 720p the 10 px font floor makes the label wider than the 1080p design (was truncated).
        resetSens = OverlayButton("USE VALORANT SENSITIVITY", VButton.Look.Ghost, new Rect2(bx, sensY + ab + 6 * k, bw, 28 * k), k, () =>
        {
            app.Settings.UseSensOverride = false;
            app.Settings.Save();
        }, 14);
        resetSens.TitleFont = UiTheme.HudWide;
        y += 118 * k;
        OverlayButton("BACK TO MENU", VButton.Look.Secondary, new Rect2(bx, y, bw, bh), k, Main.I.ShowMenu);

        var pauseTier = Difficulty.Get(s.Tier);
        string pauseSub = $"{m.Name.ToUpperInvariant()} · {pauseTier.Name.ToUpperInvariant()}";
        overlay!.OnDraw = d =>
        {
            Dimmer(d, size, k, 0.78f);
            var pr = panel;
            Gfx.Plate(d, pr, 22 * k, new Color(0.06f, 0.1f, 0.14f, 0.96f), new Color(UiTheme.Text, 0.14f));
            d.DrawRect(new Rect2(pr.Position, new Vector2(48 * k, 3 * k)), UiTheme.Accent);
            float cx = pr.GetCenter().X;
            int ts = UiTheme.Fs(76, k), ss = UiTheme.Fs(14, k);
            Gfx.TextC(d, UiTheme.Display, "PAUSED", cx, pr.Position.Y + 92 * k, ts, UiTheme.Text);
            Gfx.TextC(d, UiTheme.HudWide, pauseSub, cx, pr.Position.Y + 124 * k, ss, UiTheme.Dim);

            // sensitivity quick adjust
            float sens = app.Sens;
            int vs = UiTheme.Fs(30, k), ls = UiTheme.Fs(11, k);
            Gfx.TextC(d, UiTheme.HudWide, "SENSITIVITY", cx, sensY - 12 * k, ls, UiTheme.Dim);
            d.DrawRect(new Rect2(bx + ab + 8 * k, sensY, bw - 2 * ab - 16 * k, ab), new Color(0.03f, 0.06f, 0.09f, 0.9f));
            Gfx.TextC(d, UiTheme.Display, sens.ToString("0.000", Inv), cx, Gfx.Mid(sensY + ab / 2, vs), vs, app.Settings.UseSensOverride ? UiTheme.Warn : UiTheme.Text);
            int ns = UiTheme.Fs(12, k);
            string note = app.Settings.UseSensOverride
                ? $"CUSTOM · {PlayerView.Cm360(sens, app.Settings.Dpi):0.0} CM/360 · SHIFT ±0.001"
                : $"FROM VALORANT · {PlayerView.Cm360(sens, app.Settings.Dpi):0.0} CM/360";
            if (!app.Settings.UseSensOverride) Gfx.TextC(d, UiTheme.HudWide, note, cx, sensY + ab + 24 * k, ns, UiTheme.Faint);
            else Gfx.TextC(d, UiTheme.HudWide, note, cx, sensY + ab + 56 * k, ns, UiTheme.Faint);

            Gfx.TextC(d, UiTheme.HudWide, "ESC — RESUME", cx, pr.End.Y - 26 * k, ls, UiTheme.Faint);
        };
    }

    // ---------------- results ----------------

    void BuildResults(Vector2 size, float k)
    {
        var m = s.Mode;
        resultLines = m.ResultLines().ToList();
        var rec = s.Record;
        int prev = s.PrevBest;
        int tierId = s.Tier;
        int badge = rec?.Badge ?? m.Badge();
        int score = rec?.Score ?? m.Score;
        bool first = prev < 0, pb = !first && score > prev;
        var tier = Difficulty.Get(tierId);
        string scoreText = score.ToString("#,0", Inv);
        string modeName = m.Name.ToUpperInvariant();
        string tl = $"{tier.Name.ToUpperInvariant()} · {tier.Ranks.ToUpperInvariant()}";
        string? subUp = m.Subtitle?.ToUpperInvariant();
        string chip = pb ? "NEW PERSONAL BEST" : $"FIRST RUN AT {tier.Name.ToUpperInvariant()}";
        string prevText = pb ? $"previous best {prev.ToString("#,0", Inv)}" : $"{tier.Name} best: {prev.ToString("#,0", Inv)}  ({score - prev:+#,0;-#,0;0})";
        string badgeName = badge >= 0 ? Difficulty.Tiers[badge].Name.ToUpperInvariant() : "BELOW ROOKIE";
        string badgeRanks = badge >= 0 ? Difficulty.Tiers[badge].Ranks : "keep practising";
        bool showScore = m.ShowScore;
        float scoreH = showScore ? 292 * k : 40 * k; // score + badge block (hidden for drills like the Sens Finder)

        // aim coach review (right-hand panel) and mode-specific extra buttons
        var review = CoachData.ReviewFor(s);
        bool hasReview = CoachData.HasContent(review);
        if (hasReview) nextFocus = FocusFor(review!, m.Key);
        var extra = ExtraButtons(m);
        if (hasReview) k = Mathf.Min(k, size.X / 1764f); // both panels side by side must fit (4:3 / 5:4 windows)

        bool twoCol = resultLines.Count > 5;
        int lineRows = twoCol ? (resultLines.Count + 1) / 2 : resultLines.Count;
        float pw = (hasReview ? 900 : 1060) * k, rw = hasReview ? 760 * k : 0, gap = hasReview ? 24 * k : 0;
        float extraH = extra.Count > 0 ? 72 * k : 0;
        float maxH = size.Y - 60 * k;
        float ph = Mathf.Min(maxH, 120 * k + scoreH + lineRows * 40 * k + 40 * k + 104 * k + extraH);
        var rl = hasReview ? LayoutReview(review!, rw, maxH, k) : null;
        if (rl != null) ph = Mathf.Min(maxH, Mathf.Max(ph, rl.Height));
        float left = size.X / 2 - (pw + gap + rw) / 2;
        var panel = new Rect2(left, size.Y / 2 - ph / 2, pw, ph);
        var rpanel = new Rect2(panel.End.X + gap, panel.Position.Y, rw, ph);
        float btnY = panel.End.Y - 90 * k;
        OverlayButton("PLAY AGAIN  (R)", VButton.Look.Primary, new Rect2(panel.GetCenter().X - 330 * k, btnY, 320 * k, 58 * k), k, s.Restart);
        OverlayButton("MENU  (ESC)", VButton.Look.Secondary, new Rect2(panel.GetCenter().X + 10 * k, btnY, 320 * k, 58 * k), k, Main.I.ShowMenu);
        if (extra.Count > 0)
        {
            float eg = 20 * k;
            float ew = Mathf.Min(320 * k, (pw - 88 * k - (extra.Count - 1) * eg) / extra.Count);
            float ex = panel.GetCenter().X - (extra.Count * ew + (extra.Count - 1) * eg) / 2;
            var extraBtns = new List<(VButton B, string Up)>();
            foreach (var (label, act) in extra)
            {
                string up = label.ToUpperInvariant();
                VButton? eb = null;
                eb = OverlayButton(up, VButton.Look.Secondary, new Rect2(ex, btnY - 70 * k, ew, 52 * k), k, () =>
                {
                    try { act(); } catch (Exception ex2) { GD.PushWarning($"Result button failed: {ex2.Message}"); }
                    // only the last choice is ticked ("Use …" then "Keep my sens" undoes the first)
                    foreach (var (b, u) in extraBtns) { b.Label = b == eb ? "✓  " + u : u; b.QueueRedraw(); }
                }, 19);
                extraBtns.Add((eb, up));
                ex += ew + eg;
            }
        }
        if (rl != null)
            foreach (var (r, label, sub, act, look) in rl.Buttons)
            {
                var b = OverlayButton(label, look, new Rect2(rpanel.Position + r.Position, r.Size), k, act, look == VButton.Look.Ghost ? 14 : 15);
                b.Sub = sub;
                b.SubPx = 11.5f;
                if (look == VButton.Look.Ghost) { b.TitleFont = UiTheme.HudWide; b.Align = HorizontalAlignment.Left; }
            }

        overlay!.OnDraw = d =>
        {
            float t = RealNow - overlayAt;
            Dimmer(d, size, k, 0.86f);
            var pr = panel;
            Gfx.Plate(d, pr, 26 * k, new Color(0.06f, 0.1f, 0.14f, 0.97f), new Color(UiTheme.Text, 0.14f));
            d.DrawRect(new Rect2(pr.Position, new Vector2(60 * k, 3 * k)), UiTheme.Accent);
            float x0 = pr.Position.X + 44 * k, x1 = pr.End.X - 44 * k, y = pr.Position.Y;

            // header
            int hs = UiTheme.Fs(13, k), ts = UiTheme.Fs(46, k);
            var tag = new Rect2(x0, y + 34 * k, Gfx.TextW(UiTheme.HudWide, "RESULTS", hs) + 18 * k, 22 * k);
            d.DrawRect(tag, UiTheme.Accent);
            Gfx.Text(d, UiTheme.HudWide, "RESULTS", tag.Position.X + 9 * k, Gfx.Mid(tag.GetCenter().Y, hs), hs, UiTheme.Text);
            Gfx.Text(d, UiTheme.Display, modeName, x0, y + 100 * k, ts, UiTheme.Text);
            // tier chip (right)
            if (showScore)
            {
            float tw = Gfx.TextW(UiTheme.HudWide, tl, hs);
            Gfx.Text(d, UiTheme.HudWide, "PLAYED AT", x1 - tw, y + 50 * k, UiTheme.Fs(11, k), UiTheme.Faint);
            Gfx.TextR(d, UiTheme.HudWide, tl, x1, y + 72 * k, hs, UiTheme.TierColor(tierId));
            RankEmblem.Draw(d, new Vector2(x1 - tw - 22 * k, y + 66 * k), 12 * k, tierId);
            }
            if (subUp != null) Gfx.TextR(d, UiTheme.HudWide, subUp, x1, y + 96 * k, UiTheme.Fs(11, k), UiTheme.Dim);
            d.DrawRect(new Rect2(x0, y + 120 * k, x1 - x0, 1), new Color(UiTheme.Text, 0.1f));

            // score block (left)
            float sy = y + 120 * k;
            if (showScore)
            {
            int ls = UiTheme.Fs(13, k), ss = UiTheme.Fs(140, k);
            Gfx.Text(d, UiTheme.HudWide, "SCORE", x0, sy + 40 * k, ls, UiTheme.Dim);
            float countT = Mathf.Clamp(t / 0.6f, 0, 1);
            string shown = countT >= 1 ? scoreText : ((int)(score * Gfx.EaseOut(countT))).ToString("#,0", Inv);
            Gfx.Text(d, UiTheme.Display, shown, x0 - 4 * k, sy + 170 * k, ss, UiTheme.Text);
            int ps = UiTheme.Fs(15, k);
            if (pb || first)
            {
                var cr = new Rect2(x0, sy + 202 * k, Gfx.TextW(UiTheme.HudWide, chip, ps) + 22 * k, 30 * k);
                float glow = pb ? 0.5f + 0.5f * Mathf.Sin(t * 5f) : 0f;
                d.DrawRect(cr, new Color(UiTheme.Good, 0.16f + 0.1f * glow));
                d.DrawRect(cr, UiTheme.Good, false, 1f);
                Gfx.Text(d, UiTheme.HudWide, chip, cr.Position.X + 11 * k, Gfx.Mid(cr.GetCenter().Y, ps), ps, UiTheme.Good);
                if (pb) Gfx.Text(d, UiTheme.Body, prevText, cr.End.X + 14 * k, Gfx.Mid(cr.GetCenter().Y, ps), ps, UiTheme.Dim);
            }
            else Gfx.Text(d, UiTheme.Body, prevText,
                x0, Gfx.Mid(sy + 217 * k, ps + 2), ps + 2, UiTheme.Dim);

            // rank emblem block (right)
            float ex = pr.Position.X + pr.Size.X * 0.73f, ey = sy + 90 * k, er = 54 * k;
            float pop = Gfx.EaseOutBack(Mathf.Clamp((t - 0.15f) / 0.45f, 0, 1));
            var tc = UiTheme.TierColor(badge);
            if (pop > 0.01f)
            {
                float ring = 0.5f + 0.5f * Mathf.Sin(t * 2.4f);
                d.DrawCircle(new Vector2(ex, ey), er * 1.35f, new Color(tc, 0.06f + 0.04f * ring));
                d.DrawArc(new Vector2(ex, ey), er * 1.35f, 0, Mathf.Tau, 64, new Color(tc, 0.35f), Mathf.Max(1, 2 * k), true);
                d.DrawSetTransform(new Vector2(ex, ey), 0, new Vector2(pop, pop));
                RankEmblem.Draw(d, Vector2.Zero, er, badge);
                d.DrawSetTransformMatrix(Transform2D.Identity);
            }
            float ty = ey + er * 1.35f;
            Gfx.TextC(d, UiTheme.HudWide, "PERFORMED AT", ex, ty + 22 * k, UiTheme.Fs(12, k), UiTheme.Dim);
            int bs = UiTheme.Fs(36, k);
            Gfx.TextC(d, UiTheme.Display, badgeName, ex, ty + 58 * k, bs, tc);
            Gfx.TextC(d, UiTheme.Body, badgeRanks, ex, ty + 82 * k, UiTheme.Fs(15, k), UiTheme.Dim);
            }

            // result lines
            float ly = sy + scoreH;
            d.DrawRect(new Rect2(x0, ly - 10 * k, x1 - x0, 1), new Color(UiTheme.Text, 0.1f));
            int rs = UiTheme.Fs(18, k);
            float colW = twoCol ? (x1 - x0 - 40 * k) / 2 : x1 - x0;
            for (int i = 0; i < resultLines.Count; i++)
            {
                int ci = twoCol ? i / lineRows : 0, ri = twoCol ? i % lineRows : i;
                float lx = x0 + ci * (colW + 40 * k), lyy = ly + ri * 40 * k + 20 * k;
                if (ri % 2 == 0) d.DrawRect(new Rect2(lx, lyy - 20 * k, colW, 40 * k), new Color(1, 1, 1, 0.025f));
                var (label, value) = resultLines[i];
                float vw = Mathf.Min(Gfx.TextW(UiTheme.Body, value, rs), colW * 0.6f);
                Gfx.TextFit(d, UiTheme.Body, label, lx + 12 * k, Gfx.Mid(lyy, rs), rs, UiTheme.Dim, colW - vw - 36 * k);
                Gfx.TextFit(d, UiTheme.Body, value, lx + colW - 12 * k - colW * 0.6f, Gfx.Mid(lyy, rs), rs, UiTheme.Text, colW * 0.6f, HorizontalAlignment.Right);
            }

            if (rl != null)
            {
                var rp = rpanel;
                Gfx.Plate(d, rp, 26 * k, new Color(0.06f, 0.1f, 0.14f, 0.97f), new Color(UiTheme.Text, 0.14f));
                d.DrawRect(new Rect2(rp.Position, new Vector2(60 * k, 3 * k)), UiTheme.Accent);
                foreach (var op in rl.Ops) op(d, rp.Position);
            }
        };
    }

    // ---------------- coach review (results) ----------------

    static readonly bool DevFakeButtons = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).Contains("--fakebuttons");

    static List<(string Label, Action Act)> ExtraButtons(TrainingMode m)
    {
        List<(string Label, Action Act)> list;
        try { list = m.ResultButtons().Take(3).ToList(); }
        catch (Exception ex) { GD.PushWarning($"ResultButtons failed: {ex.Message}"); list = new(); }
        // Dev-only layout check of the extra-button row (--dev --fakecoach --fakebuttons).
        if (list.Count == 0 && CoachData.Fake && DevFakeButtons) list.Add(("Use 0.190 in ValTrainer", () => { }));
        return list;
    }

    /// <summary>Focus for the next run of this drill: the first issue it trains (or the top issue).</summary>
    static CoachData.Focus? FocusFor(RunReview r, string modeKey)
    {
        var d = r.Issues.FirstOrDefault(i => (i.Drills ?? Array.Empty<string>()).Contains(modeKey)) ?? r.Issues.FirstOrDefault();
        return d == null ? null : CoachData.FocusFrom(d)! with { Mode = modeKey };
    }

    /// <summary>Pre-measured review panel: draw ops (relative to the panel origin) and its buttons.</summary>
    sealed class ReviewLayout
    {
        public float Height;
        public readonly List<Action<CanvasItem, Vector2>> Ops = new();
        public readonly List<(Rect2 R, string Label, string? Sub, Action Act, VButton.Look Look)> Buttons = new();
    }

    /// <summary>Lays the review out, dropping detail (cues, issues, strengths, summary lines) until it fits <paramref name="maxH"/>.</summary>
    static ReviewLayout LayoutReview(RunReview rv, float w, float maxH, float k)
    {
        (int Issues, int Cues, int Strengths, int SumLines)[] variants = { (3, 2, 3, 6), (3, 1, 2, 5), (2, 2, 2, 5), (2, 1, 1, 4), (1, 1, 1, 3), (1, 0, 0, 3) };
        ReviewLayout? l = null;
        foreach (var v in variants)
        {
            l = TryLayoutReview(rv, w, k, v.Issues, v.Cues, v.Strengths, v.SumLines);
            if (l.Height <= maxH) break;
        }
        return l!;
    }

    static ReviewLayout TryLayoutReview(RunReview rv, float w, float k, int maxIssues, int maxCues, int maxStrengths, int sumLines)
    {
        var L = new ReviewLayout();
        var body = UiTheme.Body;
        float pad = 36 * k, x = pad, tw = w - pad * 2;
        int hs = UiTheme.Fs(13, k), ss = UiTheme.Fs(17, k), cs = UiTheme.Fs(12, k), ts = UiTheme.Fs(23, k), es = UiTheme.Fs(14.5f, k);

        // header tag
        string caption = rv.Issues.Count > 0 ? "WHAT TO FIX NEXT RUN" : "NO CLEAR PROBLEMS THIS RUN";
        L.Ops.Add((d, o) =>
        {
            var tag = new Rect2(o.X + x, o.Y + 34 * k, Gfx.TextW(UiTheme.HudWide, "COACH REVIEW", hs) + 18 * k, 22 * k);
            d.DrawRect(tag, UiTheme.Accent);
            Gfx.Text(d, UiTheme.HudWide, "COACH REVIEW", tag.Position.X + 9 * k, Gfx.Mid(tag.GetCenter().Y, hs), hs, UiTheme.Text);
            Gfx.TextR(d, UiTheme.HudWide, caption, o.X + w - pad, Gfx.Mid(tag.GetCenter().Y, cs), cs, rv.Issues.Count > 0 ? UiTheme.Faint : UiTheme.Good);
        });
        float y = 78 * k;

        if (rv.Summary.Length > 0)
        {
            float sy = y;
            float h = CoachDraw.TextH(body, rv.Summary, tw, ss, sumLines);
            L.Ops.Add((d, o) => CoachDraw.Para(d, body, rv.Summary, o.X + x, o.Y + sy, tw, ss, UiTheme.Text, sumLines));
            y += h + 22 * k;
        }

        void Section(string title)
        {
            float sy = y;
            L.Ops.Add((d, o) =>
            {
                float cy = o.Y + sy + 8 * k;
                d.DrawRect(new Rect2(o.X + x, cy - 3 * k, 6 * k, 6 * k), UiTheme.Accent);
                float tw0 = Gfx.TextW(UiTheme.HudWide, title, cs);
                Gfx.Text(d, UiTheme.HudWide, title, o.X + x + 14 * k, Gfx.Mid(cy, cs), cs, UiTheme.Dim);
                d.DrawRect(new Rect2(o.X + x + 28 * k + tw0, cy, tw - 28 * k - tw0, 1), new Color(UiTheme.Text, 0.1f));
            });
            y += 28 * k;
        }

        var issues = rv.Issues.Take(maxIssues).ToList();
        if (issues.Count > 0)
        {
            Section(issues.Count == 1 ? "TOP ISSUE" : "TOP ISSUES");
            foreach (var iss in issues)
            {
                float iy = y;
                var sc = CoachSkills.SeverityColor(iss.Severity);
                string sev = CoachSkills.SeverityName(iss.Severity);
                string? drill = (iss.Drills ?? Array.Empty<string>()).FirstOrDefault(CoachData.HasDrill);
                float bw = drill != null ? 160 * k : 0;
                if (drill != null)
                {
                    var focusCue = CoachData.FocusFrom(iss);
                    L.Buttons.Add((new Rect2(x + tw - bw, iy, bw, 42 * k), "PRACTISE", CoachData.DrillName(drill), () => CoachData.StartDrill(drill, focusCue), VButton.Look.Secondary));
                }
                L.Ops.Add((d, o) =>
                {
                    float cy = o.Y + iy + 21 * k;
                    float cw = CoachDraw.Chip(d, sev, o.X + x, cy, k, sc, filled: iss.Severity == Severity.Major);
                    float avail = tw - cw - 10 * k - (bw > 0 ? bw + 12 * k : 0);
                    int fs = ts;
                    while (fs > UiTheme.Fs(18, k) && Gfx.TextW(UiTheme.Display, iss.Title, fs) > avail) fs--;
                    Gfx.TextFit(d, UiTheme.Display, iss.Title, o.X + x + cw + 10 * k, Gfx.Mid(cy, fs), fs, UiTheme.Text, avail);
                });
                y += 50 * k;
                if (iss.Evidence.Length > 0)
                {
                    float ey = y;
                    float h = CoachDraw.TextH(body, iss.Evidence, tw, es, 2);
                    L.Ops.Add((d, o) => CoachDraw.Para(d, body, iss.Evidence, o.X + x, o.Y + ey, tw, es, UiTheme.Dim, 2));
                    y += h + 6 * k;
                }
                foreach (var cue in (iss.Fixes ?? Array.Empty<string>()).Take(maxCues))
                {
                    float qy = y;
                    float h = CoachDraw.TextH(body, cue, tw - 20 * k, es, 2);
                    L.Ops.Add((d, o) =>
                    {
                        Gfx.Diamond(d, new Vector2(o.X + x + 5 * k, o.Y + qy + es * 0.62f), 3.5f * k, 3.5f * k, sc);
                        CoachDraw.Para(d, body, cue, o.X + x + 20 * k, o.Y + qy, tw - 20 * k, es, UiTheme.Text, 2);
                    });
                    y += h + 4 * k;
                }
                y += 16 * k;
            }
        }

        var good = rv.Strengths.Take(maxStrengths).ToList();
        if (good.Count > 0)
        {
            Section("WHAT WENT WELL");
            foreach (var g in good)
            {
                float gy = y;
                float h = CoachDraw.TextH(body, g, tw - 20 * k, es, 2);
                L.Ops.Add((d, o) =>
                {
                    Span<Vector2> pts = stackalloc Vector2[3];
                    float bx = o.X + x, by = o.Y + gy + es * 0.62f;
                    pts[0] = new(bx, by); pts[1] = new(bx + 4 * k, by + 4 * k); pts[2] = new(bx + 11 * k, by - 5 * k);
                    d.Polyline(pts, UiTheme.Good, Mathf.Max(1.5f, 2 * k), true);
                    CoachDraw.Para(d, body, g, o.X + x + 20 * k, o.Y + gy, tw - 20 * k, es, UiTheme.Text, 2);
                });
                y += h + 6 * k;
            }
        }

        y += 10 * k;
        L.Buttons.Add((new Rect2(x - 2 * k, y, 300 * k, 32 * k), "FULL COACH PROFILE  ›", null, () => Main.I.ShowProfile(), VButton.Look.Ghost));
        y += 32 * k + 26 * k;
        L.Height = y;
        return L;
    }
}
