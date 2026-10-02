using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>
/// Sensitivity finder: a blind, performance-scored PSA method that bisects in log space (docs/coach_spec.md §E).
/// Each step plays 4 blocks (ABBA / BAAB) of L = C/hw and H = C·hw; a block is a standard set of head flicks plus
/// 10 s of seeded Veteran strafe tracking. Trials are scored (flicks: ln(ID/MT'), tracking: logit of on-target),
/// standardized within the step, blended 0.65/0.35 and compared with Welch's SE; the centre moves half a step toward
/// a clear winner. Ends with a blind confirmation of the final C against the player's current sens.
/// No gun: clicks are read from the trigger's rising edge, so there is no spread, recoil or fire-rate limit.
/// Dev flags (with --dev): --sfquick (short protocol), --sfsens X / --sfcm X (pretend current sens),
/// --sfseed N (reproducible), --sfexit (quit when the result is computed), --sftimescale N (fast-forward screenshots),
/// --sfshot DIR (save PNGs of the game's own viewport at key moments, then of the results screen; quits after).
/// </summary>
public sealed partial class SensFinderMode : TrainingMode
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public override string Key => "sensfinder";
    public override string Name => "Sens Finder";
    public override string Description => "Blind A/B test of sensitivities with flicks + tracking. Finds the sens you actually perform best with.";
    public override string Category => "Coach";
    public override WeaponKind Weapon => WeaponKind.None;
    public override bool Timed => false;
    public override bool ShowScore => false;
    public override bool Done => done;
    public override string? Subtitle => "Blind A/B test";

    enum Ph { Intro, Rest, Flick, TrackReady, Track, Summary }
    Ph phase;
    float phaseT;
    bool done, prevHeld;
    ulong lastRealUsec;

    SensFinderConfig cfg = null!;
    Random rng = null!;
    int dpi;
    float current;        // the player's sens S when the drill started
    float centre;         // C
    int ties, flips, lastDir, doneBlocks;
    bool aborted;
    string abortWhy = "", notice = "";
    float noticeUntil;
    readonly List<SfStage> stages = new();
    SfStage? stage;
    int blockIdx;
    SfBlock? block;
    bool devExit;
    string? shotDir;
    readonly HashSet<string> shotsTaken = new();
    bool origUseOverride;  // ValTrainer sens settings before the drill ("Keep my sens" restores them)
    float origOverride;

    // flicks
    Target flickT = null!, trackT = null!;
    readonly List<(SfFlick F, bool Scored)> seq = new();
    int flickIdx, focusId = 1, misses;
    bool trialLive, onsetFound;
    float spawnAt, onsetAt, amp, rAng;
    Vector3 d0, prevFwd;
    readonly List<(float T, float W)> motion = new();

    // tracking
    float winOn, winTot, tickAcc;

    // result
    float rec, confT = float.NaN;
    bool confirmed;       // a confirmation stage was run
    bool adoptC = true;
    string confidence = "", confidenceWhy = "";
    bool atLimit;         // every decisive step pointed the same way: the optimum may lie beyond this run's reach

    public override AimFocus? Focus => phase switch
    {
        Ph.Flick when trialLive => new AimFocus(focusId, flickT.GlobalPosition, flickT.Radius),
        Ph.TrackReady or Ph.Track => new AimFocus(focusId, trackT.GlobalPosition, trackT.Radius),
        _ => null,
    };

    static string[] Args => OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
    static string? ArgAfter(string flag)
    {
        var a = Args;
        int i = Array.IndexOf(a, flag);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
    static float? ArgFloat(string flag) =>
        float.TryParse(ArgAfter(flag), NumberStyles.Float, Inv, out var v) && v > 0 ? v : null;

    protected override void Setup()
    {
        bool dev = Main.I.Dev;
        cfg = dev && Args.Contains("--sfquick") ? SensFinderConfig.Quick() : SensFinderConfig.Full();
        int seed = dev && int.TryParse(ArgAfter("--sfseed"), out var s) ? s : System.Environment.TickCount;
        rng = new Random(seed);
        devExit = dev && Args.Contains("--sfexit");
        shotDir = dev ? ArgAfter("--sfshot") : null;
        if (shotDir != null) G.World.GetTree().Root.Title = "SF-dev"; // other agents' window-title tooling ignores this run
        if (dev && ArgFloat("--sftimescale") is { } ts) Engine.TimeScale = Mathf.Clamp(ts, 0.1f, 8f);
        dpi = Main.I.Settings.Dpi > 0 ? Main.I.Settings.Dpi : 800;
        origUseOverride = Main.I.Settings.UseSensOverride;
        origOverride = Main.I.Settings.SensOverride;
        current = Main.I.Sens > 0 ? Main.I.Sens : SfUnits.SensForCm(40f, dpi);
        if (dev && ArgFloat("--sfsens") is { } ss) current = ss;
        if (dev && ArgFloat("--sfcm") is { } cm) current = SfUnits.SensForCm(cm, dpi);
        centre = SfUnits.ClampCm(current, dpi, 20f, 80f);
        G.TrialSens = current;
        Event("sens_set", current, -1);

        // Both targets stay visible behind the intro panel so their material is compiled before the first block
        // (a first-show hitch inside a block would look like a pause and replay the block).
        flickT = G.SpawnTarget(SensFinderConfig.FlickRadius);
        flickT.Position = new Vector3(0.6f, PlayerView.EyeHeight, -SensFinderConfig.FlickDist);
        trackT = G.SpawnTarget(Difficulty.TrackRadius(SensFinderConfig.TrackTier));
        trackT.Position = new Vector3(-0.6f, PlayerView.EyeHeight, -SensFinderConfig.TrackDist);
        phase = Ph.Intro;
        phaseT = 0;
        lastRealUsec = Time.GetTicksUsec();
        GD.Print($"[sf] start seed={seed} quick={cfg.Steps < 5} dpi={dpi} current={current:0.000} ({SfUnits.Cm360(current, dpi):0.0} cm/360) C1={centre:0.000} ({SfUnits.Cm360(centre, dpi):0.0} cm)");
    }

    // =====================================================================================
    // Frame
    // =====================================================================================

    public override void Update(float dt)
    {
        bool held = G.TriggerHeld, pressed = G.FirePressed || (held && !prevHeld);
        prevHeld = held;
        ulong nowUsec = Time.GetTicksUsec();
        float realGap = (nowUsec - lastRealUsec) / 1e6f;
        lastRealUsec = nowUsec;
        phaseT += dt;
        if (shotDir != null) DevShots();

        // A pause (Esc / focus loss) inside a block invalidates it: replay the block after a rest.
        if (phase is Ph.Flick or Ph.TrackReady or Ph.Track && realGap > dt + 0.75f)
        {
            Notice("Round restarted after the pause");
            StartRest(blockIdx);
            return;
        }

        switch (phase)
        {
            case Ph.Intro:
                if ((phaseT > 2f && pressed) || (Main.I.Dev && phaseT > 3f)) BeginStage(0);
                break;
            case Ph.Rest:
                if (phaseT >= cfg.RestSec) StartBlock();
                break;
            case Ph.Flick:
                UpdateFlick(dt, pressed);
                break;
            case Ph.TrackReady:
                PlaceTrack(0f);
                if (phaseT >= cfg.TrackReady) { phase = Ph.Track; phaseT = 0; winOn = winTot = 0; }
                break;
            case Ph.Track:
                UpdateTrack(dt);
                break;
            case Ph.Summary:
                if (devExit && shotDir == null) { done = true; GD.Print("[sf] exit"); G.World.GetTree().Quit(); break; }
                if ((phaseT > 3f && pressed) || (Main.I.Dev && phaseT > 8f * (float)Engine.TimeScale)) { done = true; G.TrialSens = null; Engine.TimeScale = 1; }
                break;
        }
    }

    void Notice(string s) { notice = s; noticeUntil = Now + 4f; }

    /// <summary>Dev-only (--sfshot DIR): saves the viewport once per phase (and the results screen), so validation
    /// screenshots never depend on finding the window among other ValTrainer instances.</summary>
    void DevShots()
    {
        string? name = phase switch
        {
            Ph.Intro when phaseT > 1.5f => "1_intro",
            Ph.Rest when phaseT > 0.6f => "2_rest",
            Ph.Flick when trialLive && flickIdx >= 3 => "3_flick",
            Ph.Track when phaseT > 3f => "4_track",
            Ph.Summary when phaseT > 1.5f * (float)Engine.TimeScale => "5_summary",
            _ => null,
        };
        if (name == null || !shotsTaken.Add(name)) return;
        SaveShot(name);
        if (name == "5_summary")
        {
            done = true; // → results screen
            G.TrialSens = null;
            var tree = G.World.GetTree();
            tree.CreateTimer(1.5, processAlways: true, ignoreTimeScale: true).Timeout += () =>
            {
                SaveShot("6_results");
                if (devExit) tree.Quit();
            };
        }
    }

    void SaveShot(string name)
    {
        try
        {
            System.IO.Directory.CreateDirectory(shotDir!);
            var img = G.World.GetViewport().GetTexture().GetImage();
            string path = System.IO.Path.Combine(shotDir!, name + ".png");
            img.SavePng(path);
            GD.Print($"[sf] shot {path}");
        }
        catch (Exception ex) { GD.Print($"[sf] shot failed: {ex.Message}"); }
    }

    // =====================================================================================
    // Stages and blocks
    // =====================================================================================

    void BeginStage(int step)
    {
        float hw = cfg.HalfWidths[step];
        var st = new SfStage { Step = step, Centre = centre, HalfWidth = hw };
        st.Sens[0] = centre / hw;
        st.Sens[1] = centre * hw;
        SetupStage(st);
    }

    void BeginConfirm()
    {
        var st = new SfStage { Confirm = true, Step = -1, Centre = centre, HalfWidth = 1f };
        st.Sens[0] = centre;
        st.Sens[1] = current;
        SetupStage(st);
    }

    void SetupStage(SfStage st)
    {
        bool aIs0 = rng.Next(2) == 0;
        st.Label[0] = aIs0 ? 'A' : 'B';
        st.Label[1] = aIs0 ? 'B' : 'A';
        int condA = aIs0 ? 0 : 1, condB = 1 - condA;
        bool abba = rng.Next(2) == 0;
        st.Order = abba ? new[] { condA, condB, condB, condA } : new[] { condB, condA, condA, condB };
        st.Flicks = SfFlick.MakeSet(cfg, rng);
        st.Script = new SfStrafeScript(SensFinderConfig.TrackTier, cfg.TrackSec + 0.5f, rng.Next());
        stages.Add(st);
        stage = st;
        string order = string.Concat(st.Order.Select(c => st.Label[c]));
        GD.Print(st.Confirm
            ? $"[sf] confirm: new C={st.Sens[0]:0.000} ({Cm(st.Sens[0]):0.0}cm, {st.Label[0]}) vs current S={st.Sens[1]:0.000} ({Cm(st.Sens[1]):0.0}cm, {st.Label[1]}) order {order}"
            : $"[sf] step {st.Step + 1}/{cfg.Steps}: C={st.Centre:0.000} ({Cm(st.Centre):0.0}cm) +/-{(st.HalfWidth - 1) * 100:0}%  L={st.Sens[0]:0.000} ({Cm(st.Sens[0]):0.0}cm, {st.Label[0]})  H={st.Sens[1]:0.000} ({Cm(st.Sens[1]):0.0}cm, {st.Label[1]})  order {order}");
        StartRest(0);
    }

    float Cm(float sens) => SfUnits.Cm360(sens, dpi);
    float Rf(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    void StartRest(int bi)
    {
        var st = stage!;
        blockIdx = bi;
        int cond = st.Order[bi];
        block = new SfBlock { Cond = cond, Sens = st.Sens[cond], Label = st.Label[cond], Mirror = bi % 2 == 1 };
        G.TrialSens = block.Sens;
        Event("sens_set", block.Sens, cond);
        phase = Ph.Rest;
        phaseT = 0;
        trialLive = false;
        flickT.Visible = false;
        trackT.Visible = false;
        trackT.SetHot(false);
    }

    void StartBlock()
    {
        G.Respawn(Vector3.Zero, 0f); // every block starts from the same view
        seq.Clear();
        for (int i = 0; i < cfg.WarmupFlicks; i++) seq.Add((new SfFlick(Rf(8f, 18f), Rf(0f, 25f)), false));
        foreach (var f in stage!.Flicks.OrderBy(_ => rng.Next())) seq.Add((f, true));
        flickIdx = 0;
        trialLive = false;
        phase = Ph.Flick;
        phaseT = 0;
    }

    void EndBlock()
    {
        var st = stage!;
        var b = block!;
        trackT.Visible = false;
        trackT.SetHot(false);
        st.Blocks.Add(b);
        doneBlocks++;
        float medMt = b.FlickMt.Count > 0 ? Median(b.FlickMt) : 0;
        GD.Print($"[sf]   block {blockIdx + 1} {b.Label} sens={b.Sens:0.000} ({Cm(b.Sens):0.0}cm): flicks n={b.FlickZ.Count} zMean={SfStats.Mean(b.FlickZ):0.000} medMT'={medMt * 1000:0}ms acc={(b.Attempts > 0 ? 100f * b.Hits / b.Attempts : 0):0}% timeouts={b.Timeouts}  track on={(b.TrackTotal > 0 ? 100 * b.TrackOn / b.TrackTotal : 0):0}% windows={b.TrackLogit.Count}");
        if (blockIdx + 1 < st.Order.Length) StartRest(blockIdx + 1);
        else EndStage();
    }

    void EndStage()
    {
        var st = stage!;
        var cmp = SfStats.Compare(st.Blocks);
        st.Result = cmp;
        foreach (var b in st.Blocks) Event("sens_trial", b.Sens, (float)b.Score);
        float acc = st.Accuracy;
        string stats = $"d={cmp.D:+0.000;-0.000} SE={cmp.Se:0.000} t={cmp.T:+0.00;-0.00} (flick d={cmp.DFlick:+0.00;-0.00}+/-{cmp.SeFlick:0.00} n={cmp.NFlick0}/{cmp.NFlick1}, track d={cmp.DTrack:+0.00;-0.00}+/-{cmp.SeTrack:0.00} n={cmp.NTrack0}/{cmp.NTrack1}) acc={acc * 100:0}%";
        if (!st.Confirm)
        {
            int win = cmp.T > 1 ? 0 : cmp.T < -1 ? 1 : -1;
            float h = Mathf.Log(st.HalfWidth);
            if (win == 0) centre *= Mathf.Exp(-h / 2);
            else if (win == 1) centre *= Mathf.Exp(h / 2);
            centre = SfUnits.ClampCm(centre, dpi, 15f, 100f);
            st.Winner = win;
            st.NewCentre = centre;
            ties = win < 0 ? ties + 1 : 0;
            int dir = win == 0 ? -1 : win == 1 ? 1 : 0;
            if (dir != 0)
            {
                if (lastDir != 0 && dir != lastDir) flips++;
                lastDir = dir;
            }
            Event("sens_step", st.Step + 1, (float)cmp.T);
            GD.Print($"[sf] step {st.Step + 1} result: {stats} -> {(win == 0 ? "L wins" : win == 1 ? "H wins" : "tie")}, C={centre:0.000} ({Cm(centre):0.0}cm) ties={ties} flips={flips}");
        }
        else
        {
            confT = (float)cmp.T;
            Event("sens_confirm", centre, confT);
            GD.Print($"[sf] confirm result: {stats} -> {(confT >= -0.5f ? "adopt C" : "geometric mean")}");
        }

        if (acc < cfg.MinAccuracy)
        {
            aborted = true;
            abortWhy = $"Accuracy fell to {acc * 100:0}% (below {cfg.MinAccuracy * 100:0}%). You're probably tired. Take a break and run it again later.";
            GD.Print($"[sf] ABORT (fatigue): {abortWhy}");
            Finish();
            return;
        }
        if (!st.Confirm)
        {
            int next = st.Step + 1;
            if (next < cfg.Steps && ties < cfg.MaxTies) BeginStage(next);
            else if (Mathf.Abs(Mathf.Log(centre / current)) >= Mathf.Log(1.02f)) BeginConfirm();
            else Finish();
        }
        else Finish();
    }

    // =====================================================================================
    // Flicks
    // =====================================================================================

    void UpdateFlick(float dt, bool pressed)
    {
        if (!trialLive)
        {
            if (phaseT > 0.08f) flickT.Visible = false; // the hit flash shows briefly, then the gap
            if (phaseT >= cfg.Foreperiod) SpawnFlick();
            return;
        }

        // movement onset: the start of the contiguous movement (ω ≥ 8°/s) that first carries the crosshair
        // max(0.5°, 15% of A) away from where it was when the target appeared
        var fwd = G.View.Forward;
        float w = Mathf.RadToDeg(prevFwd.AngleTo(fwd)) / Mathf.Max(dt, 1e-4f);
        prevFwd = fwd;
        motion.Add((Now, w));
        if (!onsetFound && Mathf.RadToDeg(d0.AngleTo(fwd)) >= Mathf.Max(0.5f, 0.15f * amp))
        {
            onsetFound = true;
            int i = motion.Count - 1;
            while (i > 0 && motion[i - 1].W >= 8f) i--;
            onsetAt = i > 0 ? motion[i - 1].T : spawnAt;
        }

        if (pressed)
        {
            if (!float.IsPositiveInfinity(flickT.Ray(G.View.Eye, fwd))) { EndTrial(true); return; }
            misses++;
            Shots++;
            block!.Attempts++;
        }
        if (Now - spawnAt >= cfg.FlickTimeout) EndTrial(false);
    }

    void SpawnFlick()
    {
        var (f, _) = seq[flickIdx];
        var dir = PlaceFlick(f);
        flickT.Position = G.View.Eye + dir * SensFinderConfig.FlickDist;
        flickT.SpawnTime = Now;
        flickT.Visible = true;
        focusId++;
        amp = f.Amp;
        rAng = Mathf.RadToDeg(Mathf.Atan2(flickT.Radius, SensFinderConfig.FlickDist));
        spawnAt = Now;
        d0 = prevFwd = G.View.Forward;
        motion.Clear();
        onsetFound = false;
        misses = 0;
        trialLive = true;
    }

    /// <summary>
    /// Direction at exactly <paramref name="f"/>.Amp degrees from the crosshair, with elevation |φ|; the left/right and
    /// up/down signs are randomized (mirroring) among those that keep the head inside a comfortable window.
    /// </summary>
    Vector3 PlaceFlick(SfFlick f)
    {
        var fwd = G.View.Forward;
        var right = fwd.Cross(Vector3.Up).Normalized();
        var up = right.Cross(fwd).Normalized();
        float a = Mathf.DegToRad(f.Amp);
        int first = rng.Next(4);
        float[] elevScale = { 1f, 0.5f, 0f };
        foreach (float es in elevScale)
        {
            float e = Mathf.DegToRad(f.Elev * es);
            for (int j = 0; j < 4; j++)
            {
                int k = (first + j) % 4;
                float sx = (k & 1) == 0 ? 1 : -1, sy = (k & 2) == 0 ? 1 : -1;
                var dir = (fwd * Mathf.Cos(a) + (right * (sx * Mathf.Cos(e)) + up * (sy * Mathf.Sin(e))) * Mathf.Sin(a)).Normalized();
                float yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z)), pitch = Mathf.RadToDeg(Mathf.Asin(dir.Y));
                if (Mathf.Abs(yaw) <= 62f && pitch >= -4f && pitch <= 16f) return dir;
            }
        }
        // fallback: straight toward the middle of the window
        var mid = PlayerView.Dir(0, 5);
        var axis = fwd.Cross(mid);
        return axis.LengthSquared() < 1e-8f ? PlayerView.Dir(G.View.ViewYaw + f.Amp, G.View.ViewPitch) : fwd.Rotated(axis.Normalized(), a).Normalized();
    }

    void EndTrial(bool hit)
    {
        var (_, scored) = seq[flickIdx];
        var b = block!;
        if (hit)
        {
            Hits++; Shots++; Score++;
            b.Hits++; b.Attempts++;
            G.Sound("head", 0.8f);
            flickT.Hit();
        }
        else
        {
            b.Timeouts++;
            if (misses == 0) { Shots++; b.Attempts++; }
            G.Sound("fail", 0.4f);
        }
        float mt = hit ? Now - (onsetFound ? onsetAt : spawnAt) : cfg.FlickTimeout;
        mt = Mathf.Max(0.08f, mt + 0.15f * misses);
        if (scored)
        {
            b.FlickZ.Add(Math.Log(SfStats.Id(amp, rAng) / mt));
            b.FlickMt.Add(mt);
        }
        Event("sf_flick", amp, hit ? mt * 1000f : -1f);
        trialLive = false;
        if (!hit) flickT.Visible = false;
        flickIdx++;
        phaseT = 0;
        if (flickIdx >= seq.Count)
        {
            phase = Ph.TrackReady;
            focusId++;
            flickT.Visible = false;
            PlaceTrack(0f);
            trackT.Visible = true;
        }
    }

    // =====================================================================================
    // Tracking
    // =====================================================================================

    void PlaceTrack(float t)
    {
        var (x, crouch) = stage!.Script.At(t);
        if (block!.Mirror) x = -x;
        trackT.Position = new Vector3(x, PlayerView.EyeHeight - (crouch ? 0.5f : 0f), -SensFinderConfig.TrackDist);
    }

    void UpdateTrack(float dt)
    {
        var b = block!;
        PlaceTrack(phaseT);
        bool on = !float.IsPositiveInfinity(trackT.Ray(G.View.Eye, G.View.Forward));
        trackT.SetHot(on);
        if (on)
        {
            tickAcc += dt;
            if (tickAcc >= 0.1f) { tickAcc = 0; G.Sound("tick", 0.12f, 1.6f); }
        }
        if (phaseT >= cfg.TrackWarmup)
        {
            if (on) { winOn += dt; b.TrackOn += dt; }
            winTot += dt;
            b.TrackTotal += dt;
            if (winTot >= cfg.TrackWindow)
            {
                b.TrackLogit.Add(SfStats.Logit(winOn / winTot));
                winOn = winTot = 0;
            }
        }
        if (phaseT >= cfg.TrackSec)
        {
            if (winTot >= cfg.TrackWindow * 0.5f) b.TrackLogit.Add(SfStats.Logit(winOn / winTot));
            winOn = winTot = 0;
            EndBlock();
        }
    }

    // =====================================================================================
    // Result
    // =====================================================================================

    IEnumerable<SfStage> Steps => stages.Where(s => !s.Confirm);
    SfStage? ConfirmStage => stages.FirstOrDefault(s => s.Confirm);

    void Finish()
    {
        G.TrialSens = null;
        Event("sens_set", Main.I.Sens, -1);
        flickT.Visible = trackT.Visible = false;
        var conf = ConfirmStage;
        confirmed = conf?.Result != null;
        adoptC = !confirmed || confT >= -0.5f;
        float r = adoptC ? centre : Mathf.Sqrt(centre * current);
        r = SfUnits.ClampCm(r, dpi, 15f, 100f);
        rec = Mathf.Max(0.001f, Mathf.Round(r * 1000f) / 1000f);
        if (Mathf.Abs(rec / current - 1f) < 0.02f) rec = current; // "keep": show exactly the current numbers

        int decisive = Steps.Count(s => s.Winner >= 0);
        atLimit = !aborted && flips == 0 && decisive >= 3 && decisive >= Steps.Count() - 1;
        // A failed confirmation between two nearby values is expected noise (Medium); rejecting a big change is not (Low).
        bool bigReject = confirmed && !adoptC && Mathf.Abs(Mathf.Log(centre / current)) > Mathf.Log(1.15f);
        if (aborted || flips >= 3 || bigReject) confidence = "Low";
        else if (flips <= 1 && decisive >= 2 && adoptC && (!confirmed || confT >= 0f)) confidence = "High";
        else confidence = "Medium";
        var why = new List<string>();
        if (aborted) why.Add("aborted");
        why.Add($"{decisive}/{Steps.Count()} decisive");
        why.Add(flips == 1 ? "1 flip" : $"{flips} flips");
        if (confirmed) why.Add($"confirm {confT:+0.0;-0.0}σ");
        if (atLimit) why.Add("at search limit");
        confidenceWhy = string.Join(" · ", why);

        Event("sens_result", rec, confidence == "High" ? 2 : confidence == "Medium" ? 1 : 0);
        GD.Print($"[sf] RESULT rec={rec:0.000} ({Cm(rec):0.0} cm/360, eDPI {rec * dpi:0}) current={current:0.000} ({Cm(current):0.0}cm) change={ChangePct:+0.0;-0.0}% confidence={confidence} ({confidenceWhy.Replace("·", "/").Replace("σ", "sd")}) aborted={aborted}");
        phase = Ph.Summary;
        phaseT = 0;
    }

    float ChangePct => current > 0 ? (rec / current - 1f) * 100f : 0f;
    bool KeepSens => Mathf.Abs(ChangePct) < 2f;

    public override IEnumerable<string> HudLines()
    {
        if (phase is Ph.Intro or Ph.Summary || stage == null) yield break;
        var st = stage;
        yield return st.Confirm ? $"Confirmation · round {blockIdx + 1} / {st.Order.Length}"
            : $"Step {st.Step + 1} / {cfg.Steps} · round {blockIdx + 1} / {st.Order.Length}";
        yield return $"Setting {block?.Label}";
        if (phase == Ph.Flick) yield return $"Flick {Math.Min(flickIdx + 1, seq.Count)} / {seq.Count}";
        else if (phase is Ph.TrackReady or Ph.Track) yield return $"Tracking · {Mathf.Max(0, cfg.TrackSec - (phase == Ph.Track ? phaseT : 0)):0} s";
        if (Now < noticeUntil) yield return notice;
    }

    public override string? Prompt => phase switch
    {
        Ph.Flick when doneBlocks < 2 => "Flick to the head and click it",
        Ph.TrackReady => "Tracking next: keep your crosshair on the target",
        Ph.Track when doneBlocks < 2 => "Keep your crosshair on the target",
        _ => null,
    };

    /// <summary>Short values (≤ 24 chars): the results panel shares the width with the coach review.
    /// The full explanation and the chart are on the summary screen just before.</summary>
    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        if (phase != Ph.Summary) yield break;
        if (aborted) yield return ("Result", "aborted (fatigue)");
        yield return (aborted ? "Provisional" : "VALORANT sens", $"{rec.ToString("0.000", Inv)}  (was {current.ToString("0.000", Inv)})");
        yield return ("eDPI", $"{rec * dpi:0}  (was {current * dpi:0})");
        yield return ("cm/360", $"{Cm(rec):0.0}  (was {Cm(current):0.0})");
        yield return ("Change", KeepSens ? $"{ChangePct:+0.0;-0.0}% · keep yours" : $"{ChangePct:+0.0;-0.0}%");
        yield return ("Confidence", flips > 0 ? $"{confidence} · {flips} flip{(flips == 1 ? "" : "s")}" : confidence);
        yield return ("Pro median", "eDPI 240 ≈ 54 cm/360");
        yield return ("Run again", atLimit ? "in a few days" : "another day");
        yield return (atLimit ? "Then" : "If within 10%", atLimit ? "start from the new sens" : "use the average");
        yield return ("In VALORANT", "set it yourself");
    }

    public override IEnumerable<(string Label, Action Act)> ResultButtons()
    {
        if (phase != Ph.Summary) yield break;
        if (!aborted && !KeepSens)
        {
            float r = rec;
            yield return ($"Use {r.ToString("0.000", Inv)} in ValTrainer", () =>
            {
                var s = Main.I.Settings;
                s.UseSensOverride = true;
                s.SensOverride = r;
                s.Save();
            });
        }
        // Undoes "Use …" if it was clicked; otherwise nothing changes.
        yield return ("Keep my sens", () =>
        {
            var s = Main.I.Settings;
            if (s.UseSensOverride == origUseOverride && s.SensOverride == origOverride) return;
            s.UseSensOverride = origUseOverride;
            s.SensOverride = origOverride;
            s.Save();
        });
    }

    /// <summary>No rank badge: the drill measures which sens suits you, not skill (Score is just the flick hit count).</summary>
    public override int Badge() => -1;
}
