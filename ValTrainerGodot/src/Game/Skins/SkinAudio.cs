using Godot;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game.Skins;

/// <summary>
/// Skin sounds, skins 3 (docs/research/skins3-contracts.md; files from <c>tools/sfx/skin_sfx.py</c>: CC0 recordings +
/// processing, no AI audio). A line with <see cref="SkinFeature.Sounds"/> REPLACES the gun's fire: the factory shot's own
/// transient and low body (the "core", <c>_core/&lt;factory file&gt;.wav</c>) plus the skin's mid band and tail
/// (<c>fire_&lt;class&gt;_1..5</c>, round robin), both on Master at the factory shot's gain, so a shot is exactly as loud
/// and lands exactly when the factory one would (the generator measures every gun of the class to ±1 LU). Body / headshot
/// hits (<see cref="SkinFeature.HitSounds"/>) replace the factory hit sounds unless Settings → Classic hit sounds is on;
/// kill sounds 1–5 replace the factory ones at their length and loudness. Everything else (reload layer, equip, inspect,
/// the special reload, kill effect, finisher, counter) is a quieter layer on the <see cref="Sfx.SkinBus"/> bus.
/// <para><b>Contract.</b> Every method is a no-op (or returns false = "play the factory sound") when <paramref name="s"/> is
/// null, the state lacks the feature, <see cref="Enabled"/> is false or nothing is loaded. Main thread only.</para>
/// <para><b>Files</b> (<c>assets/audio/skins/&lt;SkinState.Sounds&gt;/</c>, .ogg unless noted): fire_&lt;class&gt;_1..5,
/// reload_&lt;key&gt;, equip, inspect, hit_body_1..5.wav, hit_head_1..5.wav, kill1..kill5, kill; Apex: reload_special_a,
/// reload_special_b (its seat hit <see cref="ReloadSpecialHit"/> s into the file), fin, counter.</para>
/// </summary>
public static class SkinAudio
{
    /// <summary>Settings → Skin sounds (fire, hits, kills 1–5, layers). Kill-effect / finisher sounds follow their own toggles too.</summary>
    public static bool Enabled => Main.I?.Settings?.SkinSounds ?? true;

    /// <summary>Settings → Classic hit sounds: the factory body / headshot sounds even on a skin with its own.</summary>
    public static bool ClassicHits => Main.I?.Settings?.ClassicHitSounds ?? false;

    /// <summary>Skin bus volume (× master volume), from <see cref="AppSettings.SkinFxVolume"/>.</summary>
    public static float Volume => Main.I?.Settings?.SkinFxVolume ?? 0.8f;

    /// <summary>The gains of the factory calls (GameSession shot 0.55 × look volume, reload 0.6; TrainingMode kill 0.9).</summary>
    public const float ShotGain = 0.55f, BoltGain = 0.7f, ReloadGain = 0.6f, KillGain = 0.9f;

    /// <summary>reload_special_b: the seat / lock hit, seconds into the file (skin_sfx.py RS_B_HIT).</summary>
    public const float ReloadSpecialHit = 0.30f;

    /// <summary>Fire classes (files <c>fire_&lt;class&gt;_n</c>), as in skin_sfx.py's CLASSES table.</summary>
    public static readonly string[] Classes = { "sidearm", "smg", "suppressed", "shotgun", "rifle", "sniper", "heavy" };
    /// <summary>Reload-layer keys (file <c>reload_&lt;key&gt;</c>): the factory <see cref="WeaponLook.ReloadSound"/> keys.</summary>
    public static readonly string[] ReloadKeys = { "rifle", "pistol", "shotgun", "revolver", "lmg" };

    const int Variants = 5;
    const string Root = "res://assets/audio/skins/";

    /// <summary>Every sound of a set: name → its files (a round-robin group for fire and hits).</summary>
    static readonly (string Name, string[] Files)[] AllNames =
        Classes.Select(c => ("fire_" + c, Enumerable.Range(1, Variants).Select(i => $"fire_{c}_{i}").ToArray()))
        .Concat(ReloadKeys.Select(k => ("reload_" + k, new[] { "reload_" + k })))
        .Concat(new[] { "equip", "inspect", "kill", "kill1", "kill2", "kill3", "kill4", "kill5", "reload_special_a", "reload_special_b", "fin", "counter" }.Select(n => (n, new[] { n })))
        .Concat(new[] { "hit_body", "hit_head" }.Select(h => (h, Enumerable.Range(1, Variants).Select(i => $"{h}_{i}").ToArray())))
        .ToArray();

