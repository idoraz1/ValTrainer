using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// Hush (Duelist, patch 13.09). Flow (C, 1 charge): instantly drops an anchor where you aim (the surface under the
/// crosshair, up to 15 m, else 15 m out in the air); if you're airborne you hang in place; 0.4 s later you're pulled
/// straight to it at 18 m/s (it climbs: aim at a ledge up to 4.5 m above you to land on it). Displacement (E, 1 charge, back every two kills:
/// patch notes): equip, LMB throws it (it blows on the first surface or after 1.2 s), RMB sticks it to the first surface
/// it reaches, and the key again sends a flying or stuck one toward the point under your crosshair. The blast drags every
/// player within 7 m that it can see toward its centre, and concusses (here: a long daze: slow reaction, poor aim) anyone
/// who slams into a wall or another player on the way; the others are dazed briefly. Only the inputs and the two-kill
/// reset are published: every distance, speed and time here is an estimate (docs/research/hush-1.20.md).
/// </summary>
public sealed class HushKit : DuelistKit
{
    // Flow (estimates: "the longest dash in VALORANT, slower than Jett's" per guides; no published numbers).
    public const float FlowRange = 15f, FlowDelay = 0.4f, FlowSpeed = 18f, FlowGunDraw = 0.25f, MaxClimb = 4.5f;
    // Displacement (estimates except the two-kill reset).
    public const float DpEquip = 0.6f, DpSpeed = 18f, DpGravity = 0.3f * PlayerUtility.WorldGravity, DpFuse = 1.2f;
    public const float DpRedirectSpeed = 22f, DpRedirectFlight = 1.0f, DpWindup = 0.3f, DpStickLife = 10f, DpGunBack = 0.6f;
    public const float DpRadius = 7f, DpStop = 1.2f, DpPullTime = 0.35f, DpDaze = 1f, DpConcuss = 2.5f, DpAimRange = 40f;

    static readonly Color Tint = Color.Color8(235, 120, 200);

    int flowCharges = 1, dpCharges = 1;
    // Flow
    float anchorAt = -1f;
    Vector3 anchor, targetFeet, flowFrom;
    bool flowing;
    DuelistVisual? anchorVis;
    // Displacement
    enum Dp { Ready, Equipping, Equipped }
    Dp dp;
    float dpT;
    sealed class Charge
    {
        public Vector3 Pos, Vel;
        public bool Prepare, Stuck, Redirected, Exploding;
        public float Age, Flight, ExplodeAt;
        public DuelistVisual? Vis;
    }
    Charge? charge;
    sealed class Pull { public BotCharacter Body = null!; public Vector3 From, To; public float T; }
    readonly List<Pull> pulls = new();
    readonly Dictionary<BotCharacter, float> pulledAt = new();
    // stats
    int flows, flowsUp, dpThrown, dpStuck, dpRedirects, dpPulled, dpConcussed, dpKills, dpSelf, dpFizzled;
    readonly List<float> flowDist = new();

    public HushKit(IGame g, MobilityEntryMode d) : base(g, d) { }

    public override string AgentName => "Hush";
    public override Color Color => Tint;
    public override string[] HowTo => new[]
    {
        L.F($"{KeyText(AbilitySlot.C)}: {"Flow"} drops an anchor where you aim (up to 15 m) and pulls you to it 0.4 s later. Aim at a ledge to land on it."),
        L.F($"{KeyText(AbilitySlot.E)} equips {"Displacement"}: LMB throws it, RMB sticks it to a wall, {KeyText(AbilitySlot.E)} again sends it toward your crosshair."),
        L.T("It drags defenders out of cover toward the blast. Shoot them while they're concussed."),
    };

    bool Anchored => anchorAt >= 0;

    public override void ResetRound()
    {
        base.ResetRound();
        flowCharges = 1; dpCharges = 1; dp = Dp.Ready;
        anchorAt = -1f; flowing = false;
        ClearAnchor();
        ClearCharge();
        pulls.Clear();
        pulledAt.Clear();
    }

    protected override void OnPlayerDead()
    {
        base.OnPlayerDead();
        anchorAt = -1f; flowing = false;
        dp = Dp.Ready;
        ClearAnchor();
    }

    protected override void OnTwoKills()
    {
        if (dpCharges >= 1 || charge != null) return;
        dpCharges = 1;
        G.Banner(L.F($"{"Displacement"} recharged (2 kills)"), UiTheme.Teal);
    }

