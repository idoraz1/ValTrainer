using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>
/// Spec section A: turns one run's telemetry into a flat metric dictionary. Keys are grouped by family
/// (flick., head., speed., track., xhair., move., spray., react., flash.); counts are stored next to every
/// average so runs can be aggregated exactly (see <see cref="Agg"/>). Missing data → key omitted.
/// </summary>
static class RunAnalysis
{
    static readonly HashSet<string> StaticModes = new() { "flick", "spider", "gridshot" };
    static readonly HashSet<string> SpawnBotModes = new() { "strafebots", "counterstrafe" };
    static readonly HashSet<string> SeenBotModes = new() { "peek", "peekduel", "siteclear", "deathmatch" };
    static readonly HashSet<string> MovementModes = new() { "counterstrafe", "peekduel", "siteclear", "flashmap", "deathmatch" };

    public static Dictionary<string, float> Run(RunTelemetry t)
    {
        var m = new Dictionary<string, float>();
        if (t.Mode == "sensfinder")
        {
            // The sens changes mid-run: never feed skill ratings / sens history. Keep only the drill's result.
            var res = t.Events.LastOrDefault(e => e.Kind == "sens_result");
            if (res != null && res.A > 0) { m["sensfinder.sens"] = res.A; m["sensfinder.conf"] = res.B; }
            return m;
        }
        if (t.Mode == "xhairfinder") return m; // the crosshair changes mid-run: its trials are not comparable skill data
        if (t.Frames.Count < 10 || !FramesUsable(t)) { Reaction(t, m); Flash(t, m); return Clean(m); }
        var p = new Prep(t);
        m["run.secs"] = p.Ft[^1] - p.Ft[0];
        Flicks(p, m);
        SpeedTier(p, m);
        if (t.Mode == "tracking") Tracking(p, m);
        Crosshair(p, m);
        Movement(p, m);
        if (t.Mode is "spray_vandal" or "spray_phantom") Spray(p, m);
        Reaction(t, m);
        Flash(t, m);
        Agg.Derive(m);
        return Clean(m);
    }

    /// <summary>
    /// Frame timestamps must be finite and ascending over a sane span (≤ 2 h): a corrupt file could otherwise ask the
    /// 240 Hz resampler for gigabytes. The game's own runs always pass (dt is clamped, pauses don't advance time).
    /// </summary>
    static bool FramesUsable(RunTelemetry t)
    {
        var fr = t.Frames;
        float prev = fr[0].T;
        if (!float.IsFinite(prev)) return false;
        for (int i = 1; i < fr.Count; i++)
        {
            float ti = fr[i].T;
            if (!float.IsFinite(ti) || ti < prev - 1e-3f) return false;
            prev = ti;
        }
        float span = fr[^1].T - fr[0].T;
        return span > 0.1f && span <= 7200f;
    }

    /// <summary>Stats are saved as JSON, which can't hold NaN / ±∞: drop any such value.</summary>
    static Dictionary<string, float> Clean(Dictionary<string, float> m)
    {
        foreach (var k in m.Where(kv => !float.IsFinite(kv.Value)).Select(kv => kv.Key).ToList()) m.Remove(k);
        return m;
    }

    // =====================================================================================================
    // A1. Flicks
    // =====================================================================================================

    sealed class Trial
    {
        public int Id;
        public float T0, TEnd, THit = -1;
        public bool Static, Predictable, Bot;
        public float AbsYaw, AbsPitch, R;
        public bool Hit => THit >= 0;
    }

    sealed class Sub
    {
        public int S, E;
        public float Peak, Dx, Dy, Along, Amp;
    }

    sealed class TrialResult
    {
        public float A, R;
        public bool Predictable, Hit, Head, HasPrimary, Bot;
        public float Onset = float.NaN, Mt, PeakDps, PeakCms, EndErr, S, Perp, Gain, Curv, EndY, EtaP = float.NaN;
        public bool Over, Under;
        public int Nc;
        public float Tc = float.NaN, Dwell = float.NaN, Eta = float.NaN, MtFitts = float.NaN, Id, Ttk = float.NaN;
        public readonly List<float> CorrAmps = new();
        public int Misses, Premature, Snap;
        public readonly List<float> PrematureDps = new(), SnapErr = new();
        public bool HasFirstShot, FirstHit;
        public float ShotErr = float.NaN;
    }

