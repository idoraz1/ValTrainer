using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>
/// DEV-ONLY simulated player (enabled with "--dev --simaim &lt;profile&gt;") used to validate the aim coach and the
/// sens finder end-to-end without a human. It only drives the mouse and the trigger.
/// <para>Human model (Meyer et al. optimized-submovement style):</para>
/// <list type="bullet">
/// <item>reaction delay ~ N(rt, rtSd) after a target appears (shorter for predictable targets: Gridshot, Spidershot's centre);</item>
/// <item>a minimum-jerk ballistic primary submovement whose amplitude has a gain error (gain ~ N(gain, gainSd)), direction
/// noise and Fitts-like duration MT = mtA + mtB·log2(1 + A/2r);</item>
/// <item>visually guided corrective submovements after a feedback latency, until the crosshair is within tol·r, then a
/// click after a confirmation delay (dwell);</item>
/// <item>moving targets: delayed proportional-derivative pursuit v = kv·v_target(t−d) + kp·e(t−d) with tremor / jitter;</item>
/// <item>sprays: learned pull-down feed-forward (gain "pull", start "pulldelay") plus the same delayed feedback.</item>
/// </list>
/// Profiles: good, overshoot, undershoot, nomicro, premature, slow, jitter, lowxhair, hesitate, curved, spraylate,
/// runngun (movement can't be simulated: same as good). Any parameter can be overridden: "good:pref=40,rt=0.3".
/// <para>pref = the cm/360 the sim is "used to": away from it the gain error follows the muscle-memory mismatch
/// (gain × (pref/cm)^0.5 → overshoot at higher sens, undershoot below), angular noise grows with sens, and movements
/// get slower / laggier at lower sens. Without pref the sim is at home at any sens.</para>
/// </summary>
public sealed class SimAim
{
    public readonly string Profile;
    readonly Random rng;
    readonly Prm p;
    readonly string mode;
    readonly bool sprayMode, transferMode, botMode, gridMode, sfMode;
    bool trackMode, staticTargets;   // mutable: the sens finder alternates static flick heads and a strafing target
    readonly WeaponDef? weapon;

    sealed class Prm
    {
        public float Rt = 0.235f, RtSd = 0.03f, RtPred = 0.13f;
        public float Gain = 0.99f, GainSd = 0.05f, Dir = 0.025f;     // ≈40% of off-target flicks overshoot (spec neutral)
        public float MtA = 0.05f, MtB = 0.042f;
        public float Lat = 0.09f, LatSd = 0.02f;
        public float CGain = 0.98f, CGainSd = 0.10f;
        public float Confirm = 0.06f, ConfirmSd = 0.02f, Tol = 0.8f;
        public float Tremor = 3f;                         // deg/s RMS (≈8 Hz)
        public float Delay = 0.12f, Kp = 5f, Kv = 1.0f, Jitter = 14f, Predict = 1.0f; // jitter ≈ human J 0.4–0.5
        public float Pull = 0.9f, PullDelay = 0.10f;
        public float IdlePitch = 0f;                      // where the crosshair rests between bot peeks (deg, 0 = head height)
        public bool NoMicro;                              // click right at the end of the primary
        public float Premature;                           // probability of clicking during the primary
        public float Bow, EndDrop;                        // curved flicks (fraction of A) / end below the head (deg)
        public float Pref;                                // preferred cm/360 (0 = none)
        public int Seed = 1234;
    }

