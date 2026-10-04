using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

// Ability numbers: official VALORANT wiki (valorant.fandom.com ability pages; "game files" / patch-confirmed values) and
// patch notes to 13.07. Values the wiki marks as estimated (distances) or that nobody publishes (Drift fall speed, satchel
// force, re-equip times named only "Fast") are labelled "estimate" next to the constant.

/// <summary>
/// Jett. Tailwind (E): activate to prime (1 s windup, patch 7.04), then re-use within 7.5 s to dash in your movement
/// direction (forward when standing still): 0.45 s, ~11 m (wiki estimate), works in the air; the gun is back 0.2 s after the
/// dash. 1 charge, back every two kills. Updraft (Q): 1 charge (7.04), launches you ~5 m up (wiki estimate) over ~0.6 s.
/// Drift (passive): hold jump while falling to glide (fall speed 2.5 m/s: estimate).
/// </summary>
public sealed class JettKit : DuelistKit
{
    public const float Windup = 1f, Window = 7.5f, DashTime = 0.45f, DashDist = 11f, DashGunDraw = 0.2f;
    public const float UpdraftHeight = 5f, DriftFall = 2.5f;

    int tail = 1, updraft = 1;
    float primedAt = -1f, updraftAt = -99f;
    int dashes, updrafts, wasted, dashSeen;
    float driftTime;
    readonly List<float> updraftRise = new();

    public JettKit(IGame g, MobilityEntryMode d) : base(g, d)
    {
        g.PlayerLanded += () =>
        {
            if (Now - updraftAt > 3f) return;
            updraftAt = -99f;
            var a = G.Mover.LastAir;
            updraftRise.Add(a.FeetRise);
            Log($"jett updraft: feet rise {F1(a.FeetRise)} m (researched ~{UpdraftHeight} m), air {a.AirTime:0.00} s, drift {F1(driftTime)} s");
            Audit("jett updraft");
        };
    }

    public override string AgentName => "Jett";
    public override Color Color => Color.Color8(150, 220, 255);
    public override string HowTo =>
        $"{KeyText(AbilitySlot.E)} primes Tailwind (1 s), then {KeyText(AbilitySlot.E)} again within 7.5 s dashes 11 m the way you're moving. " +
        $"{KeyText(AbilitySlot.Q)}: Updraft (5 m up) · hold jump while falling to drift. " +
        "Prime it behind cover, dash past the angle, stop and shoot.";

    bool Priming => primedAt >= 0 && Now < primedAt + Windup;
    bool Empowered => primedAt >= 0 && Now >= primedAt + Windup && Now < primedAt + Windup + Window;

    public override void ResetRound()
    {
        base.ResetRound();
        tail = 1; updraft = 1; primedAt = -1f;
    }

    protected override void OnTwoKills()
    {
        if (tail >= 1 || primedAt >= 0) return;
        tail = 1;
        G.Banner("Tailwind recharged (2 kills)", UiTheme.Teal);
    }

    protected override void Update(float dt)
    {
        if (primedAt >= 0 && Now >= primedAt + Windup + Window)
        {
            primedAt = -1f;
            wasted++;
            G.Banner("Tailwind faded", UiTheme.Dim);
        }
        if (Pressed[(int)AbilitySlot.E] && !Dashing)
        {
            if (primedAt < 0)
            {
                if (tail > 0)
                {
                    tail--;
                    primedAt = Now;
                    DuelistSfx.Play(G, "wind_prime", 0.6f);
                    MarkUse("tailwind");
                }
                else DuelistSfx.Play(G, "empty", 0.6f);
            }
            else if (Empowered) DoDash(MoveDir());
        }
        if (Pressed[(int)AbilitySlot.Q] && !Dashing)
        {
            if (updraft > 0)
            {
                updraft--;
                updrafts++;
                updraftAt = Now;
                driftTime = 0;
                var v = G.Mover.Vel;
                G.PlayerImpulse(new Vector3(v.X, Mathf.Sqrt(2f * MovementTuning.Gravity * UpdraftHeight), v.Z));
                HideGun();
                GunBack(0.4f, 0.2f); // re-equip "Fast": estimate
                DuelistSfx.Play(G, "updraft", 0.7f);
                LastAbilityAt = Now;
                LastAbilityTag = "updraft";
                MarkUse("updraft");
            }
            else DuelistSfx.Play(G, "empty", 0.6f);
        }
        // Drift: hold jump while falling.
        if (!G.PlayerGrounded && !Dashing && JumpHeld() && G.PlayerVerticalSpeed < -DriftFall)
        {
            var v = G.Mover.Vel;
            G.PlayerImpulse(new Vector3(v.X, -DriftFall, v.Z));
            driftTime += dt;
        }
    }

    void DoDash(Vector3 dir)
    {
        primedAt = -1f;
        dashes++;
        if (D.AnyDefenderSeesPlayer) dashSeen++;
        HideGun();
        DuelistSfx.Play(G, "wind_dash", 0.8f);
        DuelistBurst.Spawn(G, Feet + Vector3.Up * 0.6f, new Color(0.85f, 0.95f, 1f), 1.2f, 0.35f, 0f);
        StartDash(dir, DashDist, DashTime, Mover.RunSpeed, "jett tailwind", DashDist, holdAltitude: true, () => GunBack(0f, DashGunDraw));
        D.OnAbilityUsed("dash");
    }

    public override IEnumerable<KitSlot> Slots
    {
        get
        {
            yield return new KitSlot(AbilitySlot.Q, "Updraft", updraft, 1, updraft > 0 ? "READY" : "USED", -1f, Now - updraftAt < 0.6f);
            string st = Dashing ? "DASHING" : Priming ? "PRIMING…" : Empowered ? $"DASH READY  {primedAt + Windup + Window - Now:0.0}s" : tail > 0 ? "READY" : "NO CHARGE";
            float prog = Priming ? (Now - primedAt) / Windup : Empowered ? (primedAt + Windup + Window - Now) / Window : -1f;
            yield return new KitSlot(AbilitySlot.E, "Tailwind", tail, 1, st, prog, Priming || Empowered || Dashing);
        }
    }

    public override string? Prompt => Empowered ? $"{KeyText(AbilitySlot.E)}: dash (the way you're moving)" : Priming ? "Tailwind priming…" : null;

    public override void Draw2D(CanvasItem c, Vector2 size, float k)
    {
        if (!Empowered && !Dashing) return;
        // Wispy wind outline at the screen edges while empowered.
        float a = Dashing ? 0.35f : 0.16f + 0.06f * Mathf.Sin(Now * 5f);
        var col = new Color(0.85f, 0.95f, 1f, a);
        float w = 26 * k;
        Gfx.HGradient(c, new Rect2(0, 0, w * 3, size.Y), col, new Color(col, 0));
        Gfx.HGradient(c, new Rect2(size.X - w * 3, 0, w * 3, size.Y), new Color(col, 0), col);
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Tailwind dashes", $"{dashes}" + (dashSeen > 0 ? $"  ({dashSeen} spotted)" : ""));
        if (wasted > 0) yield return ("Tailwind wasted", $"{wasted} (window ran out)");
        if (updrafts > 0) yield return ("Updrafts", updraftRise.Count > 0 ? $"{updrafts}  (avg {updraftRise.Average():0.0} m up)" : updrafts.ToString());
    }

    // ---- dev auto-play: prime at the hidden spot (round 2: Updraft test first), then dash into the site.
    public override bool DevEntry(DuelistDev c, float dt)
    {
        switch (c.Step)
        {
            case 0:
                if (D.RoundIndex == 2 && updraft > 0) { DevPress(AbilitySlot.Q); c.Next(); break; }
                c.Next(2);
                break;
            case 1: // updraft test: hold jump to drift down
                c.Hold(Main.I.Valorant.KeyJump);
                if (c.StepT > 0.3f && G.PlayerGrounded) { c.Release(Main.I.Valorant.KeyJump); c.Next(); }
                break;
            case 2:
                DevPress(AbilitySlot.E);
                c.Next();
                break;
            case 3:
                if (c.StepT < Windup + 0.05f) break;
                c.LookDir(c.BestDashDir(DashDist));
                c.Hold(Main.I.Valorant.KeyForward);
                c.Next();
                break;
            case 4:
                if (c.StepT < 0.12f) break;
                DevPress(AbilitySlot.E);
                c.Next();
                break;
            case 5:
                if (c.StepT > 0.1f && !Dashing) return true;
                break;
        }
        return false;
    }
}

