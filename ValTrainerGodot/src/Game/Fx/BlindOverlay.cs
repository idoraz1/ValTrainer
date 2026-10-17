using System.Globalization;
using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Fx;

/// <summary>Per-agent blind screens (VALORANT tints each agent's flash; Dizzy's plasma is a goo blob instead).</summary>
public enum BlindLook { White, Phoenix, Breach, KayO, Skye, Yoru, Vyse, Dizzy }

/// <summary>
/// The "you are flashed" screen, one full-screen procedural shader pass (no textures; GLES3/Compatibility-safe).
/// <para>Agent flashes: an OPAQUE agent-tinted backdrop with a star-burst of tinted rays radiating from a small
/// white-hot core at the pop's on-screen position (pushed to the screen edge when it popped off-screen, so the rays
/// stream in from that side). The onset is a brief tinted swell, never a white frame (VALORANT 2.02 toned the initial
/// brightness down). Full-blind indicator (VALORANT 5.07 "1P visuals shrink over time"): while the full blind runs,
/// the rays, bloom and glow contract toward the core and the periphery dims, so the bright area visibly shrinks until
/// the fade begins. Fade: 1 s for full flashes, front-loaded (alpha = (1 - t/fade)^2, VALORANT 3.0 "visibility
/// returns faster"), with the screen edges clearing before the core.</para>
/// <para>White: the generic near-white version for blinds that aren't an agent's flash (centred).</para>
/// <para>Dizzy: not a flash — a big irregular electric-blue goo blob (~80% × 90% of the screen) with marbled darker
/// contour lines, a glowing cyan inner band and ragged splatter tendrils; the world stays visible at the edges and
/// corners. 1 s hold + exactly 1 s fade, dissolving inward.</para>
/// </summary>
public partial class BlindOverlay : Control
{
    float full, fade = BlindModel.FadeTime, t = 1e9f, anim, seed;
    bool wasActive;
    ShaderMaterial? mat;

    public bool Active => t < full + fade;
    public bool FullyBlind => t < full;
    /// <summary>How blind the screen is right now, 0..1 (the overlay's overall alpha as drawn; Rewind recorder).</summary>
    public float Strength
    {
        get
        {
            if (t < full) return 1f;
            if (!Active) return 0f;
            float a = Mathf.Clamp(1f - (t - full) / fade, 0f, 1f);
            return a * a;   // front-loaded: most visibility comes back early in the fade
        }
    }

    /// <summary>Which agent's blind screen to draw (White = the generic white-out, for blinds that aren't an agent's flash).</summary>
    public BlindLook Look { get; private set; } = BlindLook.White;
    /// <summary>Where the flash popped on screen (0..1, may lie outside when it popped off-screen); null = screen centre.</summary>
    public Vector2? PopUv { get; private set; }

    public static BlindLook LookOf(FlashAgent a) => a.Agent switch
    {
        "Phoenix" => BlindLook.Phoenix, "Breach" => BlindLook.Breach, "KAY/O" => BlindLook.KayO, "Skye" => BlindLook.Skye,
        "Yoru" => BlindLook.Yoru, "Vyse" => BlindLook.Vyse, "Gekko" => BlindLook.Dizzy, _ => BlindLook.White,
    };

    /// <summary>Palette per look: backdrop at the corners, backdrop overall, ray tint, core tint, max opacity.
    /// Backdrops/cores are the measured averages of VALORANT's blind screens (research/flashes.md §2); ray tints are
    /// saturated versions of the described hues.</summary>
    readonly record struct Palette(Color Edge, Color Mid, Color Ray, Color Core, float Opacity);

    static Palette PaletteOf(BlindLook l) => l switch
    {
        BlindLook.Phoenix => new(new("66443d"), new("7b5246"), new("ff7a4d"), new("ebb486"), 1f),
        BlindLook.Breach => new(new("775048"), new("825c44"), new("ffb547"), new("c89559"), 1f),
        BlindLook.KayO => new(new("425c5e"), new("3f5e5c"), new("55e6dc"), new("bdbc88"), 1f),
        BlindLook.Skye => new(new("3f625b"), new("446c59"), new("80f29a"), new("ced5a8"), 1f),
        BlindLook.Yoru => new(new("4e4354"), new("3e4673"), new("4170ff"), new("8cafdc"), 1f),
        BlindLook.Vyse => new(new("4e5fb0"), new("5055a6"), new("da5cff"), new("e8cb8e"), 1f),
        // White: rays are a dim grey ADDED on a near-white backdrop, so the screen reads bright without clipping flat white.
        _ => new(new("cfcdc6"), new("e4e1da"), new("4a4844"), new("fffdf8"), 0.975f),   // (Dizzy ignores palettes)
    };

