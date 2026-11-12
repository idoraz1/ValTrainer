// Shared body of viewmodel.gdshader (factory finishes) and viewmodel_skin(_lite).gdshader (gun skins, GunSkins.cs).
// - Re-projects the mesh with its own vertical FOV (vm_focal = 1/tan(fov/2)) so the gun keeps its size
//   when the world camera zooms, and squeezes its depth into the first 1% next to the near plane, so it is
//   always drawn in front of the world and never clips into walls.
// - Lighting stays physically placed: VERTEX/NORMAL are the real view-space values, so the scene's
//   directional light, sky ambient, reflections and omni lights (muzzle flash) shade it normally.
// SKIN: one procedural pattern (skin_pattern = SkinPattern) in GUN space, in metres (skin_xform = mesh -> gun), because
// the models have no usable UVs. 3D patterns use solid noise, 2D ones a dominant-axis box projection; hard edges are
// fwidth-filtered and fine detail fades to its average colour once a cell nears a pixel (no MSAA/TAA on the 3D view).
// The simple patterns blend up to three finishes (col_*/mr_*); the skins-3 patterns (17-26, skins3-contracts.md) compute
// their colour and PBR numbers directly ("direct") because they need more than three (jewel glass, molten cracks).
// SKIN_LITE (Low quality): one noise octave, no fine secondary detail, no TIME animation, less metal (Low has no sky
// reflections).
// Level effects are uniforms pushed by the viewmodel (VmMaterials.SetSkinDynamics): fire_t / kill_t (seconds since the
// last shot / player kill) x react / kill_glow gains, kill_slide (kills so far, eased: the Orrery dial), vm_ads (reactive
// glow and the counter fade out when aiming), and the Apex kill counter (counter_*: two 7-segment glyphs on a small plate on
// the camera-facing receiver side). All of it adds exactly nothing at the defaults, and the pulses only light where the
// pattern already glows, off the top 15 % of the gun (the sight picture). SKIN_LITE keeps the pulses but drops every TIME
// animation.

uniform vec4 albedo : source_color = vec4(0.2, 0.2, 0.2, 1.0);
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.6;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
uniform float detail : hint_range(0.0, 1.0) = 0.0;      // procedural micro-variation (wear / grain)
uniform float detail_scale = 90.0;
uniform float rim : hint_range(0.0, 1.0) = 0.0;         // subtle back-light rim for readability
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_energy = 0.0;
uniform float vm_focal = 1.428;          // 1 / tan(viewmodel_vfov / 2)
uniform float vm_depth = 0.01;           // fraction of the depth range the viewmodel may use
uniform float flash = 0.0;               // muzzle-flash light boost (0..1), adds warm fill to the front
uniform vec3 clip_min = vec3(0.0);       // object-space box hidden while clip_on (magazine out during reload)
uniform vec3 clip_max = vec3(0.0);
uniform float clip_on = 0.0;
#ifdef TEXTURED
// TEXTURED (viewmodel_tex.gdshader): a modelled skin (Blender .glb with UVs). The base colour and the emissive mask come
// from the model's textures; the emissive parts glow, flow along the gun when animated (anim > 0, Apex level 2) and pulse
// on shots / kills through the same dynamics uniforms as the skin shader (VmMaterials.SetSkinDynamics).
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D emission_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float tex_glow = 2.5;            // emissive map gain
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D mr_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_disable; // glTF: G roughness, B metallic
uniform float use_normal = 0.0;
// Variants of a modelled skin (Viewmodel.ModelVariant): colours of the model's own textures picked by hue and moved to a
// variant colour, keeping their light and shade. from = (hue 0..1, half width, min saturation, 1 = keep the peak brightness
// (glows) else the luminance); to = (rgb, amount).
// A half width >= 0.5 takes every colour (a whole-surface tint).
uniform vec4 alb_from_a = vec4(0.0); uniform vec4 alb_to_a = vec4(0.0);
uniform vec4 alb_from_b = vec4(0.0); uniform vec4 alb_to_b = vec4(0.0);
uniform vec4 em_from_a = vec4(0.0);  uniform vec4 em_to_a = vec4(0.0);
uniform vec4 em_from_b = vec4(0.0);  uniform vec4 em_to_b = vec4(0.0);
uniform float use_mr = 0.0;
uniform float normal_depth = 1.0;
uniform float anim = 0.0;                // > 0: molten flow along the gun (level 2)
uniform float react = 0.0;
uniform float kill_glow = 0.0;
uniform float fire_t = 9.0;
uniform float kill_t = 9.0;
uniform float vm_ads = 0.0;
#endif

varying vec3 obj_pos;

#ifdef SKIN
uniform int skin_pattern = 1;            // SkinPattern (Solid never gets here)
uniform vec4 col_a : source_color = vec4(0.2, 0.2, 0.2, 1.0);
uniform vec4 col_b : source_color = vec4(0.1, 0.1, 0.1, 1.0);
uniform vec4 col_c : source_color = vec4(0.05, 0.05, 0.05, 1.0);
uniform vec4 col_d : source_color = vec4(0.05, 0.05, 0.05, 1.0); // the fourth colour (Rally's black)
uniform vec3 mr_a = vec3(0.0, 0.6, 0.5); // metallic, roughness, specular of each finish
uniform vec3 mr_b = vec3(0.0, 0.6, 0.5);
uniform vec3 mr_c = vec3(0.0, 0.6, 0.5);
uniform float skin_scale = 0.05;         // pattern cell, metres in gun space
uniform float skin_mid = 0.45;           // Split: border as a fraction of the length from the muzzle
uniform mat4 skin_xform = mat4(1.0);     // mesh-local -> gun space (metres), set per surface by Viewmodel
uniform vec3 gun_min = vec3(-0.1, -0.1, -0.6); // gun-space bounds (muzzle at min z) for gradients and the glow mask
uniform vec3 gun_max = vec3(0.1, 0.1, 0.3);
uniform float skin_seed = 0.0;           // per gun, so two guns' camo isn't identical
uniform vec4 glow_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float glow_energy = 0.0;
uniform float anim = 0.0;                // > 0 turns on a pattern's level-2 motion (its speed)
uniform int skin_sub = 0;                // variation inside a pattern (GunSkin.Sub; no skins-3 pattern uses one yet)
uniform float react = 0.0;               // fire-reactive glow gain (GunSkin.React)
uniform float kill_glow = 0.0;           // gun-side kill pulse gain (GunSkin.KillGlow)
uniform float fire_t = 9.0;              // seconds since the last shot (pushed only for reactive skins)
uniform float kill_t = 9.0;              // seconds since the last player kill
uniform float kill_slide = 0.0;          // kills so far, eased: each kill adds 1 over 0.4 s
uniform float vm_ads = 0.0;              // eased ADS weight
uniform float counter_on = 0.0;          // kill counter (GunSkin.Counter)
uniform vec4 counter_rect = vec4(0.0);   // gun-space display: z0, y0, z1, y1 (metres)
uniform float counter_side = -1.0;       // the camera-facing side: -1 = -x (right-handed), +1 = +x (left-handed)
uniform vec3 counter_val = vec3(0.0);    // 7-segment masks of the two glyphs (bits a..g), and 1 = the "+" (over 99)
uniform float counter_flash = 0.0;       // brightness kick on an increment (0..1)
uniform vec2 recv = vec2(-0.06, 0.08);   // receiver side anchor (gun space z, y): the dials, Rally's roundel, the counter

