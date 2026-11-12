using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Fx;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// Dev-only viewmodel/FX test hook, active only with the user arg <c>--vmtest</c> (e.g. gshot -Extra "--vmtest").
/// Options: <c>--vmfire</c> auto-fire bursts with tracers/impacts/body hits on a test wall,
/// <c>--vmkind &lt;any WeaponKind&gt;</c> (e.g. Classic, Judge, Odin), <c>--vmleft</c>/<c>--vmright</c>, <c>--vmads 0..1</c>,
/// <c>--vmreload 0..1</c> (or <c>--vmreload loop</c>), <c>--vmspeed m/s</c>, <c>--vmcrouch</c>, <c>--vmlook</c>,
/// <c>--vmflash</c> (hold the muzzle flash), <c>--vmwall</c> (wall 0.2 m in front of the eye: clip test),
/// <c>--vmview side|left|top</c> (debug orthographic-ish views with a 5 cm grid).
/// Once the gun is fully aimed (<c>--vmads 1</c>, or a session aimed with <c>--force-ads</c>) it prints the ADS pose check
/// ("[vmtest] ads sight top …": the gun's top must sit at 0.60–0.65 H); <c>--vmprofile</c> adds the model's top silhouette.
/// The log may not be flushed when gshot kills the game: add <c>--vmshots T --vmout prefix</c> so it quits by itself.
/// <para>Contact sheets: <c>--vmcycle all|Gun1,Gun2,… [--vmevery S]</c> rebuilds the viewmodel with each gun in turn (S
/// seconds each, default 2.5), saves <c>prefix_&lt;Gun&gt;.png</c> near the end of each slot (with <c>--vmout prefix</c>),
/// runs the ADS check per gun and quits after the last one.</para>
/// <para>Gun skins: <c>--skin &lt;id&gt;</c> (any run, not only --vmtest: <see cref="GunSkins.DevSkin"/>) and
/// <c>--skincycle all|id1,id2,… [--vmevery S]</c>, which works like --vmcycle over skins on one gun (factory first with
/// "all"): saves <c>prefix_&lt;id&gt;.png</c> and quits after the last one. <c>--skinlive &lt;id&gt;</c> switches to that skin
/// 2.5 s in through <see cref="GunSkins.Changed"/> (the skin picker's live re-skin of a running drill or demo).</para>
/// <para>Skins 2 (skins2-design §5.1): --skincycle also takes choices (<c>honeycomb@2,phosphor/amber@5</c>) and
/// <c>levels</c> (factory, then every line with levels or variants at each level, then its variants at the top level);
/// <c>--skin-level N</c> adds <c>@N</c> to every cycled id without a level. Files are <c>prefix_&lt;line&gt;[.variant][_L&lt;n&gt;].png</c>.
/// Level effects: <c>--vmkill [S]</c> simulates a player kill every S s (default 1.5: <see cref="Viewmodel.OnPlayerKill"/> and
/// the kill counter +1); <c>--vmpulse fire|kill|both [D]</c> fires / kills D s (default 0.07) before every saved screenshot, so
/// contact sheets catch the pulses; <c>--vmkills N</c> starts the counter at N; <c>--vmgg</c> shows the counter's "GG".</para>
/// </summary>
static class VmTest
{
    public static bool Enabled, FxHold, Body, Fire, Crouch, Look, HoldFlash, Wall, ReloadLoop, NoHands, Hide, Bench, BenchFx;
    /// <summary>--vmskin line[/variant][@level]: the viewmodel wears this state (InspectFx.DevState: the catalogue's line, or a
    /// skins-3 stand-in from SkinLooks before the catalogue is merged), with its inspect flourish and special reload.</summary>
    public static Skins.SkinState? DevState;
    /// <summary>--vminspect [S]: start an inspect S s in (default 1) and again 1.2 s after each one ends;
    /// --vminspect-fire T: fire at T s (the cancel).</summary>
    static float inspectAt = -1f, inspectFireAt = -1f;
    /// <summary>--vmreload loop length (s, default 2.5): --vmreloadlen.</summary>
    static float reloadLen = 2.5f;
    public static WeaponKind? Kind;
    public static (Vector3 Pos, Vector3 Rot)? HipOverride;
    public static bool? ForceLeft;
    public static float? Ads, Reload, Speed, Zoom;
    public static string? View, Hand;
    public static float ViewD = 1f, Dist = 7f;
    public static Vector2 ViewOff;
    static string? shotPrefix;
    static float[] shotTimes = Array.Empty<float>();
    static int shotIdx;
    static WeaponKind[] cycle = Array.Empty<WeaponKind>();
    static string[] skinCycle = Array.Empty<string>();
    static string? liveSkin;
    static float killEvery, pulseD = 0.07f, killT;
    static string? pulse;
    static int kills;
    static bool gg, pulsed;
    static int cycleIdx = -1;
    static float every = 2.5f, gunClock;
    static bool cycleShot;
    static float clock;
    static bool inited;