    public override void OnKill(BotCharacter bot, Vector3 at)
    {
        if (pulledAt.TryGetValue(bot, out float t) && Now - t < 4f) dpKills++;
        base.OnKill(bot, at);
    }

    void ClearAnchor() { anchorVis?.Remove(); anchorVis = null; }
    void ClearCharge() { charge?.Vis?.Remove(); charge = null; }

    // ------------------------------------------------------------------ frame

    protected override void Update(float dt)
    {
        UpdatePulls(dt);
        UpdateCharge(dt);

        // Flow
        if (Anchored)
        {
            // Airborne: hang in place until the pull starts.
            if (!G.PlayerGrounded) G.PlayerImpulse(new Vector3(0f, -0.0001f, 0f), 0.05f);
            if (Now >= anchorAt + FlowDelay) Launch();
        }
        else if (Pressed[(int)AbilitySlot.C] && !Dashing)
        {
            if (flowCharges > 0)
            {
                if (dp != Dp.Ready) { dp = Dp.Ready; } // Flow takes the hands: the Displacement goes back
                PlaceAnchor();
            }
            else DuelistSfx.Play(G, "empty", 0.6f);
        }

        // Displacement
        bool key = Pressed[(int)AbilitySlot.E];
        if (charge != null)
        {
            if (key && !charge.Redirected && !charge.Exploding) Redirect(charge);
            return;
        }
        switch (dp)
        {
            case Dp.Ready:
                if (!key || Anchored || Dashing) break;
                if (dpCharges > 0) { dp = Dp.Equipping; dpT = 0; HideGun(); AbilitySfx.Play(G, "equip", 0.5f); }
                else DuelistSfx.Play(G, "empty", 0.6f);
                break;
            case Dp.Equipping:
            case Dp.Equipped:
                dpT += dt;
                if (key) { dp = Dp.Ready; GunBack(0f, 0.3f); break; }
                if (dp == Dp.Equipping) { if (dpT >= DpEquip) dp = Dp.Equipped; break; }
                if (FirePressed || AltPressed) Throw(prepare: !FirePressed);
                break;
        }
    }

    // ------------------------------------------------------------------ Flow

    /// <summary>Where Flow's anchor goes for a look direction, and where your feet end up.</summary>
    (Vector3 Anchor, Vector3 Feet) AnchorFor(Vector3 eye, Vector3 dir)
    {
        Vector3 a, f;
        // Never more than MaxClimb above your feet (body centre 0.9 m above them): a steep aim stops on the line, so
        // map walls and roofs stay out of reach.
        float climb = dir.Y > 1e-3f ? (Feet.Y + MaxClimb + 0.9f - eye.Y) / dir.Y : float.PositiveInfinity;
        float limit = Mathf.Min(FlowRange, Mathf.Max(0.5f, climb));
        float t = Collision.FirstHit(eye, dir, G.Solid, out int i);
        if (t <= limit && i >= 0)
        {
            var p = eye + dir * t;
            var n = G.Solid[i].NormalAt(p);
            a = p + n * 0.1f;
            // A ledge or floor: land on it. A wall: stop just off it.
            f = n.Y > 0.7f ? p + Vector3.Up * 0.05f : p + n * 0.5f - Vector3.Up * 0.9f;
        }
        else
        {
            a = eye + dir * limit;
            f = a - Vector3.Up * 0.9f;
        }
        f.Y = Mathf.Max(f.Y, Collision.Ground(f, G.Solid, 0.6f));
        return (a, f);
    }

    void PlaceAnchor()
    {
        (anchor, targetFeet) = AnchorFor(G.View.Eye, G.View.Forward.Normalized());
        flowCharges--;
        anchorAt = Now;
        HideGun();
        anchorVis = DuelistVisual.Make(G, anchor, Tint, 0.8f, core: true, ring: true, pulseHz: 3f, light: 1.2f);
        DuelistSfx.Play(G, "flow_anchor", 0.7f);
        DuelistSfx.PlayAt(G, "flow_anchor", anchor, 0.5f, maxRange: 30f);
        MarkUse("flow");
    }

