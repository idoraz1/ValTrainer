using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Skins;

/// <summary>Settings → Kill effects (<see cref="AppSettings.SkinKillFx"/>).</summary>
public enum SkinKillFxMode { Off = 0, GunOnly = 1, Full = 2 }

/// <summary>
/// Pooled world kill effects and finishers (skins 3: skins3-contracts §2–3). One per session under <c>Effects.Root</c>
/// (built at Restart, pre-warmed below the world), or one inside the skin picker's SHOWCASE world: it works under any
/// Node3D in any (sub-)viewport, needs no GameSession or Effects.Root, and uses that viewport's camera for the close-kill rule.
/// <para>It only builds the pools the equipped line's recipes need (<see cref="KillFxRecipes"/>): 3 kill slots (round-robin;
/// a 4th concurrent kill reuses the oldest) and 1 finisher slot. Every slot pre-holds its nodes, so spawning only sets
/// transforms and instance uniforms and restarts emitters: no allocations and no shader compiles during play. Readability
/// rules are enforced here, not left to the recipes: kill effects ≤ <see cref="KillMaxLife"/> s and low to the ground,
/// finishers ≤ <see cref="FullMaxLife"/> s and only on the round-winning kill of the last enemy (KillTracker); bright lines
/// over a dark under-glow so they read on bright and dark floors, coverage capped (skinfx shaders); spawn-clear fast fade
/// (<see cref="ClearNear"/>); close kills (≤ 3 m from the camera) at reduced size; the palette's <see cref="FxPalette.Alt"/>
/// replaces any colour close to the enemy highlight. No lights, decals or screen-space passes; world layer 1 only (never on
/// the viewmodel).</para>
/// </summary>
public partial class KillFxWorld : Node3D
{
    /// <summary>Settings → Kill effects.</summary>
    public static SkinKillFxMode Mode => (SkinKillFxMode)Math.Clamp(Main.I?.Settings?.SkinKillFx ?? 2, 0, 2);
    /// <summary>Settings → Finishers.</summary>
    public static bool FinishersOn => Main.I?.Settings?.SkinFinishers ?? true;

    /// <summary>Longest kill effect (s).</summary>
    public const float KillMaxLife = 1.0f;
    /// <summary>Longest finisher (s). The skin picker waits this long before the next dummy stands up.</summary>
    public const float FullMaxLife = 3.0f;
    /// <summary>Spawn-clear fade time (s).</summary>
    public const float ClearFadeTime = 0.12f;
    /// <summary>A death point this close to the camera (m) gets a smaller effect (kill: half size, finisher: 70 %).</summary>
    public const float CloseKill = 3f;
    const int KillSlots = 3, PrewarmFrames = 3;
    const uint WorldMask = 1u;

    /// <summary>The skin picker's SHOWCASE: plays regardless of Settings → Kill effects / Finishers (the picker previews
    /// what a level contains). Drills never set it.</summary>
    public bool Showcase { get; set; }

    readonly FxSlot?[] kills = new FxSlot?[KillSlots];
    FxSlot? finFull;
    int nextKill;
    string? builtKill, builtFin;
    bool built;
    int prewarm;
    /// <summary>Particle amount scale (Low quality: half).</summary>
    float ratio = 1f;

    /// <summary>Builds the effect pools for <paramref name="state"/> under <paramref name="root"/> (null state = nothing to
    /// build; still returns a node so callers needn't null-check).</summary>
    public static KillFxWorld Create(Node3D root, SkinState? state) => Create(root, state, false);

    /// <summary><see cref="Create(Node3D, SkinState?)"/> for the skin picker's SHOWCASE (<paramref name="showcase"/> = true
    /// ignores the effect settings).</summary>
    public static KillFxWorld Create(Node3D root, SkinState? state, bool showcase)
    {
        var w = new KillFxWorld { Name = "KillFxWorld", Showcase = showcase };
        root.AddChild(w);
        w.Setup(state);
        return w;
    }

