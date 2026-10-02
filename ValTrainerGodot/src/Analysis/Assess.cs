using ValTrainer.Core;

namespace ValTrainer.Analysis;

/// <summary>Result of rating + diagnosing one metric dictionary (a single run or the aggregated profile).</summary>
sealed class Assessment
{
    public readonly Dictionary<Skill, SkillRating> Ratings = new();
    public float Overall = -1;            // −1 = fewer than 4 skills with confidence ≥ 0.5
    public float Provisional = float.NaN; // same formula over whatever is rated (used as a reference when Overall is −1)
    public float OverallConf;
    public float RefTier;                 // tier whose benchmark values are the "band" in the rules
    public readonly List<Issue> Issues = new();
    public readonly List<(Skill Skill, float Score, string Text)> Strengths = new();

    public sealed record Issue(Diagnosis D, float Importance, string Short, bool Possible);
}

/// <summary>Spec B (skill tiers, composites, confidence, overall rank) and C (the 20 diagnosis rules).</summary>
static class Assess
{
    public static readonly Dictionary<Skill, float> SkillWeight = new()
    {
        [Skill.CrosshairPlacement] = 0.20f, [Skill.Flicking] = 0.15f, [Skill.Precision] = 0.15f, [Skill.Movement] = 0.15f,
        [Skill.SprayControl] = 0.10f, [Skill.Tracking] = 0.10f, [Skill.Utility] = 0.10f, [Skill.Reaction] = 0.05f,
    };

    static float V(Dictionary<string, float> m, string k) => m.TryGetValue(k, out var v) ? v : float.NaN;
    static float N(Dictionary<string, float> m, string k) => m.TryGetValue(k, out var v) && !float.IsNaN(v) ? v : 0;
    static string F(FormattableString s) => FormattableString.Invariant(s);
    static float Conf(float n, float nMin) => n <= 0 ? 0 : n / (n + nMin);
    /// <summary>"+1.2" / "−0.8" / "0.0" (never "-0.0").</summary>
    static string Signed(float v) => MathF.Abs(v) < 0.05f ? "0.0" : F($"{(v > 0 ? "+" : "")}{v:0.0}");

    /// <summary>
    /// Movement is only rated once the player has actually stopped a few times: a drill played standing still would
    /// otherwise score "0% moving shots" as Radiant.
    /// </summary>
    const float MinStops = 5;

    static float Comp(params (float T, float W)[] parts)
    {
        float s = 0, w = 0;
        foreach (var (t, wt) in parts) if (!float.IsNaN(t)) { s += t * wt; w += wt; }
        return w > 0 ? s / w : float.NaN;
    }

    /// <summary>
    /// The precision benchmarks are for head-sized targets: the rating uses head-sized trials only (first-shot hits from
    /// any head, settle time / corrections from static heads). The click-behaviour rules fall back to all static targets.
    /// </summary>
    static bool HeadData(Dictionary<string, float> m) => N(m, "head.n_first") >= 5;
    static bool HeadSettle(Dictionary<string, float> m) => N(m, "head.n_hit") >= 10;
    static float Tc(Dictionary<string, float> m) => HeadSettle(m) ? V(m, "head.tc_ms") : V(m, "flick.tc_ms");
    static float Nc(Dictionary<string, float> m) => HeadSettle(m) ? V(m, "head.nc") : V(m, "flick.nc");
    static float NHit(Dictionary<string, float> m) => HeadSettle(m) ? N(m, "head.n_hit") : N(m, "flick.n_hit");

    public static Assessment Run(Dictionary<string, float> m, bool runLevel, int tierPlayed)
    {
        var a = new Assessment();
        Rate(m, a);
        // Overall = Σ w·c·T / Σ w·c, needs ≥ 4 skills with c ≥ 0.5.
        float sw = 0, s = 0, wAll = SkillWeight.Values.Sum(), wc = 0;
        int confident = 0;
        foreach (var r in a.Ratings.Values)
        {
            if (r.Tier < -0.9f || r.Confidence <= 0) continue;
            float w = SkillWeight[r.Skill] * r.Confidence;
            sw += w; s += w * r.Tier; wc += SkillWeight[r.Skill] * r.Confidence;
            if (r.Confidence >= 0.5f) confident++;
        }
        a.Provisional = sw > 0 ? s / sw : float.NaN;
        a.OverallConf = wc / wAll;
        if (confident >= 4) a.Overall = a.Provisional;
        a.RefTier = runLevel ? Math.Clamp(tierPlayed, 0, 4)
            : a.Overall >= 0 ? a.Overall
            : !float.IsNaN(a.Provisional) ? Math.Clamp(a.Provisional, 0, 4) : Math.Clamp(tierPlayed, 0, 4);
        Diagnose(m, a, runLevel);
        Strengths(m, a, runLevel, tierPlayed);
        return a;
    }

    // =====================================================================================================
    // Skill ratings
    // =====================================================================================================

