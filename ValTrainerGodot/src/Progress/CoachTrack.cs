using ValTrainer.Analysis;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Progression;

/// <summary>
/// Confirmed coach ranks (progression-design §4.2). One sample per day (the day's last real profile), 40 days kept.
/// A sample counts with confidence ≥ 0.75 and tier ≥ 0 from the current <see cref="Bench.Calibration"/>. The first 3
/// valid days place a track (overall: +300 XP and <c>coach:placed</c>, no stars); after that the confirmed rank is
/// <c>CoachRank.Index(median of the last 5 valid days after the placement day − 0.10)</c>, and a rank-up is paid when it
/// passes the track's high-water mark (skill: +300 XP, 2★; overall: +1,000 XP, 5★). The mark only rises; falling back
/// takes nothing away. A calibration change re-places every track silently and keeps everything earned.
/// </summary>
internal sealed partial class ProgressEngine
{
    public const string OverallTrack = "overall";
    public const float MinConfidence = 0.75f, Margin = 0.10f;
    public const int PlaceDays = 3, ConfirmDays = 5, KeepDays = 40;

    static bool Valid(float[]? s) => s is { Length: >= 2 } && s[0] >= 0 && !float.IsNaN(s[0]) && s[1] >= MinConfidence - 1e-4f;

    static float Median(List<float> v)
    {
        v.Sort();
        int n = v.Count;
        return n == 0 ? float.NaN : n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2f;
    }

    static float[]? SampleOf(CoachDay d, string track) =>
        track == OverallTrack ? d.O : d.S != null && d.S.TryGetValue(track, out var s) ? s : null;

    /// <summary>A real (not fake) coach profile finished: store today's sample, the focus drills, and confirm ranks.
    /// Returns the XP it paid (placement / rank-ups).</summary>
    public int OnProfile(SkillProfile p, DateTime now, List<XpPart>? parts, List<string>? reasons)
    {
        int xp0 = F.Xp;
        var c = F.Coach ??= new CoachTrackInfo();
        var top = p.Problems.FirstOrDefault(pr => pr.Drills.Any(ValidDrill));
        if (top != null)
        {
            c.FocusDrills = top.Drills.Where(ValidDrill).Distinct().ToList();
            c.FocusWhy = top.Id;
            c.FocusWhyText = top.Title;
        }
        else if (p.RunsAnalyzed > 0) { c.FocusDrills = null; c.FocusWhy = null; c.FocusWhyText = null; }
        var weakest = p.Skills.Where(s => s.Confidence > 0 && s.Tier >= 0 && !s.IsProgress).OrderBy(s => s.Tier).FirstOrDefault();
        if (weakest != null) c.Weakest = weakest.Skill.ToString();

        if (c.Calib != Bench.Calibration)
        {
            // A recalibration: samples of the old scale are dropped and every track re-places quietly. Earned rank-ups,
            // stars and unlocks stay (OverallUps / SkillUps / Ever are kept).
            if (c.Calib != 0 && c.PlacedOn != null) c.Recalibrated = true;
            c.Calib = Bench.Calibration;
            c.Days = new();
            c.Placed = null;
            c.High = null;
            c.PlacedOn = null;
            c.PlacedDays = null;
        }
        bool hasData = p.OverallTier >= 0 || p.Skills.Any(s => s.Confidence > 0 && s.Tier >= 0);
        if (hasData)
        {
            string day = ProgressTime.DayKey(now);
            c.Days ??= new();
            if (c.Days.Count > 0 && string.CompareOrdinal(day, c.Days[^1].D) < 0) day = c.Days[^1].D; // clock went back
            var sample = new CoachDay
            {
                D = day,
                O = new[] { p.OverallTier, p.OverallConfidence },
                S = p.Skills.Where(s => !s.IsProgress).GroupBy(s => s.Skill).ToDictionary(g => g.Key.ToString(), g => new[] { g.First().Tier, g.First().Confidence }),
            };
            c.Days.RemoveAll(x => x.D == day);
            c.Days.Add(sample);
            c.Days.Sort((a, b) => string.CompareOrdinal(a.D, b.D));
            if (c.Days.Count > KeepDays) c.Days.RemoveRange(0, c.Days.Count - KeepDays);

            if (c.RetroPending)
            {
                // Retro credit: no history was sampled, so the current estimate becomes the placement (one sample).
                c.RetroPending = false;
                if (Valid(sample.O))
                {
                    PlaceTrack(c, OverallTrack, CoachRank.Index(sample.O![0]), day, parts);
                    foreach (var (k, v) in sample.S) if (Valid(v)) PlaceTrack(c, k, CoachRank.Index(v[0]), day, parts);
                    if (F.Retro is { Shown: false } r) { r.Xp = F.Xp; r.Stars = F.Stars; }
                }
            }
            ConfirmAll(c, day, parts, reasons);
        }
        else c.RetroPending = c.RetroPending && p.RunsAnalyzed == 0; // nothing to place from: retry with the next profile only if it was empty
        CheckUnlocks("coach", now);
        return F.Xp - xp0;
    }

