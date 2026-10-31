using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Bots;

/// <summary>
/// Player-like strafing for a bot along one axis: short ADAD micro-strafes, long strafes, crouch-strafes,
/// A-crouch / D-crouch, jiggles, and counter-strafed stops in which it may shoot. Built for Strafe Duels and shared with
/// the Deathmatch bots' fights.
/// <para>The caller owns the axis and the position: each frame it passes where the bot is along the axis and how far it
/// may go (<c>laneLo</c> … <c>laneHi</c>, same units), moves the bot by <see cref="V"/>·dt, crouches it with
/// <see cref="Crouch"/> and lets it fire only while <see cref="MayShoot"/> (<see cref="BotBrain.HoldFire"/>).</para>
/// <list type="bullet">
/// <item>Acceleration and braking are the player's (<see cref="Mover"/>: <see cref="Mover.GroundAccel"/> 18 m/s², full
/// speed in ≈0.3 s; brake 38 / 24 m/s² = Riot's measured ≈105 ms to accurate, ≈160 ms to a full stop; counter-strafe 6%
/// quicker), the same constants as Mixed Peeks (PeekMixMode).</item>
/// <item>It only shoots below the accuracy deadzone (<see cref="Mover.AccurateSpeed"/>, 27.5% of max speed, Riot patch
/// 3.0) and after its tier's settle delay.</item>
/// <item>Cadences (ADAD 0.16–0.45 s per direction, jiggles 0.10–0.16 s) are project estimates; sources and reasoning in
/// StrafeDuelMode's summary. Tune them here: Strafe Duels and Deathmatch both use these tables.</item>
/// </list>
/// The planner draws from the <see cref="Random"/> it is given in a fixed order, so a mode that shares its session
/// random keeps the exact sequence it had before the extraction.
/// </summary>
public sealed class StrafePlanner
{
    // ---- movement: the player's horizontal model (Mover.cs) ----
    public const float Accel = Mover.GroundAccel, BrakeHigh = 38f, BrakeLow = 24f, CounterBonus = 1.06f;
    public const float CrouchSpeed = Mover.CrouchSpeed;      // 2.43 m/s

    public enum Pat { Long, Micro, CrouchStrafe, ACrouchD, Jiggle }
    public static readonly string[] PatNames = { "Long strafe", "ADAD micro-strafe", "Crouch-strafe", "A-crouch / D-crouch", "Jiggle" };
    public static readonly string[] PatShort = { "long", "ADAD", "crouch", "ACD", "jiggle" };
    // Pattern weights per tier: Rookie = long predictable strafes; Pro = mostly micro-strafes, A-crouch-D and jiggles.
    static readonly int[][] Weights =
    {
        new[] { 6, 1, 0, 0, 0 },
        new[] { 4, 3, 1, 1, 0 },
        new[] { 2, 4, 2, 2, 1 },
        new[] { 1, 4, 2, 3, 2 },
        new[] { 1, 4, 2, 3, 3 },
    };

    /// <summary>One "key state" of the bot: strafe direction (−1 left, 0 none), crouch, and whether it may shoot.</summary>
    public readonly record struct Seg(int Dir, float Dur, bool Crouch, bool Shoot);

    // ---- tiers (Rookie … Pro) ----
    public float RunCap => Difficulty.T(Tier, 2.97f, 4.2f, 5.4f, 5.4f, 5.4f);           // Rookie walks, Regular jogs
    (float, float) LongDur => Tier switch { 0 => (1.0f, 1.6f), 1 => (0.8f, 1.3f), 2 => (0.6f, 1.1f), 3 => (0.5f, 0.9f), _ => (0.4f, 0.8f) };
    (float, float) TapDur => Tier switch { 0 => (0.45f, 0.6f), 1 => (0.35f, 0.5f), 2 => (0.25f, 0.42f), 3 => (0.2f, 0.35f), _ => (0.16f, 0.3f) };
    (float, float) StopDur => Tier switch { 0 => (0.9f, 1.3f), 1 => (0.7f, 1.0f), 2 => (0.5f, 0.8f), 3 => (0.35f, 0.6f), _ => (0.25f, 0.45f) };
    /// <summary>Stopped (below the deadzone) → first trigger pull: Rookie settles before shooting, Pro shoots the moment it's accurate.</summary>
    float ShootDelay => Difficulty.T(Tier, .25f, .17f, .10f, .06f, .03f);
    /// <summary>Chance a movement pattern flows straight into the next one with no stop (a fake-out).</summary>
    float ChainChance => Difficulty.T(Tier, 0f, .1f, .2f, .3f, .35f);
    /// <summary>Chance a stop is crouched (crouch-shoot); Elite+ also crouch-spam during it.</summary>
    float CrouchStopChance => Difficulty.T(Tier, 0f, .1f, .3f, .45f, .55f);

