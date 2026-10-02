using System.Globalization;
using Godot;
using ValTrainer.Analysis;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// Rank names for the coach's continuous tier (spec B): Iron &lt; 0 ≤ Bronze &lt; 0.5 ≤ Silver &lt; 1 ≤ Gold &lt; 1.5 ≤
/// Platinum &lt; 2 ≤ Diamond &lt; 2.5 ≤ Ascendant &lt; 3 ≤ Immortal &lt; 3.5 ≤ Radiant. Each rank band (0.5 wide) is split
/// into three divisions. Tiers below -0.5 (the engine's -1) mean "not rated".
/// </summary>
public static class CoachRank
{
    public static readonly string[] Names = { "Iron", "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Ascendant", "Immortal", "Radiant" };
    public static readonly string[] Short = { "IRON", "BRONZE", "SILVER", "GOLD", "PLAT", "DIAMOND", "ASCENDANT", "IMMORTAL", "RADIANT" };
    /// <summary>Ladder labels for narrow ladders (small windows), where the short names would run together.</summary>
    public static readonly string[] Tiny = { "IRON", "BRNZ", "SILV", "GOLD", "PLAT", "DIA", "ASC", "IMM", "RAD" };

    /// <summary>Generic rank palette (no Riot art).</summary>
    public static readonly Color[] Colors =
    {
        Color.Color8(150, 150, 146), // Iron
        Color.Color8(196, 136, 86),  // Bronze
        Color.Color8(200, 210, 218), // Silver
        Color.Color8(238, 194, 86),  // Gold
        Color.Color8(74, 198, 208),  // Platinum
        Color.Color8(196, 142, 250), // Diamond
        Color.Color8(76, 210, 132),  // Ascendant
        Color.Color8(240, 82, 108),  // Immortal
        Color.Color8(255, 232, 150), // Radiant
    };

    /// <summary>Axis of the rank ladder: Iron starts at -0.5, Radiant drawn as one more 0.5 band.</summary>
    public const float Min = -0.5f, Max = 4.0f;

    public static bool Rated(float tier) => !float.IsNaN(tier) && tier >= -0.5f - 1e-3f;
    public static bool Rated(SkillRating? r) => r != null && Rated(r.Tier) && r.Confidence > 0;

    public static int Index(float t) => t < 0 ? 0 : t >= 3.5f ? 8 : Math.Clamp(1 + (int)(t / 0.5f), 1, 7);

    public static int Division(float t)
    {
        int i = Index(t);
        if (i == 8) return 0;
        float lo = i == 0 ? -0.5f : (i - 1) * 0.5f;
        return Math.Clamp(1 + (int)((t - lo) / (0.5f / 3f)), 1, 3);
    }

    public static string Name(float t)
    {
        int i = Index(t);
        return i == 8 ? "RADIANT" : $"{Names[i].ToUpperInvariant()} {Division(t)}";
    }

    public static string Title(float t) => Rated(t) ? Name(t) : "NOT RATED";

    /// <summary>Uncertainty half-width in tier units (wider for low confidence).</summary>
    public static float Spread(float conf) => 0.06f + 0.25f * (1f - Mathf.Clamp(conf, 0, 1));

    /// <summary>"GOLD 2 – PLATINUM 1" (or one name when the band is narrow).</summary>
    public static (string Lo, string? Hi) Range(float t, float conf)
    {
        float u = Spread(conf);
        string lo = Name(Mathf.Max(t - u, -0.5f)), hi = Name(Mathf.Min(t + u, 4.5f));
        return lo == hi ? (lo, null) : (lo, hi);
    }

    public static string RangeText(float t, float conf)
    {
        var (lo, hi) = Range(t, conf);
        return hi == null ? lo : $"{lo} – {hi}";
    }

    public static Color Col(float t) => Rated(t) ? Colors[Index(t)] : UiTheme.NoTier;
    public static Color Col(string name)
    {
        for (int i = 0; i < Names.Length; i++) if (name.StartsWith(Names[i], StringComparison.OrdinalIgnoreCase)) return Colors[i];
        return UiTheme.NoTier;
    }

    /// <summary>The trainer's own emblem for the matching tier band (Rookie = Iron–Bronze … Pro = Radiant).</summary>
    public static int EmblemTier(float t) => Rated(t) ? Math.Clamp((int)MathF.Round(t, MidpointRounding.AwayFromZero), 0, 4) : -1;

