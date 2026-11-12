using System.Text.Json;
using System.Text.Json.Serialization;
using ValTrainer.Core;

namespace ValTrainer.Progression;

// ---------------------------------------------------------------------------------------------------------------------
// API records (what queries and feeds return). Owned by the Progress core agent; the UI agents only read them.
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
/// Lock state of one item for the UI (<see cref="Progress.Status"/>).
/// </summary>
/// <param name="ItemId">The item (<see cref="ItemIds"/>).</param>
/// <param name="Known">False for ids no registered line has (they are locked, text "Not available yet").</param>
/// <param name="Unlocked">Whether it can be worn now.</param>
/// <param name="Req">Its requirement (for a structural block, the item's own requirement still).</param>
/// <param name="Text">The requirement as a sentence, translated: "Reach level 15 + Earn 3 stars".</param>
/// <param name="Progress01">0..1 towards it (1 when unlocked).</param>
/// <param name="ProgressText">Short progress: "Level 9 / 12", "3 / 25" ("" when unlocked).</param>
/// <param name="Blocker">A structural prerequisite still missing, translated ("Unlock level 2 first"), or null.</param>
public sealed record UnlockStatus(string ItemId, bool Known, bool Unlocked, Requirement Req, string Text, float Progress01, string ProgressText, string? Blocker)
{
    /// <summary>One line for a locked tile or button: the blocker if any, else the requirement and its progress.</summary>
    public string Summary => Unlocked ? "" : Blocker ?? (ProgressText.Length > 0 ? $"{Text} · {ProgressText}" : Text);
}

/// <summary>A newly unlocked item. <see cref="Source"/>: run | lockin | challenge | coach | retro | dev.</summary>
public sealed record UnlockInfo(string ItemId, string Source, DateTime When);

/// <summary>One chip of the results screen's XP strip ("RUN" 99, "FIRST TODAY" 150). Label: English, translated where drawn.</summary>
public sealed record XpPart(string Label, int Xp);

/// <summary>A challenge's progress after a run ("Head Flicks 2/3"). Title: translated when built.</summary>
public sealed record ChallengeTick(string Title, int Value, int Target, bool Done);

/// <summary>What a finished run earned: the results screen's XP strip (Progress UI) draws it.</summary>
public sealed record RunXp(int Total, IReadOnlyList<XpPart> Parts, int StarsGained, IReadOnlyList<string> StarReasons,
                           int LevelBefore, int LevelAfter, float BarBefore, float BarAfter,
                           IReadOnlyList<ChallengeTick> Challenges, IReadOnlyList<string> NewUnlocks);

/// <summary>
/// Everything <see cref="Progress.OnRunFinished"/> needs about a finished run, collected by the Progress core agent in
/// <c>GameSession.Finish</c> (after the record is made and stored or not).
/// </summary>
/// <param name="Record">The run's record (also when it was not stored).</param>
/// <param name="Stored">It counts as a stats.json run, i.e. a real run (<see cref="XpRules.RealRun"/>; false: sens-shifted
/// <c>KeepInStats</c> false, adaptive Lock-In step, a timed run shorter than the drill's own length). Only stored runs give
/// PBs, stars and score-target challenges. Dev runs, which never write stats.json, still pass true when they are real:
/// their PBs and stars count in memory.</param>
/// <param name="ActiveSecs">Seconds with real input (telemetry frames with mouse movement, firing or key movement).</param>
/// <param name="SkinLine">The line on the run's gun (<see cref="Game.Weapon.GunSkins.IdFor"/>), null / "factory" = none.</param>
/// <param name="LockInStep">Played as a Lock-In step (no score-target challenges, toasts held).</param>
/// <param name="Adaptive">An adaptive Lock-In step (time XP only, no PB).</param>
/// <param name="Finder">Sens Finder / Crosshair Finder (a flat XP once a day, never challenges).</param>
public sealed record RunFacts(RunRecord Record, bool Stored, float ActiveSecs, string? SkinLine, bool LockInStep, bool Adaptive, bool Finder);

// ---------------------------------------------------------------------------------------------------------------------
// progress.json (progression-design §11). Versioned; every object round-trips unknown fields through Extra so a 1.19
// save never drops what a newer build wrote. Field names are the JSON names: never rename. Dates are local ISO strings
// for day / week keys ("2026-11-07", "2026-W45") and DateTime for moments.
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>Base for every progress.json object: keeps unknown fields.</summary>
public abstract class ProgressJson
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The root of progress.json. Level is derived from <see cref="Xp"/> (<see cref="XpCurve"/>), never stored.</summary>
public sealed class ProgressFile : ProgressJson
{
    /// <summary>Format version written by this build (<see cref="ProgressStore.CurrentVersion"/>).</summary>
    public int Version = ProgressStore.CurrentVersion;
    /// <summary>The oldest format version that can safely write this file; a file with MinReader &gt; our version is read-only.</summary>
    public int MinReader = 1;
    /// <summary>Random per install: seeds daily / weekly challenges (a restart never rerolls).</summary>
    public string Salt = Guid.NewGuid().ToString("N");
    public string? CreatedWith;
    public int Xp, Stars;
    /// <summary>Newest stats run already credited (retro and catch-up start after it).</summary>
    public DateTime? CreditedThrough;
    /// <summary>Newest Lock-In already credited.</summary>
    public DateTime? LockInsThrough;
    public RetroInfo? Retro;
    /// <summary>Rested XP pool left.</summary>
    public int Rested;
    public string? LastActiveDay;
    /// <summary>Per local day (04:00 boundary) bookkeeping, last 14 days.</summary>
    public Dictionary<string, DayInfo>? Days;
    public ChallengeSet? Daily, Weekly;
    public StreakInfo? Streak;
    public CoachTrackInfo? Coach;
    public CounterInfo Counters = new();
    /// <summary>"With it" counters per skin line.</summary>
    public Dictionary<string, WithLine>? With;
    /// <summary>Permanent unlocks: item id → when. Never removed by any version (never revoke).</summary>
    public Dictionary<string, DateTime> Unlocked = new();
    /// <summary>Unlocked items the player hasn't looked at yet (toasts, the SKINS dot, the reveal).</summary>
    public List<string> Unseen = new();
    /// <summary>The Sens Finder was completed once (also read from stats.json: <c>finder:sens</c>).</summary>
    public bool SensFinder;
}