    void Launch()
    {
        anchorAt = -1f;
        var f = Feet;
        bool rising = targetFeet.Y > f.Y + 0.3f;
        // Aim a little above a ledge so the body clears its lip.
        var aim = targetFeet + Vector3.Up * (rising ? 0.6f : 0.15f);
        var d = aim - f;
        float dist = d.Length();
        if (dist < 0.8f) { ClearAnchor(); GunBack(0f, FlowGunDraw); return; }
        flows++;
        if (targetFeet.Y > f.Y + 1f) flowsUp++;
        flowing = true;
        flowFrom = f;
        DuelistSfx.Play(G, "flow_pull", 0.8f);
        DuelistBurst.Spawn(G, f + Vector3.Up * 0.9f, Tint, 1.2f, 0.35f, 1.5f);
        D.Alert(f, 20f, 0.5f);
        StartDash(d / dist, dist, dist / FlowSpeed, G.Mover.RunMax * 0.6f, "hush flow", dist, holdAltitude: true, () =>
        {
            flowing = false;
            ClearAnchor();
            // Don't keep the dash's climb or dive: a small hop over a ledge, else start falling gently.
            var v = G.Mover.Vel;
            G.PlayerImpulse(new Vector3(v.X, rising ? 2.5f : -0.5f, v.Z));
            GunBack(0f, FlowGunDraw);
            flowDist.Add(Feet.DistanceTo(flowFrom));
            Log($"hush flow: {F1(Feet.DistanceTo(flowFrom))} m to the anchor (target {Fmt(targetFeet)}, ended {Fmt(Feet)}, " +
                $"rise {F1(Feet.Y - flowFrom.Y)} m; estimates: ≤{FlowRange} m at {FlowSpeed} m/s after {FlowDelay} s)");
        });
        D.OnAbilityUsed("dash");
    }

    // ------------------------------------------------------------------ Displacement

    void Throw(bool prepare)
    {
        dp = Dp.Ready;
        dpCharges--;
        dpThrown++;
        var dir = G.View.Forward.Normalized();
        var c = new Charge { Pos = PlayerUtility.HandPoint(G, dir), Vel = dir * DpSpeed, Prepare = prepare };
        c.Vis = DuelistVisual.Make(G, c.Pos, Tint, 0.45f, core: true, ring: false, pulseHz: 5f, light: 0.8f);
        c.Vis.AddTrail(Tint, new Color(0.4f, 0.2f, 0.6f, 0f), 0.18f);
        charge = c;
        GunBack(DpGunBack, 0.2f);
        DuelistSfx.Play(G, "displace_throw", 0.7f);
        MarkUse("displacement");
    }

    void Redirect(Charge c)
    {
        c.Redirected = true;
        dpRedirects++;
        var eye = G.View.Eye;
        var aim = G.View.Forward.Normalized();
        float hit = Mathf.Min(DpAimRange, Collision.FirstHit(eye, aim, G.Solid));
        var to = eye + aim * hit - c.Pos;
        c.Vel = (to.LengthSquared() > 0.04f ? to.Normalized() : aim) * DpRedirectSpeed;
        c.Stuck = false;
        c.Flight = 0f;
        DuelistSfx.PlayAt(G, "flashbulb_redirect", c.Pos, 0.7f);
        G.Event("ability_redirect", c.Prepare ? 1 : 0, 0);
    }

    void UpdateCharge(float dt)
    {
        if (charge is not { } c) return;
        c.Age += dt;
        if (c.Exploding)
        {
            if (c.Age - c.ExplodeAt >= DpWindup) { var at = c.Pos; ClearCharge(); Blast(at); }
            return;
        }
        if (c.Stuck)
        {
            if (c.Age >= DpStickLife) { dpFizzled++; ClearCharge(); G.Banner(L.N("The charge faded before you launched it"), UiTheme.Dim); }
            return;
        }
        c.Flight += dt;
        bool waits = c.Prepare && !c.Redirected;
        if (PlayerUtility.Step(ref c.Pos, ref c.Vel, c.Redirected ? 0f : DpGravity, dt, G.Solid, out _))
        {
            if (waits)
            {
                c.Stuck = true;
                c.Vel = Vector3.Zero;
                dpStuck++;
                DuelistSfx.PlayAt(G, "displace_stick", c.Pos, 0.7f);
            }
            else Explode(c);
        }
        else if (!waits && c.Flight >= (c.Redirected ? DpRedirectFlight : DpFuse)) Explode(c);
        else if (waits && c.Flight >= DpFuse * 2f) { c.Stuck = true; c.Vel = Vector3.Zero; } // hangs in the air
        if (PlayerUtility.OffMap(c.Pos, G.Solid)) { dpFizzled++; ClearCharge(); return; }
        if (c.Vis != null && GodotObject.IsInstanceValid(c.Vis)) c.Vis.GlobalPosition = c.Pos;
    }

