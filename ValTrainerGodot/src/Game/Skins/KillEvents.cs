using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Bots;

namespace ValTrainer.Game.Skins;

/// <summary>How a drill ends kills, for the finisher rule (skins3-contracts §3: VALORANT's). <c>TrainingMode.KillDrill</c>
/// (virtual, overridden in BotMode) classifies each drill; GameSession fills <see cref="KillInfo.Drill"/>.</summary>
public enum KillDrill
{
    /// <summary>Not classified yet (treated like <see cref="Rep"/>).</summary>
    Unknown,
    /// <summary>Emits <c>round_won</c>: the finisher plays on the kill of the last living enemy that wins the round.</summary>
    Round,
    /// <summary>Deathmatch: never a finisher (no rounds).</summary>
    Deathmatch,
    /// <summary>One bot at a time, no rounds: no finisher (promoted to <see cref="Round"/> if it ever emits <c>round_won</c>).</summary>
    Rep,
    /// <summary>Target spheres: no bot dies, nothing plays.</summary>
    Target,
    /// <summary>Sens Finder / Crosshair Finder: skin effects never play (trials must look and sound identical).</summary>
    Measuring,
}

/// <summary>
/// One kill by the player's own bullet (skins2-design §4.4). Built by <see cref="For"/> at the kill (BotMode.ShootBots,
/// DefenseModes' direct kill) and passed to <see cref="IGame.OnPlayerKill"/>. Read-only data: nothing is recorded
/// (no telemetry, replay or stats event).
/// </summary>
/// <param name="Bot">The bot that died. May be freed soon after: copy what you need, check <c>IsInstanceValid</c>.</param>
/// <param name="Feet">Death point on the ground (world).</param>
/// <param name="Chest">Chest point (head − 0.35 m up): where bursts start.</param>
/// <param name="Zone">Hit zone of the killing bullet.</param>
/// <param name="Dist">Distance of the killing bullet (metres; Calibrated's callout).</param>
/// <param name="Gun">The gun in hand.</param>
/// <param name="Time">Session clock (<see cref="IGame.Now"/>) at the kill.</param>
public readonly record struct KillInfo(BotCharacter Bot, Vector3 Feet, Vector3 Chest, HitZone Zone, float Dist, WeaponKind Gun, float Time)
{
    public bool Headshot => Zone == HitZone.Head;
    /// <summary>Filled by GameSession from the mode (Kills/FX agent).</summary>
    public KillDrill Drill { get; init; }
    /// <summary>The mode knows this kill wins the round (it killed the last living enemy). False = unknown: the tracker
    /// watches for <c>round_won</c> within <see cref="KillTracker.FinisherWindow"/> s (<see cref="RoundEndInfo"/>).</summary>
    public bool RoundWinning { get; init; }
    /// <summary>Kills in this life including this one (filled by the KillTracker; information only, no rule uses it).</summary>
    public int LifeKills { get; init; }

    /// <summary>The standard way to build one at a kill site.</summary>
    public static KillInfo For(IGame g, BotCharacter bot, HitZone zone, float dist, bool roundWinning = false)
    {
        var feet = bot.Feet;
        var chest = bot.Head - new Vector3(0, 0.35f, 0);
        return new KillInfo(bot, feet, chest, zone, dist, g.Weapon?.Kind ?? WeaponKind.None, g.Now) { RoundWinning = roundWinning };
    }
}

/// <summary>What ended a round (or the run's match). Only <see cref="RoundWon"/> can lead to a finisher.</summary>
public enum RoundEndKind { RoundWon, RoundLost, MatchWon, RunEnded }

/// <summary>A round / match ended: GameSession raises it from its read-only watch of <c>Event("round_won")</c> /
/// <c>Event("round_lost")</c> and of <c>Mode.Done</c> turning true; modes may also call <see cref="IGame.OnRoundEnd"/>.</summary>
public readonly record struct RoundEndInfo(RoundEndKind Kind, float Time)
{
    public bool PlayerWon => Kind is RoundEndKind.RoundWon or RoundEndKind.MatchWon;
}

/// <summary>Why a finisher plays: the round-winning kill of the last enemy (VALORANT's only case), or a dev / preview demand.</summary>
public enum FinisherReason { RoundWon, Dev }

/// <summary>A finisher to play at a kill (KillTracker → <see cref="KillFxWorld.Finisher"/>, and the picker's showcase). There
/// are no lite finishers (skins 3): every cue plays the full one.</summary>
public readonly record struct FinisherCue(KillInfo Kill, FinisherReason Reason)
{
    /// <summary>Kept for callers written against skins 2: <paramref name="lite"/> is ignored (no lite finishers).</summary>
    public FinisherCue(KillInfo kill, bool lite, FinisherReason reason) : this(kill, reason) { }
}
