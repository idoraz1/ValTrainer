using ValTrainer.Analysis;
using ValTrainer.Game;

namespace ValTrainer.UI;

/// <summary>
/// Dev-only hand-made coach data (<c>--dev --fakecoach [full|partial|empty]</c>) so the coach UI can be laid out
/// while the analysis engine is still being written. Never used in normal runs.
/// </summary>
public static class CoachFake
{
    static readonly Diagnosis XhairLow = new(
        "XHAIR_LOW", Skill.CrosshairPlacement, Severity.Major,
        "Crosshair too low",
        "Enemies appeared 2.9° above your crosshair on average, and below the head on 74% of peeks.",
        "You hold angles at body height or drift toward the floor while moving, so every fight starts with a vertical flick before you can shoot.",
        new[]
        {
            "Pick a head-height line on the wall (a ledge, a seam, a sign) and keep the crosshair on it while you move.",
            "Before you clear an angle, park the crosshair just off the cover edge where the head will appear.",
            "If you have to flick UP to get the kill, your placement was too low — reset it after every fight.",
        },
        new[] { "peek", "siteclear" }, 38);

    static readonly Diagnosis OverFlick = new(
        "OVERFLICK", Skill.Flicking, Severity.Major,
        "Over-flicking: your flicks travel too far",
        "62% of your off-target flicks ended past the target, by 2.1° (11% of the distance).",
        "The flick is planned too long or your arm is tense, so it can't brake. Undoing an overshoot costs more time than finishing a short flick.",
        new[]
        {
            "Aim to land about 90% of the way there, then finish with one small micro-adjust.",
            "Brake early: the second half of the flick should be slower than the first.",
            "Relax your grip and forearm — a tense arm overshoots.",
        },
        new[] { "flick", "spider" }, 186);

    static readonly Diagnosis NoMicro = new(
        "NO_MICRO", Skill.Precision, Severity.Major,
        "Flicks have no micro-adjustment",
        "46% of your misses were fired straight off the flick, 0.9° from the head, with no correction.",
        "Your click is chained to the end of the flick — you shoot when your hand stops, not when the crosshair is on the head.",
        new[]
        {
            "Flick, confirm, click: let your eyes confirm the crosshair is on the head before you fire.",
            "Play Head Flicks at Rookie (untimed) until you hit 85% accuracy, then speed up.",
            "Accept being 50 ms slower for now — accuracy first, the speed comes back.",
        },
        new[] { "flick", "gridshot" }, 64);

    static readonly Diagnosis Clumsy = new(
        "CLUMSY_MOVE", Skill.Movement, Severity.Minor,
        "Clumsy stops",
        "Only 28% of your stops were counter-strafed; average stop 232 ms (best ≈ 160 ms).",
        "Releasing the key lets your agent slide to a stop, so the first bullet often leaves while you're still inaccurate.",
        new[]
        {
            "Tap the opposite key for about 80 ms, then release both.",
            "Only shoot once the speed meter turns green.",
            "Drill the rhythm slowly: A … D-tap … click.",
        },
        new[] { "counterstrafe", "peekduel" }, 45);

    static readonly Diagnosis TrackBehind = new(
        "TRACK_BEHIND", Skill.Tracking, Severity.Minor,
        "Chasing the target",
        "You trailed the head by 0.6° (≈170 ms) while tracking strafes.",
        "You react to where the target was instead of matching how fast it moves.",
        new[]
        {
            "Match the target's speed first, then centre on it.",
            "Ride the leading edge of the head when it strafes.",
        },
        new[] { "tracking", "strafebots" }, 96);

    static readonly Diagnosis SprayVert = new(
        "SPRAY_VERT", Skill.SprayControl, Severity.Tip,
        "Start pulling down a little earlier",
        "Bullets 4–15 landed 0.7° high; you started pulling 310 ms into the spray.",
        "The Vandal kicks hardest on bullets 2–6. Starting the pull late means you spend the spray catching up.",
        new[] { "Start pulling by bullet 2–3, then keep it steady.", "Watch the first five bullets, not the target." },
        new[] { "spray_vandal" }, 11);

