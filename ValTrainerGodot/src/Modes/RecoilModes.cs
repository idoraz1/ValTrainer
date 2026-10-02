using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>Recoil drills share tier distances and a "how much do I need to spray" rule.</summary>
public static class RecoilTiers
{
    public static float Distance(int t) => Difficulty.T(t, 8f, 10f, 15f, 20f, 25f);
    /// <summary>Minimum bullets per spray that count (Vandal; Phantom gets proportionally more).</summary>
    public static int SprayLength(int t) => Difficulty.Ti(t, 5, 8, 12, 18, 25);
    public static float TransferDistance(int t) => Difficulty.T(t, 8f, 10f, 12f, 15f, 18f);
    public static float TransferSpacing(int t) => Difficulty.T(t, 1.5f, 1.8f, 2.2f, 2.6f, 3.0f);
    public static int TransferBots(int t) => Difficulty.Ti(t, 2, 3, 3, 3, 4);
}

/// <summary>
/// Spray control: hold Mouse 1 on a standing agent and keep the whole spray on the body by pulling down against
/// the climb and countering the side drift. After every spray a chart shows where each bullet landed against the
/// target (and the gun's uncompensated pattern in grey) plus a coaching hint ("pull down more", "counter right").
/// </summary>
public sealed class SprayControlMode : TrainingMode
{
    public override AimFocus? Focus => dummy != null && GodotObject.IsInstanceValid(dummy) ? new AimFocus(1, Chest, 0.27f) : null;

    readonly WeaponKind kind;
    public SprayControlMode(WeaponKind kind) => this.kind = kind;

    public override string Key => kind == WeaponKind.Phantom ? "spray_phantom" : "spray_vandal";
    public override string Name => kind == WeaponKind.Phantom ? "Phantom Spray" : "Vandal Spray";
    public override string Description => kind == WeaponKind.Phantom
        ? "Recoil control with the Phantom (11 rps, 30 rounds). Keep the spray on the body; chart + tips after each spray."
        : "Recoil control with the Vandal (9.75 rps, 25 rounds). Pull down, counter the drift; chart + tips after each spray.";
    public override string Category => "Recoil";
    public override WeaponKind Weapon => kind;
    public override bool InfiniteAmmo => false;   // real magazine: a full spray is 25 (Vandal) / 30 (Phantom)
    public override bool InfiniteReserve => true;
    public override bool UsesHeadshots => true;

    float Dist => RecoilTiers.Distance(Tier);
    int Required => kind == WeaponKind.Phantom ? Mathf.Min(30, (int)Mathf.Round(RecoilTiers.SprayLength(Tier) * 1.2f)) : RecoilTiers.SprayLength(Tier);

    /// <summary>A backboard behind the dummy so misses leave visible bullet holes.</summary>
    public override IEnumerable<Box> ExtraSolids => new[] { new Box(new Vector3(-3f, 0f, -Dist - 2.2f), new Vector3(3f, 4f, -Dist - 2f)) };

    BotCharacter dummy = null!;
    readonly List<(float Yaw, float Pitch, HitZone Zone)> current = new(), last = new();
    bool spraying;
    float lastBulletAt = -99f;
    int sprays, countedSprays, shortSprays, countedShots, countedHits;
    float bestPct = -1;
    string tip = "";
    float lastPct = -1;

    protected override void Setup()
    {
        dummy = G.SpawnBot(new Vector3(0, 0, -Dist));
        dummy.FacingYaw = 180f;
    }

    Vector3 Chest => dummy.Feet + new Vector3(0, 1.25f, 0);