    const string Code = @"
shader_type canvas_item;
uniform int mode = 0;             // 0 = flash star-burst, 1 = Dizzy goo
uniform vec3 back_edge = vec3(0.4);
uniform vec3 back_mid = vec3(0.5);
uniform vec3 ray_col = vec3(1.0);
uniform vec3 core_col = vec3(1.0);
uniform float opacity = 1.0;
uniform vec2 pop = vec2(0.5);
uniform float aspect = 1.7778;
uniform float alpha_k = 1.0;      // overall alpha: 1 during the full blind, (1-u)^2 in the fade
uniform float shrink = 0.0;       // 0..1 progress through the full blind (the 5.07 shrink indicator)
uniform float fading = 0.0;       // 0..1 progress through the fade
uniform float tm = 0.0;           // seconds since the blind started (animation)
uniform float seed = 0.0;

float hash11(float p) { p = fract(p * 0.1031); p *= p + 33.33; p *= p + p; return fract(p); }
float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}
float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
	           mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
	float s = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) { s += a * vnoise(p); p = p * 2.03 + vec2(17.1, 9.2); a *= 0.5; }
	return s / 0.9375;
}

// One ring of n star-burst rays: each slot holds one ray with its own width, length, offset and flicker.
float rays(float ang, float d, float n, float rot, float sd, float reach) {
	float a = ang / 6.2831853 * n + rot;
	float idx = mod(floor(a), n);
	float f = fract(a) - 0.5;
	float h1 = hash11(idx * 1.13 + sd);
	float h2 = hash11(idx * 1.71 + sd + 3.1);
	float h3 = hash11(idx * 2.37 + sd + 7.7);
	float w = mix(0.05, 0.15, h1);
	float x = (f - (h3 - 0.5) * 0.3) / w;
	float len = mix(0.3, 1.05, h2) * reach;
	float flick = 0.82 + 0.18 * sin(tm * (4.0 + 7.0 * h1) + h3 * 6.2831853);
	return exp(-x * x) * exp(-d / len) * flick * mix(0.55, 1.0, h2);
}

vec4 flash(vec2 uv) {
	vec2 pc = clamp(pop, vec2(-0.03), vec2(1.03));      // off-screen pops sit on the edge: rays stream in from there
	vec2 p = (uv - pc) * vec2(aspect, 1.0);
	float d = length(p);
	float ang = atan(p.y, p.x);
	float reach = (1.0 - 0.6 * shrink) * (1.0 - 0.35 * fading);
	float rc = length((uv - 0.5) * vec2(aspect, 1.0)) / length(vec2(aspect * 0.5, 0.5));
	vec3 col = mix(back_mid, back_edge, smoothstep(0.15, 1.0, rc));
	col *= 1.0 - 0.22 * shrink * smoothstep(0.15, 0.9, d / max(reach, 0.05));   // periphery dims as the full blind runs out
	col += ray_col * 0.24 * exp(-d / (0.6 * reach));                            // broad tinted glow around the pop
	float r1 = rays(ang, d, 20.0, tm * 0.025, seed, reach);
	float r2 = rays(ang, d, 13.0, 0.37 - tm * 0.018, seed + 11.0, reach * 0.75);
	col += ray_col * (0.7 * r1 + 0.35 * r2);
	float bloom = exp(-d / (0.15 * (1.0 - 0.3 * shrink)));
	col = mix(col, core_col, clamp(bloom, 0.0, 1.0) * 0.95);
	float hot = exp(-(d * d) / (0.03 * 0.03));
	col = mix(col, vec3(1.0), hot);
	float intro = exp(-tm / 0.2);                                                // toned-down onset swell, not a white frame
	col = mix(col, mix(ray_col, vec3(1.0), 0.4), 0.3 * intro * exp(-d * 0.9));
	float a = pow(alpha_k, 1.0 + 1.3 * smoothstep(0.05, 0.9, d));             // in the fade the edges clear first
	return vec4(min(col, vec3(1.0)), a * opacity);
}