    public static string Confidence(float c) => c < 0.5f ? "LOW" : c < 0.75f ? "MEDIUM" : "HIGH";
    public static int ConfidenceBars(float c) => c <= 0 ? 0 : c < 0.5f ? 1 : c < 0.75f ? 2 : 3;
    public static Color ConfidenceColor(float c) => c < 0.5f ? UiTheme.Warn : c < 0.75f ? UiTheme.Text : UiTheme.Good;
}

/// <summary>Display names, descriptions and rating drills of the coach's skills.</summary>
public static class CoachSkills
{
    public static string Name(Skill s) => s switch
    {
        Skill.Flicking => "FLICKING",
        Skill.Precision => "PRECISION",
        Skill.Tracking => "TRACKING",
        Skill.Reaction => "REACTION",
        Skill.CrosshairPlacement => "CROSSHAIR PLACEMENT",
        Skill.SprayControl => "SPRAY CONTROL",
        Skill.Movement => "MOVEMENT",
        Skill.Utility => "UTILITY",
        _ => s.ToString().ToUpperInvariant(),
    };

    public static string Short(Skill s) => s switch
    {
        Skill.Flicking => "FLICK",
        Skill.Precision => "PRECISION",
        Skill.CrosshairPlacement => "PLACEMENT",
        Skill.SprayControl => "SPRAY",
        _ => Name(s),
    };

    public static string About(Skill s) => s switch
    {
        Skill.Flicking => "The first big motion onto a target: how fast you start, how fast you get there and how close the flick lands.",
        Skill.Precision => "Micro-adjustment after the flick: first-shot accuracy, how long you need to settle and how many corrections you make.",
        Skill.Tracking => "Keeping the crosshair on a strafing target: time on target, lag behind it and how fast you re-acquire after a reversal.",
        Skill.Reaction => "Raw reaction time to a visual cue (Reaction Test).",
        Skill.CrosshairPlacement => "Where your crosshair already is when an enemy appears: distance to the head and how far below it you hold.",
        Skill.SprayControl => "Pulling down against recoil: how far bullets 4–15 drift vertically and sideways, and how early you start pulling.",
        Skill.Movement => "Stopping before you shoot: counter-strafe use, stop time and shots fired while moving.",
        Skill.Utility => "Dodging flashes: how fast you turn away from the audio cue and how often you still get blinded.",
        _ => "",
    };

    /// <summary>Drills that produce data for the skill (first = best).</summary>
    public static string[] Drills(Skill s) => s switch
    {
        Skill.Flicking => new[] { "flick", "spider", "gridshot" },
        Skill.Precision => new[] { "flick", "gridshot" },
        Skill.Tracking => new[] { "tracking", "strafebots" },
        Skill.Reaction => new[] { "reaction" },
        Skill.CrosshairPlacement => new[] { "peek", "siteclear" },
        Skill.SprayControl => new[] { "spray_vandal", "spray_phantom" },
        Skill.Movement => new[] { "counterstrafe", "peekduel" },
        Skill.Utility => new[] { "flashmap" },
        _ => Array.Empty<string>(),
    };

    public static Color SeverityColor(Severity s) => s switch
    {
        Severity.Major => UiTheme.Accent,
        Severity.Minor => UiTheme.Warn,
        _ => UiTheme.Teal,
    };

    /// <summary>The engine marks low-confidence findings as Tip, shown as "POSSIBLE".</summary>
    public static string SeverityName(Severity s) => s switch { Severity.Major => "MAJOR", Severity.Minor => "MINOR", _ => "POSSIBLE" };
}

/// <summary>
/// Loads the coach's data for the UI: the skill profile is built on a background thread (cached until the run
/// count, sensitivity or DPI change) and the post-run review comes from the session. Dev-only
/// <c>--dev --fakecoach [full|partial|empty]</c> swaps in hand-made data (<see cref="CoachFake"/>) for layout work.
/// </summary>
public static class CoachData
{
    static readonly string[] Args = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();

    /// <summary>null = real coach; otherwise "full", "partial" or "empty".</summary>
    public static readonly string? FakeVariant = ParseFake();
    public static bool Fake => FakeVariant != null;

    static string? ParseFake()
    {
        if (!Args.Contains("--dev")) return null;
        int i = Array.IndexOf(Args, "--fakecoach");
        if (i < 0) return null;
        string v = i + 1 < Args.Length && !Args[i + 1].StartsWith("--", StringComparison.Ordinal) ? Args[i + 1].ToLowerInvariant() : "full";
        return v is "empty" or "partial" ? v : "full";
    }