    static void Rate(Dictionary<string, float> m, Assessment a)
    {
        void Put(Skill sk, float t, float c, float n, string headline)
        {
            // No confidence (e.g. tracking without a single target reversal) = not rated yet: show the progress line.
            if (float.IsNaN(t) || n <= 0 || !(c > 0)) { a.Ratings[sk] = new SkillRating(sk, -1, 0, (int)MathF.Round(Math.Max(0, n)), Progress(sk, m)); return; }
            t = Math.Clamp(t, -0.5f, 4.5f); // −1 is reserved for "not enough data"
            a.Ratings[sk] = new SkillRating(sk, t, c, (int)MathF.Round(n), headline.Length == 0 ? Bench.Rank(t) : $"{Bench.Rank(t)} · {headline}");
        }

        // Flicking: 0.5 time-to-hit (or MT_ref), 0.3 endpoint error, 0.2 onset RT.
        {
            float speedT = V(m, "speed.tier");
            string speed = "";
            if (!float.IsNaN(V(m, "flick.ttk_hit_ms"))) speed = F($"{V(m, "flick.ttk_hit_ms"):0} ms to hit");
            else if (!float.IsNaN(V(m, "speed.score_min"))) speed = F($"{V(m, "speed.score_min"):0} pts/min");
            if (float.IsNaN(speedT))
            {
                speedT = Bench.TierOf(V(m, "flick.mt_ref_ms"), Bench.MtRefMs);
                if (!float.IsNaN(speedT)) speed = F($"{V(m, "flick.mt_ref_ms"):0} ms flick time");
            }
            float endT = Bench.TierOf(V(m, "flick.end_err_pct"), Bench.EndErrPct);
            float onT = Bench.TierOf(V(m, "flick.onset_ms"), Bench.OnsetMs);
            var parts = new List<string>();
            if (speed.Length > 0) parts.Add(speed);
            if (!float.IsNaN(endT)) parts.Add(F($"lands within {V(m, "flick.end_err_pct"):0}%"));
            if (!float.IsNaN(onT)) parts.Add(F($"starts in {V(m, "flick.onset_ms"):0} ms"));
            float n = N(m, "flick.n5");
            Put(Skill.Flicking, Comp((speedT, .5f), (endT, .3f), (onT, .2f)), Conf(n, 30), n, string.Join(", ", parts));
        }
        // Precision: 0.4 first-shot hit, 0.4 T_c, 0.2 N_c — head-sized targets only (the benchmarks are for heads).
        {
            float fsh = V(m, "head.fsh_pct"), n = N(m, "head.n_first");
            bool settle = HeadSettle(m);
            float tc = settle ? V(m, "head.tc_ms") : float.NaN, nc = settle ? V(m, "head.nc") : float.NaN;
            float t = HeadData(m) ? Comp((Bench.TierOf(fsh, Bench.FirstShotHitPct), .4f), (Bench.TierOf(tc, Bench.TcMs), .4f), (Bench.TierOf(nc, Bench.Nc), .2f)) : float.NaN;
            var parts = new List<string>();
            if (!float.IsNaN(fsh)) parts.Add(F($"{fsh:0}% first-shot head hits"));
            if (!float.IsNaN(tc)) parts.Add(F($"settles in {tc:0} ms"));
            if (!float.IsNaN(nc)) parts.Add(F($"{nc:0.0} corrections"));
            Put(Skill.Precision, t, Conf(n, 30), n, string.Join(", ", parts));
        }
        // Tracking: 0.6 on-target, 0.2 τ, 0.2 re-acquire.
        {
            float secs = N(m, "track.secs"), rev = N(m, "track.reversals");
            float t = Comp((V(m, "track.ontarget_tier"), .6f), (Bench.TierOf(V(m, "track.tau_ms"), Bench.TauMs), .2f), (Bench.TierOf(V(m, "track.reacq_ms"), Bench.ReacqMs), .2f));
            var parts = new List<string>();
            if (!float.IsNaN(V(m, "track.ontarget"))) parts.Add(F($"{V(m, "track.ontarget"):0}% on target"));
            if (!float.IsNaN(V(m, "track.tau_ms"))) parts.Add(F($"{V(m, "track.tau_ms"):0} ms lag"));
            if (!float.IsNaN(V(m, "track.reacq_ms"))) parts.Add(F($"re-acquires in {V(m, "track.reacq_ms"):0} ms"));
            Put(Skill.Tracking, t, Math.Min(Conf(secs, 60), Conf(rev, 10)), secs, string.Join(", ", parts));
        }
        // Reaction.
        {
            float n = N(m, "react.n"), ms = V(m, "react.ms");
            Put(Skill.Reaction, Bench.TierOf(ms, Bench.ReactMs), Conf(n, 10), n, F($"{ms:0} ms median"));
        }
        // Crosshair placement: 0.6 E, 0.4 ε_p (too high is as bad as too low).
        {
            float n = N(m, "xhair.n"), e = V(m, "xhair.err_deg"), p = V(m, "xhair.pitch_deg");
            float t = Comp((Bench.TierOf(e, Bench.XhairErrDeg), .6f), (Bench.TierOf(-MathF.Abs(p), Bench.PitchErrDeg), .4f));
            string head = float.IsNaN(p) ? F($"{e:0.0}° off on first sight")
                : float.IsNaN(e) ? $"{Signed(p)}° from head height on first sight"
                : F($"{e:0.0}° off on first sight, {Signed(p)}° vertical");
            Put(Skill.CrosshairPlacement, t, Conf(n, 15), n, head);
        }
        // Spray: 0.4 |V|, 0.3 H, 0.3 compensation delay.
        {
            float n = N(m, "spray.n");
            float t = Comp((Bench.TierOf(V(m, "spray.v_abs"), Bench.SprayV), .4f), (Bench.TierOf(V(m, "spray.h"), Bench.SprayH), .3f),
                (Bench.TierOf(V(m, "spray.delay_ms"), Bench.CompDelayMs), .3f));
            var parts = new List<string>();
            if (!float.IsNaN(V(m, "spray.v_abs"))) parts.Add(F($"{V(m, "spray.v_abs"):0.0}° vertical"));
            if (!float.IsNaN(V(m, "spray.h"))) parts.Add(F($"{V(m, "spray.h"):0.0}° sideways"));
            if (!float.IsNaN(V(m, "spray.delay_ms"))) parts.Add(F($"pulls after {V(m, "spray.delay_ms"):0} ms"));
            Put(Skill.SprayControl, t, Conf(n, 8), n, string.Join(", ", parts));
        }
        // Movement: 0.4 moving shots, 0.3 counter-strafe use, 0.3 stop time.
        {
            float shots = N(m, "move.shots"), stops = N(m, "move.stops");
            float stop = V(m, "move.stop_ms");
            float t = stops < MinStops ? float.NaN
                : Comp((Bench.TierOf(V(m, "move.moving_shot_pct"), Bench.MovingShotPct), .4f), (Bench.TierOf(V(m, "move.counter_pct"), Bench.CounterPct), .3f),
                    (Bench.TierOf(stop + Bench.StopOffsetMs, Bench.StopMs), .3f));
            var cs = new List<float>();
            if (shots > 0) cs.Add(Conf(shots, 30));
            if (stops > 0) cs.Add(Conf(stops, 20));
            var parts = new List<string>();
            if (!float.IsNaN(V(m, "move.moving_shot_pct"))) parts.Add(F($"{V(m, "move.moving_shot_pct"):0}% moving shots"));
            if (!float.IsNaN(V(m, "move.counter_pct"))) parts.Add(F($"{V(m, "move.counter_pct"):0}% counter-strafed"));
            Put(Skill.Movement, t, cs.Count == 0 ? 0 : cs.Average(), shots + stops, string.Join(", ", parts));
        }
        // Utility (flash): 0.6 turn-away, 0.4 outcome.
        {
            float n = N(m, "flash.n");
            float t = Comp((Bench.TierOf(V(m, "flash.turn_ms"), Bench.FlashTurnMs), .6f), (Bench.TierOf(V(m, "flash.dodge_pct"), Bench.FlashDodgePct), .4f));
            var parts = new List<string>();
            if (!float.IsNaN(V(m, "flash.turn_ms"))) parts.Add(F($"turns in {V(m, "flash.turn_ms"):0} ms"));
            if (!float.IsNaN(V(m, "flash.flashed_pct"))) parts.Add(F($"flashed {V(m, "flash.flashed_pct"):0}%"));
            Put(Skill.Utility, t, Conf(n, 8), n, string.Join(", ", parts));
        }
    }

