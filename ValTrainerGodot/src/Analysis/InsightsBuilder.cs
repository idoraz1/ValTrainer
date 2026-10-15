using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>
/// Fills <see cref="RunInsights"/> from RunAnalysis's own trials, tracking reversals and sprays (nothing is re-derived
/// here), flagging each instance with the per-instance version of the matching <see cref="Assess"/> rule, judged at the
/// tier played (as a run review is). Moment windows bracket what to watch: from just before the cause to just after.
/// </summary>
static partial class InsightsBuilder
{
    static string F(FormattableString s) => FormattableString.Invariant(s);
    const float Pre = 0.3f, Post = 0.4f;
    const float BaseRtMs = 250f;          // Assess SLOW_START's reaction baseline without Reaction Test data

    sealed class Ctx
    {
        public RunInsights Ins = null!;
        public float TA, TB;
        public int Tier;
        public readonly List<Moment> Mo = new();
        public readonly HashSet<float> Explained = new(); // shot times already shown as PREMATURE / NO_MICRO

        public void Add(float a, float b, string code, string label, int trial = -1)
        {
            if (!float.IsFinite(a) || !float.IsFinite(b)) return;
            a = Math.Clamp(a, TA, TB);
            b = Math.Clamp(Math.Max(a, b), TA, TB);
            Mo.Add(new Moment(a, b, code, label, trial));
        }
    }

    static partial void FillImpl(RunInsights ins)
    {
        var t = ins.Run;
        if (t == null || t.Frames.Count == 0) return;
        // Moment windows are clamped to the recorded span (finite frame times only: a corrupt file must not poison it).
        float ta = float.PositiveInfinity, tb = float.NegativeInfinity;
        foreach (var f in t.Frames) if (float.IsFinite(f.T)) { ta = Math.Min(ta, f.T); tb = Math.Max(tb, f.T); }
        if (tb < ta) return;
        var c = new Ctx { Ins = ins, TA = ta, TB = tb, Tier = Math.Clamp(t.Tier, 0, 4) };

        var p = RunAnalysis.Prepare(t);
        if (p != null)
        {
            Trials(c, p);
            if (t.Mode == "tracking") Tracking(c, p);
            if (t.Mode is "spray_vandal" or "spray_phantom" or "spray_transfer") Sprays(c, p, vertOnly: t.Mode == "spray_transfer");
        }
        Shots(c);
        Events(c);
        ins.Moments.AddRange(c.Mo.OrderBy(m => m.T0).ThenBy(m => m.T1)); // stable: same-time moments keep their order
    }

    /// <summary>Screen direction of a flick (deg, 0 = right, 90 = up) and its 45° sector (0 = right, counter-clockwise).</summary>
    static float Dir(float ux, float uy)
    {
        float d = MathF.Atan2(uy, ux) * 180f / MathF.PI;
        return d < 0 ? d + 360f : d;
    }

    static int Sector(float dirDeg) => (int)MathF.Floor((dirDeg + 22.5f) / 45f) & 7;

    static float Med(List<float> xs) => xs.Count == 0 ? float.NaN : Dsp.Median(xs);

    // ---------------------------------------------------------------------------------------------------------
    // Engagements (flicks / bots)
    // ---------------------------------------------------------------------------------------------------------