/// <summary>The one-time "your training counts" credit (progression-design §7). The Progress UI shows the reveal while
/// <see cref="Shown"/> is false (<see cref="Progress.Reveal"/>, <see cref="Progress.MarkRevealShown"/>).</summary>
public sealed class RetroInfo : ProgressJson
{
    public bool Done, Shown;
    public DateTime? When;
    /// <summary>Stats runs replayed, total XP and stars after the credit (welcome and the coach placement included).</summary>
    public int Runs, Xp, Stars;
    /// <summary>Counters for the reveal: active training seconds, personal bests, badge rank-ups, completed Lock-Ins.</summary>
    public long ActiveSecs;
    public int Pbs, BadgeUps, LockIns;
    /// <summary>Items the credit unlocked, table order.</summary>
    public List<string>? Unlocks;
}

public sealed class DayInfo : ProgressJson
{
    /// <summary>Run XP before the taper; PB XP awards (max 5); PB stars (max 3).</summary>
    public int RunXp, PbXp, PbStars;
    /// <summary>Training seconds credited today (finders excluded).</summary>
    public float Secs;
    /// <summary>Runs per drill key today (variety factor).</summary>
    public Dictionary<string, int>? Drills;
    public List<string>? PbDrills, Finders;
}

public sealed class ChallengeSet : ProgressJson
{
    /// <summary>Day key ("2026-11-07") or ISO week key ("2026-W45").</summary>
    public string? Key;
    public int Rerolls;
    public List<ChallengeItem> Items = new();
}

public sealed class ChallengeItem : ProgressJson
{
    /// <summary>Generator kind ("focus_runs", "improve", "variety" …).</summary>
    public string Kind = "";
    public string? Drill;
    public float Baseline, Target, Value;
    public bool Done;
    public int Xp, Stars;
    /// <summary>Why it was picked (a coach diagnosis id, "starter", a skill name …).</summary>
    public string? Why;
    /// <summary>The reason as the coach worded it when the challenge was made (a diagnosis title), or null.</summary>
    public string? WhyText;
    /// <summary>Tier the target was measured at (-1 = any).</summary>
    public int Tier = -1;
}

/// <summary>Weekly training streak (a week counts with 2+ training days). Tokens: 1 per 4 counted weeks, max 2, used
/// automatically for a missed week. Never shows "streak lost": with no token the counter restarts quietly.</summary>
public sealed class StreakInfo : ProgressJson
{
    public int Weeks, Best, Tokens, Counted;
    public string? LastWeek;
    /// <summary>The last counted week used rest tokens ("Rest week used: your streak is safe").</summary>
    public bool RestUsed;
    /// <summary>Highest milestone already paid (weeks).</summary>
    public int Milestone;
}

public sealed class CoachTrackInfo : ProgressJson
{
    /// <summary>Bench calibration version the samples belong to.</summary>
    public int Calib;
    public string? PlacedOn;
    /// <summary>Track ("overall", skill names) → rank index at placement / high-water mark.</summary>
    public Dictionary<string, int>? Placed, High;
    /// <summary>Track → the day it was placed (a confirmed rank only uses days after it).</summary>
    public Dictionary<string, string>? PlacedDays;
    public int OverallUps, SkillUps;
    public List<CoachDay>? Days;
    /// <summary>An overall rank was placed once (any calibration): <c>coach:placed</c> stays met after a recalibration.</summary>
    public bool Ever;
    /// <summary>The coach was recalibrated after a placement: the rank is being re-placed (nothing earned is lost).</summary>
    public bool Recalibrated;
    /// <summary>Retro credit: the first real profile becomes the placement (one sample, +300 XP).</summary>
    public bool RetroPending;
    /// <summary>The newest profile's focus: the drills of its top problem, that problem's id and title, and the weakest
    /// rated skill (daily / weekly challenges).</summary>
    public List<string>? FocusDrills;
    public string? FocusWhy, FocusWhyText, Weakest;
}

public sealed class CoachDay : ProgressJson
{
    public string D = "";
    /// <summary>Overall [tier, confidence].</summary>
    public float[]? O;
    /// <summary>Skill → [tier, confidence].</summary>
    public Dictionary<string, float[]>? S;
}

public sealed class CounterInfo : ProgressJson
{
    public int Runs, Pbs, PbStars, BadgeUps, LockIns, Dailies, Weeklies, WeeklyImprove;
    public long ActiveSecs;
}
