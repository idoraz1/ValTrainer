using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game.Replay;

/// <summary>
/// Records a run for the 3D Rewind while it plays: the player's state the .vtt frames lack, every bot and target from
/// its spawn until it leaves the tree, and the visual events (bot hits / deaths / shots, player shots, weapon switches).
/// <para>Cheap by design: 60 Hz decimation (frames arrive at hundreds per second), plain list appends, no per-frame
/// allocations. An extra sample with the Cut bit is forced at the exact frame of a discontinuity (a jump over 0.5 m, a
/// visibility or flash change), preceded by the previous frame's position so the replay holds the right spot until it.</para>
/// <para>Never throws into gameplay: any error disables recording for the rest of the run (the run then has no 3D data).</para>
/// </summary>
public sealed class ReplayRecorder
{
    const float Step = 1f / 60f, Slack = 1e-4f, JumpSq = 0.5f * 0.5f;

    readonly ReplayRecording rec = new();
    readonly Func<float> clock;
    readonly List<Tracked> live = new();
    readonly Action<BotCharacter, BotCharacter.BotFx, Vector3, int> onFx;
    ushort nextId = 1;
    float next;               // next 60 Hz tick (session time)
    bool finished;
    Vector3 prevEye; float prevEyeT = -1;

    /// <summary>False after an error: nothing more is recorded and <see cref="Finish"/> returns null.</summary>
    public bool Enabled { get; private set; } = true;
    public ReplayRecording Recording => rec;

    sealed class Tracked
    {
        public EntityTrack Track = null!;
        public BotCharacter? Bot;
        public Target? Tgt;
        public Vector3 PrevPos;
        public float PrevT = -1;
        public byte PrevBits;
        public bool Ended;
    }

    /// <param name="clock">The session's run time (GameSession.Now), read when bots/targets leave or fire events.</param>
    public ReplayRecorder(ReplayHeader header, Func<float> clock)
    {
        rec.Header = header;
        this.clock = clock;
        onFx = OnFx;
    }

    void Fail(Exception e)
    {
        if (!Enabled) return;
        Enabled = false;
        GD.PushWarning($"Rewind recording stopped: {e.GetType().Name}: {e.Message}");
        Log.Error($"Rewind recording stopped: {e}");
    }

    // ------------------------------------------------------------------ spawns

    public void TrackBot(BotCharacter b, float now)
    {
        if (!Enabled) return;
        try
        {
            var tr = Add(EntKind.Bot, (byte)Math.Clamp(b.Variant, 0, 255), now);
            tr.Bot = b;
            b.Fx += onFx;
            b.TreeExiting += () => End(tr);
        }
        catch (Exception e) { Fail(e); }
    }

    public void TrackTarget(Target t, float now)
    {
        if (!Enabled) return;
        try
        {
            var tr = Add(EntKind.Target, 0, now);
            tr.Tgt = t;
            t.TreeExiting += () => End(tr);
        }
        catch (Exception e) { Fail(e); }
    }

    Tracked Add(EntKind kind, byte variant, float now)
    {
        if (nextId == ushort.MaxValue) throw new InvalidOperationException("too many entities");
        var track = new EntityTrack { Id = nextId++, Kind = kind, Variant = variant, SpawnT = now };
        rec.Entities.Add(track);
        var tr = new Tracked { Track = track };
        live.Add(tr);
        return tr;
    }

    void End(Tracked tr)
    {
        if (!Enabled || finished || tr.Ended) return;
        try
        {
            float now = clock();
            // Still in the tree here: one last sample so the replay keeps it until it really left.
            if (Valid(tr)) SampleEntity(tr, now, cut: false, force: true);
            tr.Ended = true;
            tr.Track.EndT = now;
            live.Remove(tr);
        }
        catch (Exception e) { Fail(e); }
    }

    static bool Valid(Tracked tr) =>
        tr.Bot != null ? GodotObject.IsInstanceValid(tr.Bot) && tr.Bot.IsInsideTree()
        : tr.Tgt != null && GodotObject.IsInstanceValid(tr.Tgt) && tr.Tgt.IsInsideTree();

    // ------------------------------------------------------------------ events

    void OnFx(BotCharacter b, BotCharacter.BotFx kind, Vector3 p, int arg)
    {
        if (!Enabled || finished) return;
        try
        {
            ushort id = 0;
            foreach (var tr in live) if (tr.Bot == b) { id = tr.Track.Id; break; }
            if (id == 0) return;
            var rx = kind switch { BotCharacter.BotFx.Hit => RxKind.BotHit, BotCharacter.BotFx.Die => RxKind.BotDie, _ => RxKind.BotShoot };
            Push(new ReplayEvent(clock(), rx, id, p, Vector3.Zero, arg));
        }
        catch (Exception e) { Fail(e); }
    }

