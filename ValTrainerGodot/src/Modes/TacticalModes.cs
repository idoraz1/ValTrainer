using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// Post-Plant: the spike is down and you are the last attacker. Each round you start at a post-plant position — On site
/// (next to cover), Off-angle (long range from your side) or Crossfire (another angle on the spike); press 1 / 2 / 3 in the
/// first seconds to switch — and 2–4 defenders (Rookie/Regular 2, Veteran 2–3, Elite 3, Pro 3–4) retake from the map's retake entries.
/// <para>The retake: defenders wait out of sight, run to the last hidden spot on their route ("stage"), then swing onto the
/// site (Elite+ swing together; lower tiers trickle in), clear angles from points that see the spike while checking the
/// usual post-plant spots, and one of them goes for the defuse when it looks safe (no contact for a while, teammates in
/// position) or when the clock forces it. Veteran+ fake ("stick") the defuse to bait your peek; Elite+ play the half-defuse
/// checkpoint. A defuser who sees you either fights or gambles on finishing, by tier.</para>
/// <para>You win when the spike detonates or every defender is down; you lose when the defuse completes (if you die, the
/// round is decided by whether a defender can still reach and finish the defuse before detonation). Graded on rounds won,
/// how you won, how fast you kill a defuser after the defuse sound, deaths, and whether you held your spot instead of
/// peeking before the defuse.</para>
/// </summary>
public sealed class PostPlantMode : TacticalMode
{
    public override string Key => "postplant";
    public override string Name => "Post-Plant";
    public override string Description => "The spike is down: hold your post-plant angle while defenders retake. Play the defuse, not the peek.";
    public override string Category => "Map";
    public override string? Subtitle => $"{MapSpot.Map} — Post-Plant";
    protected override string Side => "ATK";
    protected override string OtherSide => "DEF";

    // plan states of a retaking defender
    const int Wait = 0, ToStage = 1, Staged = 2, ToClear = 3, Clearing = 4, ToSpike = 5, Kneel = 6, AfterStick = 7, Hunt = 8;

    readonly PostSpot?[] offers = new PostSpot?[3];
    PostSpot spot = null!;
    int spotKind, kindOffset = -1;
    float setupUntil, resolveAt = -1f, firstStagedAt = -1f, stickUntil;
    bool leftEarly, defuseHeard, halfPlayed;
    readonly bool[] keyWas = new bool[3];

    // stats
    int wonDetonation, wonKills, wonAfterDeath, lostDefuse, heldRounds, peekedEarly, earlyPeekDeaths, defusersKilled, botDefuses, botSticks;
    readonly List<float> defuserKillMs = new();
    readonly int[] roundsPerKind = new int[3], wonPerKind = new int[3];

    int DefenderCount() => Tier switch { 0 or 1 => 2, 2 => Rng.NextDouble() < 0.5 ? 3 : 2, 3 => 3, _ => Rng.NextDouble() < 0.5 ? 4 : 3 };

    // ---------------------------------------------------------------- round

    protected override void BeginRound()
    {
        for (int k = 0; k < 3; k++)
        {
            var opts = Site.Spots[k];
            offers[k] = opts.Length == 0 ? null : opts[Rng.Next(Math.Min(2, opts.Length))];
        }
        var avail = Enumerable.Range(0, 3).Where(k => offers[k] != null).ToArray();
        if (kindOffset < 0) kindOffset = Rng.Next(3);
        spotKind = avail[(Round - 1 + kindOffset) % avail.Length];
        PlaceAt(spotKind);
        setupUntil = Now + 3f;
        resolveAt = -1f; firstStagedAt = -1f;
        leftEarly = false; defuseHeard = false; halfPlayed = false;
        for (int k = 0; k < 3; k++) keyWas[k] = false;

        // Defenders spawn at the retake entries, hidden from you.
        int n = DefenderCount();
        var taken = new List<Vector3>();
        int start = Rng.Next(Site.Entries.Length);
        var playerEye = SpawnFeet + new Vector3(0, PlayerView.EyeHeight, 0);
        for (int i = 0; i < n; i++)
        {
            int e = (start + i) % Site.Entries.Length;
            var feet = FreeNodeNear(Site.Entries[e], taken, p => Hidden(p, playerEye));
            taken.Add(feet);
            var bot = new TacBot(this, i, $"Defender {i + 1}") { Entry = e, Stage = Site.Stages[e] };
            bot.Spawn(feet, Spike + new Vector3(0, 1.2f, 0), false);
            bot.Plan = Wait;
            // Rookies trickle in one by one; higher tiers move as a group.
            float delay = Difficulty.T(Tier, 6f, 4f, 2.5f, 1.2f, 0.8f);
            bot.GoAt = Now + 3f + R(0.5f, 2f) + i * delay * R(0.6f, 1.2f);
            bot.ClearPt = Site.ClearPoints[(i + Rng.Next(Site.ClearPoints.Length)) % Site.ClearPoints.Length];
            Bots.Add(bot);
        }
        AssignDefuser();
        roundsPerKind[spotKind]++;
        if (DevLog) GD.Print($"[tac] round {Round}: spot {spot.Name} {Fmt(spot.Feet)}, {n} defenders from entries {string.Join(",", Bots.Select(b => b.Entry))}, spike {Fmt(Spike)}");
    }