    public static void Init()
    {
        if (inited) return;
        inited = true;
        var a = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
        Enabled = a.Contains("--vmtest");
        if (!Enabled) return;
        string? Val(string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
        float? F(string k) => float.TryParse(Val(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;
        Fire = a.Contains("--vmfire");
        Body = a.Contains("--vmbody");
        FxHold = a.Contains("--vmfxhold");
        Crouch = a.Contains("--vmcrouch");
        Look = a.Contains("--vmlook");
        HoldFlash = a.Contains("--vmflash");
        Wall = a.Contains("--vmwall");
        NoHands = a.Contains("--vmnohands");
        Hide = a.Contains("--vmhide");
        Bench = a.Contains("--vmbench") || a.Contains("--vmbenchfx");
        BenchFx = a.Contains("--vmbenchfx");
        Hand = Val("--vmhand");
        if (a.Contains("--vmleft")) ForceLeft = true;
        if (a.Contains("--vmright")) ForceLeft = false;
        if (Enum.TryParse<WeaponKind>(Val("--vmkind") ?? "", true, out var k)) Kind = k;
        Ads = F("--vmads");
        Speed = F("--vmspeed");
        Zoom = F("--vmzoom");
        if (Val("--vmreload") == "loop") ReloadLoop = true; else Reload = F("--vmreload");
        var cyc = Val("--vmcycle");
        if (cyc != null)
            cycle = cyc == "all" ? Weapons.All.Select(w => w.Kind).ToArray()
                : cyc.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Weapons.Parse).OfType<WeaponKind>().ToArray();
        every = F("--vmevery") ?? 2.5f;
        if (cycle.Length > 0) Kind = cycle[0];
        var sc = Val("--skincycle");
        if (sc != null && cycle.Length == 0)
        {
            skinCycle = sc == "all" ? GunSkins.All.Select(s => s.Id).Prepend(GunSkins.FactoryId).ToArray()
                : sc == "levels" ? LevelCycle().Prepend(GunSkins.FactoryId).ToArray()
                : sc.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(id => id == GunSkins.FactoryId || GunSkins.Get(id) != null).ToArray();
            if (F("--skin-level") is { } lv)
                skinCycle = skinCycle.Select(id => id == GunSkins.FactoryId || id.Contains('@') ? id : $"{id}@{(int)lv}").ToArray();
            if (skinCycle.Length > 0) GunSkins.DevSkin = skinCycle[0];
        }
        if (a.Contains("--vmkill")) killEvery = F("--vmkill") ?? 1.5f;
        pulse = Val("--vmpulse");
        int pi = Array.IndexOf(a, "--vmpulse");
        if (pulse != null && pi + 2 < a.Length && float.TryParse(a[pi + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pd)) pulseD = pd;
        kills = (int)(F("--vmkills") ?? 0f);
        gg = a.Contains("--vmgg");
        shotScale = Mathf.Clamp(F("--vmscale") ?? 0.6f, 0.1f, 1f);
        liveSkin = Val("--skinlive");
        DevState = Skins.InspectFx.DevState(Val("--vmskin"));
        if (a.Contains("--vminspect")) inspectAt = F("--vminspect") ?? 1f;
        inspectFireAt = F("--vminspect-fire") ?? -1f;
        reloadLen = F("--vmreloadlen") ?? 2.5f;
        View = Val("--vmview");
        // --vmhip x,y,z,pitch,yaw,roll: overrides the hip pose (framing studies)
        if (Val("--vmhip") is { } hp && hp.Split(',') is { Length: 6 } hv)
        {
            var n = hv.Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            HipOverride = (new Vector3(n[0], n[1], n[2]), new Vector3(n[3], n[4], n[5]));
        }
        ViewD = F("--vmviewd") ?? 1f;
        Dist = F("--vmdist") ?? 7f;
        ViewOff = new Vector2(F("--vmviewz") ?? 0f, F("--vmviewy") ?? 0f);
        // --vmshots 3,5.5 --vmout C:/path/prefix  → saves prefix_0.png, prefix_1.png from the game's own viewport
        shotPrefix = Val("--vmout");
        shotTimes = (Val("--vmshots") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        GD.Print($"[vmtest] fire={Fire} kind={Kind} left={ForceLeft} ads={Ads} reload={Reload}{(ReloadLoop ? " loop" : "")} view={View}");
    }

    /// <summary>--skincycle levels: every line with levels or variants at each level, then its variants at the top level.</summary>
    static IEnumerable<string> LevelCycle()
    {
        foreach (var l in Skins.SkinCatalog.Lines)
        {
            if (l.MaxLevel <= 1 && l.Variants.Count == 0) continue;
            for (int n = 1; n <= l.MaxLevel; n++) yield return l.MaxLevel > 1 ? $"{l.Id}@{n}" : l.Id;
            foreach (var v in l.Variants) yield return l.MaxLevel > 1 ? $"{l.Id}/{v.Id}@{l.MaxLevel}" : $"{l.Id}/{v.Id}";
        }
    }

    /// <summary>Saved screenshots are 3/5 of the window (<c>--vmscale F</c> changes it, 1 = full size).</summary>
    static void Shrink(Image img)
    {
        if (shotScale < 0.999f) img.Resize((int)(img.GetWidth() * shotScale), (int)(img.GetHeight() * shotScale), Image.Interpolation.Bilinear);
    }
    static float shotScale = 0.6f;

    /// <summary>A skin choice as a file name part: <c>phosphor/amber@5</c> → <c>phosphor.amber_L5</c>.</summary>
    static string FileId(string id) => id.Replace('/', '.').Replace("@", "_L");

    /// <summary>Debug view of the gun: distance and centre per layout (<see cref="VmPose.ViewD"/>, <see cref="VmPose.ViewZ"/>).</summary>
    public static Transform3D ViewXform(string view, VmPose pose)
    {
        float d = pose.ViewD * ViewD;
        float zc = pose.ViewZ + ViewOff.X;
        var b = view switch
        {
            "left" => new Basis(Vector3.Up, Mathf.Pi / 2),
            "top" => new Basis(Vector3.Right, Mathf.Pi / 2) * new Basis(Vector3.Up, -Mathf.Pi / 2),
            "front" => new Basis(Vector3.Up, Mathf.Pi),
            _ => new Basis(Vector3.Up, -Mathf.Pi / 2),
        };
        // Centre the gun's mid-length in front of the camera.
        return new Transform3D(b, new Vector3(0, 0, -d) - b * new Vector3(0, ViewOff.Y, zc));
    }

    static Node3D? wall, backdrop;
    static float fxT, fireT = 4f, burstLeft, burstGap, reloadT;
    static int shot;
    static readonly Random rng = new(1);

    public static void Attach(Viewmodel vm)
    {
        if (View != null) AddGrid(vm);
        if (Hand != null) AddHand(vm);
    }

    /// <summary>Standalone right hand (back toward the camera, fingers up) + a mirrored left hand.</summary>
    static void AddHand(Viewmodel vm)
    {
        var shape = VmPose.DebugShape(Hand!);
        var m = vm.Materials;
        var glove = m.Make(new Color(0.05f, 0.052f, 0.058f), 0f, 0.8f);
        var pad = m.Make(new Color(0.3f, 0.3f, 0.32f), 0f, 0.5f);
        var sleeve = m.Make(new Color(0.1f, 0.12f, 0.15f), 0f, 0.85f);
        var acc = m.Make(new Color(0.55f, 0.13f, 0.12f), 0f, 0.6f);
        var b = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0));
        for (int i = 0; i < 2; i++)
        {
            var mi = new MeshInstance3D { Mesh = VmHands.Build(shape, i == 1, Vector3.Back, glove, pad, sleeve, acc), Layers = Viewmodel.VisualLayer };
            var t = new Transform3D(b, new Vector3(i == 0 ? 0.07f : -0.07f, -0.05f, -0.32f));
            if (i == 1) t = VmMesh.Mirror(t);
            mi.Transform = t;
            vm.AddChild(mi);
        }
        vm.GunNode.Visible = false;
    }

    /// <summary>5 cm grid in gun space behind the gun (red = origin axes), drawn with the viewmodel shader.</summary>
    static void AddGrid(Viewmodel vm)
    {
        var mats = vm.Materials;
        var line = mats.Make(new Color(0, 0, 0), 0, 1, 0);
        line.SetShaderParameter("emission_color", new Color(0.2f, 0.9f, 0.3f));
        line.SetShaderParameter("emission_energy", 0.6f);
        var major = mats.Make(new Color(0, 0, 0), 0, 1, 0);
        major.SetShaderParameter("emission_color", new Color(0.9f, 0.9f, 0.9f));
        major.SetShaderParameter("emission_energy", 0.8f);
        var axis = mats.Make(new Color(0, 0, 0), 0, 1, 0);
        axis.SetShaderParameter("emission_color", new Color(1f, 0.1f, 0.1f));
        axis.SetShaderParameter("emission_energy", 1.5f);
        bool top = View == "top";
        float plane = top ? -0.16f : (View == "left" ? 0.07f : -0.07f);
        var root = new Node3D { Name = "Grid" };
        for (int i = -24; i <= 8; i++)
        {
            float z = i * 0.05f;
            var m = i == 0 ? axis : i % 2 == 0 ? major : line;
            var pos = top ? new Vector3(0, plane, z) : new Vector3(plane, 0, z);
            var size = top ? new Vector3(0.4f, 0.001f, 0.0012f) : new Vector3(0.001f, 0.6f, 0.0012f);
            root.AddChild(Bar(pos, size, m));
        }
        for (int i = -6; i <= 6; i++)
        {
            float c = i * 0.05f;
            var m = i == 0 ? axis : i % 2 == 0 ? major : line;
            var pos = top ? new Vector3(c, plane, -0.4f) : new Vector3(plane, c, -0.4f);
            var size = top ? new Vector3(0.0012f, 0.001f, 1.6f) : new Vector3(0.001f, 0.0012f, 1.6f);
            root.AddChild(Bar(pos, size, m));
        }
        vm.GunNode.AddChild(root);
    }

    static MeshInstance3D Bar(Vector3 pos, Vector3 size, Material m)
    {
        var b = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos, Layers = Viewmodel.VisualLayer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        return b;
    }

    // A/B benchmark: alternate viewmodel hidden/shown every second (after 4 s warm-up), average GPU/CPU frame time.
    static readonly double[] gpuSum = new double[2], cpuSum = new double[2];
    static readonly int[] frames = new int[2];
    static bool measuring;
    static void BenchTick(Viewmodel vm, float dt)
    {
        var rid = vm.GetViewport().GetViewportRid();
        if (!measuring) { RenderingServer.ViewportSetMeasureRenderTime(rid, true); measuring = true; }
        if (clock < 4f) return;
        int phase = ((int)(clock - 4f)) % 2; // 0 = hidden / not firing, 1 = shown / firing
        if (BenchFx) { Fire = phase == 1; Dist = 4f; } else vm.Visible = phase == 1;
        float inPhase = (clock - 4f) % 1f;
        if (inPhase > 0.25f) // skip the switch frames
        {
            gpuSum[phase] += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
            cpuSum[phase] += dt * 1000.0;
            frames[phase]++;
        }
        if (clock > 4f + 14f && frames[0] > 0 && frames[1] > 0)
        {
            GD.Print($"[vmbench{(BenchFx ? "fx" : "")}] off: gpu {gpuSum[0] / frames[0]:0.00} ms frame {cpuSum[0] / frames[0]:0.00} ms | on: gpu {gpuSum[1] / frames[1]:0.00} ms frame {cpuSum[1] / frames[1]:0.00} ms ({frames[0]}/{frames[1]} frames)");
            Bench = false;
            vm.Visible = true;
            vm.GetTree().Quit();
        }
    }

    /// <summary>ADS pose check, printed once the gun is fully aimed (--vmads 1, or a session aimed with --force-ads): the
    /// gun's on-screen top must sit at 0.60–0.65 H, below the crosshair, like VALORANT (ads-model.md §4.3).
    /// --vmprofile also prints the model's gun-space top silhouette.</summary>
    static void AdsCheck(Viewmodel vm)
    {
        if (adsChecked || gunClock < 1.5f || View != null) return;
        var (top, x, width, ads) = vm.SightTop();
        if (ads < 0.999f) return;
        adsChecked = true;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float aspect = vm.GetViewport().GetVisibleRect().Size.Aspect();
        bool ok = top >= 0.60f && top <= 0.65f && Mathf.Abs(x - aspect / 2f) < 0.03f;
        GD.Print(string.Format(inv, "[vmtest] ads sight top {0}: y {1:0.000} H (target 0.60-0.65), x {2:+0.000;-0.000} H from centre, top width {3:0.000} H -> {4}",
            vm.Held, top, x - aspect / 2f, width, ok ? "PASS" : "FAIL"));
        if (CmdLine.Has("--vmprofile"))
            foreach (var (z, y, px) in vm.GunProfile())
                GD.Print(string.Format(inv, "[vmprofile] z {0:0.000} y {1:0.000} x {2:0.000}", z, y, px));
    }
    static bool adsChecked;

    /// <summary>--vmcycle: next gun every <see cref="every"/> s; a screenshot 0.3 s before each switch.</summary>
    static void CycleTick(Viewmodel vm)
    {
        int i = (int)(clock / every);
        if (i != cycleIdx)
        {
            if (i >= cycle.Length) { cycle = Array.Empty<WeaponKind>(); vm.GetTree().CreateTimer(0.2).Timeout += () => vm.GetTree().Quit(); return; }
            if (cycleIdx >= 0) vm.Rebuild(cycle[i]);
            cycleIdx = i; gunClock = clock - i * every; adsChecked = false; cycleShot = false; pulsed = false;
            fireT = 1.2f; burstLeft = 0; reloadT = 0f;
        }
        if (!cycleShot && shotPrefix != null && clock >= (i + 1) * every - 0.3f)
        {
            cycleShot = true;
            var img = vm.GetViewport().GetTexture().GetImage();
            Shrink(img);
            img.SavePng($"{shotPrefix}_{cycle[i]}.png");
            GD.Print($"[vmtest] saved {shotPrefix}_{cycle[i]}.png");
        }
    }

    /// <summary>--skincycle: next skin every <see cref="every"/> s on the same gun; a screenshot 0.3 s before each switch.</summary>
    static void SkinCycleTick(Viewmodel vm)
    {
        int i = (int)(clock / every);
        if (i != cycleIdx)
        {
            if (i >= skinCycle.Length) { skinCycle = Array.Empty<string>(); vm.GetTree().CreateTimer(0.2).Timeout += () => vm.GetTree().Quit(); return; }
            GunSkins.DevSkin = skinCycle[i];
            if (cycleIdx >= 0) vm.Rebuild(vm.Held);
            cycleIdx = i; gunClock = clock - i * every; cycleShot = false; pulsed = false;
            fireT = 1.2f; burstLeft = 0; reloadT = 0f;
            GD.Print($"[vmtest] skin {skinCycle[i]} on {vm.Held}");
        }
        if (!cycleShot && shotPrefix != null && clock >= (i + 1) * every - 0.3f)
        {
            cycleShot = true;
            var img = vm.GetViewport().GetTexture().GetImage();
            Shrink(img);
            img.SavePng($"{shotPrefix}_{FileId(skinCycle[i])}.png");
            GD.Print($"[vmtest] saved {shotPrefix}_{FileId(skinCycle[i])}.png");
        }
    }

    /// <summary>--vmkill / --vmpulse / --vmkills / --vmgg: simulated kills and shots for the skin level effects.</summary>
    static void SkinFxTick(Viewmodel vm, float dt)
    {
        if (killEvery > 0f)
        {
            killT += dt;
            if (killT >= killEvery) { killT -= killEvery; Kill(vm); }
        }
        if (pulse != null && !pulsed)
        {
            // the next screenshot: the cycle's (0.3 s before each switch) or the next --vmshots time
            float next = cycle.Length > 0 || skinCycle.Length > 0 ? (cycleIdx + 1) * every - 0.3f
                : shotIdx < shotTimes.Length ? shotTimes[shotIdx] : float.MaxValue;
            if (clock >= next - pulseD)
            {
                pulsed = true;
                if (pulse is "fire" or "both") vm.OnFire();
                if (pulse is "kill" or "both") Kill(vm);
            }
        }
        vm.SetKillCount(kills, gg);
    }

    static void Kill(Viewmodel vm)
    {
        vm.OnPlayerKill();
        kills++;
        GD.Print($"[vmtest] kill {kills} at {clock:0.00}s");
    }

    public static void Tick(Viewmodel vm, float dt)
    {
        clock += dt;
        gunClock += dt;
        SkinFxTick(vm, dt);
        if (inspectAt >= 0f && clock >= inspectAt && !vm.Inspecting && vm.CanInspect)
        {
            vm.Inspect();
            GD.Print($"[vmtest] inspect at {clock:0.00}s");
            inspectAt = clock + VmInspect.For(vm.Held).Duration + 1.2f;
        }
        if (inspectFireAt >= 0f && clock >= inspectFireAt)
        {
            inspectFireAt = -1f;
            GD.Print($"[vmtest] fire at {clock:0.00}s (inspecting {vm.Inspecting})");
            vm.OnFire();
        }
        if (cycle.Length > 0) CycleTick(vm);
        else if (skinCycle.Length > 0) SkinCycleTick(vm);
        // --skinlive <id>: 2.5 s in, every gun switches to that skin through GunSkins.Changed (the live re-skin path)
        if (liveSkin != null && clock >= 2.5f)
        {
            GunSkins.DevSkin = liveSkin;
            liveSkin = null;
            GunSkins.NotifyChanged();
            GD.Print($"[vmtest] live re-skin to {GunSkins.DevSkin}");
        }
        AdsCheck(vm);
        if (Hide) vm.Hide();
        if (Bench) BenchTick(vm, dt);
        if (shotPrefix != null && shotIdx < shotTimes.Length && clock >= shotTimes[shotIdx])
        {
            var img = vm.GetViewport().GetTexture().GetImage();
            Shrink(img);
            img.SavePng($"{shotPrefix}_{shotIdx}.png");
            GD.Print($"[vmtest] saved {shotPrefix}_{shotIdx}.png at {clock:0.00}s fps={Engine.GetFramesPerSecond()}");
            shotIdx++;
            pulsed = false;
            if (shotIdx >= shotTimes.Length) vm.GetTree().CreateTimer(0.3).Timeout += () => vm.GetTree().Quit();
        }
        if (ReloadLoop)
        {
            reloadT += dt;
            float cyc = reloadT % (reloadLen + 0.9f);
            Reload = cyc < reloadLen ? cyc / reloadLen : -1f;
        }
        var cam = vm.GetParent() as Camera3D;
        if (cam == null) return;
        if (Zoom is { } z) cam.Fov = Mathf.RadToDeg(2f * Mathf.Atan(Mathf.Tan(Mathf.DegToRad(PlayerView.HipHFov / 2f)) / z));
        if (Wall && wall == null && Effects.Root != null)
        {
            wall = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(3f, 3f, 0.05f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.55f, 0.5f), Roughness = 0.9f },
            };
            Effects.Root.AddChild(wall);
            wall.GlobalTransform = cam.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, -0.2f));
        }
        if (FxHold && Effects.Root != null)
        {
            // Continuous effects at fixed points 3 m ahead: wall impact (left), body hit (centre), headshot (right).
            fxT -= dt;
            if (fxT <= 0f)
            {
                fxT = 0.05f;
                var f = -cam.GlobalTransform.Basis.Z; var r = cam.GlobalTransform.Basis.X; var e = cam.GlobalTransform.Origin;
                var col = Main.I?.EnemyColor ?? new Color(1, 0.2f, 0.2f);
                Effects.Impact(e + f * 3f - r * 0.8f, -f, new Color(0.55f, 0.52f, 0.48f));
                Effects.BodyHit(e + f * 3f, col, false);
                Effects.BodyHit(e + f * 3f + r * 0.8f, col, true);
            }
        }
        if (!Fire) return;
        if (backdrop == null && Effects.Root != null)
        {
            backdrop = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(8f, 5f, 0.1f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.6f, 0.56f), Roughness = 0.9f },
            };
            Effects.Root.AddChild(backdrop);
            backdrop.GlobalTransform = cam.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, -Dist - 0.05f));
        }
        var def = Weapons.Get(vm.Held) ?? Weapons.Vandal;
        var look = WeaponLooks.For(vm.Held);
        fireT -= dt;
        if (fireT > 0) return;
        if (burstLeft <= 0)
        {
            burstLeft = def.Auto ? 6 : 1;
            burstGap = def.Auto ? def.Interval : 0f;
        }
        burstLeft--;
        fireT = burstLeft > 0 ? burstGap : look.BoltCycle || look.Pump ? 1.6f : def.Auto ? 1.0f : Mathf.Max(0.6f, def.Interval);
        if (BenchFx) fireT = def.Interval;
        vm.OnFire();

        // Shoot at a virtual wall 7 m ahead (or the real geometry behind it).
        var ct = cam.GlobalTransform;
        var fwd = -ct.Basis.Z;
        float sx = (float)(rng.NextDouble() - 0.5) * 0.5f, sy = (float)(rng.NextDouble() - 0.3) * 0.35f;
        var dir = (fwd + ct.Basis.X * sx + ct.Basis.Y * sy).Normalized();
        var eye = ct.Origin;
        float dist = Dist / Mathf.Max(0.2f, dir.Dot(fwd));
        var hit = eye + dir * dist;
        var mw = vm.MuzzleWorld;
        Effects.Tracer(mw, hit);
        if (Zoom != null && shot == 0) GD.Print($"[vmtest] muzzle screen: tracer start {cam.UnprojectPosition(mw)} vs drawn {vm.MuzzleScreen()}");
        shot++;
        if (Body) Effects.BodyHit(hit + fwd * 0.3f, Main.I?.EnemyColor ?? new Color(1, 0.2f, 0.2f), shot % 2 == 0);
        else if (shot % 4 == 0) Effects.BodyHit(hit + fwd * 0.3f, Main.I?.EnemyColor ?? new Color(1, 0.2f, 0.2f), shot % 8 == 0);
        else Effects.Impact(hit, -fwd, new Color(0.55f, 0.52f, 0.48f));
    }
}
