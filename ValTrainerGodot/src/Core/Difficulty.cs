namespace ValTrainer.Core;

/// <summary>
/// How a bot fights. Reaction is a human-like normal distribution; after reacting the bot still has to
/// move its crosshair onto you (Fitts' law: <see cref="MsPerBit"/> per bit of difficulty), then fires the
/// Vandal at 9.75 rounds/s with a per-shot hit chance that decays during a spray.
/// </summary>
public sealed record BotSkill(
    float ReactMs, float ReactSd,      // reaction time distribution (ms)
    float XhairErrDeg,                 // how far off your head their crosshair placement typically is
    float MsPerBit,                    // aim-settle speed (Fitts' law)
    float PHit1, float PHead1,         // first-shot hit / headshot chance
    float Decay,                       // hit chance multiplier per extra bullet in a spray
    int Burst, float GapMs,            // bullets per burst, pause between bursts
    bool RunAndGun);                   // shoots while moving (low tiers) instead of stopping first

/// <summary>A difficulty tier calibrated to Valorant ranks (see README for sources).</summary>
public sealed record Tier(int Id, string Name, string Ranks, BotSkill Bot);

public static class Difficulty
{
    public static readonly Tier[] Tiers =
    {
        // Recalibrated after playtest feedback ("Rookie still too hard"): our bots have advantages no human has
        // (perfect awareness, no peeker's advantage for you), so low tiers are deliberately softer than a raw
        // human model. Simulated median time-to-kill vs a player standing in view: Rookie 4.4 s, Regular 2.4 s,
        // Veteran 1.4 s, Elite 0.84 s, Pro 0.53 s (scratchpad botsim.py mirrors BotBrain).
        new(0, "Rookie",  "Iron – Bronze",       new(480, 90, 9.0f, 300, .26f, .05f, .85f, 3, 420, true)),
        new(1, "Regular", "Silver – Gold",       new(370, 65, 6.0f, 230, .38f, .10f, .88f, 4, 320, true)),
        new(2, "Veteran", "Platinum – Diamond",  new(290, 45, 3.5f, 160, .55f, .18f, .92f, 4, 220, false)),
        new(3, "Elite",   "Ascendant – Immortal", new(225, 30, 1.8f, 110, .72f, .28f, .95f, 4, 170, false)),
        new(4, "Pro",     "Radiant",             new(180, 22, 0.8f,  80, .86f, .42f, .96f, 4, 140, false)),
    };

    public static Tier Get(int id) => Tiers[Math.Clamp(id, 0, Tiers.Length - 1)];

    /// <summary>
    /// The deathmatch finisher's bots: <paramref name="tier"/>'s bot with the combat skill (reaction, crosshair error,
    /// settle speed, hit chances) of one tier lower. Burst rhythm and run-and-gun stay the tier's own. Rookie has no lower
    /// tier: it reacts 30% slower and hits 25% less often instead.
    /// </summary>
    public static BotSkill EasedBot(int tier)
    {
        var b = Get(tier).Bot;
        if (tier <= 0) return b with { ReactMs = b.ReactMs * 1.3f, ReactSd = b.ReactSd * 1.3f, PHit1 = b.PHit1 * 0.75f, PHead1 = b.PHead1 * 0.75f };
        var l = Get(tier - 1).Bot;
        return b with
        {
            ReactMs = l.ReactMs, ReactSd = l.ReactSd, XhairErrDeg = l.XhairErrDeg, MsPerBit = l.MsPerBit,
            PHit1 = l.PHit1, PHead1 = l.PHead1, Decay = l.Decay,
        };
    }

    /// <summary>Pick per-tier values: T(tier, rookie, regular, veteran, elite, pro).</summary>
    public static float T(int tier, params float[] v) => v[Math.Clamp(tier, 0, v.Length - 1)];
    public static int Ti(int tier, params int[] v) => v[Math.Clamp(tier, 0, v.Length - 1)];

    // ---- continuous levels (Lock-In adaptive drills): level 2.5 = halfway between Veteran and Elite ----

    /// <summary>Per-tier values at a continuous level: linear interpolation between the neighbouring tiers (whole levels
    /// give exactly the tier's value).</summary>
    public static float Tf(float level, params float[] v)
    {
        level = Math.Clamp(level, 0f, v.Length - 1);
        int i = (int)level;
        return i >= v.Length - 1 ? v[^1] : v[i] + (v[i + 1] - v[i]) * (level - i);
    }