    // ---------------- profile (background) ----------------

    static Task<SkillProfile>? task;
    static string taskKey = "";
    public static string? LastError { get; private set; }

    static string Key()
    {
        var app = Main.I;
        return string.Create(CultureInfo.InvariantCulture, $"{app.Stats.Runs.Count}|{app.Sens:0.0000}|{app.Settings.Dpi}|{FakeVariant}");
    }

    /// <summary>Starts the analysis (or keeps the cached / running one if nothing changed). Main thread only.</summary>
    public static void Request()
    {
        string key = Key();
        if (task != null && key == taskKey) return;
        taskKey = key;
        var app = Main.I;
        // Snapshot so a run finishing meanwhile can't modify the list under the worker.
        var snapshot = new StatsStore { Runs = app.Stats.Runs.ToList() };
        float sens = app.Sens;
        int dpi = app.Settings.Dpi;
        string? fake = FakeVariant;
        task = Task.Run(() =>
        {
            try
            {
                if (fake != null)
                {
                    Thread.Sleep(300); // exercise the loading state
                    return CoachFake.Profile(fake, sens, dpi);
                }
                LastError = null;
                return Coach.BuildProfile(snapshot, sens, dpi) ?? new SkillProfile();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new SkillProfile();
            }
        });
    }

    /// <summary>The finished profile of the last <see cref="Request"/>, or null while it's still being built.</summary>
    public static SkillProfile? Ready => task is { IsCompleted: true } t ? t.Result : null;

    /// <summary>True when the profile has anything to show beyond the empty state.</summary>
    public static bool HasData(SkillProfile p) => p.Skills.Any(CoachRank.Rated) || p.Problems.Count > 0 || CoachRank.Rated(p.OverallTier);

    public static bool HasOverall(SkillProfile p) => CoachRank.Rated(p.OverallTier);

    // ---------------- run review ----------------

    public static RunReview? ReviewFor(GameSession s)
    {
        if (Fake) return CoachFake.Review(FakeVariant!, s.Mode.Key);
        return s.Review;
    }

    public static bool HasContent(RunReview? r) => r != null && (r.Summary.Length > 0 || r.Issues.Count > 0 || r.Strengths.Count > 0);

    // ---------------- drills ----------------

    static Dictionary<string, string>? names;

    static Dictionary<string, string> Names => names ??= ModeRegistry.All.Select(f => f())
        .GroupBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

    public static bool HasDrill(string key) => Names.ContainsKey(key);
    public static string DrillName(string key) => Names.TryGetValue(key, out var n) ? n : key;

    /// <summary>Starts a drill; the optional focus cue is shown during its countdown.</summary>
    public static void StartDrill(string key, Focus? focus = null)
    {
        var make = ModeRegistry.Find(key);
        if (make == null) return;
        pending = focus == null ? null : focus with { Mode = key };
        Main.I.StartMode(make);
    }

    /// <summary>The drills the empty state asks for, how many coached runs each needs and what they rate.</summary>
    public static readonly (string Key, int Target, string Rates)[] Starter =
    {
        ("flick", 2, "Flicking · Precision"),
        ("tracking", 2, "Tracking"),
        ("peek", 2, "Crosshair placement"),
        ("counterstrafe", 1, "Movement"),
        ("spray_vandal", 1, "Spray control"),
    };

    /// <summary>Runs of a drill that carry coach data (metrics or telemetry).</summary>
    public static int CoachedRuns(string key)
    {
        if (FakeVariant == "empty") return 0;
        if (FakeVariant == "partial") return key switch { "flick" => 3, "tracking" => 1, _ => 0 };
        if (Fake) return 3;
        return Main.I.Stats.Runs.Count(r => r.Mode == key && (r.Metrics is { Count: > 0 } || r.TelemetryFile != null));
    }

    public static int RunsNeeded() => Starter.Sum(s => Math.Max(0, s.Target - CoachedRuns(s.Key)));

    // ---------------- focus cue (shown during the next countdown) ----------------

    public sealed record Focus(string Mode, string Title, string Cue);
    static Focus? pending;

    public static Focus? FocusFrom(Diagnosis d) => new("", d.Title ?? "", d.Fixes?.FirstOrDefault() ?? "");

