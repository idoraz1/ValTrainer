using System.Globalization;
using ValTrainer.Core;

namespace ValTrainer.Progression;

/// <summary>The level curve (progression-design §3.3): level L → L+1 costs min(500 + 100·(L−1), 2500) XP; no cap.</summary>
public static class XpCurve
{
    public static int CostOf(int level) => Math.Min(500 + 100 * (Math.Max(1, level) - 1), 2500);

    /// <summary>Total XP needed to reach <paramref name="level"/> (level 1 = 0).</summary>
    public static int TotalFor(int level)
    {
        int t = 0;
        for (int l = 1; l < level; l++) t += CostOf(l);
        return t;
    }

    public static int LevelOf(int xp)
    {
        int l = 1;
        while (xp >= CostOf(l)) { xp -= CostOf(l); l++; }
        return l;
    }
}

/// <summary>What a finished Lock-In earned (the Lock-In hand-off's summary line): every step's XP plus the completion
/// bonus, the challenges it finished and what it unlocked.</summary>
public sealed record LockInXp(int Total, int Bonus, int ChallengesDone, IReadOnlyList<string> NewUnlocks);

/// <summary>The one-time "your training counts" reveal (progression-design §7). <see cref="Welcome"/>: a new player
/// (no history): "Welcome: +250 XP. Your first skin is ready."</summary>
public sealed record RevealInfo(bool Welcome, int Runs, long ActiveSecs, int Pbs, int BadgeUps, int LockIns, int Xp, int Level, int Stars,
                                IReadOnlyList<string> Unlocks);

/// <summary>
/// Progression: account level / XP, stars and the unlocks they gate (research: progression-design-1.19.md; the item side:
/// skins2-design-1.19.md). This is the ONE contract between progression and skins; see docs/research/skins2-contracts.md.
/// <para><b>Ids:</b> <see cref="ItemIds"/> (<c>line</c>, <c>line/n</c>, <c>line/variant</c>, <c>factory</c>).
/// <b>Requirements:</b> <see cref="Requirement"/> grammar, table in <see cref="UnlockTable"/>. <b>Lines:</b> the skins side
/// declares them (<see cref="Game.Skins.SkinCatalog.UnlockLines"/>); Progress pulls them lazily on the first query, or
/// takes them through <see cref="RegisterLines"/> (tests).</para>
/// <para><b>Threading:</b> main thread only (queries are cheap and safe before <see cref="Init"/>).</para>
/// <para><b>Rules</b> live in <see cref="ProgressEngine"/> (XP, stars, streak, Lock-Ins), CoachTrack.cs (confirmed coach
/// ranks), Challenges.cs and Retro.cs (first-launch credit + catch-up). <b>Saving:</b> progress.json after each run,
/// Lock-In, coach sample, reroll and seen change, only when <see cref="ProgressStore.CanWrite"/> (dev runs never write).</para>
/// <para><b>Dev flags</b> (with --dev, in memory): <c>--progress-xp N</c>, <c>--progress-level N</c>, <c>--progress-stars N</c>,
/// <c>--progress-to-next N</c>, <c>--progress-unlock-all</c> / <c>--skins-unlock-all</c>, <c>--progress-unlock a,b</c>,
/// <c>--progress-lock-all</c>, <c>--progress-reset</c>, <c>--progress-now 2026-11-20T10:00</c>, <c>--progress-retro</c>,
/// <c>--progress-reveal demo|welcome</c>, <c>--progress-demo-unlock id</c>; headless tools <c>--progress-test</c>,
/// <c>--progress-sim casual|regular|grinder [--sim-days N]</c>, <c>--progress-report</c> (ProgressDev.cs).</para>
/// </summary>
public static class Progress
{
    static ProgressEngine engine = new(new ProgressFile());
    static ProgressFile file => engine.F;
    static bool inited;
    static readonly List<UnlockLine> lineList = new();
    static readonly Dictionary<string, UnlockLine> lines = new(StringComparer.Ordinal);
    static Dictionary<string, string> table = new(StringComparer.Ordinal);
    static readonly Dictionary<string, Requirement> parsed = new(StringComparer.Ordinal);
    static ProgressFacts? facts;
    static TimeSpan clockOffset;
    static bool devLockAll;
    static RevealInfo? devReveal;
    static int lockInXp, lockInDone;
    static readonly List<string> lockInUnlocks = new();