    /// <summary>
    /// Like <see cref="Tf"/> for lifetimes where 0 means "never expires". Between a tier without expiry and the next tier
    /// with lifetime L, the lifetime is L / f (f = how far past the lower tier the level is): no expiry at the lower tier,
    /// very long just above it, L at the next tier. Continuous, so a small level change never jumps from "never" to L.
    /// </summary>
    public static float TfLifetime(float level, params float[] v)
    {
        level = Math.Clamp(level, 0f, v.Length - 1);
        int i = (int)level;
        if (i >= v.Length - 1) return v[^1];
        float f = level - i, a = v[i], b = v[i + 1];
        if (a > 0 && b > 0) return a + (b - a) * f;
        if (a <= 0 && b <= 0) return 0f;
        if (a <= 0) return f < 0.05f ? 0f : b / f;  // from "never" towards L
        return a;                                   // never happens in the tables (expiry only gets stricter)
    }

    static (float, float) Tf2(float level, (float, float)[] v) =>
        (Tf(level, v.Select(x => x.Item1).ToArray()), Tf(level, v.Select(x => x.Item2).ToArray()));

    // ---------------- per-mode parameters (research-calibrated) ----------------

    public static float GridshotRadius(int t) => T(t, .45f, .36f, .28f, .22f, .17f);
    public static float GridshotSpacing(int t) => T(t, 1.15f, 1.15f, 1.25f, 1.35f, 1.45f);

    public static (float min, float max) FlickDistance(int t) => t switch
    { 0 => (8, 12), 1 => (10, 16), 2 => (12, 22), 3 => (15, 28), _ => (18, 35) };
    public static float FlickYaw(int t) => T(t, 20, 28, 38, 45, 55);
    /// <summary>Seconds before an unhit target expires (0 = never).</summary>
    public static float FlickLifetime(int t) => T(t, 0, 2.0f, 1.6f, 1.3f, 1.0f);

    public static float SpiderRadius(int t) => T(t, .40f, .32f, .25f, .20f, .16f);
    public static (float yaw, float pitch) SpiderRange(int t) => t switch
    { 0 => (25, 8), 1 => (35, 11), 2 => (45, 14), 3 => (55, 16), _ => (65, 18) };
    public static float SpiderLifetime(int t) => T(t, 0, 0, 0, 1.2f, 0.9f);

    // Continuous-level versions for the adaptive drills (same tables; integer levels give exactly the tier values).
    public static float GridshotRadius(float l) => Tf(l, .45f, .36f, .28f, .22f, .17f);
    public static float GridshotSpacing(float l) => Tf(l, 1.15f, 1.15f, 1.25f, 1.35f, 1.45f);
    public static (float min, float max) FlickDistance(float l) => Tf2(l, FlickDistances);
    public static float FlickYaw(float l) => Tf(l, 20, 28, 38, 45, 55);
    public static float FlickLifetime(float l) => TfLifetime(l, 0, 2.0f, 1.6f, 1.3f, 1.0f);
    public static float SpiderRadius(float l) => Tf(l, .40f, .32f, .25f, .20f, .16f);
    public static (float yaw, float pitch) SpiderRange(float l) => Tf2(l, SpiderRanges);
    public static float SpiderLifetime(float l) => TfLifetime(l, 0, 0, 0, 1.2f, 0.9f);
    static readonly (float, float)[] FlickDistances = { (8, 12), (10, 16), (12, 22), (15, 28), (18, 35) };
    static readonly (float, float)[] SpiderRanges = { (25, 8), (35, 11), (45, 14), (55, 16), (65, 18) };

    /// <summary>A continuous level as a tier name with a +/− hint: 2.0 "Veteran", 2.4 "Veteran+", 2.8 "Elite−".</summary>
    public static string LevelName(float level)
    {
        level = Math.Clamp(level, 0f, 4f);
        int n = (int)MathF.Round(level);
        float d = level - n;
        return Tiers[n].Name + (d >= 0.2f ? "+" : d <= -0.2f ? "−" : "");
    }

    public static float TrackRadius(int t) => T(t, .45f, .38f, .30f, .22f, .16f);
    public static float TrackSpeed(int t) => T(t, 2.7f, 4.0f, 5.4f, 5.4f, 5.4f);
    public static (float min, float max) TurnInterval(int t) => t switch
    { 0 => (0.8f, 1.6f), 1 => (0.5f, 1.2f), 2 => (0.3f, 0.9f), 3 => (0.2f, 0.7f), _ => (0.12f, 0.55f) };
    public static float StopChance(int t) => T(t, 0, 0, .10f, .25f, .30f);
    /// <summary>Real players need ~0.16 s to stop/reverse — no instant direction flips.</summary>
    public const float StrafeDecel = 34f;