    static void Flicks(Prep p, Dictionary<string, float> m)
    {
        var t = p.T;
        var trials = BuildTrials(p);
        var res = new List<TrialResult>();
        foreach (var tr in trials)
        {
            var r = AnalyzeTrial(p, tr);
            if (r != null) res.Add(r);
        }
        if (res.Count == 0) return;

        m["flick.n"] = res.Count;
        m["flick.n5"] = res.Count(r => r.A >= 5);
        m["flick.amp_deg"] = Dsp.Mean(res.Select(r => r.A));
        m["flick.r_deg"] = Dsp.Mean(res.Select(r => r.R));
        var prim = res.Where(r => r.HasPrimary).ToList();
        m["flick.n_prim"] = prim.Count;
        if (prim.Count > 0)
        {
            m["flick.mt_ms"] = Dsp.Median(prim.Select(r => r.Mt));
            m["flick.peak_dps"] = Dsp.Median(prim.Select(r => r.PeakDps));
            m["flick.peak_cms"] = Dsp.Median(prim.Select(r => r.PeakCms));
            m["flick.end_err_pct"] = 100 * Dsp.Median(prim.Select(r => r.EndErr));
            // Over/undershoot and gain only on targets that don't move: leading a strafing bot would read as overshoot.
            var still = prim.Where(r => !r.Bot).ToList();
            if (still.Count > 0) { m["flick.n_still"] = still.Count; m["flick.gain"] = Dsp.Mean(still.Select(r => r.Gain)); }
            m["flick.perp_deg"] = Dsp.Median(prim.Select(r => r.Perp));
            m["flick.curv"] = Dsp.Median(prim.Select(r => r.Curv));
            var etaP = prim.Where(r => !float.IsNaN(r.EtaP)).ToList();
            if (etaP.Count > 0) m["flick.eta_p"] = Dsp.Median(etaP.Select(r => r.EtaP));
            m["flick.end_y_deg"] = Dsp.Mean(prim.Select(r => r.EndY));
            m["flick.n_low"] = prim.Count(r => r.EndY < -r.R);
            var over = still.Where(r => r.Over).ToList();
            var under = still.Where(r => r.Under).ToList();
            if (still.Count > 0)
            {
                m["flick.n_over"] = over.Count;
                m["flick.n_under"] = under.Count;
                m["flick.n_off"] = over.Count + under.Count;
            }
            if (over.Count > 0)
            {
                m["flick.mean_overshoot_deg"] = Dsp.Mean(over.Select(r => r.S));
                m["flick.mean_overshoot_pct"] = 100 * Dsp.Mean(over.Select(r => r.S / r.A));
            }
            if (under.Count > 0) m["flick.mean_undershoot_deg"] = Dsp.Mean(under.Select(r => -r.S));
            var small = still.Where(r => r.A < 25).ToList();
            var large = still.Where(r => r.A >= 25).ToList();
            if (small.Count > 0) { m["flick.n_small"] = small.Count; m["flick.gain_small"] = Dsp.Mean(small.Select(r => r.Gain)); }
            if (large.Count > 0) { m["flick.n_large"] = large.Count; m["flick.gain_large"] = Dsp.Mean(large.Select(r => r.Gain)); }
            var onset = prim.Where(r => !r.Predictable && !float.IsNaN(r.Onset)).ToList();
            var valid = onset.Where(r => r.Onset >= 100 && r.Onset <= 1000).ToList();
            if (onset.Count > 0) m["flick.anticip"] = onset.Count(r => r.Onset < 100);
            if (valid.Count > 0) { m["flick.onset_n"] = valid.Count; m["flick.onset_ms"] = Dsp.Median(valid.Select(r => r.Onset)); }
        }

        // Click timing / corrections / path only on static targets: on moving bots they would include pursuit time.
        var hits = prim.Where(r => r.Hit && !r.Bot).ToList();
        if (hits.Count > 0)
        {
            m["flick.n_hit"] = hits.Count;
            m["flick.nc"] = Dsp.Mean(hits.Select(r => (float)r.Nc));
            m["flick.tc_ms"] = Dsp.Median(hits.Select(r => r.Tc));
            m["flick.dwell_ms"] = Dsp.Median(hits.Select(r => r.Dwell));
            m["flick.eta"] = Dsp.Median(hits.Select(r => r.Eta));
            m["flick.ttk_ms"] = Dsp.Median(hits.Select(r => r.Ttk));
            var corr = hits.SelectMany(r => r.CorrAmps.Select(a => (a, r.R))).ToList();
            if (corr.Count > 0)
            {
                m["flick.n_corr"] = corr.Count;
                m["flick.corr_amp_deg"] = Dsp.Median(corr.Select(c => c.a));
                m["flick.corr_amp_r"] = Dsp.Median(corr.Select(c => c.a / Math.Max(0.05f, c.R)));
            }
            // Fitts: MT = a + b·ID, reported at ID 4.7 bits.
            var fx = hits.Where(r => !float.IsNaN(r.MtFitts)).ToList();
            if (fx.Count >= 5)
            {
                float mtRef = Dsp.Fit(fx.Select(r => r.Id).ToList(), fx.Select(r => r.MtFitts).ToList(), out var a, out var b) && b > 0
                    ? a + b * 4.7f : Dsp.Median(fx.Select(r => r.MtFitts)) * 4.7f / Math.Max(1f, Dsp.Mean(fx.Select(r => r.Id)));
                m["flick.mt_ref_ms"] = Math.Clamp(mtRef, 120, 4000);
                if (b > 0) m["flick.fitts_b"] = b;
            }
        }

        int misses = res.Sum(r => r.Misses);
        m["flick.misses"] = misses;
        if (misses > 0)
        {
            m["flick.premature"] = res.Sum(r => r.Premature);
            m["flick.snap"] = res.Sum(r => r.Snap);
            var pd = res.SelectMany(r => r.PrematureDps).ToList();
            var se = res.SelectMany(r => r.SnapErr).ToList();
            if (pd.Count > 0) m["flick.premature_dps"] = Dsp.Median(pd);
            if (se.Count > 0) m["flick.snap_err_deg"] = Dsp.Median(se);
        }

        var first = res.Where(r => r.HasFirstShot).ToList();
        if (first.Count > 0)
        {
            m["flick.n_first"] = first.Count;
            m["flick.first_hits"] = first.Count(r => r.FirstHit);
            m["flick.shot_err_deg"] = Dsp.Median(first.Select(r => r.ShotErr));
            m["flick.shot_err_r"] = Dsp.Median(first.Select(r => r.ShotErr / Math.Max(0.05f, r.R)));
            var sd = Dsp.Sd(first.Select(r => r.ShotErr));
            if (!float.IsNaN(sd)) m["flick.precision_deg"] = sd;
        }

        // Head-sized targets only (≤ 1.2° radius): the precision benchmarks are for heads, not big range balls.
        var head = res.Where(r => r.Head).ToList();
        var headFirst = head.Where(r => r.HasFirstShot).ToList();
        var headHits = head.Where(r => r.HasPrimary && r.Hit && !r.Bot).ToList();
        if (headFirst.Count > 0)
        {
            m["head.n_first"] = headFirst.Count;
            m["head.first_hits"] = headFirst.Count(r => r.FirstHit);
        }
        if (headHits.Count > 0)
        {
            m["head.n_hit"] = headHits.Count;
            m["head.tc_ms"] = Dsp.Median(headHits.Select(r => r.Tc));
            m["head.nc"] = Dsp.Mean(headHits.Select(r => (float)r.Nc));
        }
    }

