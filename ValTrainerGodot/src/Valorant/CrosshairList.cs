using ValTrainer.Core;

namespace ValTrainer.Valorant;

/// <summary>Where a crosshair of the switcher comes from.</summary>
public enum CrosshairSource
{
    /// <summary>A VALORANT profile on this PC (SavedCrosshairProfileData), or the one crosshair the import found.</summary>
    Valorant,
    /// <summary>ValTrainer's CUSTOM slot: the Crosshair Finder's result or a code pasted before 1.19 (<see cref="CrosshairCode.Finder"/>).</summary>
    Custom,
    /// <summary>A code saved in ValTrainer (<see cref="AppSettings.SavedCrosshairs"/>).</summary>
    Saved,
}

/// <summary>One crosshair of the switcher.</summary>
/// <param name="Index">Valorant: the profile's index in VALORANT's profiles[] (-1 without the profile JSON); Saved: the
/// index in <see cref="AppSettings.SavedCrosshairs"/>; Custom: -1.</param>
/// <param name="Name">The name to show; duplicates get " (2)", " (3)" … (the profile itself keeps its own name).</param>
/// <param name="Tag">Where it comes from, in English (logs, self-tests): "VALORANT 2/4 · active in VALORANT", "Saved 3/7",
/// "ValTrainer custom". Shown as <see cref="DisplayTag"/> (the toast shows it in upper case).</param>
public sealed record CrosshairEntry(CrosshairSource Source, int Index, string Name, string Tag, CrosshairSettings Profile, bool ActiveInValorant)
{
    /// <summary>Position in <see cref="CrosshairList.All"/> (0-based).</summary>
    public int Position { get; init; }
    /// <summary>The "2/4" in the tag: the entry's number among its source's entries and their count (0 = no number).</summary>
    public int TagNum { get; init; }
    public int TagOf { get; init; }

    /// <summary><see cref="Tag"/> in the UI language (built now, so a language switch shows at once).</summary>
    public string DisplayTag => TagOf <= 0 ? L.T(Tag)
        : Source == CrosshairSource.Saved ? L.F($"Saved {TagNum}/{TagOf}")
        : ActiveInValorant ? L.F($"VALORANT {TagNum}/{TagOf} · active in VALORANT") : L.F($"VALORANT {TagNum}/{TagOf}");
}

/// <summary>
/// The crosshair switcher (Settings → Crosshair, the menu's crosshair panel, the pause menu, the Previous / Next crosshair
/// binds in drills): every VALORANT profile on this PC in VALORANT's order, then ValTrainer's CUSTOM slot (the Crosshair
/// Finder's result) when there is one, then the codes saved in ValTrainer. Always at least one entry.
/// <para>The pick lives in ValTrainer's settings.json only (<see cref="AppSettings.CrosshairChoice"/>,
/// <see cref="AppSettings.UseFinderCrosshair"/>): VALORANT's files are only ever read. The default (no choice) follows
/// VALORANT's active profile, exactly as before 1.19.</para>
/// <para>Cached: rebuilt only when the import (<see cref="Main.Valorant"/>), the CUSTOM slot, the saved list or the pick
/// change, so <see cref="Picked"/> is cheap enough for <see cref="CrosshairCode.Effective"/> every frame.</para>
/// </summary>
public static class CrosshairList
{
    public const int MaxSaved = 20;
    public const int MaxName = 40;

    // ---------------- cache ----------------

    static object? kValorant, kFinder, kSettings, kSaved, kChoice;
    static bool kUseFinder;
    static int kRev = -1;
    static List<CrosshairEntry> all = new();
    static int current;
    static CrosshairSettings? picked;

    /// <summary>Bumped by every change made here (cache key; UI lists rebuild on it).</summary>
    public static int Revision { get; private set; }

    static void Sync()
    {
        var app = Main.I;
        if (app == null)
        {
            if (all.Count == 0) all = Build(new ValorantProfile(), null, null);
            return;
        }
        var v = app.Valorant;
        var st = app.Settings;
        var f = CrosshairCode.Finder;
        if (ReferenceEquals(v, kValorant) && ReferenceEquals(f, kFinder) && ReferenceEquals(st, kSettings) && ReferenceEquals(st.SavedCrosshairs, kSaved)
            && ReferenceEquals(st.CrosshairChoice, kChoice) && st.UseFinderCrosshair == kUseFinder && Revision == kRev && all.Count > 0) return;
        (kValorant, kFinder, kSettings, kSaved, kChoice, kUseFinder, kRev) = (v, f, st, st.SavedCrosshairs, st.CrosshairChoice, st.UseFinderCrosshair, Revision);
        all = Build(v, f, st.SavedCrosshairs);
        (current, picked) = Resolve(all, v, st);
    }