/// <summary>
/// Neon. High Gear (E): sprint at 9.11 m/s (+35% over the knife's 6.75: game files, patch 8.11) with no gun; airborne speed
/// is the knife speed (patch 12.09). Energy 100, drains 6.25/s (16 s of sprint, patch 11.08), refills to full in 60 s;
/// needs 2 to start. Slide (RMB while sprinting, after the 0.7 s activation, moving forward or sideways): 7 m (wiki
/// estimate) in 0.6 s, ends the sprint; the gun is out 0.2 s into the slide (patch 8.11 equip buffer) and fires with
/// crouch-walk accuracy (patch 9.11). 1 slide (9.11), back every two kills. Gun after stopping a sprint: 0.5 s ("Fast": estimate).
/// </summary>
public sealed class NeonKit : DuelistKit
{
    public const float SprintSpeed = 9.11f, AirSpeed = 6.75f, Activation = 0.7f, SlideTime = 0.6f, SlideDist = 7f;
    public const float Drain = 6.25f, Regen = 100f / 60f, SprintGunDraw = 0.5f;

    float energy = 100f, activeAt = -1f, sprintTime, sprintDist;
    bool active, sliding;
    int slides = 1, slidesUsed, sprints, outOfEnergy;
    Vector3 lastFeet;

    public NeonKit(IGame g, MobilityEntryMode d) : base(g, d) { }

    public override string AgentName => "Neon";
    public override Color Color => Color.Color8(80, 170, 255);
    public override string HowTo =>
        $"{KeyText(AbilitySlot.E)}: High Gear — sprint at 9.1 m/s, but no gun. " +
        "While sprinting, RMB slides 7 m (once a round, back every two kills); your gun is out 0.2 s into the slide. " +
        "Sprint up to the angle, slide wide past it, shoot from the slide.";

    public override void ResetRound()
    {
        base.ResetRound();
        energy = 100f; active = sliding = false; slides = 1; activeAt = -1f;
    }

    protected override void OnPlayerDead() { base.OnPlayerDead(); active = sliding = false; }

    protected override void OnTwoKills()
    {
        if (slides >= 1) return;
        slides = 1;
        G.Banner("Slide recharged (2 kills)", UiTheme.Teal);
    }

    bool SlideReady => active && Now - activeAt >= Activation && slides > 0;

    protected override void Update(float dt)
    {
        if (active)
        {
            energy -= Drain * dt;
            sprintTime += dt;
            var f = Feet;
            if (G.PlayerGrounded) sprintDist += new Vector2(f.X - lastFeet.X, f.Z - lastFeet.Z).Length();
            lastFeet = f;
            if (energy <= 0f) { energy = 0f; outOfEnergy++; Stop(); G.Banner("Out of energy", UiTheme.Warn); }
        }
        else energy = Mathf.Min(100f, energy + Regen * dt);

        if (Pressed[(int)AbilitySlot.E] && !sliding)
        {
            if (active) Stop();
            else if (energy >= 2f) Start();
            else DuelistSfx.Play(G, "empty", 0.6f);
        }
        if (active)
        {
            // Sprint on the ground; airborne speed = the knife's (patch 12.09: jumping gives no sprint bonus).
            G.MoveSpeedScale = (G.PlayerGrounded ? SprintSpeed : AirSpeed) / Mover.RunSpeed;
            if (!G.PlayerGrounded && G.Mover.Speed > AirSpeed + 0.05f)
            {
                var v = G.Mover.Vel; v.Y = 0;
                G.PlayerImpulse(v.Normalized() * AirSpeed);
            }
        }
        if (AltPressed && active)
        {
            var dir = MoveDir();
            if (!SlideReady) { if (slides <= 0) DuelistSfx.Play(G, "empty", 0.6f); }
            else if (!G.PlayerGrounded || G.Mover.Speed < 1f) { }
            else if (dir.Dot(FlatForward()) < -0.3f) { } // forward or sideways only
            else Slide(dir);
        }
        if (sliding)
        {
            // Crouched for the slide: lower camera and crouch-walk accuracy (patch 9.11).
            G.Mover.Crouching = true;
            G.Mover.EyeHeight += (Mover.CrouchEye - G.Mover.EyeHeight) * (1f - Mathf.Exp(-14f * dt));
        }
    }

    void Start()
    {
        active = true;
        activeAt = Now;
        sprints++;
        lastFeet = Feet;
        HideGun();
        DuelistSfx.Play(G, "sprint_on", 0.7f);
        MarkUse("sprint");
    }

    void Stop()
    {
        active = false;
        G.MoveSpeedScale = 1f;
        GunBack(0f, SprintGunDraw);
    }

    void Slide(Vector3 dir)
    {
        slides--;
        slidesUsed++;
        active = false;
        G.MoveSpeedScale = 1f;
        sliding = true;
        GunBack(0.2f, 0f);
        DuelistSfx.Play(G, "slide", 0.8f);
        StartDash(dir, SlideDist, SlideTime, Mover.RunSpeed, "neon slide", SlideDist, holdAltitude: false, () => sliding = false);
        D.OnAbilityUsed("slide");
    }

    public override IEnumerable<KitSlot> Slots
    {
        get
        {
            string st = active ? (Now - activeAt < Activation ? "ACTIVATING…" : $"SPRINTING  {energy:0}%") : energy >= 2f ? $"READY  {energy:0}%" : "RECHARGING";
            yield return new KitSlot(AbilitySlot.E, "High Gear", 0, 0, st, energy / 100f, active);
            yield return new KitSlot(AbilitySlot.E, "Slide", slides, 1, sliding ? "SLIDING" : SlideReady ? "RMB TO SLIDE" : slides > 0 ? "SPRINT FIRST" : "USED", -1f, sliding, "RMB");
        }
    }

    public override string? Prompt => SlideReady ? "RMB: slide (forward or sideways)" : active ? $"No gun while sprinting — {KeyText(AbilitySlot.E)} to stop" : null;

    public override void Draw2D(CanvasItem c, Vector2 size, float k)
    {
        if (!active && !sliding) return;
        var col = new Color(0.35f, 0.7f, 1f, sliding ? 0.3f : 0.14f);
        Gfx.VGradient(c, new Rect2(0, size.Y - 90 * k, size.X, 90 * k), new Color(col, 0), col);
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        if (sprintTime > 0.5f) yield return ("High Gear sprint", $"{sprints}×, {sprintTime:0.0} s total");
        yield return ("Slides", slidesUsed.ToString());
        if (outOfEnergy > 0) yield return ("Out of energy", outOfEnergy.ToString());
    }

    // ---- dev auto-play: sprint from spawn, slide into the site at the hidden spot.
    public override void DevRoundStart(DuelistDev c) => DevPress(AbilitySlot.E);

    public override bool DevEntry(DuelistDev c, float dt)
    {
        switch (c.Step)
        {
            case 0:
                if (!active && !sliding) DevPress(AbilitySlot.E);
                c.LookDir(c.BestDashDir(SlideDist + 2f));
                c.Hold(Main.I.Valorant.KeyForward);
                c.Next();
                break;
            case 1:
                if (c.StepT < 0.35f || !SlideReady) { if (c.StepT > 2f) return true; break; }
                Log(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"neon sprint: ground speed {G.Mover.Speed:0.00} m/s (researched {SprintSpeed} m/s), energy {energy:0}"));
                DevAlt();
                c.Next();
                break;
            case 2:
                if (c.StepT > 0.1f && !Dashing) return true;
                break;
        }
        return false;
    }
}