varying vec3 gun_pos;
varying vec3 gun_nrm;
#endif

#ifdef TEXTURED
vec3 rgb2hsv_vm(vec3 c) {
	vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
	vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
	vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
	float d = q.x - min(q.w, q.y);
	return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-10)), d / (q.x + 1e-10), q.x);
}
// moves the colours of c near from.x (hue) to to.rgb, keeping c's brightness
vec3 remap_hue(vec3 c, vec4 from, vec4 to) {
	if (to.a <= 0.0) return c;
	vec3 h = rgb2hsv_vm(c);
	float dh = abs(h.x - from.x); dh = min(dh, 1.0 - dh);
	float w = from.y >= 0.5 ? 1.0 : 1.0 - smoothstep(from.y * 0.6, from.y, dh);
	w *= smoothstep(from.z * 0.6, from.z + 1e-4, h.y);
	// from.w = 1: keep the peak brightness (glows: an orange crack becomes an equally bright white one); else the luminance
	float l = from.w > 0.5 ? max(c.r, max(c.g, c.b)) : dot(c, vec3(0.299, 0.587, 0.114));
	float lt = max(from.w > 0.5 ? max(to.r, max(to.g, to.b)) : dot(to.rgb, vec3(0.299, 0.587, 0.114)), 0.04);
	vec3 moved = clamp(to.rgb * (l / lt), 0.0, 1.0);
	return mix(c, moved, w * to.a);
}
#endif

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float vnoise(vec3 p) {
	vec3 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash13(i), hash13(i + vec3(1, 0, 0)), f.x), mix(hash13(i + vec3(0, 1, 0)), hash13(i + vec3(1, 1, 0)), f.x), f.y),
	           mix(mix(hash13(i + vec3(0, 0, 1)), hash13(i + vec3(1, 0, 1)), f.x), mix(hash13(i + vec3(0, 1, 1)), hash13(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

#ifdef SKIN
// Two-octave value noise (one on SKIN_LITE).
float fbm2(vec3 p) {
#ifdef SKIN_LITE
	return vnoise(p);
#else
	return vnoise(p) * 0.65 + vnoise(p * 2.13 + 17.7) * 0.35;
#endif
}

// Anti-aliased step: 0 below e, 1 above, blurred over w (the value's change per pixel).
float aa_step(float e, float v, float w) {
	return smoothstep(e - w, e + w, v);
}

// Weights of the three finishes from two thresholds over v: A below t1, B between, C above t2.
vec3 bands3(float v, float t1, float t2, float w) {
	float b = aa_step(t1, v, w), c = aa_step(t2, v, w);
	return vec3(1.0 - b, b - c, c);
}

// Coverage of a line of half width hw at distance d (metres), never thinner than half a pixel (fainter instead), fading to
// its average coverage once the line spacing per nears a pixel (px = metres per pixel).
float thin_line(float d, float hw, float per, float px) {
	float h2 = max(hw, px * 0.5);
	float l = (1.0 - smoothstep(h2 - px * 0.5, h2 + px * 0.5, d)) * (hw / h2);
	return mix(l, min(2.0 * hw / per, 1.0), smoothstep(0.3, 0.7, px / per));
}

// Distance from q to the segment a-b.
float seg_dist(vec2 q, vec2 a, vec2 b) {
	vec2 pa = q - a, ba = b - a;
	float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-12), 0.0, 1.0);
	return length(pa - ba * h);
}

// Nearest cell of a hexagon grid (q in cell units): xy = q minus the cell's centre, zw = the centre.
vec4 hex_cell(vec2 q) {
	const vec2 r = vec2(1.0, 1.7320508);
	vec2 h = r * 0.5;
	vec2 a = mod(q, r) - h;
	vec2 b = mod(q - h, r) - h;
	vec2 g = dot(a, a) < dot(b, b) ? a : b;
	return vec4(g, q - g);
}

// Distance from a hex_cell offset to the hexagon's edge in cell units (0 on the edge, about 0.5 in the middle).
float hex_dist(vec2 g) {
	vec2 q = abs(g);
	return 0.5 - max(dot(q, vec2(0.5, 0.8660254)), q.x);
}

// Voronoi of one jittered point per grid cell (q in cell units, k = metres per cell unit on each axis): f1 = metres to the
// nearest point, edge = metres to the border with the second nearest (exact for that pair), id = a hash of the nearest
// cell, rel = metres from the nearest point to here.
void cells2(vec2 q, vec2 k, float sd, out float f1, out float edge, out float id, out vec2 rel) {
	vec2 c = floor(q), f = fract(q);
	float d1 = 1e9, d2 = 1e9;
	vec2 r1 = vec2(0.0), r2 = vec2(1.0), c1 = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 o = vec2(float(i), float(j));
			vec2 pt = o + 0.15 + 0.7 * vec2(hash13(vec3(c + o, sd)), hash13(vec3(c + o, sd + 7.31)));
			vec2 rv = (pt - f) * k;
			float dd = dot(rv, rv);
			if (dd < d1) { d2 = d1; r2 = r1; d1 = dd; r1 = rv; c1 = c + o; }
			else if (dd < d2) { d2 = dd; r2 = rv; }
		}
	}
	f1 = sqrt(d1);
	vec2 dr = r2 - r1;
	edge = dot(0.5 * (r1 + r2), dr / max(length(dr), 1e-7));
	id = hash13(vec3(c1, sd + 3.7));
	rel = -r1;
}

// Glasswork's seven jewel tones (linear), picked by h (0..1).
vec3 jewel(float h) {
	float i = floor(fract(h) * 7.0);
	return i < 1.0 ? vec3(0.78, 0.015, 0.03) : (i < 2.0 ? vec3(1.0, 0.22, 0.01) : (i < 3.0 ? vec3(1.0, 0.62, 0.02)
		: (i < 4.0 ? vec3(0.02, 0.55, 0.09) : (i < 5.0 ? vec3(0.01, 0.42, 0.62) : (i < 6.0 ? vec3(0.03, 0.09, 0.85) : vec3(0.34, 0.03, 0.82))))));
}

// Oscilloscope traces, y in -1..1 at x cycles: 0 sine, 1 square, 2 heartbeat pulse.
float scope_wave(float x, float type) {
	if (type < 0.5) return sin(x * 6.28318);
	if (type < 1.5) return clamp(sin(x * 6.28318) * 7.0, -1.0, 1.0) * 0.8;
	float f = fract(x) - 0.5;
	float g = f - 0.07, h = f + 0.24;
	return 1.15 * exp(-f * f / 0.0007) - 0.45 * exp(-g * g / 0.0010) + 0.22 * exp(-h * h / 0.004) - 0.15;
}