    void PlaceAt(int kind)
    {
        spotKind = kind;
        spot = offers[kind]!;
        SpawnFeet = spot.Feet;
        // Face the spike from range; next to it, face where the retake shows up first.
        var eye = EyeAt(spot.Feet);
        Vector3 look = Spike + new Vector3(0, TacticalSite.DefuserHeadY, 0);
        if (H(spot.Feet, Spike) < 10f)
        {
            var first = Enumerable.Range(0, Site.Routes.Length).Select(r => FirstVisibleOnRoute(eye, r)).Where(v => v != null).Select(v => v!.Value)
                .OrderBy(v => v.DistanceTo(eye)).FirstOrDefault(Vector3.Zero);
            if (first != Vector3.Zero) look = first;
        }
        var d = look - eye;
        SpawnYaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
        G.Respawn(SpawnFeet, SpawnYaw);
        G.View.Pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Normalized().Y, -1, 1)));
    }

    bool Hidden(Vector3 feet, Vector3 eye) =>
        !G.LineOfSight(feet + new Vector3(0, PlayerView.EyeHeight, 0), eye) && !G.LineOfSight(feet + new Vector3(0, 1.1f, 0), eye);

    Vector3 FreeNodeNear(Vector3 p, List<Vector3> taken, Func<Vector3, bool> ok)
    {
        Vector3? best = null; float bd = float.MaxValue;
        for (int i = 0; i < Nav.Count; i++)
        {
            var q = Nav.Pos[i];
            float d = q.DistanceTo(p);
            if (d > 5f || d >= bd || Mathf.Abs(q.Y - p.Y) > 1f) continue;
            if (taken.Any(t => t.DistanceTo(q) < 1.2f) || Nav.Degree(i) < 8 || !ok(q)) continue;
            bd = d; best = q;
        }
        return best ?? p;
    }

    void AssignDefuser()
    {
        var alive = Bots.Where(b => b.Alive).ToList();
        if (alive.Count == 0 || alive.Any(b => b.Defuser)) return;
        var pick = alive.OrderBy(b => H(b.Feet, Spike) + (b.Fighting ? 20f : 0f)).First();
        pick.Defuser = true;
    }

    // ---------------------------------------------------------------- squad

    float Slack => SpikeLeft - (DefuseTime - (DefuseCheckpoint ? HalfDefuse : 0f));

    /// <summary>Points a retaker checks while clearing: the usual post-plant spots (and where you were last seen / heard).</summary>
    Vector3 CheckPoint(TacBot b)
    {
        if (Tier >= 1 && Belief(b, 6f) is { } k) return EyeAt(k);
        var spots = offers.Where(o => o != null).Select(o => o!.Feet).ToList();
        if (spots.Count == 0) return Spike + new Vector3(0, 1.2f, 0);
        int i = (int)((Now + b.Index * 0.9f) / Difficulty.T(Tier, 3f, 2.4f, 1.8f, 1.5f, 1.3f)) % spots.Count;
        return spots[i] + new Vector3(0, PlayerView.EyeHeight - 0.2f, 0);
    }

    protected override void UpdateSquad(float dt)
    {
        // Setup: 1 / 2 / 3 switch the post-plant spot.
        if (Now < setupUntil && !G.Player.Dead)
        {
            Godot.Key[] keys = { Godot.Key.Key1, Godot.Key.Key2, Godot.Key.Key3 };
            for (int k = 0; k < 3; k++)
            {
                bool down = Input.IsKeyPressed(keys[k]);
                if (down && !keyWas[k] && offers[k] != null && k != spotKind)
                {
                    roundsPerKind[spotKind]--;
                    PlaceAt(k);
                    roundsPerKind[k]++;
                }
                keyWas[k] = down;
            }
        }
        else if (!leftEarly && !defuseHeard && !G.Player.Dead && H(PlayerFeet, spot.Feet) > 3f)
        {
            leftEarly = true;
            Event("left_spot", RoundT, SpikeLeft);
        }

        if (G.Player.Dead)
        {
            if (resolveAt > 0 && Now >= resolveAt) ResolveAfterDeath();
            return;
        }

        AssignDefuser();
        int aliveCount = AliveBots;
        bool allStaged = Bots.Where(b => b.Alive).All(b => b.Plan >= Staged);
        foreach (var b in Bots)
        {
            if (!b.Alive) continue;
            if (b.Plan == Kneel) { UpdateDefuser(b); continue; }
            if (b.Fighting) continue;

            // Just lost sight of you: hold the angle a moment (where you were), low tiers then chase.
            float holdLost = Difficulty.T(Tier, 0.5f, 0.8f, 1.2f, 1.5f, 1.8f);
            if (Now - b.LostSightAt < holdLost && Belief(b, 4f) is { } lk)
            {
                b.Stop();
                b.LookAt = EyeAt(lk);
                continue;
            }
            if (b.LostSightAt > 0 && Now - b.LostSightAt < holdLost + 0.1f && !b.Defuser && Tier <= 1 && Belief(b, 4f) is { } chase && b.Plan != Hunt)
            {
                b.Plan = Hunt; b.GoTo(chase); b.LookAt = chase + new Vector3(0, PlayerView.EyeHeight, 0);
                continue;
            }
            // Shot at without seeing you: turn there and hold.
            if (Now - b.HitAt < 1.2f) { b.Stop(); b.LookAt = b.HeardPos + new Vector3(0, PlayerView.EyeHeight, 0); continue; }

            float travel = H(b.Feet, Spike) * 1.3f / Mover.RunSpeed;
            bool pressure = Slack < travel + Difficulty.T(Tier, 0.5f, 1.2f, 2f, 2.5f, 3f);
            switch (b.Plan)
            {
                case Wait:
                    if (Now >= b.GoAt || pressure) { b.Plan = ToStage; b.GoTo(b.Stage); b.LookAt = null; }
                    break;
                case ToStage:
                    if (!b.Moving || b.Near(b.Stage, 1.2f))
                    {
                        b.Stop();
                        b.Plan = Staged; b.Staged = true; b.PlanAt = Now;
                        if (firstStagedAt < 0) firstStagedAt = Now;
                        b.LookAt = Spike + new Vector3(0, 1.4f, 0);
                    }
                    break;
                case Staged:
                {
                    // Elite+ wait for the whole retake and swing together; lower tiers go as they arrive.
                    bool go = Tier < 3 || allStaged || Now - firstStagedAt > 5f || pressure;
                    if (!go) break;
                    if (b.Defuser && (Tier <= 1 || aliveCount == 1)) GoDefuse(b);
                    else { b.Plan = ToClear; b.GoTo(b.ClearPt); b.PlanAt = Now; }
                    break;
                }
                case ToClear:
                    b.LookAt = Tier >= 2 || H(b.Feet, Spike) < 14f ? CheckPoint(b) : null;
                    if (!b.Moving || b.Near(b.ClearPt, 0.8f)) { b.Stop(); b.Plan = Clearing; b.PlanAt = Now; }
                    break;
                case Clearing:
                case Hunt:
                    if (b.Plan == Hunt && b.Moving) { b.LookAt = CheckPoint(b); break; }
                    b.LookAt = CheckPoint(b);
                    if (b.Defuser && (SafeToDefuse(b) || pressure)) GoDefuse(b);
                    else if (!b.Defuser && b.Plan == Hunt) { b.Plan = Clearing; b.PlanAt = Now; }
                    break;
                case ToSpike:
                    b.LookAt = Tier >= 2 ? CheckPoint(b) : null;
                    if (b.Near(Spike, 0.9f) || (!b.Moving && H(b.Feet, Spike) < DefuseRange))
                    {
                        b.Stop();
                        BotDefuse(b);
                    }
                    else if (!b.Moving) b.GoTo(Spike);
                    break;
                case AfterStick:
                    b.LookAt = Belief(b, 4f) is { } pk ? EyeAt(pk) : CheckPoint(b);
                    if (Now >= b.WaitUntil || pressure) { b.Plan = ToSpike; b.GoTo(Spike); }
                    break;
            }
        }
    }

    bool SafeToDefuse(TacBot b)
    {
        if (Now - PlayerSeenAt < Difficulty.T(Tier, 0.5f, 1f, 2f, 2.5f, 3f)) return false;
        if (Tier < 2) return true;
        var mates = Bots.Where(o => o != b && o.Alive).ToList();
        return mates.All(o => o.Plan is Clearing or Hunt) || Now - b.PlanAt > 4f;
    }

    void GoDefuse(TacBot b)
    {
        b.Plan = ToSpike;
        b.GoTo(Spike);
        b.Walk = false;
    }

    void BotDefuse(TacBot b)
    {
        if (Defusing) { b.Plan = Clearing; return; }
        b.BeginKneel();
        b.SnapLook(Spike + new Vector3(0, 0.3f, 0));
        b.Plan = Kneel;
        b.DefuseReactAt = -1f;
        // Veteran+ often tap the defuse first to bait a peek ("stick"), once per round each.
        float stickChance = Difficulty.T(Tier, 0f, 0f, 0.3f, 0.55f, 0.6f);
        stickUntil = b.Sticks == 0 && Slack > 10f && Rng.NextDouble() < stickChance ? Now + R(0.35f, 0.65f) : -1f;
        StartDefuse(b);
    }

    void UpdateDefuser(TacBot b)
    {
        if (Defuser != b) { b.EndKneel(); b.Plan = Clearing; return; }
        // fake / stick: stop and hold the likely peek
        if (stickUntil > 0 && Now >= stickUntil)
        {
            stickUntil = -1f;
            b.Sticks++; botSticks++;
            EndBotDefuse(b, AfterStick, R(1.4f, 2.4f));
            return;
        }
        // half-defuse play (Elite+): stop at the checkpoint and look around once
        if (!halfPlayed && Tier >= 3 && DefuseProgress >= HalfDefuse + 0.1f && Slack > 9f && Rng.NextDouble() < (Tier >= 4 ? 0.6 : 0.4))
        {
            halfPlayed = true;
            EndBotDefuse(b, AfterStick, R(1.0f, 1.8f));
            return;
        }
        halfPlayed |= DefuseProgress >= HalfDefuse + 0.1f;
        // Shot while defusing, or sees you: fight or gamble on finishing.
        float remain = DefuseTime - DefuseProgress;
        float gamble = Difficulty.T(Tier, 3.5f, 2.6f, 1.6f, 1.1f, 0.9f);
        if (Now - b.HitAt < 0.1f && remain > 1.0f) { Know(PlayerFeet); EndBotDefuse(b, Clearing, 0); return; }
        if (b.CanSeePlayerRaw())
        {
            if (b.DefuseReactAt < 0) b.DefuseReactAt = Now + Mathf.Max(0.15f, b.Brain.Skill.ReactMs / 1000f * R(0.8f, 1.2f));
            else if (Now >= b.DefuseReactAt && remain > gamble) { Know(PlayerFeet); EndBotDefuse(b, Clearing, 0); }
        }
        else b.DefuseReactAt = -1f;
    }

    void EndBotDefuse(TacBot b, int plan, float wait)
    {
        StopDefuse();
        b.EndKneel();
        b.Plan = plan;
        b.WaitUntil = Now + wait;
        b.PlanAt = Now;
        b.ClearPt = b.Feet;
    }

    protected override void OnDefuseStarted(bool player)
    {
        botDefuses++;
        defuseHeard = true;
    }

    protected override void OnRoundEnd(bool won)
    {
        if (leftEarly) peekedEarly++; else heldRounds++;
    }

    // ---------------------------------------------------------------- outcomes

    protected override void OnBotKilled(TacBot bot, bool headshot)
    {
        if (bot.Plan == Kneel || (Now - DefuseStartedAt < 1.5f && bot.Defuser))
        {
            defusersKilled++;
            defuserKillMs.Add((Now - DefuseStartedAt) * 1000f);
        }
        if (bot.Defuser) { bot.Defuser = false; AssignDefuser(); }
        if (AliveBots == 0)
        {
            wonKills++;
            wonPerKind[spotKind]++;
            Score += 300;
            EndRound(true, "ALL DEFENDERS DOWN", UiTheme.Good, "eliminations");
        }
    }

    protected override void OnDetonated()
    {
        if (G.Player.Dead) wonAfterDeath++; else wonDetonation++;
        wonPerKind[spotKind]++;
        Score += 300;
        EndRound(true, "SPIKE DETONATED", UiTheme.Good, "detonation");
    }

    protected override void OnDefused(bool byPlayer)
    {
        lostDefuse++;
        Score -= 50;
        EndRound(false, "SPIKE DEFUSED", UiTheme.Accent, "defused");
    }

    public override void OnPlayerDied()
    {
        Deaths++;
        Score -= 100;
        if (leftEarly && !defuseHeard) earlyPeekDeaths++;
        DevDeathLog(spot.Name);
        G.Banner("YOU DIED", UiTheme.Accent);
        resolveAt = Now + 1.6f;
    }

    /// <summary>You're dead: can a defender still get to the spike and finish the defuse before it blows?</summary>
    void ResolveAfterDeath()
    {
        resolveAt = -1f;
        float best = float.PositiveInfinity;
        foreach (var b in Bots)
        {
            if (!b.Alive) continue;
            float need = Defuser == b ? DefuseTime - DefuseProgress
                : H(b.Feet, Spike) * 1.3f / Mover.RunSpeed + 0.5f + DefuseTime - (DefuseCheckpoint ? HalfDefuse : 0f);
            best = Mathf.Min(best, need);
        }
        if (best < SpikeLeft - 0.3f)
        {
            lostDefuse++;
            SpikeNode?.Disarm();
            SpikeSfx.PlayAt(G.World, "defused", Spike, G.View.Eye, 1f, 90f, 0.6f);
            EndRound(false, "SPIKE DEFUSED", UiTheme.Accent, "died, defused");
        }
        else
        {
            SpikeFx.Detonate(G.World, Spike);
            SpikeSfx.PlayAt(G.World, "detonate", Spike, G.View.Eye, 1.1f, 120f, 0.7f);
            if (SpikeNode != null && GodotObject.IsInstanceValid(SpikeNode)) SpikeNode.QueueFree();
            SpikeNode = null;
            wonAfterDeath++;
            wonPerKind[spotKind]++;
            Score += 150;
            EndRound(true, "SPIKE DETONATED — THEY RAN OUT OF TIME", UiTheme.Good, "died, detonated");
        }
    }

    // ---------------------------------------------------------------- HUD

    public override string? Prompt
    {
        get
        {
            if (G == null || !Live || G.Player.Dead) return null;
            if (Now < setupUntil)
                return $"{spot.Name}  ·  switch: " + string.Join("  ", Enumerable.Range(0, 3).Where(k => offers[k] != null).Select(k => $"{k + 1} {TacticalSite.Kinds[k]}"));
            if (!defuseHeard && Round <= 2 && RoundT < 9f) return "Hold the spike — don't peek until you hear the defuse";
            return null;
        }
    }

    public override IEnumerable<string> HudLines()
    {
        if (Site == null) yield break;
        yield return $"Round {Round}   Won {Won}  Lost {Lost}";
        if (Live) yield return $"Your spot: {spot.Name}";
    }

    protected override void DrawMarkers(CanvasItem c, Camera3D cam, float k)
    {
        if (Now >= setupUntil) return;
        for (int i = 0; i < 3; i++)
            if (offers[i] is { } o && i != spotKind)
                DrawMarker(c, cam, o.Feet + new Vector3(0, 1.2f, 0), k, $"{i + 1} {TacticalSite.Kinds[i].ToUpperInvariant()}", UiTheme.Teal);
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Map", MapSpot.Map);
        yield return ("Rounds won / lost", $"{Won} / {Lost}");
        yield return ("Wins: detonation / kills", $"{wonDetonation} / {wonKills}");
        if (wonAfterDeath > 0) yield return ("Won after dying", wonAfterDeath.ToString());
        if (lostDefuse > 0) yield return ("Lost to the defuse", lostDefuse.ToString());
        yield return ("Defusers killed", defuserKillMs.Count > 0 ? $"{defusersKilled} · {defuserKillMs.Average() / 1000f:0.0}s after sound" : defusersKilled.ToString());
        int rounds = Won + Lost;
        if (rounds > 0) yield return ("Held spot until defuse", $"{heldRounds} of {rounds}");
        if (earlyPeekDeaths > 0) yield return ("Died peeking early", earlyPeekDeaths.ToString());
        var best = Enumerable.Range(0, 3).Where(k => roundsPerKind[k] > 0).OrderByDescending(k => (float)wonPerKind[k] / roundsPerKind[k]).ThenByDescending(k => roundsPerKind[k]).ToList();
        if (best.Count > 1) yield return ("Best spot", $"{TacticalSite.Kinds[best[0]]} ({wonPerKind[best[0]]}/{roundsPerKind[best[0]]})");
        if (botSticks > 0) yield return ("Fake defuses seen", botSticks.ToString());
        yield return ("Kills / deaths", $"{Kills} / {Deaths}");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (KillTimes.Count > 0) yield return ("Avg time to kill", $"{KillTimes.Average():0} ms");
        foreach (var l in MovementLines()) yield return l;
    }

    // ---------------------------------------------------------------- dev auto-play

    protected override void DevPlay(float dt)
    {
        DevCrouch = spot.Crouch && H(PlayerFeet, spot.Feet) < 1f;
        if (DevEnemyVisible) { DevStop(); return; }
        // A defuse you can't see: after a moment, walk toward the spike to contest it (that's when you peek).
        if (Defusing && Defuser != this && Now - DefuseStartedAt > 0.8f)
        {
            if (!DevHasPath) DevPathTo(Spike);
            DevWalk(dt, Mover.WalkSpeed + 1f);
            DevLook(Spike + new Vector3(0, TacticalSite.DefuserHeadY, 0));
            return;
        }
        DevStop();
        // Hold where the retake shows up (alternating between the routes), the spike once they're on it.
        var eye = G.View.Eye;
        if (devWatch == null || devWatchFrom.DistanceTo(eye) > 0.5f)
        {
            devWatchFrom = eye;
            devWatch = Enumerable.Range(0, Site.Routes.Length).Select(r => FirstVisibleOnRoute(eye, r)).Where(w => w != null).Select(w => w!.Value).ToList();
        }
        var bodyOnSite = Bots.Any(b => b.Alive && H(b.Feet, Spike) < 10f);
        if (devWatch.Count == 0 || bodyOnSite) DevLook(Spike + new Vector3(0, TacticalSite.DefuserHeadY, 0));
        else DevLook(devWatch[(int)(Now / 2.5f) % devWatch.Count]);
    }

    List<Vector3>? devWatch;
    Vector3 devWatchFrom;
}