    /// <summary>Headline for an unrated skill: how far along the minimum sample is and which drill fills it.</summary>
    static string Progress(Skill sk, Dictionary<string, float> m) => sk switch
    {
        Skill.Flicking => F($"{N(m, "flick.n5"):0} of 30 flicks (5°+) — play Head Flicks or Spidershot"),
        Skill.Precision => F($"{N(m, "head.n_first"):0} of 30 head-sized targets — play Head Flicks or Strafe Bots"),
        Skill.Tracking => N(m, "track.secs") >= 60 ? F($"{N(m, "track.reversals"):0} of 10 target reversals — play Strafe Tracking")
                                                   : F($"{N(m, "track.secs"):0} of 60 s of tracking — play Strafe Tracking"),
        Skill.Reaction => F($"{N(m, "react.n"):0} of 10 trials — take the Reaction Test"),
        Skill.CrosshairPlacement => F($"{N(m, "xhair.n"):0} of 15 enemy sightings — play Peek Practice or Site Clear"),
        Skill.SprayControl => F($"{N(m, "spray.n"):0} of 8 sprays (10+ bullets) — play Vandal or Phantom Spray"),
        Skill.Movement => F($"{N(m, "move.stops"):0} of 20 stops, {N(m, "move.shots"):0} of 30 shots — play Counter-Strafe or Peek Duels"),
        Skill.Utility => F($"{N(m, "flash.n"):0} of 8 flashes — play Flash Dodge"),
        _ => "Not enough data yet",
    };

    // =====================================================================================================
    // Diagnoses (spec C)
    // =====================================================================================================