    /// <summary>Takes the pending focus cue for this drill (set by a "Practise" button).</summary>
    public static Focus? TakeFocus(string modeKey)
    {
        var p = pending;
        pending = null;
        if (p != null && string.Equals(p.Mode, modeKey, StringComparison.OrdinalIgnoreCase)) return p;
        if (FakeVariant == "full")
        {
            // Dev layout check: show the fake profile's top problem for this drill.
            var d = CoachFake.Profile("full", 0.21f, 800).Problems.FirstOrDefault(x => x.Drills.Contains(modeKey));
            if (d != null) return FocusFrom(d)! with { Mode = modeKey };
        }
        return null;
    }

    /// <summary>Saves settings unless this is a dev run on fake data (layout checks never touch the user's files).</summary>
    public static void SaveSettings()
    {
        if (!Fake) Main.I.Settings.Save();
    }
}

/// <summary>Drawing helpers shared by the coach screens.</summary>
public static class CoachDraw
{
    /// <summary>Three "signal" bars for confidence (Low 1 / Medium 2 / High 3). Returns the width used.</summary>
    public static float ConfBars(CanvasItem ci, Vector2 leftMid, float k, float conf, float alpha = 1f)
    {
        int n = CoachRank.ConfidenceBars(conf);
        var on = new Color(CoachRank.ConfidenceColor(conf), alpha);
        var off = new Color(UiTheme.Text, 0.16f * alpha);
        float bw = 4 * k, gap = 2.5f * k;
        for (int i = 0; i < 3; i++)
        {
            float h = (6 + i * 4) * k;
            ci.DrawRect(new Rect2(leftMid.X + i * (bw + gap), leftMid.Y + 7 * k - h, bw, h), i < n ? on : off);
        }
        return 3 * bw + 2 * gap;
    }

    /// <summary>Small outlined chip ("MAJOR", "POSSIBLE"). Returns its width.</summary>
    public static float Chip(CanvasItem ci, string text, float x, float cy, float k, Color c, bool filled = false)
    {
        int fs = UiTheme.Fs(11, k);
        float w = Gfx.TextW(UiTheme.HudWide, text, fs) + 12 * k;
        var r = new Rect2(x, cy - 9.5f * k, w, 19 * k);
        ci.DrawRect(r, filled ? c : new Color(c, 0.14f));
        if (!filled) ci.DrawRect(r, new Color(c, 0.75f), false, 1f);
        Gfx.Text(ci, UiTheme.HudWide, text, x + 6 * k, Gfx.Mid(cy, fs), fs, filled ? UiTheme.Bg : c);
        return w;
    }

    /// <summary>
    /// Iron → Radiant ladder with the tier marker, its uncertainty band and (optionally) labels under each rank.
    /// <paramref name="tier"/> &lt; -0.5 draws the empty ladder.
    /// </summary>
    public static void Ladder(CanvasItem ci, Rect2 r, float k, float tier, float conf, bool labels, float? reference = null)
    {
        int n = CoachRank.Names.Length;
        float gap = 2 * k, segW = (r.Size.X - gap * (n - 1)) / n;
        bool rated = CoachRank.Rated(tier);
        int cur = rated ? CoachRank.Index(tier) : -1;
        float X(float t) => r.Position.X + (Mathf.Clamp(t, CoachRank.Min, CoachRank.Max) - CoachRank.Min) / (CoachRank.Max - CoachRank.Min) * r.Size.X;
        int lfs = UiTheme.Fs(Gfx.TextW(UiTheme.Display, "ASCENDANT", UiTheme.Fs(13, k)) > segW - 2 * k ? 11.5f : 13f, k);
        // the longest name doesn't fit its segment (e.g. 1280×720 where fonts hit their size floor, or 16:10 where the
        // card is narrower): abbreviate the labels wider than their segment, else DIAMOND/ASCENDANT/IMMORTAL run together
        bool tight = Gfx.TextW(UiTheme.Display, "ASCENDANT", lfs) > segW;
        for (int i = 0; i < n; i++)
        {
            var c = CoachRank.Colors[i];
            var sr = new Rect2(r.Position.X + i * (segW + gap), r.Position.Y, segW, r.Size.Y);
            ci.DrawRect(sr, new Color(c, i == cur ? 0.55f : 0.16f));
            if (i == cur) ci.DrawRect(new Rect2(sr.Position.X, sr.End.Y - 2 * k, sr.Size.X, 2 * k), c);
            if (labels)
            {
                int fs = lfs;
                string label = tight && Gfx.TextW(UiTheme.Display, CoachRank.Short[i], fs) > segW ? CoachRank.Tiny[i] : CoachRank.Short[i];
                // labels may overhang into the gaps (neighbours of the longest name are short)
                Gfx.TextFit(ci, UiTheme.Display, label, sr.Position.X - 5 * k, sr.End.Y + 8 * k + fs * 0.75f, fs,
                    i == cur ? c : new Color(UiTheme.Text, 0.38f), sr.Size.X + 10 * k, HorizontalAlignment.Center);
            }
        }
        if (reference is { } refT && CoachRank.Rated(refT))
        {
            float rx = X(refT);
            ci.DrawRect(new Rect2(rx - 1 * k, r.Position.Y - 5 * k, 2 * k, r.Size.Y + 10 * k), new Color(UiTheme.Text, 0.45f));
        }
        if (!rated) return;
        float u = CoachRank.Spread(conf);
        float x0 = X(tier - u), x1 = X(tier + u), x = X(tier);
        ci.DrawRect(new Rect2(x0, r.Position.Y - 3 * k, Mathf.Max(2, x1 - x0), r.Size.Y + 6 * k), new Color(1, 1, 1, 0.13f));
        ci.DrawRect(new Rect2(x0, r.Position.Y - 3 * k, Mathf.Max(2, x1 - x0), r.Size.Y + 6 * k), new Color(1, 1, 1, 0.35f), false, 1f);
        ci.DrawRect(new Rect2(x - 1.5f * k, r.Position.Y - 7 * k, 3 * k, r.Size.Y + 14 * k), UiTheme.Text);
        Gfx.Diamond(ci, new Vector2(x, r.Position.Y - 9 * k), 6 * k, 6 * k, UiTheme.Text);
    }

