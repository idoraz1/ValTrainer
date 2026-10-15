using System.Diagnostics;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Weapon;
using ValTrainer.Maps;
using ValTrainer.World;

namespace ValTrainer.Game.Replay;

/// <summary>Camera pose of the replay at one moment (yaw/pitch in degrees, like FrameSample; HFov in degrees).</summary>
public readonly record struct ReplayPose(Vector3 Eye, float Yaw, float Pitch, float HFov);

/// <summary>
/// The 3D Rewind world: rebuilds the run's map/range (Environments) and plays bots/targets as puppets from a
/// <see cref="ReplayRecording"/>, with the player's camera. Lives inside a SubViewport with its own World3D, so it
/// never touches the frozen session. CONTRACT: the replay-world work package implements the body; the UI package
/// only uses the members below.
/// <para>Loading: the world is built in steps across frames (inside _Process) so the UI can show progress —
/// read <see cref="LoadProgress"/> / <see cref="LoadStatus"/> until <see cref="Built"/>; <see cref="Failed"/> means
/// "fall back to the 2D view".</para>
/// <para>Isolation: everything lives under this node (environment, <see cref="ReplayPuppets"/>, <see cref="ReplayFx"/>,
/// camera, viewmodel). It never reads or writes the session, <c>Effects.Root</c> or <c>GameSession.active</c>.</para>
/// </summary>
public partial class ReplayStage : Node3D
{
    public ReplayStage(ReplayRecording rec, RunTelemetry run, int quality) { Rec = rec; Run = run; Quality = quality; }
    public ReplayStage() : this(new ReplayRecording(), new RunTelemetry(), 1) { }

    public ReplayRecording Rec { get; }
    public RunTelemetry Run { get; }
    public int Quality { get; }

    /// <summary>True once the world and puppets exist and <see cref="SetTime"/> can be called.</summary>
    public bool Built { get; protected set; }
    /// <summary>The build failed (e.g. the map changed since the run): show the 2D view instead.</summary>
    public bool Failed { get; protected set; }
    /// <summary>0..1 while building.</summary>
    public float LoadProgress { get; protected set; }
    /// <summary>Plain-English step, e.g. "Building the map…", "Placing bots…".</summary>
    public string LoadStatus { get; protected set; } = "Loading…";

    /// <summary>The replay camera (current, inside the stage's SubViewport). Null until built.</summary>
    public Camera3D? Camera { get; protected set; }
    /// <summary>Extra zoom from the Rewind's 4× button (multiplies the recorded scope zoom).</summary>
    public float LensZoom { get; set; } = 1f;
    public bool ShowViewmodel { get; set; } = true;

    /// <summary>The camera pose applied by the last <see cref="SetTime"/>. HFov is the recorded FOV without <see cref="LensZoom"/>
    /// (the camera itself does include the lens).</summary>
    public ReplayPose Pose { get; protected set; }
    /// <summary>World point of the aim target at the current time (recorded), if any.</summary>
    public Vector3? FocusPoint { get; protected set; }

    // ---------------------------------------------------------------- additive API

    /// <summary>The time applied by the last <see cref="SetTime"/> (−1 before the first).</summary>
    public float Time => hasTime ? curT : -1f;
    /// <summary>Why <see cref="Failed"/> is set (for logs; the UI shows its own sentence).</summary>
    public string? FailReason { get; private set; }
    /// <summary>The solid boxes of the rebuilt world (collision / line of sight), empty until built.</summary>
    public IReadOnlyList<Box> Solid => solid;

    /// <summary>The <see cref="ReplayHeader.MapHash"/> the recorder wrote for this world (map geometry, or the range's extra boxes).</summary>
    static uint ExpectedMapHash(MapSpot? map, List<Box> extras) => map != null ? ReplayFile.MapHashOf(map) : ReplayFile.MapHashOf(extras);

    // ---------------------------------------------------------------- state

    const float PreRoll = 0.4f, StepH = 1f / 60f, MaxPlayStep = 0.25f, AimRange = 60f;