/// <summary>
/// Raze. Blast Pack (Q, 2 charges): thrown instantly, sticks to the first surface, re-use detonates it (auto after 5 s);
/// the blast knocks you away from it — no self-damage (damage needs 1.5 s of arming and hits enemies only). Radii 2 m
/// (full) / 5 m (edge) per game files. The launch force isn't published: 12 m/s along pack → you with at least 12 m/s up,
/// 40% at the 5 m edge, added to your run with the flat part capped at 12 m/s (estimates for a ~3.5 m high, ~12–14 m long
/// boost from a pack at your feet; patch 8.11 slowed the horizontal part). Throw speed 17 m/s (estimate). The gun is back 0.7 s after a throw (game files unequip time). The
/// blast is loud: defenders who hear it may turn toward it.
/// </summary>
public sealed class RazeKit : DuelistKit
{
    public const float ThrowSpeed = 17f, MaxLife = 5f, Inner = 2f, Outer = 5f, Blast = 12f, MinUp = 12f, MaxHorizontal = 12f;

    sealed class Pack { public Vector3 Pos, Vel; public bool Stuck; public float Born; public DuelistVisual? Vis; }
    Pack? pack;
    int charges = 2, thrown, boosts, airKills;
    float launchedAt = -99f;
    readonly List<float> boostDist = new(), boostRise = new();

    public RazeKit(IGame g, MobilityEntryMode d) : base(g, d)
    {
        g.PlayerLanded += () =>
        {
            if (Now - launchedAt > 4f) return;
            launchedAt = -99f;
            var a = G.Mover.LastAir;
            boostDist.Add(a.Distance);
            boostRise.Add(a.FeetRise);
            Log($"raze boost: {F1(a.Distance)} m far, {F1(a.FeetRise)} m high, {a.AirTime:0.00} s in the air");
            Audit("raze blast pack");
        };
    }

    public override string AgentName => "Raze";
    public override Color Color => Color.Color8(255, 150, 60);
    public override string HowTo =>
        $"{KeyText(AbilitySlot.Q)} throws a Blast Pack that sticks where it lands; {KeyText(AbilitySlot.Q)} again sets it off and launches you (no self-damage). " +
        "Throw it at your feet, or let it fall behind you as you run, for a long boost. " +
        "You're inaccurate in the air: land, then shoot.";

    public override void ResetRound()
    {
        base.ResetRound();
        pack?.Vis?.Remove();
        pack = null;
        charges = 2;
    }

    protected override void Update(float dt)
    {
        if (pack is { } p)
        {
            if (!p.Stuck)
            {
                if (PlayerUtility.Step(ref p.Pos, ref p.Vel, PlayerUtility.WorldGravity, dt, G.Solid, out _)) { p.Stuck = true; p.Vel = Vector3.Zero; }
                else if (PlayerUtility.OffMap(p.Pos, G.Solid)) { p.Vis?.Remove(); pack = null; }
            }
            if (pack != null)
            {
                if (p.Vis != null && GodotObject.IsInstanceValid(p.Vis)) p.Vis.GlobalPosition = p.Pos;
                if (Now - p.Born > MaxLife - 1f) p.Vis?.SetBlink(10f);
                if (Now - p.Born >= MaxLife) Detonate();
            }
        }
        if (Pressed[(int)AbilitySlot.Q])
        {
            if (pack != null) Detonate();
            else if (charges > 0) Throw();
            else DuelistSfx.Play(G, "empty", 0.6f);
        }
    }

    void Throw()
    {
        charges--;
        thrown++;
        var dir = G.View.Forward;
        var at = PlayerUtility.HandPoint(G, dir);
        var v = G.Mover.Vel;
        pack = new Pack { Pos = at, Vel = dir * ThrowSpeed + new Vector3(v.X, G.PlayerVerticalSpeed, v.Z), Born = Now, Vis = DuelistVisual.Satchel(G, at) };
        HideGun();
        GunBack(0.4f, 0.3f);
        DuelistSfx.Play(G, "satchel_throw", 0.7f);
        MarkUse("satchel");
    }

    void Detonate()
    {
        if (pack is not { } p) return;
        pack = null;
        p.Vis?.Remove();
        DuelistBurst.Spawn(G, p.Pos, new Color(1f, 0.6f, 0.2f), 3.2f, 0.5f, 6f);
        ScanWave.Spawn(G, p.Pos, Outer, new Color(1f, 0.55f, 0.2f));
        DuelistSfx.PlayAt(G, "satchel_blast", p.Pos, 1f, maxRange: 60f, floor: 0.6f);
        D.Alert(p.Pos, 50f);
        var center = Feet + Vector3.Up * 0.9f;
        float d = center.DistanceTo(p.Pos);
        if (d > Outer || !G.LineOfSight(p.Pos, center)) { Log($"raze blast: out of range ({F1(d)} m)"); return; }
        float f = d <= Inner ? 1f : Mathf.Lerp(1f, 0.4f, (d - Inner) / (Outer - Inner));
        var dir = d > 0.05f ? (center - p.Pos) / d : Vector3.Up;
        var blast = dir * Blast * f;
        float up = Mathf.Max(blast.Y, MinUp * f);
        var h = new Vector3(G.Mover.Vel.X + blast.X, 0, G.Mover.Vel.Z + blast.Z);
        if (h.Length() > MaxHorizontal) h = h.Normalized() * MaxHorizontal;
        G.PlayerImpulse(new Vector3(h.X, up, h.Z), 0.1f);
        boosts++;
        launchedAt = Now;
        LastAbilityAt = Now;
        LastAbilityTag = "satchel";
        G.Event("ability_launch", h.Length(), up);
        Log(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"raze blast at {d:0.0} m: launch {h.Length():0.0} m/s flat + {up:0.0} m/s up (force ×{f:0.00})"));
        D.OnAbilityUsed("launch");
    }

    public override void OnKill(BotCharacter bot, Vector3 at)
    {
        base.OnKill(bot, at);
        if (!G.PlayerGrounded) airKills++;
    }

    public override IEnumerable<KitSlot> Slots
    {
        get
        {
            string st = pack == null ? (charges > 0 ? "READY" : "NO CHARGES") : pack.Stuck ? $"STUCK · {KeyText(AbilitySlot.Q)} TO BLOW" : "IN FLIGHT";
            yield return new KitSlot(AbilitySlot.Q, "Blast Pack", charges, 2, st, pack != null ? 1f - (Now - pack.Born) / MaxLife : -1f, pack != null);
        }
    }

    public override string? Prompt => pack != null ? $"{KeyText(AbilitySlot.Q)}: detonate" : null;

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Packs thrown / boosts", $"{thrown} / {boosts}");
        if (boostDist.Count > 0) yield return ("Boost distance", $"avg {boostDist.Average():0.0} m · best {boostDist.Max():0.0} m");
        if (airKills > 0) yield return ("Kills in the air", airKills.ToString());
    }

    // ---- dev auto-play: drop it at your feet, walk past it toward the site, blow it when it's behind you.
    public override bool DevEntry(DuelistDev c, float dt)
    {
        switch (c.Step)
        {
            case 0:
                c.LookFlat(c.SitePoint(6f));
                c.Pitch(-80f);
                c.Next();
                break;
            case 1:
                DevPress(AbilitySlot.Q);
                c.Next();
                break;
            case 2:
                if (pack == null) return true;
                if (!pack.Stuck) { if (c.StepT > 1.5f) { DevPress(AbilitySlot.Q); return true; } break; }
                c.LookFlat(c.SitePoint(8f));
                c.Pitch(0f);
                c.Hold(Main.I.Valorant.KeyForward);
                c.Next();
                break;
            case 3:
                if (pack == null) return true;
                if ((pack.Pos - Feet).Dot(D.SiteAxis) < -0.9f || c.StepT > 0.8f) { DevPress(AbilitySlot.Q); c.Next(); }
                break;
            case 4:
                if (c.StepT > 0.3f && G.PlayerGrounded) return true;
                if (c.StepT > 3f) return true;
                break;
        }
        return false;
    }
}