    /// <summary>
    /// Spider chart of the 8 skills on the tier scale (-0.5 centre … 4.5 rim), with the overall tier as a dashed ring.
    /// </summary>
    public static void Radar(CanvasItem ci, Vector2 c, float R, float k, SkillProfile p)
    {
        var skills = Enum.GetValues<Skill>();
        int n = skills.Length;
        Vector2 Dir(int i) => Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Tau / n);
        float Rad(float t) => R * Mathf.Clamp((t + 0.5f) / 5f, 0, 1);
        Span<Vector2> ring = stackalloc Vector2[n + 1];

        // tier-group bands (Iron–Bronze, Silver–Gold, Plat–Dia, Asc–Imm, Radiant)
        for (int g = 4; g >= 0; g--)
        {
            float rr = Rad(0.5f + g);
            for (int i = 0; i <= n; i++) ring[i] = c + Dir(i % n) * rr;
            ci.FillPoly(ring[..n], g % 2 == 0 ? new Color(0.09f, 0.135f, 0.18f, 1f) : new Color(0.075f, 0.115f, 0.155f, 1f));
            ci.Polyline(ring, new Color(UiTheme.Text, g == 4 ? 0.22f : 0.08f), 1f, true);
        }
        for (int i = 0; i < n; i++) ci.DrawLine(c, c + Dir(i) * R, new Color(UiTheme.Text, 0.07f), 1f);

        // overall rank ring (dashed)
        if (CoachRank.Rated(p.OverallTier))
        {
            float rr = Rad(p.OverallTier);
            for (int i = 0; i < n; i++)
            {
                var a = c + Dir(i) * rr;
                var b = c + Dir((i + 1) % n) * rr;
                const int dashes = 6;
                for (int d = 0; d < dashes; d++)
                    ci.DrawLine(a.Lerp(b, d / (float)dashes), a.Lerp(b, (d + 0.5f) / dashes), new Color(UiTheme.Text, 0.55f), Mathf.Max(1, 1.5f * k), true);
            }
        }