    /// <param name="modeKey">The drill actually being played (warm-up routines change it per step); null = the --mode argument.</param>
    public SimAim(string profile, string? modeKey = null, string? simKind = null)
    {
        Profile = profile;
        p = new Prm();
        var parts = profile.Split(':', 2);
        switch (parts[0].Trim().ToLowerInvariant())
        {
            case "overshoot": p.Gain = 1.25f; p.GainSd = 0.07f; break;
            case "undershoot": p.Gain = 0.74f; p.GainSd = 0.06f; p.CGain = 0.72f; p.Lat = 0.11f; break;
            case "nomicro": p.NoMicro = true; p.GainSd = 0.09f; p.Dir = 0.04f; break;
            case "premature": p.Premature = 0.85f; break;
            case "slow": p.Rt = 0.43f; p.RtSd = 0.06f; p.RtPred = 0.30f; p.Lat = 0.16f; p.Confirm = 0.10f; p.MtB = 0.055f; p.Delay = 0.22f; p.Predict = 0.4f; break;
            case "jitter": p.Jitter = 48f; p.Kp = 9f; p.Tremor = 8f; break;
            case "lowxhair": p.IdlePitch = -3f; break;
            case "hesitate": p.Confirm = 0.32f; p.ConfirmSd = 0.05f; break;
            case "curved": p.Bow = 0.30f; p.EndDrop = 1.1f; break;
            case "spraylate": p.Pull = 0.1f; p.PullDelay = 0.6f; p.Kp = 1.2f; break;
            case "runngun":
                GD.PushWarning("SimAim: movement can't be simulated (the sim only drives the mouse); 'runngun' plays like 'good'.");
                break;
        }
        if (parts.Length > 1)
            foreach (var kv in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var a = kv.Split('=', 2);
                if (a.Length != 2) continue;
                string k = a[0].Trim().ToLowerInvariant(), v = a[1].Trim();
                float f = float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
                switch (k)
                {
                    case "pref": p.Pref = f; break;
                    case "rt": p.Rt = f; break;
                    case "rtsd": p.RtSd = f; break;
                    case "gain": p.Gain = f; break;
                    case "gainsd": p.GainSd = f; break;
                    case "dir": p.Dir = f; break;
                    case "mtb": p.MtB = f; break;
                    case "lat": p.Lat = f; break;
                    case "cgain": p.CGain = f; break;
                    case "confirm": p.Confirm = f; break;
                    case "tol": p.Tol = f; break;
                    case "tremor": p.Tremor = f; break;
                    case "delay": p.Delay = f; break;
                    case "kp": p.Kp = f; break;
                    case "kv": p.Kv = f; break;
                    case "jitter": p.Jitter = f; break;
                    case "predict": p.Predict = f; break;
                    case "pull": p.Pull = f; break;
                    case "pulldelay": p.PullDelay = f; break;
                    case "idlepitch": p.IdlePitch = f; break;
                    case "premature": p.Premature = f; break;
                    case "nomicro": p.NoMicro = f > 0; break;
                    case "bow": p.Bow = f; break;
                    case "enddrop": p.EndDrop = f; break;
                    case "seed": p.Seed = (int)f; break;
                }
            }
        rng = new Random(p.Seed);
        var args = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
        int mi = Array.IndexOf(args, "--mode");
        mode = modeKey?.ToLowerInvariant() ?? (mi >= 0 && mi + 1 < args.Length ? args[mi + 1].ToLowerInvariant() : "");
        trackMode = mode == "tracking";
        sprayMode = mode is "spray_vandal" or "spray_phantom";
        transferMode = mode == "spray_transfer";
        gridMode = mode == "gridshot";
        botMode = mode is "strafebots" or "peek" or "counterstrafe" or "peekduel" or "siteclear" or "flashmap" or "operator" or "deathmatch";
        staticTargets = mode is "flick" or "spider" or "gridshot" or "spray_vandal" or "spray_phantom" or "";
        weapon = mode == "spray_phantom" ? Weapons.Phantom : Weapons.Vandal;
        sfMode = mode == "sensfinder";
        if (sfMode || mode == "xhairfinder") staticTargets = true;
        switch (simKind)
        {
            case "static": staticTargets = true; botMode = trackMode = false; break;
            case "bot": botMode = true; staticTargets = trackMode = false; break;
            case "track": trackMode = true; staticTargets = botMode = false; break;
            case "spray": sprayMode = true; staticTargets = true; botMode = trackMode = false; break;
        }
    }