    int buildStep;
    MapSpot? map;
    List<Box> extras = new();
    Environments.Built? env;
    List<Box> solid = new();
    ReplayPuppets? puppets;
    ReplayFx? fx;
    Node3D? camRig;
    Camera3D? cam;
    bool lastScrub;
    Viewmodel? vm;
    WeaponKind vmKind = WeaponKind.None;
    Color enemy;
    float curT, pendingT;
    bool hasTime;
    int evCursor;
    int[] shotEvent = Array.Empty<int>();
    readonly List<(float T, WeaponKind Kind)> weapons = new();
    WeaponKind startWeapon = WeaponKind.None;
    readonly Stopwatch sw = new();

    // AimPoint: one ray per recorded frame, ring-cached by frame index (a 0.5 s trail at up to ~4000 FPS fits).
    const int AimCache = 2048;
    readonly int[] aimFrame = new int[AimCache];
    readonly Vector3[] aimP = new Vector3[AimCache];

    // ---------------------------------------------------------------- staged build

    public override void _Process(double delta)
    {
        if (Built || Failed) return;
        try { BuildStep(); }
        catch (Exception e) { Fail("build: " + Log.Describe(e)); Log.Exception("[rewind3d] build", e); }
    }

    void Fail(string why)
    {
        Failed = true;
        FailReason = why;
        LoadStatus = "3D replay unavailable";
        Log.Error($"[rewind3d] {why}");
    }

    void SetStage(string status, float progress)
    {
        LoadStatus = status;
        LoadProgress = Mathf.Max(LoadProgress, progress);
    }

    void BuildStep()
    {
        switch (buildStep)
        {
            case 0: // nothing heavy yet: the UI paints its loading state first
                SetStage("Building the map…", 0.03f);
                Prepare();
                break;
            case 1:
            {
                var h = Rec.Header;
                int q = Math.Min(Quality, 2);
                sw.Restart();
                env = map != null ? Environments.BuildMap(map, q) : Environments.BuildRange(extras, q);
                solid = env.Solid;
                Log.Info($"[rewind3d] built {(map?.Key ?? "range")} at quality {q} in {sw.Elapsed.TotalMilliseconds:0} ms");
                SetStage("Building the map…", 0.4f);
                break;
            }
            case 2:
                AddChild(env!.Root); // enters this SubViewport's own World3D (sky, sun, viewport AA settings)
                SetStage(puppets!.HasBots ? "Placing bots…" : "Placing targets…", 0.5f);
                AddChild(puppets);
                break;
            case 3:
            {
                // One or more pooled puppets per frame, ~12 ms of work at most.
                sw.Restart();
                bool more = true;
                while (more && sw.Elapsed.TotalMilliseconds < 12) more = puppets!.PrewarmStep();
                int total = Math.Max(1, puppets!.PrewarmTotal);
                SetStage(puppets.HasBots ? "Placing bots…" : "Placing targets…", 0.5f + 0.35f * puppets.Prewarmed / total);
                if (more) return; // stay on this step
                break;
            }
            case 4:
                SetStage("Preparing effects…", 0.88f);
                fx = new ReplayFx();
                AddChild(fx);
                break;
            case 5:
                camRig = new Node3D { Name = "CameraRig" };
                AddChild(camRig);
                cam = new Camera3D { Name = "ReplayCamera", Fov = PlayerView.HipHFov, KeepAspect = Camera3D.KeepAspectEnum.Width, Near = 0.03f, Far = 400f, Current = true };
                camRig.AddChild(cam); // public Camera is set once Built (the UI projects overlays through it)
                SetWeapon(WeaponAt(pendingT));
                SetStage("Preparing effects…", 0.94f);
                break;
            case 6:
                // Pose everything once (this frame renders it, compiling any pipeline the session hadn't used yet).
                SetTimeCore(pendingT, scrubbing: false);
                SetStage("Preparing effects…", 0.98f);
                break;
            default:
                Camera = cam;
                Built = true;
                SetStage("Ready", 1f);
                return;
        }
        buildStep++;
    }