    static List<Trial> BuildTrials(Prep p)
    {
        var t = p.T;
        var list = new List<Trial>();
        var fr = t.Frames;
        if (StaticModes.Contains(t.Mode))
        {
            // Absolute direction of each static target, from the first frame that focused it.
            var abs = new Dictionary<int, (float Yaw, float Pitch, float R)>();
            for (int i = 0; i < p.N; i++)
            {
                var f = fr[i];
                if (f.FocusId != 0 && !abs.ContainsKey(f.FocusId))
                    abs[f.FocusId] = (p.Yaw[i] + f.FocusYaw, p.Pitch[i] + f.FocusPitch, f.FocusRadius);
            }
            var hitAt = new Dictionary<int, float>();
            var endAt = new Dictionary<int, float>();
            foreach (var e in t.Events)
            {
                if (e.Kind == "target_hit") hitAt.TryAdd((int)e.A, e.T);
                else if (e.Kind == "target_expired") endAt.TryAdd((int)e.A, e.T);
            }
            if (t.Mode == "gridshot")
            {
                float prev = p.Ft[0];
                bool firstTrial = true;
                foreach (var e in t.Events.Where(e => e.Kind == "target_hit"))
                {
                    int id = (int)e.A;
                    if (!firstTrial && abs.TryGetValue(id, out var a))
                        list.Add(new Trial { Id = id, T0 = prev, TEnd = e.T, THit = e.T, Static = true, Predictable = true, AbsYaw = a.Yaw, AbsPitch = a.Pitch, R = a.R });
                    firstTrial = false;
                    prev = e.T;
                }
            }
            else
            {
                int i = 0;
                while (i < p.N)
                {
                    int id = fr[i].FocusId;
                    int j = i;
                    while (j + 1 < p.N && fr[j + 1].FocusId == id) j++;
                    if (id != 0 && i > 0 && abs.TryGetValue(id, out var a))
                    {
                        var tr = new Trial { Id = id, T0 = p.Ft[i], TEnd = p.Ft[j], Static = true, AbsYaw = a.Yaw, AbsPitch = a.Pitch, R = a.R };
                        if (hitAt.TryGetValue(id, out var th)) { tr.THit = th; tr.TEnd = th; }
                        else if (endAt.TryGetValue(id, out var te)) tr.TEnd = te;
                        // Spidershot's centre target is always in the same place: the return flick is predictable.
                        tr.Predictable = t.Mode == "spider" && MathF.Abs(a.Yaw) < 1f && MathF.Abs(a.Pitch) < 1f;
                        list.Add(tr);
                    }
                    i = j + 1;
                }
            }
        }
        else if (SpawnBotModes.Contains(t.Mode) || SeenBotModes.Contains(t.Mode))
        {
            // First engagement with each bot: from spawn (strafe/counter-strafe) or first sight (peek modes).
            bool seen = SeenBotModes.Contains(t.Mode);
            var kills = t.Events.Where(e => e.Kind == "kill").Select(e => e.T).ToList();
            foreach (var e in t.Events)
            {
                if (e.Kind != (seen ? "bot_seen" : "target_spawn")) continue;
                int fi = p.FrameAt(e.T);
                int id = 0;
                for (int k = fi; k < Math.Min(p.N, fi + 6); k++)
                {
                    if (p.Ft[k] < e.T - 1e-4) continue;
                    if (fr[k].FocusId != 0 && (seen || (float)fr[k].FocusId == e.A)) { id = fr[k].FocusId; fi = k; break; }
                }
                if (id == 0) continue;
                int j = fi;
                while (j + 1 < p.N && fr[j + 1].FocusId == id && p.Ft[j + 1] - e.T < 3f) j++;
                float end = p.Ft[j];
                float kill = kills.FirstOrDefault(k => k > e.T && k <= end + 0.05f, -1);
                if (kill > 0) end = kill;
                var tr = new Trial { Id = id, T0 = e.T, TEnd = end, Bot = true, R = fr[fi].FocusRadius };
                foreach (var s in t.Shots)
                    if (s.T > tr.T0 && s.T <= tr.TEnd && s.Zone >= 0) { tr.THit = s.T; tr.TEnd = Math.Max(tr.TEnd, s.T); break; }
                list.Add(tr);
            }
        }
        return list;
    }

    /// <summary>Crosshair error e = crosshair − target (deg) at time t; NaN when unknown.</summary>
    static (float X, float Y) Err(Prep p, Trial tr, double t)
    {
        if (tr.Static)
        {
            var (y, pi) = p.CrossAt(t);
            return (y - tr.AbsYaw, pi - tr.AbsPitch);
        }
        var fr = p.T.Frames;
        int i = p.FrameAt(t);
        bool a = fr[i].FocusId == tr.Id, b = i + 1 < p.N && fr[i + 1].FocusId == tr.Id;
        if (a && b && t > p.Ft[i])
        {
            float f = (float)((t - p.Ft[i]) / Math.Max(1e-6, p.Ft[i + 1] - p.Ft[i]));
            return (-(fr[i].FocusYaw + (fr[i + 1].FocusYaw - fr[i].FocusYaw) * f), -(fr[i].FocusPitch + (fr[i + 1].FocusPitch - fr[i].FocusPitch) * f));
        }
        if (a) return (-fr[i].FocusYaw, -fr[i].FocusPitch);
        if (b) return (-fr[i + 1].FocusYaw, -fr[i + 1].FocusPitch);
        for (int k = 1; k < 12; k++)
        {
            if (i - k >= 0 && fr[i - k].FocusId == tr.Id && t - p.Ft[i - k] < 0.06) return (-fr[i - k].FocusYaw, -fr[i - k].FocusPitch);
            if (i + k < p.N && fr[i + k].FocusId == tr.Id && p.Ft[i + k] - t < 0.06) return (-fr[i + k].FocusYaw, -fr[i + k].FocusPitch);
        }
        return (float.NaN, float.NaN);
    }