    /// <summary>Sound sets (and core groups) already registered with <see cref="Sfx"/>.</summary>
    static readonly HashSet<string> loaded = new();

    /// <summary>Skin voices started so far (the dev test counts them).</summary>
    internal static int Started { get; private set; }

    /// <summary>A gun's fire class. The Frenzy (a machine pistol) shares the SMG sound; unknown guns use the rifle's.</summary>
    public static string ClassOf(WeaponKind gun) => gun switch
    {
        WeaponKind.Classic or WeaponKind.Bandit or WeaponKind.Sheriff or WeaponKind.Headhunter => "sidearm",
        WeaponKind.Frenzy or WeaponKind.Stinger => "smg",
        WeaponKind.Ghost or WeaponKind.Spectre => "suppressed",
        WeaponKind.Shorty or WeaponKind.Bucky or WeaponKind.Judge => "shotgun",
        WeaponKind.Marshal or WeaponKind.Outlaw or WeaponKind.Operator or WeaponKind.TourDeForce => "sniper",
        WeaponKind.Ares or WeaponKind.Odin => "heavy",
        _ => "rifle",
    };

    /// <summary>The sounds a state needs for its features (the data check and the dev test use it).</summary>
    public static IEnumerable<string> Required(SkinState s)
    {
        if (s.Has(SkinFeature.Sounds))
            foreach (var (n, _) in AllNames.Where(a => a.Name.StartsWith("fire_") || a.Name.StartsWith("reload_") && !a.Name.StartsWith("reload_special") || a.Name == "equip"))
                yield return n;
        if (s.Has(SkinFeature.KillSounds)) for (int k = 1; k <= 5; k++) yield return "kill" + k;
        if (s.Has(SkinFeature.HitSounds)) { yield return "hit_body"; yield return "hit_head"; }
        if (s.Has(SkinFeature.KillFx)) yield return "kill";
        if (s.Has(SkinFeature.Inspect)) yield return "inspect";
        if (s.Has(SkinFeature.ReloadAnim)) { yield return "reload_special_a"; yield return "reload_special_b"; }
        if (s.Has(SkinFeature.Finisher)) yield return "fin";
        if (s.Has(SkinFeature.KillCounter)) yield return "counter";
    }

    /// <summary>The res:// path of one file of a sound set (.ogg, or .wav for the hits).</summary>
    public static string PathOf(string set, string file) => $"{Root}{set}/{file}{(file.StartsWith("hit_") ? ".wav" : ".ogg")}";

    static string Key(string set, string name) => $"skin.{set}.{name}";

    /// <summary>The files behind one sound of <see cref="Required"/> (a round-robin group has several).</summary>
    public static IEnumerable<string> FilesOf(string name) => AllNames.FirstOrDefault(x => x.Name == name).Files ?? new[] { name };

    /// <summary>Loads (lazily, once) the files of <paramref name="s"/>'s sound set. Call at Equip (before the countdown) and in
    /// the picker when a line is shown, so the first shot never hitches.</summary>
    public static void Preload(SkinState? s)
    {
        if (s?.Sounds is { } set) Load(set);
    }

    static void Load(string set)
    {
        if (Sfx.I is not { } sfx || loaded.Contains(set)) return;
        loaded.Add(set);
        int n = 0;
        foreach (var (name, files) in AllNames)
        {
            var streams = files.Select(f => PathOf(set, f)).Where(p => ResourceLoader.Exists(p)).Select(p => GD.Load<AudioStream>(p)).Where(x => x != null).ToArray();
            if (streams.Length == 0) continue;
            sfx.Register(Key(set, name), streams);
            n++;
        }
        if (n == 0) Log.Error($"skin sound set '{set}' has no files under {Root}{set}/");
    }

    /// <summary>The factory shot files of an Sfx shot key (Sfx.cs Rec): the core files carry the same names.</summary>
    static string CoreStem(string shotKey) => shotKey switch
    {
        "vandal" => "rifle_ak", "phantom" => "rifle_ar", "operator" => "sniper_bolt", "classic" => "pistol", _ => shotKey,
    };

