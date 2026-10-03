using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;

namespace ValTrainer.Modes;

/// <summary>HUD info of one ability slot of a duelist kit (key, name, charges, state line, progress bar 0..1 or -1).</summary>
public readonly record struct KitSlot(AbilitySlot Slot, string Name, int Charges, int Max, string State, float Progress, bool Active,
    string? Key = null);

/// <summary>
/// The player's movement kit in a Mobility Entry drill (one subclass per agent, DuelistKits.cs). The base class reads the
/// ability keys (the imported VALORANT binds, default C / Q / E / X; RMB = alt fire; F = "use"), owns the gun-in-hand state
/// (<see cref="IGame.SetAbilityInHand"/>), runs dashes on top of <see cref="IGame.PlayerImpulse"/> (constant speed for the
/// dash time, then the exit speed; altitude held for horizontal air dashes) with a wall-tunnel guard, and audits every
/// dash / teleport end (no clipping into walls, not off the map). Distances and times are logged in dev runs.
/// </summary>
public abstract class DuelistKit
{
    protected readonly IGame G;
    protected readonly MobilityEntryMode D;
    protected float Now => G.Now;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    protected DuelistKit(IGame g, MobilityEntryMode d) { G = g; D = d; }

    public abstract string AgentName { get; }
    public abstract Color Color { get; }
    /// <summary>Short how-to (sentences separated by ". "), shown at the start of the drill.</summary>
    public abstract string HowTo { get; }
    public abstract IEnumerable<KitSlot> Slots { get; }
    public virtual string? Prompt => null;
    public virtual IEnumerable<string> StatusLines() => Array.Empty<string>();
    public virtual IEnumerable<(string Label, string Value)> ResultLines() => Array.Empty<(string, string)>();
    /// <summary>Extra thing worth aiming at when no enemy is in sight (Iso's energy orb).</summary>
    public virtual AimFocus? ExtraFocus => null;

    /// <summary>The kit did something this round (for the "used your ability" share and the how-to).</summary>
    public bool UsedThisRound;
    public int RoundsUsed, Uses;
    /// <summary>When the last ability movement ended (dash end, arrival, launch) — ability → kill timing.</summary>
    public float LastAbilityAt = -99f;
    public string LastAbilityTag = "";
    public int RoundKills;
    /// <summary>Dash / teleport ends that ended inside a wall or off the map (should stay 0).</summary>
    public int AuditFails, Audits;

