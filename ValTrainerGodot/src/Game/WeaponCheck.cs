using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game;

/// <summary>
/// DEV-ONLY gun self-check: <c>--dev --headless --fixed-fps 60 --weapon-check</c> (runs at startup, needs no drill, quits with
/// the number of failures). For every gun in <see cref="Weapons.All"/> it drives a <see cref="WeaponController"/> with
/// scripted trigger patterns at 60 fps and checks the result against the gun's <see cref="WeaponDef"/>:
/// <list type="bullet">
/// <item>data consistency (Spread[0] = first-shot error, table maximum = MaxSpread, burst average = valorant-api's ADS rate,
/// shell-by-shell time × magazine = reload, spin-up end rate = aimed rate …);</item>
/// <item>hip and aimed fire rate (semi-auto spam is frame-quantised: each interval may round up by one frame), the Odin's
/// spin-up curve (start rate, full rate, shots in 2 s of held fire, wind-down, aimed fire at the full rate);</item>
/// <item>aimed bursts (size, in-burst rate, burst cycle, a tap fires a whole burst, spamming can't beat the cycle);</item>
/// <item>pellets per shot, ammo per shot, pellet cone and range cap; the Classic's blast (pellets, 3 rounds, 2.22/s, cone
/// steps when spammed, reset after a pause, crouched) and the Bucky's air burst (canister to 7.5 m, 5 pellets from there);</item>
/// <item>magazine size, empty / partial / shell-by-shell reload times, reserve accounting, a trigger pull interrupting a
/// shell-by-shell reload, equip time;</item>
/// <item>scope rules (unscope after the shot, the Outlaw's second-shot spread and recovery delay), first-shot error hip /
/// crouched / aimed, the accuracy deadzone, crouched and running recoil multipliers;</item>
/// <item>frame-rate independence (60 vs 240 fps), and a frame-by-frame regression of every gun that uses none of the 1.18
/// mechanics (Vandal, Phantom, …) against the pre-1.18 controller: same bullets, same recoil.</item>
/// </list>
/// </summary>
public static class WeaponCheck
{
    public static bool Requested => CmdLine.Dev && CmdLine.Has("--weapon-check");

    const float Dt = 1f / 60f;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int fails, passes;
    static string gun = "";

    public static int Run()
    {
        fails = passes = 0;
        foreach (var w in Weapons.All)
        {
            gun = w.Name;
            try { Gun(w); }
            catch (Exception e) { Fail("exception", e.ToString()); }
        }
        gun = "all";
        try { FrameIndependence(); }
        catch (Exception e) { Fail("exception", e.ToString()); }
        GD.Print($"[weapon-check] RESULT: {(fails == 0 ? "PASS" : "FAIL")} ({passes} passed, {fails} failed, {Weapons.All.Count} guns)");
        return fails;
    }

    static void Check(string name, bool ok, string detail)
    {
        if (ok) passes++; else fails++;
        GD.Print($"[weapon-check] {(ok ? "PASS" : "FAIL")} {gun}: {name}: {detail}");
    }
    static void Fail(string name, string detail) => Check(name, false, detail);
    static string F(float v, string fmt = "0.000") => v.ToString(fmt, Inv);
    static string Ms(float s) => (s * 1000f).ToString("0.0", Inv);
    static bool Near(float a, float b, float tol = 1e-4f) => Mathf.Abs(a - b) <= tol;
    static float Deg(Vector3 a, Vector3 b) => Mathf.RadToDeg(a.AngleTo(b));

    // ------------------------------------------------------------------ test rig

    /// <summary>A gun in hand with scripted triggers; records every volley with its time.</summary>
    sealed class Rig
    {
        public readonly WeaponDef D;
        public readonly WeaponController G;
        public readonly PlayerView V = new();
        public readonly Mover M = new();
        readonly Random rng;
        public readonly float StepDt;
        public float T;
        public bool Bottomless, InfiniteReserve = true, Movement;
        /// <summary>The aim button is held (the gun aims whenever it can, like AdsSession.UpdateAds; a bolt gun re-scopes at once).</summary>
        public bool Aim;
        public readonly List<(float T, List<Shot> P)> Volleys = new();
        /// <summary>The last step's shot dropped the scope.</summary>
        public bool UnscopedByShot;

        public Rig(WeaponDef d, float dt = Dt, int seed = 1)
        {
            D = d; StepDt = dt;
            G = new WeaponController(d);
            rng = new Random(seed);
            M.Hold(d);
        }

        /// <summary>One frame. Returns the last volley fired in it (null = none).</summary>
        public List<Shot>? Step(bool held = false, bool pressed = false, bool alt = false)
        {
            T += StepDt;
            G.Scoped = Aim && !G.Reloading && D.Zoom > 1f && (!D.Scope || G.EquipLeft <= 0);
            bool was = G.Scoped;
            var s = G.Tick(T, StepDt, V, M, rng, held, pressed, Bottomless, InfiniteReserve, Movement, alt);
            List<Shot>? last = null;
            foreach (var sh in s)
            {
                if (last == null || last[0].Volley != sh.Volley) { last = new List<Shot>(); Volleys.Add((T, last)); }
                last.Add(sh);
            }
            UnscopedByShot = was && !G.Scoped && s.Count > 0;
            return last;
        }

        public int Frames(float secs) => Math.Max(1, (int)Math.Round(secs / StepDt));
        /// <summary>Holds the trigger (full auto) or clicks it every frame (semi-auto spam) for <paramref name="secs"/>.</summary>
        public void Fire(float secs) { int n = Frames(secs); for (int i = 0; i < n; i++) Step(held: true, pressed: !D.Auto || i == 0); }
        public void Idle(float secs) { int n = Frames(secs); for (int i = 0; i < n; i++) Step(); }
        public float[] Intervals(int from = 0)
        {
            var r = new List<float>();
            for (int i = Math.Max(1, from + 1); i < Volleys.Count; i++) r.Add(Volleys[i].T - Volleys[i - 1].T);
            return r.ToArray();
        }
    }