    bool KillsOn => Showcase || Mode == SkinKillFxMode.Full;
    bool FinOn => Showcase || (Mode == SkinKillFxMode.Full && FinishersOn);

    /// <summary>(Re)builds the pools for a new equipped state (gun switch, re-skin) and pre-warms them. Nothing is rebuilt
    /// when the recipes didn't change; settings Off build nothing (exactly the v1.19 cost).</summary>
    public void Setup(SkinState? state)
    {
        string? k = KillsOn ? state?.KillFx : null;
        string? f = FinOn ? state?.Finisher : null;
        if (built && k == builtKill && f == builtFin) return;
        FreePools();
        built = true;
        builtKill = k; builtFin = f;
        ratio = (Main.I?.Settings?.Quality ?? 1) <= 0 ? 0.5f : 1f;
        if (k != null)
            for (int i = 0; i < KillSlots; i++) kills[i] = Build(k, finisher: false, KillMaxLife);
        if (f != null) finFull = Build(f, finisher: true, FullMaxLife);
        if (k != null || f != null)
        {
            if (IsInsideTree()) Prewarm();
            else prewarmOnReady = true; // a stage built before it enters its (sub-)viewport
        }
    }

    bool prewarmOnReady;

    public override void _Ready()
    {
        if (prewarmOnReady) { prewarmOnReady = false; Prewarm(); }
    }

    FxSlot? Build(string id, bool finisher, float maxLife)
    {
        var s = new FxSlot { Recipe = id };
        float life = KillFxRecipes.Build(this, s, id, finisher);
        if (s.Parts.Count == 0) { GD.PushWarning($"[skinfx] unknown effect recipe '{id}'"); return null; }
        s.Life = Mathf.Min(life, maxLife);
        foreach (var p in s.Parts)
        {
            if (p.Emitter is { } e)
            {
                // a burst must be gone by the end of the slot: shorten its particles' lifetime if needed
                float room = s.Life - p.Start - (p.Stream ? p.Dur : 0f);
                if (room < e.Lifetime) e.Lifetime = Mathf.Max(0.1f, room);
            }
            else p.Dur = Mathf.Min(p.Dur, s.Life - p.Start);
        }
        return s;
    }

    void FreePools()
    {
        foreach (var c in GetChildren()) c.QueueFree();
        Array.Clear(kills);
        finFull = null;
        builtKill = builtFin = null;
        built = false;
        prewarm = 0;
    }

    /// <summary>One invisible run of every part 200 m below the world, so no pipeline compiles on the first kill.</summary>
    void Prewarm()
    {
        var hidden = new Vector3(0, -200f, 0);
        foreach (var s in Slots())
        {
            foreach (var p in s.Parts)
            {
                p.Node.GlobalPosition = hidden;
                p.Node.Visible = true;
                if (p.Emitter is { } e) { e.SetInstanceShaderParameter(FadeP, 0f); e.Restart(); e.Emitting = true; }
                else if (p.Mesh is { } m) { m.SetInstanceShaderParameter(FadeP, 0f); m.SetInstanceShaderParameter(TP, 0.5f); }
                else if (p.Label is { } l) l.Modulate = new Color(1, 1, 1, 0);
            }
        }
        prewarm = PrewarmFrames;
    }

    IEnumerable<FxSlot> Slots()
    {
        foreach (var s in kills) if (s != null) yield return s;
        if (finFull != null) yield return finFull;
    }

    // ---------------- spawning ----------------

    static readonly StringName TP = "t", FadeP = "fade", MainP = "col_main", AccentP = "col_accent", AltP = "col_alt", SeedP = "seed",
        TintP = "tint", Tint2P = "tint2", Tint3P = "tint3";
    static readonly Color Neutral = new("#DDE6EA");
    readonly Random rng = new();