    /// <summary>A player bullet: <paramref name="shotIndex"/> into RunTelemetry.Shots, where it ended and the wall's
    /// normal there (zero = it didn't end on a wall).</summary>
    public void PlayerShot(float now, int shotIndex, Vector3 end, Vector3 wallNormal)
    {
        if (!Enabled || finished) return;
        try { Push(new ReplayEvent(now, RxKind.PlayerShot, 0, end, wallNormal, shotIndex)); }
        catch (Exception e) { Fail(e); }
    }

    public void WeaponSwitch(float now, WeaponKind kind)
    {
        if (!Enabled || finished) return;
        try { Push(new ReplayEvent(now, RxKind.Weapon, 0, Vector3.Zero, Vector3.Zero, (int)kind)); }
        catch (Exception e) { Fail(e); }
    }

    void Push(ReplayEvent e)
    {
        var l = rec.Events;
        // Events must stay sorted by time (FirstEventAtOrAfter); the session clock never goes back, but be safe.
        if (l.Count > 0 && l[^1].T > e.T) e = e with { T = l[^1].T };
        l.Add(e);
    }

    // ------------------------------------------------------------------ per frame

    /// <summary>End of each running frame (GameSession.RecordFrame). <paramref name="vmShown"/>: the first-person gun is
    /// visible.</summary>
    public void Frame(GameSession s, float now, bool vmShown)
    {
        if (!Enabled || finished) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        frames++;
        try
        {
            bool due = now >= next - Slack;
            if (due) { next += Step; if (next <= now) next = now + Step; }

            // Player: a jump of the eye (respawn, teleport) is a cut.
            var eye = s.View.Eye;
            bool cut = prevEyeT >= 0 && (eye - prevEye).LengthSquared() > JumpSq;
            if (cut || due || rec.Player.Count == 0)
            {
                if (cut && rec.Player.Count > 0 && prevEyeT > rec.Player[^1].T)
                {
                    var hold = rec.Player[^1];
                    hold.T = prevEyeT; hold.Eye = prevEye; hold.Bits &= ~PBits.Cut;
                    rec.Player.Add(hold);
                }
                rec.Player.Add(SamplePlayer(s, now, vmShown, cut));
            }
            prevEye = eye; prevEyeT = now;

            for (int i = live.Count - 1; i >= 0; i--)
            {
                var tr = live[i];
                if (!Valid(tr))
                {
                    // Freed without leaving the tree normally: end it here.
                    tr.Ended = true; tr.Track.EndT = now; live.RemoveAt(i);
                    continue;
                }
                Step1(tr, now, due);
            }
        }
        catch (Exception e) { Fail(e); }
        ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    long ticks, frames; // dev log: the recorder's own cost per frame

    PlayerSample SamplePlayer(GameSession s, float now, bool vmShown, bool cut)
    {
        var p = new PlayerSample
        {
            T = now, Eye = s.View.Eye, VisualZoom = s.View.VisualZoom, AdsBlend = s.AdsBlend,
            Blind = s.Blind != null ? s.Blind.Strength : 0f,
        };
        var gun = s.Gun;
        if (gun != null && gun.Reloading && gun.Def.Reload > 0) p.Reload = Math.Clamp(1f - gun.ReloadLeft / gun.Def.Reload, 0f, 1f);
        PBits b = 0;
        if (cut) b |= PBits.Cut;
        if (s.AbilityInHand) b |= PBits.AbilityInHand;
        if (vmShown) b |= PBits.VmShown;
        if (gun?.Scoped == true) b |= PBits.Scoped;
        if (s.Mover.Crouching) b |= PBits.Crouch;
        if (s.Mode.Focus is { } f) { b |= PBits.HasFocus; p.Focus = f.Point; }
        p.Bits = b;
        return p;
    }

    /// <summary>One entity, one frame: detect a discontinuity (cut), else sample on the 60 Hz tick.</summary>
    void Step1(Tracked tr, float now, bool due)
    {
        Vector3 pos;
        byte bits;
        if (tr.Bot is { } b) { pos = b.Feet; bits = (byte)(b.Visible ? BBits.Visible : 0); }
        else
        {
            var t = tr.Tgt!;
            pos = t.GlobalPosition;
            bits = (byte)((t.Visible ? TBits.Visible : 0) | (t.Hot ? TBits.Hot : 0) | (t.Flashing ? TBits.Flash : 0));
        }
        int count = tr.Bot != null ? tr.Track.Bot.Count : tr.Track.Tgt.Count;
        // A jump or a visibility / flash change is a cut; a target turning hot is sampled at that frame (bits are steps
        // anyway). A bot's crouch / aim change is smooth: the 60 Hz tick is enough.
        byte cutMask = tr.Bot != null ? (byte)BBits.Visible : (byte)(TBits.Visible | TBits.Flash);
        byte changed = (byte)(bits ^ tr.PrevBits);
        bool seen = tr.PrevT >= 0;
        bool cut = seen && ((pos - tr.PrevPos).LengthSquared() > JumpSq || (changed & cutMask) != 0);
        if (cut && count > 0 && tr.PrevT > LastT(tr)) HoldPrevious(tr);
        if (cut || due || count == 0 || (seen && changed != 0)) SampleEntity(tr, now, cut, force: false, pos);
        tr.PrevPos = pos; tr.PrevT = now; tr.PrevBits = bits;
    }

    static float LastT(Tracked tr) => tr.Bot != null ? tr.Track.Bot[^1].T : tr.Track.Tgt[^1].T;

    /// <summary>Before a cut: the previous frame's position with the last sample's state (so the replay doesn't slide
    /// from the last 60 Hz sample into the jump).</summary>
    static void HoldPrevious(Tracked tr)
    {
        if (tr.Bot != null)
        {
            var h = tr.Track.Bot[^1];
            h.T = tr.PrevT; h.Feet = tr.PrevPos; h.Bits &= ~BBits.Cut;
            tr.Track.Bot.Add(h);
        }
        else
        {
            var h = tr.Track.Tgt[^1];
            h.T = tr.PrevT; h.Pos = tr.PrevPos; h.Bits &= ~TBits.Cut;
            tr.Track.Tgt.Add(h);
        }
    }

    void SampleEntity(Tracked tr, float now, bool cut, bool force, Vector3? knownPos = null)
    {
        if (tr.Bot is { } b)
        {
            var l = tr.Track.Bot;
            if (force && l.Count > 0 && l[^1].T >= now) return;
            var s = new BotSample
            {
                T = now, Feet = knownPos ?? b.Feet, Vel = b.Velocity, Facing = Mathf.DegToRad(b.FacingYaw),
            };
            BBits bits = 0;
            if (cut) bits |= BBits.Cut;
            if (b.Crouched) bits |= BBits.Crouched;
            if (b.Grounded) bits |= BBits.Grounded;
            if (b.Visible) bits |= BBits.Visible;
            if (b.AimTarget is { } at)
            {
                bits |= BBits.Aiming;
                var to = at - b.Head;
                s.AimPitch = Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length());
            }
            s.Bits = bits;
            l.Add(s);
        }
        else if (tr.Tgt is { } t)
        {
            var l = tr.Track.Tgt;
            if (force && l.Count > 0 && l[^1].T >= now) return;
            TBits bits = 0;
            if (cut) bits |= TBits.Cut;
            if (t.Visible) bits |= TBits.Visible;
            if (t.Hot) bits |= TBits.Hot;
            if (t.Flashing) bits |= TBits.Flash;
            l.Add(new TargetSample { T = now, Pos = knownPos ?? t.GlobalPosition, Radius = t.Radius, Bits = bits });
        }
    }