    static TrialResult? AnalyzeTrial(Prep p, Trial tr)
    {
        if (tr.T0 <= p.Ft[0] + 1e-4) return null; // started before recording (countdown motion)
        var (ex0, ey0) = Err(p, tr, tr.T0);
        if (float.IsNaN(ex0)) return null;
        float A = MathF.Sqrt(ex0 * ex0 + ey0 * ey0);
        float R = Math.Max(0.05f, tr.R);
        if (A < Math.Max(3f, 3f * R)) return null; // not a flick (already on / next to the target)
        float ux = -ex0 / A, uy = -ey0 / A;
        float tClick = tr.Hit ? tr.THit : tr.TEnd;
        int i0 = p.Gi(tr.T0), i1 = p.Gi(tClick);
        if (i1 - i0 < 6) return null;

        var r = new TrialResult { A = A, R = R, Predictable = tr.Predictable, Hit = tr.Hit, Head = R <= 1.2f, Bot = tr.Bot };
        if (tr.Hit) r.Ttk = (tr.THit - tr.T0) * 1000f;

        var subs = Segment(p, i0, i1, ux, uy);
        var P = subs.FirstOrDefault(s => s.Along >= 0.3f * A);
        double tsP = 0, teP = 0;
        if (P != null)
        {
            r.HasPrimary = true;
            tsP = p.Gt(P.S); teP = p.Gt(P.E);
            r.Onset = (float)(tsP - tr.T0) * 1000f;
            r.Mt = (float)(teP - tsP) * 1000f;
            r.PeakDps = P.Peak;
            r.PeakCms = p.DegToCm(P.Peak);
            var (ex, ey) = Err(p, tr, teP);
            if (float.IsNaN(ex)) { r.HasPrimary = false; P = null; }
            else
            {
                r.S = ex * ux + ey * uy;
                float px = ex - r.S * ux, py = ey - r.S * uy;
                r.Perp = MathF.Sqrt(px * px + py * py);
                r.Gain = (A + r.S) / A;
                r.EndErr = MathF.Sqrt(ex * ex + ey * ey) / A;
                r.Over = r.S > R;
                r.Under = r.S < -R;
                r.EndY = ey;
                float maxPerp = 0;
                for (int i = P.S; i <= P.E; i++)
                {
                    float dx = p.Gx[i] - p.Gx[P.S], dy = p.Gy[i] - p.Gy[P.S];
                    maxPerp = Math.Max(maxPerp, MathF.Abs(dx * -uy + dy * ux));
                }
                r.Curv = maxPerp / A;
                // Straightness of the primary itself: |displacement| / ∫|ω|dt over the submovement.
                double len = 0;
                for (int i = P.S; i < P.E; i++) len += p.Sp[i] * Dsp.Dt;
                if (len > 1e-3) r.EtaP = (float)Math.Min(1.0, P.Amp / len);
            }
        }

        var prim0 = P;
        var corrections = prim0 == null ? new List<Sub>() : subs.Where(s => s.S > prim0.S && s != prim0).ToList();
        if (tr.Hit && P != null)
        {
            var before = corrections.Where(s => p.Gt(s.S) < tr.THit).ToList();
            r.Nc = before.Count;
            r.CorrAmps.AddRange(before.Select(s => s.Amp));
            r.Tc = (float)Math.Max(0, tr.THit - teP) * 1000f;
            // Dwell: time on target (|e| ≤ r, continuously) before the click.
            int fi = p.FrameAt(tr.THit);
            float enter = tr.THit;
            for (int k = fi; k >= 0 && p.Ft[k] > tr.T0; k--)
            {
                var (ex, ey) = Err(p, tr, p.Ft[k]);
                if (float.IsNaN(ex) || ex * ex + ey * ey > R * R) break;
                enter = p.Ft[k];
            }
            r.Dwell = (tr.THit - enter) * 1000f;
            float path = p.PathLength(tsP, tr.THit);
            r.Eta = path > 1e-3f ? Math.Min(1f, A / path) : float.NaN;
            r.MtFitts = (float)(tr.THit - tsP) * 1000f;
            r.Id = MathF.Log2(A / (2 * R) + 1);
        }

        // Shots of this trial: first-shot outcome and miss classification.
        bool firstDone = false;
        foreach (var s in p.T.Shots)
        {
            if (s.T <= tr.T0 + 1e-5f || s.T > tClick + 1e-5f) continue;
            if (!firstDone)
            {
                firstDone = true;
                r.HasFirstShot = true;
                r.FirstHit = tr.Bot ? s.Zone == 0 : s.Zone >= 0;
                r.ShotErr = s.Err;
            }
            if (s.Zone >= 0) continue;
            r.Misses++;
            int fi = p.FrameAt(s.T);
            float w = p.RawSpeed(fi, 3);
            bool premature = w > Math.Max(30f, 0.25f * (P?.Peak ?? 0f));
            if (premature) { r.Premature++; r.PrematureDps.Add(w); continue; }
            if (P != null && s.T > tsP && s.T - teP < 0.08)
            {
                var (ex, ey) = Err(p, tr, s.T);
                bool corrected = corrections.Any(c => p.Gt(c.S) < s.T);
                float e = MathF.Sqrt(ex * ex + ey * ey);
                if (!corrected && !float.IsNaN(e) && e > R) { r.Snap++; r.SnapErr.Add(e); }
            }
        }
        return r;
    }