/// <summary>
/// Reyna. Soul Harvest: an enemy that dies within 3 s of taking your damage drops a soul orb for 3 s (patch 12.02). Orbs
/// must be in line of sight; the oldest is used. Dismiss (E): eat an orb, intangible for 1.5 s (no damage, no gun) at a
/// 12 m/s top speed (patch 8.11); re-use ends it early; the gun is back 0.6 s later (game files unequip). Devour (Q): eat an
/// orb to heal 50 over 3 s (overheal up to 150 with shields, lasting 10 s: patch 12.02). Devour and Dismiss share 2 charges.
/// </summary>
public sealed class ReynaKit : DuelistKit
{
    public const float OrbLife = 3f, DismissTime = 1.5f, DismissSpeed = 12f, DismissGunDraw = 0.6f, DevourHeal = 50f, DevourTime = 3f, Overheal = 10f;

    sealed class Orb { public Vector3 Pos; public float Born; public DuelistVisual? Vis; }
    readonly List<Orb> orbs = new();
    int charges = 2, orbsDropped, dismisses, devours;
    bool dismissing;
    float dismissUntil, healLeft, healAcc, overhealUntil = -1f, healed, dismissTop;
    Vector3 dismissFrom;
    readonly List<float> dismissDist = new();

    public ReynaKit(IGame g, MobilityEntryMode d) : base(g, d) { }

    public override string AgentName => "Reyna";
    public override Color Color => Color.Color8(200, 90, 255);
    public override string HowTo =>
        "Kills drop a soul orb for 3 s. " +
        $"{KeyText(AbilitySlot.E)}: Dismiss — eat it, become untouchable and run at 12 m/s for 1.5 s (no gun). " +
        $"{KeyText(AbilitySlot.Q)}: Devour — eat it to heal 50. " +
        "Win the first duel, then Dismiss out of the trade or onto a new angle.";

    public override void ResetRound()
    {
        base.ResetRound();
        foreach (var o in orbs) o.Vis?.Remove();
        orbs.Clear();
        charges = 2; dismissing = false; healLeft = 0; overhealUntil = -1f;
    }

    protected override void OnPlayerDead() { base.OnPlayerDead(); dismissing = false; healLeft = 0; }

    public override void OnKill(BotCharacter bot, Vector3 at)
    {
        base.OnKill(bot, at);
        var p = at + Vector3.Up * 1.0f;
        orbs.Add(new Orb { Pos = p, Born = Now, Vis = DuelistVisual.Make(G, p, Color, 0.6f, pulseHz: 2f, light: 1.5f) });
        orbsDropped++;
        DuelistSfx.PlayAt(G, "soul_orb", p, 0.8f);
    }

    Orb? Viable => orbs.OrderBy(o => o.Born).FirstOrDefault(o => G.LineOfSight(G.View.Eye, o.Pos));

    Orb Consume(Orb o)
    {
        orbs.Remove(o);
        o.Vis?.Remove();
        charges--;
        return o;
    }

    protected override void Update(float dt)
    {
        for (int i = orbs.Count - 1; i >= 0; i--)
        {
            var o = orbs[i];
            if (Now - o.Born > OrbLife - 0.8f) o.Vis?.SetBlink(6f);
            if (Now - o.Born >= OrbLife) { o.Vis?.Remove(); orbs.RemoveAt(i); }
        }
        if (Pressed[(int)AbilitySlot.E])
        {
            if (dismissing) EndDismiss();
            else if (charges > 0 && Viable is { } o) { Consume(o); StartDismiss(); }
            else DuelistSfx.Play(G, "empty", 0.6f);
        }
        if (Pressed[(int)AbilitySlot.Q] && !dismissing)
        {
            if (charges > 0 && Viable is { } o) { Consume(o); StartDevour(); }
            else DuelistSfx.Play(G, "empty", 0.6f);
        }
        if (dismissing) dismissTop = Mathf.Max(dismissTop, G.Mover.Speed);
        if (dismissing && Now >= dismissUntil) EndDismiss();

        // Devour: 1 HP every 0.06 s; above 100 HP is overheal (up to 150 with shields), lost after 10 s.
        var pl = G.Player;
        if (healLeft > 0)
        {
            healAcc += dt * DevourHeal / DevourTime;
            while (healAcc >= 1f && healLeft > 0)
            {
                healAcc -= 1f; healLeft -= 1f;
                if (pl.Hp < 100f || pl.Hp + pl.Shield < 150f) { pl.Hp += 1f; healed += 1f; }
            }
        }
        if (overhealUntil >= 0 && Now > overhealUntil && pl.Hp > 100f) pl.Hp = Mathf.Max(100f, pl.Hp - 25f * dt);
    }

    void StartDismiss()
    {
        dismissing = true;
        dismisses++;
        dismissUntil = Now + DismissTime;
        dismissFrom = Feet;
        dismissTop = 0f;
        G.Player.ProtectedUntil = dismissUntil;
        G.MoveSpeedScale = DismissSpeed / Mover.RunSpeed;
        HideGun();
        DuelistSfx.Play(G, "dismiss", 0.8f);
        MarkUse("dismiss");
    }

    void EndDismiss()
    {
        dismissing = false;
        G.Player.ProtectedUntil = Mathf.Min(G.Player.ProtectedUntil, Now);
        G.MoveSpeedScale = 1f;
        GunBack(0f, DismissGunDraw);
        var f = Feet;
        float dist = new Vector2(f.X - dismissFrom.X, f.Z - dismissFrom.Z).Length();
        dismissDist.Add(dist);
        LastAbilityAt = Now;
        LastAbilityTag = "dismiss";
        G.Event("ability_dismiss", dist);
        Log($"reyna dismiss: {F1(dist)} m in {DismissTime - Mathf.Max(0, dismissUntil - Now):0.00} s, top speed {F1(dismissTop)} m/s (researched {DismissSpeed} m/s → ≤{DismissSpeed * DismissTime:0} m)");
        Audit("reyna dismiss");
    }

    void StartDevour()
    {
        devours++;
        Log($"reyna devour at {G.Player.Hp:0} HP / {G.Player.Shield:0} shield");
        healLeft = DevourHeal;
        healAcc = 0;
        overhealUntil = Now + Overheal;
        HideGun();
        GunBack(0.25f, 0.25f); // devour cast: estimate
        DuelistSfx.Play(G, "devour", 0.8f);
        LastAbilityAt = Now;
        MarkUse("devour");
    }

    public override IEnumerable<KitSlot> Slots
    {
        get
        {
            bool orb = Viable != null;
            yield return new KitSlot(AbilitySlot.Q, "Devour", charges, 2, healLeft > 0 ? "HEALING" : orb && charges > 0 ? "ORB READY" : "NEEDS A SOUL ORB", -1f, healLeft > 0);
            yield return new KitSlot(AbilitySlot.E, "Dismiss", charges, 2, dismissing ? $"INTANGIBLE  {dismissUntil - Now:0.0}s" : orb && charges > 0 ? "ORB READY" : "NEEDS A SOUL ORB",
                dismissing ? (dismissUntil - Now) / DismissTime : -1f, dismissing);
        }
    }

    public override string? Prompt => dismissing ? "Intangible — reposition!" : charges > 0 && Viable != null
        ? $"Soul orb: {KeyText(AbilitySlot.E)} Dismiss · {KeyText(AbilitySlot.Q)} Devour" : null;