    // ---------------- state ----------------

    sealed class Move
    {
        public float T0, Mt, Dx, Dy, BowX, BowY, SPrev, BPrev, PremTau = -1;
        public bool Primary;
    }

    readonly record struct Seen(float T, float Ex, float Ey, float TYaw, float TPitch, int Id);
    readonly List<Seen> hist = new();

    bool locked, visible, prefired, primaryDone, pursuit;
    int lockId;
    Vector3 lockPoint;
    float trialStart, reactUntil = -1, evalAt = -1, clickAt = -1, missCheckAt = -1, lastClickAt = -9;
    Move? cur;
    float tremX, tremY, jitX, jitY;
    float cntX, cntY;
    float burstUntil = -1, restUntil = -1, burstStart;
    bool firing;

    float Gauss(float mean, float sd)
    {
        double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + sd * (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    static (float Yaw, float Pitch) Angles(Vector3 from, Vector3 to)
    {
        var d = (to - from).Normalized();
        return (Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1))));
    }

    static GameSession? Session() => Main.I?.GetChildren().OfType<GameSession>().FirstOrDefault();

    // sens-preference modulation (computed per frame; the sens finder changes the trial sens)
    float rho = 1, noiseMul = 1, slowMul = 1;

    void UpdatePref(float sens)
    {
        rho = 1; noiseMul = 1; slowMul = 1;
        if (p.Pref <= 0 || sens <= 0) return;
        int dpi = Main.I?.Settings.Dpi ?? 800;
        float cm = PlayerView.Cm360(sens, dpi);
        rho = p.Pref / cm;                 // > 1: faster than preferred (higher sens)
        float L = MathF.Log(rho);
        noiseMul = MathF.Exp(Math.Max(0, L));
        slowMul = MathF.Exp(Math.Max(0, -L) * 0.6f);
    }

    float GainMul => MathF.Pow(rho, 0.5f);

    // ---------------- main ----------------

    /// <summary>
    /// One frame of simulated input: mouse counts to apply (+x = turn right, +y = look down, like a real mouse)
    /// and the trigger state.
    /// </summary>
    public (Vector2 Counts, bool FireHeld, bool FirePressed) Next(float dt, float now, PlayerView view, AimFocus? focus, float sens)
    {
        UpdatePref(sens);
        float k = PlayerView.DegPerCount * sens * (view.Zoom > 1f ? 1f / view.Zoom : 1f);
        float cy = view.ViewYaw, cp = view.ViewPitch;
        bool press = false;

        // ---- sens finder: a target that starts moving is the tracking segment ----
        if (sfMode && focus is { } sf)
        {
            bool moved = sf.Id == sfLastId && dt > 0 && sf.Point.DistanceTo(sfLastPoint) / dt > 0.3f;
            if (sf.Id != sfLastId) { trackMode = false; staticTargets = true; }
            else if (moved && !trackMode)
            {
                trackMode = true; staticTargets = false;
                if (locked) { primaryDone = true; pursuit = true; cur = null; evalAt = -1; clickAt = -1; missCheckAt = -1; }
            }
            sfLastId = sf.Id; sfLastPoint = sf.Point;
        }

        // ---- target lock ----
        if (focus is not { } f)
        {
            if (locked) Unlock();
        }
        else if (!locked || (f.Id != lockId && (!gridMode || now - lastClickAt < 0.15f)))
            Lock(f, now, view);

        float ex = 0, ey = 0, rDeg = 0.5f, tYaw = 0, tPitch = 0;
        if (locked && focus is { } fc)
        {
            var pt = staticTargets ? lockPoint : (fc.Id == lockId ? fc.Point : lockPoint);
            if (!staticTargets && fc.Id == lockId) lockPoint = fc.Point;
            (tYaw, tPitch) = Angles(view.Eye, pt);
            ex = Mathf.Wrap(tYaw - cy, -180f, 180f);
            ey = tPitch - cp;
            rDeg = Mathf.RadToDeg(Mathf.Atan2(fc.Radius, Mathf.Max(0.1f, view.Eye.DistanceTo(pt))));
            hist.Add(new Seen(now, ex, ey, tYaw, tPitch, lockId));
            while (hist.Count > 0 && now - hist[0].T > 1.2f) hist.RemoveAt(0);

            // Bot drills: react to the enemy when it becomes visible (pre-aim the hidden position before that).
            if (botMode)
            {
                bool vis = Session()?.LineOfSight(view.Eye, pt) ?? true;
                if (vis && !visible)
                {
                    visible = true;
                    visibleAt = now;
                    cur = null; primaryDone = false; pursuit = false; evalAt = -1; clickAt = -1;
                    reactUntil = now + Math.Max(0.12f, Gauss(p.Rt, p.RtSd));
                }
            }
        }

        float dx = 0, dy = 0;
        if (locked)
        {
            if (botMode && !visible)
            {
                // Pre-aim where the enemy is expected (head height + the profile's resting offset), no shooting.
                if (!prefired && now >= reactUntil)
                {
                    prefired = true;
                    float gx = ex, gy = ey + p.IdlePitch;
                    cur = new Move { T0 = now, Mt = 0.45f * slowMul, Dx = gx * Gauss(0.9f, 0.05f), Dy = gy };
                }
            }
            else if (now >= reactUntil)
            {
                if (!primaryDone && cur == null) PlanPrimary(now, ex, ey, rDeg);
                else if (primaryDone && (pursuit || trackMode || sprayMode || transferMode))
                {
                    // Continuous delayed pursuit for moving targets / sprays.
                    var (pex, pey, vx, vy) = Perceived(now - p.Delay * slowMul);
                    float kv = p.Kv * MathF.Pow(Math.Min(1f, rho), 0.25f), kp = p.Kp * MathF.Pow(Math.Min(1f, rho), 0.3f);
                    float ux = kv * vx + kp * pex, uy = kv * vy + kp * pey;
                    if (sprayMode && firing) uy -= p.Pull * Climb(now - burstStart - p.PullDelay);
                    dx += ux * dt; dy += uy * dt;
                    float jit = p.Jitter * noiseMul * (sprayMode ? 0.35f : 1f); // calmer hands while pulling a spray
                    if (jit > 0)
                    {
                        Ou(ref jitX, jit, 0.022f, dt); Ou(ref jitY, jit * 0.5f, 0.022f, dt);
                        dx += jitX * dt; dy += jitY * dt;
                    }
                    // Clicking modes: shoot when the head is under the crosshair (in Peek Practice, once the peeker plants).
                    bool settled = mode != "peek" || MathF.Sqrt(vx * vx + vy * vy) < 4f || now - visibleAt > 1.2f;
                    if (Clicking && clickAt < 0 && now - lastClickAt > 0.22f && settled
                        && MathF.Sqrt(ex * ex + ey * ey) <= p.Tol * rDeg)
                        clickAt = now + Math.Max(0.02f, Gauss(p.Confirm, p.ConfirmSd));
                    // Way off (e.g. a sharp reversal on a fast bot): a fresh corrective submovement.
                    if (cur == null && MathF.Sqrt(pex * pex + pey * pey) > Math.Max(3f, 6 * rDeg) && !trackMode)
                        PlanCorrection(now, ex, ey, rDeg);
                }
                if (evalAt >= 0 && now >= evalAt && cur == null)
                {
                    evalAt = -1;
                    float err = MathF.Sqrt(ex * ex + ey * ey) + MathF.Abs(Gauss(0, 0.12f * rDeg));
                    if (err <= p.Tol * rDeg || now - trialStart > 3.5f) clickAt = now + Math.Max(0.015f, Gauss(p.Confirm, p.ConfirmSd));
                    else PlanCorrection(now, ex, ey, rDeg);
                }
            }
        }
        else if (botMode && cur == null)
        {
            // Between enemies: drift back to the resting height.
            float gy = p.IdlePitch - cp;
            if (MathF.Abs(gy) > 0.4f) cur = new Move { T0 = now, Mt = 0.35f, Dx = 0, Dy = gy };
        }

        // ---- execute the current minimum-jerk submovement ----
        if (cur != null)
        {
            float tau = Math.Clamp((now - cur.T0) / cur.Mt, 0, 1);
            float s = MinJerk(tau), sb = 16f * tau * tau * (1 - tau) * (1 - tau); // bow: smooth bump, zero speed at both ends
            dx += cur.Dx * (s - cur.SPrev) + cur.BowX * (sb - cur.BPrev);
            dy += cur.Dy * (s - cur.SPrev) + cur.BowY * (sb - cur.BPrev);
            cur.SPrev = s; cur.BPrev = sb;
            if (cur.PremTau >= 0 && tau >= cur.PremTau) { cur.PremTau = -1; clickAt = now; }
            if (tau >= 1)
            {
                bool wasPrimary = cur.Primary;
                cur = null;
                idleSince = now;
                if (locked && (!botMode || visible))
                {
                    if (wasPrimary)
                    {
                        primaryDone = true;
                        pursuit = !staticTargets;
                        if (p.NoMicro && Clicking) clickAt = now + (float)rng.NextDouble() * 0.02f;
                        else if (!pursuit && !sprayMode) Land(now, ex, ey, rDeg);
                    }
                    else if (!pursuit && !sprayMode) Land(now, ex, ey, rDeg);
                }
            }
        }
        // Watchdog: a static trial with nothing scheduled looks again.
        if (locked && staticTargets && !sprayMode && primaryDone && cur == null && evalAt < 0 && clickAt < 0 && missCheckAt < 0 && now - idleSince > 0.5f)
        { evalAt = now; idleSince = now; }

        // ---- tremor (always) ----
        float trem = p.Tremor * noiseMul;
        Ou(ref tremX, trem, 0.02f, dt); Ou(ref tremY, trem, 0.02f, dt);
        dx += tremX * dt; dy += tremY * dt;

        // ---- trigger ----
        bool held = false;
        if (clickAt >= 0 && now >= clickAt && now - lastClickAt >= 0.11f && locked)
        {
            press = true; held = true; clickAt = -1; lastClickAt = now;
            missCheckAt = staticTargets ? now + 0.06f : -1;
        }
        if (missCheckAt >= 0 && now >= missCheckAt)
        {
            missCheckAt = -1;
            if (locked && focus is { } fm)
            {
                if (fm.Id == lockId) { if (evalAt < 0 && cur == null) evalAt = now + Math.Max(0.03f, Gauss(p.Lat, p.LatSd) * slowMul); } // missed: look again, correct, re-click
                else if (gridMode) Lock(fm, now, view); // our target is gone (hit): go for the nearest one
            }
        }
        if (trackMode && !sfMode) held = focus != null; // the sens finder scores tracking without the trigger
        if (transferMode) held = locked && primaryDone || (firing && focus != null);
        if (sprayMode)
        {
            if (!firing && locked && primaryDone && now >= restUntil && MathF.Sqrt(ex * ex + ey * ey) <= 1.5f * rDeg)
            { firing = true; burstStart = now; burstUntil = now + Gauss(1.3f, 0.05f); }
            if (firing && now >= burstUntil) { firing = false; restUntil = now + Gauss(0.7f, 0.1f); }
            held = firing;
            press = firing && now - burstStart < 1e-4f;
        }
        else if (transferMode) { firing = held; }

        // ---- degrees → integer mouse counts (+y = down) ----
        cntX += dx / k; cntY += -dy / k;
        float ox = MathF.Truncate(cntX), oy = MathF.Truncate(cntY);
        cntX -= ox; cntY -= oy;
        return (new Vector2(ox, oy), held, press);
    }

    float idleSince, visibleAt;
    int sfLastId = int.MinValue;
    Vector3 sfLastPoint;

    /// <summary>A submovement just ended: if it landed on the target, click after the confirmation delay
    /// (skilled players commit to the click as the correction lands); otherwise look again after the feedback latency.</summary>
    void Land(float now, float ex, float ey, float r)
    {
        float err = MathF.Sqrt(ex * ex + ey * ey) + MathF.Abs(Gauss(0, 0.12f * r));
        if (Clicking && err <= p.Tol * r) clickAt = now + Math.Max(0.015f, Gauss(p.Confirm, p.ConfirmSd));
        else evalAt = now + Math.Max(0.03f, Gauss(p.Lat, p.LatSd) * slowMul);
    }
    bool Clicking => !trackMode && !sprayMode && !transferMode;

    static float MinJerk(float t) => t * t * t * (10 - 15 * t + 6 * t * t);

    void Ou(ref float x, float sigma, float tau, float dt)
    {
        if (sigma <= 0) { x = 0; return; }
        x += -x / tau * dt + sigma * MathF.Sqrt(2 * dt / tau) * Gauss(0, 1);
    }

    /// <summary>Recoil climb rate (deg/s) of the weapon's vertical pattern at time t into the spray.</summary>
    float Climb(float t)
    {
        if (t <= 0 || weapon == null) return 0;
        float a = WeaponDef.Lerp(weapon.Pitch, t * weapon.Rps), b = WeaponDef.Lerp(weapon.Pitch, Math.Max(0, t - 0.03f) * weapon.Rps);
        return (a - b) / 0.03f;
    }

    void Lock(AimFocus f, float now, PlayerView view)
    {
        locked = true;
        lockId = f.Id;
        lockPoint = f.Point;
        cur = null; evalAt = -1; clickAt = -1; missCheckAt = -1;
        primaryDone = false; pursuit = false; prefired = false;
        trialStart = now;
        firing = firing && transferMode;
        hist.Clear();
        visible = !botMode || (Session()?.LineOfSight(view.Eye, f.Point) ?? true);
        visibleAt = now;
        var (ty, tp) = Angles(view.Eye, f.Point);
        bool predictable = gridMode || (mode == "spider" && MathF.Abs(ty) < 1f && MathF.Abs(tp) < 1f) || transferMode;
        reactUntil = now + Math.Max(0.1f, predictable ? Gauss(p.RtPred, p.RtSd * 0.7f) * slowMul : Gauss(p.Rt, p.RtSd));
    }

    void Unlock()
    {
        locked = false; visible = false;
        cur = null; evalAt = -1; clickAt = -1; missCheckAt = -1;
        primaryDone = false; pursuit = false;
        firing = false;
    }

    /// <summary>Error and target angular velocity as perceived at time tq (visual delay).</summary>
    (float Ex, float Ey, float Vx, float Vy) Perceived(float tq)
    {
        if (hist.Count == 0) return (0, 0, 0, 0);
        int i = hist.Count - 1;
        while (i > 0 && hist[i].T > tq) i--;
        var a = hist[i];
        int j = i;
        while (j > 0 && a.T - hist[j].T < 0.05f) j--;
        var b = hist[j];
        float dtv = a.T - b.T;
        float vx = 0, vy = 0;
        if (dtv > 0.01f && a.Id == b.Id)
        {
            vx = Mathf.Wrap(a.TYaw - b.TYaw, -180f, 180f) / dtv;
            vy = (a.TPitch - b.TPitch) / dtv;
        }
        // Own motion since then is known (efference copy): the delayed error is shifted by the crosshair travel, and the
        // target is extrapolated over the visual delay (humans predict smooth motion; "predict" = how well).
        var last = hist[^1];
        float ownX = (last.TYaw - last.Ex) - (a.TYaw - a.Ex), ownY = (last.TPitch - last.Ey) - (a.TPitch - a.Ey);
        float lead = p.Predict * Math.Max(0, last.T - a.T);
        return (a.Ex - Mathf.Wrap(ownX, -180f, 180f) + vx * lead, a.Ey - ownY + vy * lead, vx, vy);
    }

    void PlanPrimary(float now, float ex, float ey, float r)
    {
        // Moving target: aim where it will be at the end of the movement.
        var (_, _, vx, vy) = Perceived(now - 0.05f);
        float A0 = MathF.Sqrt(ex * ex + ey * ey);
        float mt = Mt(A0, r);
        float gx = ex, gy = ey;
        if (!staticTargets) { gx += vx * mt * 0.9f; gy += vy * mt * 0.9f; }
        float A = MathF.Sqrt(gx * gx + gy * gy);
        if (A < 0.6f * r && !sprayMode)
        {
            // Already on it: no flick, just confirm.
            primaryDone = true; pursuit = !staticTargets;
            if (!pursuit) evalAt = now + Math.Max(0.03f, Gauss(p.Lat, p.LatSd));
            return;
        }
        float ux = A > 0 ? gx / A : 1, uy = A > 0 ? gy / A : 0;
        float gain = Gauss(p.Gain * GainMul, p.GainSd * Math.Max(1, noiseMul));
        // Low sens: wide flicks are arm-limited and fall shorter than small ones.
        if (rho < 1) gain *= 1 - 0.4f * MathF.Log(1 / rho) * Math.Min(1f, A / 40f);
        float perp = Gauss(0, p.Dir * A * noiseMul);
        var m = new Move { T0 = now, Mt = mt, Primary = true };
        m.Dx = gx * gain - uy * perp;
        m.Dy = gy * gain + ux * perp + (p.EndDrop > 0 ? -Gauss(p.EndDrop, 0.2f * p.EndDrop) : 0);
        if (p.Bow > 0)
        {
            // Wrist arc: bulge perpendicular to the path, sagging downward.
            float nx = -uy, ny = ux;
            if (ny > 0) { nx = -nx; ny = -ny; }
            float b = Gauss(p.Bow, p.Bow * 0.2f) * A;
            m.BowX = nx * b; m.BowY = ny * b;
        }
        if (p.Premature > 0 && Clicking && rng.NextDouble() < p.Premature)
            m.PremTau = 0.62f + 0.2f * (float)rng.NextDouble();
        cur = m;
    }

    void PlanCorrection(float now, float ex, float ey, float r)
    {
        float noise = 0.12f * r;
        float px = ex + Gauss(0, noise), py = ey + Gauss(0, noise);
        float E = MathF.Sqrt(px * px + py * py);
        float gain = Gauss(p.CGain * MathF.Pow(rho, 0.3f), p.CGainSd * Math.Max(1, noiseMul));
        float perp = Gauss(0, 0.1f * E * noiseMul);
        float ux = E > 0 ? px / E : 1, uy = E > 0 ? py / E : 0;
        cur = new Move
        {
            T0 = now,
            Mt = Math.Max(0.07f, (0.07f + 0.03f * MathF.Log2(1 + E / (2 * Math.Max(0.15f, r)))) * slowMul * Gauss(1, 0.1f)),
            Dx = px * gain - uy * perp,
            Dy = py * gain + ux * perp,
        };
        if (p.Premature > 0 && Clicking && rng.NextDouble() < p.Premature * 0.5f) cur.PremTau = 0.7f;
    }

    float Mt(float A, float r)
    {
        float id = MathF.Log2(1 + A / (2 * Math.Max(0.2f, r)));
        // Low sens: wide flicks become arm-limited (slower, more so the wider they are).
        float arm = slowMul > 1 ? 1 + (slowMul - 1) * Math.Min(1.5f, A / 20f) : 1;
        return Math.Max(0.08f, (p.MtA + p.MtB * id) * arm * Gauss(1, 0.08f));
    }
}