        // data polygon
        Span<Vector2> pts = stackalloc Vector2[n + 1];
        var ratings = new SkillRating?[n];
        for (int i = 0; i < n; i++)
        {
            ratings[i] = p.Skills.FirstOrDefault(s => s.Skill == skills[i]);
            float t = CoachRank.Rated(ratings[i]) ? ratings[i]!.Tier : -0.5f;
            pts[i] = c + Dir(i) * Rad(t);
        }
        pts[n] = pts[0];
        if (p.Skills.Any(CoachRank.Rated))
        {
            try { ci.FillPoly(pts[..n], new Color(UiTheme.Accent, 0.26f)); } catch { /* degenerate polygon */ }
            ci.Polyline(pts, UiTheme.Accent, Mathf.Max(1.5f, 2.2f * k), true);
        }
        for (int i = 0; i < n; i++)
        {
            bool rated = CoachRank.Rated(ratings[i]);
            if (rated)
            {
                ci.DrawCircle(pts[i], 5 * k, UiTheme.Bg);
                ci.DrawCircle(pts[i], 3.6f * k, CoachRank.Col(ratings[i]!.Tier));
            }

            // labels outside the rim
            var dir = Dir(i);
            var lp = c + dir * (R + 14 * k);
            int ls = UiTheme.Fs(15, k), rs = UiTheme.Fs(12.5f, k);
            string name = CoachSkills.Short(skills[i]);
            string rank = rated ? CoachRank.Name(ratings[i]!.Tier) : "—";
            float wName = Gfx.TextW(UiTheme.Display, name, ls), wRank = Gfx.TextW(UiTheme.Display, rank, rs);
            float wMax = Mathf.Max(wName, wRank);
            float bx = dir.X > 0.3f ? lp.X : dir.X < -0.3f ? lp.X - wMax : lp.X - wMax / 2;
            float blockH = ls * 0.8f + rs * 0.95f;
            float by = dir.Y > 0.3f ? lp.Y : dir.Y < -0.3f ? lp.Y - blockH : lp.Y - blockH / 2;
            var align = dir.X > 0.3f ? HorizontalAlignment.Left : dir.X < -0.3f ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            Gfx.TextFit(ci, UiTheme.Display, name, bx, by + ls * 0.74f, ls, rated ? UiTheme.Text : UiTheme.Faint, wMax, align);
            Gfx.TextFit(ci, UiTheme.Display, rank, bx, by + ls * 0.8f + rs * 0.86f, rs, rated ? CoachRank.Col(ratings[i]!.Tier) : UiTheme.Faint, wMax, align);
        }
    }

    /// <summary>Spinning arc for loading states.</summary>
    public static void Spinner(CanvasItem ci, Vector2 c, float r, float k, float t)
    {
        ci.DrawArc(c, r, 0, Mathf.Tau, 48, new Color(UiTheme.Text, 0.1f), Mathf.Max(2, 3 * k), true);
        float a = t * 5f;
        ci.DrawArc(c, r, a, a + 1.6f, 24, UiTheme.Accent, Mathf.Max(2, 3 * k), true);
    }

    /// <summary>Height of word-wrapped text.</summary>
    public static float TextH(Font f, string text, float width, int fs, int maxLines = -1) =>
        text.Length == 0 ? 0 : f.GetMultilineStringSize(text, HorizontalAlignment.Left, width, fs, maxLines).Y;

    /// <summary>Draws word-wrapped text whose first line's TOP is at <paramref name="top"/>; returns the height.</summary>
    public static float Para(CanvasItem ci, Font f, string text, float x, float top, float width, int fs, Color c, int maxLines = -1)
    {
        if (text.Length == 0) return 0;
        ci.DrawMultilineString(f, new Vector2(x, top + f.GetAscent(fs)), text, HorizontalAlignment.Left, width, fs, maxLines, c);
        return TextH(f, text, width, fs, maxLines);
    }
}

/// <summary>Angular card background (plate + coloured left strip) for coach cards; children laid out inside.</summary>
public partial class CardFrame : MarginContainer
{
    public float K = 1f;
    public Color Strip = UiTheme.Accent;
    public Color Fill = new(0.075f, 0.115f, 0.155f, 0.92f);

    public CardFrame() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        Gfx.Plate(this, r, 14 * K, Fill, new Color(UiTheme.Text, 0.09f));
        DrawRect(new Rect2(0, 0, 4 * K, r.Size.Y - 14 * K), Strip);
    }
}

/// <summary>Clickable header of a problem card: priority, severity, skill, title and (collapsed) one line of evidence.</summary>
public partial class ProblemHeader : BaseButton
{
    public Diagnosis D = null!;
    public int Index;
    public bool Possible, Expanded;
    public float K = 1f;
    float hover;