/// <summary>
/// Retake: you are the defender who rotated in. The spike was planted (35 s left when you arrive) and 2–3 attackers (by
/// tier) hold post-plant spots around it — on site, off-angles, crossfires, and at Elite+ a lurk that plays off the defuse
/// sound. Clear the site and defuse (hold the Use Spike bind, VALORANT's default 4; 7 s, checkpoint at 3.5 s) before it blows.
/// <para>Attackers pre-aim where you will first appear on your route, hold their angle after a duel (low tiers re-peek or
/// chase your gunfire), and react to the defuse sound: they turn or swing onto the spike after a tier-based delay — Elite+
/// wait a beat to tell a fake ("stick") from a real defuse; lower tiers swing at the first sound, so a tap-defuse can bait
/// them into your crosshair. Killing them all doesn't win the round: only the defuse does.</para>
/// </summary>
public sealed class RetakeMode : TacticalMode
{
    public override string Key => "retake";
    public override string Name => "Retake";
    public override string Description => $"Retake the site and defuse while attackers hold their post-plant spots. Hold {DefuseKeyText} to defuse (7 s, half at 3.5 s).";
    public override string Category => "Map";
    public override string? Subtitle => $"{MapSpot.Map} — Retake";
    protected override string Side => "DEF";
    protected override string OtherSide => "ATK";
    protected override float SpikeTimerAtStart => 35f;
    protected override float BadgeThreshold => 0.40f;

