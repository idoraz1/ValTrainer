using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game.Skins;

/// <summary>
/// The player's skin kills in one run: plays the kill effect of every accepted kill and decides the finisher. One per
/// session run, owned by <see cref="GameSession"/>, which accepts a kill only for the player's own bullet with a skinnable
/// gun in hand (never Demo, abilities, bot-on-bot or utility kills; never where the drill disallows skin effects).
/// Read-only: nothing is written anywhere (no telemetry, replay or stats event); scores never change.
/// <para><b>Finisher rule = VALORANT's</b> (skins3-contracts §3): a finisher plays ONLY on the player's kill that eliminates
/// the last living enemy AND wins the round, i.e. the drill emits <c>round_won</c> within <see cref="FinisherWindow"/> s of
/// that kill (or the mode flags the kill <see cref="KillInfo.RoundWinning"/>). Nothing else: no lite finishers, no
/// streaks in one life, no Deathmatch / match-won finisher, none in rep, target or measuring drills, none for a round won
/// while an enemy still lives (a defuse, a timer). Rounds exist wherever a drill emits <c>round_won</c>: a drill classed
/// <see cref="KillDrill.Rep"/> that emits one is treated as a round drill from then on. Dev <c>--skinfx-finisher</c> plays
/// one on every tracked kill.</para>
/// </summary>
public sealed class KillTracker
{
    /// <summary>How long after a kill a round win still counts as "this kill won it" (s).</summary>
    public const float FinisherWindow = 0.3f;
    /// <summary>The gun's kill counter shows "GG" this long after a finisher (s).</summary>
    public const float GgTime = 2f;

    /// <summary>Dev "--skinfx-finisher": every tracked kill plays the finisher (screenshots).</summary>
    public static readonly bool DevFinisher = CmdLine.Dev && CmdLine.Has("--skinfx-finisher");

    readonly KillFxWorld? fx;
    readonly Func<Viewmodel?> viewmodel;
    readonly Func<Color> enemy;
    readonly Func<BotCharacter?, bool> enemyAlive;
    KillInfo? pending;

    /// <summary>The equipped gun's skin (null = factory: nothing plays).</summary>
    public SkinState? State { get; set; }
    /// <summary>How this drill ends kills (the mode's <c>KillDrill</c>, upgraded to Round once <c>round_won</c> is seen).</summary>
    public KillDrill Drill { get; private set; }
    /// <summary>Accepted kills in this life (reset on death and on respawn). Information only.</summary>
    public int LifeKills { get; private set; }
    /// <summary>Accepted kills this run.</summary>
    public int Kills { get; private set; }
    /// <summary>Finishers this run: kills the rule picked while the worn level has a finisher (counted even when Settings
    /// hide the effect, which only changes what is drawn).</summary>
    public int Finishers { get; private set; }
    /// <summary>Session clock until which the kill counter shows "GG".</summary>
    public float GgUntil { get; private set; } = -99f;
    /// <summary>Session clock of the last accepted kill.</summary>
    public float LastKillAt { get; private set; } = -99f;
    /// <summary>Tests: decide only (no effect, sound or viewmodel call).</summary>
    internal bool Dry { get; init; }

    /// <param name="fx">The session's pooled world effects (null = decide only).</param>
    /// <param name="drill">The mode's <c>TrainingMode.KillDrill</c>.</param>
    /// <param name="viewmodel">The gun in hand (gun-side kill pulse).</param>
    /// <param name="enemy">The enemy highlight colour (effects never use it).</param>
    /// <param name="enemyAlive">True while any enemy other than the given (just killed) bot is alive.</param>
    public KillTracker(KillFxWorld? fx, KillDrill drill, Func<Viewmodel?> viewmodel, Func<Color> enemy, Func<BotCharacter?, bool> enemyAlive)
    {
        this.fx = fx;
        Drill = drill;
        this.viewmodel = viewmodel;
        this.enemy = enemy;
        this.enemyAlive = enemyAlive;
    }

    /// <summary>Raised on every accepted kill (after the effects): the session's gun-side Bullet FX pulse.</summary>
    public event Action<KillInfo>? Killed;

