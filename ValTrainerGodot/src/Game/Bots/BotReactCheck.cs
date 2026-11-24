using System.Globalization;
using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Bots;

/// <summary>
/// DEV-ONLY bot reaction check: <c>--dev --headless --botreact-check [--botreact-trials N] [--botreact-seed S]</c> (needs no
/// drill; quits with the number of failed checks). Drives a real <see cref="BotBrain"/> and <see cref="BotCharacter"/> in a
/// small test world at 240 steps/s, per tier, and measures what the player feels:
/// <list type="bullet">
/// <item><b>you swing</b>: a bot holds a corner (pre-aimed or not, 5-30 m) and you swing out past the wall edge: a wide swing
/// (5.4 m/s), a tight swing that stops with ~0-0.3 m showing (shoulder peek, 2.97 m/s), or a re-peek of the same angle 0.5 s
/// after it saw you (the bot is "alert");</item>
/// <item><b>the bot swings</b>: you hold the corner and the bot sprints out and stops (Flash Dodge's entry).</item>
/// </list>
/// The reaction time is from the first step any part of the bot (head, chest or a shoulder) is on your screen (clear line from
/// your eye and inside your 103° view) to its first shot. This is measured here, independently of how the brain decides it
/// sees you. Shots fired before the bot was ever on your screen count as "unseen". A last block reports the time-to-kill of a
/// bot against a player who stands in full view (the tier tuning in Difficulty.cs), for before/after comparisons.
/// Lines start with "[botreact]"; "[botreact] RESULT: PASS|FAIL" at the end.
/// </summary>
public partial class BotReactCheck : Node
{
    public static bool Requested => CmdLine.Dev && CmdLine.Has("--botreact-check");

    const float Dt = 1f / 240f;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Lower bounds (ms) per tier: the median of first sightings, and the fastest shot of all (first and re-peeks).</summary>
    static readonly float[] MinMedianMs = { 380, 280, 220, 190, 170 };
    static readonly float[] MinFastestMs = { 200, 170, 150, 145, 140 };

    int fails, passes;

    /// <summary>The test world: a long wall whose edge is the corner, the player's view and health, the clock.</summary>
    sealed class World : IBotWorld
    {
        public PlayerView View { get; } = new();
        public PlayerState Player { get; } = new();
        public float Now { get; set; }
        public bool PlayerFullyBlind => false;
        public readonly List<Box> Solid = new();
        public bool RealDamage;
        public float FirstShotAt = -1f;
        public int Shots;
        public bool LineOfSight(Vector3 a, Vector3 b) => !Collision.Blocked(a, b, Solid);
        public void DamagePlayer(float dmg, Vector3 from) { if (RealDamage) Player.Damage(dmg, from, Now); }
        public void SoundAt(string name, Vector3 pos, float maxRange = 40f, float floor = 0.45f, float vol = 1f)
        {
            if (FirstShotAt < 0) FirstShotAt = Now;
            Shots++;
        }
    }

    public override void _Ready()
    {
        int trials = int.TryParse(CmdLine.After("--botreact-trials"), out var n) && n > 0 ? n : 300;
        int seed = int.TryParse(CmdLine.After("--botreact-seed"), out var sd) ? sd : 1;
        try { Run(trials, seed); }
        catch (Exception e) { Check("exception", false, e.ToString()); }
        GD.Print($"[botreact] RESULT: {(fails == 0 ? "PASS" : "FAIL")} ({passes} passed, {fails} failed)");
        GetTree().Quit(fails);
    }

    void Check(string name, bool ok, string detail)
    {
        if (ok) passes++; else fails++;
        GD.Print($"[botreact] {(ok ? "PASS" : "FAIL")} {name}: {detail}");
    }

    enum Swing { Wide, Tight, RePeek, BotSwings }