    void PlaceTrack(CoachTrackInfo c, string track, int rank, string day, List<XpPart>? parts)
    {
        c.Placed ??= new();
        c.High ??= new();
        c.PlacedDays ??= new();
        c.Placed[track] = rank;
        c.High[track] = rank;
        c.PlacedDays[track] = day;
        if (track != OverallTrack) return;
        c.PlacedOn = day;
        if (!c.Ever) Grant(parts, L.N("Coach placed"), XpRules.PlacementXp); // a re-placement after a recalibration pays nothing
        c.Ever = true;
    }

    void ConfirmAll(CoachTrackInfo c, string today, List<XpPart>? parts, List<string>? reasons)
    {
        if (c.Days == null) return;
        var tracks = new List<string> { OverallTrack };
        tracks.AddRange(c.Days.Where(d => d.S != null).SelectMany(d => d.S!.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal));
        foreach (var track in tracks)
        {
            var valid = c.Days.Where(d => Valid(SampleOf(d, track))).ToList();
            if (c.Placed == null || !c.Placed.ContainsKey(track))
            {
                if (valid.Count >= PlaceDays)
                    PlaceTrack(c, track, CoachRank.Index(Median(valid.TakeLast(ConfirmDays).Select(d => SampleOf(d, track)![0]).ToList())), today, parts);
                continue;
            }
            string placedDay = c.PlacedDays != null && c.PlacedDays.TryGetValue(track, out var pd) ? pd : track == OverallTrack ? c.PlacedOn ?? "" : "";
            var after = valid.Where(d => string.CompareOrdinal(d.D, placedDay) > 0).ToList();
            if (after.Count < ConfirmDays) continue;
            float med = Median(after.TakeLast(ConfirmDays).Select(d => SampleOf(d, track)![0]).ToList()) - Margin;
            int rank = CoachRank.Index(med);
            c.High ??= new();
            int high = c.High.TryGetValue(track, out var h) ? h : c.Placed[track];
            if (rank <= high) continue;
            c.High[track] = rank;
            string rankName = CoachRank.Names[Math.Clamp(rank, 0, CoachRank.Names.Length - 1)];
            if (track == OverallTrack)
            {
                c.OverallUps++;
                Grant(parts, L.N("Rank up"), XpRules.OverallRankUpXp);
                Star(XpRules.OverallRankUpStars, "RANK UP", reasons, () => Core.L.F($"COACH RANK-UP · {Core.L.T(rankName)}"));
            }
            else
            {
                c.SkillUps++;
                Grant(parts, L.N("Skill up"), XpRules.SkillRankUpXp);
                string skill = Enum.TryParse<Skill>(track, out var sk) ? Coach.SkillName(sk) : track;
                Star(XpRules.SkillRankUpStars, "SKILL UP", reasons, () => Core.L.F($"{Core.L.T(skill)} RANK-UP · {Core.L.T(rankName)}"));
            }
        }
    }

    /// <summary>The confirmed rank of a track now (-1 = not placed), and what the next rank-up needs (for the UI).</summary>
    public (int Rank, int ValidAfter, int Need) TrackState(string track)
    {
        var c = F.Coach;
        if (c?.Placed == null || !c.Placed.ContainsKey(track)) return (-1, c?.Days?.Count(d => Valid(SampleOf(d, track))) ?? 0, PlaceDays);
        string placedDay = c.PlacedDays != null && c.PlacedDays.TryGetValue(track, out var pd) ? pd : c.PlacedOn ?? "";
        int after = c.Days?.Count(d => Valid(SampleOf(d, track)) && string.CompareOrdinal(d.D, placedDay) > 0) ?? 0;
        return (c.High != null && c.High.TryGetValue(track, out var h) ? h : c.Placed[track], after, ConfirmDays);
    }
}