    /// <summary>The core group of a factory shot key (registered once), or null when the generator made none for it.</summary>
    static string? Core(string shotKey)
    {
        string key = "skin.core." + shotKey;
        if (Sfx.I is not { } sfx) return null;
        if (!loaded.Contains(key))
        {
            loaded.Add(key);
            var streams = Enumerable.Range(1, 3).Select(i => $"{Root}_core/{CoreStem(shotKey)}_shot_{i:00}.wav").Where(p => ResourceLoader.Exists(p))
                .Select(p => GD.Load<AudioStream>(p)).Where(x => x != null).ToArray();
            if (streams.Length > 0) sfx.Register(key, streams);
        }
        return sfx.IsRegistered(key) ? key : null;
    }

    /// <summary>The set to play from when <paramref name="s"/> has <paramref name="f"/> and skin sounds are on.</summary>
    static string? SetFor(SkinState? s, SkinFeature f) => s is { } st && Enabled && st.Has(f) ? st.Sounds : null;

    static bool Play(string set, string name, float vol, float pitch = 1f, bool skinBus = true)
    {
        if (Sfx.I is not { } sfx) return false;
        Load(set);
        if (sfx.PlaySkin(Key(set, name), vol, pitch, skinBus) == null) return false;
        Started++;
        return true;
    }

    /// <summary>One shot with a skin's fire (<see cref="SkinFeature.Sounds"/>): the factory shot's core plus the skin's fire,
    /// on Master at <paramref name="vol"/> (the factory call's gain) and <paramref name="pitch"/>. Returns false when the
    /// caller must play the factory shot (no feature, sounds off, files missing).</summary>
    public static bool PlayFire(SkinState? s, WeaponKind gun, string shotKey, float vol, float pitch)
    {
        if (SetFor(s, SkinFeature.Sounds) is not { } set || Sfx.I is not { } sfx || Core(shotKey) is not { } core) return false;
        Load(set);
        if (!sfx.IsRegistered(Key(set, "fire_" + ClassOf(gun)))) return false;
        sfx.Play(core, vol, pitch);
        Play(set, "fire_" + ClassOf(gun), vol, pitch, skinBus: false);
        return true;
    }

    /// <summary>The fire sound for the skin picker's preview (core + skin, like <see cref="PlayFire"/>).</summary>
    public static void PlayFireLayer(SkinState? s, WeaponKind gun, float pitch) =>
        PlayFire(s, gun, WeaponLooks.For(gun).ShotSound, ShotGain * WeaponLooks.For(gun).Volume, pitch);

    /// <summary>Skins 3 sets have no bolt / pump layer: the factory bolt plays alone. Kept for the callers.</summary>
    public static void PlayBoltLayer(SkinState? s, WeaponKind gun) { }

    /// <summary>The reload layer on the reload's rising edge (factory <c>ReloadPitch</c>).</summary>
    public static void PlayReloadLayer(SkinState? s, WeaponKind gun, float pitch)
    {
        if (SetFor(s, SkinFeature.Sounds) is not { } set) return;
        string key = WeaponLooks.For(gun).ReloadSound;   // "reload_rifle" … = the file name
        if (key.StartsWith("reload_")) Play(set, key, ReloadGain, pitch);
    }

    /// <summary>The equip flourish sound (the factory equip is silent).</summary>
    public static void PlayEquip(SkinState? s, WeaponKind gun)
    {
        if (SetFor(s, SkinFeature.Sounds) is { } set) Play(set, "equip", ShotGain);
    }

    /// <summary>Kill sound <paramref name="n"/> (1..5, the streak). True when the skin played its own escalating sound
    /// (<see cref="SkinFeature.KillSounds"/>); false = the caller plays the factory <c>"kill" + n</c>. On Master at
    /// <paramref name="vol"/> like the factory kill (same length and loudness, skin_sfx.py), not on the ducked Skin bus.</summary>
    public static bool PlayKillSound(SkinState? s, int n, float vol = 1f) =>
        SetFor(s, SkinFeature.KillSounds) is { } set && Play(set, "kill" + Math.Clamp(n, 1, 5), vol, 1f, skinBus: false);

    /// <summary>The kill effect's sound (<see cref="SkinFeature.KillFx"/>). Off with Settings → Kill effects off.</summary>
    public static void PlayKillEffect(SkinState? s)
    {
        if ((Main.I?.Settings?.SkinKillFx ?? 2) > 0 && SetFor(s, SkinFeature.KillFx) is { } set) Play(set, "kill", KillGain);
    }

    /// <summary>The finisher's sound. Off with Settings → Finishers off. Skins 3 has no lite finisher (VALORANT's rule: only
    /// the kill that wins the round), so <paramref name="lite"/> plays nothing.</summary>
    public static void PlayFinisher(SkinState? s, bool lite)
    {
        if (!lite && (Main.I?.Settings?.SkinFinishers ?? true) && SetFor(s, SkinFeature.Finisher) is { } set) Play(set, "fin", KillGain);
    }

