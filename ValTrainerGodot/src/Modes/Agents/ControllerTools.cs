using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Fx;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// One smoke ability of the Smoke Execute drill (a key, charges, an "in hand" state). The drill routes the ability keys
/// and, while one is in hand, the mouse buttons (and for the tactical map the mouse motion) to it.
/// </summary>
abstract class SmokeAbility
{
    protected readonly SmokeExecuteMode M;
    public readonly Key HotKey;
    public int Charges;
    public bool Equipped { get; protected set; }
    public abstract string Name { get; }

    protected SmokeAbility(SmokeExecuteMode m, Key key, int charges) { M = m; HotKey = key; Charges = charges; }

    protected IGame G => M.Game;
    /// <summary>The tactical map covers the screen (mouse moves a cursor instead of the view).</summary>
    public virtual bool MapOpen => false;
    public virtual bool BlocksMovement => false;
    /// <summary>Nothing left to place with this ability this round.</summary>
    public virtual bool Spent => Charges <= 0 && !Equipped;
    /// <summary>Short HUD status, e.g. "×2" or "ON".</summary>
    public virtual string Status => Charges > 0 ? "×" + Charges : "USED";

    /// <summary>The ability key was pressed. Default: take it in hand / put it away.</summary>
    public virtual bool OnKey()
    {
        if (Equipped) { Unequip(); return true; }
        if (Charges <= 0) return false;
        Equip();
        return true;
    }

    public virtual void Equip() { Equipped = true; G.SetAbilityInHand(true); }
    public virtual void Unequip() { if (!Equipped) return; Equipped = false; G.SetAbilityInHand(false, 0.45f); }
    public virtual void Update(float dt) { }
    /// <summary>Input while in hand; true = consumed.</summary>
    public virtual bool OnInput(InputEvent e) => false;
    public virtual void Draw(CanvasItem ci, Vector2 size, float k) { }
    /// <summary>Dev auto-play: place at these standard spots (in order) without input.</summary>
    public virtual void Auto(List<PlanSmoke> spots) { }
    /// <summary>The round ended: free markers, drop anything in flight.</summary>
    public virtual void Clear() { if (Equipped) Unequip(); }

    protected static bool Pressed(InputEvent e, MouseButton b) => e is InputEventMouseButton { Pressed: true } mb && mb.ButtonIndex == b;
    protected static bool Released(InputEvent e, MouseButton b) => e is InputEventMouseButton { Pressed: false } mb && mb.ButtonIndex == b;
    protected static bool Button(InputEvent e) => e is InputEventMouseButton mb && mb.ButtonIndex is MouseButton.Left or MouseButton.Right;
}

// =====================================================================================================================
// Tactical map (Brimstone Sky Smoke, Clove Ruse, Miks Waveform) and Astra's Astral Form
// =====================================================================================================================

/// <summary>Shared tactical-map cursor: the mouse moves a cursor over the map (relative motion while the mouse is captured,
/// the real pointer in dev runs).</summary>
abstract class MapAbility : SmokeAbility
{
    protected Vector2 cursor;
    bool cursorSet;
    protected MapAbility(SmokeExecuteMode m, Key key, int charges) : base(m, key, charges) { }
    public override bool MapOpen => Equipped;
    public Vector2 Cursor => cursor;

    public override void Equip()
    {
        base.Equip();
        cursorSet = false;
    }

    /// <summary>Called by the drill's draw before input/drawing so the cursor starts on the player.</summary>
    public void EnsureCursor(TacticalMap map)
    {
        if (cursorSet) return;
        cursorSet = true;
        cursor = map.ToScreen(M.Feet);
    }