    protected Vector3 Feet => G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0);

    protected void MarkUse(string tag)
    {
        Uses++;
        if (!UsedThisRound) { UsedThisRound = true; RoundsUsed++; }
        D.OnAbilityUsed(tag);
    }

    // ------------------------------------------------------------------ input

    readonly bool[] prevKey = new bool[4], devKey = new bool[4];
    bool prevAlt, prevUse, devAlt, devUse, devFire;
    protected readonly bool[] Pressed = new bool[4];
    protected bool AltPressed, UsePressed, FirePressed;

    /// <summary>Dev auto-play: presses for the next frame.</summary>
    public void DevPress(AbilitySlot s) => devKey[(int)s] = true;
    public void DevAlt() => devAlt = true;
    public void DevUse() => devUse = true;
    public void DevFire() => devFire = true;

    public static string KeyText(AbilitySlot s) => Main.I?.Valorant.AbilityBindText((int)s) ?? s.ToString();

    static bool AbilityDown(int slot)
    {
        var v = Main.I?.Valorant;
        if (v == null) return false;
        var mb = v.AbilityMouse[slot];
        if (mb != MouseButton.None) return Input.IsMouseButtonPressed(mb);
        var k = v.AbilityKeys[slot];
        return k != Key.None && Input.IsKeyPressed(k);
    }

    /// <summary>A movement key (honours the dev key override used by scripted / auto runs).</summary>
    protected static bool KeyHeld(Key k) => Mover.KeyOverride is { } f ? f(k) : k != Key.None && Input.IsKeyPressed(k);

    /// <summary>Called every running frame by the drill.</summary>
    public void Tick(float dt)
    {
        for (int i = 0; i < 4; i++)
        {
            bool down = AbilityDown(i);
            Pressed[i] = (down && !prevKey[i]) || devKey[i];
            prevKey[i] = down;
            devKey[i] = false;
        }
        bool alt = Input.IsMouseButtonPressed(MouseButton.Right);
        AltPressed = (alt && !prevAlt) || devAlt;
        prevAlt = alt; devAlt = false;
        bool use = Input.IsKeyPressed(Key.F);
        UsePressed = (use && !prevUse) || devUse;
        prevUse = use; devUse = false;
        FirePressed = G.FirePressed || devFire;
        devFire = false;
        if (G.Player.Dead) { OnPlayerDead(); return; }
        Update(dt);
        UpdateDash();
        UpdateGun();
    }

    protected abstract void Update(float dt);

    /// <summary>New round: charges back, everything cancelled, gun in hand, normal speed.</summary>
    public virtual void ResetRound()
    {
        dash = null;
        gunBackAt = -1f;
        if (gunHidden) { gunHidden = false; G.SetAbilityInHand(false, 0f); }
        G.MoveSpeedScale = 1f;
        RoundKills = 0;
        UsedThisRound = false;
    }

    protected virtual void OnPlayerDead()
    {
        dash = null;
        G.MoveSpeedScale = 1f;
    }

    // ------------------------------------------------------------------ kills / damage hooks

    /// <summary>The player killed a defender (it was damaged by you just now: Soul Harvest / flow-state orbs).</summary>
    public virtual void OnKill(BotCharacter bot, Vector3 at)
    {
        RoundKills++;
        if (RoundKills % 2 == 0) OnTwoKills();
    }

    /// <summary>"Resets a charge every two kills" (Tailwind, High Gear's slide, Gatecrash, Refract).</summary>
    protected virtual void OnTwoKills() { }

    /// <summary>A bot's hit on the player (before damage): true = absorbed (shield).</summary>
    public virtual bool Absorb(float dmg, Vector3 from) => false;

    /// <summary>A player bullet: distance to a kit object it hit (Iso's orb), +inf = none.</summary>
    public virtual float HitTest(Vector3 o, Vector3 d, float maxDist) => float.PositiveInfinity;

    /// <summary>Screen overlays (wind outline, intangible tint, shield edge).</summary>
    public virtual void Draw2D(CanvasItem c, Vector2 size, float k) { }

    // ------------------------------------------------------------------ gun in hand

    bool gunHidden;
    float gunBackAt = -1f, gunDraw;
    protected bool GunHidden => gunHidden;

    /// <summary>The ability takes the hands: the gun goes away (no firing / aiming) until <see cref="GunBack"/>.</summary>
    protected void HideGun()
    {
        gunBackAt = -1f;
        if (gunHidden) return;
        gunHidden = true;
        G.SetAbilityInHand(true);
    }

    /// <summary>The gun comes back after <paramref name="delay"/> s and can fire <paramref name="draw"/> s after that.</summary>
    protected void GunBack(float delay, float draw)
    {
        if (!gunHidden) return;
        gunBackAt = Now + Mathf.Max(0f, delay);
        gunDraw = draw;
        if (delay <= 0f) UpdateGun();
    }

    void UpdateGun()
    {
        if (!gunHidden || gunBackAt < 0f || Now < gunBackAt) return;
        gunHidden = false;
        gunBackAt = -1f;
        G.SetAbilityInHand(false, gunDraw);
    }

    // ------------------------------------------------------------------ movement helpers

    /// <summary>Direction of the movement keys relative to the view (zero = none held).</summary>
    protected Vector3 WishDir()
    {
        var v = Main.I?.Valorant;
        if (v == null) return Vector3.Zero;
        float y = Mathf.DegToRad(G.View.Yaw);
        var fwd = new Vector3(Mathf.Sin(y), 0, -Mathf.Cos(y));
        var right = new Vector3(Mathf.Cos(y), 0, Mathf.Sin(y));
        var w = Vector3.Zero;
        if (KeyHeld(v.KeyForward)) w += fwd;
        if (KeyHeld(v.KeyBack)) w -= fwd;
        if (KeyHeld(v.KeyRight)) w += right;
        if (KeyHeld(v.KeyLeft)) w -= right;
        return w.LengthSquared() > 1e-3f ? w.Normalized() : Vector3.Zero;
    }

    /// <summary>The way you're moving: the movement keys, else the current velocity, else straight ahead.</summary>
    protected Vector3 MoveDir()
    {
        var w = WishDir();
        if (w != Vector3.Zero) return w;
        var vel = G.Mover.Vel; vel.Y = 0;
        if (vel.Length() > 0.6f) return vel.Normalized();
        return FlatForward();
    }

    protected Vector3 FlatForward()
    {
        var f = G.View.Forward; f.Y = 0;
        return f.LengthSquared() > 1e-4f ? f.Normalized() : Vector3.Forward;
    }

    protected bool JumpHeld()
    {
        var v = Main.I?.Valorant;
        return v != null && (KeyHeld(v.KeyJump) || KeyHeld(v.KeyJump2));
    }

    // ------------------------------------------------------------------ dashes

    protected sealed class Dash
    {
        public Vector3 Dir, Start, Last;
        public float Speed, Exit, Time, T0, Expect;
        public bool HoldAltitude;
        /// <summary>Frames the dash lost speed against a wall (dev log).</summary>
        public int Scraped, Frames;
        public string Tag = "";
        public Action? OnEnd;
    }

    protected Dash? dash;
    public bool Dashing => dash != null;

    /// <summary>
    /// Dash <paramref name="dist"/> metres in <paramref name="time"/> seconds along <paramref name="dir"/> (its Y part rises or
    /// dives; flat dashes in the air hold their altitude when <paramref name="holdAltitude"/>), then leave at
    /// <paramref name="exitSpeed"/> along the dash. <paramref name="expect"/> = the researched distance (dev log).
    /// </summary>
    protected void StartDash(Vector3 dir, float dist, float time, float exitSpeed, string tag, float expect, bool holdAltitude, Action? onEnd = null)
    {
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : FlatForward();
        var f = Feet;
        dash = new Dash { Dir = dir, Speed = dist / time, Exit = exitSpeed, Time = time, T0 = Now, Expect = expect, Start = f, Last = f,
                          HoldAltitude = holdAltitude, Tag = tag, OnEnd = onEnd };
        ApplyDash(dash, time);
    }

    void ApplyDash(Dash d, float lockLeft)
    {
        var h = new Vector3(d.Dir.X, 0, d.Dir.Z) * d.Speed;
        float vy = d.Dir.Y * d.Speed;
        if (Mathf.Abs(vy) < 0.01f) vy = !G.PlayerGrounded && d.HoldAltitude ? -0.0001f : 0f;
        G.PlayerImpulse(new Vector3(h.X, vy, h.Z), lockLeft);
    }

    void UpdateDash()
    {
        if (dash is not { } d) return;
        var f = Feet;
        // Tunnel guard: a long frame at dash speed must never carry the body through a thin wall.
        if (Collision.Blocked(d.Last + Vector3.Up * 1.0f, f + Vector3.Up * 1.0f, G.Solid))
        {
            G.TeleportPlayer(d.Last);
            if (CmdLine.Dev) GD.Print($"[duelist] {d.Tag}: tunnel guard stopped the dash at {Fmt(d.Last)}");
            EndDash(d, d.Last);
            return;
        }
        float step = new Vector2(f.X - d.Last.X, f.Z - d.Last.Z).Length();
        if (d.Frames++ > 0 && step < 0.9f * d.Speed * new Vector2(d.Dir.X, d.Dir.Z).Length() * (float)G.World.GetProcessDeltaTime()) d.Scraped++;
        d.Last = f;
        if (G.Mover.SteerLockLeft > 1e-5f) { ApplyDash(d, G.Mover.SteerLockLeft); return; }
        EndDash(d, f);
    }

    void EndDash(Dash d, Vector3 f)
    {
        dash = null;
        var h = new Vector3(d.Dir.X, 0, d.Dir.Z);
        h = h.LengthSquared() > 1e-4f ? h.Normalized() * d.Exit : Vector3.Zero;
        G.PlayerImpulse(new Vector3(h.X, 0, h.Z), 0f);
        float dist = new Vector2(f.X - d.Start.X, f.Z - d.Start.Z).Length();
        float dt = Now - d.T0;
        LastAbilityAt = Now;
        LastAbilityTag = d.Tag;
        G.Event("ability_dash", dist, dt * 1000f);
        if (CmdLine.Dev)
        {
            string scrape = d.Scraped > 0 ? $", slowed by a wall {d.Scraped}/{d.Frames} frames" : "";
            GD.Print(string.Create(Inv, $"[duelist] {d.Tag}: {dist:0.00} m flat (rise {f.Y - d.Start.Y:+0.00;-0.00} m) in {dt:0.000} s " +
                                        $"(researched {d.Expect:0.#} m in {d.Time:0.00} s) from {Fmt(d.Start)} to {Fmt(f)}, dir {Fmt(d.Dir)}{scrape}"));
        }
        Audit(d.Tag);
        d.OnEnd?.Invoke();
    }

    /// <summary>Checks the body after a dash / teleport: not inside a wall, not off the map.</summary>
    protected void Audit(string tag)
    {
        Audits++;
        var f = Feet;
        bool clip = Collision.Overlaps(f, G.Solid, 0.25f) && G.Mover.BodyHeight > 1.5f;
        bool off = PlayerUtility.OffMap(f, G.Solid);
        if (!clip && !off) return;
        AuditFails++;
        if (CmdLine.Dev) GD.Print($"[duelist] AUDIT FAIL after {tag}: {(clip ? "inside a wall" : "off the map")} at {Fmt(f)}");
    }

    protected static string Fmt(Vector3 v) => string.Create(Inv, $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})");
    protected static string F1(float v) => v.ToString("0.0", Inv);

    /// <summary>Dev log line (dev runs only).</summary>
    protected static void Log(string s) { if (CmdLine.Dev) GD.Print("[duelist] " + s); }

    // ------------------------------------------------------------------ dev auto-play

    /// <summary>Dev auto-play at the last hidden spot before the site: do the entry. Returns true when done (then push).</summary>
    public virtual bool DevEntry(DuelistDev ctx, float dt) => true;

    /// <summary>Dev auto-play while pushing (after kills: Dismiss, Refract, shooting orbs …).</summary>
    public virtual void DevPush(DuelistDev ctx, float dt) { }

    /// <summary>Dev auto-play: the kit steers the movement keys itself right now (Reyna's Dismiss run).</summary>
    public virtual bool DevDrives => false;

    /// <summary>Dev auto-play: the kit wants the walk to start right away (Neon sprints from spawn).</summary>
    public virtual void DevRoundStart(DuelistDev ctx) { }
}