    /// <summary>The kill counter's tick on each increment, very quiet.</summary>
    public static void PlayCounterTick(SkinState? s)
    {
        if ((Main.I?.Settings?.SkinKillCounter ?? true) && SetFor(s, SkinFeature.KillCounter) is { } set) Play(set, "counter", KillGain);
    }

    /// <summary>The inspect flourish's sound (<see cref="SkinFeature.Inspect"/>), at the inspect's start.</summary>
    public static void PlayInspect(SkinState? s, WeaponKind gun)
    {
        if (SetFor(s, SkinFeature.Inspect) is { } set) Play(set, "inspect", 1f);
    }

    /// <summary>The special reload's sound (<see cref="SkinFeature.ReloadAnim"/>), instead of the reload layer: the start
    /// now, and the seat hit timed to land at the end of the factory reload (<paramref name="duration"/>, s).</summary>
    public static void PlayReloadAnim(SkinState? s, WeaponKind gun, float duration)
    {
        if (SetFor(s, SkinFeature.ReloadAnim) is not { } set) return;
        Play(set, "reload_special_a", ReloadGain);
        float wait = Mathf.Max(0f, duration - ReloadSpecialHit);
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.CreateTimer(wait).Timeout += () => { if (Enabled) Play(set, "reload_special_b", ReloadGain); };
    }

    /// <summary>The line's body-hit / headshot sound instead of the factory one (<see cref="SkinFeature.HitSounds"/>, off
    /// with Classic hit sounds), on Master at <paramref name="vol"/>. False = play the factory sound.</summary>
    public static bool PlayHit(SkinState? s, bool headshot, float vol = 1f, float pitch = 1f) =>
        !ClassicHits && SetFor(s, SkinFeature.HitSounds) is { } set && Play(set, headshot ? "hit_head" : "hit_body", vol, pitch, skinBus: false);

    /// <summary>Kept for the skins-3 contract callers (<c>PlayHit(state, headshot, kill)</c>).</summary>
    public static bool PlayHit(SkinState? s, bool headshot, bool kill) => PlayHit(s, headshot, headshot ? 1f : 0.8f);

    /// <summary>Stops every skin voice (run end, picker closed).</summary>
    public static void StopAll() => Sfx.I?.StopSkin();

    // ---------------- dev: --skinaudio-test ----------------