    public override bool OnInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            var area = M.MapArea;
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
                cursor += mm.ScreenRelative * Mathf.Max(0.05f, G.CurrentSens) * 1.25f * Mathf.Max(0.5f, area.Size.Y / 800f);
            else cursor = mm.Position;
            cursor = new Vector2(Mathf.Clamp(cursor.X, area.Position.X, area.End.X), Mathf.Clamp(cursor.Y, area.Position.Y, area.End.Y));
            return true;
        }
        return Button(e) && OnClick(e);
    }

    protected abstract bool OnClick(InputEvent e);

    /// <summary>Floor under the cursor (null = wall / outside) and whether it is in range.</summary>
    protected (Vector3? Spot, bool InRange) Under(Vector2 at)
    {
        var map = M.TMap;
        var spot = map.FloorAt(at, 1.0f);
        if (spot is not { } p) return (null, false);
        bool inRange = M.Kit.Range <= 0f || new Vector2(p.X - M.Feet.X, p.Z - M.Feet.Z).Length() <= M.Kit.Range;
        return (p, inRange);
    }
}

/// <summary>Brimstone / Clove / Miks: mark spots on the tactical map, confirm, the smokes drop in after the deploy time.</summary>
sealed class MapDropAbility : MapAbility
{
    public readonly List<Vector3> Marks = new();
    public MapDropAbility(SmokeExecuteMode m) : base(m, m.Kit.SmokeKey, m.Kit.Charges) { }
    public override string Name => M.Kit.Ability;

    public override void Unequip() { Marks.Clear(); base.Unequip(); }

    protected override bool OnClick(InputEvent e)
    {
        if (Pressed(e, MouseButton.Left))
        {
            var map = M.TMap;
            float rpx = M.Kit.Radius * map.Scale;
            for (int i = 0; i < Marks.Count; i++)
                if (map.ToScreen(Marks[i]).DistanceTo(cursor) <= Mathf.Max(10f, rpx * 0.6f)) { Marks.RemoveAt(i); G.Sound("tick", 0.4f, 0.8f); return true; }
            if (Marks.Count >= Charges) { G.Sound("fail", 0.35f); return true; }
            var (spot, ok) = Under(cursor);
            if (spot is { } p && ok) { Marks.Add(p); G.Sound("tick", 0.5f, 1.2f); }
            else G.Sound("fail", 0.35f);
            return true;
        }
        if (Pressed(e, MouseButton.Right))
        {
            if (Marks.Count > 0) Launch();
            return true;
        }
        return Button(e);
    }

    void Launch()
    {
        foreach (var p in Marks)
        {
            M.DeploySphere(p, M.Kit.Deploy, M.Kit.FormTime, M.Kit.Duration, lead: LeadKind.Drop, from: p);
            Charges--;
        }
        M.Whoosh(0.6f, 0.8f);
        Marks.Clear();
        Unequip();
    }

    /// <summary>Time ran out with marks on the map: launch them (the drill's placement timer).</summary>
    public void LaunchPending() { if (Equipped && Marks.Count > 0) Launch(); else if (Equipped) Unequip(); }

    public override void Auto(List<PlanSmoke> spots)
    {
        Equip();
        foreach (var s in spots.Take(Charges)) Marks.Add(s.Ground);
        Launch();
    }
}

/// <summary>A star Astra placed in Astral Form.</summary>
sealed class AstraStar
{
    public Vector3 Ground;
    public Node3D? Node;
    public bool Used;
}

/// <summary>Astra's Astral Form: the whole map, click to place / take back stars, right-click or X to return. Can't move meanwhile.</summary>
sealed class AstralAbility : MapAbility
{
    public AstralAbility(SmokeExecuteMode m) : base(m, Key.X, 1) { }
    public override string Name => "Astral Form";
    public override bool BlocksMovement => Equipped;
    public override bool Spent => !Equipped && M.StarsLeft <= 0;
    public override string Status => $"{M.StarsLeft} star{(M.StarsLeft == 1 ? "" : "s")}";
    public bool Visited;

    public override bool OnKey()
    {
        if (Equipped) { Unequip(); return true; }
        Equip();
        Visited = true;
        M.Whoosh(0.4f, 1.3f);
        return true;
    }

