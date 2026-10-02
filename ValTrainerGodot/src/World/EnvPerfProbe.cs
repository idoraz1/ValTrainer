using Godot;
using ValTrainer.Game.Fx;

namespace ValTrainer.World;

/// <summary>
/// Dev-only helper (see <see cref="EnvDev"/>). <c>--envperf</c>: logs FPS, GPU/CPU render time and draw calls
/// every 2 s. <c>--envcam x,y,z,yaw,pitch</c>: takes over the view with a fixed camera (clean environment shots).
/// <c>--envtweak flashtest</c> (with --envcam): a row of all agent flash orbs in front of the camera that fly,
/// wind up and pop in slow motion, for inspecting the effects.
/// </summary>
public partial class EnvPerfProbe : Node
{
    public int Quality;
    public bool Log;
    public float[]? Cam;
    double t, gpu, cpu, age;
    int frames;
    Camera3D? cam;
    readonly List<FlashOrb> orbs = new();
    readonly List<Vector3> orbHome = new();
    int flashStage;

    public override void _Ready() => RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

    public override void _Process(double delta)
    {
        age += delta;
        if (Cam is { Length: >= 5 } && age > 0.3 && cam == null)
        {
            cam = new Camera3D { Fov = 103f, KeepAspect = Camera3D.KeepAspectEnum.Width, Near = 0.03f, Far = 400f };
            GetParent().AddChild(cam);
            cam.GlobalPosition = new Vector3(Cam[0], Cam[1], Cam[2]);
            cam.GlobalRotationDegrees = new Vector3(Cam[4], -Cam[3], 0);
        }
        if (cam != null && !cam.Current) cam.MakeCurrent();
        if (EnvDev.Has("nobots") && GetParent()?.GetParent()?.GetNodeOrNull<Node3D>("ModeWorld") is { Visible: true } mw) mw.Visible = false;
        if (cam != null && EnvDev.Has("flashtest")) FlashTest();
        if (!Log) return;

        var rid = GetViewport().GetViewportRid();
        gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
        cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
        frames++;
        t += delta;
        if (t < 2.0) return;
        var size = GetViewport().GetVisibleRect().Size;
        long draws = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        long prims = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        double proc = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000, phys = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000;
        GD.Print($"[env] q={Quality} fps={frames / t:0} gpu={gpu / frames:0.00}ms cpu={cpu / frames:0.00}ms process={proc:0.00}ms physics={phys:0.00}ms draws={draws} prims={prims} res={size.X}x{size.Y}");
        t = 0; gpu = 0; cpu = 0; frames = 0;
    }

    void FlashTest()
    {
        var b = cam!.GlobalTransform.Basis;
        var fwd = -b.Z; var right = b.X;
        if (flashStage == 0 && age > 1.0)
        {
            flashStage = 1;
            int n = FlashAgent.All.Length, i = 0;
            foreach (var a in FlashAgent.All)
            {
                var o = FlashOrb.Create(a.Color);
                GetParent().AddChild(o);
                var home = cam.GlobalPosition + fwd * 5f + right * ((i - (n - 1) / 2f) * 1.1f);
                o.GlobalPosition = home;
                orbs.Add(o); orbHome.Add(home);
                i++;
            }
            GD.Print("[flashtest] spawned");
        }
        if (flashStage == 1)
        {
            float tt = (float)age;
            for (int i = 0; i < orbs.Count; i++)
                if (IsInstanceValid(orbs[i]))
                    orbs[i].GlobalPosition = orbHome[i] + new Vector3(Mathf.Cos(tt * 5f + i) * 0.35f, Mathf.Sin(tt * 5f + i) * 0.35f, 0);
            if (age > 4.0) { flashStage = 2; GD.Print("[flashtest] windup"); }
        }
        if (flashStage == 2)
        {
            float w = (float)age - 4.0f;
            foreach (var o in orbs) if (IsInstanceValid(o)) o.Windup(w);
            if (age > 5.6)
            {
                flashStage = 3;
                Engine.TimeScale = 0.06;
                foreach (var o in orbs) if (IsInstanceValid(o)) o.Pop();
                GD.Print("[flashtest] pop (slow motion)");
            }
        }
    }

    public override void _ExitTree()
    {
        if (flashStage > 0) Engine.TimeScale = 1.0;
    }
}