    public override void Draw2D(CanvasItem c, Vector2 size, float k)
    {
        if (!dismissing) return;
        var col = new Color(0.55f, 0.2f, 0.85f, 0.28f);
        c.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.25f, 0.05f, 0.35f, 0.18f));
        float w = 160 * k;
        Gfx.HGradient(c, new Rect2(0, 0, w, size.Y), col, new Color(col, 0));
        Gfx.HGradient(c, new Rect2(size.X - w, 0, w, size.Y), new Color(col, 0), col);
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Soul orbs dropped", orbsDropped.ToString());
        yield return ("Dismiss / Devour", $"{dismisses} / {devours}");
        if (dismissDist.Count > 0) yield return ("Dismiss distance", $"avg {dismissDist.Average():0.0} m");
        if (healed > 0) yield return ("Healed by Devour", $"{healed:0} HP");
    }

    public override bool DevDrives => dismissing;

    // ---- dev auto-play: after a kill, odd rounds Dismiss and run the clearest way, even rounds Devour.
    public override void DevPush(DuelistDev c, float dt)
    {
        if (dismissing) { c.Hold(Main.I.Valorant.KeyForward); return; }
        if (charges <= 0 || healLeft > 0 || Viable == null) return;
        if (D.RoundIndex % 2 == 1)
        {
            c.LookDir(c.BestDashDir(DismissSpeed * DismissTime));
            c.Hold(Main.I.Valorant.KeyForward);
            DevPress(AbilitySlot.E);
        }
        else DevPress(AbilitySlot.Q);
    }
}

/// <summary>
/// Yoru. Gatecrash (E, 2 charges, back every two kills): equip 0.8 s; LMB sends a rift tether along the floor at 8 m/s
/// (patch 4.04: 800 cm/s) that scrapes along walls and slows against them; RMB places it up to 3 m ahead. It lasts 20 s
/// (patch 13.01). Re-use teleports: 0.5 s wind-up, then 0.7 s to arrive (gun ready after that; game files). F ("use")
/// fakes the teleport. The teleport cue carries 42.5 m: defenders who hear the arrival may turn toward it. Gun back 0.5 s
/// after sending / placing (game files unequip).
/// </summary>
public sealed class YoruKit : DuelistKit
{
    public const float Equip = 0.8f, TetherSpeed = 8f, PlaceRange = 3f, TetherLife = 20f, TeleWindup = 0.5f, TeleArrive = 0.7f;
    public const float SendGunDraw = 0.5f, HearRange = 42.5f;

    enum St { Ready, Equipping, Equipped, Windup }
    St st;
    float stT;
    sealed class Tether { public Vector3 Pos, Dir, From; public float Speed, Born; public bool Moving; public DuelistVisual? Vis; }
    Tether? tether;
    int charges = 2, sent, placed, teleports, fakes, caught;
    float arrivedAt = -99f;
    readonly List<float> teleDist = new();

    public YoruKit(IGame g, MobilityEntryMode d) : base(g, d) { }

    public override string AgentName => "Yoru";
    public override Color Color => Color.Color8(90, 140, 255);
    public override string HowTo =>
        $"{KeyText(AbilitySlot.E)} equips Gatecrash (0.8 s): LMB sends the tether along the floor, RMB places it 3 m ahead. " +
        $"{KeyText(AbilitySlot.E)} again teleports to it (0.5 s wind-up, your gun is ready 0.7 s after you arrive) · {UseKey} fakes it. " +
        "Defenders hear the arrival: send it to an angle they don't hold.";

    public override void ResetRound()
    {
        base.ResetRound();
        tether?.Vis?.Remove();
        tether = null;
        st = St.Ready;
        charges = 2;
    }

    protected override void OnPlayerDead()
    {
        base.OnPlayerDead();
        if (Now - arrivedAt < 2f) { caught++; arrivedAt = -99f; }
        st = St.Ready;
    }

    protected override void OnTwoKills()
    {
        if (charges >= 2) return;
        charges++;
        G.Banner("Gatecrash recharged (2 kills)", UiTheme.Teal);
    }

    void Set(St s) { st = s; stT = 0; }

    protected override void Update(float dt)
    {
        stT += dt;
        if (tether is { } t)
        {
            if (t.Moving) MoveTether(t, dt);
            if (t.Vis != null && GodotObject.IsInstanceValid(t.Vis)) t.Vis.GlobalPosition = t.Pos + Vector3.Up * 0.25f;
            if (Now - t.Born > TetherLife && st != St.Windup) { t.Vis?.Remove(); tether = null; G.Banner("Tether faded", UiTheme.Dim); }
        }
        bool key = Pressed[(int)AbilitySlot.E];
        switch (st)
        {
            case St.Ready:
                if (key)
                {
                    if (tether != null) { Set(St.Windup); HideGun(); DuelistSfx.Play(G, "teleport_out", 0.7f); }
                    else if (charges > 0) { Set(St.Equipping); HideGun(); AbilitySfx.Play(G, "equip", 0.5f); }
                    else DuelistSfx.Play(G, "empty", 0.6f);
                }
                else if (UsePressed && tether != null) Fake();
                break;
            case St.Equipping:
            case St.Equipped:
                if (key) { Set(St.Ready); GunBack(0f, 0.3f); break; }
                if (st == St.Equipping) { if (stT >= Equip) Set(St.Equipped); break; }
                if (FirePressed) Send(moving: true);
                else if (AltPressed) Send(moving: false);
                break;
            case St.Windup:
                if (tether == null) { Set(St.Ready); GunBack(0f, 0.3f); break; }
                if (stT >= TeleWindup) Arrive();
                break;
        }
    }

    void Send(bool moving)
    {
        charges--;
        var dir = FlatForward();
        var at = Feet;
        if (!moving)
        {
            // Placed: where you aim on the floor, at most 3 m ahead (slid along walls like a body).
            var vel = dir;
            Collision.MoveAndSlide(ref at, ref vel, dir * PlaceRange, G.Solid);
            at.Y = Collision.Ground(at, G.Solid);
        }
        tether = new Tether { Pos = at, From = Feet, Dir = dir, Speed = TetherSpeed, Born = Now, Moving = moving, Vis = DuelistVisual.Make(G, at, Color, 0.7f, pulseHz: 1.5f, light: 1.2f) };
        if (moving) { tether.Vis.AddTrail(new Color(0.5f, 0.7f, 1f, 0.6f), new Color(0.2f, 0.3f, 1f, 0f), 0.22f); sent++; } else placed++;
        Set(St.Ready);
        GunBack(0f, SendGunDraw);
        DuelistSfx.Play(G, "rift_send", 0.7f);
        MarkUse(moving ? "tether" : "tether_place");
    }

    void MoveTether(Tether t, float dt)
    {
        var p = t.Pos;
        var v = t.Dir * t.Speed;
        float want = t.Speed * dt;
        var before = p;
        Collision.MoveAndSlide(ref p, ref v, t.Dir * want, G.Solid);
        p.Y = Collision.Ground(p, G.Solid); // follows the floor: up a step, down a drop
        float moved = new Vector2(p.X - before.X, p.Z - before.Z).Length();
        float ratio = want > 1e-5f ? moved / want : 1f;
        // Scraping along a wall slows it (more the more head-on); clear of it, it picks its speed back up.
        t.Speed = ratio < 0.95f ? Mathf.Max(0f, t.Speed - 14f * dt * (1f - ratio)) : Mathf.Min(TetherSpeed, t.Speed + 10f * dt);
        t.Pos = p;
        if (t.Speed < 0.4f) { t.Moving = false; Log($"yoru tether stopped after {F1(new Vector2(p.X - t.From.X, p.Z - t.From.Z).Length())} m"); }
    }

