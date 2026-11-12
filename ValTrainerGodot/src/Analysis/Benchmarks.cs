using ValTrainer.Core;

namespace ValTrainer.Analysis;

/// <summary>
/// Rank benchmarks (spec B): typical values per tier b0…b4 (Iron–Bronze, Silver–Gold, Plat–Dia, Asc–Imm, Radiant)
/// and piecewise-linear tier scoring. Values marked † in the spec are estimates to tune on playtest data.
/// <para>Calibration (v1.1.1, see the spec's "Calibration notes"): the tiers are anchored to where a player sits in the
/// VALORANT population, not to "typical values" that bunch up between ranks. b0…b4 sit at the 5th / 41st / 81st /
/// 98.6th / 99.9th percentile of ranked players (Sep 2026 distribution: Iron 4.7%, Bronze 15.7%, Silver 20.7%,
/// Gold 22.1%, Plat 17.6%, Diamond 11.6%, Ascendant 6.3%, Immortal 1.4%, Radiant 0.05%), so the median player
/// (Gold 2) is tier ≈ 1.2. Metrics the drills measure on a different scale than the spec assumed (onset, N_c,
/// re-acquire, stop time) are re-anchored to what ValTrainer actually records.</para>
/// <para>Calibration 2 (v1.19, docs/research/coach-calibration-1.19.md): drill-aware crosshair placement, movement habits
/// only from drills that don't instruct them, tighter spray tables, a validity per skill and a clip centred on the aim
/// core (a Gold 2 with easy-drill habits read Platinum 1).</para>
/// </summary>
static class Bench
{
    /// <summary>Flick onset in the drills (targets appear on a rhythm, so this is faster than a cold reaction).</summary>
    public static readonly float[] OnsetMs = { 265, 225, 195, 172, 155 };
    /// <summary>Primary-flick endpoint error |e_end|/A; a well-calibrated flick lands within ≈ 5–8% (simulated good player ≈ 5%).</summary>
    public static readonly float[] EndErrPct = { 24, 15, 10, 7, 5 };
    public static readonly float[] FirstShotHitPct = { 62, 70, 78, 85, 90 };
    public static readonly float[] TcMs = { 420, 330, 260, 205, 165 };
    /// <summary>Corrective-submovement count used by the diagnosis rules and the sens advice (spec band).</summary>
    public static readonly float[] Nc = { 2.4f, 2.0f, 1.7f, 1.4f, 1.2f };
    /// <summary>N_c for the Precision rating, on the scale RunAnalysis records (most clean hits need under one correction).</summary>
    public static readonly float[] NcRating = { 1.8f, 1.15f, 0.8f, 0.55f, 0.4f };
    public static readonly float[] TrackOnPct = { 25, 35, 45, 55, 65 };   // vs a Veteran-tier target

    /// <summary>
    /// Strafe Tracking on-target % as a tier, normalised for the tier played. The Veteran table is linear (25% + 10 per
    /// tier); each difficulty step changes the same player's on-target % by about two table tiers (simulated player:
    /// 91 / 65 / 48 / 26% on Rookie / Regular / Veteran / Elite), so T = (on − 25)/10 + 2·(tier − 2).
    /// </summary>
    public static float TrackTier(float onPct, int tierPlayed)
    {
        if (float.IsNaN(onPct)) return float.NaN;
        int tp = tierPlayed < 0 ? 1 : Math.Clamp(tierPlayed, 0, 4);
        return Math.Clamp((onPct - TrackOnPct[0]) / (TrackOnPct[1] - TrackOnPct[0]) + 2f * (tp - 2), -0.5f, 4.5f);
    }