    void Explode(Charge c)
    {
        c.Exploding = true;
        c.ExplodeAt = c.Age;
        c.Vel = Vector3.Zero;
        c.Vis?.SetBlink(14f);
        if (c.Vis != null && GodotObject.IsInstanceValid(c.Vis)) c.Vis.GlobalPosition = c.Pos;
        DuelistSfx.PlayAt(G, "displace_blast", c.Pos, 1f, maxRange: 50f, floor: 0.6f);
    }

    /// <summary>Drags every player the blast can see toward its centre; a wall or another body on the way concusses.</summary>
    void Blast(Vector3 at)
    {
        DuelistBurst.Spawn(G, at, Tint, DpRadius * 0.55f, 0.55f, 4f);
        DuelistBurst.Spawn(G, at, new Color(1f, 0.85f, 0.95f), 1.2f, 0.3f, 0f);
        D.Alert(at, 40f);
        LastAbilityAt = Now;
        LastAbilityTag = "displacement";
        int n = 0, conc = 0;
        var taken = new List<Vector3>();
        foreach (var (body, brain) in D.LiveDefenderBrains.ToList())
        {
            var chest = body.Head + Vector3.Down * 0.45f;
            if (chest.DistanceTo(at) > DpRadius) continue;
            if (!G.LineOfSight(at, chest) && !G.LineOfSight(at, body.Head)) continue;
            var from = body.Feet;
            var flat = at - from; flat.Y = 0;
            float len = flat.Length();
            float travel = Mathf.Max(0f, len - DpStop);
            bool collided = false;
            if (travel > 0.3f)
            {
                var dir = flat / len;
                float room = float.PositiveInfinity;
                foreach (var h in new[] { 0.4f, 1.2f })
                    room = Mathf.Min(room, Collision.FirstHit(from + Vector3.Up * h, dir, G.Solid) - 0.45f);
                if (room < travel) { travel = Mathf.Max(0f, room); collided = true; }
                Vector3 to;
                while (true)
                {
                    to = from + dir * travel;
                    to.Y = Collision.Ground(new Vector3(to.X, from.Y, to.Z), G.Solid, 0.5f);
                    if (travel <= 0f || !Collision.Overlaps(to, G.Solid, 0.3f)) break;
                    travel = Mathf.Max(0f, travel - 0.25f);
                    collided = true;
                }
                // Slammed into another dragged body.
                if (taken.Any(p => p.DistanceTo(to) < 0.75f)) { collided = true; to = to.Lerp(from, 0.75f / Mathf.Max(travel, 0.75f)); }
                if (travel > 0.05f) pulls.Add(new Pull { Body = body, From = from, To = to });
                taken.Add(to);
            }
            else collided = true; // already at the centre: hits the others / the device
            brain.Flash(Now, 0f, collided ? DpConcuss : DpDaze);
            pulledAt[body] = Now;
            n++;
            if (collided) conc++;
        }
        dpPulled += n;
        dpConcussed += conc;
        // It drags you too.
        var eye = G.View.Eye;
        var me = at - eye; me.Y = 0;
        if (eye.DistanceTo(at) <= DpRadius && G.LineOfSight(at, eye) && me.Length() > DpStop)
        {
            dpSelf++;
            var v = me.Normalized() * Mathf.Min(9f, me.Length() * 2f);
            G.PlayerImpulse(new Vector3(v.X, 0f, v.Z), 0.2f);
        }
        G.Event("ability_displace", n, conc);
        G.Banner(n == 0 ? L.N("Displacement: nobody pulled")
            : conc > 0 ? L.P(n, $"{n} PULLED ({conc} concussed)", $"{n} PULLED ({conc} concussed)") : L.P(n, $"{n} PULLED", $"{n} PULLED"),
            n > 0 ? UiTheme.Good : UiTheme.Dim);
        Log($"hush displacement at {Fmt(at)}: {n} pulled, {conc} concussed (estimates: {DpRadius} m radius, {DpPullTime} s pull)");
    }

