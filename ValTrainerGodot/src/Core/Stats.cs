using System.Text.Json;

namespace ValTrainer.Core;

public sealed class RunRecord
{
    public string Mode = "";
    public DateTime When;
    public int Score;
    public float Accuracy;      // 0..1, -1 when not applicable
    public int Hits, Shots, Kills;
    public float AvgKillMs;     // avg time per kill / reaction time, 0 when not applicable
    public float HeadshotPct;   // 0..1, -1 when not applicable
    public float Sens;
    public int Tier = -1;       // difficulty tier played (-1 = old Easy/Normal/Hard build)
    public int Badge = -1;      // rank badge earned (-1 = below Rookie / not rated)
    public string Map = "";
    /// <summary>The gun played (<see cref="WeaponKind"/> name, e.g. "Phantom"). Null = a run from before 1.18 (it counts as
    /// the drill's default gun) or a drill without a gun. Older versions ignore the field.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Weapon;
    /// <summary>Aim-coach metrics for this run (see Analysis.Coach.Summarize).</summary>
    public Dictionary<string, float>? Metrics;
    /// <summary>Saved telemetry file (frames/shots/events), if kept.</summary>
    public string? TelemetryFile;
    /// <summary>Played as a step of a warm-up routine (only steps at the player's own sens are kept here).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Warmup;
}

public sealed class StatsStore
{
    public List<RunRecord> Runs = new();

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    static string FilePath => Path.Combine(Paths.DataDir, "stats.json");

    public static StatsStore Load()
    {
        string path = FilePath;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<StatsStore>(File.ReadAllText(path), Json) ?? new StatsStore();
                s.Runs ??= new();
                s.Runs.RemoveAll(r => r == null);
                return s;
            }
        }
        catch (Exception e) when (Paths.IsCorruptData(e)) { Paths.KeepCorrupt(path, e); }
        catch (Exception e)
        {
            Log.Error($"{path} couldn't be opened ({e.Message}); this session's runs won't be saved so the file isn't overwritten");
            return new StatsStore { readFailed = true };
        }
        return new StatsStore();
    }

    /// <summary>stats.json exists but couldn't be opened: never overwrite it (that would wipe the history).</summary>
    [System.Text.Json.Serialization.JsonIgnore] bool readFailed;

    /// <summary>Never throws: an unwritable data folder is logged and reported via <see cref="Paths.LastWriteError"/>.</summary>
    public void Save()
    {
        if (readFailed) return;
        try { Paths.TryWriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception e) { Log.Error($"Saving stats failed: {e.Message}"); }
    }

    public IEnumerable<RunRecord> For(string mode) => Runs.Where(r => r.Mode == mode);

    public int Best(string mode) => For(mode).Select(r => r.Score).DefaultIfEmpty(-1).Max();

    /// <summary>Personal best at a specific tier (scores across tiers aren't comparable).</summary>
    public int Best(string mode, int tier) => For(mode).Where(r => r.Tier == tier).Select(r => r.Score).DefaultIfEmpty(-1).Max();

    /// <summary>Best rank badge ever earned in this mode.</summary>
    public int BestBadge(string mode) => For(mode).Select(r => r.Badge).DefaultIfEmpty(-1).Max();

    // ---------------- per gun (1.18; research: weapons-design-1.18.md §3.4) ----------------

    /// <summary>The gun a run was played with. A run without a gun name (before 1.18, or a drill without a gun) counts as
    /// <paramref name="defaultGun"/>, the drill's default. A name this build doesn't know (a newer version's gun) is
    /// <see cref="WeaponKind.None"/>: it never counts toward a real gun's records.</summary>
    public static WeaponKind GunOf(RunRecord r, WeaponKind defaultGun) =>
        string.IsNullOrWhiteSpace(r.Weapon) ? defaultGun : Weapons.Parse(r.Weapon) ?? WeaponKind.None;

    /// <summary>This drill's runs played with <paramref name="gun"/> (old runs count as <paramref name="defaultGun"/>).</summary>
    public IEnumerable<RunRecord> ForGun(string mode, WeaponKind gun, WeaponKind defaultGun) => For(mode).Where(r => GunOf(r, defaultGun) == gun);

    /// <summary>Personal best at a tier with one gun (scores across tiers or guns aren't comparable). With no runs from
    /// 1.18 on (every run counts as the default gun) this equals <see cref="Best(string, int)"/> for the default gun.</summary>
    public int Best(string mode, int tier, WeaponKind gun, WeaponKind defaultGun) =>
        ForGun(mode, gun, defaultGun).Where(r => r.Tier == tier).Select(r => r.Score).DefaultIfEmpty(-1).Max();

    /// <summary>Best rank badge ever earned in this mode with a rated gun (<see cref="GunRating"/>): badges are calibrated
    /// on the drill's own gun, so a run with another gun gets a score and a PB but no badge.</summary>
    public int BestBadge(string mode, Func<WeaponKind, bool> rated, WeaponKind defaultGun) =>
        For(mode).Where(r => rated(GunOf(r, defaultGun))).Select(r => r.Badge).DefaultIfEmpty(-1).Max();

    /// <summary>Rated best badge of drill <paramref name="m"/> (its default gun and rated guns from the drill).</summary>
    public int BestBadge(Modes.TrainingMode m) => BestBadge(m.Key, g => GunRating.IsRated(m, g), m.Weapon);

    /// <summary>Personal best of drill <paramref name="m"/> at a tier with <paramref name="gun"/>.</summary>
    public int Best(Modes.TrainingMode m, int tier, WeaponKind gun) => Best(m.Key, tier, gun, m.Weapon);
}