    /// <summary>
    /// Spidershot / Gridshot badges are about 0.8 tier more generous than Head Flicks' for the same player (simulated
    /// player: flick 2.3 vs spider 3.0–3.4 / gridshot 2.7–3.2 at every tier; real profile: 1.3 vs 2.25), so the profile
    /// takes this off their speed tier. The run's own badge is unchanged.
    /// </summary>
    public const float ScoreSpeedOffset = 0.8f;
    public static readonly float[] TauMs = { 230, 190, 160, 135, 115 };
    /// <summary>Re-acquire after a strafe reversal, as RunAnalysis measures it (back within the target after the turn).</summary>
    public static readonly float[] ReacqMs = { 240, 165, 120, 92, 75 };
    public static readonly float[] Jitter = { 0.9f, 0.7f, 0.55f, 0.45f, 0.35f };
    /// <summary>
    /// Reaction Test median, measured in-engine (no monitor / browser latency, ≈ 30–45 ms faster than Human Benchmark's
    /// 273 ms median). Population ≈ 225 ± 22 ms; lab studies put pro FPS players only ≈ 50 ms ahead of novices.
    /// </summary>
    public static readonly float[] ReactMs = { 262, 230, 206, 180, 162 };
    /// <summary>
    /// Crosshair placement: angle to the head when an enemy is first seen. Leetify (CS2) reports ≈ 10.2° for an average
    /// player and 7.5° for the top 3%, measured in matches. ValTrainer's match-like drills (Site Clear, Retake …) are on
    /// that scale; easier drills are brought onto it first (<see cref="XhairDrillFactor"/>, "xhair.err_ref_deg").
    /// </summary>
    public static readonly float[] XhairErrDeg = { 13.5f, 10.5f, 8.8f, 7.0f, 5.5f };

    /// <summary>
    /// How much smaller a drill's first-sight error is than a match's for the same player (v1.19, docs/research/
    /// coach-calibration-1.19.md §5.1): the profile rates err / k. Deathmatch bots are predictable and the player learns
    /// the spawns (a Gold 2 measured 7.3° there vs 12.1° in Site Clear, ratio 0.61; 0.70 stays conservative) [estimate,
    /// re-fit from calibration lines]. Drills where enemies come from several unknown spots are 1. Drills that announce
    /// the angle (Peek Duel, Jiggle Peek, Recon) don't count horizontally at all (RunAnalysis.HorizExcluded).
    /// </summary>
    public static float XhairDrillFactor(string baseMode) => baseMode switch
    {
        "deathmatch" => 0.70f,
        _ => 1f,
    };
    /// <summary>Typical |vertical error| on first sight (°). Capped at <see cref="PitchCap"/>.</summary>
    public static readonly float[] PitchAbsDeg = { 3.0f, 1.6f, 0.9f, 0.5f, 0.3f };
    public const float PitchCap = 2.5f;
    /// <summary>Drill sprays (static target, fixed range, visual feedback) are tighter than in-game sprays at the same rank:
    /// v1.19 tightened the spec's tables ×0.8 (1.6 1.05 0.7 0.45 0.3 / 1.1 0.8 0.55 0.4 0.28), since even a simulated
    /// player with a weak, late pull-down read Ascendant [estimate].</summary>
    public static readonly float[] SprayV = { 1.28f, 0.84f, 0.56f, 0.36f, 0.24f };
    public static readonly float[] SprayH = { 0.88f, 0.64f, 0.44f, 0.32f, 0.22f };
    /// <summary>When the pull-down starts. Starting early is necessary but not sufficient (any early pull reaches the
    /// cap): capped at <see cref="CompDelayCap"/> (2.0 before v1.19).</summary>
    public static readonly float[] CompDelayMs = { 450, 330, 250, 190, 150 };
    public const float CompDelayCap = 1.5f;
    public static readonly float[] MovingShotPct = { 28, 10, 4, 2, 1 };
    public static readonly float[] CounterPct = { 10, 30, 55, 75, 90 };
    /// <summary>
    /// Stop time as RunAnalysis measures it: key release → accurate speed. A plain release takes ≈ 112–120 ms in
    /// VALORANT's movement model and a clean counter-strafe ≈ 100 ms, so the whole range is narrow.
    /// </summary>
    public static readonly float[] StopMs = { 128, 116, 108, 102, 98 };
    public static readonly float[] FlashTurnMs = { 650, 520, 420, 340, 280 };
    /// <summary>† not in the spec table: Fitts movement time (onset → hit) at ID 4.7 bits.</summary>
    public static readonly float[] MtRefMs = { 720, 600, 500, 420, 360 };
    /// <summary>† not in the spec table: flash outcome score (dodged = 100, partial = 50, flashed = 0) vs Veteran attackers.</summary>
    public static readonly float[] FlashDodgePct = { 30, 45, 60, 75, 88 };