    /// <summary>Header checks, map lookup, event indices, puppet pools (no nodes in the tree yet).</summary>
    void Prepare()
    {
        var h = Rec.Header;
        // Color.ToHtml() string from the recorder; the player's current colour if it is missing or unreadable.
        enemy = !string.IsNullOrEmpty(h.EnemyColor) && Color.HtmlIsValid(h.EnemyColor) ? Color.FromHtml(h.EnemyColor)
            : Main.I?.EnemyColor ?? new Color(1f, 0.25f, 0.3f);
        string key = string.IsNullOrEmpty(h.MapKey) ? "range" : h.MapKey;
        if (key != "range")
        {
            map = MapSpots.All.FirstOrDefault(m => m.Key == key);
            if (map == null) { Fail($"unknown map '{key}'"); return; }
        }
        extras = ExtraBoxes(h);
        if (h.MapHash != 0)
        {
            uint want = ExpectedMapHash(map, extras);
            if (want != h.MapHash) { Fail($"{(map != null ? $"map '{key}'" : "range")} changed since the run (hash {want:x8} ≠ {h.MapHash:x8})"); return; }
        }

        // Shot index → PlayerShot event; weapon changes.
        int shots = Run.Shots.Count;
        foreach (var e in Rec.Events) if (e.Kind == RxKind.PlayerShot) shots = Math.Max(shots, e.Arg + 1);
        shotEvent = new int[shots];
        Array.Fill(shotEvent, -1);
        weapons.Clear();
        for (int i = 0; i < Rec.Events.Count; i++)
        {
            var e = Rec.Events[i];
            if (e.Kind == RxKind.PlayerShot && e.Arg >= 0 && e.Arg < shotEvent.Length && shotEvent[e.Arg] < 0) shotEvent[e.Arg] = i;
            else if (e.Kind == RxKind.Weapon) weapons.Add((e.T, (WeaponKind)e.Arg));
        }
        startWeapon = Enum.TryParse<WeaponKind>(string.IsNullOrEmpty(h.Weapon) ? Run.Weapon : h.Weapon, out var wk) ? wk : WeaponKind.None;

        puppets = new ReplayPuppets(Rec, enemy, h.Outlines, h.TargetStyle != Target.StyleClassic);
    }

    static List<Box> ExtraBoxes(ReplayHeader h)
    {
        var l = new List<Box>(h.ExtraSolids.Length);
        foreach (var a in h.ExtraSolids)
            if (a is { Length: >= 6 }) l.Add(new Box(new Vector3(a[0], a[1], a[2]), new Vector3(a[3], a[4], a[5])));
        return l;
    }

    // ---------------------------------------------------------------- time

    /// <summary>Moves everything to time <paramref name="t"/>. <paramref name="scrubbing"/>: the user is dragging the
    /// timeline (snap, skip pre-roll). Small forward steps play continuously (events fire, animations run).</summary>
    public virtual void SetTime(float t, bool scrubbing)
    {
        if (!Built) { pendingT = t; return; }
        if (Failed) return;
        try { SetTimeCore(t, scrubbing); }
        catch (Exception e) { Fail("playback: " + Log.Describe(e)); Log.Exception("[rewind3d] playback", e); }
    }

    void SetTimeCore(float t, bool scrubbing)
    {
        float end = Math.Max(Rec.Duration, Run.Duration);
        if (end > 0) t = Math.Clamp(t, 0f, end); else t = Math.Max(0f, t);
        float d = t - curT;
        // Scrubbing gives snapped stills (no pre-roll; dead bots already lying down); the first normal call after a drag
        // (even at the same time) finishes the seek with the pre-roll, so the paused frame matches playback.
        bool finishScrub = lastScrub && !scrubbing;
        lastScrub = scrubbing;
        if (!finishScrub && hasTime && d >= 0f && d <= MaxPlayStep) Play(t, d);
        else Seek(t, preRoll: !scrubbing);
        curT = t;
        hasTime = true;
        ApplyCamera(t);
        UpdateViewmodel(t, d);
    }

    /// <summary>Continuous playback: the events crossed fire (tracers, hits, deaths) and puppets animate by <paramref name="d"/>.</summary>
    void Play(float t, float d)
    {
        puppets!.Sync(t, resetAll: false);
        var ev = Rec.Events;
        while (evCursor < ev.Count && ev[evCursor].T <= t) Fire(ev[evCursor++], t, d, live: true);
        if (d > 0f) puppets.Step(d);
    }