    /// <summary>Drills where a finisher can ever play (anything that may emit <c>round_won</c>).</summary>
    bool RoundsPossible => Drill is KillDrill.Round or KillDrill.Rep or KillDrill.Unknown;

    /// <summary>An accepted player kill: fills <see cref="KillInfo.Drill"/> / <see cref="KillInfo.LifeKills"/>, plays the
    /// gun-side pulse, the kill-effect sound and the world effect (per Settings → Kill effects); a kill of the last living
    /// enemy waits up to <see cref="FinisherWindow"/> s for <c>round_won</c> to become the finisher kill.</summary>
    public KillInfo OnKill(KillInfo kill)
    {
        kill = kill with { Drill = Drill, LifeKills = ++LifeKills };
        Kills++;
        LastKillAt = kill.Time;
        bool last = !enemyAlive(kill.Bot);
        if (State is { } st && !Dry)
        {
            var mode = KillFxWorld.Mode;
            if (CmdLine.Dev)
                Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"[skinfx] t={kill.Time:0.00} kill {Kills} ({Drill}, {(last ? "last enemy" : "enemies left")}, {kill.Dist:0.0} m) {st.LevelItem}: effect {(mode == SkinKillFxMode.Full ? st.KillFx ?? "-" : mode.ToString())}"));
            if (mode != SkinKillFxMode.Off)
            {
                viewmodel()?.OnPlayerKill();
                if (st.Has(SkinFeature.KillFx)) SkinAudio.PlayKillEffect(st);
            }
            if (mode == SkinKillFxMode.Full && st.Has(SkinFeature.KillFx)) fx?.Kill(kill, st, enemy());
            Killed?.Invoke(kill);
        }

        pending = null;
        if (DevFinisher) { Finish(kill, FinisherReason.Dev); return kill; }
        if (!RoundsPossible || !last) return kill;              // Deathmatch / target / measuring, or enemies still alive
        if (kill.RoundWinning) { Finish(kill, FinisherReason.RoundWon); return kill; }
        pending = kill;                                         // round_won within the window makes this the finisher kill
        return kill;
    }

    /// <summary>A round / the match ended (GameSession's read-only watch of <c>round_won</c> / <c>round_lost</c> /
    /// <c>Mode.Done</c>, or a mode's own call). Only a round win within the window of a last-enemy kill plays a finisher.</summary>
    public void OnRoundEnd(RoundEndInfo end)
    {
        if (end.Kind == RoundEndKind.RoundWon && Drill is KillDrill.Rep or KillDrill.Unknown) Drill = KillDrill.Round;
        if (end.Kind == RoundEndKind.RoundWon && pending is { } k && end.Time - k.Time <= FinisherWindow + 1e-3f && RoundsPossible)
            Finish(k, FinisherReason.RoundWon);
        pending = null; // any round / match end closes the window
    }

    /// <summary>Every running frame: the finisher window closes.</summary>
    public void Tick(float now)
    {
        if (pending is { } k && now - k.Time > FinisherWindow) pending = null;
    }

    /// <summary>The player died or respawned: a new life.</summary>
    public void ResetLife()
    {
        LifeKills = 0;
        pending = null;
    }

    void Finish(KillInfo kill, FinisherReason reason)
    {
        if (State is not { } st || !st.Has(SkinFeature.Finisher)) return;
        Finishers++;
        GgUntil = kill.Time + GgTime;
        if (CmdLine.Dev && !Dry)
            Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[skinfx] t={kill.Time:0.00} finisher ({(reason == FinisherReason.Dev ? "dev" : "last enemy, round won")}) {st.Finisher} at kill {Kills}, drill {Drill}"));
        if (Dry || KillFxWorld.Mode != SkinKillFxMode.Full || !KillFxWorld.FinishersOn) return;
        fx?.Finisher(new FinisherCue(kill, reason), st, enemy());
        SkinAudio.PlayFinisher(st, false); // skins 3: no lite finishers (the audio builder's lite parameter is always false)
    }
}