    public static (float min, float max) StrafeBotDistance(int t) => t switch
    { 0 => (8, 15), 1 => (10, 20), 2 => (10, 25), 3 => (12, 30), _ => (15, 35) };
    public static float StrafeBotSpeed(int t) => T(t, 2.97f, 4.0f, 5.4f, 5.4f, 5.4f);
    public static float StrafeBotLifetime(int t) => T(t, 4.0f, 3.0f, 2.2f, 1.6f, 1.2f);

    public static (float min, float max) PeekHold(int t) => t switch
    { 0 => (1.4f, 2.0f), 1 => (1.0f, 1.5f), 2 => (0.7f, 1.1f), 3 => (0.45f, 0.8f), _ => (0.3f, 0.55f) };
    public static float PeekWidth(int t) => T(t, 1.4f, 1.6f, 1.8f, 2.2f, 2.6f);
    public static float JiggleChance(int t) => T(t, 0, 0, .15f, .30f, .40f);

    public static float CounterStrafeWindow(int t) => T(t, 2.0f, 1.6f, 1.25f, 1.0f, 0.8f);

    public static float OperatorGap(int t) => T(t, 8f, 6f, 4f, 3f, 2.2f);
    public static float OperatorSpeed(int t) => T(t, 2.97f, 5.4f, 5.4f, 5.4f, 6.75f);
    public static int OperatorBots(int t) => Ti(t, 1, 1, 1, 2, 2);

    public static float SiteRoundTime(int t) => T(t, 45, 35, 30, 30, 25);
    public static (float min, float max) FlashSwingDelay(int t) => t switch
    { 0 => (1.4f, 2.0f), 1 => (0.9f, 1.4f), 2 => (0.4f, 0.8f), 3 => (0.1f, 0.4f), _ => (0f, 0.2f) };
    /// <summary>Flash Dodge attacker entry speed: Rookie walks in, Regular jogs, higher tiers sprint.</summary>
    public static float FlashEntrySpeed(int t) => T(t, 2.97f, 4.0f, 5.4f, 5.4f, 5.4f);
    /// <summary>Low tiers don't shoot a fully blinded player (the drill teaches dodging, not instant deaths).</summary>
    public static bool HoldFireWhileBlind(int t) => t <= 1;
    /// <summary>Only Elite+ attackers pre-aim your exact spot after a flash.</summary>
    public static bool FlashPreAim(int t) => t >= 3;
    /// <summary>Rookie/Regular bots only engage when your head or chest is visible (not a lone shoulder).</summary>
    public static bool ShoulderSight(int t) => t >= 2;

    // ---------------- result → rank badge ----------------

    /// <summary>Badge = highest tier whose threshold the score reaches (higher is better).</summary>
    public static int BadgeHigher(float value, float[] thresholds)
    {
        int badge = -1;
        for (int i = 0; i < thresholds.Length; i++) if (value >= thresholds[i]) badge = i;
        return badge;
    }

    /// <summary>Badge for lower-is-better values (times in ms).</summary>
    public static int BadgeLower(float value, float[] thresholds)
    {
        int badge = -1;
        for (int i = 0; i < thresholds.Length; i++) if (value <= thresholds[i]) badge = i;
        return badge;
    }

    /// <summary>Gridshot score thresholds per badge, indexed by the tier you played.</summary>
    public static readonly float[][] GridshotBadges =
    {
        new[] { 11400f, 13800, 16900, 20400, 24000 },
        new[] { 10300f, 12700, 15500, 18900, 22300 },
        new[] { 9100f, 11200, 13800, 16900, 20100 },
        new[] { 8000f, 10000, 12400, 15300, 18200 },
        new[] { 7200f, 8900, 11100, 13800, 16500 },
    };

    public static readonly float[][] SpiderBadges =
    {
        new[] { 8300f, 10200, 12300, 14800, 17300 },
        new[] { 7000f, 8600, 10500, 12700, 15200 },
        new[] { 6100f, 7500, 9300, 11300, 13500 },
        new[] { 5400f, 6700, 8400, 10200, 12300 },
        new[] { 4900f, 6100, 7600, 9400, 11300 },
    };