    const int Hold = 0, SwingWait = 1, Swing = 2, Hunt = 3, Back = 4;

    int entryIdx;
    float defuseHeardAt = -1f;
    bool stoppedAfterHalf;
    // stats
    int cleanDefuses, ninjaDefuses, lostTime, lostDied, sticks, halfRounds;
    readonly List<float> defuseLeft = new(), retakeTimes = new();

    int AttackerCount() => Tier switch { <= 2 => 2, 3 => Rng.NextDouble() < 0.5 ? 3 : 2, _ => 3 };

    protected override void BeginRound()
    {
        stoppedAfterHalf = false;
        defuseHeardAt = -1f;
        int n = AttackerCount();
        // Holders: one of each post-plant kind; Elite+ sometimes swap the crossfire for a lurk that plays the sound.
        var picks = new List<PostSpot>();
        foreach (int k in new[] { 0, 1, 2 }.OrderBy(_ => Rng.Next()))
            if (Site.Spots[k].Length > 0) picks.Add(Site.Spots[k][Rng.Next(Math.Min(2, Site.Spots[k].Length))]);
        if (Tier >= 3 && Site.Lurks.Length > 0 && Rng.NextDouble() < 0.5)
        {
            int ci = picks.FindIndex(p => p.Kind == TacticalSite.Kinds[2]);
            var lurk = Site.Lurks[Rng.Next(Site.Lurks.Length)];
            if (ci >= 0) picks[ci] = lurk; else picks.Add(lurk);
        }
        while (picks.Count < n && Site.Spots.Any(s => s.Length > 1))
        {
            var extra = Site.Spots.Where(s => s.Length > 1).SelectMany(s => s.Skip(1)).FirstOrDefault(s => picks.All(p => p.Feet.DistanceTo(s.Feet) > 4f));
            if (extra == null) break;
            picks.Add(extra);
        }
        picks = picks.Take(n).ToList();

        // Your entry: one that no holder can see.
        var order = Enumerable.Range(0, Site.Entries.Length).OrderBy(_ => Rng.Next()).ToList();
        entryIdx = order.FirstOrDefault(i => picks.All(p => Hidden(Site.Entries[i], p.Feet)), order[0]);
        SpawnFeet = Site.Entries[entryIdx];
        var route = Site.Routes[entryIdx];
        var next = route.Count > 1 ? route[1] : Spike;
        var dir = next - SpawnFeet;
        SpawnYaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
        G.Respawn(SpawnFeet, SpawnYaw);

        for (int i = 0; i < picks.Count; i++)
        {
            var s = picks[i];
            var eye = s.Feet + new Vector3(0, PlayerView.EyeHeight, 0);
            // Pre-aim where a retaker first shows up: on your route (more often at higher tiers — they read the retake),
            // else on another entry's route, else the spike.
            int r = Rng.NextDouble() < Difficulty.T(Tier, 0.45f, 0.55f, 0.65f, 0.75f, 0.85f) ? entryIdx : Rng.Next(Site.Entries.Length);
            Vector3 watch = FirstVisibleOnRoute(eye, r) ?? FirstVisibleOnRoute(eye, entryIdx) ?? Spike + new Vector3(0, TacticalSite.DefuserHeadY, 0);
            var bot = new TacBot(this, i, $"Attacker {i + 1}") { Spot = s, Anchor = s.Feet, Plan = Hold };
            bot.Spawn(s.Feet, watch, s.Crouch && Tier >= 1 && Rng.NextDouble() < 0.6);
            bot.LookAt = watch;
            bot.ClearPt = watch; // remembered watch point
            Bots.Add(bot);
        }
        if (DevLog) GD.Print($"[tac] round {Round}: entry {entryIdx} {Fmt(SpawnFeet)}, holders {string.Join(" | ", picks.Select(p => $"{p.Name} {Fmt(p.Feet)}"))}, spike {Fmt(Spike)}");
    }

