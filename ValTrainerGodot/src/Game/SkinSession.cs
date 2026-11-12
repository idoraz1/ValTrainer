using System.Diagnostics;
using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Game.Skins;
using ValTrainer.Game.Weapon;
using ValTrainer.Modes;

namespace ValTrainer.Game;

/// <summary>
/// The session side of gun skins 2 (skins2-design-1.19.md §4.3–4.8): the equipped gun's <see cref="SkinState"/>, skin sound
/// layers, the player-kill gate (<see cref="OnPlayerKill"/>), the round / match watch that drives finishers, the kill counter
/// and banner feed, and the dev tools <c>--skinfx-auto</c> / <c>--skinfx-bench</c>. Everything here is cosmetic and
/// read-only: no telemetry, replay or stats event is written, and scores never change.
/// </summary>
public partial class GameSession
{
    KillFxWorld? killFx;
    BulletFxWorld? bulletFx;
    SkinMuzzle? skinMuzzle;
    /// <summary>The worn level's state is a dev synthetic one (dev <c>--skin</c> of a skins-3 line the catalogue lacks).</summary>
    bool skinSynthetic;
    KillTracker? skinKills;
    SkinState? skinState;
    Color? skinTracer;
    /// <summary>True only while <see cref="Bullet"/> resolves one of the player's bullets (Mode.OnBullet).</summary>
    bool inPlayerBullet;
    /// <summary>The next kill sound (TrainingMode.RegisterKill → <see cref="KillSound"/>) follows an accepted skin kill.</summary>
    bool skinKillSoundArmed;
    bool doneSeen;
    float skinBannerUntil = -99f;
    int counterShown = int.MinValue;
    bool counterGg;
    Viewmodel? counterVm;
    /// <summary>Every bot this run spawned (spawn-clear, "is any enemy alive", --skinfx-auto). Freed bots are skipped.</summary>
    readonly List<BotCharacter> sessionBots = new();

    /// <summary>The equipped gun's skin as the drill shows it (null = factory, a drill preview, or a drill without skin
    /// effects such as the Sens / Crosshair Finder).</summary>
    public SkinState? EquippedSkin => skinState;
    /// <summary>This run's skin kills and finishers (null before the first restart).</summary>
    public KillTracker? SkinKills => skinKills;

    /// <summary>HUD: the skin's kill-banner style for the banner on screen (null = the factory banner). Only right after an
    /// accepted skin kill, so a kill the skin had no part in keeps the factory banner.</summary>
    public (string Style, Color Main, Color Accent)? SkinBanner =>
        skinState is { Banner: { } style } st && Now < skinBannerUntil ? (style, st.Palette.Main, st.Palette.Accent) : null;

    bool SkinFxOn => !Demo && Mode.SkinFxAllowed;

    /// <summary>The player's Bullet FX for the gun in hand (null = factory muzzle / tracer / impact): GameSession.Bullet routes
    /// its tracer and wall impact here, the gun's Fired event the muzzle overlay.</summary>
    BulletFxWorld? SkinBullets => bulletFx is { Active: true } b && IsInstanceValid(b) ? b : null;

    /// <summary>Restart: a fresh effect pool under the new world and a fresh tracker (pools are built by the Equip that follows).</summary>
    void SkinRestart()
    {
        SkinAudio.StopAll(); // a new run: no skin voice from the last one
        killFx = KillFxWorld.Create(World, null);
        bulletFx = BulletFxWorld.Create(World);
        skinMuzzle = null;
        skinKills = new KillTracker(killFx, Mode.KillDrill, () => viewmodel, () => Enemy, AnyEnemyAliveBut);
        skinKills.Killed += _ => { if (KillFxWorld.Mode != SkinKillFxMode.Off) SkinMuzzleFor()?.Pulse(); }; // gun-side pulse
        sessionBots.Clear();
        doneSeen = false;
        inPlayerBullet = skinKillSoundArmed = false;
        skinBannerUntil = -99f;
        counterShown = int.MinValue;
        counterVm = null;
        devSkinAutoAt = 0f;
        devShowAt = 0f;
        if (DevSkinBenchOn && !Demo) devBench = new DevSkinBench();
        if (DevBulletBenchOn && !Demo) devBulletBench = new DevBulletBench();
    }