    /// <summary>A jump: reset every puppet at <c>t − 0.4 s</c> (with deaths / shots still in effect then), then pre-roll to
    /// <paramref name="t"/> at 1/60 s steps replaying the events in between (no effects). Scrubbing skips the pre-roll.</summary>
    void Seek(float t, bool preRoll)
    {
        fx!.Clear();
        SetWeapon(WeaponAt(t));
        float t0 = preRoll ? Math.Max(0f, t - PreRoll) : t;
        puppets!.Sync(t0, resetAll: true, flashes: t0 >= t);
        ApplyHistory(t0);
        var ev = Rec.Events;
        evCursor = FirstEventAfter(t0);
        if (t > t0)
        {
            int n = Math.Max(1, (int)MathF.Ceiling((t - t0) / StepH));
            float h = (t - t0) / n;
            for (int k = 1; k <= n; k++)
            {
                float tau = k == n ? t : t0 + k * h;
                puppets.Sync(tau, resetAll: false, flashes: k == n);
                while (evCursor < ev.Count && ev[evCursor].T <= tau) Fire(ev[evCursor++], tau, h, live: false);
                puppets.Step(h);
            }
        }
        else puppets.Step(0f); // re-pose (death seek, stance) without advancing
        evCursor = FirstEventAfter(t);
    }

    /// <summary>Deaths and recent shots before <paramref name="t0"/> that still show at <paramref name="t0"/>.</summary>
    void ApplyHistory(float t0)
    {
        var ev = Rec.Events;
        int end = FirstEventAfter(t0);
        for (int i = Rec.FirstEventAtOrAfter(t0 - 3f); i < end; i++)
        {
            var e = ev[i];
            if (e.Kind is not (RxKind.BotDie or RxKind.BotShoot)) continue;
            var b = puppets!.Bot(e.Ent);
            if (b == null) continue;
            if (e.Kind == RxKind.BotDie) b.PuppetDie(e.P, t0 - e.T);
            else if (t0 - e.T < 1.2f) b.PuppetShoot(e.P, t0 - e.T);
        }
        // Deaths longer ago than the scan window (a body that's still around): find each puppet's own death.
        for (int i = 0; i < Rec.Entities.Count; i++)
        {
            var b = puppets!.BotAt(i);
            if (b == null || b.Dead) continue;
            ushort id = Rec.Entities[i].Id;
            int lo = Rec.FirstEventAtOrAfter(Rec.Entities[i].SpawnT);
            int hi = Rec.FirstEventAtOrAfter(t0 - 3f);
            for (int j = lo; j < hi; j++)
                if (ev[j].Kind == RxKind.BotDie && ev[j].Ent == id) { b.PuppetDie(ev[j].P, t0 - ev[j].T); break; }
        }
    }

    int FirstEventAfter(float t)
    {
        int i = Rec.FirstEventAtOrAfter(t);
        while (i < Rec.Events.Count && Rec.Events[i].T <= t) i++;
        return i;
    }

    void Fire(in ReplayEvent e, float now, float dt, bool live)
    {
        switch (e.Kind)
        {
            case RxKind.BotHit:
                puppets!.Bot(e.Ent)?.PuppetHit((HitZone)e.Arg);
                break;
            case RxKind.BotDie:
                // The step after this adds dt: land on (now − event time) seconds into the fall.
                puppets!.Bot(e.Ent)?.PuppetDie(e.P, now - e.T - dt);
                break;
            case RxKind.BotShoot:
            {
                var b = puppets!.Bot(e.Ent);
                if (b == null) break;
                b.PuppetShoot(e.P, now - e.T);
                if (live && b.Visible) fx!.Tracer(b.MuzzlePosition, e.P);
                break;
            }
            case RxKind.PlayerShot:
                if (live) PlayerShot(e);
                break;
            case RxKind.Weapon:
                SetWeapon((WeaponKind)e.Arg);
                break;
        }
    }