    void Arrive()
    {
        if (tether is not { } t) return;
        var from = Feet;
        tether = null;
        t.Vis?.Remove();
        G.TeleportPlayer(t.Pos);
        teleports++;
        arrivedAt = Now;
        Set(St.Ready);
        GunBack(0f, TeleArrive);
        DuelistSfx.PlayAt(G, "teleport_in", t.Pos, 0.9f, maxRange: HearRange, floor: 0.6f);
        DuelistBurst.Spawn(G, t.Pos + Vector3.Up * 1f, Color, 2f, 0.5f, 3f);
        D.Alert(t.Pos, HearRange);
        float dist = new Vector2(t.Pos.X - from.X, t.Pos.Z - from.Z).Length();
        teleDist.Add(dist);
        LastAbilityAt = Now + TeleArrive; // you can't shoot before you've arrived
        LastAbilityTag = "teleport";
        G.Event("ability_teleport", dist);
        Log($"yoru teleport: {F1(dist)} m from {Fmt(from)} to {Fmt(t.Pos)}");
        Audit("yoru teleport");
        D.OnAbilityUsed("teleport");
    }

    void Fake()
    {
        if (tether is not { } t) return;
        tether = null;
        t.Vis?.Remove();
        fakes++;
        DuelistSfx.Play(G, "teleport_out", 0.6f);
        DuelistSfx.PlayAt(G, "teleport_in", t.Pos, 0.9f, maxRange: HearRange, floor: 0.6f);
        D.Alert(t.Pos, HearRange);
        G.Event("ability_fake");
        MarkUse("fake");
    }

    public override IEnumerable<KitSlot> Slots
    {
        get
        {
            string s = st switch
            {
                St.Equipping => "EQUIPPING…",
                St.Equipped => "LMB SEND · RMB PLACE",
                St.Windup => "TELEPORTING…",
                _ => tether != null ? $"TETHER {(tether.Moving ? "MOVING" : "SET")} · {Feet.DistanceTo(tether.Pos):0}m" : charges > 0 ? "READY" : "NO CHARGES",
            };
            float prog = st == St.Equipping ? stT / Equip : st == St.Windup ? stT / TeleWindup : tether != null ? 1f - (Now - tether.Born) / TetherLife : -1f;
            yield return new KitSlot(AbilitySlot.E, "Gatecrash", charges, 2, s, prog, st != St.Ready || tether != null);
        }
    }

    public override string? Prompt => st switch
    {
        St.Equipped => "LMB: send the tether · RMB: place it",
        St.Windup => null,
        _ => tether != null ? $"{KeyText(AbilitySlot.E)}: teleport · {UseKey}: fake it" : null,
    };

    public override void Draw2D(CanvasItem c, Vector2 size, float k)
    {
        if (st != St.Windup && Now - arrivedAt > 0.35f) return;
        float a = st == St.Windup ? Mathf.Clamp(stT / TeleWindup, 0, 1) * 0.5f : Mathf.Clamp(1f - (Now - arrivedAt) / 0.35f, 0, 1) * 0.5f;
        c.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.15f, 0.25f, 0.8f, a));
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Tethers sent / placed", $"{sent} / {placed}");
        yield return ("Teleports / fakes", $"{teleports} / {fakes}");
        if (teleDist.Count > 0) yield return ("Teleport distance", $"avg {teleDist.Average():0.0} m");
        if (caught > 0) yield return ("Caught on arrival", $"{caught} (died within 2 s)");
    }

    // ---- dev auto-play: send the tether into the site and teleport (even rounds: fake it, then walk in).
    public override bool DevEntry(DuelistDev c, float dt)
    {
        switch (c.Step)
        {
            case 0:
                c.LookFlat(c.SitePoint(5f));
                DevPress(AbilitySlot.E);
                c.Next();
                break;
            case 1:
                if (c.StepT < Equip + 0.05f) break;
                DevFire();
                c.Next();
                break;
            case 2:
                if (tether == null) return true;
                if (tether.Moving && c.StepT < 2.2f) break;
                if (D.RoundIndex % 2 == 0) { DevUse(); return true; }
                DevPress(AbilitySlot.E);
                c.Next();
                break;
            case 3:
                if (st == St.Ready && tether == null) return true;
                if (c.StepT > 2f) return true;
                break;
        }
        return false;
    }
}

/// <summary>
/// Waylay. Lightspeed (Q, 1 charge): equip 0.8 s; LMB dashes twice, RMB once: each 0.45 s (game files), ~9 m (wiki
/// estimate); the first follows your aim (it can climb), the second only flat or down. The gun is ready right after a
/// single dash and 0.6 s after the first of two ends (patch 11.00). Refract (E, 1 charge, back every two kills): drop a
/// beacon, ready after 1 s and for 8 s; re-use to rush back to it as light: intangible, 0.35 s (&lt;2.5 m) to 2 s (&gt;50 m),
/// along the way you came; 0.2 s self-blind on arrival, gun back 0.8 s later (patch 11.00).
/// </summary>
public sealed class WaylayKit : DuelistKit
{
    public const float LsEquip = 0.8f, LsTime = 0.45f, LsDist = 9f;
    public const float RfWindup = 1f, RfWindow = 8f, RfMin = 0.35f, RfMax = 2f, RfGunDraw = 0.8f, RfBlind = 0.2f;

    enum Ls { Ready, Equipping, Equipped, Dashing }
    Ls ls;
    float lsT;
    int lsCharges = 1, dashesLeft, dashNo, rfCharges = 1, lsUsed, refracts, escapes, wasted;
    bool doubleDash;
    Vector3? beacon;
    float beaconAt = -1f, lastTrailAt;
    DuelistVisual? beaconVis;
    readonly List<Vector3> trail = new();
    // travelling back
    List<Vector3>? path;
    float pathLen, travelT, travelDur;
    readonly List<float> lsDist = new();
    Vector3 lsFrom;

    public WaylayKit(IGame g, MobilityEntryMode d) : base(g, d) { }

    public override string AgentName => "Waylay";
    public override Color Color => Color.Color8(255, 220, 120);
    public override string HowTo =>
        $"{KeyText(AbilitySlot.E)} drops a Refract beacon (ready after 1 s, for 8 s): {KeyText(AbilitySlot.E)} again rushes you back to it, untouchable. " +
        $"{KeyText(AbilitySlot.Q)} equips Lightspeed: LMB dashes twice, RMB once (9 m each; aim up for height on the first). " +
        "Beacon behind cover, dash in, take the duel, Refract out.";

    bool Empowered => beacon != null && Now >= beaconAt + RfWindup && Now < beaconAt + RfWindup + RfWindow;
    bool Travelling => path != null;

    public override void ResetRound()
    {
        base.ResetRound();
        ls = Ls.Ready; lsCharges = 1; rfCharges = 1; dashesLeft = 0;
        ClearBeacon();
        path = null;
    }

    protected override void OnPlayerDead() { base.OnPlayerDead(); path = null; ls = Ls.Ready; }

    protected override void OnTwoKills()
    {
        if (rfCharges >= 1 || beacon != null) return;
        rfCharges = 1;
        G.Banner("Refract recharged (2 kills)", UiTheme.Teal);
    }

    void ClearBeacon()
    {
        beacon = null;
        beaconVis?.Remove();
        beaconVis = null;
        trail.Clear();
    }

    protected override void Update(float dt)
    {
        lsT += dt;
        if (Travelling) { Travel(dt); return; }

        // Refract
        if (beacon != null && Now >= beaconAt + RfWindup + RfWindow) { ClearBeacon(); wasted++; G.Banner("Refract beacon faded", UiTheme.Dim); }
        if (beacon != null && Now - lastTrailAt > 0.1f)
        {
            lastTrailAt = Now;
            var f = Feet;
            if (trail.Count == 0 || trail[^1].DistanceTo(f) > 0.3f) trail.Add(f);
        }
        if (Pressed[(int)AbilitySlot.E] && ls != Ls.Dashing)
        {
            if (beacon == null)
            {
                if (rfCharges > 0) PlaceBeacon();
                else DuelistSfx.Play(G, "empty", 0.6f);
            }
            else if (Empowered) StartTravel();
        }

        // Lightspeed
        bool key = Pressed[(int)AbilitySlot.Q];
        switch (ls)
        {
            case Ls.Ready:
                if (!key) break;
                if (lsCharges > 0) { ls = Ls.Equipping; lsT = 0; HideGun(); AbilitySfx.Play(G, "equip", 0.5f); }
                else DuelistSfx.Play(G, "empty", 0.6f);
                break;
            case Ls.Equipping:
            case Ls.Equipped:
                if (key) { ls = Ls.Ready; GunBack(0f, 0.3f); break; }
                if (ls == Ls.Equipping) { if (lsT >= LsEquip) ls = Ls.Equipped; break; }
                if (FirePressed || AltPressed)
                {
                    lsCharges--;
                    lsUsed++;
                    doubleDash = FirePressed;
                    dashesLeft = doubleDash ? 2 : 1;
                    dashNo = 0;
                    ls = Ls.Dashing;
                    lsFrom = Feet;
                    MarkUse("lightspeed");
                    NextDash();
                }
                break;
        }
    }