    public static SkillProfile Profile(string variant, float sens, int dpi)
    {
        var p = new SkillProfile { Updated = DateTime.Now };
        if (variant == "empty") return p;

        if (variant == "partial")
        {
            p.RunsAnalyzed = 4;
            p.Skills = new()
            {
                new(Skill.Flicking, 1.62f, 0.62f, 74, "Fast first motion, but 58% of flicks overshoot"),
                new(Skill.Precision, 0.74f, 0.58f, 74, "46% of misses fired straight off the flick"),
                new(Skill.Tracking, 1.1f, 0.32f, 41, "36% on target (41 s tracked — needs more)"),
            };
            p.Problems = new() { OverFlick, NoMicro };
            p.Strengths = new() { "You start moving fast: 255 ms after the target appears" };
            return p;
        }

        p.OverallTier = 1.42f;
        p.OverallConfidence = 0.68f;
        p.RunsAnalyzed = 23;
        p.Updated = DateTime.Now.AddMinutes(-3);
        p.Skills = new()
        {
            new(Skill.Flicking, 1.65f, 0.82f, 186, "Fast first motion (612 ms to hit at Regular) but 58% overshoot"),
            new(Skill.Precision, 0.72f, 0.80f, 186, "46% of misses fired straight off the flick · 2.3 corrections"),
            new(Skill.Tracking, 1.18f, 0.66f, 96, "39% on target · trailing by 0.6° (170 ms)"),
            new(Skill.Reaction, 2.30f, 0.74f, 30, "Median 214 ms — faster than most Diamonds"),
            new(Skill.CrosshairPlacement, 0.35f, 0.71f, 38, "Enemies appear 2.9° above your crosshair"),
            new(Skill.SprayControl, 1.55f, 0.55f, 11, "Bullets 4–15 land 0.7° high · clean side-to-side"),
            new(Skill.Movement, 0.60f, 0.69f, 45, "28% of stops counter-strafed · 232 ms average stop"),
            new(Skill.Utility, -1f, 0f, 0, ""),
        };
        p.Problems = new() { XhairLow, OverFlick, NoMicro, Clumsy, TrackBehind, SprayVert };
        p.Strengths = new()
        {
            "Reaction time 214 ms — Diamond level.",
            "You start moving fast: 255 ms after a target appears (Platinum level).",
            "Straight flick paths — 94% of the motion goes toward the target.",
            "Horizontal spray control is clean (0.4° side-to-side scatter).",
        };
        float cur = 0.21f, rec = 0.19f;
        p.Sens = new SensAdvice(cur, dpi, rec, PlayerView.Cm360(cur, dpi), PlayerView.Cm360(rec, dpi), "lower",
            "62% of your flicks overshoot and you make 2.3 corrections per target — both point to a sens that's a bit high for your arm. About 10% lower should make flicks easier to stop.",
            0.62f);
        return p;
    }

    public static RunReview Review(string variant, string modeKey)
    {
        var r = new RunReview();
        if (variant == "empty" || modeKey == "sensfinder") return r; // like the real coach: the sens finder shows its own result
        switch (modeKey)
        {
            case "counterstrafe":
            case "peekduel":
                r.Summary = "Your aim was fine once you stopped — the problem is the stop itself. Most stops were key releases, so the first bullet often left while you were still sliding.";
                r.Issues = new() { Clumsy };
                r.Strengths = new() { "One-taps after a clean stop hit the head 64% of the time." };
                break;
            case "peek":
            case "siteclear":
                r.Summary = "Bots kept appearing above your crosshair, so every fight started with a vertical flick. Hold a head-height line and most of these become instant kills.";
                r.Issues = new() { XhairLow, Clumsy };
                r.Strengths = new() { "Fast reactions: you started aiming 231 ms after a bot appeared." };
                break;
            default:
                r.Summary = "Fast hands, but your flicks keep sailing past the head: 62% of off-target flicks overshot by about 2°, and almost half your misses were fired straight off the flick. Land short, confirm, then click — your 71% accuracy should climb quickly.";
                r.Issues = new() { OverFlick, NoMicro, SprayVert };
                r.Strengths = new()
                {
                    "Quick start: you begin moving 255 ms after a target appears.",
                    "Straight flick paths (94% efficient).",
                    "Consistent pace — no fatigue drop in the last 20 seconds.",
                };
                break;
        }
        r.Metrics = new() { ["flick_overshoot_share"] = 0.62f, ["snap_miss_share"] = 0.46f, ["accuracy"] = 0.71f };
        return r;
    }
}