    bool Hidden(Vector3 playerFeet, Vector3 botFeet)
    {
        var be = botFeet + new Vector3(0, PlayerView.EyeHeight, 0);
        return !G.LineOfSight(be, playerFeet + new Vector3(0, PlayerView.EyeHeight, 0)) && !G.LineOfSight(be, playerFeet + new Vector3(0, 1.1f, 0));
    }

    bool DefuseHeld => (DevAuto && DevDefuseHeld) || (!G.Player.Dead && Main.I.Valorant.Binds.IsDown(GameAction.UseSpike));
    bool NearSpike => H(PlayerFeet, Spike) <= DefuseRange && Mathf.Abs(PlayerFeet.Y - Spike.Y) < 1.2f;

    protected override void UpdateSquad(float dt)
    {
        // ---- your defuse ----
        bool held = DefuseHeld;
        if (!PlayerDefusing && held && NearSpike && G.PlayerGrounded && !Defusing && !G.Player.Dead)
        {
            StartDefuse(this);
            G.SetAbilityInHand(true);
            G.MoveSpeedScale = 0f;
        }
        else if (PlayerDefusing && (!held || !NearSpike || G.Player.Dead)) StopDefuse();
        if (!Live) return;

        // ---- the attackers ----
        foreach (var b in Bots)
        {
            if (!b.Alive || b.Fighting) continue;
            float holdLost = Difficulty.T(Tier, 0.6f, 0.9f, 1.3f, 1.6f, 2f);
            if (Now - b.LostSightAt < holdLost && Belief(b, 4f) is { } lk)
            {
                b.Stop();
                b.LookAt = EyeAt(lk);
                continue;
            }
            // Rookie / Regular re-peek where they lost you, or chase your gunfire.
            // (decided once per lost duel / heard burst: PlanAt stamps the last decision)
            if (Tier <= 1 && b.Plan == Hold && Belief(b, 3f) is { } pk && Mathf.Max(b.LostSightAt, b.HeardAt) > b.PlanAt)
            {
                b.PlanAt = Now;
                if (Rng.NextDouble() < (b.LostSightAt > 0 && Now - b.LostSightAt < 3f ? 0.5 : 0.2))
                {
                    b.Plan = Hunt; b.GoTo(pk); b.LookAt = pk + new Vector3(0, PlayerView.EyeHeight, 0);
                    continue;
                }
            }
            if (Now - b.HitAt < 1.2f) { b.Stop(); b.LookAt = b.HeardPos + new Vector3(0, PlayerView.EyeHeight, 0); continue; }

            var defuserHead = Spike + new Vector3(0, TacticalSite.DefuserHeadY, 0);
            switch (b.Plan)
            {
                case Hold:
                    // Veteran+ alternate between their watch point and the spike; lower tiers stare at one.
                    b.LookAt = Tier >= 2 && ((int)((Now + b.Index) / 2.5f) % 3 == 2) ? defuserHead : b.ClearPt;
                    if (Tier >= 2 && Belief(b, 3f) is { } kp) b.LookAt = EyeAt(kp);
                    break;
                case SwingWait:
                    if (Now >= b.WaitUntil)
                    {
                        if (!PlayerDefusing && Tier >= 3) { b.Plan = Hold; break; } // it was a stick: hold
                        b.Plan = Swing;
                        if (b.Spot != null && b.Spot.Peek.DistanceTo(b.Feet) > 0.5f) b.GoTo(b.Spot.Peek);
                        b.SnapLook(defuserHead);
                        b.LookAt = defuserHead;
                    }
                    break;
                case Swing:
                    b.LookAt = defuserHead;
                    if (!PlayerDefusing && !b.Moving && Now - defuseHeardAt > 3f && Tier >= 2) { b.Plan = Back; b.GoTo(b.Anchor); }
                    break;
                case Hunt:
                    if (!b.Moving) { b.Plan = Back; b.GoTo(b.Anchor); }
                    break;
                case Back:
                    b.LookAt = Tier >= 2 ? b.ClearPt : null;
                    if (!b.Moving) { b.Plan = Hold; b.LookAt = b.ClearPt; }
                    break;
            }
        }
    }