    /// <summary>A gun was put in hand (Restart, buy menu, SwitchWeapon): its skin, effect pools, sounds.</summary>
    void SkinEquip(WeaponDef? def)
    {
        skinState = SkinFxOn && def != null && GunSkins.Skinnable(def.Kind) ? GunSkins.StateFor(def.Kind) : null;
        skinSynthetic = false;
        if (skinState == null && SkinFxOn && def != null && GunSkins.Skinnable(def.Kind) && SkinFxDev.FromDevSkin() is { } dev)
        {
            skinState = dev; // dev only: a skins-3 line the catalogue doesn't list yet
            skinSynthetic = true;
            if (viewmodel != null && IsInstanceValid(viewmodel)) viewmodel.SetSkinStateOverride(dev);
        }
        if (skinKills != null) skinKills.State = skinState;
        if (killFx != null && IsInstanceValid(killFx)) killFx.Setup(skinState);
        if (bulletFx != null && IsInstanceValid(bulletFx)) bulletFx.Setup(skinState, Enemy);
        skinMuzzle = null; // a fresh viewmodel: the overlay is attached on the first shot
        if (Gun != null) Gun.Fired += SkinShotFired;
        skinTracer = skinState is { } st && st.Has(SkinFeature.Tracers) ? KillFxWorld.SafePalette(st.Palette, Enemy).Main : null;
        counterShown = int.MinValue;
        if (skinState is { } s && def != null)
        {
            SkinAudio.Preload(s);
            SkinAudio.PlayEquip(s, def.Kind);
        }
    }

    /// <summary>The gun fired (after the viewmodel's own flash): the line's muzzle overlay.</summary>
    void SkinShotFired()
    {
        if (SkinBullets != null) SkinMuzzleFor()?.Fire();
    }

    /// <summary>The overlay on the current viewmodel's muzzle (re-attached after the viewmodel was re-created, e.g. by an
    /// ability); null for factory guns or without a viewmodel.</summary>
    SkinMuzzle? SkinMuzzleFor()
    {
        if (SkinBullets is not { Recipe: { } r } b || viewmodel == null || !IsInstanceValid(viewmodel)) return null;
        if (skinMuzzle != null && IsInstanceValid(skinMuzzle) && skinMuzzle.ForViewmodel == viewmodel) return skinMuzzle;
        if (skinSynthetic && skinState is { } st) viewmodel.SetSkinStateOverride(st);
        return skinMuzzle = SkinMuzzle.Attach(viewmodel, r, b.Colors3);
    }

    /// <summary>The player's own bullet killed a bot (BotMode.ShootBots, DefenseModes). Accepted only while resolving one of
    /// the player's bullets, running, with a skinnable gun in hand that wears a skin, no ability in hand, and a drill that
    /// allows skin effects; never in a drill preview.</summary>
    public void OnPlayerKill(KillInfo kill)
    {
        if (!inPlayerBullet || Demo || State != St.Running || skinKills == null || skinState == null || abilityInHand) return;
        if (weaponDef == null || !GunSkins.Skinnable(weaponDef.Kind) || !Mode.SkinFxAllowed) return;
        skinKills.OnKill(kill);
        skinKillSoundArmed = true;
        skinBannerUntil = Now + 1.5f;
    }

    /// <summary>A round / the match ended: the finisher decision (GameSession's own watch, or a mode's call).</summary>
    public void OnRoundEnd(RoundEndInfo end)
    {
        if (Demo) return;
        skinKills?.OnRoundEnd(end);
    }

    /// <summary>The multi-kill sound <paramref name="n"/> (1–5): the skin's escalating set after an accepted skin kill,
    /// else the factory <c>kill&lt;n&gt;</c>. A pellet volley plays it once.</summary>
    public void KillSound(int n, float vol = 1f)
    {
        bool skin = skinKillSoundArmed;
        skinKillSoundArmed = false;
        string key = "kill" + n;
        if (volleySounds != null && !volleySounds.Add(key)) return;
        if (skin && !Demo && skinState is { } st && SkinAudio.PlayKillSound(st, n, vol)) return;
        Play2D(key, vol);
    }