    public ProblemHeader()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
    }

    public float WantedHeight => (Expanded ? 78 : 104) * K;

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K, pad = 24 * k, w = Size.X;
        if (hover > 0.01f) DrawRect(new Rect2(4 * k, 0, w - 4 * k, Size.Y), new Color(1, 1, 1, 0.035f * hover));
        float cy = 24 * k, x = pad;
        var sc = CoachSkills.SeverityColor(D.Severity);
        int ns = UiTheme.Fs(19, k);
        x += Gfx.TextW(UiTheme.Display, $"#{Index}", ns) + 10 * k;
        Gfx.Text(this, UiTheme.Display, $"#{Index}", pad, Gfx.Mid(cy, ns), ns, new Color(UiTheme.Text, 0.4f));
        x += CoachDraw.Chip(this, CoachSkills.SeverityName(D.Severity), x, cy, k, sc, filled: D.Severity == Severity.Major) + 8 * k;
        if (Possible) x += CoachDraw.Chip(this, "POSSIBLE", x, cy, k, UiTheme.Dim) + 8 * k;
        int ts = UiTheme.Fs(12, k);
        Gfx.Text(this, UiTheme.HudWide, CoachSkills.Name(D.Skill), x + 4 * k, Gfx.Mid(cy, ts), ts, UiTheme.Dim);

        // expand chevron + sample count (right)
        float ax = w - pad;
        var chev = new Color(UiTheme.Text, 0.55f + 0.45f * hover);
        if (Expanded) Gfx.ChevronDown(this, new Vector2(ax - 7 * k, cy), 14 * k, -8 * k, -3 * k, chev);
        else Gfx.ChevronDown(this, new Vector2(ax - 7 * k, cy), 14 * k, 8 * k, 3 * k, chev);
        if (D.Samples > 0)
            Gfx.TextR(this, UiTheme.HudWide, $"{D.Samples} SAMPLES", ax - 24 * k, Gfx.Mid(cy, ts), ts, UiTheme.Faint);

        int hs = UiTheme.Fs(27, k);
        Gfx.TextFit(this, UiTheme.Display, D.Title, pad, Gfx.Mid(55 * k, hs), hs, UiTheme.Text, w - pad * 2);
        if (!Expanded)
        {
            int es = UiTheme.Fs(15, k);
            Gfx.TextFit(this, UiTheme.Body, D.Evidence, pad, Gfx.Mid(84 * k, es), es, UiTheme.Dim, w - pad * 2);
        }
    }
}

/// <summary>One skill: name, rank, confidence bars and the coach's headline. Unrated rows start a drill that rates it.</summary>
public partial class SkillRow : BaseButton
{
    public Skill Skill;
    public SkillRating? Rating;
    public float K = 1f;
    public bool Stripe;
    float hover;
    bool Rated => CoachRank.Rated(Rating);

    public SkillRow()
    {
        FocusMode = FocusModeEnum.None;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 58 * K);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var drill = CoachSkills.Drills(Skill).FirstOrDefault(CoachData.HasDrill);
        string rated = Rated
            ? $"{CoachRank.Name(Rating!.Tier)} · {CoachRank.Confidence(Rating.Confidence).ToLowerInvariant()} confidence ({Rating.Samples} samples)"
            : "Not rated yet" + (drill != null ? $" — click to play {CoachData.DrillName(drill)}" : "");
        TooltipText = $"{CoachSkills.Name(Skill)}: {CoachSkills.About(Skill)}\n{rated}";
        if (!Rated && drill != null)
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += UiTheme.HoverSound;
            Pressed += () => { UiTheme.ClickSound(); CoachData.StartDrill(drill); };
        }
    }

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K, pad = 16 * k, w = Size.X, cy = Size.Y / 2;
        var rc = Rated ? CoachRank.Col(Rating!.Tier) : UiTheme.NoTier;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, (Stripe ? 0.03f : 0.012f) + 0.03f * hover));
        DrawRect(new Rect2(0, 0, 3 * k, Size.Y), new Color(rc, Rated ? 0.85f : 0.35f));

        int ns = UiTheme.Fs(21, k), rs = UiTheme.Fs(21, k), hs = UiTheme.Fs(13.5f, k);
        float top = cy - 9 * k;
        string rank = Rated ? CoachRank.Name(Rating!.Tier) : "NOT RATED";
        float rw = Gfx.TextR(this, UiTheme.Display, rank, w - pad, Gfx.Mid(top, rs), rs, Rated ? rc : UiTheme.Faint);
        float bars = 0;
        if (Rated) bars = CoachDraw.ConfBars(this, new Vector2(w - pad - rw - 26 * k, top), k, Rating!.Confidence) + 10 * k;
        Gfx.TextFit(this, UiTheme.Display, CoachSkills.Name(Skill), pad, Gfx.Mid(top, ns), ns, Rated ? UiTheme.Text : UiTheme.Dim,
            w - pad * 2 - rw - bars - 20 * k);

        string line = Rated
            ? (Rating!.Headline.Length > 0 ? Rating.Headline : $"{CoachRank.Confidence(Rating.Confidence)} confidence · {Rating.Samples} samples")
            : NotRatedLine();
        Gfx.TextFit(this, UiTheme.Body, line, pad, Gfx.Mid(cy + 14 * k, hs), hs, Rated ? UiTheme.Dim : (hover > 0.5f ? UiTheme.Text : UiTheme.Faint), w - pad * 2);

        // thin tier bar along the bottom
        float by = Size.Y - 3 * k;
        DrawRect(new Rect2(pad, by, w - pad * 2, 2 * k), new Color(UiTheme.Text, 0.06f));
        if (Rated)
        {
            float f = Mathf.Clamp((Rating!.Tier - CoachRank.Min) / (4.5f - CoachRank.Min), 0.02f, 1f);
            DrawRect(new Rect2(pad, by, (w - pad * 2) * f, 2 * k), new Color(rc, 0.8f));
        }
    }

    string NotRatedLine()
    {
        // Actionable hint first; a specific engine headline ("12 of 30 flicks…") wins over the generic one.
        var drill = CoachSkills.Drills(Skill).FirstOrDefault(CoachData.HasDrill);
        if (Rating is { Headline.Length: > 0 } r && !r.Headline.StartsWith("Not enough data", StringComparison.OrdinalIgnoreCase))
        {
            // The coach's progress line usually already names the drill to play ("… — play Head Flicks or Spidershot").
            bool named = r.Headline.Contains(" play ", StringComparison.OrdinalIgnoreCase) || r.Headline.Contains(" take ", StringComparison.OrdinalIgnoreCase);
            return r.Headline + (drill != null ? (named ? "  ›" : $"  ·  play {CoachData.DrillName(drill)}  ›") : "");
        }
        return drill != null ? $"Play {CoachData.DrillName(drill)} to rate this skill  ›" : "Not enough data yet";
    }
}