    public override void Update(float dt)
    {
        if (!GodotObject.IsInstanceValid(dummy)) return;
        dummy.Velocity = Vector3.Zero;
        dummy.Hp = BotCharacter.MaxHp; // a practice dummy never dies
        var w = G.Weapon!;
        // The spray ends when you release or the magazine runs dry (no bullet for > 1.6 intervals).
        if (spraying && Now - lastBulletAt > w.Interval * 1.6f) EndSpray();
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        if (!spraying) { spraying = true; current.Clear(); }
        lastBulletAt = Now;

        var zone = dummy.Raycast(o, d, out float dist);
        if (zone != HitZone.None && dist < G.BulletWallDist)
        {
            Hits++;
            if (zone == HitZone.Head) HeadHits++;
            LastShotZone = zone;
            dummy.OnHit(zone);
            Effects.BodyHit(o + d * dist, G.Enemy, zone == HitZone.Head);
            G.Sound(zone == HitZone.Head ? "head" : "body", zone == HitZone.Head ? 0.8f : 0.5f);
        }
        else zone = HitZone.None;

        // Angular offset of this bullet from the target's chest, seen from the eye.
        var toChest = (Chest - o).Normalized();
        float yawT = Mathf.RadToDeg(Mathf.Atan2(toChest.X, -toChest.Z)), pitchT = Mathf.RadToDeg(Mathf.Asin(toChest.Y));
        float yawB = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), pitchB = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
        current.Add((Mathf.Wrap(yawB - yawT, -180, 180), pitchB - pitchT, zone));
        return zone != HitZone.None ? dist : float.PositiveInfinity;
    }

    void EndSpray()
    {
        spraying = false;
        sprays++;
        last.Clear();
        last.AddRange(current);
        int n = current.Count, hits = current.Count(c => c.Zone != HitZone.None);
        lastPct = n == 0 ? 0 : (float)hits / n;
        Event("spray_end", n, lastPct);
        var tailRes = current.Skip(3).ToList();
        if (tailRes.Count >= 3) Event("spray_residual", tailRes.Average(b => b.Pitch), tailRes.Average(b => b.Yaw));
        if (n < Required)
        {
            shortSprays++;
            tip = $"Short spray ({n} bullets) — hold for at least {Required} to train the full pattern.";
            return;
        }
        countedSprays++;
        countedShots += n;
        countedHits += hits;
        int heads = current.Count(c => c.Zone == HitZone.Head);
        Score += hits * 10 + heads * 15 + (lastPct >= 0.8f ? 100 : 0);
        bestPct = Mathf.Max(bestPct, lastPct);
        tip = Coach(current);
        G.Banner($"{hits}/{n} on target · {lastPct * 100:0}%", lastPct >= 0.8f ? UiTheme.Good : lastPct >= 0.5f ? UiTheme.Warn : UiTheme.Accent);
    }

    /// <summary>Plain-language feedback from where bullets 4+ ended up relative to the chest.</summary>
    static string Coach(List<(float Yaw, float Pitch, HitZone Zone)> s)
    {
        var tail = s.Skip(3).ToList();
        if (tail.Count < 3) return "Nice — now try longer sprays.";
        float v = tail.Average(b => b.Pitch), h = tail.Average(b => b.Yaw);
        var parts = new List<string>();
        if (v > 0.35f) parts.Add("pull down MORE (the spray climbed over the body)");
        else if (v < -0.45f) parts.Add("pull down LESS (you dragged below the body)");
        if (h > 0.35f) parts.Add("counter the drift to the LEFT");
        else if (h < -0.35f) parts.Add("counter the drift to the RIGHT");
        return parts.Count == 0 ? "Great control — the spray stayed centred." : "Tip: " + string.Join(", ", parts) + ".";
    }

    public override string? Prompt => sprays == 0 && !spraying ? $"Hold Mouse 1 on the agent — spray at least {Required} bullets and keep them on the body" : null;

    public override IEnumerable<string> HudLines()
    {
        yield return $"Distance {Dist:0} m · spray ≥ {Required} bullets";
        if (lastPct >= 0) yield return $"Last spray {lastPct * 100:0}% on target";
        if (tip.Length > 0) yield return tip;
    }

    public override float Accuracy => countedShots == 0 ? (Shots == 0 ? 0 : (float)Hits / Shots) : (float)countedHits / countedShots;

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Counted sprays", $"{countedSprays} (≥ {Required} bullets at {Dist:0} m)");
        yield return ("Bullets on target", countedShots == 0 ? "—" : $"{Accuracy * 100:0.0}%  ({countedHits}/{countedShots})");
        if (bestPct >= 0) yield return ("Best spray", $"{bestPct * 100:0}%");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        if (shortSprays > 0) yield return ("Short sprays (not counted)", shortSprays.ToString());
    }

    public override int Badge()
    {
        if (countedSprays < 3) return -1;
        float a = Accuracy;
        if (a >= 0.85f) return Math.Min(4, Tier + 1);
        if (a >= 0.70f) return Tier;
        if (a >= 0.50f) return Tier - 1;
        return -1;
    }

    // ---------------- spray chart (HUD overlay) ----------------

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        if (last.Count == 0 && current.Count == 0) return;
        var shots = spraying ? current : last;
        float k = size.Y / 1080f;
        float pw = 300 * k, ph = 380 * k;
        var r = new Rect2(size.X - pw - 28 * k, size.Y * 0.2f, pw, ph);
        c.DrawRect(r, new Color(0.06f, 0.1f, 0.14f, 0.82f));
        c.DrawRect(new Rect2(r.Position, new Vector2(4 * k, 26 * k)), UiTheme.Accent);
        c.DrawString(UiTheme.HudWide, r.Position + new Vector2(14 * k, 19 * k), spraying ? "SPRAY (LIVE)" : "LAST SPRAY",
            HorizontalAlignment.Left, -1, UiTheme.Fs(14, k), UiTheme.Text);

        // degrees → pixels: fit ~7° of vertical travel in the panel
        float pxPerDeg = (ph - 60 * k) / 7f;
        var origin = new Vector2(r.Position.X + pw / 2, r.End.Y - 70 * k); // chest point
        Vector2 P(float yaw, float pitch) => origin + new Vector2(yaw * pxPerDeg, -pitch * pxPerDeg);

        // silhouette (angular size at this distance), relative to the chest point
        float D = Dist;
        float Deg(float m) => Mathf.RadToDeg(Mathf.Atan2(m, D));
        var body = new Color(0.55f, 0.6f, 0.68f, 0.35f);
        float headY = Deg(PlayerView.EyeHeight - 1.25f), headR = Deg(BotCharacter.HeadRadius);
        c.DrawCircle(P(0, headY), headR * pxPerDeg, body);
        var torsoTL = P(-Deg(0.27f), Deg(1.54f - 1.25f));
        var torsoBR = P(Deg(0.27f), Deg(0.92f - 1.25f));
        c.DrawRect(new Rect2(torsoTL, torsoBR - torsoTL), body);
        var legsTL = P(-Deg(0.22f), Deg(0.92f - 1.25f));
        var legsBR = P(Deg(0.22f), Deg(0f - 1.25f));
        c.DrawRect(new Rect2(legsTL, legsBR - legsTL), new Color(body, 0.22f));

        // the gun's raw (uncompensated) pattern, starting where your first bullet went, in grey
        var w = G.Weapon!;
        float side = kind == WeaponKind.Phantom ? 1f : -1f;
        var (y0, p0, _) = shots.Count > 0 ? shots[0] : (0f, 0f, HitZone.None);
        Vector2? prev = null;
        for (int i = 0; i < Math.Min(shots.Count + 3, w.Pitch.Length); i++)
        {
            var p = P(y0 + side * w.Yaw[i], p0 + w.Pitch[i]);
            if (!r.HasPoint(p)) break;
            if (prev is { } pv) c.DrawLine(pv, p, new Color(1, 1, 1, 0.18f), 1.5f * k);
            c.DrawCircle(p, 2.5f * k, new Color(1, 1, 1, 0.25f));
            prev = p;
        }

        // your bullets
        for (int i = 0; i < shots.Count; i++)
        {
            var (yaw, pitch, zone) = shots[i];
            var p = P(yaw, pitch);
            p = new Vector2(Mathf.Clamp(p.X, r.Position.X + 4, r.End.X - 4), Mathf.Clamp(p.Y, r.Position.Y + 30 * k, r.End.Y - 4));
            var col = zone switch { HitZone.Head => UiTheme.Good, HitZone.Body => UiTheme.Text, HitZone.Legs => UiTheme.Warn, _ => UiTheme.Accent };
            c.DrawCircle(p, 3.6f * k, col);
            if (i == 0) c.DrawArc(p, 6 * k, 0, Mathf.Tau, 16, col, 1.2f * k);
        }

        int n = shots.Count, hits = shots.Count(s => s.Zone != HitZone.None);
        c.DrawString(UiTheme.Hud, new Vector2(r.Position.X + 14 * k, r.End.Y - 40 * k), $"{hits}/{n} on target",
            HorizontalAlignment.Left, -1, UiTheme.Fs(18, k), UiTheme.Text);
        c.DrawString(UiTheme.Body, new Vector2(r.Position.X + 14 * k, r.End.Y - 16 * k), "● head  ● body  ● miss   grey = raw pattern",
            HorizontalAlignment.Left, -1, UiTheme.Fs(11, k), UiTheme.Dim);
    }
}