    /// <summary>The world kill effect at <paramref name="kill"/> (half size when close to the camera), colours from <paramref name="state"/>.</summary>
    public void Kill(in KillInfo kill, SkinState state, Color enemy)
    {
        if (!KillsOn || state.KillFx is not { } id || !IsInsideTree()) return;
        if (id != builtKill) Setup(state); // a gun / skin change without a Setup (rare: allocates once)
        var s = kills[nextKill];
        if (s == null) return;
        nextKill = (nextKill + 1) % KillSlots;
        bool close = IsClose(kill.Feet);
        Play(s, kill, close ? 0.5f : 1f, state, enemy);
        if (s.Label is { } label)
            label.Text = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{kill.Dist:0.0} m");
    }

    /// <summary>The finisher at the cue's kill (there is no lite version). Never delays the run's end; freed with the
    /// session. A kill within <see cref="CloseKill"/> m of the camera plays it at 70 % size.</summary>
    public void Finisher(in FinisherCue cue, SkinState state, Color enemy)
    {
        if (!FinOn || state.Finisher is not { } id || !IsInsideTree()) return;
        if (id != builtFin) Setup(state);
        if (finFull is not { } s) return;
        Play(s, cue.Kill, IsClose(cue.Kill.Feet) ? 0.7f : 1f, state, enemy);
    }

    bool IsClose(Vector3 feet)
    {
        var cam = GetViewport()?.GetCamera3D();
        return cam != null && cam.GlobalPosition.DistanceTo(feet + new Vector3(0, 0.9f, 0)) < CloseKill;
    }

    void Play(FxSlot s, in KillInfo kill, float scale, SkinState state, Color enemy)
    {
        if (s.Live) Stop(s);
        var (main, accent, alt) = SafeColors(state.Palette, enemy);
        var cam = GetViewport()?.GetCamera3D();
        var away = cam != null ? kill.Feet - cam.GlobalPosition : Vector3.Forward;
        away.Y = 0;
        away = away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Forward;
        s.Live = true;
        s.Age = 0f;
        s.Fade = 1f;
        s.FadeV = 0f;
        s.Feet = kill.Feet;
        s.ChestH = Mathf.Clamp(kill.Chest.Y - kill.Feet.Y, 0.45f, 0.9f); // bursts start at the chest but never above 0.9 m
        s.Scale = scale;
        s.Away = away;
        s.Right = Vector3.Up.Cross(-away).Normalized();
        s.Frame = new Basis(s.Right, Vector3.Up, -away); // x = camera right, y = up, z = towards the camera
        float seed = (float)rng.NextDouble() * 100f;
        foreach (var p in s.Parts)
        {
            p.Started = p.Ended = false;
            p.Node.Visible = false;
            if (p.Emitter is { } e)
            {
                e.Emitting = false;
                e.SetInstanceShaderParameter(TintP, p.UseAccent ? accent : main);
                e.SetInstanceShaderParameter(Tint2P, p.UseAccent ? main : accent);
                e.SetInstanceShaderParameter(Tint3P, alt);
                e.SetInstanceShaderParameter(FadeP, 1f);
                e.AmountRatio = Mathf.Clamp(ratio * (scale < 1f ? 0.6f : 1f), 0.05f, 1f);
            }
            else if (p.Mesh is { } m)
            {
                m.SetInstanceShaderParameter(MainP, main);
                m.SetInstanceShaderParameter(AccentP, accent);
                m.SetInstanceShaderParameter(AltP, alt);
                m.SetInstanceShaderParameter(SeedP, seed);
                m.SetInstanceShaderParameter(TP, 0f);
                m.SetInstanceShaderParameter(FadeP, 1f);
            }
            else if (p.Label is { } l) l.Modulate = new Color(main, 0f);
            Place(s, p, 0f);
        }
        Advance(s, 0f);
    }

    /// <summary>Fast-fades (0.12 s) every effect within <paramref name="radius"/> m of a new bot's spawn point.</summary>
    public void ClearNear(Vector3 point, float radius = 2.5f)
    {
        float r2 = radius * radius;
        foreach (var s in Slots())
            if (s.Live && s.FadeV <= 0f && FlatDist2(s.Feet, point) < r2 && Mathf.Abs(s.Feet.Y - point.Y) < 3f)
                s.FadeV = 1f / ClearFadeTime;
    }