    static void Diagnose(Dictionary<string, float> m, Assessment a, bool runLevel)
    {
        float refT = a.RefTier;
        string rankName = Bench.Rank(refT);
        float overallRef = a.Overall >= 0 ? a.Overall : !float.IsNaN(a.Provisional) && !runLevel ? a.Provisional : refT;

        void Add(string id, Skill skill, int level, float n, int nMin, string title, string evidence, string why,
                 string[] fixes, string[] drills, string shortText, string? sens = null, bool forcePossible = false, string unit = "samples")
        {
            if (level <= 0 || float.IsNaN(n)) return;
            int minShow = runLevel ? Math.Max(4, nMin / 4) : Math.Max(5, nMin / 3);
            if (n < minShow) return;
            bool possible = n < nMin || forcePossible;
            var sev = level >= 2 ? Severity.Major : Severity.Minor;
            float skillT = a.Ratings.TryGetValue(skill, out var rt) && rt.Tier > -0.9f ? rt.Tier : float.NaN;
            if (!float.IsNaN(skillT) && skillT <= overallRef - 1f) sev = Severity.Major;
            if (possible) sev = Severity.Tip;
            string ev = possible
                ? (forcePossible && n >= nMin ? evidence : F($"{evidence} — early signal, only {MathF.Floor(n):0} of the {nMin} {unit} needed"))
                : F($"{evidence} ({n:0} {unit})");
            var fx = fixes.ToList();
            if (sens != null) fx.Add("Sensitivity: " + sens);
            var d = new Diagnosis(id, skill, sev, title, ev, why, fx.ToArray(), drills, (int)MathF.Round(n));
            float sevW = sev switch { Severity.Major => 2f, Severity.Minor => 1f, _ => 0.5f };
            float gap = float.IsNaN(skillT) ? 0.5f : Math.Max(0, overallRef - skillT);
            float imp = sevW * SkillWeight[skill] * (1f + gap) * (level >= 2 ? 1.25f : 1f) * (possible ? 0.5f : 1f);
            a.Issues.Add(new Assessment.Issue(d, imp, shortText, possible));
        }

        // ---- shared flick quantities ----
        float r = V(m, "flick.r_deg");
        float nOff = N(m, "flick.n_off"), nFl = N(m, "flick.n"), nPrim = N(m, "flick.n_prim"), misses = N(m, "flick.misses");
        float O = V(m, "flick.overshoot_share"), dOver = V(m, "flick.mean_overshoot_deg"), pOver = V(m, "flick.mean_overshoot_pct");
        float nc = Nc(m), tc = Tc(m), nHit = NHit(m);
        float J = V(m, "track.jitter"), bandJ = Bench.ValueAt(refT, Bench.Jitter);
        float bandNc = Bench.ValueAt(refT, Bench.Nc), bandTc = Bench.ValueAt(refT, Bench.TcMs);

        // OVERFLICK
        if (O >= .55f && dOver >= 1.5f * r && pOver >= 4f)
        {
            bool highNcJ = nc > bandNc + 0.3f || (!float.IsNaN(J) && J > 1.3f * bandJ);
            Add("OVERFLICK", Skill.Flicking, O >= .70f ? 2 : 1, nOff, 30,
                "Over-flicking: your flicks travel too far",
                F($"{O * 100:0}% of your off-target flicks ended past the target, by {dOver:0.0}° ({pOver:0}% of the distance)"),
                "The flick is planned too long, or the arm is tense and brakes late. Undoing an overshoot costs more time than finishing a short flick: you have to stop, reverse and re-aim.",
                new[]
                {
                    "Aim to land about 90% of the way there and finish with one small micro-adjustment.",
                    "Brake early: the second half of the flick should feel like slowing down, not pushing.",
                    "Relax your grip and forearm — a tense arm overshoots and can't stop cleanly.",
                    "Play Head Flicks one tier lower until most flicks land on or just short of the head.",
                },
                new[] { "flick", "spider" },
                F($"{O * 100:0}% of your off-target flicks overshot the target by {dOver:0.0}°"),
                highNcJ ? "you also need many corrections / track shakily — try 5–10% lower sens." : null, unit: "off-target flicks");
        }

        // UNDERFLICK
        float G = V(m, "flick.gain"), U = V(m, "flick.under_share"), dUnder = V(m, "flick.mean_undershoot_deg");
        if (G < .85f && U >= .85f || G < .75f && U >= .7f)
        {
            float gs = V(m, "flick.gain_small"), gl = V(m, "flick.gain_large");
            bool wideOnly = gl < .85f && gs >= .9f && N(m, "flick.n_large") >= 5;
            Add("UNDERFLICK", Skill.Flicking, G < .75f ? 2 : 1, nPrim, 30,
                "Stopping short and dragging onto the target",
                F($"Your flicks covered {G * 100:0}% of the distance; {U * 100:0}% of the off-target ones stopped short")
                    + (float.IsNaN(dUnder) ? "" : F($", by {dUnder:0.0}° on average")),
                "Hesitation, or your arm runs out of room on wide angles, so every flick ends with a slow drag onto the head.",
                new[]
                {
                    "Commit to one full motion that reaches the head; keep the micro-adjustment tiny.",
                    "Use your arm, not just the wrist, for wide flicks so the first motion covers the whole distance.",
                    "Look at the target first, then move — the eyes lead the hand.",
                },
                new[] { "spider", "flick" },
                F($"your flicks stopped short (covering {G * 100:0}% of the distance)"),
                wideOnly ? F($"only your wide flicks (≥ 25°) fall short ({gl * 100:0}% vs {gs * 100:0}%) — try 5–10% higher sens.") : null, unit: "flicks");
        }

        // NO_MICRO
        float snapShare = V(m, "flick.snap_share"), snapErr = V(m, "flick.snap_err_deg");
        if (snapShare >= .40f)
            Add("NO_MICRO", Skill.Precision, snapShare >= .60f ? 2 : 1, misses, 20,
                "Flicks have no micro-adjustment",
                float.IsNaN(snapErr)
                    ? F($"{snapShare * 100:0}% of your misses were fired straight off the flick, with no correction")
                    : F($"{snapShare * 100:0}% of your misses were fired straight off the flick, {snapErr:0.0}° from the target, with no correction"),
                "The click is chained to the flick: you fire the moment your hand stops, not when the crosshair is actually on the head.",
                new[]
                {
                    "Think \"flick, confirm, click\" — a split-second check before every shot.",
                    "If the flick lands off, make one small correction instead of firing anyway.",
                    "Play Head Flicks at Rookie (untimed) until you reach 85% accuracy, then speed up.",
                },
                new[] { "flick", "gridshot" },
                F($"{snapShare * 100:0}% of your misses were fired straight off the flick with no correction"), unit: "misses");

        // SLOW_MICRO
        if (!float.IsNaN(tc) && (tc > 1.3f * bandTc || nc >= 2.5f))
        {
            float amp = V(m, "flick.corr_amp_r");
            string? sens = amp < 2f && nc >= Math.Max(2f, bandNc + 0.3f) ? "lots of corrections smaller than two head-widths usually means the sens is a bit high — try 5% lower."
                : amp >= 2f && nc < 1.6f ? "one long, slow drag after the flick can mean the sens is a bit low — try 5% higher." : null;
            Add("SLOW_MICRO", Skill.Precision, nc >= 3f || tc > 1.7f * bandTc ? 2 : 1, nHit, 30,
                "Slow, fragmented micro-adjustments",
                float.IsNaN(nc)
                    ? F($"After the flick you needed {tc:0} ms to settle (typical for {rankName}: {bandTc:0} ms)")
                    : F($"After the flick you needed {tc:0} ms and {nc:0.0} corrections to settle (typical for {rankName}: {bandTc:0} ms, {bandNc:0.0})"),
                "Many small corrections instead of one decisive one — every extra correction costs about 100–150 ms.",
                new[]
                {
                    "Make one deliberate correction with your fingers and wrist, then click.",
                    "Keep the hand loose after the flick so the correction is quick and small.",
                    "The head is bigger than its centre: click once you're on it, don't chase the exact middle.",
                },
                new[] { "gridshot", "flick" },
                float.IsNaN(nc) ? F($"you took {tc:0} ms to settle after each flick") : F($"you took {tc:0} ms and {nc:0.0} corrections to settle after each flick"),
                sens, unit: "hits");
        }

        // PREMATURE
        float prem = V(m, "flick.premature_share"), premDps = V(m, "flick.premature_dps");
        if (prem >= .25f)
            Add("PREMATURE", Skill.Precision, prem >= .40f ? 2 : 1, misses, 20,
                "Clicking while the crosshair is still flying",
                float.IsNaN(premDps)
                    ? F($"{prem * 100:0}% of your misses were fired while the crosshair was still moving")
                    : F($"{prem * 100:0}% of your misses were fired at {premDps:0}°/s, before you stopped"),
                "Rhythm-clicking: the click is timed to the motion instead of to the crosshair being on target.",
                new[]
                {
                    "Stop, then shoot — the crosshair should be still when you click.",
                    "Slow your flicks down about 10% until your accuracy recovers.",
                    "Say \"stop-click\" in your head for a few sessions to break the rhythm.",
                },
                new[] { "flick" },
                F($"{prem * 100:0}% of your misses were fired while the crosshair was still moving"), unit: "misses");

        // HESITATE
        float dwell = V(m, "flick.dwell_ms");
        if (dwell > 150)
            Add("HESITATE", Skill.Precision, dwell > 250 ? 2 : 1, N(m, "flick.n_hit"), 30,
                "On the head but waiting to click",
                F($"You sat on the target for a median {dwell:0} ms before firing"),
                "Over-checking: you confirm twice. In a real duel the enemy fires in that gap.",
                new[]
                {
                    "Fire as soon as the crosshair touches the head.",
                    "Trust the first confirmation — speed up the click, not the flick.",
                    "Play Gridshot focusing only on clicking the instant you arrive.",
                },
                new[] { "gridshot" },
                F($"you waited {dwell:0} ms on the target before clicking"), unit: "hits");

        // CURVED (path efficiency of the primary flick itself, so overshoot returns don't count as curvature)
        float etaP = V(m, "flick.eta_p"), endY = V(m, "flick.end_y_deg"), low = V(m, "flick.low_share");
        bool curved = etaP < .85f, lowEnd = endY < -r;
        if (curved || lowEnd)
        {
            string lowTxt = float.IsNaN(low) ? F($"they ended {-endY:0.0}° below the head on average")
                : F($"{low * 100:0}% ended below the head ({-endY:0.0}° low on average)");
            Add("CURVED", Skill.Flicking, etaP < .75f ? 2 : 1, nPrim, 30,
                curved && lowEnd ? "Curved flicks that end under the head" : curved ? "Curved flicks" : "Flicks that end under the head",
                curved && lowEnd ? F($"Your flicks wasted {(1 - etaP) * 100:0}% of their path; {lowTxt}")
                    : curved ? F($"Your flicks wasted {(1 - etaP) * 100:0}% of their path by curving instead of going straight")
                    : float.IsNaN(low) ? F($"Your flicks ended {-endY:0.0}° below the head on average")
                    : F($"{low * 100:0}% of your flicks ended below the head ({-endY:0.0}° low on average)"),
                "The wrist arcs instead of moving in a straight line, or the mouse pad sits at an angle, so flicks curve and land low.",
                new[]
                {
                    "Straighten the motion: move along the line to the target instead of pivoting around the wrist.",
                    "Line your mouse pad up with the monitor and sit square to it.",
                    "Level the crosshair at head height first, then flick sideways along that line.",
                },
                new[] { "spider" },
                lowEnd ? F($"your flicks tended to end {-endY:0.0}° below the head") : F($"your flicks curved, wasting {(1 - etaP) * 100:0}% of their path"),
                unit: "flicks");
        }

        // SLOW_START
        float onset = V(m, "flick.onset_ms"), react = V(m, "react.ms");
        bool haveReact = !float.IsNaN(react) && N(m, "react.n") >= 5;
        float baseRt = haveReact ? react : 250f;
        if (onset - baseRt > 120)
            Add("SLOW_START", Skill.Flicking, onset - baseRt > 180 ? 2 : 1, N(m, "flick.onset_n"), 30,
                "Slow to start moving",
                haveReact
                    ? F($"You start moving {onset - baseRt:0} ms later than your raw reaction time (flick onset {onset:0} ms vs {react:0} ms in the Reaction Test)")
                    : F($"Your flicks start {onset:0} ms after the target appears — about {onset - baseRt:0} ms slower than a typical 250 ms reaction (take the Reaction Test for an exact comparison)"),
                "The delay is in finding the target or deciding where to go, not in your hands.",
                new[]
                {
                    "Keep your eyes at the centre of the screen and use peripheral vision to spot targets.",
                    "Start moving the moment you see colour — refine the exact spot while already moving.",
                    "Warm up with the Reaction Test, then Gridshot.",
                },
                new[] { "reaction", "gridshot" },
                F($"you took {onset:0} ms to start moving toward each target"), null, !haveReact, unit: "flicks");

        // SLOW_REACT
        float bandR = Bench.ValueAt(refT, Bench.ReactMs);
        if (react > bandR + 30 || react > 300)
            Add("SLOW_REACT", Skill.Reaction, react > bandR + 80 || react > 360 ? 2 : 1, N(m, "react.n"), 10,
                "Slow reaction time",
                F($"Median {react:0} ms (typical for {rankName}: {bandR:0} ms)"),
                "Usually fatigue or input lag rather than talent.",
                new[]
                {
                    "Turn V-Sync off and check your FPS cap and mouse polling rate.",
                    "Warm up for 5 minutes before playing ranked.",
                    "Test again when rested — tiredness costs 20–40 ms.",
                },
                new[] { "reaction" },
                F($"your median reaction was {react:0} ms"), unit: "trials");

        // ---- tracking ----
        float tSecs = N(m, "track.secs"), rev = N(m, "track.reversals"), tr = V(m, "track.r_deg");
        float lag = V(m, "track.lag_deg"), tau = V(m, "track.tau_ms"), overrun = V(m, "track.overrun_deg"), overEx = V(m, "track.overrun_excess_deg");
        if (!(tr > 0)) tr = float.NaN; // no target size → the lag / overrun thresholds can't be judged
        bool behind = lag > 0.6f * tr, lateT = tau > 200;
        if (behind || lateT)
            Add("TRACK_BEHIND", Skill.Tracking, lag > tr ? 2 : 1, tSecs, 60,
                "Chasing the target instead of matching it",
                behind && !float.IsNaN(tau) ? F($"You trailed the target by {lag:0.00}° on average, following its motion {tau:0} ms late")
                    : behind ? F($"You trailed the target by {lag:0.00}° on average")
                    : F($"Your aim followed the target's motion {tau:0} ms late (typical for {rankName}: {Bench.ValueAt(refT, Bench.TauMs):0} ms)"),
                "You react to where the target was instead of matching its speed.",
                new[]
                {
                    "Match the target's speed first, then fine-tune the position.",
                    "Ride the leading edge of the target, not its centre.",
                    "Play Strafe Tracking one tier lower until you stay on target 50%+.",
                },
                new[] { "tracking", "strafebots" },
                behind ? F($"you trailed the target by {lag:0.00}°") : F($"your aim followed the target {tau:0} ms late"),
                !float.IsNaN(J) && J <= bandJ ? "your tracking is smooth but behind — try 5% higher sens." : null, unit: "s of tracking");
        // Overrun is judged beyond the unavoidable carry-over of a ~120 ms reaction (see RunAnalysis.Tracking).
        bool ahead = lag < -0.4f * tr, overran = overEx > 1.5f * tr && !float.IsNaN(overrun);
        if (ahead || overran)
            Add("TRACK_OVERRUN", Skill.Tracking, overEx > 2.5f * tr ? 2 : 1, rev, 10,
                "Overshooting strafe reversals",
                overran
                    ? F($"When the target changed direction you ran {overrun:0.00}° past it — {overEx:0.00}° more than a normal reaction carries you")
                      + (ahead ? F($"; you also ran {-lag:0.00}° ahead of it on average") : "")
                    : F($"You ran {-lag:0.00}° ahead of the target on average, as if expecting it to keep going"),
                "You're anticipating the strafe instead of reacting to it.",
                new[]
                {
                    "React to the stop — watch the body, not where you expect it to go.",
                    "Ease off as the target slows down; don't keep pushing through.",
                    "Play Strafe Tracking at Veteran or higher to practise reversals.",
                },
                new[] { "tracking" },
                overran ? F($"you ran {overrun:0.00}° past the target when it changed direction") : F($"you ran {-lag:0.00}° ahead of the target"),
                !float.IsNaN(J) && J > 1.3f * bandJ ? "combined with shaky tracking this points to a slightly high sens — try 5% lower." : null,
                unit: "direction changes");
        if (J > 1.3f * bandJ)
            Add("JITTER", Skill.Tracking, J > 1.6f * bandJ ? 2 : 1, tSecs, 60,
                "Shaky tracking",
                F($"Your aim speed wobbled {J:0.00}× the target's motion (typical for {rankName}: {bandJ:0.00})"),
                "A tight grip or fingertip-only aiming adds tremor and constant over-corrections.",
                new[]
                {
                    "Relax your grip — hold the mouse, don't squeeze it.",
                    "Track with your arm and wrist; keep the fingers quiet.",
                    "Smooth beats fast: let small errors go instead of chasing them.",
                },
                new[] { "tracking" },
                F($"your tracking wobbled ({J:0.00}× the target's motion)"),
                "try 5–10% lower sens.", unit: "s of tracking");
        float reacq = V(m, "track.reacq_ms"), bandRq = Bench.ValueAt(refT, Bench.ReacqMs);
        if (reacq > 1.3f * bandRq)
            Add("SLOW_REACQ", Skill.Tracking, reacq > 1.6f * bandRq ? 2 : 1, rev, 10,
                "Slow to re-acquire after a direction change",
                F($"It took {reacq:0} ms to get back on after a direction change (typical for {rankName}: {bandRq:0} ms)"),
                "After the target changes direction your correction comes late or overshoots, so you spend time off target.",
                new[]
                {
                    "Watch the body for the moment it plants, then reverse immediately.",
                    "Make the reversal one quick, small adjustment instead of a big swing.",
                },
                new[] { "strafebots", "tracking" },
                F($"you needed {reacq:0} ms to get back on after each direction change"), unit: "direction changes");

        // ---- crosshair placement ----
        float xn = N(m, "xhair.n"), ep = V(m, "xhair.pitch_deg"), below = V(m, "xhair.below_pct"), eh = V(m, "xhair.h_deg");
        bool epLow = ep < -0.05f;
        if (ep < -1.5f || below >= 60)
            Add("XHAIR_LOW", Skill.CrosshairPlacement, ep < -3f ? 2 : 1, xn, 15,
                "Crosshair too low",
                epLow && !float.IsNaN(below) ? F($"Enemies appeared {-ep:0.0}° above your crosshair on average (it was below the head in {below:0}% of sightings)")
                    : epLow ? F($"Enemies appeared {-ep:0.0}° above your crosshair on average")
                    : F($"Your crosshair was below the head in {below:0}% of sightings"),
                "You're looking at the floor or body height, so every fight starts with a vertical flick.",
                new[]
                {
                    "Hold a head-height line and move the crosshair along it.",
                    "Use map details at head height (door frames, box edges) as reference points.",
                    "After every kill, reset the crosshair to head level — not down.",
                },
                new[] { "peek", "siteclear" },
                epLow ? F($"enemies appeared {-ep:0.0}° above your crosshair") : F($"your crosshair was below head height in {below:0}% of sightings"),
                unit: "sightings");
        float bandE = Bench.ValueAt(refT, Bench.XhairErrDeg);
        if (eh > bandE)
            Add("XHAIR_WIDE", Skill.CrosshairPlacement, eh > 2 * bandE ? 2 : 1, N(m, "xhair.nh"), 15,
                "Not pre-aiming the angle",
                F($"Enemies appeared {eh:0.0}° to the side of your crosshair (typical for {rankName}: {bandE:0.0}°)"),
                "The crosshair is parked in open space instead of where enemies will appear.",
                new[]
                {
                    "Sit just off the cover edge where they'll appear.",
                    "Clear one angle at a time, keeping the crosshair glued to the edge.",
                    "When you move, slide the crosshair along the wall edge with you.",
                },
                new[] { "peek", "peekduel" },
                F($"enemies appeared {eh:0.0}° to the side of your crosshair"), unit: "sightings");

        // ---- movement ----
        float mv = V(m, "move.moving_shot_pct"), early = V(m, "move.early_shot_pct"), mvSpeed = V(m, "move.moving_speed");
        bool moving = mv >= 15, earlyStop = early >= 25;
        if (moving || earlyStop)
            Add("RUN_GUN", Skill.Movement, mv >= 30 || early >= 30 ? 2 : 1, N(m, "move.shots"), 30,
                "Shooting while moving",
                (moving
                    ? (float.IsNaN(mvSpeed) ? F($"{mv:0}% of your shots were fired while moving") : F($"{mv:0}% of your shots were fired at {mvSpeed:0.0} m/s"))
                      + " (only shots under 1.35 m/s are accurate)"
                    : "")
                + (earlyStop
                    ? (moving ? F($"; {early:0}% within 0.1 s of starting to stop") : F($"{early:0}% of your shots were fired within 0.1 s of starting to stop, before you were accurate"))
                    : ""),
                "Moving bullets are wildly inaccurate in VALORANT — the first shot can land metres off.",
                new[]
                {
                    "Stop, then shoot: counter-strafe and click once you're still.",
                    "Don't hold a movement key while firing — tap the opposite key, then click.",
                },
                new[] { "counterstrafe", "peekduel" },
                moving ? F($"{mv:0}% of your shots were fired while moving") : F($"{early:0}% of your shots were fired before you had finished stopping"),
                unit: "shots");
        float cs = V(m, "move.counter_pct"), stopMs = V(m, "move.stop_ms"), stopAdj = stopMs + Bench.StopOffsetMs;
        bool fewCounter = cs < 40, slowStop = stopAdj > 200;
        if (fewCounter || slowStop)
            Add("CLUMSY_MOVE", Skill.Movement, cs < 20 || stopAdj > 240 ? 2 : 1, N(m, "move.stops"), 20,
                "Clumsy stops",
                fewCounter
                    ? (cs < 0.5f ? "None of your stops were counter-strafed" : F($"Only {cs:0}% of stops were counter-strafed"))
                      + (float.IsNaN(stopMs) ? "" : F($"; average stop {stopMs:0} ms to accurate speed (best ≈ 105)"))
                    : F($"Your stops took {stopMs:0} ms on average to reach accurate speed (best ≈ 105)") + (float.IsNaN(cs) ? "" : F($", with {cs:0}% counter-strafed")),
                "Releasing the keys and waiting to slow down wastes time every time you peek.",
                new[]
                {
                    "Tap the opposite key for about 80 ms, then release both.",
                    "Drill the rhythm: strafe, tap, shoot, strafe.",
                },
                new[] { "counterstrafe" },
                !fewCounter ? F($"your stops took {stopMs:0} ms to reach accurate speed")
                    : cs < 0.5f ? "none of your stops were counter-strafed" : F($"only {cs:0}% of your stops were counter-strafed"),
                unit: "stops");

        // ---- spray ----
        float sn = N(m, "spray.n"), sv = V(m, "spray.v"), svAbs = V(m, "spray.v_abs"), sh = V(m, "spray.h"), sd = V(m, "spray.delay_ms");
        bool vert = svAbs > 1.0f, latePull = sd > 300;
        if (vert || latePull)
        {
            bool high = float.IsNaN(sv) || sv >= 0;
            // Sprays that miss high and low in turn average out: report the typical size, not the (small) mean.
            bool mixed = vert && !float.IsNaN(sv) && MathF.Abs(sv) < 0.5f * svAbs;
            string where = mixed ? F($"strayed {svAbs:0.0}° vertically — too high in some sprays, too low in others")
                : F($"landed {svAbs:0.0}° {(high ? "high" : "low")}");
            string[] highFix = { "Start pulling down by bullet 2–3, steadily — not one big late drag.", "Pull at a constant speed for the first second; the Vandal climbs about 5°/s.", "Watch the chart after each spray and match the grey pattern in reverse." };
            Add("SPRAY_VERT", Skill.SprayControl, svAbs > 1.8f ? 2 : 1, sn, 8,
                !vert ? "Late recoil compensation" : mixed ? "Inconsistent recoil control" : high ? "Spray climbs over the target" : "Over-pulling your spray",
                !vert ? F($"You started pulling down {sd:0} ms into the spray (aim for under 300 ms)")
                    : $"Bullets 4–15 {where}" + (float.IsNaN(sd) ? "" : F($"; you started pulling {sd:0} ms in")),
                !vert ? "You wait for the climb before reacting to it, so the first bullets after the opener already go high."
                    : mixed ? "Your pull changes from spray to spray — sometimes too little, sometimes too much — so the pattern never settles."
                    : high ? "You start pulling down too late or not hard enough, so the climb carries bullets 4+ over the target."
                    : "You pull down harder than the gun climbs, dragging the spray under the target.",
                mixed ? new[] { "Use the same pull every time: start by bullet 2–3 and keep a steady speed.", "Rest the crosshair on the target before each spray so every spray starts the same way.", "Watch the chart after each spray and match the grey pattern in reverse." }
                    : high || !vert ? highFix
                    : new[] { "Pull less: match the climb, don't beat it.", "Ease off the pull after bullet 10 — the climb flattens out.", "Watch the chart after each spray and match the grey pattern in reverse." },
                new[] { "spray_vandal", "spray_phantom" },
                !vert ? F($"you started pulling down {sd:0} ms into each spray") : $"bullets 4–15 of your sprays {where}",
                unit: "sprays");
        }
        if (sh > 0.9f)
            Add("SPRAY_HORIZ", Skill.SprayControl, sh > 1.4f ? 2 : 1, sn, 8,
                "Spray drifts sideways",
                F($"Late bullets scattered {sh:0.0}° left-right"),
                "You're not countering the sideways drift that starts around bullet 8.",
                new[]
                {
                    "From bullet 8 on, watch which way the spray drifts and pull the opposite way.",
                    "Keep the counter-pull smooth — sudden sideways jerks make it worse.",
                },
                new[] { "spray_transfer", "spray_vandal" },
                F($"your late bullets scattered {sh:0.0}° sideways"), unit: "sprays");

        // ---- utility ----
        float turn = V(m, "flash.turn_ms"), bandF = Bench.ValueAt(refT, Bench.FlashTurnMs);
        if (turn > bandF + 100)
            Add("FLASH_SLOW", Skill.Utility, turn > bandF + 250 ? 2 : 1, N(m, "flash.turn_n"), 8,
                "Late flash turns",
                F($"You turned away {turn:0} ms after the cue (typical for {rankName}: {bandF:0} ms)"),
                "Waiting to see the flash instead of reacting to the throw sound.",
                new[]
                {
                    "Turn on the audio cue — every agent's flash has a distinct sound.",
                    "Turn at least 90° away, then swing back right after the pop.",
                },
                new[] { "flashmap" },
                F($"you turned away from flashes {turn:0} ms after the cue"), unit: "flashes");

        a.Issues.Sort((x, y) => y.Importance.CompareTo(x.Importance));
    }

