namespace ValTrainer.Game.Replay;

/// <summary>
/// The .vtr sidecar next to a run's .vtt: magic "VTT2", version, JSON header, then zlib-compressed tracks.
/// Contract stub: the recording work package implements Save/Load and the support table.
/// </summary>
public static partial class ReplayFile
{
    public const int Magic = 0x32545456, Version = 1;

    public static string SidecarOf(string vttPath) => Path.ChangeExtension(vttPath, ".vtr");

    /// <summary>The run's recording: in memory (just played) or its sidecar on disk. Null = no 3D data (old run).</summary>
    public static ReplayRecording? For(RunTelemetry run) =>
        run.Replay ?? (run.SourceFile is { } f && File.Exists(SidecarOf(f)) ? Load(SidecarOf(f)) : null);

    public static void Save(ReplayRecording r, string path) => SaveImpl(r, path);

    /// <summary>Never throws: null for a missing, truncated or unknown-version file. Safe on worker threads.</summary>
    public static ReplayRecording? Load(string path)
    {
        try { return LoadImpl(path); } catch { return null; }
    }

    /// <summary>How well a drill replays in 3D (None = 2D only, Partial = utility/props missing).</summary>
    public static ReplaySupport SupportFor(string modeKey) => SupportImpl(modeKey);

    static partial void SaveCore(ReplayRecording r, string path, ref bool done);
    static partial void LoadCore(string path, ref ReplayRecording? result);
    static partial void SupportCore(string modeKey, ref ReplaySupport result);

    static void SaveImpl(ReplayRecording r, string path) { bool done = false; SaveCore(r, path, ref done); }
    static ReplayRecording? LoadImpl(string path) { ReplayRecording? r = null; LoadCore(path, ref r); return r; }
    static ReplaySupport SupportImpl(string modeKey) { var s = ReplaySupport.None; SupportCore(modeKey, ref s); return s; }
}