    /// <summary>
    /// Submovement segmentation on the 7 Hz-smoothed hand speed: start when ω &gt; 8°/s, end when ω &lt; 4°/s and
    /// ≥ 80 ms have passed; split at deep minima (&lt; 0.5 × the smaller neighbouring peak) and where ω⃗·u changes sign.
    /// </summary>
    static List<Sub> Segment(Prep p, int i0, int i1, float ux, float uy)
    {
        var raw = new List<(int S, int E)>();
        bool moving = false;
        int s0 = i0;
        for (int i = i0; i <= i1; i++)
        {
            if (!moving && p.Sp[i] > 8f) { moving = true; s0 = i; }
            else if (moving && p.Sp[i] < 4f && (i - s0) * Dsp.Dt >= 0.08) { raw.Add((s0, i)); moving = false; }
        }
        if (moving) raw.Add((s0, i1));

        var pieces = new List<(int S, int E)>();
        foreach (var (s, e) in raw)
        {
            // 1) deep minima between neighbouring peaks
            var maxima = new List<int>();
            for (int i = s + 1; i < e; i++)
                if (p.Sp[i] >= p.Sp[i - 1] && p.Sp[i] > p.Sp[i + 1] && p.Sp[i] > 8f) maxima.Add(i);
            var cuts = new List<int>();
            for (int k = 0; k + 1 < maxima.Count; k++)
            {
                int a = maxima[k], b = maxima[k + 1], mi = a;
                for (int i = a; i <= b; i++) if (p.Sp[i] < p.Sp[mi]) mi = i;
                if (p.Sp[mi] < 0.5f * Math.Min(p.Sp[a], p.Sp[b])) cuts.Add(mi);
            }
            int start = s;
            foreach (var c in cuts.Append(e))
            {
                // 2) direction reversal along the flick axis
                int segStart = start;
                for (int i = start + 1; i < c; i++)
                {
                    float w0 = p.Vx[i - 1] * ux + p.Vy[i - 1] * uy, w1 = p.Vx[i] * ux + p.Vy[i] * uy;
                    if (MathF.Sign(w0) == MathF.Sign(w1) || i - segStart < 5 || c - i < 5) continue;
                    float left = 0, right = 0;
                    for (int k = segStart; k < i; k++) left = Math.Max(left, MathF.Abs(p.Vx[k] * ux + p.Vy[k] * uy));
                    for (int k = i; k <= c; k++) right = Math.Max(right, MathF.Abs(p.Vx[k] * ux + p.Vy[k] * uy));
                    if (left > 8f && right > 8f) { pieces.Add((segStart, i)); segStart = i; }
                }
                pieces.Add((segStart, c));
                start = c;
            }
        }

        var subs = new List<Sub>();
        foreach (var (s, e) in pieces)
        {
            if (e - s < 4) continue;
            float peak = 0;
            for (int i = s; i <= e; i++) peak = Math.Max(peak, p.Sp[i]);
            if (peak <= 8f) continue;
            float dx = p.Gx[e] - p.Gx[s], dy = p.Gy[e] - p.Gy[s];
            subs.Add(new Sub { S = s, E = e, Peak = peak, Dx = dx, Dy = dy, Along = dx * ux + dy * uy, Amp = MathF.Sqrt(dx * dx + dy * dy) });
        }
        return subs;
    }

    /// <summary>Flick speed as a tier: Head Flicks median time-to-hit vs FlickBadges, Spidershot/Gridshot score/min vs badges.</summary>
    static void SpeedTier(Prep p, Dictionary<string, float> m)
    {
        var t = p.T;
        int tier = Math.Clamp(t.Tier, 0, 4);
        float dur = p.Ft[^1] - p.Ft[0];
        int hits = t.Events.Count(e => e.Kind == "target_hit");
        if (hits < 5) return;
        float value;
        if (t.Mode == "flick")
        {
            float med = Dsp.Median(t.Events.Where(e => e.Kind == "target_hit").Select(e => e.B));
            value = Bench.TierOf(med, Difficulty.FlickBadges[tier]);
            m["flick.ttk_hit_ms"] = med;
        }
        else if (t.Mode is "spider" or "gridshot")
        {
            if (dur < 15) return;
            int misses = t.Shots.Count(s => s.Zone < 0), expired = t.Events.Count(e => e.Kind == "target_expired");
            float score = t.Mode == "spider" ? 100 * hits - 20 * misses - 30 * expired : 100 * hits - 25 * misses;
            float perMin = score * 60f / dur;
            m["speed.score_min"] = perMin;
            value = Bench.TierOf(perMin, t.Mode == "spider" ? Difficulty.SpiderBadges[tier] : Difficulty.GridshotBadges[tier]);
        }
        else return;
        if (float.IsNaN(value)) return;
        m["speed.n"] = hits;
        m["speed.tier"] = value;
    }

    // =====================================================================================================
    // A2. Tracking (frames with fire held)
    // =====================================================================================================