    void Run(int trials, int seed)
    {
        var rng = new Random(seed);
        var body = BotCharacter.Create(Colors.Red, false, 0);
        AddChild(body);
        GD.Print($"[botreact] {trials} trials per tier and swing, seed {seed}, {1f / Dt:0} steps/s; times = ms from the bot on your screen to its first shot");
        for (int t = 0; t < Difficulty.Tiers.Length; t++)
        {
            var tier = Difficulty.Tiers[t];
            var first = new List<float>();   // first sightings (you swing wide / tight, the bot swings)
            var all = new List<float>();
            int unseen = 0;
            foreach (var swing in Enum.GetValues<Swing>())
            {
                var ms = new List<float>();
                int unseenHere = 0, noShot = 0;
                for (int i = 0; i < trials; i++)
                {
                    float? r = Trial(body, t, swing, rng, out bool shotUnseen);
                    if (shotUnseen) { unseenHere++; continue; }
                    if (r is not { } v) { noShot++; continue; }
                    ms.Add(v);
                }
                unseen += unseenHere;
                all.AddRange(ms);
                if (swing != Swing.RePeek) first.AddRange(ms);
                GD.Print($"[botreact] {tier.Name,-7} {swing,-9} n={ms.Count,3} {Dist(ms)}" +
                         (unseenHere > 0 ? $", {unseenHere} shot before on screen" : "") + (noShot > 0 ? $", {noShot} no shot" : ""));
            }
            float med = Pct(first, 0.5f), fastest = all.Count > 0 ? all.Min() : float.NaN;
            Check($"{tier.Name} no shots before the bot is on your screen", unseen == 0, $"{unseen} shot(s) fired while no part of the bot was on your screen");
            Check($"{tier.Name} first-sight median >= {MinMedianMs[t]:0} ms", med >= MinMedianMs[t], $"median {F(med)} ms (n={first.Count})");
            Check($"{tier.Name} fastest shot >= {MinFastestMs[t]:0} ms", fastest >= MinFastestMs[t], $"fastest {F(fastest)} ms of {all.Count}");
        }
        float rookie = 0, pro = 0;
        for (int t = 0; t < Difficulty.Tiers.Length; t++)
        {
            var ttk = new List<float>();
            for (int i = 0; i < trials; i++) if (Ttk(body, t, rng) is { } v) ttk.Add(v);
            float med = Pct(ttk, 0.5f);
            if (t == 0) rookie = med;
            if (t == Difficulty.Tiers.Length - 1) pro = med;
            GD.Print($"[botreact] TTK {Difficulty.Tiers[t].Name,-7} standing in view, 8-25 m: n={ttk.Count} {Dist(ttk)}");
        }
        Check("tiers are ordered (Rookie's time-to-kill > 2x Pro's)", rookie > 2f * pro, $"Rookie {F(rookie)} ms, Pro {F(pro)} ms");
        RemoveChild(body);
        body.QueueFree();
    }

    // ------------------------------------------------------------------ the corner

    // The wall: x <= 0, between z = -0.6 and -0.2, 3 m high. Its edge (x = 0) is the corner. The corner's near side (z > -0.2)
    // is the peeker's lane along +x; the holder stands far down -z, off to +x.
    static World NewWorld()
    {
        var w = new World();
        w.Solid.Add(new Box(new Vector3(-30, 0, -0.6f), new Vector3(0, 3, -0.2f)));
        return w;
    }