    /// <summary>Every crosshair the switcher cycles through, in order (never empty).</summary>
    public static IReadOnlyList<CrosshairEntry> All { get { Sync(); return all; } }

    /// <summary>Index in <see cref="All"/> of the crosshair drills draw now.</summary>
    public static int Current { get { Sync(); return current; } }

    public static CrosshairEntry CurrentEntry { get { Sync(); return all[Math.Clamp(current, 0, all.Count - 1)]; } }

    /// <summary>The non-CUSTOM pick resolved to a profile; null = VALORANT's active profile (the default).</summary>
    public static CrosshairSettings? Picked { get { Sync(); return picked; } }

    /// <summary>"2/5".</summary>
    public static string CountText => $"{Current + 1}/{All.Count}";

    /// <summary>The Previous / Next crosshair binds for hints: ", ." ("" when both are unbound).</summary>
    public static string KeysText
    {
        get
        {
            var b = Main.I.Valorant.Binds;
            if (!b.IsBound(GameAction.CrosshairPrev) && !b.IsBound(GameAction.CrosshairNext)) return "";
            return $"{b.Short(GameAction.CrosshairPrev)} {b.Short(GameAction.CrosshairNext)}";
        }
    }

    // ---------------- picking ----------------

    /// <summary>Draws entry <paramref name="i"/> (wraps) from now on and saves the pick (settings.json; a no-op in dev runs).</summary>
    public static CrosshairEntry Select(int i)
    {
        var list = All;
        var e = list[WrapIndex(i, list.Count)];
        Apply(e, Main.I.Settings, Main.I.Valorant);
        Revision++;
        Main.I.Settings.Save();
        return e;
    }

    /// <summary>The next (<paramref name="d"/> = 1) or previous (-1) crosshair; wraps both ways.</summary>
    public static CrosshairEntry Step(int d) => Select(Current + d);

    /// <summary><paramref name="i"/> wrapped into 0 … n-1 (both ways).</summary>
    internal static int WrapIndex(int i, int n) => n <= 0 ? 0 : ((i % n) + n) % n;

    /// <summary>Stores the pick of <paramref name="e"/>: the CUSTOM slot wins through <see cref="AppSettings.UseFinderCrosshair"/>
    /// (unchanged meaning, so the Crosshair Finder's buttons and older builds work as before); VALORANT's active profile
    /// is stored as "no choice" so a later change in VALORANT shows up.</summary>
    internal static void Apply(CrosshairEntry e, AppSettings st, ValorantProfile v)
    {
        switch (e.Source)
        {
            case CrosshairSource.Custom:
                st.UseFinderCrosshair = true;
                break;
            case CrosshairSource.Saved:
                st.UseFinderCrosshair = false;
                st.CrosshairChoice = st.SavedCrosshairs is { } sv && e.Index >= 0 && e.Index < sv.Count
                    ? new CrosshairChoice { Kind = "saved", Code = sv[e.Index].Code } : null;
                break;
            default:
                st.UseFinderCrosshair = false;
                st.CrosshairChoice = e.ActiveInValorant || e.Index < 0 ? null
                    : new CrosshairChoice { Kind = "valorant", Account = v.AccountId, Index = e.Index, Name = e.Profile.Name };
                break;
        }
    }

    // ---------------- building and resolving (pure: the self-tests call these directly) ----------------