    protected override bool OnClick(InputEvent e)
    {
        if (Pressed(e, MouseButton.Left))
        {
            var map = M.TMap;
            foreach (var s in M.Stars)
                if (!s.Used && map.ToScreen(s.Ground).DistanceTo(cursor) <= 14f) { M.RecallStar(s, instant: true); G.Sound("tick", 0.4f, 0.8f); return true; }
            if (M.StarsLeft <= 0) { G.Sound("fail", 0.35f); return true; }
            var (spot, _) = Under(cursor);
            if (spot is { } p) { M.PlaceStar(p); G.Sound("tick", 0.5f, 1.4f); }
            else G.Sound("fail", 0.35f);
            return true;
        }
        if (Pressed(e, MouseButton.Right)) { Unequip(); return true; }
        return Button(e);
    }

    public override void Auto(List<PlanSmoke> spots)
    {
        foreach (var s in spots.Take(M.StarsLeft)) M.PlaceStar(s.Ground);
        Visited = true;
    }
}

/// <summary>Astra's Nebula (E on the star nearest the crosshair) and Dissipate (F: a one-second fake, the star comes back later).</summary>
sealed class NebulaAbility : SmokeAbility
{
    public NebulaAbility(SmokeExecuteMode m) : base(m, Key.E, m.Kit.Charges) { }
    public override string Name => "Nebula";
    public override bool Spent => Charges <= 0 || M.Stars.All(s => s.Used);
    const float AimCone = 11f;

    /// <summary>The unused star closest to the crosshair inside the targeting cone (through walls).</summary>
    public AstraStar? Target()
    {
        AstraStar? best = null;
        float bestA = AimCone;
        foreach (var s in M.Stars)
        {
            if (s.Used) continue;
            float a = G.View.AngleTo(s.Ground + new Vector3(0, 1f, 0));
            if (a < bestA) { bestA = a; best = s; }
        }
        return best;
    }

    public override bool OnKey()
    {
        if (M.AstralOpen) return false;
        var t = Target();
        if (t == null || Charges <= 0) { G.Sound("fail", 0.35f); return true; }
        Activate(t);
        return true;
    }

    void Activate(AstraStar s)
    {
        Charges--;
        s.Used = true;
        M.DeploySphere(s.Ground, M.Kit.Deploy, M.Kit.FormTime, M.Kit.Duration, lead: LeadKind.None, from: s.Ground);
        M.StarConsumed(s);
        M.Whoosh(0.5f, 1.2f);
    }

    /// <summary>F: a short fake smoke at the targeted star; the star is recalled (back after 25 s).</summary>
    public bool Dissipate()
    {
        if (M.AstralOpen) return false;
        var t = Target();
        if (t == null) { G.Sound("fail", 0.35f); return true; }
        M.DeploySphere(t.Ground, 0.35f, 0.3f, 1f, lead: LeadKind.None, from: t.Ground, scored: false);
        M.RecallStar(t, instant: false);
        return true;
    }

    public override void Auto(List<PlanSmoke> spots)
    {
        foreach (var s in spots.Take(Charges))
        {
            var star = M.Stars.Where(x => !x.Used).OrderBy(x => x.Ground.DistanceTo(s.Ground)).FirstOrDefault();
            if (star != null) Activate(star);
        }
    }
}

// =====================================================================================================================
// Aimed marker (Omen Dark Cover, Harbor Cove)
// =====================================================================================================================

/// <summary>
/// Omen's Dark Cover / Harbor's Cove: the marker sits on the crosshair line; HOLD fire pushes it out, HOLD alt-fire pulls
/// it in (through walls), the ability key throws an invisible orb (64 m/s) and the smoke sinks to the floor below the
/// marker. R toggles an overhead tactical view of where the marker is.
/// </summary>
sealed class AimAbility : SmokeAbility
{
    float dist, holdT;
    bool outHeld, inHeld;
    public bool Overhead;
    Node3D? marker;
    MeshInstance3D? beam;
    public Vector3 Point { get; private set; }
    public Vector3 Landing { get; private set; }
    public bool InRange { get; private set; } = true;

    public AimAbility(SmokeExecuteMode m) : base(m, m.Kit.SmokeKey, m.Kit.Charges) { }
    public override string Name => M.Kit.Ability;
    public override bool MapOpen => Equipped && Overhead;
    public float Distance => dist;