    void NextDash()
    {
        dashNo++;
        dashesLeft--;
        var dir = G.View.Forward;
        if (dashNo > 1) dir.Y = Mathf.Min(dir.Y, 0f);          // only the first dash can climb
        if (G.PlayerGrounded) dir.Y = Mathf.Max(dir.Y, 0f);    // not into the floor
        DuelistSfx.Play(G, "light_dash", 0.7f);
        DuelistBurst.Spawn(G, Feet + Vector3.Up * 0.9f, Color, 1.2f, 0.3f, 2f);
        StartDash(dir, LsDist, LsTime, Mover.RunSpeed, $"waylay lightspeed #{dashNo}", LsDist, holdAltitude: true, () =>
        {
            if (dashesLeft > 0) { NextDash(); return; }
            ls = Ls.Ready;
            GunBack(0f, doubleDash ? 0.15f : 0f);
            var f = Feet;
            lsDist.Add(new Vector2(f.X - lsFrom.X, f.Z - lsFrom.Z).Length());
        });
        D.OnAbilityUsed("dash");
    }

    void PlaceBeacon()
    {
        rfCharges--;
        beacon = Feet;
        beaconAt = Now;
        trail.Clear();
        trail.Add(beacon.Value);
        lastTrailAt = Now;
        beaconVis = DuelistVisual.Make(G, beacon.Value + Vector3.Up * 0.05f, Color, 1.1f, core: false, ring: true, pulseHz: 1.2f, light: 1f);
        DuelistSfx.Play(G, "refract", 0.6f);
        MarkUse("refract_beacon");
    }

    void StartTravel()
    {
        if (beacon is not { } b) return;
        // The way back: the trail you walked since dropping the beacon, reversed, with corners cut where nothing's in the way.
        var pts = new List<Vector3> { Feet };
        for (int i = trail.Count - 1; i >= 0; i--) pts.Add(trail[i]);
        pts.Add(b);
        var simple = new List<Vector3> { pts[0] };
        int at = 0;
        while (at < pts.Count - 1)
        {
            int next = at + 1;
            for (int j = pts.Count - 1; j > at + 1; j--)
                if (!Collision.Blocked(pts[at] + Vector3.Up * 0.5f, pts[j] + Vector3.Up * 0.5f, G.Solid) &&
                    !Collision.Blocked(pts[at] + Vector3.Up * 1.5f, pts[j] + Vector3.Up * 1.5f, G.Solid)) { next = j; break; }
            simple.Add(pts[next]);
            at = next;
        }
        path = simple;
        pathLen = 0;
        for (int i = 1; i < path.Count; i++) pathLen += path[i - 1].DistanceTo(path[i]);
        travelDur = Mathf.Lerp(RfMin, RfMax, Mathf.Clamp((pathLen - 2.5f) / (50f - 2.5f), 0f, 1f));
        travelT = 0;
        refracts++;
        if (D.AnyDefenderSeesPlayer || G.Player.Hp < 100f) escapes++;
        G.Player.ProtectedUntil = Now + travelDur + 0.05f;
        G.PlayerImpulse(Vector3.Zero);
        HideGun();
        DuelistSfx.Play(G, "refract", 0.9f);
        MarkUse("refract");
    }

    void Travel(float dt)
    {
        travelT += dt;
        float s = Mathf.Clamp(travelT / travelDur, 0f, 1f) * pathLen;
        var p = path![^1];
        for (int i = 1; i < path.Count; i++)
        {
            float seg = path[i - 1].DistanceTo(path[i]);
            if (s <= seg) { p = path[i - 1].Lerp(path[i], seg > 1e-4f ? s / seg : 1f); break; }
            s -= seg;
        }
        G.PlayerImpulse(Vector3.Zero);
        G.TeleportPlayer(p);
        if (travelT < travelDur) return;
        var b = beacon ?? p;
        G.TeleportPlayer(b);
        path = null;
        ClearBeacon();
        G.Blind.Apply(RfBlind);
        GunBack(0f, RfGunDraw);
        LastAbilityAt = Now;
        LastAbilityTag = "refract";
        G.Event("ability_refract", pathLen, travelDur * 1000f);
        Log($"waylay refract: {F1(pathLen)} m back in {travelDur:0.00} s (researched 0.35–2 s for 2.5–50 m)");
        Audit("waylay refract");
    }

    public override IEnumerable<KitSlot> Slots
    {
        get
        {
            string l = ls switch { Ls.Equipping => "EQUIPPING…", Ls.Equipped => "LMB ×2 · RMB ×1", Ls.Dashing => "DASHING", _ => lsCharges > 0 ? "READY" : "USED" };
            yield return new KitSlot(AbilitySlot.Q, "Lightspeed", lsCharges, 1, l, ls == Ls.Equipping ? lsT / LsEquip : -1f, ls != Ls.Ready);
            string r = Travelling ? "RETURNING" : beacon == null ? (rfCharges > 0 ? "READY" : "NO CHARGE")
                : Empowered ? $"BEACON  {beaconAt + RfWindup + RfWindow - Now:0.0}s" : "ARMING…";
            float rp = beacon == null ? -1f : Empowered ? (beaconAt + RfWindup + RfWindow - Now) / RfWindow : (Now - beaconAt) / RfWindup;
            yield return new KitSlot(AbilitySlot.E, "Refract", rfCharges, 1, r, rp, beacon != null);
        }
    }

    public override string? Prompt => ls == Ls.Equipped ? "LMB: dash twice · RMB: dash once" : Empowered ? $"{KeyText(AbilitySlot.E)}: Refract back to the beacon" : null;

    public override void Draw2D(CanvasItem c, Vector2 size, float k)
    {
        if (!Travelling) return;
        // Light form: you see almost nothing but the way.
        c.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.05f, 0.04f, 0.02f, 0.75f));
        var col = new Color(1f, 0.9f, 0.6f, 0.5f);
        float w = 120 * k;
        Gfx.HGradient(c, new Rect2(0, 0, w, size.Y), col, new Color(col, 0));
        Gfx.HGradient(c, new Rect2(size.X - w, 0, w, size.Y), new Color(col, 0), col);
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Lightspeed casts", lsUsed + (lsDist.Count > 0 ? $"  (avg {lsDist.Average():0.0} m)" : ""));
        yield return ("Refract returns", refracts + (escapes > 0 ? $"  ({escapes} from a fight)" : ""));
        if (wasted > 0) yield return ("Beacons faded", wasted.ToString());
    }

    // ---- dev auto-play: beacon at the hidden spot, double dash into the site, Refract out after a kill or when hurt.
    public override bool DevEntry(DuelistDev c, float dt)
    {
        switch (c.Step)
        {
            case 0:
                DevPress(AbilitySlot.E);
                c.Next();
                break;
            case 1:
                if (c.StepT < 0.2f) break;
                DevPress(AbilitySlot.Q);
                c.Next();
                break;
            case 2:
                if (c.StepT < LsEquip + 0.05f) break;
                c.LookDir(c.BestDashDir(LsDist));
                c.Pitch(D.RoundIndex % 2 == 0 ? 12f : 0f);
                c.Hold(Main.I.Valorant.KeyForward);
                DevFire();
                c.Next();
                break;
            case 3:
                c.Pitch(0f);
                if (c.StepT > 0.1f && ls == Ls.Ready && !Dashing) return true;
                break;
        }
        return false;
    }

    public override void DevPush(DuelistDev c, float dt)
    {
        if (Empowered && !Travelling && (RoundKills >= 1 || G.Player.Hp < 100f)) DevPress(AbilitySlot.E);
    }
}