    protected override void OnDefuseStarted(bool player)
    {
        if (!player) return;
        defuseHeardAt = Now;
        // They hear it: swing / turn onto the spike after a tier delay (Elite+ wait a beat to see if it's a fake).
        foreach (var b in Bots)
        {
            if (!b.Alive || b.Fighting || b.Plan is Swing or SwingWait) continue;
            b.Plan = SwingWait;
            b.WaitUntil = Now + Difficulty.T(Tier, 1.5f, 1.1f, 0.7f, 0.85f, 0.7f) * R(0.8f, 1.25f);
        }
    }

    protected override void OnDefuseStopped(bool player, float progress)
    {
        if (!player) return;
        G.SetAbilityInHand(false);
        G.MoveSpeedScale = 1f;
        if (progress < 1.0f) sticks++;
        if (progress >= HalfDefuse && progress < DefuseTime) stoppedAfterHalf = true;
    }

    protected override void OnDefused(bool byPlayer)
    {
        G.SetAbilityInHand(false);
        G.MoveSpeedScale = 1f;
        bool clean = AliveBots == 0;
        if (clean) cleanDefuses++; else ninjaDefuses++;
        if (stoppedAfterHalf) halfRounds++;
        defuseLeft.Add(SpikeLeft);
        retakeTimes.Add(RoundT);
        Score += 300 + (int)(SpikeLeft * 5);
        EndRound(true, clean ? "SPIKE DEFUSED" : "SPIKE DEFUSED — UNDER FIRE", UiTheme.Good, clean ? "clean defuse" : "ninja defuse");
    }