/// <summary>Quick-start tile for the empty state: drill name, what it rates and coached runs so far.</summary>
public partial class DrillTile : BaseButton
{
    public string Key = "", Rates = "";
    public int Runs, Target = 1;
    public float K = 1f;
    float hover;

    public DrillTile()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 70 * K);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseEntered += UiTheme.HoverSound;
        Pressed += () => { UiTheme.ClickSound(); CoachData.StartDrill(Key); };
        TooltipText = $"Start {CoachData.DrillName(Key)} — rates {Rates.ToLowerInvariant()}";
    }

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K, pad = 18 * k;
        var r = new Rect2(Vector2.Zero, Size);
        bool done = Runs >= Target;
        var fill = new Color(0.075f, 0.115f, 0.155f, 0.92f).Lerp(new Color(0.13f, 0.18f, 0.23f, 0.96f), hover);
        Gfx.Plate(this, r, 12 * k, fill, new Color(UiTheme.Text, 0.08f + 0.3f * hover));
        DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y - 12 * k), done ? UiTheme.Good : new Color(UiTheme.Accent, 0.5f + 0.5f * hover));
        if (hover > 0.01f) Gfx.Brackets(this, r.Grow(3 * k), 8 * k * hover, new Color(UiTheme.Text, 0.8f * hover), Mathf.Max(1, 2 * k));

        int ns = UiTheme.Fs(23, k), ds = UiTheme.Fs(13, k), ps = UiTheme.Fs(22, k), ls = UiTheme.Fs(10.5f, k);
        float right = r.Size.X - pad;
        string prog = $"{Math.Min(Runs, Target)}/{Target}";
        float pw = Gfx.TextR(this, UiTheme.Display, prog, right, Gfx.Mid(r.Size.Y / 2 - 6 * k, ps), ps, done ? UiTheme.Good : UiTheme.Text);
        Gfx.TextR(this, UiTheme.HudWide, done ? "DONE" : "RUNS", right, Gfx.Mid(r.Size.Y / 2 + 15 * k, ls), ls, done ? UiTheme.Good : UiTheme.Faint);
        float tw = r.Size.X - pad * 2 - Mathf.Max(pw, 40 * k) - 12 * k;
        Gfx.TextFit(this, UiTheme.Display, CoachData.DrillName(Key).ToUpperInvariant(), pad, Gfx.Mid(r.Size.Y / 2 - 9 * k, ns), ns, UiTheme.Text, tw);
        Gfx.TextFit(this, UiTheme.Body, "Rates " + Rates.ToLowerInvariant(), pad, Gfx.Mid(r.Size.Y / 2 + 14 * k, ds), ds, UiTheme.Dim, tw);
    }
}