    /// <summary>Dev: every item unlocked (<c>--progress-unlock-all</c> / <c>--skins-unlock-all</c>; only with --dev; never saved).
    /// Self-tests may set it and must restore it.</summary>
    public static bool DevUnlockAll { get; set; } = CmdLine.Dev && (CmdLine.Has("--progress-unlock-all") || CmdLine.Has("--skins-unlock-all"));

    /// <summary>How progress.json loaded at <see cref="Init"/> (Missing before Init).</summary>
    public static ProgressLoad LoadResult { get; private set; } = ProgressLoad.Missing;

    /// <summary>The loaded data. Read-only for everyone but the Progress core (it is the save file).</summary>
    public static ProgressFile Data => file;

    /// <summary>The progression clock: now, or the dev <c>--progress-now</c> moment (day / week resets, rested XP).</summary>
    public static DateTime Now => DateTime.Now + clockOffset;

    internal static ProgressEngine Engine => engine;

    // ---------------- lifecycle ----------------

    /// <summary>Loads progress.json, credits the history (first launch: the retro credit; later: runs from other builds),
    /// applies the dev flags and saves. Called once from Main._Ready (after settings and stats).</summary>
    public static void Init()
    {
        if (inited) return;
        inited = true;
        var f = ProgressStore.Load(out var r);
        LoadResult = r;
        bool dev = CmdLine.Dev;
        if (dev && CmdLine.DevAfter("--progress-now") is { } nowArg
            && DateTime.TryParse(nowArg, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var fake)) clockOffset = fake - DateTime.Now;
        bool reset = dev && CmdLine.Has("--progress-reset");
        bool forceRetro = dev && CmdLine.Has("--progress-retro");
        if (reset || forceRetro) f = new ProgressFile { CreatedWith = AppInfo.Version };
        engine = new ProgressEngine(f);
        var stats = (IReadOnlyList<RunRecord>?)Main.I?.Stats?.Runs ?? Array.Empty<RunRecord>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (reset)
            {
                f.Retro = new RetroInfo { Done = true, Shown = true, When = Now };
                engine.SyncIndex(stats, null);
            }
            else
            {
                bool retro = forceRetro || r is ProgressLoad.Missing or ProgressLoad.Corrupt || f.Retro is not { Done: true };
                engine.Replay(stats, Warmup.WarmupStore.I.Warmups, Now, retro);
                engine.MarkIndexed(stats.Count);
            }
        }
        catch (Exception e) { Log.Exception("Crediting the training history failed", e); }
        engine.PendingUnlocks.Clear(); // startup credit isn't announced one by one: Unseen / the reveal show it
        if (dev) ApplyDevFlags();
        facts = null;
        if (!reset && !forceRetro) ProgressStore.Save(file);
        Log.Info($"[progress] {r}: level {Level}, {Xp} XP, {Stars} stars, {file.Unlocked.Count} unlocks, {stats.Count} runs credited through " +
                 $"{file.CreditedThrough?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-"} ({sw.ElapsedMilliseconds} ms)" +
                 $"{(DevUnlockAll ? " (dev: everything unlocked)" : "")}{(ProgressStore.CanWrite ? "" : " (not saved)")}");
    }

    static void ApplyDevFlags()
    {
        static int? Arg(string flag) => int.TryParse(CmdLine.DevAfter(flag), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        if (Arg("--progress-xp") is { } xp) file.Xp = Math.Max(0, xp);
        if (Arg("--progress-level") is { } lv) file.Xp = XpCurve.TotalFor(Math.Max(1, lv));
        if (Arg("--progress-stars") is { } st) file.Stars = Math.Max(0, st);
        if (Arg("--progress-to-next") is { } tn) file.Xp = Math.Max(0, XpCurve.TotalFor(Level + 1) - Math.Max(1, tn));
        devLockAll = CmdLine.Has("--progress-lock-all");
        foreach (var id in (CmdLine.DevAfter("--progress-unlock") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            file.Unlocked[id.ToLowerInvariant()] = Now;
        }
        if (CmdLine.DevAfter("--progress-demo-unlock") is { } demo && ItemIds.TryParse(demo, out _, out _, out _))
        {
            // Recorded like a real unlock and announced through Progress.Unlocked once the menu is up (the UI's toasts listen).
            string id = demo.Trim().ToLowerInvariant();
            file.Unlocked[id] = Now;
            if (!file.Unseen.Contains(id)) file.Unseen.Add(id);
            var info = new UnlockInfo(id, "dev", Now);
            if (Main.I?.GetTree()?.CreateTimer(1.5) is { } timer) timer.Timeout += () => RaiseUnlocked(info);
        }
        devReveal = CmdLine.DevAfter("--progress-reveal")?.ToLowerInvariant() switch
        {
            "welcome" => new RevealInfo(true, 0, 0, 0, 0, 0, XpRules.Welcome, 1, 0, Array.Empty<string>()),
            "demo" => new RevealInfo(false, 187, 12720, 34, 5, 8, 26029, XpCurve.LevelOf(26029), 12,
                                     AllItems.Where(id => !IsFreeItem(id)).Take(22).ToList()),
            _ => null,
        };
        engine.PendingUnlocks.Clear();
    }

    // ---------------- lines (skins side declares them) ----------------

    /// <summary>Replaces the registered lines and rebuilds the requirement table. The skins catalogue is pulled
    /// automatically on the first query, so normal code never calls this; tests may.</summary>
    public static void RegisterLines(IEnumerable<UnlockLine> newLines)
    {
        lineList.Clear();
        lines.Clear();
        foreach (var l in newLines) { lineList.Add(l); lines[l.Line] = l; }
        table = UnlockTable.Build(lineList);
        parsed.Clear();
        facts = null;
    }

    static void EnsureLines()
    {
        if (lineList.Count == 0) RegisterLines(Game.Skins.SkinCatalog.UnlockLines());
    }

    /// <summary>Every registered line, catalogue order.</summary>
    public static IReadOnlyList<UnlockLine> Lines { get { EnsureLines(); return lineList; } }

    /// <summary>Every item id in table order (lines, then each line's levels and variants).</summary>
    public static IEnumerable<string> AllItems { get { EnsureLines(); return table.Keys; } }

    internal static UnlockLine? LineOf(string line) { EnsureLines(); return lines.TryGetValue(line, out var l) ? l : null; }
    internal static UnlockLine? LineOfItem(string itemId) => ItemIds.TryParse(itemId, out var line, out _, out _) ? LineOf(line) : null;

    /// <summary>The requirement of an item (<see cref="Requirement.Never"/> for unknown ids, free for factory).</summary>
    public static Requirement RequirementOf(string itemId)
    {
        EnsureLines();
        if (itemId == ItemIds.Factory) return Requirement.Free;
        if (parsed.TryGetValue(itemId, out var r)) return r;
        r = table.TryGetValue(itemId, out var src) ? Requirement.Parse(src) : Requirement.Never;
        parsed[itemId] = r;
        return r;
    }

    /// <summary>A table item whose requirement is just <c>free</c> (Core lines): always unlocked, never recorded.</summary>
    internal static bool IsFreeItem(string itemId) => RequirementOf(itemId).Root is ReqLeaf { Term.Kind: ReqKind.Free } && table.ContainsKey(itemId);

    // ---------------- account ----------------

    public static int Xp => file.Xp;
    public static int Level => XpCurve.LevelOf(file.Xp);
    /// <summary>XP gained inside the current level.</summary>
    public static int XpIntoLevel => file.Xp - XpCurve.TotalFor(Level);
    /// <summary>XP the current level needs in total.</summary>
    public static int XpForLevel => XpCurve.CostOf(Level);
    public static int Stars => file.Stars;
    /// <summary>Rested XP pool (shown as a second colour on the XP bar).</summary>
    public static int Rested => file.Rested;
    /// <summary>What the last finished run earned (the results XP strip), null before the first run.</summary>
    public static RunXp? LastRun { get; private set; }
    /// <summary>What the last finished Lock-In earned (the hand-off's summary line), null before the first.</summary>
    public static LockInXp? LastLockIn { get; private set; }
    /// <summary>A Lock-In is running (its steps credited XP): hold unlock / level toasts until its hand-off.</summary>
    public static bool ToastsHeld { get; private set; }

    /// <summary>The snapshot requirements are checked against (rebuilt after <see cref="Changed"/>): progress.json plus the
    /// stats history (best badge, badge drills, benchmark ranks, Sens Finder).</summary>
    public static ProgressFacts Facts => facts ??= engine.Facts();

    // ---------------- weekly streak / coach ----------------

    /// <summary>Weekly training streak in weeks (a week counts with 2+ training days), the best ever and the rest tokens
    /// held (max 2). <see cref="StreakRestUsed"/>: the last counted week used a token ("Rest week used: your streak is safe").</summary>
    public static int StreakWeeks => file.Streak?.Weeks ?? 0;
    public static int StreakBest => file.Streak?.Best ?? 0;
    public static int StreakTokens => file.Streak?.Tokens ?? 0;
    public static bool StreakRestUsed => file.Streak?.RestUsed ?? false;

    /// <summary>The coach was recalibrated after a placement and is re-placing: show "The coach was recalibrated in 1.19.
    /// Your coach rank is being re-placed; nothing you earned is lost."</summary>
    public static bool CoachRecalibrated => file.Coach is { Recalibrated: true, PlacedOn: null };

    /// <summary>A coach track ("overall" or a <see cref="Analysis.Skill"/> name): confirmed rank (CoachRank index, -1 = not
    /// placed yet), valid days counted (after the placement day once placed) and how many the next step needs (3 to
    /// place, 5 to confirm a rank-up).</summary>
    public static (int Rank, int Days, int Need) CoachTrack(string track = ProgressEngine.OverallTrack) => engine.TrackState(track);

    // ---------------- challenges ----------------

    /// <summary>Today's 3 daily challenges (made on first use each day: call at draw / build time).</summary>
    public static IReadOnlyList<ChallengeItem> Daily { get { engine.EnsureChallenges(Now); return file.Daily?.Items ?? new(); } }
    /// <summary>This week's 3 weekly challenges.</summary>
    public static IReadOnlyList<ChallengeItem> Weekly { get { engine.EnsureChallenges(Now); return file.Weekly?.Items ?? new(); } }
    public static DateTime DailyResetsAt => ProgressTime.NextDayReset(Now);
    public static DateTime WeeklyResetsAt => ProgressTime.NextWeekReset(Now);
    /// <summary>Whether a daily can still be rerolled today (one a day).</summary>
    public static bool CanReroll => RerollsLeft > 0;
    /// <summary>Daily rerolls left today (<see cref="ChallengeSet.Rerolls"/> of the daily set counts what is LEFT: 1 a day).</summary>
    public static int RerollsLeft { get { engine.EnsureChallenges(Now); return Math.Max(0, file.Daily?.Rerolls ?? 0); } }

    /// <summary>Rerolls daily slot <paramref name="slot"/> (0–2): false when today's reroll is used or the slot is done.</summary>
    public static bool RerollDaily(int slot) => Reroll(slot);

    /// <summary>Same as <see cref="RerollDaily"/>.</summary>
    public static bool Reroll(int slot)
    {
        if (!engine.Reroll(slot, Now)) return false;
        Save();
        RaiseChanged();
        return true;
    }

    /// <summary>The challenge as a sentence in the UI language ("Coach focus: 3 runs of Head Flicks").</summary>
    public static string ChallengeTitle(ChallengeItem i)
    {
        string drill = i.Drill != null ? DrillName(i.Drill) : "";
        int n = (int)MathF.Round(i.Target);
        return i.Kind switch
        {
            "focus_runs" => L.P(n, $"Coach focus: {n} run of {drill}", $"Coach focus: {n} runs of {drill}"),
            "match_avg" => L.F($"Match your {drill} average ({Warmup.WarmupScore.Format(i.Drill ?? "", i.Baseline)})"),
            "badge" => L.F($"Earn a {Difficulty.Get((int)i.Baseline).DisplayName} badge or better in any drill"),
            "accuracy" => L.F($"Hit {(int)i.Baseline}% accuracy in {drill}"),
            "variety" => L.P(n, $"Play {n} different drill", $"Play {n} different drills"),
            "time" => L.P(n, $"Train for {n} minute", $"Train for {n} minutes"),
            "fresh" => L.F($"Try {drill} again"),
            "map_agent" => L.T("Play a map or agent drill"),
            "lockin" => L.T("Complete a Lock-In"),
            "days" => L.P(n, $"Train on {n} different day", $"Train on {n} different days"),
            "improve" => L.F($"Beat your {drill} baseline by 5%"),
            "lockins" => L.P(n, $"Complete {n} Lock-In", $"Complete {n} Lock-Ins"),
            "badgeups" => L.P(n, $"Earn {n} badge rank-up", $"Earn {n} badge rank-ups"),
            "skill_time" => L.F($"Train {L.T(SkillName(i.Drill))} drills for {n} minutes"),
            "pbs" => L.P(n, $"Set {n} personal best", $"Set {n} personal bests"),
            _ => L.T("Challenge"),
        };
    }

    /// <summary>Why it was picked, in the UI language ("Coach: slow micro-adjustments"), or "".</summary>
    public static string ChallengeWhy(ChallengeItem i) => i.Kind switch
    {
        "focus_runs" or "improve" => i.Why switch
        {
            "starter" => L.T("Fills your coach profile"),
            "fresh" => L.T("Not played this week"),
            _ => i.WhyText is { Length: > 0 } w ? L.F($"Coach: {w}") : L.T("The coach's focus"),
        },
        "fresh" => L.T("Not played in 14 days"),
        "skill_time" => L.T("Your weakest skill"),
        "match_avg" or "accuracy" => L.T("Your 7-day average"),
        "badge" => L.T("Your typical badge"),
        _ => "",
    };

    /// <summary>Progress text for a challenge card ("2 / 3", "6 / 10 min").</summary>
    public static string ChallengeProgress(ChallengeItem i)
    {
        int v = (int)Math.Floor(Math.Min(i.Value, i.Target) + 1e-4f), t = (int)MathF.Round(i.Target);
        return i.Kind is "time" or "skill_time" ? L.F($"{v} / {t} min") : L.Raw($"{v.ToString(CultureInfo.InvariantCulture)} / {t.ToString(CultureInfo.InvariantCulture)}");
    }

    static string DrillName(string key) => DrillGuns.Proto(key)?.DisplayName ?? key;
    static string SkillName(string? skill) => Enum.TryParse<Analysis.Skill>(skill, out var s) ? Analysis.Coach.SkillName(s) : skill ?? "";

    // ---------------- reveal ----------------

    /// <summary>The first-launch reveal to show (after What's new / the updates prompt), or null. Data only: the Progress UI
    /// draws it and calls <see cref="MarkRevealShown"/> when it is closed or skipped.</summary>
    public static RevealInfo? Reveal
    {
        get
        {
            if (devReveal != null) return devReveal;
            if (file.Retro is not { Done: true, Shown: false } r) return null;
            return new RevealInfo(r.Runs == 0, r.Runs, r.ActiveSecs, r.Pbs, r.BadgeUps, r.LockIns, r.Xp, XpCurve.LevelOf(r.Xp), r.Stars,
                                  (IReadOnlyList<string>?)r.Unlocks ?? Array.Empty<string>());
        }
    }

    /// <summary>The retro / welcome reveal was shown (saved in progress.json: <c>Retro.Shown</c>), so it never shows again.</summary>
    public static bool RetroShown => file.Retro is not { Done: true, Shown: false };

    /// <summary>Same as <see cref="MarkRevealShown"/> (the Progress UI's name).</summary>
    public static void MarkRetroShown() => MarkRevealShown();

    public static void MarkRevealShown()
    {
        devReveal = null;
        if (file.Retro is not { Shown: false } r) return;
        r.Shown = true;
        foreach (var id in r.Unlocks ?? new()) file.Unseen.Remove(id); // the reveal showed them
        Save();
        RaiseChanged();
    }

    // ---------------- unlock queries ----------------

    /// <summary>Whether <paramref name="itemId"/> can be worn. Factory: always. Unknown ids: false. Permanent unlocks in
    /// progress.json always count (never revoke).</summary>
    public static bool IsUnlocked(string? itemId)
    {
        if (itemId == null) return false;
        if (itemId == ItemIds.Factory) return true;
        if (!ItemIds.TryParse(itemId, out var line, out int level, out var variant)) return false;
        EnsureLines();
        if (!lines.TryGetValue(line, out var l)) return false;
        string id = variant != null ? ItemIds.Variant(line, variant) : ItemIds.Level(line, level);
        if (!table.ContainsKey(id)) return false;
        if (DevUnlockAll || ValTrainer.Core.Supporter.Active) return true; // a supporter code: every skin, level and variant
        if (devLockAll) return IsFreeItem(id);
        if (file.Unlocked.ContainsKey(id)) return true;
        if (Blocker(l, level, variant) != null) return false;
        return RequirementOf(id).Met(Facts);
    }

    /// <summary>Structural prerequisite: level n needs n−1, a variant needs <see cref="UnlockLine.VariantNeedsLevel"/>.</summary>
    static string? Blocker(UnlockLine l, int level, string? variant)
    {
        if (variant != null)
        {
            int need = l.VariantNeedsLevel;
            return IsUnlocked(ItemIds.Level(l.Line, need)) ? null : need <= 1 ? L.T("Unlock the skin first") : L.F($"Unlock level {need} first");
        }
        if (level <= 1) return null;
        return IsUnlocked(ItemIds.Level(l.Line, level - 1)) ? null : L.F($"Unlock level {level - 1} first");
    }

    /// <summary>Lock state + requirement text + progress for the UI (translated now: call at draw / build time).</summary>
    public static UnlockStatus Status(string itemId)
    {
        EnsureLines();
        if (itemId == ItemIds.Factory) return new UnlockStatus(itemId, true, true, Requirement.Free, L.T("Free"), 1f, "", null);
        bool known = ItemIds.TryParse(itemId, out var line, out int level, out var variant) && lines.ContainsKey(line);
        string id = known ? (variant != null ? ItemIds.Variant(line, variant) : ItemIds.Level(line, level)) : itemId;
        known = known && table.ContainsKey(id);
        if (!known) return new UnlockStatus(itemId, false, false, Requirement.Never, L.T("Not available yet"), 0f, "", null);
        var req = RequirementOf(id);
        if (IsUnlocked(id)) return new UnlockStatus(id, true, true, req, req.Text(), 1f, "", null);
        var f = Facts;
        string? blocker = Blocker(lines[line], level, variant);
        var unmet = req.Root.FirstUnmet(f);
        return new UnlockStatus(id, true, false, req, req.Text(), req.Fraction(f), unmet?.ProgressText(f) ?? "", blocker);
    }

    /// <summary>The <paramref name="n"/> locked items closest to unlocking (menu tooltip, Progress screen): no structural
    /// blocker, highest progress first, table order on ties.</summary>
    public static IReadOnlyList<UnlockStatus> NextUnlocks(int n)
    {
        EnsureLines();
        return table.Keys.Where(id => !IsUnlocked(id)).Select(Status).Where(s => s.Blocker == null)
            .OrderByDescending(s => s.Progress01).Take(Math.Max(0, n)).ToList();
    }

    /// <summary>The <paramref name="n"/> newest permanent unlocks (item id, when), newest first.</summary>
    public static IReadOnlyList<UnlockInfo> RecentUnlocks(int n) =>
        file.Unlocked.OrderByDescending(e => e.Value).Take(Math.Max(0, n))
            .Select(e => new UnlockInfo(e.Key, file.Retro?.Unlocks?.Contains(e.Key) == true ? "retro" : "run", e.Value)).ToList();

    /// <summary>Unlocked items not looked at yet (toasts, the SKINS dot).</summary>
    public static IReadOnlyList<string> Unseen => file.Unseen;

    /// <summary>The player saw <paramref name="itemId"/> (picker opened on it, toast clicked).</summary>
    public static void MarkSeen(string itemId)
    {
        if (!file.Unseen.Remove(itemId)) return;
        Save();
        Changed?.Invoke();
    }

    // ---------------- events (main thread) ----------------

    /// <summary>One per newly unlocked item (live, retro, dev). Main forwards it to <c>GunSkins.NotifyChanged</c>.</summary>
    public static event Action<UnlockInfo>? Unlocked;
    /// <summary>The account level went up (from, to).</summary>
    public static event Action<int, int>? LevelUp;
    /// <summary>XP / stars / challenges / seen state changed (menu strip redraw).</summary>
    public static event Action? Changed;

    /// <summary>Progress core: raise after recording a permanent unlock in <see cref="Data"/>.</summary>
    internal static void RaiseUnlocked(UnlockInfo u) { facts = null; Unlocked?.Invoke(u); }
    internal static void RaiseLevelUp(int from, int to) => LevelUp?.Invoke(from, to);
    internal static void RaiseChanged() { facts = null; Changed?.Invoke(); }

    static void Save() => ProgressStore.Save(file);

    /// <summary>Announces what the engine recorded since the last call (unlocks, level change) and saves.</summary>
    static void Publish(int levelBefore)
    {
        facts = null;
        var pending = engine.PendingUnlocks.ToList();
        engine.PendingUnlocks.Clear();
        Save();
        foreach (var u in pending) RaiseUnlocked(u);
        if (Level > levelBefore) RaiseLevelUp(levelBefore, Level);
        RaiseChanged();
    }

    // ---------------- feeds (Progress core only) ----------------

    /// <summary>Active seconds of a run (1-s buckets with mouse movement, firing or key movement), for <see cref="RunFacts"/>.</summary>
    public static float ActiveSeconds(Game.RunTelemetry? t) => XpRules.ActiveSeconds(t);

    /// <summary>A run finished (GameSession.Finish, after the record). Returns the XP breakdown for the results strip (also
    /// kept in <see cref="LastRun"/>). Dev runs count in memory only.</summary>
    public static RunXp? OnRunFinished(RunFacts run)
    {
        try
        {
            if (!inited) return LastRun = null;
            int lv0 = Level;
            var stats = Main.I?.Stats?.Runs;
            if (stats != null) engine.SyncIndex(stats, run.Record);
            var r = engine.ScoreRun(run, Now, replay: false);
            if (stats != null) engine.MarkIndexed(stats.Count);
            if (run.LockInStep)
            {
                lockInXp += r.Total;
                lockInDone += r.Challenges.Count(c => c.Done);
                lockInUnlocks.AddRange(r.NewUnlocks);
            }
            else { lockInXp = lockInDone = 0; lockInUnlocks.Clear(); } // an abandoned Lock-In's steps don't carry over
            ToastsHeld = run.LockInStep;
            LastRun = r;
            Log.Info($"[progress] {run.Record.Mode}: +{r.Total} XP ({string.Join(" · ", r.Parts.Select(p => $"{p.Label} {p.Xp}"))}), " +
                     $"{(r.StarsGained > 0 ? $"+{r.StarsGained} stars, " : "")}level {r.LevelAfter}, active {run.ActiveSecs:0} s{(ProgressStore.CanWrite ? "" : " (not saved)")}");
            Publish(lv0);
            return r;
        }
        catch (Exception e)
        {
            Log.Exception("Progress: crediting the run failed", e);
            return LastRun = null;
        }
    }

    /// <summary>A Lock-In finished (WarmupRunner.Complete, after its store saved): completion bonus, Lock-In challenges;
    /// <see cref="LastLockIn"/> sums the routine for the hand-off.</summary>
    public static void OnLockInFinished(Warmup.WarmupRecord rec)
    {
        try
        {
            if (!inited) return;
            int lv0 = Level;
            var ticks = new List<ChallengeTick>();
            int unlocks0 = engine.PendingUnlocks.Count;
            int bonus = engine.LockIn(rec, Now, replay: false, new List<XpPart>(), ticks);
            lockInUnlocks.AddRange(engine.PendingUnlocks.Skip(unlocks0).Select(u => u.ItemId));
            LastLockIn = new LockInXp(lockInXp + bonus, bonus, lockInDone + ticks.Count(t => t.Done), lockInUnlocks.ToList());
            lockInXp = lockInDone = 0;
            lockInUnlocks.Clear();
            ToastsHeld = false;
            Publish(lv0);
        }
        catch (Exception e) { Log.Exception("Progress: crediting the Lock-In failed", e); }
    }

    /// <summary>The coach finished a real profile (CoachData, main thread; never for --fakecoach): today's sample,
    /// placement / confirmed rank-ups, the focus drill for challenges.</summary>
    public static void OnProfile(Analysis.SkillProfile profile)
    {
        try
        {
            if (!inited) return;
            int lv0 = Level;
            engine.OnProfile(profile, Now, new List<XpPart>(), new List<string>());
            Publish(lv0);
        }
        catch (Exception e) { Log.Exception("Progress: the coach sample failed", e); }
    }

    // ---------------- tests ----------------

    /// <summary>Self-tests: run against <paramref name="e"/> (null = back to the previous engine).</summary>
    internal static ProgressEngine SwapEngineForTests(ProgressEngine e)
    {
        var old = engine;
        engine = e;
        facts = null;
        return old;
    }
}