    void UpdatePulls(float dt)
    {
        for (int i = pulls.Count - 1; i >= 0; i--)
        {
            var p = pulls[i];
            p.T += dt;
            float k = Mathf.Clamp(p.T / DpPullTime, 0f, 1f);
            k = 1f - (1f - k) * (1f - k); // fast start, eases in
            if (GodotObject.IsInstanceValid(p.Body) && !p.Body.Dead) p.Body.Feet = p.From.Lerp(p.To, k);
            if (p.T >= DpPullTime) pulls.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------ HUD

    public override IEnumerable<KitSlot> Slots
    {
        get
        {
            string fs = Anchored ? L.N("ANCHORED…") : flowing ? L.N("FLOWING") : flowCharges > 0 ? L.N("READY") : L.N("USED");
            float fp = Anchored ? (Now - anchorAt) / FlowDelay : -1f;
            yield return new KitSlot(AbilitySlot.C, "Flow", flowCharges, 1, fs, fp, Anchored || flowing);
            string ds = charge != null
                ? charge.Exploding ? L.N("PULLING") : charge.Stuck ? L.N("STUCK") : charge.Redirected ? L.N("REDIRECTED") : L.N("IN FLIGHT")
                : dp == Dp.Equipping ? L.N("EQUIPPING…") : dp == Dp.Equipped ? "LMB · RMB" : dpCharges > 0 ? L.N("READY") : L.N("NO CHARGE");
            float dprog = dp == Dp.Equipping ? dpT / DpEquip : charge is { Stuck: true } s ? 1f - s.Age / DpStickLife : -1f;
            yield return new KitSlot(AbilitySlot.E, "Displacement", dpCharges, 1, ds, dprog, dp != Dp.Ready || charge != null);
        }
    }

    public override string? Prompt =>
        dp == Dp.Equipped ? L.N("LMB: throw · RMB: stick it to a wall")
        : charge is { Exploding: false, Redirected: false } c
            ? c.Stuck ? L.F($"{KeyText(AbilitySlot.E)}: launch it toward your crosshair") : L.F($"{KeyText(AbilitySlot.E)}: send it toward your crosshair")
        : Anchored ? L.F($"{"Flow"}: pulling you to the anchor…") : null;

    public override void Draw2D(CanvasItem c, Vector2 size, float k)
    {
        if (!flowing && !Anchored) return;
        float a = flowing ? 0.3f : 0.12f + 0.05f * Mathf.Sin(Now * 9f);
        var col = new Color(Tint, a);
        float w = 80 * k;
        Gfx.HGradient(c, new Rect2(0, 0, w, size.Y), col, new Color(col, 0));
        Gfx.HGradient(c, new Rect2(size.X - w, 0, w, size.Y), new Color(col, 0), col);
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return (L.F($"{"Flow"} pulls"), flowDist.Count > 0
            ? L.F($"{flows}  (avg {flowDist.Average():0.0} m, {flowsUp} up a level)") : $"{flows}");
        yield return (L.F($"{"Displacement"} throws"), dpRedirects > 0 ? L.F($"{dpThrown}  ({dpRedirects} redirected)") : $"{dpThrown}");
        if (dpPulled > 0) yield return (L.N("Defenders pulled"), L.F($"{dpPulled}  ({dpConcussed} concussed)"));
        if (dpKills > 0) yield return (L.N("Kills after a pull"), dpKills.ToString());
        if (dpSelf > 0) yield return (L.N("Pulled yourself"), dpSelf.ToString());
        if (dpFizzled > 0) yield return (L.N("Charges wasted"), dpFizzled.ToString());
    }

    // ------------------------------------------------------------------ dev auto-play

    /// <summary>Dev: odd rounds throw Displacement first where its blast would catch the most defenders (round 3, 7 …:
    /// stick it to a wall, then launch it at the nearest defender); every round then Flows in: even rounds onto the highest
    /// reachable ledge on the site side, odd rounds to the anchor that gets you farthest into the site.</summary>
    public override bool DevEntry(DuelistDev c, float dt)
    {
        switch (c.Step)
        {
            case 0:
                if (D.RoundIndex % 2 == 1 && dpCharges > 0) { DevPress(AbilitySlot.E); c.Next(1); }
                else c.Next(3);
                break;
            case 1:
            {
                if (c.StepT < DpEquip + 0.05f) break;
                bool stick = D.RoundIndex % 4 == 3;
                var (dir, caught) = DevThrowDir();
                Look(c, dir);
                Log($"hush displacement (dev): {(stick ? "stick" : "throw")} toward {Fmt(dir)}, {caught} predicted in the blast");
                // Cast now, in the frame the view was set (a key press lands next frame, after the simulated aim moved).
                if (dp == Dp.Equipped) Throw(prepare: stick);
                c.Next();
                break;
            }
            case 2:
                if (charge is { Stuck: true, Redirected: false } && c.StepT > 0.5f)
                {
                    var tgt = D.LiveDefenders.OrderBy(b => b.Feet.DistanceTo(charge.Pos)).FirstOrDefault();
                    if (tgt != null) Look(c, (tgt.Feet + Vector3.Up * 0.3f - G.View.Eye).Normalized());
                    Redirect(charge);
                }
                if (charge == null || c.StepT > 3f) c.Next(3);
                break;
            case 3:
            {
                bool ledge = D.RoundIndex % 2 == 0;
                var pick = DevFlowDir(ledge) ?? (ledge ? DevFlowDir(false) : null);
                if (pick is { } dir)
                {
                    Look(c, dir);
                    var (_, f) = AnchorFor(G.View.Eye, dir);
                    Log($"hush flow (dev): {(ledge ? "ledge" : "entry")} anchor, feet to {Fmt(f)}, {F1(f.DistanceTo(Feet))} m away, {F1(f.Y - Feet.Y)} m up");
                }
                else { c.LookDir(c.BestDashDir(FlowRange)); c.Pitch(0f); }
                if (flowCharges > 0 && !Dashing) PlaceAnchor(); // now, with this view (see the throw above)
                c.Next();
                break;
            }
            case 4:
                if (c.StepT > FlowDelay + 0.1f && !Dashing && !Anchored) { c.Pitch(0f); return true; }
                break;
        }
        return false;
    }

    static void Look(DuelistDev c, Vector3 dir)
    {
        c.LookDir(dir);
        c.Pitch(Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(dir.Normalized().Y, -1f, 1f))));
    }