    static void Tracking(Prep p, Dictionary<string, float> m)
    {
        var fr = p.T.Frames;
        int n = p.Gn;
        if (n < 240) return;
        // Frame-level on-target %, vertical drift and radius.
        double fireT = 0, onT = 0, vy = 0, rSum = 0, rT = 0;
        for (int i = 1; i < p.N; i++)
        {
            var f = fr[i];
            if (!f.Has(FrameFlags.Firing) || f.FocusId == 0) continue;
            double dt = p.Ft[i] - p.Ft[i - 1];
            fireT += dt;
            if (f.FocusErr <= f.FocusRadius) onT += dt;
            if (float.IsFinite(f.FocusPitch)) vy += -f.FocusPitch * dt;
            if (float.IsFinite(f.FocusRadius) && f.FocusRadius > 0) { rSum += f.FocusRadius * dt; rT += dt; }
        }
        if (fireT < 3 || rT <= 0) return;
        float rMean = (float)(rSum / rT);
        m["track.secs"] = MathF.Round((float)fireT, 1);
        m["track.ontarget"] = (float)(100 * onT / fireT);
        m["track.vdrift_deg"] = (float)(vy / fireT);
        m["track.r_deg"] = rMean;
        int tier = Math.Clamp(p.T.Tier, 0, 4);
        // The spec's on-target benchmark is for a Veteran target: normalised for the tier played (see Bench.TrackTier).
        m["track.ontarget_tier"] = Bench.TrackTier(m["track.ontarget"], tier);

        // 240 Hz series: target yaw (absolute), crosshair error, fire flag.
        var ty = new float[p.N];
        var exf = new float[p.N];
        var eyf = new float[p.N];
        var rf = new float[p.N];
        for (int i = 0; i < p.N; i++)
        {
            var f = fr[i];
            ty[i] = p.Yaw[i] + f.FocusYaw;
            exf[i] = -f.FocusYaw; eyf[i] = -f.FocusPitch; rf[i] = float.IsFinite(f.FocusRadius) && f.FocusRadius > 0 ? f.FocusRadius : rMean;
        }
        var tyg = Dsp.Resample(p.Ft, ty, p.G0, n);
        var ex = Dsp.Resample(p.Ft, exf, p.G0, n);
        var ey = Dsp.Resample(p.Ft, eyf, p.G0, n);
        var rg = Dsp.Resample(p.Ft, rf, p.G0, n);
        var fire = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var f = fr[p.FrameAt(p.Gt(i))];
            fire[i] = f.Has(FrameFlags.Firing) && f.FocusId != 0;
        }
        var vT = Dsp.Deriv(Dsp.LowPass(tyg, 7));
        var vC = p.Vx;

        double absVT = 0; int vtN = 0;
        for (int i = 0; i < n; i++) if (fire[i]) { absVT += MathF.Abs(vT[i]); vtN++; }

        // τ: the delay (0–400 ms) maximising corr(ψ̇c(t), ψ̇T(t − τ)).
        float bestC = -2; int bestL = 0;
        for (int L = 0; L <= 96; L += 2)
        {
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0; int k = 0;
            for (int i = L; i < n; i++)
            {
                if (!fire[i] || !fire[i - L]) continue;
                double a = vC[i], b = vT[i - L];
                sx += a; sy += b; sxx += a * a; syy += b * b; sxy += a * b; k++;
            }
            if (k < 240) continue;
            double cov = sxy / k - sx / k * sy / k, va = sxx / k - (sx / k) * (sx / k), vb = syy / k - (sy / k) * (sy / k);
            if (va <= 1e-9 || vb <= 1e-9) continue;
            float c = (float)(cov / Math.Sqrt(va * vb));
            if (c > bestC) { bestC = c; bestL = L; }
        }
        if (bestC > -2) { m["track.tau_ms"] = (float)(bestL * Dsp.Dt * 1000); m["track.tau_corr"] = bestC; }

        // Jitter: RMS of the 4–15 Hz band of (ψ̇c − ψ̇T), divided by mean |ψ̇T|.
        var dRaw = new float[n];
        var vCr = Dsp.Deriv(p.Gx);
        var vTr = Dsp.Deriv(tyg);
        for (int i = 0; i < n; i++) dRaw[i] = vCr[i] - vTr[i];
        var lo15 = Dsp.LowPass(dRaw, 15, 2);
        var lo4 = Dsp.LowPass(dRaw, 4, 2);
        double js = 0; int jn = 0;
        for (int i = 0; i < n; i++) if (fire[i]) { double h = lo15[i] - lo4[i]; js += h * h; jn++; }
        if (jn > 0 && vtN > 0 && absVT / vtN > 2) m["track.jitter"] = (float)(Math.Sqrt(js / jn) / (absVT / vtN));