vec4 dizzy(vec2 uv) {
	vec2 q = (uv - 0.5) / vec2(0.40, 0.45);              // unit ellipse = 80% of the width, 90% of the height
	float wob = tm * 0.35;
	vec2 w = vec2(fbm(q * 1.3 + vec2(wob, 1.7 + seed)), fbm(q * 1.3 + vec2(5.2 + seed, -wob))) - 0.5;
	vec2 qw = q + w * 0.35;
	qw.y -= 0.05 * smoothstep(0.0, 1.0, q.y) * min(tm, 2.0);   // slow drip downwards
	float r = length(qw);
	vec2 circ = qw / max(r, 1e-4);
	float R = 0.9 + 0.22 * (vnoise(circ * 1.8 + seed) - 0.5);
	R += 0.34 * pow(smoothstep(0.58, 1.0, vnoise(circ * 6.0 + seed + 3.0)), 2.0);     // ragged tendrils
	R += 0.22 * pow(smoothstep(0.62, 1.0, vnoise(circ * 13.0 + seed + 9.0)), 2.0);
	R *= 1.0 - 0.3 * fading;                                  // dissolves inward while fading
	float m = smoothstep(R + 0.02, R - 0.02, r);
	float drops = smoothstep(0.80, 0.85, vnoise(q * 8.0 + seed * 1.3)) * smoothstep(R + 0.45, R + 0.05, r);
	m = max(m, drops * (1.0 - fading));                      // splatter droplets just outside the rim
	vec2 mq = qw * 1.5 + vec2(fbm(qw * 2.0 + wob * 0.6), fbm(qw * 2.0 - wob * 0.5 + 4.0)) * 1.5;
	float h = fbm(mq + seed);
	vec3 blue_a = vec3(0.141, 0.608, 0.831);   // #249BD4
	vec3 blue_b = vec3(0.114, 0.780, 0.996);   // #1DC7FE
	vec3 cyan = vec3(0.149, 0.871, 0.996);     // #26DEFE
	vec3 dark = vec3(0.035, 0.29, 0.57);
	vec3 col = mix(blue_a, blue_b, smoothstep(0.3, 0.7, h));
	float k = fract(h * 9.0 + tm * 0.08);
	col = mix(col, dark, 0.75 * smoothstep(0.13, 0.03, abs(k - 0.5)));   // marbled contour lines
	float rn = r / max(R, 0.1);
	float band = exp(-pow((rn - 0.74) / 0.12, 2.0));
	col = mix(col, cyan, 0.75 * band) + vec3(0.25, 0.35, 0.3) * band;              // glowing cyan inner band
	col = mix(col, dark * 1.15, 0.55 * smoothstep(0.86, 1.0, rn));      // darker goo meniscus at the rim
	return vec4(min(col, vec3(1.0)), m * 0.96 * alpha_k);
}