    // ------------------------------------------------------------------ finish

    /// <summary>The run ended at <paramref name="now"/>: last samples, entities still alive stay open-ended. Returns the
    /// recording (null if recording failed). Nothing is recorded afterwards, so a worker can save it safely.</summary>
    public ReplayRecording? Finish(float now)
    {
        if (finished) return Enabled ? rec : null;
        try
        {
            if (Enabled)
            {
                // Close every track at the end time: the player's last eye, the entities' current state.
                if (rec.Player.Count > 0 && rec.Player[^1].T < now)
                {
                    var last = rec.Player[^1];
                    last.T = now; last.Bits &= ~PBits.Cut;
                    if (prevEyeT >= 0) last.Eye = prevEye;
                    rec.Player.Add(last);
                }
                foreach (var tr in live)
                    if (Valid(tr)) SampleEntity(tr, now, cut: false, force: true);
                rec.Duration = now;
                if (CmdLine.Dev && frames > 0)
                    Log.Info(FormattableString.Invariant(
                        $"[rewind] {rec.Player.Count} player samples, {rec.Entities.Count} entities ({rec.Entities.Sum(e => e.Bot.Count + e.Tgt.Count)} samples), {rec.Events.Count} events; recorder {ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / frames:0.0} µs/frame over {frames} frames"));
            }
        }
        catch (Exception e) { Fail(e); }
        finished = true;
        foreach (var tr in live) if (tr.Bot is { } b && GodotObject.IsInstanceValid(b)) b.Fx -= onFx;
        live.Clear();
        return Enabled ? rec : null;
    }
}