    public override void Equip()
    {
        base.Equip();
        float hit = Collision.FirstHit(G.View.Eye, G.View.Forward, G.Solid);
        dist = Mathf.Clamp(float.IsInfinity(hit) ? 20f : hit, 3f, M.Kit.Range);
        outHeld = inHeld = false;
        holdT = 0;
        BuildMarker();
        Update(0);
    }

    public override void Unequip()
    {
        base.Unequip();
        Overhead = false;
        FreeMarker();
    }

    public override bool OnKey()
    {
        if (!Equipped) return base.OnKey();
        Throw();
        return true;
    }

    public override bool OnInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) { outHeld = mb.Pressed; if (mb.Pressed) holdT = 0; return true; }
            if (mb.ButtonIndex == MouseButton.Right) { inHeld = mb.Pressed; if (mb.Pressed) holdT = 0; return true; }
        }
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.R }) { Overhead = !Overhead; return true; }
        return false;
    }

    public override void Update(float dt)
    {
        if (!Equipped) return;
        if (outHeld ^ inHeld)
        {
            holdT += dt;
            float speed = 7f + 26f * Mathf.Clamp(holdT / 1.1f, 0f, 1f);
            dist += (outHeld ? 1 : -1) * speed * dt;
        }
        dist = Mathf.Clamp(dist, 2f, M.Kit.Range);
        var eye = G.View.Eye;
        var p = eye + G.View.Forward * dist;
        float ground = Collision.Ground(p, G.Solid);
        if (p.Y < ground + 0.2f) p.Y = ground + 0.2f;
        Point = p;
        Landing = new Vector3(p.X, ground, p.Z);
        InRange = eye.DistanceTo(Landing) <= M.Kit.Range + 1f;
        if (marker != null)
        {
            marker.Position = ExecutePlan.CenterFor(Landing, M.Kit.Radius);
            if (beam != null)
            {
                float h = p.Y - (Landing.Y + 0.1f);
                beam.Visible = h > 1.2f;
                if (beam.Visible) { beam.GlobalPosition = new Vector3(p.X, Landing.Y + h / 2f, p.Z); beam.Scale = new Vector3(0.12f, h, 1f); }
            }
        }
    }

    void Throw()
    {
        if (Charges <= 0) return;
        var eye = G.View.Eye;
        var center = ExecutePlan.CenterFor(Landing, M.Kit.Radius);
        float travel = eye.DistanceTo(center) / 64f;
        M.DeploySphere(Landing, travel, 1f, M.Kit.Duration, lead: LeadKind.Missile, from: eye + G.View.Forward * 0.6f);
        Charges--;
        M.Whoosh(0.55f, 0.7f);
        if (Charges <= 0) Unequip();
        else { float hit = Collision.FirstHit(G.View.Eye, G.View.Forward, G.Solid); dist = Mathf.Clamp(float.IsInfinity(hit) ? 20f : hit, 3f, M.Kit.Range); }
    }

    public override void Auto(List<PlanSmoke> spots)
    {
        foreach (var s in spots.Take(Charges).ToList())
        {
            var eye = G.View.Eye;
            float travel = eye.DistanceTo(ExecutePlan.CenterFor(s.Ground, M.Kit.Radius)) / 64f;
            M.DeploySphere(s.Ground, travel, 1f, M.Kit.Duration, lead: LeadKind.Missile, from: eye);
            Charges--;
        }
    }

    void BuildMarker()
    {
        FreeMarker();
        var col = M.Kit.Look.Rim;
        var mat = new ShaderMaterial { Shader = SmokeAssets.XrayShader };
        mat.SetShaderParameter("kind", 0);
        mat.SetShaderParameter("color", new Color(0.45f, 0.7f, 1f).Lerp(col, 0.35f));
        mat.SetShaderParameter("energy", 1.3f);
        mat.SetShaderParameter("opacity", 0.9f);
        var sphere = new MeshInstance3D { Mesh = SmokeAssets.Sphere, MaterialOverride = mat, Scale = Vector3.One * M.Kit.Radius, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        marker = new Node3D { Name = "SmokeMarker" };
        marker.AddChild(sphere);
        var bm = new ShaderMaterial { Shader = SmokeAssets.XrayShader };
        bm.SetShaderParameter("kind", 4);
        bm.SetShaderParameter("color", new Color(0.5f, 0.75f, 1f));
        bm.SetShaderParameter("opacity", 0.8f);
        beam = new MeshInstance3D { Mesh = SmokeAssets.Quad, MaterialOverride = bm, TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        marker.AddChild(beam);
        G.World.AddChild(marker);
    }

    void FreeMarker()
    {
        if (marker != null && GodotObject.IsInstanceValid(marker)) marker.QueueFree();
        marker = null;
        beam = null;
    }

    public override void Clear() { base.Clear(); FreeMarker(); }
}

// =====================================================================================================================
// Viper: Poison Cloud (thrown emitter) and Toxic Screen (wall), both switched on / off with fuel
// =====================================================================================================================

/// <summary>Viper's fuel: 100; one gas on drains 8.33/s (12 s), both 12.5/s (8 s); refills 3.33/s while both are off;
/// switching on needs 30; a gas can be switched off 2 s after it went on and on again 5 s after it went off.</summary>
sealed class ViperFuel
{
    public float Fuel = 100f;
    public const float Min = 30f;
    public void Update(float dt, int on)
    {
        if (on == 0) Fuel = Mathf.Min(100f, Fuel + 3.333f * dt);
        else Fuel = Mathf.Max(0f, Fuel - (on >= 2 ? 12.5f : 8.333f) * dt);
    }
}

/// <summary>Shared on/off logic of Viper's two gases.</summary>
abstract class ViperGas : SmokeAbility
{
    public bool Deployed;
    public SmokeVolume? Active;
    float onAt = -99f, offAt = -99f;
    /// <summary>Only the first activation of a gas counts for the round's placement score.</summary>
    protected bool Scored;
    protected ViperGas(SmokeExecuteMode m, Key key) : base(m, key, 1) { }
    public bool On => Active != null && Active.DownAt > M.Clock;
    public override bool Spent => Deployed && !Equipped;
    public override string Status => !Deployed ? (Equipped ? "AIM" : "READY") : On ? "ON" : "OFF";

    public override bool OnKey()
    {
        if (!Deployed) return base.OnKey();
        Toggle();
        return true;
    }

    public void Toggle()
    {
        float now = M.Clock;
        if (On)
        {
            if (now - onAt < 2f) { G.Sound("fail", 0.3f); return; }
            SwitchOff(now);
            return;
        }
        if (M.Fuel.Fuel < ViperFuel.Min || now - offAt < 5f) { G.Sound("fail", 0.3f); return; }
        onAt = now;
        Active = SwitchOn(now);
        Scored = true;
        M.Whoosh(0.45f, 0.6f);
    }

    public void SwitchOff(float now)
    {
        if (Active == null) return;
        Active.DownAt = now;
        offAt = now;
    }

    protected abstract SmokeVolume SwitchOn(float now);

    public override void Clear()
    {
        base.Clear();
        Deployed = false;
        Active = null;
        Scored = false;
        onAt = offAt = -99f;
    }
}

/// <summary>Viper's Poison Cloud: FIRE throws the emitter, ALT FIRE lobs it underhand; it sticks on the first floor it
/// touches (walls bounce it). The ability key then switches the gas cloud on / off.</summary>
sealed class OrbAbility : ViperGas
{
    Vector3 pos, vel;
    bool flying;
    int bounces;
    MeshInstance3D? orb;
    public Vector3 Ground { get; private set; }
    public OrbAbility(SmokeExecuteMode m) : base(m, m.Kit.SmokeKey) { }
    public override string Name => "Poison Cloud";
    public override string Status => flying ? "FLYING" : base.Status;
    public override bool Spent => Deployed && !Equipped && !flying;

    public override bool OnInput(InputEvent e)
    {
        if (flying) return Button(e);
        if (Pressed(e, MouseButton.Left)) { Launch(false); return true; }
        if (Pressed(e, MouseButton.Right)) { Launch(true); return true; }
        return Button(e);
    }

    /// <summary>Initial position / velocity of a throw from the current view.</summary>
    public (Vector3 Pos, Vector3 Vel) ThrowFrom(bool underhand)
    {
        var f = G.View.Forward;
        var eye = G.View.Eye;
        if (underhand)
        {
            var flat = new Vector3(f.X, 0, f.Z).Normalized();
            return (eye + flat * 0.4f + Vector3.Down * 0.5f, flat * 6.5f + Vector3.Up * 3.2f);
        }
        return (eye + f * 0.5f, f * 15.5f + Vector3.Up * 1.6f);
    }

    void Launch(bool underhand)
    {
        (pos, vel) = ThrowFrom(underhand);
        flying = true;
        bounces = 0;
        Equipped = false;
        G.SetAbilityInHand(false, 0.45f);
        var mat = new ShaderMaterial { Shader = SmokeAssets.RingShader };
        mat.SetShaderParameter("kind", 3);
        mat.SetShaderParameter("color", M.Kit.Look.Lead);
        mat.SetShaderParameter("energy", 2f);
        orb = new MeshInstance3D { Mesh = SmokeAssets.SmallSphere, MaterialOverride = mat, Scale = Vector3.One * 0.13f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        G.World.AddChild(orb);
        orb.GlobalPosition = pos;
        M.Whoosh(0.4f, 1.4f);
    }

    /// <summary>Simulates a throw to where it sticks (used by the live projectile and the low-tier preview).</summary>
    public static List<Vector3> Simulate(IReadOnlyList<Box> solid, Vector3 p, Vector3 v, out Vector3 landing, float maxT = 4f)
    {
        var pts = new List<Vector3> { p };
        landing = p;
        int bounces = 0;
        const float dt = 1f / 60f;
        for (float t = 0; t < maxT; t += dt)
        {
            if (Step(solid, ref p, ref v, dt, ref bounces, out bool landed)) { }
            pts.Add(p);
            if (landed) { landing = p; return pts; }
        }
        landing = new Vector3(p.X, Collision.Ground(p, solid), p.Z);
        return pts;
    }

    /// <summary>One integration step: gravity, wall bounces, sticks on floors.</summary>
    static bool Step(IReadOnlyList<Box> solid, ref Vector3 p, ref Vector3 v, float dt, ref int bounces, out bool landed)
    {
        landed = false;
        v += Vector3.Down * 9.8f * dt;
        var d = v * dt;
        float len = d.Length();
        if (len < 1e-5f) return false;
        var dir = d / len;
        float hit = Collision.FirstHit(p, dir, solid, out int bi);
        if (hit <= len)
        {
            var hp = p + dir * Mathf.Max(0f, hit - 0.01f);
            var n = solid[bi].NormalAt(p + dir * hit);
            if (n.Y > 0.7f || bounces >= 6)
            {
                p = new Vector3(hp.X, Collision.Ground(hp + Vector3.Up * 0.05f, solid), hp.Z);
                landed = true;
                return true;
            }
            v = (v - 2f * v.Dot(n) * n) * 0.42f;
            p = hp;
            bounces++;
            return true;
        }
        p += d;
        if (p.Y < -5f) { p.Y = 0; landed = true; }
        return false;
    }

    public override void Update(float dt)
    {
        if (!flying) return;
        int steps = Math.Max(1, (int)Mathf.Ceil(dt / (1f / 120f)));
        for (int i = 0; i < steps && flying; i++)
        {
            Step(G.Solid, ref pos, ref vel, dt / steps, ref bounces, out bool landed);
            if (landed) Land(pos);
        }
        if (orb != null && GodotObject.IsInstanceValid(orb)) orb.GlobalPosition = pos + Vector3.Up * 0.12f;
    }

    void Land(Vector3 p)
    {
        flying = false;
        Deployed = true;
        Charges = 0;
        Ground = p;
        if (orb != null && GodotObject.IsInstanceValid(orb)) orb.GlobalPosition = p + Vector3.Up * 0.12f;
        M.EmitterPlaced(p);
        G.SoundAt("tick", p, 30f, 0.5f, 0.8f);
    }

    protected override SmokeVolume SwitchOn(float now) =>
        M.DeploySphere(Ground, M.Kit.Deploy, M.Kit.FormTime, float.PositiveInfinity, lead: LeadKind.None, from: Ground, scored: !Scored);

    public override void Auto(List<PlanSmoke> spots)
    {
        if (spots.Count == 0) return;
        Land(spots[0].Ground);
        Toggle();
    }

    public override void Clear()
    {
        base.Clear();
        flying = false;
        if (orb != null && GodotObject.IsInstanceValid(orb)) orb.QueueFree();
        orb = null;
        Charges = 1;
    }
}

/// <summary>Viper's Toxic Screen: FIRE sends a launcher along your aim (through walls) that drops an emitter every 2 m for up
/// to 60 m; the ability key raises / drops the 4 m gas wall along them.</summary>
sealed class ScreenAbility : ViperGas
{
    public readonly List<Vector3> Points = new();
    public ScreenAbility(SmokeExecuteMode m) : base(m, m.Kit.WallKey) { }
    public override string Name => "Toxic Screen";

    public override bool OnInput(InputEvent e)
    {
        if (Pressed(e, MouseButton.Left)) { Fire(G.View.Eye, G.View.Yaw); return true; }
        return Button(e);
    }

    /// <summary>Emitter line from the eye along a yaw: every 2 m, dropped to the floor under the launcher.</summary>
    public static List<Vector3> Line(IReadOnlyList<Box> solid, Vector3 eye, float yawDeg, Rect2 bounds)
    {
        var pts = new List<Vector3>();
        float y = Mathf.DegToRad(yawDeg);
        var dir = new Vector3(Mathf.Sin(y), 0, -Mathf.Cos(y));
        for (int i = 0; i < 30; i++)
        {
            var p = eye + dir * (1f + i * 2f);
            if (!bounds.HasPoint(new Vector2(p.X, p.Z))) break;
            float g = Collision.Ground(new Vector3(p.X, eye.Y - 0.7f, p.Z), solid);
            pts.Add(new Vector3(p.X, g, p.Z));
        }
        return pts;
    }

    void Fire(Vector3 eye, float yaw)
    {
        Points.Clear();
        Points.AddRange(Line(G.Solid, eye, yaw, M.WorldBounds));
        if (Points.Count < 2) { G.Sound("fail", 0.3f); return; }
        Deployed = true;
        Charges = 0;
        Unequip();
        M.WallPlaced(Points);
        M.Whoosh(0.45f, 1.1f);
    }

    protected override SmokeVolume SwitchOn(float now) =>
        M.DeployWall(Points, 4f, 0.5f, 1.55f / 30f, float.PositiveInfinity, 0.45f, 0.8f, M.Kit.WallLook ?? M.Kit.Look, scored: !Scored);

    public override void Auto(List<PlanSmoke> spots)
    {
        if (spots.Count == 0) return;
        var target = spots[Math.Min(1, spots.Count - 1)].Ground;
        var eye = G.View.Eye;
        var d = target - eye;
        Fire(eye, Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)));
        Toggle();
    }

    public override void Clear() { base.Clear(); Points.Clear(); Charges = 1; }
}