void fragment() {
	COLOR = mode == 1 ? dizzy(UV) : flash(UV);
}";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        Material = mat;
        DevTestHook();
    }

    /// <summary>Blinds for <paramref name="seconds"/> at full strength, then fades. <paramref name="look"/>: that agent's
    /// screen; <paramref name="popUv"/>: the pop's position on screen (the star-burst's centre).</summary>
    public void Apply(float seconds, BlindLook look, Vector2? popUv)
    {
        if (seconds <= 0 || (Active && seconds <= full - t)) return;   // only a longer blind replaces the current one
        Look = look; PopUv = popUv;
        full = seconds; t = 0; anim = 0; seed = GD.Randf() * 50f;
        // Full flashes fade over 1 s (VALORANT). Brief blinds (the ~0.25 s off-screen minimum) get a shorter fade —
        // 0.25 s → 0.5 s; this scaling is our ESTIMATE (the wiki only gives 1 s). Dizzy: exactly 1 s hold + 1 s fade.
        fade = look == BlindLook.Dizzy ? 1f : Mathf.Clamp(0.25f + seconds, 0.25f, BlindModel.FadeTime);
        QueueRedraw();
    }

    /// <summary>Legacy: the generic white blind, centred.</summary>
    public void Apply(float seconds) => Apply(seconds, BlindLook.White, null);

    public void Reset()
    {
        if (testFreezes.Length > 0) return;   // dev --blind-test with pinned frames: keep the frame for the screenshot
        t = 1e9f; QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        anim += dt;
        if (testFreeze >= 0f && t < 1e8f) t = testFreeze; else t += dt;
        DevTestTick(dt);
        bool active = Active;
        if (active || wasActive) QueueRedraw();
        wasActive = active;
    }

    public override void _Draw()
    {
        if (!Active || mat == null) return;
        var size = GetSize();
        if (size.X < 1f || size.Y < 1f) size = GetViewportRect().Size;
        var pal = PaletteOf(Look);
        float shrink = Mathf.Clamp(t / Mathf.Max(0.01f, full), 0f, 1f);
        float fading = t < full ? 0f : Mathf.Clamp((t - full) / fade, 0f, 1f);
        Vector2 pop = PopUv ?? new Vector2(0.5f, 0.5f);
        if (!float.IsFinite(pop.X) || !float.IsFinite(pop.Y)) pop = new Vector2(0.5f, 0.5f);
        mat.SetShaderParameter("mode", Look == BlindLook.Dizzy ? 1 : 0);
        mat.SetShaderParameter("back_edge", new Vector3(pal.Edge.R, pal.Edge.G, pal.Edge.B));
        mat.SetShaderParameter("back_mid", new Vector3(pal.Mid.R, pal.Mid.G, pal.Mid.B));
        mat.SetShaderParameter("ray_col", new Vector3(pal.Ray.R, pal.Ray.G, pal.Ray.B));
        mat.SetShaderParameter("core_col", new Vector3(pal.Core.R, pal.Core.G, pal.Core.B));
        mat.SetShaderParameter("opacity", pal.Opacity);
        mat.SetShaderParameter("pop", pop);
        mat.SetShaderParameter("aspect", size.X / size.Y);
        mat.SetShaderParameter("alpha_k", Strength);
        mat.SetShaderParameter("shrink", shrink);
        mat.SetShaderParameter("fading", fading);
        mat.SetShaderParameter("tm", anim);
        mat.SetShaderParameter("seed", seed);
        DrawRect(new Rect2(Vector2.Zero, size), Colors.White);
    }

    // ---- Dev-only test hook: "--dev --blind-test <look>[,u,v[,t]]" shows that blind 1 s after the overlay appears and
    // every 4 s after that (2.25 s full + fade), popping at screen UV (u,v). The optional t pins the blind's clock at t
    // seconds (e.g. 0.3 = early full blind, 2.0 = late full blind, 2.6 = mid-fade) so screenshots are exact; several
    // "t1/t2/..." cycle every 3 s of wall clock. ----
    float testNext = -1f, testFreeze = -1f;
    float[] testFreezes = System.Array.Empty<float>();
    int testIdx;
    BlindLook testLook;
    Vector2? testPop;
    static bool testClaimed;   // only the first overlay (the real session, not a drill preview) runs the hook

    void DevTestHook()
    {
        string? arg = CmdLine.DevAfter("--blind-test");
        if (arg == null || testClaimed) return;
        testClaimed = true;
        var parts = arg.Split(',');
        if (!System.Enum.TryParse(parts[0], true, out testLook)) { GD.PushWarning($"--blind-test: unknown look '{parts[0]}'"); testLook = BlindLook.White; }
        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        if (parts.Length >= 3) testPop = new Vector2(F(parts[1]), F(parts[2]));
        if (parts.Length >= 4) testFreezes = System.Array.ConvertAll(parts[3].Split('/'), F);
        testNext = 1f;
    }

    void DevTestTick(float dt)
    {
        if (testNext < 0f) return;
        if (testFreezes.Length > 0)
        {
            // Frozen frames switch on the wall clock (every 3 s since engine start: index = (s / 3) mod n), so
            // screenshot times line up even when the game runs slowly.
            int phase = (int)(Time.GetTicksMsec() / 3000);
            if (phase == testIdx) return;
            testIdx = phase;
            t = 1e9f;
            Apply(testLook == BlindLook.Dizzy ? 1f : 2.25f, testLook, testPop);
            testFreeze = testFreezes[phase % testFreezes.Length]; t = testFreeze;
            return;
        }
        testNext -= dt;
        if (testNext > 0f) return;
        testNext = 4f;
        t = 1e9f;   // restart even if the previous one is still showing
        Apply(testLook == BlindLook.Dizzy ? 1f : 2.25f, testLook, testPop);
    }
}