    static float Yaw(Vector3 from, Vector3 to) { var d = to - from; return Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)); }

    /// <summary>Points of the bot model the player can spot: head, chest and both shoulders (same layout as the player's).</summary>
    static IEnumerable<Vector3> BotPoints(BotCharacter b)
    {
        float y = Mathf.DegToRad(b.FacingYaw);
        var right = new Vector3(Mathf.Cos(y), 0, Mathf.Sin(y));
        var h = b.Head;
        yield return h;
        yield return h + Vector3.Down * 0.45f;
        yield return h + Vector3.Down * 0.3f + right * 0.3f;
        yield return h + Vector3.Down * 0.3f - right * 0.3f;
    }

    /// <summary>Some part of the bot is on the player's screen: a clear line from the eye and inside the view (103° x 16:9).</summary>
    static bool OnScreen(World w, BotCharacter b)
    {
        var v = w.View;
        var f = v.Forward;
        var right = f.Cross(Vector3.Up).Normalized();
        var up = right.Cross(f);
        float tx = Mathf.Tan(Mathf.DegToRad(PlayerView.HipHFov / 2f)), ty = tx * 9f / 16f;
        foreach (var p in BotPoints(b))
        {
            var d = p - v.Eye;
            float z = d.Dot(f);
            if (z <= 0.05f) continue;
            if (Mathf.Abs(d.Dot(right) / z) > tx || Mathf.Abs(d.Dot(up) / z) > ty) continue;
            if (w.LineOfSight(v.Eye, p)) return true;
        }
        return false;
    }

    static void SetEye(World w, Vector3 eye, Vector3 lookAt)
    {
        w.View.Eye = eye;
        w.View.Yaw = Yaw(eye, lookAt);
        var d = lookAt - eye;
        w.View.Pitch = Mathf.RadToDeg(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
    }

    static float R(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>One duel. Returns ms from the bot on screen to its first shot, null if it never shot; <paramref name="shotUnseen"/>
    /// = it fired before it was ever on screen.</summary>
    float? Trial(BotCharacter body, int tier, Swing swing, Random rng, out bool shotUnseen)
    {
        shotUnseen = false;
        var w = NewWorld();
        float eyeY = PlayerView.EyeHeight;
        float dist = R(rng, 5f, 30f), side = R(rng, 0.5f, 2.5f);
        var held = Vector3.Zero;
        bool botPeeks = swing == Swing.BotSwings;
        Vector3 holder = new(side, 0, -dist), lane0 = new(-1.2f, 0, 0);
        float stopX = swing switch { Swing.Tight => R(rng, 0.0f, 0.3f), _ => R(rng, 0.8f, 1.8f) };
        float speed = swing == Swing.Tight ? 2.97f : 5.4f;

        body.Hp = BotCharacter.MaxHp;
        body.Velocity = Vector3.Zero;
        var skill = Difficulty.Get(tier).Bot;
        BotBrain brain;
        if (!botPeeks)
        {
            // the bot holds the corner from far down -z; you swing along the lane
            body.Feet = holder;
            var corner = new Vector3(0.3f, eyeY, 0);
            held = (corner - body.Head).Normalized();
            body.FacingYaw = Yaw(body.Head, corner);
            brain = new BotBrain(body, skill, rng) { HeldDir = held, PreAimed = rng.NextDouble() < 0.5, ShoulderSight = Difficulty.ShoulderSight(tier) };
        }
        else
        {
            // you hold the corner from far down -z; the bot sprints out along the lane and stops (Flash Dodge's entry)
            body.Feet = lane0;
            var you = holder + Vector3.Up * eyeY;
            held = (you - body.Head).Normalized();
            body.FacingYaw = Yaw(body.Head, you);
            brain = new BotBrain(body, skill, rng)
            {
                HeldDir = held, PreAimed = Difficulty.FlashPreAim(tier), ShoulderSight = Difficulty.ShoulderSight(tier),
            };
        }

        float x = lane0.X, seenAt = -1f, retreatAt = -1f, rePeekAt = -1f;
        int phase = 0; // RePeek: 0 first peek, 1 back behind the wall, 2 the re-peek (measured)
        for (int step = 0; step < 240 * 6; step++)
        {
            w.Now = step * Dt;
            // move the peeker
            float target = phase == 1 ? lane0.X : stopX;
            float dx = Mathf.Clamp(target - x, -speed * Dt, speed * Dt);
            x += dx;
            if (botPeeks)
            {
                body.Velocity = new Vector3(dx / Dt, 0, 0);
                body.Feet = new Vector3(x, 0, 0);
                SetEye(w, holder + Vector3.Up * eyeY, new Vector3(0.3f, eyeY, 0)); // you hold the corner's edge
            }
            else SetEye(w, new Vector3(x, eyeY, 0), body.Head);                  // you swing, looking at the bot's spot

            bool measured = swing != Swing.RePeek || phase == 2;
            if (measured && seenAt < 0 && OnScreen(w, body)) seenAt = w.Now;
            int shotsBefore = w.Shots;
            brain.Update(w, Dt);
            if (w.Shots > shotsBefore)
            {
                if (swing == Swing.RePeek && phase == 0)
                {
                    // it saw you: back off behind the wall, then re-peek the same angle 0.5 s later (the bot is alert)
                    phase = 1; retreatAt = w.Now; w.Shots = 0; w.FirstShotAt = -1f;
                    continue;
                }
                if (measured)
                {
                    if (seenAt < 0) { shotUnseen = true; return null; }
                    return (w.FirstShotAt - seenAt) * 1000f;
                }
                w.Shots = 0; w.FirstShotAt = -1f; // a shot during the retreat: not measured
            }
            if (phase == 1 && x <= lane0.X + 1e-3f && rePeekAt < 0) rePeekAt = w.Now + 0.5f;
            if (phase == 1 && rePeekAt >= 0 && w.Now >= rePeekAt) { phase = 2; w.Shots = 0; w.FirstShotAt = -1f; }
            _ = retreatAt;
        }
        return null;
    }

    /// <summary>Time-to-kill against a player standing in full view: from the first step the player is visible to death.</summary>
    float? Ttk(BotCharacter body, int tier, Random rng)
    {
        var w = NewWorld();
        w.RealDamage = true;
        float dist = R(rng, 8f, 25f);
        body.Hp = BotCharacter.MaxHp;
        body.Velocity = Vector3.Zero;
        body.Feet = new Vector3(R(rng, 0.5f, 2.5f), 0, -dist);
        var you = new Vector3(R(rng, 1.5f, 3f), PlayerView.EyeHeight, 0);
        SetEye(w, you, body.Head);
        // holding roughly where you are: 2-8° off your head
        var to = (you - body.Head).Normalized();
        var held = to.Rotated(Vector3.Up, Mathf.DegToRad(R(rng, 2f, 8f) * (rng.NextDouble() < 0.5 ? -1 : 1)));
        body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(held.X, -held.Z));
        var brain = new BotBrain(body, Difficulty.Get(tier).Bot, rng) { HeldDir = held, ShoulderSight = Difficulty.ShoulderSight(tier) };
        for (int step = 0; step < 240 * 15; step++)
        {
            w.Now = step * Dt;
            brain.Update(w, Dt);
            if (w.Player.Dead) return w.Now * 1000f;
        }
        return null;
    }

    // ------------------------------------------------------------------ stats

    static float Pct(List<float> v, float p)
    {
        if (v.Count == 0) return float.NaN;
        var s = v.OrderBy(x => x).ToList();
        return s[Math.Clamp((int)MathF.Round(p * (s.Count - 1)), 0, s.Count - 1)];
    }

    static string Dist(List<float> v) => v.Count == 0 ? "-" :
        $"min {F(v.Min())}  p10 {F(Pct(v, 0.1f))}  median {F(Pct(v, 0.5f))}  p90 {F(Pct(v, 0.9f))} ms";

    static string F(float v) => float.IsNaN(v) ? "n/a" : v.ToString("0", Inv);
}
