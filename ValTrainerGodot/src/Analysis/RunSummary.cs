using ValTrainer.Core;

namespace ValTrainer.Analysis;

/// <summary>One-paragraph, plain-English run review ("Your flicks are fast but 64% overshoot…").</summary>
static class RunSummary
{
    static float V(Dictionary<string, float> m, string k) => m.TryGetValue(k, out var v) ? v : float.NaN;

    /// <summary>Modes whose telemetry the coach analyses (everything else gets the "doesn't feed the coach" note).</summary>
    static readonly HashSet<string> CoachModes = new()
    {
        "flick", "spider", "gridshot", "tracking", "strafebots", "peek", "counterstrafe", "peekduel", "siteclear", "flashmap", "deathmatch",
        "reaction", "spray_vandal", "spray_phantom", "microshot", "popup", "longtaps", "sheriff", "jumppeek", "jigglepeek", "peek_mix",
        "flashpeek", "recon", "smokeexec", "mobility", "chamber", "postplant", "retake", "anchor", "sound",
        "switch", "operator", "spray_transfer", "strafeduel",
    };

    /// <summary>Summary for a run without enough data for any finding: what this mode needs, or that it doesn't feed the coach.</summary>
    public static string NoData(string mode)
    {
        mode = RunAnalysis.Base(mode);
        if (!CoachModes.Contains(mode))
            return L.T("This drill doesn't feed the aim coach yet — your score and accuracy above are the measure. Head Flicks, Spidershot, Gridshot, Strafe Tracking, the bot drills and the spray drills all get a detailed review.");
        string need = mode switch
        {
            "flick" or "microshot" or "popup" => L.N("hit at least 5 targets"),
            "longtaps" or "sheriff" or "jumppeek" or "jigglepeek" or "peek_mix" or "chamber" or "sound" => L.N("let a few enemies show up and shoot them"),
            "flashpeek" or "recon" or "smokeexec" or "mobility" or "postplant" or "retake" or "anchor" => L.N("play a few rounds and take some duels"),
            "spider" or "gridshot" => L.N("play at least 15 seconds and hit a few targets"),
            "tracking" => L.N("hold fire on the target for at least a few seconds"),
            "strafebots" or "switch" => L.N("take on a few bots"),
            "operator" => L.N("let a few agents cross the gap while you hold it"),
            "peek" => L.N("let a few bots peek you"),
            "counterstrafe" => L.N("stop and shoot a few times"),
            "peekduel" or "siteclear" or "deathmatch" or "strafeduel" => L.N("get a few duels in"),
            "flashmap" => L.N("face a few flashes"),
            "reaction" => L.N("finish the trials"),
            _ => L.N("fire at least 10 bullets per spray a few times"), // spray_vandal / spray_phantom
        };
        return L.F($"Not enough data this run for a review — {L.T(need)}.");
    }

