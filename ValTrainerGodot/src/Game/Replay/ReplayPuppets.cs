using Godot;
using ValTrainer.Game.Bots;

namespace ValTrainer.Game.Replay;

/// <summary>
/// The Rewind's bots and targets: one pooled node per recorded entity while it exists (SpawnT ≤ t &lt; EndT).
/// Bots are real <see cref="BotCharacter"/> puppets (pooled per variant), targets real <see cref="Target"/> spheres.
/// The pools are filled during the stage's staged load (<see cref="PrewarmStep"/>) with as many nodes as the recording
/// ever has alive at once, so playback never instantiates; steady playback allocates nothing.
/// </summary>
public partial class ReplayPuppets : Node3D
{
    /// <summary>Most nodes of one kind created during loading (more are made on demand).</summary>
    const int MaxPrewarm = 16;

    readonly ReplayRecording rec;
    readonly Color enemy;
    readonly bool outlines, valorantLook;
    readonly Dictionary<ushort, int> index = new();
    readonly Stack<BotCharacter>[] botPool = new Stack<BotCharacter>[BotAssets.Variants.Length];
    readonly Stack<Target> targetPool = new();
    readonly BotCharacter?[] bots;
    readonly Target?[] targets;
    readonly bool[] shown;           // entity's recorded Visible bit at the last Sync
    readonly int[] needBots = new int[BotAssets.Variants.Length];
    int needTargets, madeBots, madeTargets;
    readonly int[] madeVariant = new int[BotAssets.Variants.Length];

    public ReplayPuppets(ReplayRecording rec, Color enemy, bool outlines, bool valorantLook)
    {
        Name = "Puppets";
        this.rec = rec;
        this.enemy = enemy;
        this.outlines = outlines;
        this.valorantLook = valorantLook;
        int n = rec.Entities.Count;
        bots = new BotCharacter?[n];
        targets = new Target?[n];
        shown = new bool[n];
        for (int i = 0; i < botPool.Length; i++) botPool[i] = new Stack<BotCharacter>();
        for (int i = 0; i < n; i++) index[rec.Entities[i].Id] = i;
        CountNeeds();
    }

    public ReplayPuppets() : this(new ReplayRecording(), Colors.Red, true, true) { }

    /// <summary>Nodes to create while loading (for the progress bar).</summary>
    public int PrewarmTotal { get; private set; }
    public int Prewarmed => madeBots + madeTargets;
    public bool HasBots { get; private set; }

    /// <summary>Peak number of simultaneously alive entities per bot variant and for targets.</summary>
    void CountNeeds()
    {
        var edges = new List<(float T, int D, int Slot)>();
        float end = Math.Max(rec.Duration, 0f) + 1f;
        foreach (var e in rec.Entities)
        {
            int slot = e.Kind == EntKind.Bot ? Math.Clamp((int)e.Variant, 0, needBots.Length - 1) : needBots.Length;
            if (e.Kind == EntKind.Bot) HasBots = true;
            float a = e.SpawnT, b = float.IsFinite(e.EndT) ? e.EndT : end;
            if (b <= a) continue;
            edges.Add((a, 1, slot));
            edges.Add((b, -1, slot));
        }
        edges.Sort((x, y) => x.T != y.T ? x.T.CompareTo(y.T) : x.D.CompareTo(y.D)); // ends before starts at the same time
        var cur = new int[needBots.Length + 1];
        foreach (var (_, d, slot) in edges)
        {
            cur[slot] += d;
            if (slot < needBots.Length) needBots[slot] = Math.Max(needBots[slot], cur[slot]);
            else needTargets = Math.Max(needTargets, cur[slot]);
        }
        for (int i = 0; i < needBots.Length; i++) { needBots[i] = Math.Min(needBots[i], MaxPrewarm); PrewarmTotal += needBots[i]; }
        needTargets = Math.Min(needTargets, MaxPrewarm);
        PrewarmTotal += needTargets;
    }