// =====================================================================================================================
// Harbor: High Tide (guided water wall)
// =====================================================================================================================

/// <summary>Harbor's High Tide: FIRE sends the wave (25 m/s) along the floor and through walls; HOLD FIRE steers it toward
/// your aim (up to 180°/s); ALT FIRE stops it early; at 60 m or on stop an 8 m water wall rises from you along its path
/// (15 s).</summary>
sealed class TideAbility : SmokeAbility
{
    readonly List<Vector3> path = new();
    bool running, steer;
    Vector3 head;
    float heading, travelled, lastY;
    MeshInstance3D? orb;
    public TideAbility(SmokeExecuteMode m) : base(m, m.Kit.WallKey, 1) { }
    public override string Name => M.Kit.WallAbility;
    public override bool Spent => Charges <= 0 && !Equipped && !running;
    public override string Status => running ? "FLOWING" : base.Status;

    public override bool OnInput(InputEvent e)
    {
        if (!running && Pressed(e, MouseButton.Left)) { Launch(); steer = true; return true; }
        if (running && Released(e, MouseButton.Left)) { steer = false; return true; }
        if (running && Pressed(e, MouseButton.Right)) { Stop(); return true; }
        return Button(e);
    }

    void Launch()
    {
        float y = Mathf.DegToRad(G.View.Yaw);
        heading = G.View.Yaw;
        var feet = M.Feet;
        head = feet + new Vector3(Mathf.Sin(y), 0, -Mathf.Cos(y)) * 1f;
        lastY = feet.Y;
        head.Y = Collision.Ground(new Vector3(head.X, lastY + 0.7f, head.Z), G.Solid);
        path.Clear();
        path.Add(head);
        travelled = 0;
        running = true;
        var mat = new ShaderMaterial { Shader = SmokeAssets.RingShader };
        mat.SetShaderParameter("kind", 3);
        mat.SetShaderParameter("color", M.Kit.Look.Lead);
        mat.SetShaderParameter("energy", 2f);
        orb = new MeshInstance3D { Mesh = SmokeAssets.SmallSphere, MaterialOverride = mat, Scale = Vector3.One * 0.45f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        G.World.AddChild(orb);
        M.Whoosh(0.5f, 0.6f);
    }

    public override void Update(float dt)
    {
        if (!running) return;
        if (steer && Equipped)
        {
            float want = Mathf.Wrap(G.View.Yaw - heading, -180f, 180f);
            heading += Mathf.Clamp(want, -180f * dt, 180f * dt);
        }
        float step = 25f * dt;
        float y = Mathf.DegToRad(heading);
        var dir = new Vector3(Mathf.Sin(y), 0, -Mathf.Cos(y));
        head += dir * step;
        travelled += step;
        float g = Collision.Ground(new Vector3(head.X, lastY + 0.7f, head.Z), G.Solid);
        head.Y = g;
        lastY = g;
        if (path.Count == 0 || new Vector2(head.X - path[^1].X, head.Z - path[^1].Z).Length() >= 1f) path.Add(head);
        if (orb != null && GodotObject.IsInstanceValid(orb)) orb.GlobalPosition = head + Vector3.Up * 0.6f;
        if (travelled >= 60f || !M.WorldBounds.HasPoint(new Vector2(head.X, head.Z))) Stop();
    }

    void Stop()
    {
        running = false;
        if (orb != null && GodotObject.IsInstanceValid(orb)) orb.QueueFree();
        orb = null;
        if (path.Count >= 2)
        {
            M.DeployWall(path, 8f, 0.05f, 1f / 30f, 15f, 0.4f, 1.5f, M.Kit.WallLook ?? M.Kit.Look);
            Charges = 0;
        }
        Unequip();
    }

    public override void Auto(List<PlanSmoke> spots)
    {
        if (spots.Count == 0) return;
        var target = spots[Math.Min(1, spots.Count - 1)].Ground;
        var feet = M.Feet;
        var d = target - feet;
        d.Y = 0;
        float len = Mathf.Min(60f, d.Length() + 6f);
        var dir = d.Normalized();
        path.Clear();
        float ly = feet.Y;
        for (float s = 1f; s <= len; s += 1f)
        {
            var p = feet + dir * s;
            float g = Collision.Ground(new Vector3(p.X, ly + 0.7f, p.Z), G.Solid);
            ly = g;
            path.Add(new Vector3(p.X, g, p.Z));
        }
        M.DeployWall(path, 8f, 0.05f, 1f / 30f, 15f, 0.4f, 1.5f, M.Kit.WallLook ?? M.Kit.Look);
        Charges = 0;
    }

    public override void Clear()
    {
        base.Clear();
        running = false;
        if (orb != null && GodotObject.IsInstanceValid(orb)) orb.QueueFree();
        orb = null;
        path.Clear();
        Charges = 1;
    }
}