    /// <summary>Dev <c>--skinaudio-test</c> (from <see cref="Sfx"/>): loads every line's sound set at its top level and plays
    /// all of it through this API (audible in a windowed run), then checks the voice pool, the feature and settings gating,
    /// the bus volume and the ducking. Prints <c>[skinaudio-test] RESULT: PASS|FAIL</c> and quits (exit code = failures).
    /// Settings are changed in memory only and restored; nothing is saved.</summary>
    internal static async void RunTest(Node host)
    {
        int pass = 0, fail = 0;
        void Check(string what, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            GD.Print($"[skinaudio-test] {(ok ? "PASS" : "FAIL")} {what}{(detail != "" ? " (" + detail + ")" : "")}");
        }
        async Task Wait(double secs) => await host.ToSignal(host.GetTree().CreateTimer(secs), SceneTreeTimer.SignalName.Timeout);
        var sfx = Sfx.I;
        var set = Main.I?.Settings;
        if (sfx == null || set == null) { GD.Print("[skinaudio-test] RESULT: FAIL (no Sfx / settings)"); host.GetTree().Quit(1); return; }
        bool keepClassic = set.ClassicHitSounds;
        bool keepSounds = set.SkinSounds, keepFin = set.SkinFinishers, keepCounter = set.SkinKillCounter;
        int keepFx = set.SkinKillFx;
        float keepVol = set.SkinFxVolume;
        try
        {
            set.ClassicHitSounds = false;
            set.SkinSounds = true; set.SkinKillFx = 2; set.SkinFinishers = true; set.SkinKillCounter = true; set.SkinFxVolume = 0.8f;
            await Wait(0.3);
            var reps = new[] { WeaponKind.Classic, WeaponKind.Frenzy, WeaponKind.Ghost, WeaponKind.Judge, WeaponKind.Vandal, WeaponKind.Operator, WeaponKind.Odin };
            var reloads = new[] { WeaponKind.Vandal, WeaponKind.Classic, WeaponKind.Judge, WeaponKind.Sheriff, WeaponKind.Odin };
            Check("every gun has a fire class with a file name", Enum.GetValues<WeaponKind>().All(g => Classes.Contains(ClassOf(g))));
            Check("every gun's factory shot has a core (the skin fire replaces the shot)", Enum.GetValues<WeaponKind>().Where(GunSkins.Skinnable).All(g => Core(WeaponLooks.For(g).ShotSound) != null),
                string.Join(",", Enum.GetValues<WeaponKind>().Where(GunSkins.Skinnable).Where(g => Core(WeaponLooks.For(g).ShotSound) == null)));
            Check("every gun's reload key has a layer file name", Enum.GetValues<WeaponKind>().All(g => ReloadKeys.Contains(WeaponLooks.For(g).ReloadSound.Replace("reload_", ""))));
            int lines = 0;
            foreach (var line in SkinCatalog.Lines)
            {
                var top = new SkinState(line, line.MaxLevel, null);
                if (top.Sounds is not { } id) { Check($"{line.Id}: no sound set, plays nothing", Silent(top)); continue; }
                lines++;
                Preload(top);
                var need = Required(top).ToList();
                var missing = need.Where(n => !sfx.IsRegistered(Key(id, n))).ToList();
                Check($"{line.Id}: {need.Count} files of set '{id}' load", missing.Count == 0, string.Join(",", missing));
                var low = new SkinState(line, 1, null);
                if (low.Features == SkinFeature.None) // Apex L1 (a Signature line's one level already has its sounds)
                    Check($"{line.Id} L1: no feature, plays nothing, factory kill sounds", Silent(low));

                int before = Started, want = 0;
                GD.Print($"[skinaudio-test] {line.Id}: playing its set");
                if (top.Has(SkinFeature.Sounds))
                {
                    PlayEquip(top, WeaponKind.Vandal); want++;
                    await Wait(0.45);
                    foreach (var g in reps) { PlayFireLayer(top, g, 1f); want++; await Wait(0.12); }
                    foreach (var g in reloads) { PlayReloadLayer(top, g, WeaponLooks.For(g).ReloadPitch); want++; await Wait(0.5); }
                    StopAll();
                }
                bool killsOk = true;
                for (int k = 1; k <= 5; k++)
                {
                    bool own = PlayKillSound(top, k, KillGain);
                    killsOk &= own == top.Has(SkinFeature.KillSounds);
                    if (own) want++;
                    await Wait(0.3);
                }
                Check($"{line.Id}: kill sounds 1-5 {(top.Has(SkinFeature.KillSounds) ? "replace" : "leave")} the factory ones", killsOk);
                if (top.Has(SkinFeature.KillFx)) { PlayKillEffect(top); want++; await Wait(0.6); }
                if (top.Has(SkinFeature.HitSounds) && !ClassicHits)
                    foreach (bool hd in new[] { false, true, false, true }) { if (PlayHit(top, hd, 0.8f)) want++; await Wait(0.25); }
                if (top.Has(SkinFeature.Inspect)) { PlayInspect(top, WeaponKind.Vandal); want++; await Wait(0.8); }
                if (top.Has(SkinFeature.ReloadAnim)) { PlayReloadAnim(top, WeaponKind.Vandal, 0.5f); want += 2; await Wait(0.9); }
                PlayFinisher(top, true); // no lite finisher in skins 3: nothing
                if (top.Has(SkinFeature.Finisher)) { PlayFinisher(top, false); want++; await Wait(2.1); }
                if (top.Has(SkinFeature.KillCounter)) { PlayCounterTick(top); want++; await Wait(0.1); }
                Check($"{line.Id}: every call started a skin voice", Started - before == want, $"{Started - before} of {want}");
            }
            Check("10 lines have sound sets (7 Signature + 3 Apex)", lines == 10, lines.ToString());

            // the pool: a burst never grows past its voices and never touches the gameplay voices
            var ph = SkinCatalog.Get("foundry")!;
            var p5 = new SkinState(ph, ph.MaxLevel, null);
            for (int i = 0; i < 30; i++) PlayFireLayer(p5, WeaponKind.Odin, 1f);
            PlayFinisher(p5, false);
            var (playing, gameplayOnSkin) = sfx.SkinPoolInfo();
            Check($"burst of 31: at most {Sfx.SkinVoices} skin voices, gameplay voices untouched", playing is > 0 and <= Sfx.SkinVoices && gameplayOnSkin == 0, $"{playing} playing");
            StopAll();
            await Wait(0.05);
            Check("StopAll silences the pool", sfx.SkinPoolInfo().Playing == 0);

            // settings gating
            set.SkinSounds = false;
            int b0 = Started;
            PlayFireLayer(p5, WeaponKind.Vandal, 1f); PlayEquip(p5, WeaponKind.Vandal); PlayReloadLayer(p5, WeaponKind.Vandal, 1f);
            bool k1 = PlayKillSound(p5, 1); PlayKillEffect(p5); PlayFinisher(p5, false); PlayCounterTick(p5);
            bool f1 = PlayFire(p5, WeaponKind.Vandal, "vandal", ShotGain, 1f), h1 = PlayHit(p5, true, 1f);
            Check("Skin sounds off: nothing plays, factory shot / hits / kill sounds", Started == b0 && !k1 && !f1 && !h1);
            set.SkinSounds = true; set.ClassicHitSounds = true;
            Check("Classic hit sounds: the factory hit sounds play", !PlayHit(p5, true, 1f) && !PlayHit(p5, false, 1f));
            set.ClassicHitSounds = false; set.SkinSounds = false;
            set.SkinSounds = true; set.SkinKillFx = 0; set.SkinFinishers = false; set.SkinKillCounter = false;
            PlayKillEffect(p5); PlayFinisher(p5, true); PlayCounterTick(p5);
            Check("kill effects / finishers / counter off: their sounds don't play", Started == b0);
            set.SkinKillFx = 2; set.SkinFinishers = true; set.SkinKillCounter = true;
            StopAll();

            // bus volume follows SkinFxVolume; gameplay cues duck it 8 dB for 0.35 s
            set.SkinFxVolume = 0.5f;
            await Wait(0.6);
            float baseDb = sfx.SkinBusDb;
            Check("Skin bus volume = SkinFxVolume", Mathf.Abs(baseDb - Mathf.LinearToDb(0.5f)) < 0.3f, $"{baseDb:0.0} dB");
            sfx.Play("damage", 0.05f);
            await Wait(0.12);
            float ducked = sfx.SkinBusDb;
            Check("a gameplay cue (damage) ducks the Skin bus", ducked <= baseDb - 6f, $"{ducked:0.0} dB vs {baseDb:0.0}");
            await Wait(0.8);
            float back = sfx.SkinBusDb;
            Check("the duck releases after 0.35 s", MathF.Abs(back - baseDb) < 0.5f, $"{back:0.0} dB");
            sfx.Play("head", 0.05f);
            await Wait(0.12);
            Check("the player's own hit sounds don't duck", MathF.Abs(sfx.SkinBusDb - baseDb) < 0.5f, $"{sfx.SkinBusDb:0.0} dB");
            set.SkinFxVolume = 0f;
            await Wait(0.3);
            Check("SkinFxVolume 0 mutes the bus", sfx.SkinBusDb <= -60f, $"{sfx.SkinBusDb:0.0} dB");
        }
        catch (Exception e) { Check("no exception", false, e.ToString()); }
        finally
        {
            set.ClassicHitSounds = keepClassic;
            set.SkinSounds = keepSounds; set.SkinKillFx = keepFx; set.SkinFinishers = keepFin; set.SkinKillCounter = keepCounter; set.SkinFxVolume = keepVol;
            StopAll();
        }
        GD.Print($"[skinaudio-test] RESULT: {(fail == 0 ? "PASS" : "FAIL")} ({pass} passed, {fail} failed)");
        host.GetTree().Quit(fail);
    }

    /// <summary>Whether every call is a no-op for <paramref name="s"/> (no sound features).</summary>
    static bool Silent(SkinState s)
    {
        int b = Started;
        PlayFireLayer(s, WeaponKind.Vandal, 1f); PlayBoltLayer(s, WeaponKind.Operator); PlayReloadLayer(s, WeaponKind.Vandal, 1f); PlayEquip(s, WeaponKind.Vandal);
        bool k = PlayKillSound(s, 3); PlayKillEffect(s); PlayFinisher(s, false); PlayCounterTick(s);
        bool f = PlayFire(s, WeaponKind.Vandal, "vandal", ShotGain, 1f), h = PlayHit(s, true, 1f);
        PlayInspect(s, WeaponKind.Vandal); PlayReloadAnim(s, WeaponKind.Vandal, 1f);
        return Started == b && !k && !f && !h;
    }
}