    // =====================================================================================================
    // Strengths
    // =====================================================================================================

    static void Strengths(Dictionary<string, float> m, Assessment a, bool runLevel, int tierPlayed)
    {
        float baseT = runLevel ? Math.Clamp(tierPlayed, 0, 4) : a.Overall >= 0 ? a.Overall : a.Provisional;
        if (float.IsNaN(baseT)) return;
        float minC = runLevel ? 0.2f : 0.5f;
        // Any issue (even a "possible" one) blocks praising the same skill, so a review never says both "slow micro-adjustments" and "clean micro-adjustments".
        var flagged = a.Issues.Select(i => i.D.Skill).ToHashSet();
        foreach (var r in a.Ratings.Values)
        {
            if (r.Tier < -0.9f || r.Confidence < minC || r.Tier < baseT + 0.5f || flagged.Contains(r.Skill)) continue;
            string rank = Bench.Rank(r.Tier);
            bool fast = V(m, "speed.tier") >= baseT || float.IsNaN(V(m, "speed.tier")) && !float.IsNaN(V(m, "flick.mt_ref_ms")) && Bench.TierOf(V(m, "flick.mt_ref_ms"), Bench.MtRefMs) >= baseT;
            float endErr = V(m, "flick.end_err_pct"), turnMs = V(m, "flash.turn_ms"), vAbs = V(m, "spray.v_abs");
            string text = r.Skill switch
            {
                Skill.Flicking => float.IsNaN(endErr) ? $"Fast flicks ({rank} level)."
                    : fast ? F($"Fast, accurate flicks ({rank} level): they land within {endErr:0}% of the distance.")
                    : F($"Accurate flicks ({rank} level): they land within {endErr:0}% of the distance."),
                Skill.Precision => float.IsNaN(V(m, "head.tc_ms")) ? F($"Precise first shots ({rank} level): {V(m, "head.fsh_pct"):0}% of them hit the head.")
                    : F($"Clean micro-adjustments ({rank} level): you settle on the head in {V(m, "head.tc_ms"):0} ms."),
                Skill.Tracking => F($"Smooth tracking ({rank} level): on target {V(m, "track.ontarget"):0}% of the time while firing."),
                Skill.Reaction => F($"Quick reactions ({rank} level): {V(m, "react.ms"):0} ms median."),
                Skill.CrosshairPlacement => float.IsNaN(V(m, "xhair.err_deg")) ? F($"Great crosshair height ({rank} level): enemies appear within {MathF.Abs(V(m, "xhair.pitch_deg")):0.0}° of head level.")
                    : F($"Great crosshair placement ({rank} level): enemies appear only {V(m, "xhair.err_deg"):0.0}° from your crosshair."),
                Skill.SprayControl => float.IsNaN(vAbs) ? $"Good spray control ({rank} level)."
                    : F($"Good spray control ({rank} level): bullets 4–15 stay within {vAbs:0.0}° vertically."),
                Skill.Movement => F($"Disciplined movement ({rank} level): {V(m, "move.counter_pct"):0}% of stops counter-strafed."),
                Skill.Utility => float.IsNaN(turnMs) ? F($"Sharp flash dodging ({rank} level): you beat {V(m, "flash.dodge_pct"):0}% of flashes.")
                    : F($"Sharp flash dodging ({rank} level): you turn away in {turnMs:0} ms."),
                _ => "",
            };
            a.Strengths.Add((r.Skill, r.Tier - baseT, text));
        }
        // Metric-level positives (useful for single-run reviews where few skills are rated).
        void Pos(string text, float score) { if (!a.Strengths.Any(s => s.Text == text)) a.Strengths.Add((Skill.Precision, score, text)); }
        float dwell = V(m, "flick.dwell_ms");
        if (dwell < 90 && N(m, "flick.n_hit") >= 8 && !flagged.Contains(Skill.Precision)) Pos(F($"Decisive clicks: you fire {dwell:0} ms after reaching the target."), 0.3f);
        float o = V(m, "flick.overshoot_share"), err = V(m, "flick.end_err_pct");
        if (err <= 10 && N(m, "flick.n_prim") >= 8 && !flagged.Contains(Skill.Flicking)) Pos(F($"Accurate first motions: your flicks land within {err:0}% of the distance."), 0.25f);
        float fsh = HeadData(m) ? V(m, "head.fsh_pct") : float.NaN;
        if (fsh >= 85 && N(m, "head.n_first") >= 8) Pos(F($"{fsh:0}% of your first shots hit the head."), 0.3f);
        if (V(m, "xhair.pitch_deg") is var xp && MathF.Abs(xp) < 0.6f && N(m, "xhair.n") >= 5) Pos(F($"Crosshair at head height: enemies appear within {MathF.Abs(xp):0.0}° vertically."), 0.3f);
        a.Strengths.Sort((x, y) => y.Score.CompareTo(x.Score));
        _ = o;
    }
}