    static float FlatDist2(Vector3 a, Vector3 b) { float dx = a.X - b.X, dz = a.Z - b.Z; return dx * dx + dz * dz; }

    /// <summary>Stops and hides everything at once (restart, round reset).</summary>
    public void ClearAll()
    {
        foreach (var s in Slots()) if (s.Live) Stop(s);
    }

    /// <summary>Effects playing now (bench / tests).</summary>
    public int LiveCount { get { int n = 0; foreach (var s in Slots()) if (s.Live) n++; return n; } }

    static void Stop(FxSlot s)
    {
        s.Live = false;
        foreach (var p in s.Parts)
        {
            if (p.Emitter is { } e) e.Emitting = false;
            p.Node.Visible = false;
            p.Started = p.Ended = false;
        }
    }

    // ---------------- update ----------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (prewarm > 0 && --prewarm == 0)
            foreach (var s in Slots()) if (!s.Live) Stop(s);
        foreach (var s in kills) if (s is { Live: true }) Advance(s, dt);
        if (finFull is { Live: true }) Advance(finFull, dt);
    }

    void Advance(FxSlot s, float dt)
    {
        s.Age += dt;
        if (s.FadeV > 0f) s.Fade -= s.FadeV * dt;
        if (s.Age >= s.Life || s.Fade <= 0f) { Stop(s); return; }
        foreach (var p in s.Parts)
        {
            if (p.Ended) continue;
            float local = s.Age - p.Start;
            if (local < 0f) continue;
            if (!p.Started)
            {
                p.Started = true;
                Place(s, p, 0f);
                p.Node.Visible = true;
                if (p.Emitter is { } e) { e.Restart(); e.Emitting = true; }
            }
            if (p.Emitter is { } em)
            {
                // a stream emits for Dur; its particles (and a one-shot burst's) live on for their lifetime
                if (p.Stream && em.Emitting && local >= p.Dur) em.Emitting = false;
                if (local >= (p.Stream ? p.Dur : 0f) + (float)em.Lifetime + 0.05f) { p.Ended = true; p.Node.Visible = false; continue; }
                if (p.Anim != null) Place(s, p, Mathf.Clamp(local / Mathf.Max(p.Dur, 0.01f), 0f, 1f));
                if (s.FadeV > 0f) em.SetInstanceShaderParameter(FadeP, Mathf.Max(s.Fade, 0f));
                continue;
            }
            if (local >= p.Dur) { p.Ended = true; p.Node.Visible = false; continue; }
            float t = Mathf.Clamp(local / Mathf.Max(p.Dur, 0.01f), 0f, 1f);
            if (p.Anim != null) Place(s, p, t);
            if (p.Mesh is { } m)
            {
                m.SetInstanceShaderParameter(TP, t);
                m.SetInstanceShaderParameter(FadeP, s.Fade);
            }
            else if (p.Label is { } l)
            {
                float a = Mathf.Clamp(t / 0.08f, 0f, 1f) * (1f - Mathf.Clamp((t - 0.7f) / 0.3f, 0f, 1f));
                l.Modulate = new Color(l.Modulate, 0.85f * a * s.Fade);
            }
        }
    }

    static void Place(FxSlot s, FxPart p, float t)
    {
        var off = p.Offset;
        var size = p.Size;
        var rot = p.Rot;
        p.Anim?.Invoke(t, ref off, ref size, ref rot);
        if (p.AtChest) off.Y += s.ChestH;
        var pos = s.Feet + (s.Right * off.X + Vector3.Up * off.Y + s.Away * off.Z) * s.Scale;
        var sz = size * s.Scale;
        // the part's own frame: camera right / up / towards the camera, then its rotation (Euler, radians), then flat for
        // ground quads (an XY quad laid in XZ), then its size
        var b = s.Frame;
        if (rot != Vector3.Zero) b *= Basis.FromEuler(rot);
        if (p.Ground) b *= new Basis(Vector3.Right, -Mathf.Pi / 2f);
        b *= Basis.FromScale(p.Ground ? new Vector3(sz.X, sz.Y, 1f) : sz);
        if (p.Ground) pos.Y += 0.02f; // just above the floor (no z-fighting)
        p.Node.GlobalTransform = new Transform3D(b, pos);
    }

    // ---------------- building blocks (KillFxRecipes) ----------------

    internal FxPart Quad(FxSlot s, int kind, Vector2 size, bool ground, int billboard = 0)
    {
        var m = new MeshInstance3D { Mesh = KillFxRecipes.Assets.Quad, Visible = false, ExtraCullMargin = 2f, Layers = WorldMask };
        Common(m);
        AddChild(m);
        m.SetInstanceShaderParameter("kind", kind);
        m.SetInstanceShaderParameter("bb", billboard);
        var p = new FxPart { Node = m, Mesh = m, Ground = ground, Size = new Vector3(size.X, size.Y, 1f) };
        s.Parts.Add(p);
        return p;
    }

    internal FxPart Shell(FxSlot s, int kind, Mesh mesh, Vector3 size)
    {
        var m = new MeshInstance3D { Mesh = mesh, Visible = false, ExtraCullMargin = 1f, Layers = WorldMask };
        Common(m);
        AddChild(m);
        m.SetInstanceShaderParameter("kind", kind);
        m.SetInstanceShaderParameter("bb", 0);
        var p = new FxPart { Node = m, Mesh = m, Size = size };
        s.Parts.Add(p);
        return p;
    }

    internal FxPart Burst(FxSlot s, ParticleProcessMaterial proc, int shape, int amount, float lifetime, bool stream = false, float explosiveness = 1f)
    {
        var e = new GpuParticles3D
        {
            ProcessMaterial = proc,
            DrawPass1 = KillFxRecipes.Assets.ParticleQuad,
            Amount = amount,
            Lifetime = lifetime,
            OneShot = !stream,
            Explosiveness = stream ? 0f : explosiveness,
            Emitting = false,
            LocalCoords = false,
            FixedFps = 60,
            Visible = false,
            Layers = WorldMask,
            VisibilityAabb = new Aabb(new Vector3(-4, -4, -4), new Vector3(8, 8, 8)),
        };
        Common(e);
        AddChild(e);
        e.SetInstanceShaderParameter("shape", shape);
        var p = new FxPart { Node = e, Emitter = e, Stream = stream, Dur = stream ? 1f : 0.05f };
        s.Parts.Add(p);
        return p;
    }

    internal FxPart Label(FxSlot s)
    {
        var l = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            NoDepthTest = false, // walls still hide it
            FixedSize = false,
            PixelSize = 0.005f,
            FontSize = 44,
            OutlineSize = 8,
            OutlineModulate = new Color(0.03f, 0.04f, 0.05f, 0.8f),
            Visible = false,
            Layers = WorldMask,
            Text = "0.0 m",
        };
        if (KillFxRecipes.Assets.LabelFont is { } font) l.Font = font;
        Common(l);
        AddChild(l);
        var p = new FxPart { Node = l, Label = l };
        s.Parts.Add(p);
        s.Label = l;
        return p;
    }

    static void Common(GeometryInstance3D g)
    {
        g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        g.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
    }

    // ---------------- enemy-colour safety (§4.6) ----------------

    /// <summary>The effect colours for <paramref name="enemy"/>: <see cref="FxPalette.Main"/> within NearHue° of the enemy
    /// highlight's hue (both saturated) is replaced by <see cref="FxPalette.Alt"/> (or a neutral pale grey if Alt is close
    /// too); a close accent becomes a pale version of the main colour. No effect is ever drawn in the enemy colour.</summary>
    public static (Color Main, Color Accent) SafePalette(FxPalette p, Color enemy)
    {
        var (m, a, _) = SafeColors(p, enemy);
        return (m, a);
    }

    /// <summary><see cref="SafePalette"/> plus a safe third colour: <see cref="FxPalette.Alt"/> when it is not the main
    /// colour already and not close to the enemy, else a pale version of the accent. Bullet FX and the multi-colour effects
    /// (Glasswork's jewels, Rally's stripes, Aurora's curtain) use all three.</summary>
    public static (Color Main, Color Accent, Color Alt) SafeColors(FxPalette p, Color enemy)
    {
        var (main, accent) = SafeMain(p, enemy);
        var alt = p.Alt;
        if (alt == main || NearEnemy(alt, enemy)) alt = accent.Lerp(Colors.White, 0.4f);
        if (NearEnemy(alt, enemy)) alt = Neutral;
        return (main, accent, alt);
    }

    static (Color Main, Color Accent) SafeMain(FxPalette p, Color enemy)
    {
        var main = p.Main;
        var accent = p.Accent;
        if (NearEnemy(main, enemy))
        {
            main = NearEnemy(p.Alt, enemy) ? Neutral : p.Alt;
            accent = main.Lerp(Colors.White, 0.55f);
        }
        if (NearEnemy(accent, enemy)) accent = main.Lerp(Colors.White, 0.55f);
        if (NearEnemy(accent, enemy)) accent = Neutral;
        return (main, accent);
    }

    /// <summary>Hue distance (degrees) under which an effect colour counts as the enemy's: 22° keeps molten orange (25° from
    /// the red highlight) while amber vs yellow and red vs red still swap to the palette's Alt.</summary>
    public const float NearHue = 27f; // amber (Hive) vs a yellow highlight is ~25°: still too close; orange (Foundry) vs red is 29°

    /// <summary>Within <see cref="NearHue"/>° of hue of a saturated enemy colour (and saturated itself).</summary>
    public static bool NearEnemy(Color c, Color enemy)
    {
        if (c.S <= 0.4f || enemy.S <= 0.25f) return false;
        float d = Mathf.Abs(c.H - enemy.H);
        d = Mathf.Min(d, 1f - d);
        return d < NearHue / 360f;
    }
}