    /// <summary>
    /// Flash outcome as a tier, normalised for the tier played like the tracking badge (one tier per difficulty step
    /// below Veteran) and capped at one tier above it: dodging Regular attackers who swing a second late and hold fire
    /// on a blinded player says little about Diamond-level flash play.
    /// </summary>
    public static float DodgeTier(float pct, int tierPlayed)
    {
        int tp = tierPlayed < 0 ? 1 : Math.Clamp(tierPlayed, 0, 4);
        float t = TierOf(pct, FlashDodgePct);
        return float.IsNaN(t) ? float.NaN : Math.Clamp(Math.Min(t + (tp - 2), tp + 1f), -0.5f, 4.5f);
    }

    /// <summary>
    /// The diagnosis rules' stop-time thresholds were written for the spec's full-stop scale (≈ 160 ms floor): add this
    /// to the measured key-release → accurate time before comparing with them.
    /// </summary>
    public const float StopOffsetMs = 55f;

    // ---------------- overall rank ----------------

    /// <summary>
    /// Version of the rank estimate's calibration (tables, drill factors, overall formula). Bump it whenever a change
    /// moves players' estimates on the same data, so systems that keep a placement from the estimate (progression) can
    /// re-place players without taking unlocks back. 1 = v1.1.1 … v1.18; 2 = v1.19 (docs/research/coach-calibration-1.19.md).
    /// </summary>
    public const int Calibration = 2;

    /// <summary>The median ranked player (Gold 2): where an estimate goes without evidence.</summary>
    public const float PriorTier = 1.2f;
    /// <summary>
    /// How much each skill's drill measurement says about rank (v1.19, replaces one global 0.9) [estimate]: a per-skill
    /// shrink toward the median player. Nothing published puts aim-trainer scores anywhere near r = 0.9 with rank; the
    /// w-weighted mean here is ≈ 0.6 (mechanics ≈ 20–35% of rank variance). Clean aim mechanics count most; behaviour the
    /// drills make easy or ask for (placement, spray on a static target, movement, flash dodging) counts less.
    /// </summary>
    public static readonly Dictionary<Skill, float> SkillValidity = new()
    {
        [Skill.Flicking] = 0.85f, [Skill.Precision] = 0.85f, [Skill.Tracking] = 0.75f, [Skill.CrosshairPlacement] = 0.65f,
        [Skill.SprayControl] = 0.50f, [Skill.Movement] = 0.45f, [Skill.Utility] = 0.40f, [Skill.Reaction] = 0.40f,
    };
    /// <summary>The aim core: the overall's robust clip is centred on these (when at least two are rated), so a majority of
    /// drill-behaviour weight can't lift weak aim skills.</summary>
    public static bool AimCore(Skill s) => s is Skill.Flicking or Skill.Precision or Skill.Tracking or Skill.Reaction;
    /// <summary>Behaviour skills (placement, spray, movement, utility) count at most this far above the aim core's median:
    /// a drill habit can't carry the estimate (weak behaviour still pulls it down: that's a real habit gap).</summary>
    public const float BehaviourLift = 0.5f;
    /// <summary>Evidence (Σ w·c / Σ w) at which half of the skill validities apply.</summary>
    public const float Evidence0 = 0.1f;
    /// <summary>A skill counts at most this many tiers away from the (aim core's) weighted median.</summary>
    public const float OutlierClip = 0.75f;