/// <summary>
/// Which guns a drill's badges (and the coach's rank estimate) are calibrated for (weapons-design-1.18.md §3.4): the
/// drill's default gun, plus the Phantom in drills whose default is the Vandal (so the two rifles are equal citizens). A
/// run with any other gun is "unrated": it still gets a score, a PB per gun and a coach review.
/// </summary>
public static class GunRating
{
    /// <summary>The rated guns of <paramref name="m"/> (<see cref="Modes.TrainingMode.RatedWeapons"/>), default first.</summary>
    public static IReadOnlyList<WeaponKind> RatedFor(Modes.TrainingMode m) =>
        m.RatedWeapons.Append(m.Weapon).Distinct().OrderBy(k => k == m.Weapon ? 0 : 1).ToList();

    public static bool IsRated(Modes.TrainingMode m, WeaponKind gun) => m.IsRated(gun);

    /// <summary>"the Vandal or Phantom" / "the Operator" (results note for unrated runs).</summary>
    public static string RatedText(Modes.TrainingMode m) => "the " + string.Join(" or ", RatedFor(m).Select(GunName));

    /// <summary><see cref="RatedText"/> in the UI language, for display (RatedText stays English: a self-test compares it).
    /// Gun names are never translated.</summary>
    public static string DisplayRatedText(Modes.TrainingMode m)
    {
        var g = RatedFor(m).Select(GunName).ToList();
        return g.Count <= 1 ? L.F($"the {g.FirstOrDefault() ?? ""}") : L.F($"the {string.Join(", ", g.Take(g.Count - 1))} or {g[^1]}");
    }

    /// <summary>"Vandal / Phantom" (where space is tight).</summary>
    public static string RatedShort(Modes.TrainingMode m) => string.Join(" / ", RatedFor(m).Select(GunName));

    /// <summary>A gun's display name ("Tour De Force", "Vandal").</summary>
    public static string GunName(WeaponKind k) => Weapons.Get(k)?.Name ?? k.ToString();
}

/// <summary>
/// Default guns of drill keys, for reading old runs (a run without a gun name counts as its drill's default). Built once
/// per key from <see cref="Modes.ModeRegistry"/>; an unknown key (a drill this build no longer has) defaults to
/// <see cref="WeaponKind.Vandal"/>, the default of almost every drill. Main thread only.
/// </summary>
public static class DrillGuns
{
    static readonly Dictionary<string, Modes.TrainingMode?> protos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A throw-away instance of the drill (null for an unknown key).</summary>
    public static Modes.TrainingMode? Proto(string key)
    {
        if (!protos.TryGetValue(key, out var m))
        {
            try { m = Modes.ModeRegistry.Find(key)?.Invoke(); } catch { m = null; }
            protos[key] = m;
        }
        return m;
    }

    public static WeaponKind DefaultOf(string key) => Proto(key)?.Weapon ?? WeaponKind.Vandal;

    /// <summary>The gun of a run, reading its drill's default for old runs.</summary>
    public static WeaponKind GunOf(RunRecord r) => StatsStore.GunOf(r, DefaultOf(r.Mode ?? ""));

    /// <summary>Whether a run's gun is rated for its drill (an unknown drill: rated, as every run was before 1.18).</summary>
    public static bool IsRated(RunRecord r) => Proto(r.Mode ?? "") is not { } m || GunRating.IsRated(m, GunOf(r));
}
