using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>
/// Runs the analysis once on a tiny synthetic flick run on a worker thread at startup, so .NET compiles the coach
/// code before the first real results screen (otherwise the first review costs a ~60 ms hitch on the main thread).
/// </summary>
public static class CoachWarmUp
{
    public static void Start() => Task.Run(() =>
    {
        try
        {
            var t = new RunTelemetry { Mode = "flick", Tier = 1, Sens = 0.4f, Dpi = 800, Weapon = "Vandal", Map = "range", Duration = 2f };
            const float dt = 1f / 240f, start = 0.45f, move = 0.3f, target = 20f;
            float prevYaw = 0;
            t.Events.Add(new TelemetryEvent(0.2f, "target_spawn", 1, target));
            for (int i = 0; i < 480; i++)
            {
                float time = i * dt, u = Math.Clamp((time - start) / move, 0f, 1f);
                float yaw = target * (10 * u * u * u - 15 * u * u * u * u + 6 * u * u * u * u * u); // minimum-jerk flick
                bool live = time >= 0.2f && time < 0.85f;
                t.Frames.Add(new FrameSample
                {
                    T = time, Yaw = yaw, Dx = (yaw - prevYaw) / (PlayerView.DegPerCount * t.Sens),
                    FocusId = live ? 1 : 0, FocusYaw = live ? target - yaw : 0, FocusRadius = live ? 0.6f : 0,
                    Flags = (byte)(FrameFlags.Accurate | FrameFlags.Alive),
                });
                prevYaw = yaw;
            }
            t.Shots.Add(new ShotSample { T = 0.85f, Yaw = target, Zone = 0, FocusId = 1, FocusRadius = 0.6f, Accurate = true });
            t.Events.Add(new TelemetryEvent(0.85f, "target_hit", 1, 650));
            var m = Coach.Summarize(t);
            Coach.ReviewRun(t, m, 1);
        }
        catch { /* warm-up only */ }
    });
}