    /// <summary>Creates one pooled node. False once every pool is full.</summary>
    public bool PrewarmStep()
    {
        for (int v = 0; v < needBots.Length; v++)
        {
            if (madeVariant[v] >= needBots[v]) continue;
            var b = NewBot(v);
            botPool[v].Push(b);
            madeVariant[v]++;
            madeBots++;
            return true;
        }
        if (madeTargets < needTargets)
        {
            targetPool.Push(NewTarget(0.2f));
            madeTargets++;
            return true;
        }
        return false;
    }

    BotCharacter NewBot(int variant)
    {
        var b = BotCharacter.CreatePuppet(enemy, outlines, variant);
        AddChild(b);
        b.Feet = new Vector3(0, -50f, 0);
        b.Visible = false;
        return b;
    }

    Target NewTarget(float radius)
    {
        var t = Target.Create(radius, enemy, valorantLook);
        t.Name = "Target";
        AddChild(t);
        t.Visible = false;
        return t;
    }

    public int IndexOf(ushort id) => index.TryGetValue(id, out int i) ? i : -1;

    /// <summary>The live puppet of bot entity <paramref name="id"/>, or null.</summary>
    public BotCharacter? Bot(ushort id) => index.TryGetValue(id, out int i) ? bots[i] : null;
    public BotCharacter? BotAt(int entityIndex) => bots[entityIndex];

    /// <summary>
    /// Acquires / releases nodes for time <paramref name="t"/> and applies the recorded state. New puppets (and, with
    /// <paramref name="resetAll"/>, every puppet) are snapped with <see cref="BotCharacter.PuppetReset"/>.
    /// </summary>
    public void Sync(float t, bool resetAll, bool flashes = true)
    {
        var ents = rec.Entities;
        for (int i = 0; i < ents.Count; i++)
        {
            var e = ents[i];
            if (e.Kind == EntKind.Bot)
            {
                if (!rec.BotAt(e, t, out var s)) { ReleaseBot(i, e); continue; }
                var b = bots[i];
                bool isNew = b == null;
                if (b == null)
                {
                    int v = Math.Clamp((int)e.Variant, 0, botPool.Length - 1);
                    b = botPool[v].Count > 0 ? botPool[v].Pop() : NewBot(v);
                    bots[i] = b;
                }
                b.PuppetApply(s);
                if (isNew || resetAll) b.PuppetReset();
                shown[i] = (s.Bits & BBits.Visible) != 0;
            }
            else
            {
                if (!rec.TargetAt(e, t, out var s)) { ReleaseTarget(i); continue; }
                var g = targets[i];
                if (g == null)
                {
                    g = targetPool.Count > 0 ? targetPool.Pop() : NewTarget(s.Radius);
                    targets[i] = g;
                }
                if (!Mathf.IsEqualApprox(g.Radius, s.Radius)) g.SetRadius(Mathf.Max(0.01f, s.Radius));
                g.Position = s.Pos;
                g.Visible = (s.Bits & TBits.Visible) != 0;
                g.SetHot((s.Bits & TBits.Hot) != 0);
                if (flashes && (s.Bits & TBits.Flash) != 0) g.Hit(); // not during a pre-roll: the flash lasts 80 ms of real time
            }
        }
    }

    void ReleaseBot(int i, EntityTrack e)
    {
        var b = bots[i];
        if (b == null) return;
        bots[i] = null;
        b.Visible = false;
        botPool[Math.Clamp((int)e.Variant, 0, botPool.Length - 1)].Push(b);
    }

    void ReleaseTarget(int i)
    {
        var g = targets[i];
        if (g == null) return;
        targets[i] = null;
        g.Visible = false;
        g.SetHot(false);
        targetPool.Push(g);
    }

    /// <summary>Advances every live puppet by <paramref name="dt"/> of replay time, then applies visibility.</summary>
    public void Step(float dt)
    {
        for (int i = 0; i < bots.Length; i++)
        {
            var b = bots[i];
            if (b == null) continue;
            b.PuppetStep(dt);
            b.Visible = shown[i] && !b.PuppetGone;
        }
    }

    /// <summary>Releases everything (e.g. before freeing the stage).</summary>
    public void ReleaseAll()
    {
        for (int i = 0; i < bots.Length; i++) if (bots[i] != null) ReleaseBot(i, rec.Entities[i]);
        for (int i = 0; i < targets.Length; i++) ReleaseTarget(i);
    }
}