    IEnumerable<Vector3> DevDirs()
    {
        var axis = D.SiteAxis;
        float baseYaw = Mathf.RadToDeg(Mathf.Atan2(axis.X, -axis.Z));
        for (float dy = -90f; dy <= 90f; dy += 5f)
            for (float p = -8f; p <= 40f; p += 3f)
                yield return PlayerView.Dir(baseYaw + dy, p);
    }

    /// <summary>A Flow direction whose pull isn't blocked: the farthest into the site, or (ledge) the highest ledge top at
    /// least 0.8 m above you with head room. Null = nothing usable.</summary>
    Vector3? DevFlowDir(bool ledge)
    {
        var eye = G.View.Eye;
        var feet = Feet;
        var axis = D.SiteAxis;
        Vector3? best = null;
        float bestScore = float.NegativeInfinity;
        foreach (var dir in DevDirs())
        {
            var (_, f) = AnchorFor(eye, dir);
            if (f.DistanceTo(feet) < 4f) continue;
            float lift = f.Y > feet.Y + 0.3f ? 0.6f : 0.15f;
            bool clear = true;
            foreach (var h in new[] { 0.5f, 1.0f, 1.6f })
                if (Collision.Blocked(feet + Vector3.Up * h, f + Vector3.Up * (h + lift), G.Solid)) { clear = false; break; }
            if (!clear || Collision.Overlaps(f, G.Solid, 0.3f)) continue;
            float rise = f.Y - feet.Y;
            if (ledge && (rise < 0.8f || Collision.Ground(f, G.Solid, 0.1f) < f.Y - 0.1f ||
                          Collision.Blocked(f + Vector3.Up * 0.1f, f + Vector3.Up * 1.9f, G.Solid))) continue; // a real top, with head room
            float score = (f - D.Spot.Choke).Dot(axis) + (ledge ? 3f * rise : 0f);
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        return best;
    }

    /// <summary>The throw whose blast (simulated, no redirect) would catch the most defenders it can see, else the one that
    /// lands nearest a defender.</summary>
    (Vector3 Dir, int Caught) DevThrowDir()
    {
        var bodies = D.LiveDefenders.ToList();
        Vector3 best = D.SiteAxis;
        int bestN = -1;
        float bestD = float.PositiveInfinity;
        const float step = 1f / 60f;
        foreach (var dir in DevDirs())
        {
            var pos = PlayerUtility.HandPoint(G, dir);
            var vel = dir * DpSpeed;
            for (float t = 0; t < DpFuse; t += step)
                if (PlayerUtility.Step(ref pos, ref vel, DpGravity, step, G.Solid, out _)) break;
            int n = bodies.Count(b => (b.Head + Vector3.Down * 0.45f).DistanceTo(pos) <= DpRadius &&
                                      (G.LineOfSight(pos, b.Head + Vector3.Down * 0.45f) || G.LineOfSight(pos, b.Head)));
            float near = bodies.Count > 0 ? bodies.Min(b => b.Feet.DistanceTo(pos)) : 0f;
            if (n > bestN || (n == bestN && near < bestD)) { bestN = n; bestD = near; best = dir; }
        }
        return (best, Math.Max(0, bestN));
    }
}