    // ------------------------------------------------------------------ per gun

    static void Gun(WeaponDef w)
    {
        Data(w);
        FirstShot(w);
        if (w.SpinUpTime > 0f) Spin(w); else Rate(w, aim: false);
        if (w.Zoom > 1f && w.AltFire != AltFireKind.ZoomBurst) Rate(w, aim: true);
        if (w.AltFire == AltFireKind.ZoomBurst) Bursts(w);
        if (w.Pellets > 1) Pellets(w);
        else
        {
            var r = new Rig(w) { Bottomless = true };
            r.Fire(1f);
            Check("one bullet per shot", r.Volleys.Count > 0 && r.Volleys.All(v => v.P.Count == 1) && r.Volleys.Select(v => v.P[0].Volley).Distinct().Count() == r.Volleys.Count,
                $"{r.Volleys.Count} shots, {r.Volleys.Sum(v => v.P.Count)} bullets");
        }
        if (w.AltFire == AltFireKind.Shotgun) Blast(w);
        if (w.AltFire == AltFireKind.AirBurst) AirBurst(w);
        MagReload(w);
        Equip(w);
        if (w.Scope) Scope(w);
        Deadzone(w);
        RecoilMuls(w);
        if (Plain(w)) Regression(w);
    }

    /// <summary>The numbers agree with each other and with the research's cross-checks.</summary>
    static void Data(WeaponDef w)
    {
        var p = new List<string>();
        if (w.Rps <= 0 || w.Mag <= 0 || w.Reload <= 0 || w.RecoveryTime <= 0 || w.YawSwitchTime <= 0) p.Add("rate / magazine / reload / recovery / yaw switch must be > 0");
        if (w.Pitch.Length == 0 || w.Yaw.Length == 0 || w.Spread.Length == 0) p.Add("an empty recoil / spread table");
        else
        {
            if (w.Pitch[0] != 0f || w.Yaw[0] != 0f) p.Add("the first pitch / yaw must be 0 (cumulative tables)");
            if (!w.Scope && !Near(w.Spread[0], w.FirstShotHip)) p.Add($"Spread[0] {F(w.Spread[0])} ≠ FirstShotHip {F(w.FirstShotHip)}");
            if (!Near(w.Spread.Max(), w.MaxSpread, 0.011f)) p.Add($"table maximum {F(w.Spread.Max())} ≠ MaxSpread {F(w.MaxSpread)}");
        }
        if (w.HeatCap < 1) p.Add("HeatCap < 1");
        if (w.FirstShotAds > w.FirstShotHip + 1e-4f) p.Add("aimed first shot worse than hip");
        if (w.Pellets < 1) p.Add("Pellets < 1");
        if (w.AltFire == AltFireKind.ZoomBurst)
        {
            if (w.BurstCount < 2 || w.BurstRps <= 0 || w.BurstCycle < (w.BurstCount - 1) / w.BurstRps || w.Zoom <= 1f) p.Add("burst numbers don't fit together");
            else if (!Near(w.BurstCount / w.BurstCycle, w.Rps * w.AdsRpsMul, 0.01f * w.Rps * w.AdsRpsMul))
                p.Add($"burst average {F(w.BurstCount / w.BurstCycle, "0.00")} rps ≠ Rps × AdsRpsMul {F(w.Rps * w.AdsRpsMul, "0.00")}");
        }
        if (w.AltFire == AltFireKind.Shotgun && (w.AltPellets < 1 || w.AltRps <= 0 || w.AltSpread.Length == 0 || w.Zoom > 1f)) p.Add("blast alt fire incomplete");
        if (w.AltFire == AltFireKind.AirBurst && (w.AltPellets < 1 || w.AltBurstDistance <= 0 || w.AltSpread.Length == 0 || w.Zoom > 1f)) p.Add("air burst incomplete");
        if (w.Scope != (w.AltFire == AltFireKind.Scope)) p.Add("Scope and AltFire disagree");
        if (w.UnscopeOnShot && !w.Scope) p.Add("UnscopeOnShot without a scope");
        if (w.Zoom2 > 0f && (w.Zoom2 <= w.Zoom || !w.Scope)) p.Add("Zoom2 must be a scope's higher level");
        if (w.ReloadPerRound > 0f && !Near(w.ReloadPerRound * w.Mag, w.Reload, 0.01f)) p.Add("ReloadPerRound × Mag ≠ Reload");
        if (w.ReloadPartial > 0f && w.ReloadPartial >= w.Reload) p.Add("ReloadPartial ≥ Reload");
        if (w.SpinUpTime > 0f && (w.SpinUpStartMul <= 0f || w.SpinUpStartMul >= 1f)) p.Add("SpinUpStartMul must be in (0, 1)");
        if (w.SpinUpTime > 0f && w.AdsSkipsSpinUp && !Near(w.MaxRps, w.Rps * w.AdsRpsMul, 0.02f)) p.Add($"MaxRps {F(w.MaxRps, "0.00")} ≠ Rps × AdsRpsMul");
        if (w.CrouchSpreadMul is <= 0f or > 1f || w.CrouchRecoilMul is <= 0f or > 1f || w.DeadzoneMul is <= 0f or >= 1f) p.Add("a multiplier out of range");
        if (Weapons.Get(w.Kind) != w || Weapons.Parse(w.Kind.ToString()) != w.Kind) p.Add("lookup / parse");
        string alt = w.AltFire switch
        {
            AltFireKind.ZoomBurst => $"burst {w.BurstCount}×{F(w.BurstRps, "0.##")} every {F(w.BurstCycle, "0.###")} s",
            AltFireKind.Shotgun => $"blast {w.AltPellets} pellets {F(w.AltRps, "0.##")}/s",
            AltFireKind.AirBurst => $"air burst {w.AltPellets} pellets at {F(w.AltBurstDistance, "0.#")} m",
            _ => w.AltFire.ToString(),
        };
        Check("data", p.Count == 0, p.Count > 0 ? string.Join("; ", p)
            : $"{w.Class}, {(w.Auto ? "auto" : "semi")} {F(w.Rps, "0.###")} rps, mag {w.Mag}+{w.Reserve}, {(w.Pellets > 1 ? $"{w.Pellets} pellets, " : "")}alt {alt}");
    }

    static void FirstShot(WeaponDef w)
    {
        float Fire1(bool aim, bool crouch, out float cone)
        {
            var r = new Rig(w) { Aim = aim };
            r.M.Crouching = crouch;
            r.Step(held: true, pressed: true);
            cone = r.G.LastConeDeg;
            return r.G.Volleys == 1 ? r.G.LastShotError : float.NaN;
        }
        float hip = Fire1(false, false, out float cone), hipC = Fire1(false, true, out float coneC);
        if (w.Pellets > 1)
            Check("first shot: pellet cone hip / crouched", hip == 0f && Near(cone, w.Spread[0]) && Near(coneC, w.Spread[0] * w.CrouchSpreadMul),
                $"centre error {F(hip)}°, cone {F(cone)}° / {F(coneC)}° (expected {F(w.Spread[0])}° / {F(w.Spread[0] * w.CrouchSpreadMul)}°, crouch ×{F(w.CrouchSpreadMul, "0.##")})");
        else
        {
            float want = w.Scope ? w.FirstShotHip : w.Spread[0];
            Check("first shot error hip / crouched", Near(hip, want) && Near(hipC, want * w.CrouchSpreadMul),
                $"{F(hip)}° / {F(hipC)}° (expected {F(want)}° / {F(want * w.CrouchSpreadMul)}°, crouch ×{F(w.CrouchSpreadMul, "0.##")})");
        }
        if (w.Zoom > 1f)
        {
            float ads = Fire1(true, false, out _);
            bool floor = !w.Scope && w.AdsSpread.Length == 0 && w.FirstShotAds < 0.05f;
            float want = w.Scope ? (w.ScopedSpread.Length > 0 ? w.ScopedSpread[0] : 0f) : w.AdsSpread.Length > 0 ? w.AdsSpread[0] : Mathf.Max(0.05f, w.FirstShotAds);
            Check("first shot error aimed", Near(ads, want), $"{F(ads)}° (expected {F(want)}°{(floor ? $"; FirstShotAds {F(w.FirstShotAds)}° under the controller's 0.05° floor" : "")})");
        }
    }

    static void Rate(WeaponDef w, bool aim)
    {
        var r = new Rig(w) { Bottomless = true, Aim = aim };
        float expected = aim ? (w.SpinUpTime > 0f && w.AdsSkipsSpinUp ? w.MaxRps : w.Rps * w.AdsRpsMul) : w.Rps;
        r.Fire(Math.Max(3f, 12f / expected));
        var iv = r.Intervals();
        string name = aim ? "aimed fire rate" : "hip fire rate";
        if (iv.Length < 3) { Fail(name, $"only {r.Volleys.Count} shots"); return; }
        float want = 1f / expected, mean = iv.Average();
        bool ok = w.Auto
            ? Near(mean, want, want * 0.005f + 1e-4f) && iv.All(x => x >= want - Dt - 1e-4f && x <= want + Dt + 1e-4f)
            : iv.All(x => x >= want - 1e-4f && x <= want + Dt + 1e-4f);
        Check(name, ok, $"{F(1f / mean, "0.00")} rps measured over {iv.Length} intervals ({Ms(iv.Min())}–{Ms(iv.Max())} ms), {F(expected, "0.00")} expected"
            + (w.Auto ? "" : " (semi-auto spam: whole frames)") + (aim && w.UnscopeOnShot ? ", re-scoped after every shot" : ""));
    }

    /// <summary>Odin: the fire rate ramps from Rps to MaxRps over SpinUpTime of held fire, winds down when released, and
    /// aimed fire starts at MaxRps.</summary>
    static void Spin(WeaponDef w)
    {
        var r = new Rig(w) { Bottomless = true };
        r.Fire(2.5f);
        var iv = r.Intervals();
        float r0 = w.Rps, r1 = w.MaxRps, T = w.SpinUpTime;
        float first = 1f / iv[0], last = 1f / iv.Skip(iv.Length - 10).Average();
        int in2 = r.Volleys.Count(v => v.T <= 2f + 1e-4f);
        float integral = 2f <= T ? r0 * 2f + (r1 - r0) * 2f * 2f / (2f * T) : (r0 + r1) / 2f * T + r1 * (2f - T);
        var ramp = string.Join(", ", new[] { 0.15f, 0.35f, 0.55f, 0.75f, 0.95f, 1.5f, 2.2f }.Select(t =>
        {
            // average rate of the shots within ±0.15 s (single 60 fps intervals are whole frames)
            var near = r.Volleys.Where(v => Mathf.Abs(v.T - t) <= 0.15f).Select(v => v.T).ToList();
            return near.Count > 2 ? $"{F(t, "0.00")} s {F((near.Count - 1) / (near[^1] - near[0]), "0.0")}" : "";
        }).Where(s => s.Length > 0));
        GD.Print($"[weapon-check] {gun}: ramp (time held, rps): {ramp}");
        Check("spin-up: starts at the start rate", Near(first, r0, r0 * 0.03f), $"{F(first, "0.00")} rps (expected {F(r0, "0.00")})");
        Check("spin-up: full rate after SpinUpTime", Near(last, r1, r1 * 0.015f), $"{F(last, "0.00")} rps (expected {F(r1, "0.00")} after {F(T, "0.0")} s)");
        Check("spin-up: shots in 2 s of held fire", Mathf.Abs(in2 - (1f + integral)) <= 1.5f, $"{in2} (expected {F(1f + integral, "0.0")}: 1 + ∫ linear ramp; without spin-up {F(1f + r0 * 2f, "0")})");
        r.Idle(T * 1.05f);
        int n0 = r.Volleys.Count;
        r.Fire(0.5f);
        float again = 1f / (r.Volleys[n0 + 1].T - r.Volleys[n0].T);
        Check("spin-up: winds down when released", Near(again, r0, r0 * 0.03f), $"{F(again, "0.00")} rps after {F(T * 1.05f, "0.00")} s off the trigger");
        if (w.AdsSkipsSpinUp)
        {
            var a = new Rig(w) { Bottomless = true, Aim = true };
            a.Fire(0.5f);
            var ai = a.Intervals();
            float f = 1f / ai.Average();
            Check("spin-up: aimed fire starts at the full rate", Near(f, r1, r1 * 0.03f) && ai[0] < 1f / r0 - 1e-3f,
                $"{F(f, "0.00")} rps over the first 0.5 s, first interval {Ms(ai[0])} ms (expected {F(r1, "0.00")} rps; the hip start rate would be {Ms(1f / r0)} ms)");
        }
    }

    /// <summary>Stinger / Bulldog: aimed, a trigger pull fires a whole burst at BurstRps; holding repeats a burst every BurstCycle.</summary>
    static void Bursts(WeaponDef w)
    {
        float want = 1f / w.BurstRps;
        var r = new Rig(w) { Bottomless = true, Aim = true };
        r.Step(held: true, pressed: true);
        r.Idle(1f);
        var iv = r.Intervals();
        float span = r.Volleys.Count > 0 ? r.Volleys[^1].T - r.Volleys[0].T : 0f;
        Check("aimed tap fires one whole burst", r.Volleys.Count == w.BurstCount && Near(span, (w.BurstCount - 1) * want, Dt + 1e-4f) && iv.All(x => Near(x, want, Dt + 1e-4f)),
            $"{r.Volleys.Count} rounds (expected {w.BurstCount}), {Ms(span)} ms first to last (expected {Ms((w.BurstCount - 1) * want)}), intervals {string.Join("/", iv.Select(Ms))} ms");

        r = new Rig(w) { Bottomless = true, Aim = true };
        r.Fire(4f);
        var starts = new List<int>();
        for (int i = 0; i < r.Volleys.Count; i++) if (i == 0 || r.Volleys[i].T - r.Volleys[i - 1].T > 1.6f * want) starts.Add(i);
        var sizes = starts.Select((s, k) => (k + 1 < starts.Count ? starts[k + 1] : r.Volleys.Count) - s).ToList();
        var cycles = starts.Skip(1).Select((s, k) => r.Volleys[s].T - r.Volleys[starts[k]].T).ToList();
        float cyc = cycles.Count > 0 ? cycles.Average() : 0f;
        float avg = (r.Volleys.Count - sizes[^1]) / (r.Volleys[starts[^1]].T - r.Volleys[0].T);
        Check("held aimed fire repeats bursts", sizes.Take(sizes.Count - 1).All(s => s == w.BurstCount) && Near(cyc, w.BurstCycle, w.BurstCycle * 0.01f) && cycles.All(c => Near(c, w.BurstCycle, Dt + 1e-4f)),
            $"{starts.Count} bursts of {string.Join(",", sizes.Distinct())}, cycle {Ms(cyc)} ms (expected {Ms(w.BurstCycle)})");
        Check("held aimed fire: average rate = valorant-api ADS rate", Near(avg, w.Rps * w.AdsRpsMul, w.Rps * w.AdsRpsMul * 0.02f),
            $"{F(avg, "0.00")} rps (Rps × AdsRpsMul = {F(w.Rps * w.AdsRpsMul, "0.00")})");

        r = new Rig(w) { Bottomless = true, Aim = true };
        int every = r.Frames(w.BurstCycle * 0.4f);
        for (int i = 0; i < r.Frames(3f); i++) r.Step(held: i % every == 0, pressed: i % every == 0);
        starts.Clear();
        for (int i = 0; i < r.Volleys.Count; i++) if (i == 0 || r.Volleys[i].T - r.Volleys[i - 1].T > 1.6f * want) starts.Add(i);
        float minGap = starts.Count > 1 ? starts.Skip(1).Select((s, k) => r.Volleys[s].T - r.Volleys[starts[k]].T).Min() : 0f;
        Check("spamming can't beat the burst cycle", starts.Count > 1 && minGap >= w.BurstCycle - 1e-4f,
            $"clicks every {Ms(every * Dt)} ms: {starts.Count} bursts, closest {Ms(minGap)} ms apart (cycle {Ms(w.BurstCycle)})");
    }

    /// <summary>Shorty / Bucky / Judge: one round, Pellets pellets in the Spread cone around pellet 0, range cap.</summary>
    static void Pellets(WeaponDef w)
    {
        var r = new Rig(w);
        int ammo = r.G.Ammo;
        var fwd = r.V.Forward;
        var v = r.Step(held: true, pressed: true);
        if (v == null) { Fail("pellets", "no shot"); return; }
        float cone = w.Spread[0], worst = v.Max(s => Deg(s.Dir, v[0].Dir)), mean = v.Skip(1).Average(s => Deg(s.Dir, v[0].Dir));
        Check("pellets per shot, ammo, cone, range", v.Count == w.Pellets && ammo - r.G.Ammo == 1 && Deg(v[0].Dir, fwd) < 1e-3f && worst <= cone + 1e-3f && mean > cone * 0.3f
            && v.All(s => s.Volley == v[0].Volley && s.StartAt == 0f && Near(s.MaxRange, w.PelletMaxRange)),
            $"{v.Count} pellets (expected {w.Pellets}), ammo −{ammo - r.G.Ammo}, pellet 0 {F(Deg(v[0].Dir, fwd))}° off the aim, spread up to {F(worst, "0.00")}° (mean {F(mean, "0.00")}°) in a {F(cone, "0.00")}° cone, range cap {F(w.PelletMaxRange, "0")} m");
        if (w.Auto && w.Spread.Length > 1 && w.Spread[1] > w.Spread[0])
        {
            var a = new Rig(w) { Bottomless = true };
            a.Fire(1.5f / w.Rps);
            Check("held fire: the pellet cone grows", a.Volleys.Count >= 2 && Near(a.G.LastConeDeg, w.Spread[1], 0.01f),
                $"second shot's cone {F(a.G.LastConeDeg, "0.00")}° (Spread[1] {F(w.Spread[1], "0.00")}°)");
        }
    }

    /// <summary>Classic right click: AltPellets pellets in one blast, AltAmmoPerShot rounds, at most AltRps, cone stepping
    /// through AltSpread when spammed.</summary>
    static void Blast(WeaponDef w)
    {
        var r = new Rig(w);
        int ammo = r.G.Ammo;
        var v = r.Step(alt: true);
        float c0 = r.G.LastConeDeg;
        float worst = v?.Max(s => Deg(s.Dir, v[0].Dir)) ?? 0f;
        Check("alt fire: one blast", v != null && v.Count == w.AltPellets && ammo - r.G.Ammo == w.AltAmmoPerShot && !r.G.Scoped && Near(c0, w.AltSpread[0]) && worst <= c0 + 1e-3f,
            $"{v?.Count ?? 0} pellets (expected {w.AltPellets}), ammo −{ammo - r.G.Ammo} (expected {w.AltAmmoPerShot}), cone {F(c0, "0.00")}°, widest {F(worst, "0.00")}°");

        r = new Rig(w) { Bottomless = true };
        var cones = new List<float>();
        for (int i = 0; i < r.Frames(3f); i++) if (r.Step(alt: true) != null) cones.Add(r.G.LastConeDeg);
        var iv = r.Intervals();
        float want = 1f / w.AltRps;
        Check("alt fire: blast rate", iv.Length > 2 && iv.All(x => x >= want - 1e-4f && x <= want + Dt + 1e-4f), $"{F(1f / iv.Average(), "0.00")} blasts/s spammed (expected {F(w.AltRps, "0.00")}, whole frames)");
        var expect = Enumerable.Range(0, 4).Select(i => w.AltSpread[Math.Min(i, w.AltSpread.Length - 1)]).ToList();
        Check("alt fire: the cone steps up when spammed", cones.Count >= 4 && cones.Take(4).Zip(expect).All(z => Near(z.First, z.Second)),
            $"{string.Join(" → ", cones.Take(4).Select(c => F(c, "0.0")))}° (expected {string.Join(" → ", expect.Select(c => F(c, "0.0")))}°)");
        r.Idle(1.5f);
        r.Step(alt: true);
        float rested = r.G.LastConeDeg;
        r.Idle(1.5f);
        r.M.Crouching = true;
        r.Step(alt: true);
        float crouch = w.AltCrouchSpreadMul > 0f ? w.AltCrouchSpreadMul : w.CrouchSpreadMul;
        Check("alt fire: cone resets after a pause, crouched", Near(rested, w.AltSpread[0]) && Near(r.G.LastConeDeg, w.AltSpread[0] * crouch),
            $"{F(rested, "0.00")}° after 1.5 s, crouched {F(r.G.LastConeDeg, "0.00")}° (×{F(crouch, "0.##")})");
    }

    /// <summary>Bucky right click: a canister to AltBurstDistance, then AltPellets pellets from there in the AltSpread cone.</summary>
    static void AirBurst(WeaponDef w)
    {
        var r = new Rig(w);
        int ammo = r.G.Ammo;
        var v = r.Step(alt: true);
        float cone = w.AltSpread[0];
        bool ok = v != null && v.Count == 1 + w.AltPellets && v[0].StartAt == 0f && Near(v[0].MaxRange, w.AltBurstDistance) && ammo - r.G.Ammo == 1
                  && v.Skip(1).All(s => Near(s.StartAt, w.AltBurstDistance) && Near(s.MaxRange, w.PelletMaxRange) && Deg(s.Dir, v[0].Dir) <= cone + 1e-3f);
        Check("alt fire: air burst", ok, $"{v?.Count ?? 0} parts (canister + {w.AltPellets} pellets expected), canister to {F(v?[0].MaxRange ?? 0f, "0.0")} m, pellets from {F(v is { Count: > 1 } ? v[1].StartAt : 0f, "0.0")} m in {F(cone, "0.0")}°, ammo −{ammo - r.G.Ammo}");
        r = new Rig(w) { Bottomless = true };
        for (int i = 0; i < r.Frames(4f); i++) r.Step(alt: true);
        var iv = r.Intervals();
        Check("alt fire: air-burst rate = fire rate", iv.Length > 1 && iv.All(x => x >= w.Interval - 1e-4f && x <= w.Interval + Dt + 1e-4f), $"{F(1f / iv.Average(), "0.00")} per s (expected {F(w.Rps, "0.00")})");
    }

    static void MagReload(WeaponDef w)
    {
        var r = new Rig(w) { InfiniteReserve = w.Reserve <= 0 };
        bool full = r.G.Ammo == w.Mag && r.G.Reserve == w.Reserve;
        int guard = r.Frames(60f);
        while (!r.G.Reloading && guard-- > 0) r.Step(held: true, pressed: true);
        int fired = r.Volleys.Count;
        float t0 = r.T;
        while (r.G.Reloading && guard-- > 0) r.Step();
        float took = r.T - t0, want = w.ReloadPerRound > 0f ? w.Mag * w.ReloadPerRound : w.Reload;
        int reserve = r.InfiniteReserve ? w.Reserve : Math.Max(0, w.Reserve - w.Mag);
        Check("magazine and reload from empty", full && fired == w.Mag && r.G.Ammo == Math.Min(w.Mag, r.InfiniteReserve ? w.Mag : w.Reserve) && r.G.Reserve == reserve
            && took >= want - 1e-4f && took <= want + Dt + 1e-4f,
            $"{fired} shots per magazine (expected {w.Mag}), reload {F(took, "0.000")} s (expected {F(want, "0.000")}{(w.ReloadPerRound > 0f ? $" = {w.Mag} × {F(w.ReloadPerRound, "0.0")} s per round" : "")}), reserve {r.G.Reserve} (expected {reserve})");

        if (w.ReloadPartial > 0f)
        {
            var p = new Rig(w);
            p.Step(held: true, pressed: true);
            p.G.StartReload(true);
            float s = p.T;
            for (int i = 0; i < p.Frames(10f) && p.G.Reloading; i++) p.Step();
            float t = p.T - s;
            Check("partial reload (a round left)", p.G.Ammo == w.Mag && t >= w.ReloadPartial - 1e-4f && t <= w.ReloadPartial + Dt + 1e-4f,
                $"{F(t, "0.000")} s (expected {F(w.ReloadPartial, "0.0")}; empty {F(w.Reload, "0.0")})");
        }
        if (w.ReloadPerRound > 0f)
        {
            var p = new Rig(w);
            for (int i = 0; i < p.Frames(10f) && p.Volleys.Count < 2; i++) p.Step(held: true, pressed: true);
            p.Idle(w.Interval + 0.1f);
            p.G.StartReload(true);
            p.Idle(w.ReloadPerRound * 1.2f);
            int mid = p.G.Ammo;
            bool reloading = p.G.Reloading;
            p.Step(held: true, pressed: true);
            Check("shell-by-shell reload, a trigger pull interrupts it", reloading && mid == w.Mag - 1 && !p.G.Reloading && p.G.Ammo == w.Mag - 2 && p.Volleys.Count == 3,
                $"after {F(w.ReloadPerRound * 1.2f, "0.00")} s: {mid}/{w.Mag} loaded, still reloading {reloading}; the pull fired ({p.Volleys.Count - 2} shot) and stopped the reload ({p.G.Ammo}/{w.Mag})");
        }
    }

    static void Equip(WeaponDef w)
    {
        var r = new Rig(w) { Bottomless = true };
        r.G.EquipLeft = w.Equip;
        for (int i = 0; i < r.Frames(w.Equip + 1f) && r.Volleys.Count == 0; i++) r.Step(held: true, pressed: true);
        float t = r.Volleys.Count > 0 ? r.Volleys[0].T : float.NaN;
        Check("equip time", t >= w.Equip - 1e-4f && t <= w.Equip + Dt + 1e-4f, $"first shot {F(t, "0.000")} s after drawing (expected {F(w.Equip, "0.00")})");
    }

    /// <summary>Scoped guns: unscope after the shot (Operator, Marshal), or stay zoomed (Outlaw) with its second-shot spread
    /// and recovery delay.</summary>
    static void Scope(WeaponDef w)
    {
        var r = new Rig(w) { Aim = true };
        r.Step(held: true, pressed: true);
        Check("scope after the shot", r.UnscopedByShot == w.UnscopeOnShot, $"{(r.UnscopedByShot ? "unscoped" : "stays scoped")} (expected {(w.UnscopeOnShot ? "unscope" : "stay scoped")})");
        if (w.ScopedSpread.Length > 1)
        {
            for (int i = 0; i < r.Frames(3f) && r.Volleys.Count < 2; i++) r.Step(held: true, pressed: true);
            float gap = r.Volleys.Count > 1 ? r.Volleys[1].T - r.Volleys[0].T : float.NaN;
            Check("second scoped shot error", r.Volleys.Count == 2 && Near(r.G.LastShotError, w.ScopedSpread[1], 1e-3f),
                $"{F(r.G.LastShotError, "0.00")}° {Ms(gap)} ms after the first (expected {F(w.ScopedSpread[1], "0.00")}°)");
        }
        if (w.RecoveryDelay > 0f)
        {
            var d = new Rig(w) { Aim = true };
            d.Step(held: true, pressed: true);
            float t1 = d.T;
            d.Idle(w.RecoveryDelay * 0.9f);
            float held = d.G.Heat;
            d.Idle(w.RecoveryDelay * 0.1f + w.RecoveryTime * 1.1f + Dt);
            float after = d.G.Heat;
            d.Step(held: true, pressed: true);
            Check("recovery delay", Near(held, 1f) && after == 0f && Near(d.G.LastShotError, w.ScopedSpread.Length > 0 ? w.ScopedSpread[0] : 0f),
                $"heat {F(held, "0.00")} {Ms(w.RecoveryDelay * 0.9f)} ms after the shot, {F(after, "0.00")} after {Ms(w.RecoveryDelay + w.RecoveryTime * 1.1f)} ms; next shot {F(d.G.LastShotError, "0.00")}°");
        }
    }

    static void Deadzone(WeaponDef w)
    {
        var m = new Mover();
        m.Hold(w);
        Check("run speed and accuracy deadzone", Near(m.RunMax, w.RunSpeed) && Near(m.AccurateMax, w.RunSpeed * w.DeadzoneMul) && Near(m.AdsSpeedMul, w.AdsMoveMul),
            $"run {F(m.RunMax, "0.00")} m/s, accurate ≤ {F(m.AccurateMax, "0.00")} m/s ({F(w.DeadzoneMul * 100f, "0.#")}%), aimed ×{F(m.AdsSpeedMul, "0.00")}");
    }

    /// <summary>Crouched-still and running recoil multipliers, after one shot (heat 1).</summary>
    static void RecoilMuls(WeaponDef w)
    {
        if (w.Pitch.Length < 2 || w.Pitch[1] <= 0f) return; // nothing climbs (Operator, Marshal, Tour De Force)
        (float P, float Y) One(bool crouch, bool run)
        {
            var r = new Rig(w) { Movement = run, Bottomless = true };
            r.M.Crouching = crouch;
            if (run) r.M.Vel = new Vector3(w.RunSpeed, 0, 0);
            r.Step(held: true, pressed: true);
            return (r.V.RecoilPitch, r.V.RecoilYaw);
        }
        var (stand, standYaw) = One(false, false);
        var (crouch, _) = One(true, false);
        var (run, runYaw) = One(false, true);
        bool yawOk = Mathf.Abs(standYaw) < 1e-6f || Near(runYaw / standYaw, w.RunYawMul, 1e-3f);
        Check("recoil multipliers: crouched / running", Near(crouch / stand, w.CrouchRecoilMul, 1e-3f) && Near(run / stand, w.RunPitchMul, 1e-3f) && yawOk,
            $"pitch ×{F(crouch / stand, "0.00")} crouched (expected {F(w.CrouchRecoilMul, "0.00")}), ×{F(run / stand, "0.00")} running (expected {F(w.RunPitchMul, "0.00")})"
            + (Mathf.Abs(standYaw) < 1e-6f ? "" : $", yaw ×{F(runYaw / standYaw, "0.00")} running (expected {F(w.RunYawMul, "0.00")})"));
    }

    /// <summary>60 vs 240 fps: the same number of shots in a held spray (fixed fire schedule).</summary>
    static void FrameIndependence()
    {
        foreach (var w in new[] { Weapons.Vandal, Weapons.Odin, Weapons.Stinger })
        {
            int Count(float dt, bool aim)
            {
                var r = new Rig(w, dt) { Bottomless = true, Aim = aim };
                r.Fire(2f);
                return r.Volleys.Count(v => v.T <= 2f + 1e-4f);
            }
            bool aim = w.AltFire == AltFireKind.ZoomBurst;
            int a = Count(1f / 60f, aim), b = Count(1f / 240f, aim);
            Check($"{w.Name}: 60 vs 240 fps", Math.Abs(a - b) <= 1, $"{a} vs {b} shots in 2 s of held{(aim ? " aimed" : "")} fire");
        }
    }

    // ------------------------------------------------------------------ regression: pre-1.18 controller

    /// <summary>Guns that use none of the 1.18 mechanics: they must shoot exactly as before.</summary>
    static bool Plain(WeaponDef w) => w.Pellets <= 1 && (w.AltFire is AltFireKind.None or AltFireKind.Zoom or AltFireKind.Scope) && w.SpinUpTime <= 0f
        && w.ScopedSpread.Length == 0 && w.RecoveryDelay <= 0f && w.ReloadPerRound <= 0f && w.ReloadPartial <= 0f && w.AdsPitch.Length == 0 && w.AdsSpread.Length == 0;

    /// <summary>The same scripted frames (sprays, taps, crouched, aimed, moving, reloads) through the new controller and the
    /// pre-1.18 one: every bullet direction, the recoil and the ammo must match on every frame.</summary>
    static void Regression(WeaponDef w)
    {
        var a = new Rig(w, seed: 7);
        var b = new RefController(w);
        var bv = new PlayerView();
        var brng = new Random(7);
        float worst = 0f;
        int frames = 0, bullets = 0, mismatch = 0;
        string? first = null;
        (bool Held, bool Pressed, bool Aim, bool Crouch, bool Move) Script(int i)
        {
            float t = i * Dt;
            if (t < 1.2f) return (true, i == 0 || !w.Auto && i % 9 == 0, false, false, false);
            if (t < 1.6f) return (false, false, false, false, false);
            if (t < 2.2f) { bool tap = (i % 12) == 0; return (tap, tap, false, false, false); }
            if (t < 3.0f) return (true, i == Frame(2.2f) || !w.Auto && i % 9 == 0, false, true, false);
            if (t < 3.4f) return (false, false, false, false, false);
            if (t < 4.2f) return (true, i == Frame(3.4f) || !w.Auto && i % 9 == 0, true, false, false);
            if (t < 4.6f) return (false, false, false, false, false);
            if (t < 5.4f) return (true, i == Frame(4.6f) || !w.Auto && i % 9 == 0, false, false, true);
            return (true, !w.Auto && i % 9 == 0, false, false, false); // keeps firing through a reload
        }
        static int Frame(float t) => (int)Math.Round(t / Dt);
        for (int i = 0; i < Frame(9f); i++)
        {
            var s = Script(i);
            a.Aim = s.Aim;
            a.M.Crouching = s.Crouch;
            a.Movement = s.Move;
            a.M.Vel = s.Move ? new Vector3(w.RunSpeed, 0, 0) : Vector3.Zero;
            int before = a.Volleys.Count;
            a.Step(held: s.Held, pressed: s.Pressed);
            b.Scoped = s.Aim && !b.Reloading && w.Zoom > 1f && (!w.Scope || b.EquipLeft <= 0);
            var bs = b.Tick(a.T, Dt, bv, a.M, brng, s.Held, s.Pressed, false, true, s.Move);
            var news = a.Volleys.Skip(before).Select(v => v.P[0].Dir).ToList();
            frames++;
            bool same = news.Count == bs.Count && a.G.Ammo == b.Ammo && a.G.Reloading == b.Reloading
                        && Near(a.V.RecoilPitch, bv.RecoilPitch, 1e-6f) && Near(a.V.RecoilYaw, bv.RecoilYaw, 1e-6f);
            for (int k = 0; same && k < bs.Count; k++)
            {
                float d = Deg(news[k], bs[k].Dir);
                worst = Mathf.Max(worst, d);
                same &= d < 1e-4f;
                bullets++;
            }
            if (!same) { mismatch++; first ??= $"frame {i}: {news.Count} vs {bs.Count} bullets, ammo {a.G.Ammo} vs {b.Ammo}, pitch {F(a.V.RecoilPitch)} vs {F(bv.RecoilPitch)}"; }
        }
        Check("identical to the pre-1.18 controller", mismatch == 0, mismatch == 0 ? $"{frames} frames, {bullets} bullets, worst {F(worst, "0.000000")}°" : $"{mismatch} frames differ, first {first}");
    }

    /// <summary>WeaponController as it was before 1.18 (Phase 0: data-driven multipliers, nothing else), verbatim except
    /// the class name and the unused event. The reference for <see cref="Regression"/>.</summary>
    sealed class RefController
    {
        public WeaponDef Def { get; private set; }
        public int Ammo, Reserve;
        public float ReloadLeft;
        public bool Reloading => ReloadLeft > 0;
        public bool Scoped;
        public float EquipLeft;
        float heat, yawHeat, lastShot = -99f, nextShotAt;
        bool triggerWasDown;
        bool bottomless;
        int sprayShots;
        int side = -1;
        float sideBlend = -1;
        public float SpreadNow;
        readonly List<(Vector3 Dir, bool Accurate)> shots = new();

        public RefController(WeaponDef def)
        {
            Def = def;
            Ammo = def.Mag;
            Reserve = def.Reserve;
            side = def.FirstDriftSide >= 0 ? 1 : -1;
            sideBlend = side;
        }

        public void StartReload(bool infinite)
        {
            if (Reloading || Ammo >= Def.Mag || (!infinite && Reserve <= 0)) return;
            ReloadLeft = Def.Reload;
            Scoped = false;
        }

        public IReadOnlyList<(Vector3 Dir, bool Accurate)> Tick(float now, float dt, PlayerView view, Mover mover, Random rng,
            bool triggerHeld, bool triggerPressed, bool bottomlessMag, bool infiniteAmmo, bool movementMode)
        {
            shots.Clear();
            bottomless = bottomlessMag;
            if (EquipLeft > 0) EquipLeft -= dt;
            if (Reloading)
            {
                ReloadLeft -= dt;
                if (ReloadLeft <= 0)
                {
                    int need = Def.Mag - Ammo;
                    int take = infiniteAmmo ? need : Math.Min(need, Reserve);
                    Ammo += take;
                    if (!infiniteAmmo) Reserve -= take;
                }
            }
            bool firingNow = triggerHeld && Def.Auto || triggerPressed;
            if (!firingNow || Reloading)
            {
                heat = Mathf.Max(0, heat - dt * 2f * Mathf.Sqrt(Mathf.Max(heat, 1e-3f)) / Def.RecoveryTime);
                yawHeat = Mathf.Max(0, yawHeat - dt * 2f * Mathf.Sqrt(Mathf.Max(yawHeat, 1e-3f)) / Def.RecoveryTime);
                if (heat <= 0f) sprayShots = 0;
            }
            float interval = Def.Interval / (Scoped ? Def.AdsRpsMul : 1f);
            bool wantShot = Def.Auto ? triggerHeld : triggerPressed;
            if (wantShot && !Reloading && EquipLeft <= 0)
            {
                if (Ammo <= 0) StartReload(infiniteAmmo);
                else
                {
                    if (!triggerWasDown || nextShotAt < now - interval) nextShotAt = Mathf.Max(nextShotAt, now);
                    int guard = 0;
                    while (now >= nextShotAt && Ammo > 0 && guard++ < 4)
                    {
                        shots.Add(FireOne(now, view, mover, rng, movementMode));
                        nextShotAt += interval;
                        if (!Def.Auto) { nextShotAt = now + interval; break; }
                    }
                    if (Ammo == 0) StartReload(infiniteAmmo);
                }
            }
            triggerWasDown = Def.Auto && triggerHeld && !Reloading;
            sideBlend = Mathf.MoveToward(sideBlend, side, dt * 2f / Def.YawSwitchTime);
            bool still = mover.Crouching && mover.Speed < 0.5f, running = movementMode && mover.Speed > mover.WalkMax;
            float crouchMul = still ? Def.CrouchRecoilMul : 1f;
            float runMul = running ? Def.RunPitchMul : 1f, runYawMul = running ? Def.RunYawMul : 1f;
            view.RecoilPitch = WeaponDef.Lerp(Def.Pitch, heat) * crouchMul * runMul;
            view.RecoilYaw = sideBlend * WeaponDef.Lerp(Def.Yaw, yawHeat) * crouchMul * runYawMul;
            SpreadNow = CurrentError(mover, movementMode);
            return shots;
        }

        float CurrentError(Mover mover, bool movementMode)
        {
            float err;
            if (Def.Scope) err = Scoped ? 0f : Def.FirstShotHip;
            else
            {
                err = WeaponDef.Lerp(Def.Spread, heat);
                if (Scoped) err = Mathf.Max(0.05f, err - (Def.FirstShotHip - Def.FirstShotAds));
            }
            if (mover.Crouching) err *= Def.CrouchSpreadMul;
            if (movementMode) err += mover.MovementErrorDeg(Def);
            return err;
        }

        (Vector3, bool) FireOne(float now, PlayerView view, Mover mover, Random rng, bool movementMode)
        {
            float gap = now - lastShot;
            lastShot = now;
            if (!bottomless) Ammo--;
            sprayShots++;
            float err = CurrentError(mover, movementMode);
            bool moving = movementMode && !mover.Accurate;
            var dir = PlayerView.Spread(view.Forward, err, rng, uniform: moving);
            float interval = Def.Interval;
            bool spray = gap <= 1.2f * interval;
            float add = spray || heat <= 0f ? 1f : 1f / (1f + Def.TapEfficiency * (gap - interval) / Def.RecoveryTime);
            heat = Mathf.Min(heat + add, Def.HeatCap);
            yawHeat = Mathf.Min(yawHeat + (spray ? 1f : 0.5f * add), Def.Yaw.Length - 1);
            if (sprayShots > Def.ProtectedBullets && rng.NextDouble() < 0.10) side = -side;
            if (Def.UnscopeOnShot) Scoped = false;
            return (dir, !moving);
        }
    }
}