    /// <summary>Read-only watch of the drill's telemetry events (never adds one): round results and player deaths.</summary>
    void SkinWatchEvent(string kind)
    {
        if (skinKills == null) return;
        if (kind == "round_won") OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, Now));
        else if (kind == "round_lost") OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundLost, Now));
        else if (kind == "died") skinKills.ResetLife();
    }

    /// <summary>Every running frame, before the run-end check: the Mode.Done watch, the finisher window, the counter, dev tools.</summary>
    void SkinFrame()
    {
        if (skinKills == null) return;
        if (!doneSeen && Mode.Done)
        {
            doneSeen = true;
            bool won = Mode is DeathmatchMode dm ? dm.Kills >= dm.KillTarget : !Player.Dead;
            OnRoundEnd(new RoundEndInfo(won ? RoundEndKind.MatchWon : RoundEndKind.RunEnded, Now));
        }
        skinKills.Tick(Now);
        FeedKillCounter();
        if (CmdLine.Dev && !Demo)
        {
            if (DevSkinAuto) DevSkinAutoTick();
            if (DevShow != null) DevShowTick();
            devBench?.Tick(this);
            devBulletBench?.Tick(this);
        }
    }

    /// <summary>Results: a finisher that started on the run's last kill plays out behind the results screen.</summary>
    void SkinResults()
    {
        if (killFx != null && IsInstanceValid(killFx) && killFx.LiveCount > 0) killFx.ProcessMode = ProcessModeEnum.Always;
        else SkinAudio.StopAll(); // run end: skin voices stop (a finisher still playing keeps its sound)
    }

    /// <summary>Kill counter digits (lines with <see cref="SkinFeature.KillCounter"/>): the drill's kills and "GG" for
    /// <see cref="KillTracker.GgTime"/> after a finisher (the viewmodel also holds GG 2 s from its rising edge, and hides the
    /// counter itself with Settings → Kill counter off). Sent on change (and to a re-created viewmodel).</summary>
    void FeedKillCounter()
    {
        if (viewmodel == null || skinKills == null || skinState is not { } st || !st.Has(SkinFeature.KillCounter)) return;
        bool on = Main.I.Settings.SkinKillCounter;
        int n = Math.Max(0, Mode.Kills);
        bool gg = Now < skinKills.GgUntil;
        if (n == counterShown && gg == counterGg && counterVm == viewmodel) return;
        if (on && counterVm == viewmodel && counterShown >= 0 && n > counterShown) SkinAudio.PlayCounterTick(st);
        counterShown = n;
        counterGg = gg;
        counterVm = viewmodel;
        viewmodel.SetKillCount(n, gg);
    }

    /// <summary>SpawnBot: remember the bot and fast-fade effects near its spawn point (the next enemy is never hidden).</summary>
    void SkinSpawned(BotCharacter b, Vector3 feet)
    {
        if (sessionBots.Count >= 48) sessionBots.RemoveAll(static x => !IsInstanceValid(x));
        sessionBots.Add(b);
        if (killFx != null && IsInstanceValid(killFx)) killFx.ClearNear(feet);
    }

    bool AnyEnemyAliveBut(BotCharacter? killed)
    {
        foreach (var b in sessionBots)
            if (b != killed && IsInstanceValid(b) && !b.Dead && b.Visible && b.IsInsideTree()) return true;
        return false;
    }

    // ---------------- dev: --skinfx-auto [S] ----------------

    /// <summary>Dev "--skinfx-auto [S]": every S s (default 2) turn to the nearest visible live bot and fire real player
    /// bullets at its head (through <see cref="Bullet"/>, so the kill → finisher path is the real one) until it dies.
    /// Telemetry and the Rewind get nothing (record: false); dev runs never save anyway.</summary>
    static readonly bool DevSkinAuto = CmdLine.Dev && CmdLine.Has("--skinfx-auto");
    static readonly float DevSkinAutoEvery = float.TryParse(CmdLine.DevAfter("--skinfx-auto"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ev) && ev >= 0.2f ? ev : 2f;
    /// <summary>Dev "--skinfx-bench": the kill-effect / finisher GPU benchmark (any drill; best in a range drill).</summary>
    static readonly bool DevSkinBenchOn = CmdLine.Dev && CmdLine.Has("--skinfx-bench");
    float devSkinAutoAt;
    DevSkinBench? devBench;

    void DevSkinAutoTick()
    {
        if (Player.Dead || Gun == null || abilityInHand || Now < 1f || Now < devSkinAutoAt + DevSkinAutoEvery) return;
        BotCharacter? best = null;
        float bd = float.MaxValue;
        foreach (var b in sessionBots)
        {
            if (!IsInstanceValid(b) || b.Dead || !b.Visible || !b.IsInsideTree()) continue;
            var head = b.Head;
            float d = View.Eye.DistanceTo(head);
            if (d < bd && LineOfSight(View.Eye, head)) { bd = d; best = b; }
        }
        if (best == null) return;
        devSkinAutoAt = Now;
        for (int i = 0; i < 4 && IsInstanceValid(best) && !best.Dead; i++)
        {
            var dir = (best.Head - View.Eye).Normalized();
            View.Yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
            View.Pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(dir.Y, -1f, 1f)));
            View.RecoilPitch = View.RecoilYaw = 0;
            UpdateCamera();
            Bullet(View.Forward, true, record: false);
        }
    }

    // ---------------- dev: --skinfx-bench ----------------

    /// <summary>
    /// Dev "--skinfx-bench": after 3 s, four steps of 8 s each (1, 2 and 3 concurrent kill effects, then a full finisher) at
    /// fixed spots 8 m ahead and right of the gun arm, alternating 1 s without / 1 s with the effects (A/B cancels drift). Logs the GPU time of each
    /// (VmTest's method: <c>ViewportGetMeasuredRenderTimeGpu</c>) and the CPU cost of a spawn. Uses the equipped skin, or
    /// Rainfront at its top level when the gun is factory.
    /// </summary>
    sealed class DevSkinBench
    {
        const float Warm = 3f, StepLen = 8f;
        static readonly string[] Names = { "1 kill effect", "2 kill effects", "3 kill effects", "full finisher" };
        readonly double[] gpu = new double[2];
        readonly int[] frames = new int[2];
        int step = -1, lastPhase = -1;
        bool measuring, done;
        double spawnMs;
        int spawns;

        public void Tick(GameSession g)
        {
            if (done || g.killFx == null || !GodotObject.IsInstanceValid(g.killFx)) return;
            var rid = g.GetViewport().GetViewportRid();
            if (!measuring) { RenderingServer.ViewportSetMeasureRenderTime(rid, true); measuring = true; }
            float clock = g.Now - Warm;
            if (clock < 0) return;
            int s = (int)(clock / StepLen);
            if (s != step)
            {
                if (step >= 0) Report(g, step);
                if (s >= Names.Length) { done = true; g.killFx.ClearAll(); Log.Info("[skinfx] bench done"); return; }
                step = s;
                Array.Clear(gpu);
                Array.Clear(frames);
                lastPhase = -1;
            }
            float local = clock - step * StepLen;
            int phase = (int)local % 2; // 0 = without, 1 = with
            float inPhase = local % 1f;
            if (phase != lastPhase)
            {
                lastPhase = phase;
                if (phase == 0) g.killFx.ClearAll();
                else Spawn(g);
            }
            int want = step < 3 ? step + 1 : 1;
            bool valid = phase == 0 ? inPhase > 0.25f : inPhase > 0.1f && g.killFx.LiveCount == want;
            if (!valid) return;
            gpu[phase] += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
            frames[phase]++;
        }

        void Spawn(GameSession g)
        {
            var st = g.skinState is { } own && own.Has(SkinFeature.Finisher) ? own : SkinFxDev.State("foundry", 5);
            if (st is not { } state) return;
            var fwd = g.View.Forward;
            fwd.Y = 0;
            fwd = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : Vector3.Forward;
            var right = fwd.Cross(Vector3.Up).Normalized();
            var feet0 = new Vector3(g.View.Eye.X, g.feetY, g.View.Eye.Z) + fwd * 8f + right * 3f; // right of the gun arm
            var sw = Stopwatch.StartNew();
            if (step < 3)
            {
                for (int i = 0; i <= step; i++)
                {
                    var feet = feet0 + right * (i * 1.7f);
                    var k = new KillInfo(null!, feet, feet + new Vector3(0, 1.35f, 0), HitZone.Head, 8.5f, g.weaponDef?.Kind ?? WeaponKind.Vandal, g.Now);
                    g.killFx!.Kill(k, state, g.Enemy);
                }
            }
            else
            {
                var k = new KillInfo(null!, feet0, feet0 + new Vector3(0, 1.35f, 0), HitZone.Head, 8.5f, g.weaponDef?.Kind ?? WeaponKind.Vandal, g.Now);
                g.killFx!.Finisher(new FinisherCue(k, FinisherReason.Dev), state, g.Enemy);
            }
            spawnMs += sw.Elapsed.TotalMilliseconds / (step < 3 ? step + 1 : 1);
            spawns++;
        }

        void Report(GameSession g, int s)
        {
            var inv = CultureInfo.InvariantCulture;
            string skin = g.skinState is { } st ? st.LevelItem + (st.Variant != null ? "/" + st.Variant.Id : "") : "foundry/5 (default)";
            string q = (Main.I.Settings.Quality switch { 0 => "Low", 1 => "Medium", 2 => "High", _ => "Ultra" });
            if (frames[0] == 0 || frames[1] == 0) { Log.Info($"[skinfx] bench {Names[s]}: no frames (effects off in Settings?)"); return; }
            double off = gpu[0] / frames[0], on = gpu[1] / frames[1];
            Log.Info(string.Create(inv, $"[skinfx] bench {skin} {q}: {Names[s]}: gpu off {off:0.000} ms, on {on:0.000} ms, cost {on - off:+0.000;-0.000} ms ({frames[0]}/{frames[1]} frames); spawn cpu {(spawns > 0 ? spawnMs / spawns : 0):0.000} ms"));
            spawnMs = 0;
            spawns = 0;
        }
    }

    // ---------------- dev: --skinfx-show kill|finisher [S] ----------------

    /// <summary>Dev "--skinfx-show kill|finisher [S]": every S s (default 3.4, first at 2 s) plays the worn line's kill effect
    /// (or finisher) at a fixed spot 7 m ahead of the camera on the floor, without any bot or score (screenshots of the
    /// effects on any map: use with <c>--skin line@level</c>, <c>--campos</c>, <c>--map</c>).</summary>
    static readonly string? DevShow = CmdLine.DevAfter("--skinfx-show") is { } ds && ds is "kill" or "finisher" ? ds : null;
    static readonly float DevShowEvery = float.TryParse(CmdLine.DevAfter(DevShow ?? "--skinfx-show-every"), NumberStyles.Float, CultureInfo.InvariantCulture, out var se) && se >= 0.5f ? se : 3.4f;
    float devShowAt;

    void DevShowTick()
    {
        if (killFx == null || !IsInstanceValid(killFx) || skinState is not { } st || Now < 2f || Now < devShowAt) return;
        devShowAt = (devShowAt <= 0f ? 2f : devShowAt) + DevShowEvery;
        var fwd = View.Forward;
        fwd.Y = 0;
        fwd = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : Vector3.Forward;
        var feet = new Vector3(View.Eye.X, feetY, View.Eye.Z) + fwd * 7f;
        var k = new KillInfo(null!, feet, feet + new Vector3(0, 1.35f, 0), HitZone.Head, 7f, weaponDef?.Kind ?? WeaponKind.Vandal, Now);
        if (DevShow == "finisher") killFx.Finisher(new FinisherCue(k, FinisherReason.Dev), st, Enemy);
        else killFx.Kill(k, st, Enemy);
        SkinMuzzleFor()?.Pulse();
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"[skinfx] show {DevShow} {st.LevelItem} at t={Now:0.00}"));
    }

    // ---------------- dev: --bulletfx-bench ----------------

    /// <summary>Dev "--bulletfx-bench": the frame cost of a full-auto spray with each line's Bullet FX vs the factory FX.
    /// After 3 s, for each of the 10 recipes: 8 s alternating 1 s factory / 1 s skin while synthetic shots fire at 16 rounds/s
    /// at the wall ahead (the viewmodel flash + muzzle overlay, a tracer and a wall impact per shot, the real spawn calls;
    /// no telemetry, no scores). Logs GPU time (<c>ViewportGetMeasuredRenderTimeGpu</c>), CPU frame time and the spawn
    /// cost per shot. Best in a range drill facing a wall, <c>--gun Vandal</c>.</summary>
    static readonly bool DevBulletBenchOn = CmdLine.Dev && CmdLine.Has("--bulletfx-bench");
    DevBulletBench? devBulletBench;

    sealed class DevBulletBench
    {
        const float Warm = 3f, StepLen = 8f, Rps = 16f;
        readonly string[] ids = BulletFx.Ids.ToArray();
        readonly double[] gpu = new double[2], cpu = new double[2], spawn = new double[2];
        readonly int[] frames = new int[2], shots = new int[2];
        int step = -1;
        bool measuring, done;
        float nextShot;
        ulong lastTicks;
        readonly Random rng = new(7);
        readonly List<string> lines = new();

        public void Tick(GameSession g)
        {
            if (done || g.bulletFx == null || !GodotObject.IsInstanceValid(g.bulletFx)) return;
            var rid = g.GetViewport().GetViewportRid();
            if (!measuring) { RenderingServer.ViewportSetMeasureRenderTime(rid, true); measuring = true; }
            ulong now = Time.GetTicksUsec();
            double frameMs = lastTicks == 0 ? 0 : (now - lastTicks) / 1000.0;
            lastTicks = now;
            float clock = g.Now - Warm;
            if (clock < 0) return;
            int s = (int)(clock / StepLen);
            if (s != step)
            {
                if (step >= 0) Report(step);
                if (s >= ids.Length)
                {
                    done = true;
                    Log.Info("[bulletfx] bench done");
                    foreach (var l in lines) Log.Info(l);
                    return;
                }
                step = s;
                Array.Clear(gpu); Array.Clear(cpu); Array.Clear(spawn); Array.Clear(frames); Array.Clear(shots);
                g.bulletFx.Setup(SkinFxDev.State(ids[s], 5), g.Enemy);
                g.skinMuzzle = null;
            }
            float local = clock - step * StepLen;
            int phase = (int)local % 2; // 0 = factory, 1 = skin
            float inPhase = local % 1f;
            // fire at 16 rounds/s
            while (g.Now >= nextShot)
            {
                nextShot = Mathf.Max(nextShot + 1f / Rps, g.Now - 0.2f);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Shot(g, phase == 1);
                spawn[phase] += sw.Elapsed.TotalMilliseconds;
                shots[phase]++;
            }
            if (inPhase < 0.3f) return; // let the other phase's effects die out
            gpu[phase] += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
            cpu[phase] += frameMs;
            frames[phase]++;
        }

        void Shot(GameSession g, bool skin)
        {
            var dir = (g.View.Forward + new Vector3(R(-0.03f, 0.03f), R(-0.02f, 0.03f), R(-0.03f, 0.03f))).Normalized();
            var eye = g.View.Eye;
            float wall = Collision.FirstHit(eye, dir, g.solid, out int wi);
            float end = Mathf.Min(wall, 80f);
            var vm = g.viewmodel != null && GodotObject.IsInstanceValid(g.viewmodel) ? g.viewmodel : null;
            vm?.OnFire();
            var muzzle = vm?.MuzzleWorld ?? eye + dir * 0.6f;
            if (skin && vm != null && g.bulletFx!.Recipe is { } r)
            {
                if (g.skinMuzzle == null || !GodotObject.IsInstanceValid(g.skinMuzzle) || g.skinMuzzle.ForViewmodel != vm)
                    g.skinMuzzle = SkinMuzzle.Attach(vm, r, g.bulletFx.Colors3);
                else g.skinMuzzle.Configure(r, g.bulletFx.Colors3);
                g.skinMuzzle?.Fire();
            }
            if (skin) g.bulletFx!.Tracer(muzzle, eye + dir * end); else Effects.Tracer(muzzle, eye + dir * end);
            if (wall < 80f && wi >= 0)
            {
                var p = eye + dir * wall;
                var n = g.solid[wi].NormalAt(p);
                if (skin) g.bulletFx!.Impact(p, n); else Effects.Impact(p, n, new Color(0.5f, 0.5f, 0.5f));
            }
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        void Report(int s)
        {
            var inv = CultureInfo.InvariantCulture;
            string q = (Main.I.Settings.Quality switch { 0 => "Low", 1 => "Medium", 2 => "High", _ => "Ultra" });
            if (frames[0] == 0 || frames[1] == 0) { lines.Add($"[bulletfx] bench {ids[s]}: no frames"); return; }
            string line = string.Create(inv,
                $"[bulletfx] bench {ids[s]} {q} @16 rps: gpu factory {gpu[0] / frames[0]:0.000} ms, skin {gpu[1] / frames[1]:0.000} ms ({gpu[1] / frames[1] - gpu[0] / frames[0]:+0.000;-0.000}); frame factory {cpu[0] / frames[0]:0.00} ms, skin {cpu[1] / frames[1]:0.00} ms; spawn/shot factory {spawn[0] / Math.Max(1, shots[0]) * 1000:0} us, skin {spawn[1] / Math.Max(1, shots[1]) * 1000:0} us ({frames[0]}/{frames[1]} frames)");
            lines.Add(line);
            Log.Info(line);
        }
    }
}