/// <summary>
/// Spray transfer: kill every agent in ONE continuous spray. Release the trigger (or run dry) with someone alive
/// and the round is lost. Tier adds distance, spacing and a 4th agent at Pro.
/// </summary>
public sealed class SprayTransferMode : BotMode
{
    public override AimFocus? Focus
    {
        get
        {
            AimFocus? best = null; float bestA = float.MaxValue;
            foreach (var b in bots)
            {
                if (b.Dead || !GodotObject.IsInstanceValid(b)) continue;
                var chest = b.Feet + new Vector3(0, 1.25f, 0);
                float a = AngleFromCrosshair(chest);
                if (a < bestA) { bestA = a; best = new AimFocus(IdOf(b), chest, 0.27f); }
            }
            return best;
        }
    }

    public override string Key => "spray_transfer";
    public override string Name => "Spray Transfer";
    public override string Description => "Kill all the agents in one continuous Vandal spray: control the recoil while dragging between targets.";
    public override string Category => "Recoil";

    readonly List<BotCharacter> bots = new();
    bool spraying;
    float sprayStart, lastBulletAt = -99f, resetIn = 0.5f;
    int rounds, wins;
    readonly List<float> clearTimes = new();
    bool roundLive;

    void NewRound()
    {
        foreach (var b in bots) if (!b.Dead) G.Despawn(b);
        bots.Clear();
        int n = RecoilTiers.TransferBots(Tier);
        float d = RecoilTiers.TransferDistance(Tier), sp = RecoilTiers.TransferSpacing(Tier);
        for (int i = 0; i < n; i++)
        {
            float x = (i - (n - 1) / 2f) * sp;
            var b = G.SpawnBot(new Vector3(x, 0, -d - (i % 2) * 0.8f));
            b.FacingYaw = 180f;
            bots.Add(b);
        }
        spraying = false;
        roundLive = true;
    }