/// <summary>Per-frame motion of a part (t = 0..1 over its life): may change its offset, size and rotation (Euler radians in
/// the part's frame: x = camera right, y = up, z = towards the camera).</summary>
internal delegate void FxAnim(float t, ref Vector3 offset, ref Vector3 size, ref Vector3 rot);

/// <summary>One node of a slot's recipe (quad, shell, emitter or label) and where / when it plays.</summary>
internal sealed class FxPart
{
    public Node3D Node = null!;
    public GpuParticles3D? Emitter;
    public MeshInstance3D? Mesh;
    public Label3D? Label;
    /// <summary>From the feet, in metres: x = camera right, y = up, z = away from the camera. Scaled with close kills.</summary>
    public Vector3 Offset;
    public Vector3 Size = Vector3.One;
    /// <summary>Rotation (Euler radians) in the part's frame (not used by camera-facing billboards).</summary>
    public Vector3 Rot;
    /// <summary>Offset.y counts from the chest height (≤ 0.9 m) instead of the feet.</summary>
    public bool AtChest;
    /// <summary>A flat quad on the ground (XY quad laid in XZ).</summary>
    public bool Ground;
    /// <summary>Seconds on the slot's timeline.</summary>
    public float Start, Dur = 1f;
    /// <summary>The emitter keeps emitting for <see cref="Dur"/> (rain).</summary>
    public bool Stream;
    /// <summary>Particles use the accent as their main tint.</summary>
    public bool UseAccent;
    public FxAnim? Anim;
    internal bool Started, Ended;
}

/// <summary>A pooled effect instance: the nodes of one recipe and its playback state.</summary>
internal sealed class FxSlot
{
    public readonly List<FxPart> Parts = new();
    public string Recipe = "";
    public float Life = 1f;
    public bool Live;
    public float Age, Fade = 1f, FadeV;
    public Vector3 Feet, Away = Vector3.Forward, Right = Vector3.Right;
    public Basis Frame = Basis.Identity;
    public float ChestH = 0.9f, Scale = 1f;
    public Label3D? Label;
}
