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
    };

    public static Func<TrainingMode>? Find(string key) => All.FirstOrDefault(f => f().Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