    public override void Update(float dt)
    {
        if (!roundLive)
        {
            resetIn -= dt;
            if (resetIn <= 0) NewRound();
            return;
        }
        foreach (var b in bots) if (GodotObject.IsInstanceValid(b)) b.Velocity = Vector3.Zero;
        // Spray broken: no bullet for > 1.6 intervals while someone is still alive.
        if (spraying && Now - lastBulletAt > Weapons.Vandal.Interval * 1.6f && bots.Any(b => !b.Dead))
        {
            rounds++;
            Event("round_lost");
            G.Banner("Spray broken — keep holding until everyone is down", UiTheme.Accent);
            G.Sound("fail", 0.6f);
            EndRound();
        }
    }

    void EndRound()
    {
        roundLive = false;
        spraying = false;
        resetIn = 1.6f;
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        if (!roundLive) return float.PositiveInfinity;
        if (!spraying) { spraying = true; sprayStart = Now; }
        lastBulletAt = Now;
        var hit = ShootBots(bots, o, d, out var zone, out bool killed, out float dist);
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 30 : 10;
        if (killed) Score += 100;
        if (bots.All(b => b.Dead))
        {
            float t = Now - sprayStart;
            rounds++; wins++;
            Event("round_won", t);
            clearTimes.Add(t);
            Score += 300 + (int)Mathf.Max(0, (3f - t) * 100);
            G.Banner($"ALL DOWN in {t:0.00}s", UiTheme.Good);
            EndRound();
        }
        return dist;
    }

    public override string? Prompt => roundLive && !spraying && rounds == 0 ? "One spray, no release: kill every agent before you let go of Mouse 1" : null;

    public override IEnumerable<string> HudLines()
    {
        if (roundLive) yield return $"Agents left: {bots.Count(b => !b.Dead)}";
        if (rounds > 0) yield return $"Rounds won {wins}/{rounds}";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Rounds won", $"{wins} / {rounds}");
        if (clearTimes.Count > 0) yield return ("Median clear time", $"{Median(clearTimes):0.00} s");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
    }

    public override int Badge() => rounds < 3 ? -1 : Difficulty.BadgeWinRate(Tier, (float)wins / rounds);
}