    public static string Write(string mode, int tier, Dictionary<string, float> m, Assessment a)
    {
        string tierName = L.T(Bench.TierName(tier));
        var top = a.Issues.FirstOrDefault();
        var (lead, positive) = Lead(mode, tier, tierName, m, a, a.Issues.Count > 0, top?.D.Id);
        var parts = new List<string>();
        if (top != null)
        {
            string clause = top.Possible ? L.F($"it looks like {top.Short} (not confirmed yet — keep an eye on it)") : top.Short;
            parts.Add(lead.Length == 0 ? Cap(clause) + "." : positive ? L.F($"{lead}, but {clause}.") : L.F($"{lead}; also, {clause}."));
            var fix = (top.SensFix ? top.D.Fixes[..^1] : top.D.Fixes).FirstOrDefault(); // the first technique fix, not the sens note
            if (fix != null) parts.Add(fix);
            if (a.Issues.Count > 1 && !a.Issues[1].Possible) parts.Add(L.F($"Next on the list: {a.Issues[1].Short}."));
        }
        else if (lead.Length == 0 && a.Ratings.Values.All(r => r.Tier < -0.9f))
            return NoData(mode);
        else
        {
            parts.Add(lead.Length > 0 ? $"{lead}." : L.T("Solid run."));
            parts.Add(L.T("No clear problems this run — raise the tier or play longer runs for a sharper read."));
        }
        if (a.Strengths.Count > 0) parts.Add(L.F($"On the plus side: {Uncap(a.Strengths[0].Text)}"));
        return string.Join(" ", parts);
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    // English only: other languages keep the case (German nouns are capitalized; the translation decides its own case).
    static string Uncap(string s) => L.IsEnglish && s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    /// <summary>"fast" / "solid" / "slow" for a speed tier vs the tier played, translated for the sentence it goes in.</summary>
    static string Pace(float t, int tier, bool flicks) => flicks
        ? L.C("your flicks were", t >= tier + 0.5f ? "fast" : t >= tier - 0.5f ? "solid" : "slow")
        : L.C("your speed was", t >= tier + 0.5f ? "fast" : t >= tier - 0.5f ? "solid" : "slow");

    static (string Text, bool Positive) Lead(string mode, int tier, string tierName, Dictionary<string, float> m, Assessment a, bool issue, string? topId)
    {
        float sp = V(m, "speed.tier");
        switch (mode)
        {
            case "flick":
            {
                float ttk = V(m, "flick.ttk_hit_ms");
                if (float.IsNaN(ttk)) break;
                return (float.IsNaN(sp) ? L.F($"Your flicks were {L.C("your flicks were", "measured")} — {ttk:0} ms median to hit")
                    : L.F($"Your flicks were {Pace(sp, tier, true)} — {ttk:0} ms median to hit ({Bench.RankUi(sp)} pace on {tierName})"), sp >= tier - 0.5f);
            }
            case "spider":
            case "gridshot":
            {
                float spm = V(m, "speed.score_min");
                if (float.IsNaN(sp) || float.IsNaN(spm)) break;
                return (L.F($"Your speed was {Pace(sp, tier, false)} — {spm:0} points/min ({Bench.RankUi(sp)} level on {tierName})"), sp >= tier - 0.5f);
            }
            case "tracking":
            {
                float on = V(m, "track.ontarget"), t = a.Ratings.TryGetValue(Skill.Tracking, out var r) ? r.Tier : float.NaN;
                if (float.IsNaN(on)) break;
                return (L.F($"You were on target {on:0}% of the time you were firing ({Bench.RankUi(V(m, "track.ontarget_tier"))} level on {tierName})"), !float.IsNaN(t) && t >= tier - 0.5f);
            }
            case "spray_vandal":
            case "spray_phantom":
            {
                float v = V(m, "spray.v"), h = V(m, "spray.h"), n = V(m, "spray.n");
                if (float.IsNaN(v)) break;
                float t = a.Ratings.TryGetValue(Skill.SprayControl, out var r) ? r.Tier : float.NaN;
                if (issue) return (L.F($"Over {n:0} sprays your recoil control was {Bench.RankUi(t)} level"), t >= tier - 0.5f);
                return (v >= 0 ? L.F($"Over {n:0} sprays, bullets 4–15 landed {MathF.Abs(v):0.0}° high with {h:0.0}° of sideways scatter ({Bench.RankUi(t)} level)")
                    : L.F($"Over {n:0} sprays, bullets 4–15 landed {MathF.Abs(v):0.0}° low with {h:0.0}° of sideways scatter ({Bench.RankUi(t)} level)"), t >= tier - 0.5f);
            }
            case "reaction":
            {
                float ms = V(m, "react.ms");
                if (float.IsNaN(ms)) break;
                float t = Bench.TierOf(ms, Bench.ReactMs);
                return (L.F($"Your median reaction was {ms:0} ms over {V(m, "react.n"):0} trials ({Bench.RankUi(t)} level)"), t >= 1);
            }
            case "flashmap":
            {
                float d = V(m, "flash.dodge_pct"), turn = V(m, "flash.turn_ms");
                if (float.IsNaN(d)) break;
                return (float.IsNaN(turn) ? L.F($"You beat {d:0}% of the flashes") : L.F($"You beat {d:0}% of the flashes and turned away in {turn:0} ms on average"), d >= 60);
            }
        }
        // Bot / movement modes and fallbacks (skipping the fact the top issue is about, so it isn't said twice).
        var bits = new List<string>();
        float fsh = V(m, "head.fsh_pct"), nf = V(m, "head.n_first");
        if (!float.IsNaN(fsh) && nf >= 3) bits.Add(L.F($"{fsh:0}% of your first shots hit the head"));
        float xe = V(m, "xhair.err_deg");
        if (!float.IsNaN(xe) && topId is not ("XHAIR_WIDE" or "XHAIR_LOW")) bits.Add(L.F($"enemies appeared {xe:0.0}° from your crosshair"));
        float mv = V(m, "move.moving_shot_pct");
        if (!float.IsNaN(mv) && topId != "RUN_GUN") bits.Add(L.F($"{mv:0}% of shots were fired while moving"));
        float cs = V(m, "move.counter_pct");
        if (!float.IsNaN(cs) && topId != "CLUMSY_MOVE") bits.Add(L.F($"{cs:0}% of stops were counter-strafed"));
        if (bits.Count == 0) return ("", false);
        // "…, but <problem>" only when the facts listed are good ones; otherwise "…; also, <problem>".
        bool good = !(fsh < Bench.ValueAt(tier, Bench.FirstShotHitPct) && nf >= 3) && !(XhairRef(m, xe) > Bench.ValueAt(tier, Bench.XhairErrDeg))
                    && !(mv >= 15) && !(cs < 40);
        return (Cap(string.Join(", ", bits.Take(3))), good);
    }

    /// <summary>The first-sight error on the match scale (Deathmatch's is easier: Bench.XhairDrillFactor) for judging it;
    /// the summary still quotes the degrees the player had.</summary>
    static float XhairRef(Dictionary<string, float> m, float raw) => m.TryGetValue("xhair.err_ref_deg", out var r) && !float.IsNaN(r) ? r : raw;
}