    void PlayerShot(in ReplayEvent e)
    {
        var eye = Rec.PlayerAt(e.T).Eye;
        var dir = e.P - eye;
        float len = dir.Length();
        if (len < 1e-3f) return;
        dir /= len;
        var muzzle = vm != null && vm.Visible && vm.IsInsideTree() ? vm.MuzzleWorld : eye + dir * 0.6f;
        fx!.Tracer(muzzle, e.P);
        bool wall = e.N.LengthSquared() > 0.25f;
        if (wall) fx.Impact(e.P, e.N);
        else if (e.Arg >= 0 && e.Arg < Run.Shots.Count && Run.Shots[e.Arg].Zone >= 0)
            fx.BodyHit(e.P, enemy, Run.Shots[e.Arg].Zone == (sbyte)HitZone.Head);
        vm?.OnFire();
    }

    // ---------------------------------------------------------------- camera / viewmodel

    void ApplyCamera(float t)
    {
        var pose = ReplayView.PoseAt(Run, Rec, t);
        float hf = pose.HFov;
        if (LensZoom > 1.0001f) hf = Mathf.RadToDeg(2f * MathF.Atan(MathF.Tan(Mathf.DegToRad(hf) / 2f) / LensZoom));
        Pose = pose; // HFov as recorded (without LensZoom); only the camera gets the lens
        if (cam != null)
        {
            cam.Position = pose.Eye;
            cam.RotationDegrees = new Vector3(pose.Pitch, -pose.Yaw, 0);
            cam.Fov = Mathf.Clamp(hf, 1f, 179f);
        }
        var p = Rec.PlayerAt(t);
        FocusPoint = (p.Bits & PBits.HasFocus) != 0 ? p.Focus : null;
    }

    WeaponKind WeaponAt(float t)
    {
        var k = startWeapon;
        foreach (var (wt, wk) in weapons) { if (wt > t) break; k = wk; }
        return k;
    }

    void SetWeapon(WeaponKind kind)
    {
        if (kind == vmKind && (vm != null || kind == WeaponKind.None)) return;
        vmKind = kind;
        if (vm != null) { vm.QueueFree(); vm = null; }
        if (cam == null || kind == WeaponKind.None || !Rec.Header.Viewmodel || Weapons.Get(kind) == null) return;
        vm = Viewmodel.Create(kind, Rec.Header.LeftHanded);
        cam.AddChild(vm);
    }

    void UpdateViewmodel(float t, float d)
    {
        if (vm == null) return;
        var p = Rec.PlayerAt(t);
        var def = Weapons.Get(vmKind);
        float a = Mathf.Clamp(p.AdsBlend, 0f, 1f), ease = a * a * (3f - 2f * a);
        bool scopeUp = (p.Bits & PBits.Scoped) != 0 && def?.Sniper == true && ease >= 0.6f; // AdsSession.ScopeShowAt
        vm.Visible = ShowViewmodel && (p.Bits & PBits.VmShown) != 0 && (p.Bits & PBits.AbilityInHand) == 0 && !scopeUp;
        vm.SetAds(a);
        vm.SetReload(p.Reload > 0f ? Mathf.Clamp(p.Reload, 0f, 1f) : -1f);
        vm.SetMotion(SpeedAt(t), (p.Bits & PBits.Crouch) != 0);
    }

    float SpeedAt(float t)
    {
        var fr = Run.Frames;
        if (fr.Count == 0) return 0f;
        int i = FrameAt(t);
        return i >= 0 ? fr[i].Speed : fr[0].Speed;
    }

    int FrameAt(float t)
    {
        var fr = Run.Frames;
        int lo = 0, hi = fr.Count - 1, r = -1;
        while (lo <= hi) { int m = (lo + hi) >> 1; if (fr[m].T <= t) { r = m; lo = m + 1; } else hi = m - 1; }
        return r;
    }

    // ---------------------------------------------------------------- queries