        // Reversals / stops of the target → re-acquire time and overrun.
        int win = (int)(0.3 * Dsp.Fs);
        var reacq = new List<float>();
        var over = new List<float>();
        var overEx = new List<float>();
        double lastEvent = -10;
        var transient = new bool[n];
        for (int i = 1; i < n - 1; i++)
        {
            bool reversal = MathF.Sign(vT[i]) != MathF.Sign(vT[i - 1]) && MaxAbs(vT, i - win, i) > 5 && MaxAbs(vT, i, i + win) > 5;
            bool stop = MathF.Abs(vT[i]) < 1.5f && MathF.Abs(vT[i - 1]) >= 1.5f && MaxAbs(vT, i - win, i) > 8 && MaxAbs(vT, i, i + 24) < 1.5f;
            if (!reversal && !stop) continue;
            // t_r = start of the target's deceleration.
            int j = i;
            while (j > 1 && i - j < 96 && MathF.Abs(vT[j - 1]) > MathF.Abs(vT[j]) + 0.02f) j--;
            for (int k = j; k < Math.Min(n, j + (int)(0.4 * Dsp.Fs)); k++) transient[k] = true;
            if (p.Gt(j) - lastEvent < 0.25 || !fire[j]) continue;
            lastEvent = p.Gt(j);
            float oldSign = MathF.Sign(vT[Math.Max(0, j - 1)]), vOld = MathF.Abs(vT[Math.Max(0, j - 1)]);
            float ov = 0;
            for (int k = j; k < Math.Min(n, j + win); k++) ov = Math.Max(ov, ex[k] * oldSign);
            over.Add(ov);
            // How far the crosshair ran past the target's turning point, beyond the carry-over of a normal ~120 ms
            // visual reaction (v·0.12 s): anticipation / over-aggressive corrections show up here.
            float turn = float.MinValue, past = 0;
            for (int k = j; k < Math.Min(n, j + (int)(0.5 * Dsp.Fs)); k++) turn = Math.Max(turn, tyg[k] * oldSign);
            for (int k = j; k < Math.Min(n, j + (int)(0.4 * Dsp.Fs)); k++) past = Math.Max(past, (tyg[k] + ex[k]) * oldSign - turn);
            overEx.Add(Math.Max(0, past - 0.12f * vOld));
            int run = 0, found = -1;
            for (int k = j; k < Math.Min(n, j + (int)(1.5 * Dsp.Fs)); k++)
            {
                if (MathF.Sqrt(ex[k] * ex[k] + ey[k] * ey[k]) <= rg[k]) { if (++run >= 24) { found = k - 23; break; } }
                else run = 0;
            }
            reacq.Add(found < 0 ? 1500f : (float)((found - j) * Dsp.Dt * 1000));
        }
        // Signed lag ℓ (positive = behind) while the target moves steadily: the 0.4 s after each direction change / stop
        // is left out (that's re-acquire / overrun territory), otherwise frequent reversals make everyone look "behind".
        double lagSum = 0, lagAll = 0; int lagN = 0, lagAllN = 0;
        for (int i = 0; i < n; i++)
        {
            if (!fire[i] || MathF.Abs(vT[i]) <= 1f) continue;
            float l = -ex[i] * MathF.Sign(vT[i]);
            lagAll += l; lagAllN++;
            if (!transient[i]) { lagSum += l; lagN++; }
        }
        if (lagN < 240) { lagSum = lagAll; lagN = lagAllN; }
        if (lagN > 0)
        {
            m["track.lag_deg"] = (float)(lagSum / lagN);
            m["track.lag_r"] = (float)(lagSum / lagN) / Math.Max(0.05f, rMean);
            m["track.lag_all_deg"] = (float)(lagAll / Math.Max(1, lagAllN));
        }

