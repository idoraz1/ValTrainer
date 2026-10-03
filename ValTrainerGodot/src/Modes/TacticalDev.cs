using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>
/// DEV-ONLY auto-play for headless validation ("--dev --autotac", with "--simaim good" for the aim): the simulated player
/// only aims and shoots, so this moves the player. Post-Plant: hold the spot looking at the spike, walk toward a defuse you
/// can't see. Retake: walk the route to the spike, stop whenever an attacker is in sight, defuse when nobody is.
/// </summary>
public abstract partial class TacticalMode
{
    protected static readonly bool DevAuto = CmdLine.Dev && CmdLine.Has("--autotac");
    /// <summary>Dev "--tacghost": the bots never see you (tests their full retake / defuse and the detonation).</summary>
    protected static readonly bool DevGhost = CmdLine.Dev && CmdLine.Has("--tacghost");
    protected bool DevDefuseHeld;
    /// <summary>Hold crouch (through the dev-only <see cref="Mover.KeyOverride"/>; only with --autotac).</summary>
    protected bool DevCrouch;
    List<Vector3> devPath = new();
    int devIdx;
    float devRepathAt;

    void DevNewRound()
    {
        DevDefuseHeld = false;
        devPath.Clear();
        devIdx = 0;
    }

    void DevUpdate(float dt)
    {
        if (!DevAuto) return;
        Mover.KeyOverride = k => DevCrouch && k == Main.I.Valorant.KeyCrouch;
        if (G.Player.Dead || !Live) { DevDefuseHeld = false; DevCrouch = false; return; }
        DevPlay(dt);
    }

    protected abstract void DevPlay(float dt);

    protected bool DevEnemyVisible => Bots.Any(b => b.Alive && G.LineOfSight(G.View.Eye, b.Body.Head));
    protected bool DevHasPath => devIdx < devPath.Count && Now < devRepathAt;
    protected void DevStop() { devPath.Clear(); devIdx = 0; }

    protected void DevPathTo(Vector3 target)
    {
        devPath = Nav.FindPath(PlayerFeet, target) ?? new List<Vector3> { target };
        if (H(devPath[^1], target) > 0.1f) devPath.Add(target);
        devIdx = 0;
        devRepathAt = Now + 4f;
    }

    /// <summary>Moves the player along the dev path (teleport steps at <paramref name="speed"/>).</summary>
    protected void DevWalk(float dt, float speed)
    {
        float step = speed * dt;
        var feet = PlayerFeet;
        while (devIdx < devPath.Count && step > 0)
        {
            var target = devPath[devIdx];
            var to = new Vector3(target.X - feet.X, 0, target.Z - feet.Z);
            float d = to.Length();
            if (d <= step) { feet = target; step -= d; devIdx++; continue; }
            feet += to / d * step;
            feet.Y = Mathf.Lerp(feet.Y, target.Y, Mathf.Clamp(step / Mathf.Max(d, 0.01f), 0, 1));
            step = 0;
        }
        G.TeleportPlayer(feet);
    }

    /// <summary>A point a few metres further along the dev path (where to look while walking).</summary>
    protected Vector3 DevAhead(float dist)
    {
        var p = PlayerFeet;
        for (int i = devIdx; i < devPath.Count; i++)
        {
            float d = H(p, devPath[i]);
            if (d >= dist) return p + (devPath[i] - p) * (dist / d);
            dist -= d; p = devPath[i];
        }
        return devPath.Count > 0 ? devPath[^1] + (devPath[^1] - PlayerFeet).Normalized() * 0.01f : Spike;
    }

    protected void DevLook(Vector3 point)
    {
        var dir = (point - G.View.Eye);
        if (dir.LengthSquared() < 1e-4f) return;
        dir = dir.Normalized();
        G.View.Yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
        G.View.Pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(dir.Y, -1f, 1f)));
    }
}