// A round gauge at c (metres in the side face's z, y), radius rr: (face, bezel, ticks, hand) coverage. The hand points at
// angle a (radians, 0 = toward +z); dir mirrors z on the +x side so the dial reads the same from either side.
vec4 gauge(vec2 q, vec2 c, float rr, float a, float px) {
	vec2 d = q - c;
	float dr = length(d);
	float face = 1.0 - aa_step(rr, dr, px);
	float bez = (1.0 - aa_step(rr * 1.22, dr, px)) - face;
	float ang = atan(d.y, d.x);
	float tk = abs(fract(ang / 6.28318 * 12.0 + 0.5) - 0.5) / 12.0 * 6.28318 * dr;
	float ticks = (1.0 - aa_step(rr * 0.045, tk, px)) * aa_step(rr * 0.72, dr, px) * face;
	float hand = (1.0 - aa_step(rr * 0.07, seg_dist(d, vec2(0.0), vec2(cos(a), sin(a)) * rr * 0.82), px)) * face;
	hand = max(hand, (1.0 - aa_step(rr * 0.14, dr, px)));
	return vec4(face, bez, ticks, hand);
}

// Signed distance to a box (centre c, half size h).
float seg_box(vec2 p, vec2 c, vec2 h) {
	vec2 d = abs(p - c) - h;
	return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

// A 7-segment glyph at q (0..1 square, x right, y up), segment bits a..g = 0..6 of m: (lit coverage, all segments).
vec2 seven_seg(vec2 q, int m, float w) {
	q.x -= (q.y - 0.5) * 0.12;                    // a slight slant, like an instrument display
	float lit = 0.0, all_s = 0.0;
	for (int i = 0; i < 7; i++) {
		vec2 c = vec2(0.5, 0.5), h = vec2(0.27, 0.07);
		if (i == 0) c = vec2(0.5, 0.92);
		else if (i == 1) { c = vec2(0.85, 0.71); h = vec2(0.07, 0.15); }
		else if (i == 2) { c = vec2(0.85, 0.29); h = vec2(0.07, 0.15); }
		else if (i == 3) c = vec2(0.5, 0.08);
		else if (i == 4) { c = vec2(0.15, 0.29); h = vec2(0.07, 0.15); }
		else if (i == 5) { c = vec2(0.15, 0.71); h = vec2(0.07, 0.15); }
		float cov = 1.0 - smoothstep(-w, w, seg_box(q, c, h) - 0.01);
		all_s = max(all_s, cov);
		if (((m >> i) & 1) == 1) lit = max(lit, cov);
	}
	return vec2(lit, all_s);
}
#endif

void vertex() {
	obj_pos = VERTEX;
#ifdef SKIN
	gun_pos = (skin_xform * vec4(VERTEX, 1.0)).xyz;
	gun_nrm = normalize(mat3(skin_xform) * NORMAL);
#endif
	vec4 vp = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	mat4 p = PROJECTION_MATRIX;
	// Scale the focal terms (keeps the aspect, the API's axis signs and any jitter/offset terms).
	float k = vm_focal / abs(p[1][1]);
	p[0][0] *= k;
	p[1][1] *= k;
	vec4 clip = p * vp;
	// Which end of the depth range is "near"? (reverse-Z in Forward+: near = 1)
	float z1 = PROJECTION_MATRIX[2][2] * -1.0 + PROJECTION_MATRIX[3][2];
	float z2 = (PROJECTION_MATRIX[2][2] * -2.0 + PROJECTION_MATRIX[3][2]) * 0.5;
	float near_ndc = z1 > z2 ? 1.0 : 0.0;
	clip.z = mix(near_ndc * clip.w, clip.z, vm_depth);
	POSITION = clip;
}

void fragment() {
	if (clip_on > 0.5 && all(greaterThan(obj_pos, clip_min)) && all(lessThan(obj_pos, clip_max))) {
		discard;
	}
#ifndef SKIN
	vec3 col = albedo.rgb;
	float r = roughness;
	float m = metallic;
	float sp = specular;
	vec3 glow = vec3(0.0);
	vec3 wear_p = obj_pos;
#ifdef TEXTURED
	col = texture(albedo_tex, UV).rgb * albedo.rgb;
	col = remap_hue(remap_hue(col, alb_from_a, alb_to_a), alb_from_b, alb_to_b);
	if (use_mr > 0.5) {
		vec4 mrs = texture(mr_tex, UV);
		r = clamp(mrs.g * roughness * 2.0, 0.04, 1.0);   // roughness uniform 0.5 = the map as is
		m = clamp(mrs.b * metallic * 2.0, 0.0, 1.0);
	}
	if (use_normal > 0.5) {
		NORMAL_MAP = texture(normal_tex, UV).rgb;
		NORMAL_MAP_DEPTH = normal_depth;
	}
	vec3 em = texture(emission_tex, UV).rgb;
	em = remap_hue(remap_hue(em, em_from_a, em_to_a), em_from_b, em_to_b);
	float flow = 1.0;
	if (anim > 0.0) {
		flow = 0.65 + 0.5 * (0.5 + 0.5 * sin(TIME * 2.4 * anim - obj_pos.z * 16.0)) + 0.25 * (vnoise(obj_pos * 22.0 + vec3(0.0, 0.0, TIME * 0.7)) - 0.5);
	}
	float kk = max(1.0 - kill_t / 0.35, 0.0);
	flow += (react * exp(-fire_t / 0.06) * 1.5 + kill_glow * kk * kk * 2.5) * (1.0 - vm_ads * 0.7);
	glow = em * tex_glow * flow;
	col *= 1.0 - clamp(dot(em, vec3(0.333)) * 1.5, 0.0, 0.6); // the cracks emit; they don't also reflect
#endif
#else
	// ---- skin pattern: weights of finishes A/B/C, or a direct colour (skins-3 patterns), in gun space ----
	vec3 p = gun_pos;
	vec3 sp3 = p + vec3(skin_seed * 3.7, skin_seed * 1.9, skin_seed * 5.3);
	float s = max(skin_scale, 0.001);
	vec3 an = abs(gun_nrm);
	vec2 uv = an.x > an.y && an.x > an.z ? p.zy : (an.y > an.z ? p.xz : p.xy);
	float px = length(fwidth(p));                   // metres per pixel here
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float tz = clamp((p.z - gun_min.z) / max(gun_max.z - gun_min.z, 0.01), 0.0, 1.0); // 0 muzzle .. 1 stock
	vec3 wt = vec3(1.0, 0.0, 0.0);
	float shade = 1.0;                              // albedo multiplier (thread / streak shading)
	float rough_add = 0.0;
	bool direct = false;                            // the pattern set col_dir / mr_dir itself
	vec3 col_dir = vec3(0.0);
	vec3 mr_dir = mr_a;
	vec3 glow = vec3(0.0);
	float ty = (p.y - gun_min.y) / max(gun_max.y - gun_min.y, 0.01); // 0 bottom .. 1 top
	float top_mask = 1.0 - smoothstep(0.78, 0.85, ty);              // no animated / reactive glow in the sight picture
	float tmk = mix(0.45, 1.0, top_mask);                            // the static glow: dimmer there, never gone
	float hipk = 1.0 - vm_ads;
	bool side = an.x > an.y && an.x > an.z;
	float zdir = gun_nrm.x < 0.0 ? 1.0 : -1.0;                       // side-face text and dials read the same from both sides
	float fire_env = react * exp(-fire_t / 0.06);                    // a shot: 0.12 s
	float kill_k = max(1.0 - kill_t / 0.35, 0.0);
	float kill_env = kill_glow * kill_k * kill_k;                    // a kill: 0.35 s
	float fx_env = (fire_env + kill_env) * top_mask * hipk;
#ifdef SKIN_LITE
	float tt = 0.0;                                                  // no TIME animation on Low
#else
	float tt = TIME;
#endif
	if (skin_pattern == 1) {
		// Split: A behind the border, B in front, a thin C seam on it
		float d = p.z - mix(gun_min.z, gun_max.z, skin_mid);
		float w = fwidth(p.z) + 1e-5;
		float seam = 1.0 - aa_step(0.002, abs(d), w);
		float front = 1.0 - aa_step(0.0, d, w);
		wt = vec3((1.0 - front) * (1.0 - seam), front * (1.0 - seam), seam);
	} else if (skin_pattern == 2) {
		// Carbon: 2x2 twill of box-projected threads; fades to the average weave once a cell nears a pixel
		vec2 q = uv / s;
		vec2 c = floor(q), f = fract(q);
		float tw = step(2.0, mod(c.x + c.y, 4.0));
		float th = sin(3.14159 * (tw > 0.5 ? f.y : f.x));
		float fade = smoothstep(0.25, 0.7, px / s);
		wt = vec3(mix(1.0 - tw, 0.5, fade), mix(tw, 0.5, fade), 0.0);
		shade = mix(0.6 + 0.4 * th, 0.85, fade);
		rough_add = mix(0.12 * (1.0 - th), 0.06, fade);
	} else if (skin_pattern == 3) {
		// Blot: solid 3D noise -> three colours
		float n = fbm2(sp3 / s);
		wt = bands3(n, 0.44, 0.58, fwidth(n) + 1e-4);
	} else if (skin_pattern == 7) {
		// Brushed: fine streaks along z (roughness and albedo), coarse anodising variation (A/B)
		float st = vnoise(vec3(p.x / 0.0012, p.y / 0.0012, p.z / 0.05));
		float fade = smoothstep(0.4, 1.0, px / 0.0012);
		st = mix(st, 0.5, fade);
		float n = vnoise(sp3 / s);
		wt = vec3(1.0 - n, n, 0.0);
		shade = 0.8 + 0.4 * st;
		rough_add = (st - 0.5) * 0.25;
	} else if (skin_pattern == 8) {
		// Damascus: wavy folded-steel layers
		float n = fbm2(sp3 / s);
		float period = s * 0.14;
		float yy = p.y + p.z * 0.1 + n * s * 0.8;
		float v = 0.5 + 0.5 * sin(yy * 6.28318 / period);
		float fade = smoothstep(0.3, 0.6, px / period);
		v = mix(v, 0.5, fade);
		float b = smoothstep(0.15, 0.85, v);
		wt = vec3(1.0 - b, b, 0.0);
	} else if (skin_pattern == 17) {
		// Glasswork: stained-glass panes in jewel tones, lit from behind (emission), set in bright lead came (A). Panes are
		// Voronoi cells, taller than wide; the hue runs through the rainbow along the gun with a random step per pane, and
		// each pane has a refraction gradient and a fine ripple. Glass is glossy (B's roughness / specular).
		vec2 k = vec2(s, s * 1.8);
		float f1, edge, id;
		vec2 rel;
		cells2(uv / k, k, 3.1 + skin_seed, f1, edge, id, rel);
		float came = 1.0 - aa_step(s * 0.06, edge, px);
		float fade = smoothstep(0.12, 0.35, px / s);
		came = mix(came, 0.2, fade);
		vec3 jw = jewel(tz * 0.85 + id * 0.42 + skin_seed * 0.05);
		float grad = clamp(0.5 + dot(rel / k, vec2(0.6, 0.8)) * 0.9, 0.0, 1.0);
		float rip = vnoise(vec3(uv / (s * 0.22), id * 13.0));
		float vig = smoothstep(0.0, s * 0.35, edge);                  // each pane darker at its lead
		col_dir = mix(jw * (0.08 + 0.22 * grad) * (0.5 + 0.5 * vig), col_a.rgb, came);
		mr_dir = mix(vec3(0.0, mr_b.y + 0.08 * rip, mr_b.z), mr_a, came);
		direct = true;
		float lum = (0.15 + 1.1 * grad * grad + 0.35 * rip) * (0.35 + 0.65 * vig);
		glow = jw * lum * (1.0 - came) * (glow_energy * tmk + fx_env);
	} else if (skin_pattern == 18) {
		// Thunderhead: billowing storm clouds (A storm blue -> B silver-white tops, C the deepest shadows) with blue-white
		// lightning veins (emission): the ridge of a 2D noise, warped by the clouds and a fine jitter, in sparse patches.
		float cn = fbm2(sp3 / s);
		float hi = smoothstep(0.5, 0.78, cn);
		float lo = 1.0 - smoothstep(0.15, 0.4, cn);
		col_dir = mix(mix(col_a.rgb, col_b.rgb, hi), col_c.rgb, lo * 0.45);
		mr_dir = mix(mr_a, mr_b, hi);
		vec2 lq = uv / (s * 1.6);
		float ln = vnoise(vec3(lq, 5.0 + skin_seed)) + (cn - 0.5) * 0.4;
#ifndef SKIN_LITE
		ln += (vnoise(vec3(uv / (s * 0.1), 9.1)) - 0.5) * 0.06;
#endif
		float ld = abs(ln - 0.5);
		float lw = fwidth(ln) + 1e-4;
		float patchm = smoothstep(0.38, 0.55, vnoise(vec3(lq * 0.5, 2.2 + skin_seed)));
		float core = (1.0 - aa_step(0.007, ld, lw)) * patchm;
		float halo = (1.0 - smoothstep(0.0, 0.06, ld)) * patchm;
		col_dir = mix(col_dir, vec3(0.8, 0.9, 1.0), core);
		mr_dir = mix(mr_dir, vec3(0.0, 0.3, 0.5), core);
		direct = true;
		vec3 gc = glow_color.rgb;
		glow = (mix(gc, vec3(1.0), 0.55) * core * 2.4 + gc * halo * halo * 0.6) * (glow_energy * tmk + fx_env);
	} else if (skin_pattern == 19) {
		// Basalt: charcoal hex columns (A/B per column, a bevel at the rim, rock grain) split by thin magma seams (C) that
		// glow a dim red (emission: dimmer and redder than Foundry), hotter in places.
		vec2 q = uv / s;
		q += (vec2(vnoise(vec3(q * 0.4, 1.3 + skin_seed)), vnoise(vec3(q * 0.4, 4.7 + skin_seed))) - 0.5) * 0.3;
		vec4 hc = hex_cell(q);
		float he = hex_dist(hc.xy);
		float hw = fwidth(he) + 1e-4;
		float cid = hash13(vec3(floor(hc.zw * 2.0 + 0.5), 2.9 + skin_seed));
		float seam = 1.0 - aa_step(0.04, he, hw);
		float fade = smoothstep(0.2, 0.5, hw / 0.08);
		seam = mix(seam, 0.12, fade);
		float bevel = smoothstep(0.04, 0.17, he);
		float grain = vnoise(sp3 / 0.0022);
		vec3 stone = mix(col_a.rgb, col_b.rgb, cid) * (0.5 + 0.5 * bevel) * (0.8 + 0.4 * grain);
		col_dir = mix(stone, col_c.rgb, seam);
		mr_dir = mix(mr_a + vec3(0.0, (grain - 0.5) * 0.25, 0.0), mr_c, seam);
		direct = true;
		float hot = 0.45 + 0.75 * vnoise(vec3(q * 0.6, 8.8 + skin_seed));
		float rimh = exp(-max(he - 0.04, 0.0) / 0.035) * 0.22 * (1.0 - fade);   // the columns' rims are warm
		glow = glow_color.rgb * (seam + rimh) * hot * (glow_energy * tmk + fx_env);
	} else if (skin_pattern == 20) {
		// Oscilloscope: gunmetal (A) framing phosphor screens (B, dark glass) on the side faces: a faint graticule and a
		// glowing trace (a sine at the front, a heartbeat pulse at the receiver, a square wave at the stock), and an
		// analogue dial (C face) on the receiver. Top faces stay plain gunmetal (the sight picture).
		float mach = vnoise(vec3(p.x / 0.0015, p.y / 0.0015, p.z / 0.03));
		col_dir = col_a.rgb * (0.85 + 0.3 * mach);
		mr_dir = mr_a + vec3(0.0, (mach - 0.5) * 0.15, 0.0);
		direct = true;
		if (side) {
			float H = 0.042, L = 0.11;
			vec2 bq = vec2((p.z - gun_min.z) / L, (p.y - gun_min.y) / H);
			vec2 cell = floor(bq), lc = fract(bq);
			vec2 dm = min(lc, 1.0 - lc) * vec2(L, H);                  // metres to the screen's edges
			float scr = aa_step(0.0035, min(dm.x, dm.y), px);
			vec2 gq = abs(fract(vec2(p.z - gun_min.z, p.y - gun_min.y) / 0.0084 + 0.5) - 0.5) * 0.0084;
			float grid = thin_line(min(gq.x, gq.y), 0.00018, 0.0084, px);
			float type = tz < 0.4 ? 0.0 : (tz < 0.68 ? 2.0 : 1.0);
			float cyc = type > 1.5 ? 1.0 / 0.075 : (type > 0.5 ? 1.0 / 0.045 : 1.0 / 0.04);   // cycles per metre
			float x0 = p.z * cyc + hash13(vec3(cell, 1.1)) + (anim > 0.0 ? tt * anim * 2.0 : 0.0);
			float A = H * 0.3, dl = 0.0007;
			vec2 qq = vec2(0.0, (lc.y - 0.5) * H);
			vec2 P0 = vec2(-dl, scope_wave(x0 - dl * cyc, type) * A);
			vec2 P1 = vec2(0.0, scope_wave(x0, type) * A);
			vec2 P2 = vec2(dl, scope_wave(x0 + dl * cyc, type) * A);
			float td = min(seg_dist(qq, P0, P1), seg_dist(qq, P1, P2));
			float trace = (1.0 - aa_step(0.00055, td, px)) * scr;
			float tglow = exp(-td / 0.0016) * scr;
			col_dir = mix(col_dir, col_b.rgb + glow_color.rgb * 0.02, scr);
			mr_dir = mix(mr_dir, mr_b, scr);
			col_dir = mix(col_dir, glow_color.rgb * 0.5, trace);
			glow = glow_color.rgb * (grid * 0.22 * scr + trace * 2.2 + tglow * 0.35) * (glow_energy * tmk + fx_env);
			// the dial, on the receiver below the counter's spot
			vec2 dc = recv + vec2(-0.004, -0.004);
			vec2 dq = vec2(dc.x + (p.z - dc.x) * zdir, p.y);
			vec4 gg = gauge(dq, dc, 0.0105, 2.2 - fire_env * 1.2, px);
			float dial = gg.x + gg.y;
			col_dir = mix(col_dir, mix(col_c.rgb, vec3(0.03), max(gg.z, gg.w)), gg.x);
			col_dir = mix(col_dir, col_a.rgb * 1.3, gg.y);
			mr_dir = mix(mr_dir, mix(vec3(0.0, 0.3, 0.6), vec3(0.9, 0.25, 0.5), gg.y / max(dial, 1e-4)), dial);
			glow *= 1.0 - dial;
			glow += col_c.rgb * gg.x * (1.0 - max(gg.z, gg.w)) * 0.12 * glow_energy;   // a backlit dial face
		}
	} else if (skin_pattern == 21) {
		// Rally: motorsport livery. Glossy white (A) with diagonal racing stripe groups (red B, blue C, then a chequered
		// strip) repeating along the gun, a number-7 roundel on the receiver and carbon twill low down (magazine, grip); all
		// black parts D.
		float u = p.z * 0.82 + p.y * 0.57;
		float vv = side ? p.y * 0.82 - p.z * 0.57 : p.x;
		float P = 0.25;
		float dd = fract((u - gun_min.z * 0.82 + 0.04) / P) * P;     // metres into this stripe group
		float red = aa_step(0.0, dd, px) * (1.0 - aa_step(0.026, dd, px));
		float blue = aa_step(0.031, dd, px) * (1.0 - aa_step(0.047, dd, px));
		float band = aa_step(0.053, dd, px) * (1.0 - aa_step(0.0718, dd, px));
		col_dir = mix(mix(col_a.rgb, col_b.rgb, red), col_c.rgb, blue);
		mr_dir = mix(mr_a, mr_b, max(red, blue));
		float chk = mod(floor((dd - 0.053) / 0.0063) + floor(vv / 0.0063), 2.0);
		chk = mix(chk, 0.5, smoothstep(0.3, 0.6, px / 0.0063));
		col_dir = mix(col_dir, mix(col_a.rgb, col_d.rgb, chk), band);
		// carbon twill low down
		float carb = 1.0 - aa_step(0.25, ty, fwidth(ty));
		vec2 tq = uv / 0.0045;
		vec2 tc = floor(tq), tf = fract(tq);
		float tw = step(2.0, mod(tc.x + tc.y, 4.0));
		float th = sin(3.14159 * (tw > 0.5 ? tf.y : tf.x));
		float tfade = smoothstep(0.25, 0.7, px / 0.0045);
		vec3 cc = mix(col_d.rgb, vec3(0.07, 0.075, 0.085), mix(tw, 0.5, tfade)) * mix(0.55 + 0.45 * th, 0.85, tfade);
		col_dir = mix(col_dir, cc, carb);
		mr_dir = mix(mr_dir, vec3(0.2, 0.25 + 0.1 * (1.0 - th), 0.6), carb);
		direct = true;
		if (side) {
			vec2 rc = recv + vec2(-0.012, 0.004);
			vec2 lq = vec2((p.z - rc.x) * zdir, p.y - rc.y);
			float R = 0.017;
			float dr = length(lq);
			float disc = 1.0 - aa_step(R, dr, px);
			float ring = (1.0 - aa_step(R * 1.16, dr, px)) - disc;
			float g7 = min(seg_dist(lq, vec2(-0.40, 0.5) * R, vec2(0.40, 0.5) * R), seg_dist(lq, vec2(0.38, 0.5) * R, vec2(-0.1, -0.62) * R));
			float seven = (1.0 - aa_step(0.12 * R, g7, px)) * disc;
			col_dir = mix(col_dir, vec3(0.86), disc);
			col_dir = mix(col_dir, col_d.rgb, max(ring, seven));
			mr_dir = mix(mr_dir, mr_a, disc + ring);
		}
		glow = (col_b.rgb * red + col_c.rgb * blue) * fx_env;
	} else if (skin_pattern == 22) {
		// Frostbite: pale blue frozen metal (A) under fern-like frost (B, white, rough): feathers grow from scattered seeds
		// (a tapering spine with barbs angled to the tip), denser in frosty patches, with a white haze; glowing ice
		// fractures (emission) run through it in places.
		float fp = smoothstep(0.32, 0.68, fbm2(sp3 / (s * 3.0)));
		float fr = 0.0;
#ifndef SKIN_LITE
		vec2 q = uv / s;
		vec2 c = floor(q);
		for (int j = -1; j <= 1; j++) {
			for (int i = -1; i <= 1; i++) {
				vec2 o = c + vec2(float(i), float(j));
				float h1 = hash13(vec3(o, 6.1 + skin_seed)), h2 = hash13(vec3(o, 2.3 + skin_seed));
				vec2 seed = o + 0.2 + 0.6 * vec2(h1, h2);
				float a = hash13(vec3(o, 9.7)) * 6.28318;
				vec2 dir = vec2(cos(a), sin(a));
				vec2 d = (q - seed) * s;
				float x = dot(d, dir), y = abs(dot(d, vec2(-dir.y, dir.x)));
				float len = s * (0.8 + 0.9 * h1);
				float ins = step(0.0, x) * step(x, len);
				float spine = ins * (1.0 - aa_step(0.0006 * (1.0 - x / len) + 0.00015, y, px));
				float per = 0.0026;
				float bd = abs(fract((x - y) / per + 0.5) - 0.5) * per * 0.707;
				float barb = ins * (1.0 - aa_step(0.00034, bd, px)) * (1.0 - aa_step((len - x) * 0.38, y, px));
				fr = max(fr, max(spine, barb * 0.8));
			}
		}
		fr = mix(fr, 0.28, smoothstep(0.35, 0.9, px / 0.0026)) * mix(0.3, 1.0, fp);
#endif
		float haze = fp * 0.4;
		float wf = max(fr, haze);
		col_dir = mix(col_a.rgb, col_b.rgb, wf);
		mr_dir = mix(mr_a, mr_b, wf);
		float f1, edge, id;
		vec2 rel;
		vec2 kq = uv / (s * 3.2);
		kq += (vec2(vnoise(vec3(kq * 2.0, 3.1)), vnoise(vec3(kq * 2.0, 8.3))) - 0.5) * 0.35;
		cells2(kq, vec2(s * 3.2), 5.5 + skin_seed, f1, edge, id, rel);
		float crack = (1.0 - aa_step(0.0006, edge, px)) * step(0.35, id);
		float chalo = exp(-edge / 0.0025) * step(0.35, id);
		col_dir = mix(col_dir, vec3(0.75, 0.92, 1.0), crack);
		direct = true;
		glow = glow_color.rgb * (crack * 2.4 + chalo * 0.45) * (glow_energy * tmk + fx_env);
	} else if (skin_pattern == 23) {
		// Hive: translucent amber honeycomb lit from within (A amber, C the darker amber at the walls; emission brightest
		// in each cell's middle, varying per cell, with a honey ripple) in bronze walls (B); honey drips run down below
		// some cells on the side faces.
		vec2 q = uv / s;
		vec4 hc = hex_cell(q);
		float he = hex_dist(hc.xy);
		float hw = fwidth(he) + 1e-4;
		float wall = 1.0 - aa_step(0.08, he, hw);
		float fade = smoothstep(0.2, 0.5, hw / 0.1);
		wall = mix(wall, 0.3, fade);
		float cid = hash13(vec3(floor(hc.zw * 2.0 + 0.5), 5.5 + skin_seed));
		float inner = smoothstep(0.08, 0.42, he);
		float rip = vnoise(vec3(q * 3.2, cid * 9.0));
		float fill = 0.55 + 0.45 * cid;
		// drips: from the bottom vertex of some cells on the side faces, a tapering bead running down
		float drip = 0.0;
		if (side) {
			vec2 dq = (q - hc.zw) * s;                    // metres from the cell centre
			float dh = hash13(vec3(floor(hc.zw * 2.0 + 0.5), 1.9 + skin_seed));
			float dlen = s * (0.4 + 0.9 * dh);
			float yy = -dq.y - s * 0.55;                  // below the cell's bottom
			float wd = s * 0.07 * (1.0 - clamp(yy / dlen, 0.0, 1.0) * 0.5);
			float bead = 1.0 - aa_step(s * 0.11, length(vec2(dq.x, yy - dlen)), px);
			drip = step(0.62, dh) * max((1.0 - aa_step(wd, abs(dq.x), px)) * step(-s * 0.1, yy) * step(yy, dlen), bead);
		}
		vec3 amber = mix(col_c.rgb, col_a.rgb, inner * 0.75 + 0.25 * cid) * (0.75 + 0.35 * rip);
		vec3 bronze = col_b.rgb * (0.7 + 0.5 * smoothstep(0.0, 0.08, he));
		col_dir = mix(amber, bronze, wall);
		col_dir = mix(col_dir, col_a.rgb * 0.9, drip);
		mr_dir = mix(mix(mr_a, mr_b, wall), mr_a, drip);
		direct = true;
		glow = glow_color.rgb * ((0.3 + 0.7 * inner) * fill * (0.75 + 0.5 * rip) * (1.0 - wall) + drip * 0.6) * (glow_energy * tmk + fx_env);
	} else if (skin_pattern == 24) {
		// Foundry: hammered cast iron (A, rust-brown B on the dimples' rims) cracked open by molten metal: warped Voronoi
		// cracks glowing in the glow colour with a white-hot core, the iron around them heat-tinted (C). L2: the melt flows
		// along the cracks and embers flicker in them; a shot flares the cracks (most at the muzzle), a kill floods them.
		vec2 wq = uv / s;
		vec2 warp = vec2(vnoise(vec3(wq * 1.6, 3.3 + skin_seed)), vnoise(vec3(wq * 1.6, 7.9 + skin_seed))) - 0.5;
		float f1, ce, cid;
		vec2 rel;
		cells2(wq + warp * 0.6, vec2(s), 1.9 + skin_seed, f1, ce, cid, rel);
		float cwid = 0.00045 + 0.0008 * vnoise(sp3 / (s * 0.3));
		float crack = 1.0 - aa_step(cwid, ce, px);
		float halo = exp(-ce / 0.0022);
		float dimple = 0.5;
#ifndef SKIN_LITE
		float hf1, he2, hid;
		vec2 hrel;
		cells2(uv / 0.0055, vec2(0.0055), 4.4 + skin_seed, hf1, he2, hid, hrel);
		// hammer facets: each dimple lit on one side, shadowed on the other
		float facet = dot(hrel / max(hf1, 1e-5), vec2(-0.6, 0.8)) * smoothstep(0.0, 0.0025, hf1);
		dimple = mix(0.5 + 0.5 * facet, 0.5, smoothstep(0.3, 0.8, px / 0.0055));
#endif
		vec3 iron = mix(col_a.rgb, col_b.rgb, smoothstep(0.6, 1.0, dimple) * 0.8) * (0.72 + 0.5 * dimple);
		iron = mix(iron, col_c.rgb, halo * 0.6);
		vec3 gc = glow_color.rgb;
		vec3 hot = mix(gc, vec3(1.0, 0.93, 0.78), 0.5);
		col_dir = mix(iron, hot * 0.7, crack);
		mr_dir = mix(mix(mr_a, mr_b, dimple * 0.6), vec3(0.0, 0.5, 0.3), crack);
		direct = true;
		float flow = 1.0, ember = 0.0;
#ifndef SKIN_LITE
		if (anim > 0.0) {
			// the melt flows: a noise scrolled along the gun brightens and dims stretches of every crack
			flow = 0.45 + 1.1 * vnoise(vec3(uv / (s * 0.7) + vec2(tt * anim * 1.8, tt * anim * 0.4), tt * anim * 0.5));
			// embers: sparse specks near the cracks flash white-hot for a moment
			vec2 ec = floor(uv / 0.0035);
			float eh = hash13(vec3(ec, floor(tt * 5.0 + hash13(vec3(ec, 1.7)) * 5.0)));
			ember = step(0.9, eh) * halo * halo;
		}
#endif
		glow = (hot * crack * 2.6 + gc * halo * 0.3) * flow * glow_energy * tmk + hot * ember * 2.0 * top_mask;
		glow += hot * (crack + halo * 0.7) * ((fire_env * (1.5 - tz) + kill_env * 1.6) * top_mask * hipk);
	} else if (skin_pattern == 25) {
		// Aurora: dark frosted ice (A, frost patches and star specks) under flowing northern-light ribbons: three diagonal
		// curtains, each with a bright lower edge (B) fading up into C with fine vertical rays (emission). L2: the curtains
		// drift and their rays shimmer; a shot brightens them, a kill flares them.
		float tdr = anim > 0.0 ? tt * anim : 0.0;
		float H = max(gun_max.y - gun_min.y, 0.05);
		vec3 rib = vec3(0.0);
		float ribw = 0.0;
		for (int k = 0; k < 3; k++) {
			float fk = float(k);
			float ph = fk * 2.3 + skin_seed;
			float zz = p.z / 0.1;
			float yc = gun_min.y + H * (0.12 + 0.3 * fk) + (p.z - gun_min.z) * 0.18
				+ 0.016 * sin(zz * 2.0 + ph + tdr * 2.0) + 0.007 * sin(zz * 5.1 - ph * 1.7 - tdr * 3.1);
			float dy = p.y - yc;                                         // metres above the curtain's lower edge
			float up = 0.028;
			float body = smoothstep(-0.0025, 0.0006, dy) * exp(-max(dy, 0.0) / up);
			float rays = 0.6 + 0.4 * sin(p.z / 0.0016 + 3.0 * sin(p.z / 0.009 + fk) + tdr * 5.0);
			float a = body * mix(1.0, rays, smoothstep(0.0, 0.006, dy)) * (0.7 + 0.3 * sin(zz * 1.3 + fk * 4.0 - tdr));
			rib += mix(col_b.rgb, col_c.rgb, smoothstep(0.0, up * 1.4, dy)) * a;
			ribw = max(ribw, a);
		}
		float fn = fbm2(sp3 / 0.018);
		float frost = smoothstep(0.55, 0.8, fn);
		vec3 ice = mix(col_a.rgb, col_d.rgb, frost * 0.55);
		vec2 sc = floor(uv / 0.003);
		vec2 sf = fract(uv / 0.003) - 0.5;
		float sh = hash13(vec3(sc, 4.2 + skin_seed));
		float star = step(0.93, sh) * (1.0 - aa_step(0.12, length(sf), px / 0.003)) * (1.0 - smoothstep(0.4, 0.9, px / 0.003));
		col_dir = ice + rib * 0.3 + vec3(star * 0.4);
		mr_dir = mix(mr_a, vec3(0.0, 0.7, 0.3), frost * 0.5);
		direct = true;
		float tw = anim > 0.0 ? 0.5 + 0.5 * sin(tt * 4.0 + sh * 40.0) : 1.0;
		glow = rib * (glow_energy * tmk + fx_env * 1.2) + vec3(0.8, 0.85, 1.0) * star * tw * 0.6 * tmk;
	} else if (skin_pattern == 26) {
		// Orrery: navy enamel (B) engraved with a brass (A) star chart on the side faces: chart medallions (rings, 16
		// spokes, a star at the centre) on a staggered grid and scattered stars; each medallion is a little orrery with two
		// planets (C, D) and a tilted orbit ring, and a brass clock dial sits on the receiver. L2: the planets orbit and the rings precess, the
		// dial's hand ticks once a second (and once more per kill); a shot flashes the brass lines.
		float tdr = anim > 0.0 ? tt * anim : 0.0;
		float ln = 0.0, star = 0.0, planet = 0.0;
		vec3 pcol = col_c.rgb;
		if (side) {
			float G = 0.07;
			vec2 gq = vec2(p.z, p.y) / G;
			gq.x += 0.5 * mod(floor(gq.y), 2.0);
			vec2 d = (gq - floor(gq) - 0.5) * G;                          // metres from the medallion's centre
			float r = length(d);
			float outer = 1.0 - aa_step(0.03, r, px);
			float rings = thin_line(abs(fract(r / 0.0085 + 0.5) - 0.5) * 0.0085, 0.00026, 0.0085, px) * outer;
			float ang = atan(d.y, d.x);
			float spk = abs(fract(ang / 6.28318 * 16.0 + 0.5) - 0.5) / 16.0 * 6.28318 * r;
			float spokes = thin_line(spk, 0.0002, 0.003, px) * aa_step(0.0085, r, px) * outer;
			vec2 ad = abs(d);
			float st4 = 1.0 - aa_step(0.0, min(ad.x * 0.22 + ad.y, ad.x + ad.y * 0.22) - 0.0042, px);
			float rim2 = thin_line(abs(r - 0.0315), 0.0005, 1.0, px);
			ln = max(max(rings, spokes), max(st4 * (1.0 - aa_step(0.008, r, px)), rim2));
			// each medallion is a little orrery: two planets (C, D) on its rings and a tilted orbit ring round it (A);
			// at L2 the planets orbit and the tilted ring precesses
			float mh = hash13(vec3(floor(gq), 3.9 + skin_seed));
			for (int k = 0; k < 2; k++) {
				float fk = float(k);
				float rk = 0.0085 * (2.0 + fk);
				float th = mh * 6.28318 + fk * 2.5 + tdr * (1.7 - 0.6 * fk) * 2.0;
				float pl = 1.0 - aa_step(0.0029 - fk * 0.0006, length(d - vec2(cos(th), sin(th)) * rk), px);
				if (pl > planet) { planet = pl; pcol = fk < 0.5 ? col_c.rgb : col_d.rgb; }
			}
			float pa = 0.6 + mh * 0.8;
			vec2 od = vec2(cos(pa) * d.x + sin(pa) * d.y, -sin(pa) * d.x + cos(pa) * d.y);
			vec2 ab = vec2(0.033, 0.004 + 0.011 * abs(cos(tdr * 1.3 + mh * 6.0)));
			float orbit = thin_line(abs(length(od / ab) - 1.0) * ab.y, 0.0005, 1.0, px);
			ln = max(ln, orbit * (1.0 - planet));
			// scattered stars
			vec2 sc = floor(uv / 0.005);
			vec2 sf = fract(uv / 0.005) - 0.5;
			float sh = hash13(vec3(sc, 7.7 + skin_seed));
			star = step(0.8, sh) * (1.0 - aa_step(0.06 + 0.08 * fract(sh * 13.0), length(sf), px / 0.005)) * (1.0 - outer) * (1.0 - smoothstep(0.4, 0.9, px / 0.005));
			// the clock dial, ahead of the kill counter's spot
			vec2 dc = recv + vec2(-0.038, 0.002);
			vec2 dq = vec2(dc.x + (p.z - dc.x) * zdir, p.y);
			float tick = anim > 0.0 ? floor(tt) + smoothstep(0.0, 0.08, fract(tt)) : 1.5;
			vec4 gg = gauge(dq, dc, 0.0115, 1.5708 - (tick + kill_slide) * 0.5236, px);
			ln = mix(ln, max(gg.z, gg.w), gg.x);
			ln = max(ln, gg.y);
		}
		col_dir = mix(col_b.rgb, col_a.rgb, max(ln, star));
		col_dir = mix(col_dir, pcol, planet);
		mr_dir = mix(mr_b, mr_a, max(ln, star));
		mr_dir = mix(mr_dir, vec3(0.3, 0.3, 0.6), planet);
		direct = true;
		float tw = anim > 0.0 ? 0.55 + 0.45 * sin(tt * 3.0 + p.z * 900.0 + p.y * 700.0) : 1.0;
		glow = glow_color.rgb * (ln * 0.3 + star * tw * 1.4) * glow_energy * tmk + pcol * planet * 0.5 * glow_energy * tmk;
		glow += glow_color.rgb * ln * 1.5 * fx_env;
	}
	vec3 col = col_a.rgb * wt.x + col_b.rgb * wt.y + col_c.rgb * wt.z;
	vec3 mrs = mr_a * wt.x + mr_b * wt.y + mr_c * wt.z;
	if (direct) {
		col = col_dir;
		mrs = mr_dir;
	}
	col *= shade;
	float r = clamp(mrs.y + rough_add, 0.05, 1.0);
	float m = mrs.x;
	float sp = mrs.z;
#ifdef SKIN_LITE
	// Low has no sky reflections: less metal and a lighter base, or metallic finishes go black
	col = col * mix(1.0, 1.35, m) + vec3(0.015);
	m *= 0.6;
#endif
	if (counter_on > 0.5 && gun_nrm.x * counter_side > 0.5) {
		// Kill counter: a small dark display with two 7-segment glyphs (unlit segments faintly visible) in the glow colour
		vec2 lq = vec2(p.z - counter_rect.x, p.y - counter_rect.y);
		vec2 sz = counter_rect.zw - counter_rect.xy;
		if (all(greaterThan(lq, vec2(0.0))) && all(lessThan(lq, sz))) {
			float edge = min(min(lq.x, sz.x - lq.x), min(lq.y, sz.y - lq.y));
			float scr = aa_step(0.0006, edge, px);
			col = mix(col * 0.6, vec3(0.012, 0.014, 0.014), scr);
			r = mix(r, 0.15, scr);
			m = mix(m, 0.0, scr);
			float gh = sz.y * 0.74, gw = gh * 0.56, gap = gw * 0.28;
			float x0 = (sz.x - (gw * 2.0 + gap)) * 0.42;
			float ww = px / gw * 0.7;
			vec2 g0 = seven_seg(vec2((lq.x - x0) / gw, (lq.y - sz.y * 0.13) / gh), int(counter_val.x), ww);
			vec2 g1 = seven_seg(vec2((lq.x - x0 - gw - gap) / gw, (lq.y - sz.y * 0.13) / gh), int(counter_val.y), ww);
			vec2 pc = abs(lq - vec2(x0 + gw * 2.0 + gap * 1.8, sz.y * 0.72));
			float plus = counter_val.z * max(1.0 - aa_step(0.0, seg_box(pc, vec2(0.0), vec2(gw * 0.2, gw * 0.05)), px),
			                                 1.0 - aa_step(0.0, seg_box(pc, vec2(0.0), vec2(gw * 0.05, gw * 0.2)), px));
			float lit = max(max(g0.x, g1.x), plus);
			float ghost = max(g0.y, g1.y) - lit;
			glow = glow * (1.0 - scr) + glow_color.rgb * (lit * (0.85 + 0.6 * counter_flash) + max(ghost, 0.0) * 0.025 + 0.012) * scr * hipk;
			col = mix(col, glow_color.rgb * 0.25, lit * scr);
		}
	}
	vec3 wear_p = gun_pos * 6.0;                    // gun metres -> about the factory's mesh-local units
#endif
	if (detail > 0.0) {
		float n1 = vnoise(wear_p * detail_scale);
#ifdef SKIN_LITE
		float n2 = n1;
#else
		float n2 = vnoise(wear_p * detail_scale * 4.3 + 7.1);
#endif
		float n = n1 * 0.65 + n2 * 0.35;
		col *= 1.0 + (n - 0.5) * 0.35 * detail;
		r = clamp(r + (n2 - 0.5) * 0.30 * detail, 0.05, 1.0);
	}
	ALBEDO = col;
	METALLIC = m;
	ROUGHNESS = r;
	SPECULAR = sp;
#ifndef SKIN
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
#endif
	vec3 e = emission_color.rgb * emission_energy + glow;
	e += col * rim * pow(1.0 - ndv, 3.0) * 0.6;
	e += col * vec3(1.0, 0.62, 0.3) * flash * 0.8 * (0.35 + 0.65 * ndv);
	EMISSION = e;
}
