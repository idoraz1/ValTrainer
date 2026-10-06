namespace ValTrainer.Modes;

/// <summary>
/// Adaptive difficulty for the Lock-In calibration drills: a weighted up/down staircase (Kaernbach 1991) on a continuous
/// tier level (0 = Rookie … 4 = Pro) that settles where the player hits <see cref="TargetHitRate"/> of the trials.
/// A hit raises the level by <c>Step · (1 − p)</c>, a miss lowers it by <c>Step · p</c>, so the expected change is zero
/// exactly at hit rate p. The step starts big (fast approach from a wrong start level) and shrinks after reversals, so a
/// drill of 50–150 trials (about a minute of flicks / spider / gridshot) settles within the run.
/// </summary>
public sealed class Staircase
{
    /// <summary>Full step (levels) by reversal count: 0–1 → 1.0, 2–3 → 0.6, 4–5 → 0.4, 6–11 → 0.3, then <see cref="MinStep"/>
    /// (flicks reach 12 reversals in about 35 s, gridshot / spider in about 15 s).</summary>
    static readonly float[] Steps = { 1.0f, 1.0f, 0.6f, 0.6f, 0.4f, 0.4f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f };
    const float MinStep = 0.2f;
    public const float MinLevel = 0f, MaxLevel = 4f;

    readonly List<float> levels = new(), outcomes = new(); // level each trial was played at; 1 = hit, 0 = miss
    int hits, reversals, lastDir, reversal4At = -1;

    public Staircase(float startLevel, float targetHitRate = 0.8f)
    {
        Level = StartLevel = Math.Clamp(startLevel, MinLevel, MaxLevel);
        TargetHitRate = Math.Clamp(targetHitRate, 0.05f, 0.95f);
    }

    /// <summary>A new staircase with the same start level and target (a restarted run starts over).</summary>
    public Staircase Fresh() => new(StartLevel, TargetHitRate);

    public float StartLevel { get; }
    public float TargetHitRate { get; }
    /// <summary>Current level; modes read it at every spawn.</summary>
    public float Level { get; private set; }
    public int Trials => levels.Count;
    public int Reversals => reversals;
    /// <summary>Hits / trials over the whole run (0 before the first trial).</summary>
    public float HitRate => Trials == 0 ? 0 : (float)hits / Trials;
    /// <summary>Current full step size (levels).</summary>
    public float Step => reversals < Steps.Length ? Steps[reversals] : MinStep;

    /// <summary>
    /// Estimate of the level held at the target hit rate: the mean level over the second half of the trials, or since the
    /// 4th reversal if that came later (the approach from the start level is left out). −1 with fewer than 8 trials.
    /// </summary>
    public float Threshold => Trials < 8 ? -1f : Mean(levels, SettledFrom);

    /// <summary>Hit rate over the same trials as <see cref="Threshold"/> (−1 with fewer than 8 trials).</summary>
    public float SettledHitRate => Trials < 8 ? -1f : Mean(outcomes, SettledFrom);

    /// <summary>First trial of the settled part of the run (see <see cref="Threshold"/>).</summary>
    int SettledFrom
    {
        get
        {
            int from = Math.Max(Trials / 2, reversal4At);
            return from >= Trials - 3 ? Trials / 2 : from; // 4th reversal came too late to average over: the second half
        }
    }

    static float Mean(List<float> v, int from)
    {
        float sum = 0;
        for (int i = from; i < v.Count; i++) sum += v[i];
        return sum / (v.Count - from);
    }

    /// <summary>The trial was a hit: the next targets get harder.</summary>
    public void Hit() => Trial(true);

    /// <summary>A miss or an expired target: the next targets get easier.</summary>
    public void Miss() => Trial(false);

    void Trial(bool hit)
    {
        levels.Add(Level);
        outcomes.Add(hit ? 1f : 0f);
        if (hit) hits++;
        int dir = hit ? 1 : -1;
        float step = Step; // the step this trial was played with (a reversal shrinks it from the next trial on)
        if (lastDir != 0 && dir != lastDir && ++reversals == 4) reversal4At = Trials;
        lastDir = dir;
        Level = Math.Clamp(Level + (hit ? step * (1f - TargetHitRate) : -step * TargetHitRate), MinLevel, MaxLevel);
    }

    public override string ToString() =>
        $"level {Level:0.00} threshold {Threshold:0.00} hit {HitRate * 100:0}% (settled {SettledHitRate * 100:0}%) trials {Trials} reversals {reversals} step {Step:0.00}";
}