/// <summary>
/// Iso. Double Tap (E, 1 charge): 1 s focus with no gun (patch 8.11), then a shield that absorbs one instance of damage
/// and a 12 s flow state (patch 9.0) with 25% faster reloads. Kills during the flow drop an energy orb for 3 s (patch 8.01);
/// shooting it gives the shield back (or refreshes it) and refreshes the flow. Gun back instantly after the focus (patch 13.01).
/// </summary>
public sealed class IsoKit : DuelistKit
{
    public const float Windup = 1f, Flow = 12f, OrbLife = 3f, OrbRadius = 0.3f, ReloadBoost = 1.25f;

    int charges = 1, formed, blocked, orbsDropped, orbsShot, shieldKills;
    float focusAt = -1f, flowUntil = -1f;
    bool shield, wasReloading;
    sealed class Orb { public Vector3 Pos; public float Born; public DuelistVisual? Vis; }
    readonly List<Orb> orbs = new();

    public IsoKit(IGame g, MobilityEntryMode d) : base(g, d) { }

    public override string AgentName => "Iso";
    public override Color Color => Color.Color8(170, 110, 255);
    public override string HowTo =>
        $"{KeyText(AbilitySlot.E)}: Double Tap — 1 s focus (no gun), then a shield eats the next hit. " +
        "Kills in the 12 s flow drop an orb: shoot it to get the shield back. " +
        "Start it just before you swing.";

    bool Focusing => focusAt >= 0;
    bool InFlow => flowUntil >= 0 && Now < flowUntil;

    public override void ResetRound()
    {
        base.ResetRound();
        foreach (var o in orbs) o.Vis?.Remove();
        orbs.Clear();
        charges = 1; focusAt = -1f; flowUntil = -1f; shield = false;
    }

    protected override void OnPlayerDead() { base.OnPlayerDead(); shield = false; focusAt = -1f; }

    protected override void Update(float dt)
    {
        if (Pressed[(int)AbilitySlot.E] && !Focusing)
        {
            if (charges > 0)
            {
                charges--;
                focusAt = Now;
                HideGun();
                GunBack(Windup, 0f);
                MarkUse("doubletap");
            }
            else DuelistSfx.Play(G, "empty", 0.6f);
        }
        if (Focusing && Now >= focusAt + Windup)
        {
            focusAt = -1f;
            GiveShield();
            LastAbilityAt = Now;
            LastAbilityTag = "shield";
        }
        if (flowUntil >= 0 && Now >= flowUntil) { flowUntil = -1f; shield = false; }
        for (int i = orbs.Count - 1; i >= 0; i--)
        {
            var o = orbs[i];
            if (Now - o.Born > OrbLife - 0.8f) o.Vis?.SetBlink(6f);
            if (Now - o.Born >= OrbLife) { o.Vis?.Remove(); orbs.RemoveAt(i); }
        }
        // Flow: 25% faster reloads.
        if (G is GameSession gs && gs.Gun is { } gun)
        {
            if (gun.Reloading && !wasReloading && InFlow) gun.ReloadLeft /= ReloadBoost;
            wasReloading = gun.Reloading;
        }
    }

    void GiveShield()
    {
        if (!shield) formed++;
        shield = true;
        flowUntil = Now + Flow;
        DuelistSfx.Play(G, "shield_on", 0.8f);
        G.Event("ability_shield", 1);
        Log($"iso shield up (flow until +{Flow:0} s)");
    }

    public override bool Absorb(float dmg, Vector3 from)
    {
        if (!shield || !InFlow) return false;
        shield = false;
        blocked++;
        DuelistSfx.Play(G, "shield_break", 0.9f);
        G.Event("ability_shield_block", dmg);
        Log($"iso shield absorbed a {dmg:0} damage hit");
        return true;
    }

    public override void OnKill(BotCharacter bot, Vector3 at)
    {
        base.OnKill(bot, at);
        if (shield) shieldKills++;
        if (!InFlow) return;
        var p = bot.Head + Vector3.Up * 0.6f;
        orbs.Add(new Orb { Pos = p, Born = Now, Vis = DuelistVisual.Make(G, p, Color, 0.55f, pulseHz: 2.5f, light: 1.2f) });
        orbsDropped++;
        DuelistSfx.PlayAt(G, "iso_orb", p, 0.7f);
    }

    public override float HitTest(Vector3 o, Vector3 d, float maxDist)
    {
        Orb? best = null;
        float bt = maxDist;
        foreach (var orb in orbs)
        {
            var oc = o - orb.Pos;
            float b = oc.Dot(d), c = oc.LengthSquared() - OrbRadius * OrbRadius, disc = b * b - c;
            if (disc < 0) continue;
            float t = -b - Mathf.Sqrt(disc);
            if (t > 0 && t < bt) { bt = t; best = orb; }
        }
        if (best == null) return float.PositiveInfinity;
        orbs.Remove(best);
        best.Vis?.Remove();
        orbsShot++;
        Log("iso orb shot");
        GiveShield();
        DuelistBurst.Spawn(G, best.Pos, Color, 1f, 0.3f, 2f);
        return bt;
    }

    public override AimFocus? ExtraFocus
    {
        get
        {
            foreach (var o in orbs)
                if (G.LineOfSight(G.View.Eye, o.Pos)) return new AimFocus(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o), o.Pos, OrbRadius);
            return null;
        }
    }

    public override IEnumerable<KitSlot> Slots
    {
        get
        {
            string s = Focusing ? "FOCUSING…" : InFlow ? (shield ? $"SHIELD UP  {flowUntil - Now:0.0}s" : $"FLOW  {flowUntil - Now:0.0}s · NO SHIELD") : charges > 0 ? "READY" : "USED";
            float p = Focusing ? (Now - focusAt) / Windup : InFlow ? (flowUntil - Now) / Flow : -1f;
            yield return new KitSlot(AbilitySlot.E, "Double Tap", charges, 1, s, p, Focusing || InFlow);
        }
    }

    public override string? Prompt => Focusing ? "Focusing…" : orbs.Count > 0 ? "Shoot the orb for a new shield" : null;

    public override IEnumerable<string> StatusLines()
    {
        if (shield && InFlow) yield return "Shield up";
    }

    public override void Draw2D(CanvasItem c, Vector2 size, float k)
    {
        if (!shield || !InFlow) return;
        var col = new Color(0.7f, 0.45f, 1f, 0.22f + 0.05f * Mathf.Sin(Now * 4f));
        float w = 40 * k;
        Gfx.HGradient(c, new Rect2(0, 0, w, size.Y), col, new Color(col, 0));
        Gfx.HGradient(c, new Rect2(size.X - w, 0, w, size.Y), new Color(col, 0), col);
        Gfx.VGradient(c, new Rect2(0, 0, size.X, w), col, new Color(col, 0));
        Gfx.VGradient(c, new Rect2(0, size.Y - w, size.X, w), new Color(col, 0), col);
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Shields / hits blocked", $"{formed} / {blocked}");
        if (orbsDropped > 0) yield return ("Orbs shot", $"{orbsShot} of {orbsDropped}");
        if (shieldKills > 0) yield return ("Kills shield up", shieldKills.ToString());
    }

    // ---- dev auto-play: focus at the hidden spot, then push (the simulated player shoots orbs: ExtraFocus).
    public override bool DevEntry(DuelistDev c, float dt)
    {
        if (c.Step == 0) { DevPress(AbilitySlot.E); c.Next(); return false; }
        return c.StepT >= Windup + 0.05f;
    }
}