    /// <summary>The list for this import, CUSTOM slot and saved codes.</summary>
    internal static List<CrosshairEntry> Build(ValorantProfile v, CrosshairSettings? finder, IReadOnlyList<SavedCrosshair>? saved)
    {
        var list = new List<CrosshairEntry>();
        var profiles = v.CrosshairProfiles.Count > 0 ? v.CrosshairProfiles : new List<CrosshairSettings> { v.Crosshair };
        bool fromJson = profiles.Any(p => p.ProfileIndex >= 0);
        var names = Distinct(profiles.Select(p => p.Name).ToList());
        for (int i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i];
            bool active = ReferenceEquals(p, v.Crosshair) || profiles.Count == 1;
            string tag = fromJson ? $"VALORANT {i + 1}/{profiles.Count}{(active ? " · active in VALORANT" : "")}"
                : !v.Found ? L.N("VALORANT default") : L.N("VALORANT · the only one on this PC");
            list.Add(new CrosshairEntry(CrosshairSource.Valorant, fromJson ? p.ProfileIndex : -1, names[i], tag, p, active)
                { TagNum = fromJson ? i + 1 : 0, TagOf = fromJson ? profiles.Count : 0 });
        }
        if (finder != null) list.Add(new CrosshairEntry(CrosshairSource.Custom, -1, finder.Name, L.N("ValTrainer custom"), finder, false));
        if (saved is { Count: > 0 })
        {
            var ok = new List<(int Index, CrosshairSettings Profile)>();
            for (int j = 0; j < saved.Count; j++)
                if (saved[j] is { } sc && CrosshairCode.Decode(sc.Code) is { } s)
                {
                    s.Name = CleanName(sc.Name) ?? $"Code {j + 1}";
                    ok.Add((j, s));
                }
            var sn = Distinct(ok.Select(x => x.Profile.Name).ToList());
            for (int j = 0; j < ok.Count; j++)
                list.Add(new CrosshairEntry(CrosshairSource.Saved, ok[j].Index, sn[j], $"Saved {j + 1}/{ok.Count}", ok[j].Profile, false)
                    { TagNum = j + 1, TagOf = ok.Count });
        }
        for (int i = 0; i < list.Count; i++) list[i] = list[i] with { Position = i };
        return list;
    }

    /// <summary>"Main", "Main" → "Main", "Main (2)".</summary>
    static List<string> Distinct(List<string> names)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var r = new List<string>(names.Count);
        foreach (var raw in names)
        {
            string n = string.IsNullOrWhiteSpace(raw) ? "Crosshair" : raw.Trim();
            seen[n] = seen.TryGetValue(n, out int c) ? c + 1 : 1;
            r.Add(seen[n] == 1 ? n : $"{n} ({seen[n]})");
        }
        return r;
    }

    /// <summary>The entry the settings point at and the profile to draw for it (null = VALORANT's active profile).
    /// A VALORANT pick is found by index (if its name still matches), else by name, in the same account only; a saved
    /// pick by its code; anything not found falls back to VALORANT's active profile (the stored pick is kept).</summary>
    internal static (int Current, CrosshairSettings? Picked) Resolve(IReadOnlyList<CrosshairEntry> list, ValorantProfile v, AppSettings st)
    {
        int active = Math.Max(0, IndexOf(list, e => e.Source == CrosshairSource.Valorant && e.ActiveInValorant));
        int at = -1;
        var c = st.CrosshairChoice;
        if (c?.Kind == "saved" && !string.IsNullOrWhiteSpace(c.Code))
            at = IndexOf(list, e => e.Source == CrosshairSource.Saved && st.SavedCrosshairs is { } sv && e.Index < sv.Count
                                    && sv[e.Index].Code.Trim() == c.Code.Trim());
        else if (c?.Kind == "valorant" && c.Account == v.AccountId)
        {
            at = IndexOf(list, e => e.Source == CrosshairSource.Valorant && e.Index == c.Index && e.Index >= 0 && e.Profile.Name == c.Name);
            if (at < 0) at = IndexOf(list, e => e.Source == CrosshairSource.Valorant && e.Index >= 0 && e.Profile.Name == c.Name);
        }
        var pick = at >= 0 ? list[at].Profile : null;
        int cur = at >= 0 ? at : active;
        if (st.UseFinderCrosshair && IndexOf(list, e => e.Source == CrosshairSource.Custom) is var ci and >= 0) cur = ci;
        return (cur, pick);
    }

    static int IndexOf(IReadOnlyList<CrosshairEntry> list, Func<CrosshairEntry, bool> f)
    {
        for (int i = 0; i < list.Count; i++)
            if (f(list[i])) return i;
        return -1;
    }

    // ---------------- saved codes ----------------

    /// <summary>Adds a crosshair code to the saved list (PASTE / SAVE CURRENT). Name: <paramref name="name"/>, else the
    /// community NAME;"…" extension of the code, else "Code n". A code that draws the same crosshair as a saved one isn't
    /// added twice (that one is used instead). False when it isn't a code or the list is full; <paramref name="msg"/> is a
    /// short button label ("SAVED ✓", "ALREADY SAVED ✓", "NOT A CODE", "LIST FULL").</summary>
    public static bool Add(string? code, string? name, out string msg, bool select = true)
    {
        var st = Main.I.Settings;
        int idx = AddTo(st, code, name, out msg, out _);
        if (idx < 0) return false;
        Revision++;
        if (select && IndexOf(All, e => e.Source == CrosshairSource.Saved && e.Index == idx) is var at and >= 0) Select(at);
        else st.Save();
        return true;
    }

    /// <summary><see cref="Add"/> on any settings object: the saved index (new or the existing duplicate), -1 = not added.</summary>
    internal static int AddTo(AppSettings st, string? code, string? name, out string msg, out bool added)
    {
        added = false;
        code = (code ?? "").Trim();
        if (CrosshairCode.Decode(code) is not { } s) { msg = L.N("NOT A CODE"); return -1; }
        string canon = Canonical(s);
        var list = st.SavedCrosshairs;
        if (list != null)
            for (int j = 0; j < list.Count; j++)
                if (CrosshairCode.Decode(list[j].Code) is { } o && Canonical(o) == canon) { msg = L.N("ALREADY SAVED ✓"); return j; }
        if ((list?.Count ?? 0) >= MaxSaved) { msg = L.N("LIST FULL"); return -1; }
        list = st.SavedCrosshairs ??= new List<SavedCrosshair>();
        string nm = CleanName(name) ?? NameInCode(s) ?? NextName(list);
        list.Add(new SavedCrosshair { Name = nm, Code = code });
        msg = L.N("SAVED ✓");
        added = true;
        return list.Count - 1;
    }

    /// <summary>The code VALORANT would export for the crosshair, without the community NAME section (two codes of the same
    /// crosshair with different names are the same crosshair).</summary>
    internal static string Canonical(CrosshairSettings s)
    {
        var x = s.Extras.Clone();
        x.UnknownSections.RemoveAll(u => u.Marker == "NAME");
        try { return CrosshairCode.Encode(s, x); } catch { return "?"; }
    }

    static string? NameInCode(CrosshairSettings s) =>
        s.Extras.UnknownSections.FirstOrDefault(u => u.Marker == "NAME").Tokens is { Count: > 0 } t ? CleanName(t[0].Trim().Trim('"')) : null;

    static string NextName(List<SavedCrosshair> list)
    {
        for (int n = list.Count + 1; ; n++)
            if (!list.Any(x => string.Equals(x.Name, $"Code {n}", StringComparison.OrdinalIgnoreCase))) return $"Code {n}";
    }

    /// <summary>Trimmed, one line, at most <see cref="MaxName"/> characters; null when empty.</summary>
    public static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = new string(name.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (n.Length > MaxName) n = n[..MaxName].TrimEnd();
        return n.Length == 0 ? null : n;
    }

    /// <summary>SAVE CURRENT: a copy of the crosshair drawn now in the saved list, named after it ("Main (VALORANT)"), so a
    /// VALORANT profile stays usable on another account or after VALORANT moves it online. The pick doesn't change.</summary>
    public static bool SaveCurrent(out string msg)
    {
        var e = CurrentEntry;
        if (e.Source == CrosshairSource.Saved) { msg = L.N("ALREADY SAVED ✓"); return true; }
        string name = e.Source == CrosshairSource.Valorant ? $"{e.Name} (VALORANT)" : e.Name;
        return Add(CrosshairCode.EffectiveCode, name, out msg, select: false);
    }

    public static void Rename(int savedIdx, string? name)
    {
        var sv = Main.I.Settings.SavedCrosshairs;
        if (sv == null || savedIdx < 0 || savedIdx >= sv.Count || CleanName(name) is not { } n || sv[savedIdx].Name == n) return;
        sv[savedIdx].Name = n;
        Changed();
    }

    /// <summary>Deletes a saved code; if it was the pick, VALORANT's active profile is drawn again.</summary>
    public static void Remove(int savedIdx)
    {
        var st = Main.I.Settings;
        var sv = st.SavedCrosshairs;
        if (sv == null || savedIdx < 0 || savedIdx >= sv.Count) return;
        if (st.CrosshairChoice is { Kind: "saved" } c && c.Code?.Trim() == sv[savedIdx].Code.Trim()) st.CrosshairChoice = null;
        sv.RemoveAt(savedIdx);
        if (sv.Count == 0) st.SavedCrosshairs = null; // settings.json stays as before the feature
        Changed();
    }

    /// <summary>Moves a saved code up (-1) or down (+1) in the cycle order.</summary>
    public static void Move(int savedIdx, int d)
    {
        var sv = Main.I.Settings.SavedCrosshairs;
        int to = savedIdx + d;
        if (sv == null || savedIdx < 0 || savedIdx >= sv.Count || to < 0 || to >= sv.Count) return;
        (sv[savedIdx], sv[to]) = (sv[to], sv[savedIdx]);
        Changed();
    }

    static void Changed()
    {
        Revision++;
        Main.I.Settings.Save();
    }

    /// <summary>For bug reports: "VALORANT active", "VALORANT profile 2/4", "saved code 3/7", "custom code (primary tab)".</summary>
    public static string SourceText()
    {
        var e = CurrentEntry;
        var st = Main.I.Settings;
        int nv = All.Count(x => x.Source == CrosshairSource.Valorant), ns = All.Count(x => x.Source == CrosshairSource.Saved);
        return e.Source switch
        {
            CrosshairSource.Custom => "custom code" + (st.FinderCrosshairTab != null ? $" ({st.FinderCrosshairTab} tab)" : ""),
            CrosshairSource.Saved => $"saved code {All.Take(e.Position + 1).Count(x => x.Source == CrosshairSource.Saved)}/{ns}",
            _ => st.CrosshairChoice == null ? "VALORANT active" : $"VALORANT profile {e.Position + 1}/{nv}",
        };
    }
}