        if (reacq.Count > 0)
        {
            m["track.reversals"] = reacq.Count;
            m["track.reacq_ms"] = Dsp.Median(reacq);
            m["track.overrun_deg"] = Dsp.Median(over);
            m["track.overrun_r"] = Dsp.Median(over) / Math.Max(0.05f, rMean);
            m["track.overrun_excess_deg"] = Dsp.Median(overEx);
        }
    }

    static float MaxAbs(float[] v, int a, int b)
    {
        float mx = 0;
        for (int i = Math.Max(0, a); i < Math.Min(v.Length, b); i++) mx = Math.Max(mx, MathF.Abs(v[i]));
        return mx;
    }

    // =====================================================================================================
    // A3. Crosshair placement (bot_seen: A = total error, B = crosshair pitch − head pitch)
    // =====================================================================================================

    static void Crosshair(Prep p, Dictionary<string, float> m)
    {
        var ev = p.T.Events.Where(e => e.Kind == "bot_seen").ToList();
        if (ev.Count == 0) return;
        var fr = p.T.Frames;
        // Peek Practice swings bots out of a random pillar: only head height can be pre-aimed there, so its sightings
        // count for the vertical error but not for the total / horizontal pre-aim error. Flash Dodge asks you to turn
        // away from the flash, so the attacker is first seen far off to the side by design: vertical only as well.
        bool horiz = !HorizExcluded(p.T.Mode);
        var a = new List<float>(); var b = new List<float>(); var h = new List<float>();
        int below = 0, onHead = 0;
        foreach (var e in ev)
        {
            float r = 0.5f;
            int fi = p.FrameAt(e.T);
            for (int k = fi; k < Math.Min(p.N, fi + 4); k++) if (fr[k].FocusId != 0) { r = fr[k].FocusRadius; break; }
            a.Add(e.A); b.Add(e.B);
            h.Add(MathF.Sqrt(Math.Max(0, e.A * e.A - e.B * e.B)));
            if (e.B < -r) below++;
            if (e.A <= r) onHead++;
        }
        m["xhair.n"] = ev.Count;
        m["xhair.pitch_deg"] = Dsp.Mean(b);
        // Typical size of the vertical error (the signed mean lets too-high and too-low sightings cancel out).
        m["xhair.pitch_abs_deg"] = Dsp.Median(b.Select(MathF.Abs));
        m["xhair.below"] = below;
        if (horiz)
        {
            m["xhair.nh"] = ev.Count;
            m["xhair.err_deg"] = Dsp.Median(a);
            m["xhair.h_deg"] = Dsp.Median(h);
            m["xhair.on_head"] = onHead;
        }
    }

    // =====================================================================================================
    // A4. Movement
    // =====================================================================================================

    static void Movement(Prep p, Dictionary<string, float> m)
    {
        var t = p.T;
        var stops = t.Events.Where(e => e.Kind == "stop").ToList();
        if (stops.Count > 0)
        {
            m["move.stops"] = stops.Count;
            m["move.counter"] = stops.Count(s => s.B >= 0.5f);
            m["move.stop_ms"] = Dsp.Mean(stops.Select(s => s.A));
        }
        if (!MovementModes.Contains(t.Mode) || t.Shots.Count == 0) return;
        var shots = t.Shots;
        var moving = shots.Where(s => s.Speed > 1.35f).ToList();
        int early = shots.Count(s => stops.Any(st => s.T >= st.T - st.A / 1000f - 1e-4f && s.T < st.T - st.A / 1000f + 0.104f));
        m["move.shots"] = shots.Count;
        m["move.moving"] = moving.Count;
        m["move.early"] = early;
        if (moving.Count > 0) m["move.moving_speed"] = Dsp.Mean(moving.Select(s => s.Speed));
    }

    // =====================================================================================================
    // A5. Spray (sprays of ≥ 10 bullets; bullets 4–15)
    // =====================================================================================================

    static void Spray(Prep p, Dictionary<string, float> m)
    {
        var t = p.T;
        var sprays = new List<List<ShotSample>>();
        List<ShotSample>? cur = null;
        foreach (var s in t.Shots)
        {
            if (cur == null || s.T - cur[^1].T > 0.2f) { cur = new List<ShotSample>(); sprays.Add(cur); }
            cur.Add(s);
        }
        var used = sprays.Where(s => s.Count >= 10).ToList();
        if (used.Count == 0)
        {
            // Fall back to the mode's own residual events (bullets 4+).
            var res = t.Events.Where(e => e.Kind == "spray_residual").ToList();
            if (res.Count == 0) return;
            m["spray.n"] = res.Count;
            m["spray.v"] = Dsp.Mean(res.Select(e => e.A));
            m["spray.v_abs"] = Dsp.Mean(res.Select(e => MathF.Abs(e.A)));
            m["spray.h"] = MathF.Sqrt(Dsp.Mean(res.Select(e => e.B * e.B)));
            return;
        }
        float required = t.Mode == "spray_phantom" ? 4.6f : 5.5f; // °/s of climb the hand must cancel
        var vs = new List<float>(); var hs = new List<float>(); var delays = new List<float>();
        foreach (var s in used)
        {
            var tail = s.Skip(3).Take(12).ToList();
            vs.Add(tail.Average(b => b.ErrPitch));
            hs.Add(MathF.Sqrt(tail.Average(b => b.ErrYaw * b.ErrYaw)));
            // Compensation delay: downward hand speed ≥ 0.5 × required for 50 ms.
            int i0 = p.Gi(s[0].T), i1 = p.Gi(s[^1].T + 0.1);
            int run = 0, found = -1;
            for (int i = i0; i <= i1; i++)
            {
                if (-p.Vy[i] >= 0.5f * required) { if (++run >= 12) { found = i - 11; break; } }
                else run = 0;
            }
            delays.Add(found < 0 ? (s[^1].T - s[0].T) * 1000f : (float)(p.Gt(found) - s[0].T) * 1000f);
        }
        m["spray.n"] = used.Count;
        m["spray.v"] = Dsp.Mean(vs);
        m["spray.v_abs"] = Dsp.Mean(vs.Select(MathF.Abs));
        m["spray.h"] = MathF.Sqrt(Dsp.Mean(hs.Select(x => x * x)));
        m["spray.delay_n"] = delays.Count;
        m["spray.delay_ms"] = Dsp.Median(delays);
    }

    // =====================================================================================================
    // A6. Reaction and flash
    // =====================================================================================================

    static void Reaction(RunTelemetry t, Dictionary<string, float> m)
    {
        var r = t.Events.Where(e => e.Kind == "reaction").Select(e => e.A).Where(x => x > 80 && x < 1500).ToList();
        int early = t.Events.Count(e => e.Kind == "early_click");
        if (r.Count == 0) return;
        m["react.n"] = r.Count;
        m["react.ms"] = Dsp.Median(r);
        m["react.early"] = early;
    }

    static void Flash(RunTelemetry t, Dictionary<string, float> m)
    {
        var f = t.Events.Where(e => e.Kind == "flash" && e.A >= 0).ToList();
        if (f.Count == 0) return;
        m["flash.n"] = f.Count;
        m["flash.dodge_pct"] = 100f * f.Average(e => e.A == 0 ? 1f : e.A == 1 ? 0.5f : 0f);
        m["flash.flashed_pct"] = 100f * f.Count(e => e.A >= 2) / f.Count;
        var turns = f.Where(e => e.B >= 0).Select(e => e.B).ToList();
        if (turns.Count > 0) { m["flash.turn_n"] = turns.Count; m["flash.turn_ms"] = Dsp.Mean(turns); }
        m["flash.dodge_tier"] = Bench.DodgeTier(m["flash.dodge_pct"], t.Tier);
    }

    static bool HorizExcluded(string mode) => mode is "peek" or "flashmap";

    /// <summary>
    /// A stored run's metrics as the profile uses them (runs saved by older versions are brought up to date without
    /// their telemetry): drops Flash Dodge's horizontal crosshair numbers, (re)computes the difficulty-normalised
    /// tracking and flash-outcome tiers, and puts Spidershot / Gridshot speed on Head Flicks' scale
    /// (<see cref="Bench.ScoreSpeedOffset"/>). Returns a copy when anything changes; the stored record is never modified.
    /// </summary>
    public static Dictionary<string, float> Normalize(string mode, int tier, Dictionary<string, float> m)
    {
        bool dropX = HorizExcluded(mode) && m.ContainsKey("xhair.nh");
        bool dodge = m.ContainsKey("flash.dodge_pct");
        bool track = m.ContainsKey("track.ontarget");
        bool speed = mode is "spider" or "gridshot" && m.ContainsKey("speed.tier");
        if (!dropX && !dodge && !track && !speed) return m;
        var c = new Dictionary<string, float>(m);
        if (dropX)
            foreach (var k in new[] { "xhair.nh", "xhair.err_deg", "xhair.h_deg", "xhair.on_head", "xhair.on_head_pct" }) c.Remove(k);
        if (dodge) c["flash.dodge_tier"] = Bench.DodgeTier(m["flash.dodge_pct"], tier);
        if (track) c["track.ontarget_tier"] = Bench.TrackTier(m["track.ontarget"], tier);
        if (speed) c["speed.tier"] = Math.Max(-0.5f, m["speed.tier"] - Bench.ScoreSpeedOffset);
        return c;
    }
}