    readonly Random rng;
    readonly Queue<Seg> plan = new();
    Seg seg;
    float segEnd, readyAt = -1;
    int lastDir = 1;

    /// <summary>Tier 0 Rookie … 4 Pro (pattern mix, cadence, stop length, settle delay).</summary>
    public int Tier;
    /// <summary>Dev: always plan this pattern (0 long … 4 jiggle); −1 = by the tier's weights.</summary>
    public int ForcePattern = -1;
    /// <summary>Called when a pattern is chosen (telemetry, dev log, per-pattern counts).</summary>
    public Action<Pat>? PatternPicked;
    /// <summary>Dev log: the key states just queued for a pattern, e.g. "A0.31 D0.22 stop0.40". Null = not built.</summary>
    public Action<string>? Planned;

    /// <summary>Signed speed along the axis (m/s): the caller moves the bot by V·dt.</summary>
    public float V;
    /// <summary>The current key state.</summary>
    public Seg Current => seg;
    public bool Crouch => seg.Crouch;
    /// <summary>In a stop phase, below the accuracy deadzone and past the tier's settle delay.</summary>
    public bool MayShoot { get; private set; }

    public StrafePlanner(int tier, Random rng)
    {
        Tier = tier;
        this.rng = rng;
    }

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>Starts a fresh plan (a pattern that moves, then a stop) at <paramref name="pos"/>.</summary>
    /// <param name="v0">Speed along the axis right now; null = it appears already strafing at 60–100% of its run cap
    /// (Strafe Duels: no standing pop-in).</param>
    public void Begin(float now, float pos, float laneLo, float laneHi, float? v0 = null)
    {
        plan.Clear();
        PlanPattern(forceMove: true);
        NextSeg(now, pos, laneLo, laneHi);
        V = v0 ?? seg.Dir * RunCap * R(0.6f, 1f);
        readyAt = -1;
        MayShoot = false;
    }

    /// <summary>One frame: advances the key states (turning before the lane's ends), accelerates / brakes like the player
    /// and updates <see cref="MayShoot"/>. Returns <see cref="V"/>.</summary>
    public float Step(float now, float dt, float pos, float laneLo, float laneHi)
    {
        // Running out of lane: end this strafe early, so the brake lands inside it (stopping distance v²/2a).
        if (seg.Dir != 0)
        {
            float stopAt = pos + V * Mathf.Abs(V) / (2f * BrakeHigh);
            if ((seg.Dir > 0 && stopAt > laneHi) || (seg.Dir < 0 && stopAt < laneLo)) segEnd = now;
        }
        while (now >= segEnd) NextSeg(now, pos, laneLo, laneHi);

        float cap = seg.Crouch ? CrouchSpeed : RunCap;
        float brake = (Mathf.Abs(V) > Mover.RunSpeed * 0.25f ? BrakeHigh : BrakeLow) * dt;
        if (seg.Dir == 0) V = Mathf.MoveToward(V, 0, brake);
        else
        {
            float along = V * seg.Dir;
            if (along < 0) along = Mathf.Min(0, along + brake * CounterBonus);   // counter-strafe through zero
            else if (along > cap) along = Mathf.Max(cap, along - brake);         // crouching from a run
            else along = Mathf.Min(cap, along + Accel * dt);
            V = along * seg.Dir;
        }

        // Shoots only in its stop phases, once below the deadzone and past its tier's settle delay.
        bool slow = Mathf.Abs(V) <= Mover.AccurateSpeed;
        if (seg.Shoot && slow) { if (readyAt < 0) readyAt = now + ShootDelay; }
        else readyAt = -1;
        MayShoot = readyAt >= 0 && now >= readyAt;
        return V;
    }