    /// <summary>Head Flicks median ms-to-hit thresholds (lower is better; needs ≥70% accuracy).</summary>
    public static readonly float[][] FlickBadges =
    {
        new[] { 1000f, 825, 690, 570, 480 },
        new[] { 1140f, 935, 770, 640, 540 },
        new[] { 1290f, 1050, 860, 705, 590 },
        new[] { 1410f, 1145, 930, 755, 635 },
        new[] { 1535f, 1235, 1000, 815, 675 },
    };

    public static readonly float[] StrafeBotTtk = { 1500, 1200, 950, 750, 600 };
    public static readonly float[] PeekTtk = { 1060, 875, 720, 600, 505 };
    public static readonly float[] CounterStrafeTtk = { 1400, 1150, 950, 800, 680 };
    public static readonly float[] ReactionMs = { 9999, 265, 230, 200, 175 };

    // ---- v1.4 aim drills (tier parameters in Modes/AimPlusModes.cs → AimPlusTiers). Rows = tier played, columns =
    // badge Rookie … Pro: the median a player of that rank reaches at that tier, from a per-rank reaction + Fitts model
    // (flick onset 265 → 155 ms, primary endpoint error 24 → 5%, coach_spec.md) cross-checked against Aim Lab /
    // KovaaK's numbers and Peek Practice's PeekTtk. 999 = not reachable at that tier.

    /// <summary>Microshot median ms-to-hit (lower is better; needs ≥ 70% accuracy).</summary>
    public static readonly float[][] MicroshotBadges =
    {
        new[] { 665f, 540, 460, 400, 355 },
        new[] { 730f, 565, 480, 415, 365 },
        new[] { 800f, 615, 510, 435, 385 },
        new[] { 870f, 680, 530, 450, 395 },
        new[] { 950f, 750, 590, 485, 420 },
    };
    /// <summary>Target Switch kills per minute (higher is better; needs ≥ 30% accuracy).</summary>
    public static readonly float[][] SwitchBadges =
    {
        new[] { 37.5f, 54, 75, 105, 145 },
        new[] { 33.5f, 48, 67, 94, 130 },
        new[] { 30f, 43, 60, 84, 116 },
        new[] { 26.5f, 38, 54, 75, 103 },
        new[] { 24f, 34, 48, 67, 92 },
    };
    /// <summary>Pop-up Reflex % of pop-ups hit (higher is better; needs ≥ 50% accuracy). One badge above the tier played at most.</summary>
    public static readonly float[][] PopupBadges =
    {
        new[] { 60f, 90, 999, 999, 999 },
        new[] { 22f, 68, 90, 999, 999 },
        new[] { 8f, 32, 66, 90, 999 },
        new[] { 3f, 14, 37, 68, 90 },
        new[] { 2f, 8, 23, 47, 70 },
    };
    /// <summary>Long-range Taps median ms from first sight to the kill (lower is better; needs ≥ 60% kills and tap discipline).</summary>
    public static readonly float[][] LongTapsBadges =
    {
        new[] { 1910f, 1440, 1110, 850, 700 },
        new[] { 2150f, 1610, 1260, 980, 790 },
        new[] { 2380f, 1780, 1390, 1090, 890 },
        new[] { 2680f, 1970, 1530, 1220, 980 },
        new[] { 2810f, 2130, 1630, 1290, 1060 },
    };

    /// <summary>Kill-time drills: clear the tier with ≥80% kills under its TTK; beat next tier's TTK with ≥90% to go up one.</summary>
    public static int BadgeTtk(int played, float killRate, float medianTtk, float[] ttk)
    {
        if (killRate >= 0.9f && played + 1 < ttk.Length && medianTtk <= ttk[played + 1]) return played + 1;
        if (killRate < 0.8f) return -1;
        for (int i = played; i >= 0; i--) if (medianTtk <= ttk[i]) return i;
        return -1;
    }

    /// <summary>Duel modes: win rate vs the played tier's bots.</summary>
    public static int BadgeWinRate(int played, float winRate, float matchThreshold = 0.45f)
    {
        if (winRate >= 0.70f) return Math.Min(4, played + 1);
        if (winRate >= matchThreshold) return played;
        if (winRate >= 0.22f) return played - 1;
        return -1;
    }

    public static string BadgeName(int badge) => badge < 0 ? "Below Rookie" : $"{Tiers[badge].Name} ({Tiers[badge].Ranks})";
}