    /// <summary>λ(E) = E / (E + E₀): how much of the validity-weighted distance from the median the data supports.</summary>
    public static float ShrinkFactor(float evidence) => evidence <= 0 ? 0 : evidence / (evidence + Evidence0);

    /// <summary>
    /// Half-width of the "likely range" shown around the estimate, in tiers: ±0.35 (two divisions) with full data, wider
    /// with less (v1.19; was 0.15 + 0.30·(1 − E), which implied r ≈ 0.96 between the estimate and rank). Still narrower
    /// than an honest 50% interval at r ≈ 0.5 (±0.49); the "aim only" note carries the rest.
    /// </summary>
    public static float RangeHalfWidth(float evidence) => 0.35f + 0.25f * (1f - Math.Clamp(evidence, 0f, 1f));

    /// <summary>How much data the estimate rests on, 0 = little, 1 = some, 2 = plenty (the overall "confidence" measured
    /// data coverage, never accuracy, so the screen says that).</summary>
    public static int DataLevel(float evidence) => evidence < 0.5f ? 0 : evidence < 0.75f ? 1 : 2;

    /// <summary>"little" / "some" / "plenty of" (English, CLI and logs).</summary>
    public static string DataAmount(float evidence) => DataLevel(evidence) switch { 0 => "little", 1 => "some", _ => "plenty of" };

    /// <summary>T(x) = i + (x − bᵢ)/(bᵢ₊₁ − bᵢ), extrapolated up to 0.5 beyond either end, clamped to [−0.5, 4.5].</summary>
    public static float TierOf(float x, float[] b)
    {
        if (float.IsNaN(x) || float.IsInfinity(x)) return float.NaN;
        float dir = MathF.Sign(b[^1] - b[0]);
        if ((x - b[0]) * dir <= 0) return Math.Max(-0.5f, (x - b[0]) / (b[1] - b[0]));
        if ((x - b[^1]) * dir >= 0) return Math.Min(4.5f, b.Length - 1 + (x - b[^1]) / (b[^1] - b[^2]));
        for (int i = 0; i < b.Length - 1; i++)
        {
            float lo = b[i], hi = b[i + 1];
            if ((x - lo) * dir >= 0 && (x - hi) * dir <= 0) return i + (x - lo) / (hi - lo);
        }
        return float.NaN;
    }

    /// <summary>Benchmark value at a continuous tier (clamped to 0…4).</summary>
    public static float ValueAt(float tier, float[] b)
    {
        if (float.IsNaN(tier)) tier = 2;
        tier = Math.Clamp(tier, 0, b.Length - 1);
        int i = Math.Min((int)tier, b.Length - 2);
        return b[i] + (b[i + 1] - b[i]) * (tier - i);
    }

    /// <summary>VALORANT rank names in English (logs, traces); <see cref="RankUi"/> for text players read (VALORANT
    /// localizes them: "Or", "Platine" …; the keys come from CoachRank.Names).</summary>
    public static readonly string[] RankNames = { "Iron", "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Ascendant", "Immortal", "Radiant" };

    /// <summary>Iron &lt; 0 ≤ Bronze &lt; 0.5 ≤ Silver &lt; 1 ≤ Gold &lt; 1.5 ≤ Platinum &lt; 2 ≤ Diamond &lt; 2.5 ≤ Ascendant &lt; 3 ≤ Immortal &lt; 3.5 ≤ Radiant.</summary>
    public static string Rank(float t)
    {
        if (float.IsNaN(t) || t < -0.75f) return L.N("Unrated");
        if (t < 0) return "Iron";
        int i = Math.Min(8, 1 + (int)(t / 0.5f));
        return RankNames[i];
    }

    /// <summary><see cref="Rank"/> in the UI language (for the coach's sentences and headlines).</summary>
    public static string RankUi(float t) => L.T(Rank(t));

    public static string TierName(int tier) => Difficulty.Get(tier).Name;
}
