namespace ValTrainer.Modes;

public static class ModeRegistry
{
    public static readonly List<Func<TrainingMode>> All = new()
    {
        () => new GridshotMode(),
        () => new FlickMode(),
        () => new SpidershotMode(),
        () => new TrackingMode(),
        () => new StrafeBotsMode(),
        () => new PeekMode(),
        () => new OperatorMode(),
        () => new SprayControlMode(Core.WeaponKind.Vandal),
        () => new SprayControlMode(Core.WeaponKind.Phantom),
        () => new SprayTransferMode(),
        () => new CounterStrafeMode(),
        () => new PeekDuelMode(),
        () => new SiteClearMode(),
        () => new FlashDodgeMode(),
        () => new ReactionMode(),
        () => new SensFinderMode(),
        () => new CrosshairFinderMode(),
        () => new DeathmatchMode(),
        // v1.4.0
        () => new MicroshotMode(),
        () => new TargetSwitchMode(),
        () => new PopupMode(),
        () => new LongTapsMode(),
        () => new JumpPeekMode(),
        () => new JigglePeekMode(),
        () => new PostPlantMode(),
        () => new RetakeMode(),
        () => new AnchorMode(),
        () => new SoundLockMode(),
        () => new FlashPeekMode(),
        () => new ReconMode(),
        () => new SmokeExecuteMode(),
        () => new MobilityEntryMode(),
        () => new ChamberMode(),
    };

    /// <summary>
    /// Drills that take an argument after a colon, usually an agent ("flashpeek:skye", "smokeexec:omen", "anchor:killjoy"):
    /// base key to factory(argument). An instance's <see cref="TrainingMode.Key"/> includes its argument, so stats, telemetry
    /// and personal bests are kept per agent. Each drill falls back to its default for arguments it doesn't support.
    /// </summary>
    public static readonly Dictionary<string, Func<string, TrainingMode>> WithArg = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flashpeek"] = a => new FlashPeekMode(a),
        ["recon"] = a => new ReconMode(a),
        ["smokeexec"] = a => new SmokeExecuteMode(a),
        ["mobility"] = a => new MobilityEntryMode(a),
        ["chamber"] = a => new ChamberMode(a),
        ["anchor"] = a => new AnchorMode(a),
    };

    /// <summary>Factory for a mode key: "flick", or "base:argument" for drills in <see cref="WithArg"/> ("flashpeek" alone = default argument).</summary>
    public static Func<TrainingMode>? Find(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        int c = key.IndexOf(':');
        if (c > 0 && WithArg.TryGetValue(key[..c], out var make)) { string arg = key[(c + 1)..]; return () => make(arg); }
        var plain = All.FirstOrDefault(f => f().Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (plain != null) return plain;
        return WithArg.TryGetValue(key, out var def) ? () => def("") : null;
    }
}