    static void Trials(Ctx c, Prep p)
    {
        var ins = c.Ins;
        var t = ins.Run;
        var list = RunAnalysis.Trials(p).OrderBy(x => x.Tr.T0).ToList();
        float bandTc = Bench.ValueAt(c.Tier, Bench.TcMs);
        // Assess SLOW_START: flick onset vs the run's own reaction time (≥ 5 Reaction Test trials) or 250 ms.
        var rts = t.Events.Where(e => e.Kind == "reaction").Select(e => e.A).Where(x => x > 80 && x < 1500).ToList();
        float baseRt = rts.Count >= 5 ? Dsp.Median(rts) : BaseRtMs;
        float[] gainSum = new float[8];

        for (int k = 0; k < list.Count; k++)
        {
            var (tr, r) = list[k];
            float R = Math.Max(0.05f, tr.R);
            var ti = new TrialInfo
            {
                Id = tr.Id, Bot = tr.Bot, Static = tr.Static, Predictable = tr.Predictable, Hit = tr.Hit, Head = R <= 1.2f,
                T0 = tr.T0, TEnd = tr.TEnd, THit = tr.Hit ? tr.THit : float.NaN, RadiusDeg = tr.R,
            };
            float tClick = tr.Hit ? tr.THit : tr.TEnd;
            if (r != null)
            {
                ti.AmpDeg = r.A; ti.RadiusDeg = r.R; ti.Head = r.Head; ti.HasPrimary = r.HasPrimary;
                ti.DirDeg = Dir(r.Ux, r.Uy);
                if (r.HasPrimary)
                {
                    ti.TOnset = (float)r.TsP; ti.TPrimEnd = (float)r.TeP; ti.Gain = r.Gain;
                    if (!tr.Bot) { ti.Over = r.Over; ti.Under = r.Under; } // leading a strafing bot would read as overshoot
                }
                ti.Corrections = r.Nc;
                ti.HasFirstShot = r.HasFirstShot; ti.FirstHit = r.FirstHit; ti.TShot = r.TShot;
                if (r.HasFirstShot) ti.FirstShotErrR = r.ShotErr / R;
                ti.TOnTarget = r.TEnter;
            }
            if (float.IsNaN(ti.TShot))
                foreach (var s in t.Shots)
                    if (s.T > tr.T0 + 1e-5f && s.T <= tClick + 1e-5f) { ti.TShot = s.T; break; }
            if (tr.Hit && float.IsNaN(ti.TOnTarget)) ti.TOnTarget = RunAnalysis.EnterTime(p, tr, R);
            // Keep the timeline ordered (appear ≤ onset ≤ flick end ≤ on target ≤ hit). A shot that hits while the
            // crosshair is still flying ends the engagement mid-flick: its flick ends at the hit (no settle, no click delay).
            if (tr.Hit)
            {
                if (!float.IsNaN(ti.TOnset) && ti.TOnset > ti.THit) ti.TOnset = ti.THit;
                if (!float.IsNaN(ti.TPrimEnd) && ti.TPrimEnd > ti.THit) ti.TPrimEnd = ti.THit;
            }
            if (tr.Hit && !float.IsNaN(ti.TOnTarget) && !float.IsNaN(ti.TPrimEnd))
                ti.TOnTarget = Math.Clamp(ti.TOnTarget, Math.Min(ti.TPrimEnd, ti.THit), ti.THit);
            ins.Trials.Add(ti);

            if (r == null) continue;
            if (r.HasFirstShot) { ins.FirstShots++; if (r.FirstHit) ins.FirstHits++; }
            if (r.HasPrimary && !tr.Bot)
            {
                int sec = Sector(ti.DirDeg);
                ins.SectorN[sec]++;
                if (r.Over) ins.SectorOver[sec]++;
                if (r.Under) ins.SectorUnder[sec]++;
                gainSum[sec] += r.Gain;
            }

            // ---- per-trial problems (Assess.Diagnose's rules, applied to this one flick) ----
            float w0 = tr.T0 - Pre, w1 = tClick + Post;
            if (r.HasPrimary && !tr.Bot)
            {
                if (r.Over && r.S >= 1.5f * R && r.S / r.A >= 0.04f)
                    c.Add(w0, w1, "OVERFLICK", F($"Overshot by {r.S:0.0}° ({100 * r.S / r.A:0}%)"), k);
                if (r.Under && r.Gain < 0.85f)
                    c.Add(w0, w1, "UNDERFLICK", F($"Stopped {-r.S:0.0}° short ({100 * r.Gain:0}% of the way)"), k);
                if (r.EtaP < 0.75f)
                    c.Add(w0, ti.TPrimEnd + Post, "CURVED", F($"Curved flick: {100 * (1 - r.EtaP):0}% of the path wasted"), k);
                else if (r.EndY < -2 * R && r.EndY < -1f)
                    c.Add(w0, ti.TPrimEnd + Post, "CURVED", F($"Flick ended {-r.EndY:0.0}° below the head"), k);
            }
            if (r.HasPrimary && !r.Predictable && r.Onset >= 100 && r.Onset <= 1000 && r.Onset - baseRt > 120)
                c.Add(w0, ti.TPrimEnd + Post, "SLOW_START", F($"Reacted in {r.Onset:0} ms"), k);
            if (r.HasPrimary && tr.Hit && !tr.Bot)
            {
                // Assess's 150 ms bar is for the median; one dwell is noisy (a flick that lands just inside the radius
                // and gets corrected reads as dwell), so a single instance needs the rule's major level.
                if (r.Dwell > 250)
                    c.Add(ti.TOnTarget - Pre, tr.THit + Post, "HESITATE", F($"Waited {r.Dwell:0} ms on the target before clicking"), k);
                if (r.Tc > 1.3f * bandTc || r.Nc >= 3)
                    c.Add(ti.TPrimEnd - Pre, tr.THit + Post, "SLOW_MICRO",
                        r.Nc > 0 ? F($"Took {r.Tc:0} ms and {r.Nc} correction{(r.Nc == 1 ? "" : "s")} to settle") : F($"Took {r.Tc:0} ms to settle"), k);
            }
            for (int j = 0; j < r.PrematureT.Count; j++)
            {
                float st = r.PrematureT[j];
                c.Explained.Add(st);
                c.Add(Math.Max(w0, st - 1f), st + Post, "PREMATURE", F($"Fired while still moving at {r.PrematureDps[j]:0}°/s"), k);
            }
            for (int j = 0; j < r.SnapT.Count; j++)
            {
                float st = r.SnapT[j];
                c.Explained.Add(st);
                c.Add(Math.Max(w0, st - 1f), st + Post, "NO_MICRO", F($"Fired straight off the flick, {r.SnapErr[j]:0.0}° off"), k);
            }
        }
        for (int s = 0; s < 8; s++) if (ins.SectorN[s] > 0) ins.SectorGain[s] = gainSum[s] / ins.SectorN[s];

        // Time-to-hit split over engagements that ended in a hit.
        var hits = ins.Trials.Where(x => x.Hit && x.HasPrimary && !float.IsNaN(x.TOnTarget)).ToList();
        if (hits.Count > 0)
        {
            ins.Ttk = new TtkBreakdown
            {
                N = hits.Count,
                ReactMs = 1000 * Med(hits.Select(x => x.TOnset - x.T0).ToList()),
                FlickMs = 1000 * Med(hits.Select(x => x.TPrimEnd - x.TOnset).ToList()),
                SettleMs = 1000 * Med(hits.Select(x => Math.Max(0, x.TOnTarget - x.TPrimEnd)).ToList()),
                ClickMs = 1000 * Med(hits.Select(x => Math.Max(0, x.THit - Math.Max(x.TOnTarget, x.TPrimEnd))).ToList()),
                TotalMs = 1000 * Med(hits.Select(x => x.THit - x.T0).ToList()),
            };
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Tracking reversals and sprays
    // ---------------------------------------------------------------------------------------------------------

    static void Tracking(Ctx c, Prep p)
    {
        var behind = new List<RunAnalysis.LagStretch>();
        var revs = RunAnalysis.Reversals(p, out float rMean, behind);
        float bandRq = Bench.ValueAt(c.Tier, Bench.ReacqMs);
        foreach (var b in behind)
            c.Add(b.T0 - Pre, b.T1 + Pre, "TRACK_BEHIND", F($"Trailed the target by {b.LagDeg:0.0}° for {b.T1 - b.T0:0.0} s"));
        foreach (var v in revs)
        {
            float end = v.T + Math.Min(v.ReacqMs, 1500f) / 1000f + Pre;
            if (v.ReacqMs > 1.3f * bandRq)
                c.Add(v.T - Pre, end, "SLOW_REACQ", v.ReacqMs >= 1500f ? "Not back on target within 1.5 s of a direction change"
                    : F($"Took {v.ReacqMs:0} ms to get back on after a direction change"));
            if (rMean > 0 && v.OverrunExcessDeg > 1.5f * rMean)
                c.Add(v.T - Pre, end, "TRACK_OVERRUN", F($"Ran {v.OverrunDeg:0.0}° past the target when it turned"));
        }
    }

    static void Sprays(Ctx c, Prep p, bool vertOnly = false)
    {
        foreach (var s in RunAnalysis.Sprays(p))
        {
            float a = s.T0 - Pre, b = s.T1 + Post;
            if (MathF.Abs(s.V) > 1.0f)
                c.Add(a, b, "SPRAY_VERT", s.V >= 0 ? F($"Spray climbed {s.V:0.0}° over the target") : F($"Pulled the spray {-s.V:0.0}° under the target"));
            else if (!vertOnly && s.DelayMs > 300)
                c.Add(a, b, "SPRAY_VERT", F($"Started pulling down {s.DelayMs:0} ms into the spray"));
            if (!vertOnly && s.H > 0.9f) c.Add(a, b, "SPRAY_HORIZ", F($"Late bullets drifted {s.H:0.0}° sideways"));
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Shots: scatter, counts, run-and-gun and plain misses
    // ---------------------------------------------------------------------------------------------------------

    static void Shots(Ctx c)
    {
        var ins = c.Ins;
        var t = ins.Run;
        ins.Shots = t.Shots.Count;
        var stops = t.Events.Where(e => e.Kind == "stop").ToList();
        List<ShotSample>? moving = null, missed = null;
        void FlushMoving()
        {
            if (moving == null) return;
            float v = moving.Average(s => s.Speed);
            c.Add(moving[0].T - 0.6f, moving[^1].T + Post, "RUN_GUN", moving.Count == 1 ? F($"Fired while moving at {v:0.0} m/s")
                : F($"{moving.Count} shots fired while moving at {v:0.0} m/s"), TrialAt(ins, moving[0].T));
            moving = null;
        }
        void FlushMissed()
        {
            if (missed == null) return;
            var s0 = missed[0];
            string label = missed.Count > 1 ? F($"Missed {missed.Count} shots in a row")
                : s0.FocusId == 0 || !float.IsFinite(s0.Err) ? "Missed"
                : s0.FocusRadius > 0 ? F($"Missed by {s0.Err:0.0}° ({s0.Err / s0.FocusRadius:0.0}× the target radius)")
                : F($"Missed by {s0.Err:0.0}°");
            c.Add(s0.T - 0.6f, missed[^1].T + Post, "MISS", label, TrialAt(ins, s0.T));
            missed = null;
        }
        foreach (var s in t.Shots)
        {
            if (!float.IsFinite(s.T)) continue;
            if (s.FocusId != 0 && s.FocusRadius > 0 && float.IsFinite(s.ErrYaw) && float.IsFinite(s.ErrPitch))
                ins.Scatter.Add(new ScatterShot(s.ErrYaw / s.FocusRadius, s.ErrPitch / s.FocusRadius, s.Zone, s.T, !s.Accurate));
            if (!s.Accurate) ins.MovingShots++;

            // RUN_GUN (Assess: shots over 1.35 m/s, or within 0.1 s of starting to stop); one moment per burst.
            bool early = stops.Any(st => s.T >= st.T - st.A / 1000f - 1e-4f && s.T < st.T - st.A / 1000f + 0.104f);
            if (s.Speed > 1.35f || early)
            {
                if (moving != null && s.T - moving[^1].T > 0.25f) FlushMoving();
                (moving ??= new()).Add(s);
            }
            else FlushMoving();

            if (s.Zone < 0 && !c.Explained.Contains(s.T))
            {
                if (missed != null && s.T - missed[^1].T > 0.25f) FlushMissed();
                (missed ??= new()).Add(s);
            }
            else FlushMissed();
        }
        FlushMoving();
        FlushMissed();
    }

    /// <summary>Index of the engagement running at time tt (latest start), or −1.</summary>
    static int TrialAt(RunInsights ins, float tt)
    {
        for (int k = ins.Trials.Count - 1; k >= 0; k--)
        {
            var x = ins.Trials[k];
            if (x.T0 <= tt + 1e-4f && tt <= x.TEnd + 0.05f) return k;
        }
        return -1;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Events: crosshair placement, reaction, flash, stops, kills and deaths
    // ---------------------------------------------------------------------------------------------------------

    static void Events(Ctx c)
    {
        var ins = c.Ins;
        var t = ins.Run;
        var fr = t.Frames;
        bool horiz = RunAnalysis.HorizCounts(t.Mode);
        float bandE = Bench.ValueAt(c.Tier, Bench.XhairErrDeg), bandR = Bench.ValueAt(c.Tier, Bench.ReactMs);
        float bandF = Bench.ValueAt(c.Tier, Bench.FlashTurnMs);
        float reactLimit = Math.Min(bandR + 30, 300);
        int fi = 0;
        foreach (var e in t.Events)
        {
            if (!float.IsFinite(e.T)) continue;
            switch (e.Kind)
            {
                case "bot_seen":
                {
                    if (e.A > 60f) break; // beside / behind you (older telemetry): not placement, as in RunAnalysis.Crosshair
                    // Target radius from the first focused frame (RunAnalysis.Crosshair's rule).
                    while (fi + 1 < fr.Count && fr[fi + 1].T <= e.T + 1e-6f) fi++;
                    float r = 0.5f;
                    for (int k = fi; k < Math.Min(fr.Count, fi + 4); k++) if (fr[k].FocusId != 0) { r = fr[k].FocusRadius; break; }
                    int ti = ins.Trials.FindIndex(x => x.Bot && MathF.Abs(x.T0 - e.T) < 1e-3f);
                    float end = ti >= 0 ? Math.Min(ins.Trials[ti].TEnd, e.T + 2f) + Post : e.T + 1f;
                    if (e.B < -r && e.B <= -1.5f)
                        c.Add(e.T - 0.5f, end, "XHAIR_LOW", F($"Crosshair {-e.B:0.0}° below head height"), ti);
                    float h = MathF.Sqrt(Math.Max(0, e.A * e.A - e.B * e.B));
                    if (horiz && h > bandE)
                        c.Add(e.T - 0.5f, end, "XHAIR_WIDE", F($"Crosshair {h:0.0}° to the side when the enemy appeared"), ti);
                    break;
                }
                case "reaction":
                    if (e.A > 80 && e.A < 1500 && e.A > reactLimit)
                        c.Add(e.T - e.A / 1000f - Pre, e.T + Post, "SLOW_REACT", F($"Reacted in {e.A:0} ms"));
                    break;
                case "flash":
                    if (e.A >= 0 && e.B >= 0 && e.B > bandF + 100)
                        c.Add(e.T - e.B / 1000f - 0.5f, e.T + 0.6f, "FLASH_SLOW", F($"Turned away {e.B:0} ms after the cue"));
                    break;
                case "stop":
                    // Assess CLUMSY_MOVE: stops that weren't counter-strafed, or slow to reach accurate speed.
                    if (e.B < 0.5f || e.A + Bench.StopOffsetMs > 200)
                        c.Add(e.T - e.A / 1000f - 0.5f, e.T + Post, "CLUMSY_MOVE",
                            e.B < 0.5f ? F($"Stopped without counter-strafing ({e.A:0} ms)") : F($"Slow stop: {e.A:0} ms to accurate speed"));
                    break;
                case "kill":
                {
                    float ttk = float.IsFinite(e.A) && e.A > 0 ? Math.Min(e.A / 1000f, 2.5f) : 0.5f;
                    int ti = ins.Trials.FindLastIndex(x => x.Bot && x.T0 <= e.T + 1e-4f && e.T <= x.TEnd + 0.1f);
                    c.Add(e.T - ttk - Pre, e.T + Post, "KILL", (e.A > 0 ? F($"Kill in {e.A:0} ms") : "Kill") + (e.B >= 1 ? " · headshot" : ""), ti);
                    break;
                }
                case "died":
                    c.Add(e.T - 2.5f, e.T + 0.6f, "DEATH", "You died", TrialAt(ins, e.T));
                    break;
            }
        }
    }
}