    protected override void OnDetonated()
    {
        lostTime++;
        Score -= 50;
        EndRound(false, "SPIKE DETONATED", UiTheme.Accent, "detonated");
    }

    public override void OnPlayerDied()
    {
        Deaths++;
        lostDied++;
        Score -= 100;
        DevDeathLog($"entry {entryIdx}");
        EndRound(false, "YOU DIED", UiTheme.Accent, "died");
    }

    protected override void OnBotKilled(TacBot bot, bool headshot)
    {
        if (AliveBots == 0 && Live) G.Banner("SITE CLEAR — DEFUSE!", UiTheme.Good);
    }

    public override string? Prompt
    {
        get
        {
            if (G == null || !Live || G.Player.Dead || PlayerDefusing) return null;
            if (NearSpike) return Defusing ? null : DefuseCheckpoint ? $"Hold {DefuseKeyText} to defuse (resumes from half)" : $"Hold {DefuseKeyText} to defuse";
            if (Round <= 1 && RoundT < 5f) return "Retake the site — clear the attackers or sneak the defuse";
            return null;
        }
    }

    public override IEnumerable<string> HudLines()
    {
        if (Site == null) yield break;
        yield return $"Round {Round}   Won {Won}  Lost {Lost}";
        if (DefuseCheckpoint && !PlayerDefusing && Live) yield return "Half defused — it resumes from 3.5 s";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Map", MapSpot.Map);
        yield return ("Retakes won / lost", $"{Won} / {Lost}");
        yield return ("Defuses: cleared / under fire", $"{cleanDefuses} / {ninjaDefuses}");
        if (Lost > 0) yield return ("Lost: died / time", $"{lostDied} / {lostTime}");
        if (defuseLeft.Count > 0) yield return ("Spike left at defuse", $"{defuseLeft.Average():0.0} s avg");
        if (retakeTimes.Count > 0) yield return ("Avg retake time", $"{retakeTimes.Average():0.0} s");
        if (halfRounds > 0 || sticks > 0) yield return ("Half-defuse wins / fakes", $"{halfRounds} / {sticks}");
        yield return ("Kills / deaths", $"{Kills} / {Deaths}");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (KillTimes.Count > 0) yield return ("Avg time to kill", $"{KillTimes.Average():0} ms");
        foreach (var l in MovementLines()) yield return l;
    }

    // ---------------------------------------------------------------- dev auto-play

    protected override void DevPlay(float dt)
    {
        if (DevEnemyVisible)
        {
            // In a fight: stop defusing unless it's nearly done.
            if (!PlayerDefusing || DefuseTime - DefuseProgress > 1.2f) DevDefuseHeld = false;
            DevStop();
            return;
        }
        if (NearSpike && !Defusing) { DevDefuseHeld = true; return; }
        if (PlayerDefusing) { DevDefuseHeld = true; return; }
        DevDefuseHeld = false;
        if (!DevHasPath) DevPathTo(Spike);
        DevWalk(dt, Mover.RunSpeed);
        // Pre-aim the common post-plant angles you can see from here (else look where you go).
        var eye = G.View.Eye;
        Vector3? best = null; float bd = 35f;
        foreach (var sp in Site.Spots.SelectMany(k => k).Concat(Site.Lurks))
        {
            var se = sp.Feet + new Vector3(0, PlayerView.EyeHeight, 0);
            float d = eye.DistanceTo(se);
            if (d < bd && G.LineOfSight(eye, se)) { bd = d; best = se; }
        }
        DevLook(best ?? DevAhead(3f) + new Vector3(0, PlayerView.EyeHeight, 0));
    }
}