    /// <summary>The crosshair is on: cut the current strafe short and counter-strafe into the pattern's next stop (the moves
    /// queued before it are dropped). What a player does once the aim has settled. No random draws. False when it is
    /// already stopping or no stop is queued (a chained pattern).</summary>
    public bool StopSoon(float now)
    {
        if (seg.Dir == 0 || !plan.Any(s => s.Dir == 0)) return false;
        while (plan.Peek().Dir != 0) plan.Dequeue();
        segEnd = now;
        return true;
    }

    // ---------------------------------------------------------------- planning

    Pat PickPattern()
    {
        if (ForcePattern is >= 0 and <= 4) return (Pat)ForcePattern;
        var w = Weights[Math.Clamp(Tier, 0, Weights.Length - 1)];
        int r = rng.Next(w.Sum());
        for (int i = 0; i < w.Length; i++) { if (r < w[i]) return (Pat)i; r -= w[i]; }
        return Pat.Long;
    }

    void Add(int dir, float dur, bool crouch = false, bool shoot = false) => plan.Enqueue(new Seg(dir, dur, crouch, shoot));

    /// <summary>Queues one movement pattern and (usually) a stop to shoot after it.</summary>
    void PlanPattern(bool forceMove = false)
    {
        var pat = PickPattern();
        PatternPicked?.Invoke(pat);
        int d = rng.NextDouble() < 0.65 ? -lastDir : lastDir; // usually reverses, sometimes re-strafes the same way
        var (t0, t1) = TapDur;
        switch (pat)
        {
            case Pat.Long:
            {
                var (l0, l1) = LongDur;
                Add(d, R(l0, l1));
                break;
            }
            case Pat.Micro:
            {
                int n = 3 + rng.Next(Difficulty.Ti(Tier, 1, 2, 3, 4, 4));
                for (int i = 0; i < n; i++) { Add(d, R(t0, t1)); d = -d; }
                break;
            }
            case Pat.CrouchStrafe:
                Add(d, R(0.45f, 0.8f), crouch: true);
                if (rng.NextDouble() < 0.5) Add(-d, R(0.35f, 0.6f), crouch: true);
                break;
            case Pat.ACrouchD:
            {
                // Strafe, crouch as you stop (a quick crouched shot), stand, strafe the other way.
                int n = 2 + rng.Next(2);
                for (int i = 0; i < n; i++) { Add(d, R(t0, t1) * 1.2f); Add(0, R(0.16f, 0.3f), crouch: true, shoot: true); d = -d; }
                break;
            }
            case Pat.Jiggle:
            {
                int n = 2 + rng.Next(3);
                for (int i = 0; i < n; i++) { Add(d, R(0.10f, 0.16f)); d = -d; }
                break;
            }
        }
        if (forceMove || rng.NextDouble() >= ChainChance) AddStop();
        Planned?.Invoke(string.Join(" ", plan.Select(s => (s.Dir < 0 ? "A" : s.Dir > 0 ? "D" : "stop") + (s.Crouch ? "+C" : "") + $"{s.Dur:0.00}")));
    }

    void AddStop()
    {
        var (s0, s1) = StopDur;
        float total = R(s0, s1);
        bool crouch = rng.NextDouble() < CrouchStopChance;
        if (crouch && Tier >= 3 && rng.NextDouble() < 0.6)
        {
            // Crouch spam while shooting: the head bobs ~0.5 m every 0.14–0.24 s.
            bool c = true;
            for (float t = 0; t < total;) { float s = R(0.14f, 0.24f); Add(0, s, crouch: c, shoot: true); c = !c; t += s; }
        }
        else Add(0, total, crouch: crouch, shoot: true);
    }

    void NextSeg(float now, float pos, float laneLo, float laneHi)
    {
        if (plan.Count == 0) PlanPattern();
        seg = plan.Dequeue();
        if (seg.Dir != 0)
        {
            // Near the edge of its lane: this and the rest of the pattern go the other way (stays readable, no clamping).
            if ((seg.Dir > 0 && pos > laneHi - 1f) || (seg.Dir < 0 && pos < laneLo + 1f))
            {
                seg = seg with { Dir = -seg.Dir };
                var rest = plan.ToArray(); plan.Clear();
                foreach (var s in rest) plan.Enqueue(s with { Dir = -s.Dir });
            }
            lastDir = seg.Dir;
        }
        segEnd = now + seg.Dur;
    }
}
