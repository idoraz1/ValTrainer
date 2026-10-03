using ValTrainer.Core;

namespace ValTrainer.Modes;

/// <summary>
/// Flash &amp; Peek ("flashpeek:&lt;agent&gt;"): attack a real site with your agent's own flash or nearsight — throw it, swing
/// while the defenders are blind, kill them before they recover. Scored on defenders flashed, kills on blinded enemies,
/// pop → kill time, self-flashes and dry peeks (swinging into a defender no utility touched).
/// </summary>
public sealed class FlashPeekMode : InitiatorDrill
{
    /// <summary>Arguments this drill supports (lower-case agent keys); the Agents screen lists one entry per item.</summary>
    public static readonly string[] Agents = { "phoenix", "skye", "yoru", "kayo", "breach", "gekko", "reyna", "omen" };
    public readonly string Agent;

    public FlashPeekMode(string agent = "phoenix") : base(Pick(agent)) => Agent = Ability.AgentKey;

    static InitiatorAbility Pick(string agent)
    {
        var a = string.IsNullOrWhiteSpace(agent) ? "phoenix" : agent.Trim().ToLowerInvariant();
        return InitiatorAbility.Get(Agents.Contains(a) ? a : "phoenix");
    }

    public override string Key => "flashpeek:" + Agent;
    public override string Name => "Flash & Peek";
    public override string Description => "Throw your own flash, then peek and kill the defenders while they are blind.";
    protected override bool CountsDryPeeks => true;

    bool ViewFlash => Ability.Kind is not (UtilKind.Dizzy or UtilKind.Leer or UtilKind.Paranoia);

    protected override IEnumerable<string> StatusLines()
    {
        if (casts == 0) yield break;
        yield return Ability.IsNearsight
            ? $"Nearsighted: {nearsighted} · utility kills: {utilKills}"
            : $"Flashed: {blindFull + blindPartial} · flash kills: {utilKills}";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        foreach (var l in CommonResultLines()) yield return l;
        if (Ability.IsNearsight) yield return ("Defenders nearsighted", $"{nearsighted}  ({casts} cast{(casts == 1 ? "" : "s")})");
        else yield return ("Defenders flashed", $"{blindFull} full + {blindPartial} partial  ({casts} thrown)");
        yield return (Ability.IsNearsight ? "Kills on nearsighted enemies" : "Kills on blinded enemies", $"{utilKills} of {Kills}");
        if (popToKillMs.Count > 0) yield return ("Avg pop → kill", $"{popToKillMs.Average():0} ms");
        if (ViewFlash) yield return ("Self-flashes", selfFlashes.ToString());
        yield return ("Dry peeks (no utility on them)", dryPeeks.ToString());
        if (destroyedUtil > 0) yield return ("Shot down by defenders", destroyedUtil.ToString());
        foreach (var l in DuelResultLines()) yield return l;
    }

    /// <summary>Win rate vs the tier's defenders, one tier lower if under a third of your kills were on blinded enemies.</summary>
    public override int Badge() => WinBadge(Kills == 0 ? 0f : (float)utilKills / Kills, 1f / 3f);
}

/// <summary>
/// Recon &amp; Clear ("recon:&lt;agent&gt;"): send Sova's Recon Bolt or Fade's Haunt into the site, read where the defenders are
/// (outlines through walls + pings), then clear it. Scored on enemies revealed, pre-aim error at revealed enemies, kills
/// and clear time.
/// </summary>
public sealed class ReconMode : InitiatorDrill
{
    /// <summary>Arguments this drill supports (lower-case agent keys); the Agents screen lists one entry per item.</summary>
    public static readonly string[] Agents = { "sova", "fade" };
    public readonly string Agent;

    public ReconMode(string agent = "sova") : base(Pick(agent)) => Agent = Ability.AgentKey;

    static InitiatorAbility Pick(string agent)
    {
        var a = string.IsNullOrWhiteSpace(agent) ? "sova" : agent.Trim().ToLowerInvariant();
        return InitiatorAbility.Get(Agents.Contains(a) ? a : "sova");
    }

    public override string Key => "recon:" + Agent;
    public override string Name => "Recon & Clear";
    public override string Description => "Use your recon to find the defenders, then clear the site.";

    protected override IEnumerable<string> StatusLines()
    {
        if (casts > 0) yield return $"Revealed: {revealed} of {defendersSpawned}";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        foreach (var l in CommonResultLines()) yield return l;
        yield return ("Enemies revealed", $"{revealed} of {defendersSpawned}  ({casts} cast{(casts == 1 ? "" : "s")})");
        if (preaimRevealed.Count > 0) yield return ("Pre-aim error on revealed enemies", $"{preaimRevealed.Average():0.0}°");
        if (preaimOther.Count > 0) yield return ("Pre-aim error on the others", $"{preaimOther.Average():0.0}°");
        if (destroyedUtil > 0) yield return ("Recon shot down", destroyedUtil.ToString());
        foreach (var l in DuelResultLines()) yield return l;
    }

    /// <summary>Win rate vs the tier's defenders, one tier lower if your recon found under a third of them.</summary>
    public override int Badge() => WinBadge(defendersSpawned == 0 ? 0f : (float)revealed / defendersSpawned, 1f / 3f);
}