    /// <summary>Where the crosshair pointed at <paramref name="t"/>: first hit along the view ray (focus point or wall, ≤ 60 m).
    /// Cheap for a trail: the ray is cast once per recorded frame (cached by frame index) and interpolated in between, so a
    /// trail sliding with the playhead only casts the few frames that are new. No allocations.</summary>
    public virtual Vector3 AimPoint(float t)
    {
        var fr = Run.Frames;
        int i = FrameAt(t);
        if (i < 0 || i + 1 >= fr.Count || fr[i + 1].T <= fr[i].T) return AimRay(t);
        var a = AimAtFrame(i);
        var b = AimAtFrame(i + 1);
        return a.Lerp(b, Math.Clamp((t - fr[i].T) / (fr[i + 1].T - fr[i].T), 0f, 1f));
    }

    Vector3 AimAtFrame(int i)
    {
        int slot = i & (AimCache - 1);
        if (aimFrame[slot] == i + 1) return aimP[slot]; // stored as index + 1 (0 = empty)
        var r = AimRay(Run.Frames[i].T);
        if (Built) { aimFrame[slot] = i + 1; aimP[slot] = r; } // solids exist only once built
        return r;
    }

    Vector3 AimRay(float t)
    {
        var pose = ReplayView.PoseAt(Run, Rec, t);
        float yaw = Mathf.DegToRad(pose.Yaw), pitch = Mathf.DegToRad(pose.Pitch), cp = MathF.Cos(pitch);
        var dir = new Vector3(MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
        float dist = Math.Min(solid.Count > 0 ? Collision.FirstHit(pose.Eye, dir, solid) : float.PositiveInfinity, AimRange);
        var p = Rec.PlayerAt(t);
        if ((p.Bits & PBits.HasFocus) != 0)
        {
            // Aiming at (or near) the target: anchor the point at the target's depth instead of the wall behind it.
            var v = p.Focus - pose.Eye;
            float along = v.Dot(dir), len = v.Length();
            if (along > 0.1f && along < dist && len > 1e-3f)
            {
                float off = Mathf.RadToDeg(MathF.Acos(Math.Clamp(along / len, -1f, 1f)));
                int fi = FrameAt(t);
                float rad = fi >= 0 ? Run.Frames[fi].FocusRadius : 0f;
                if (off <= Math.Max(3f, 2.5f * rad)) dist = along;
            }
        }
        return pose.Eye + dir * dist;
    }

    /// <summary>World end point of shot <paramref name="shotIndex"/> (index into RunTelemetry.Shots), or null.</summary>
    public virtual Vector3? ShotEnd(int shotIndex)
    {
        if (shotIndex < 0 || shotIndex >= shotEvent.Length) return null;
        int i = shotEvent[shotIndex];
        return i >= 0 ? Rec.Events[i].P : null;
    }
}

public static partial class ReplayView
{
    /// <summary>Camera pose at <paramref name="t"/>: yaw/pitch interpolated from run.Frames (with recoil), eye and zoom from
    /// the recording, HFov from the run's HFov and zoom.</summary>
    public static ReplayPose PoseAt(RunTelemetry run, ReplayRecording rec, float t)
    {
        var fr = run.Frames;
        float yaw = 0, pitch = 0;
        if (fr.Count > 0)
        {
            int lo = 0, hi = fr.Count - 1;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (fr[m].T <= t) lo = m; else hi = m - 1; }
            var a = fr[lo];
            if (lo + 1 < fr.Count && fr[lo + 1].T > a.T && t > a.T)
            {
                var b = fr[lo + 1];
                float f = Math.Clamp((t - a.T) / (b.T - a.T), 0f, 1f);
                yaw = a.Yaw + Mathf.Wrap(b.Yaw - a.Yaw, -180f, 180f) * f;
                pitch = Mathf.Lerp(a.Pitch, b.Pitch, f);
            }
            else { yaw = a.Yaw; pitch = a.Pitch; }
        }
        var p = rec.PlayerAt(t);
        float zoom = Math.Max(1f, p.VisualZoom);
        float hfov = Mathf.RadToDeg(2f * MathF.Atan(MathF.Tan(Mathf.DegToRad(run.HFov) / 2f) / zoom));
        return new ReplayPose(p.Eye, yaw, pitch, hfov);
    }
}
