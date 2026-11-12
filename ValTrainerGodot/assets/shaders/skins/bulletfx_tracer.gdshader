// Skin Bullet FX: the line's tracer (BulletFxWorld). The factory tracer's camera-facing ribbon along the node's -Z axis
// (scale.z = length; fx_tracer.gdshader), widened by `wmul` to host the line's look: a bright head streak travels from the
// muzzle to the hit while the styled trail fades. The decoration narrows to the plain core over the last quarter of the
// beam, so the crosshair end looks like a factory tracer: the target and the crosshair area are never covered.
// kind = BulletFx.Kind (1 glasswork .. 10 orrery). Additive.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

instance uniform float head = 0.0;        // head position along the beam (m)
instance uniform float fade = 1.0;        // overall fade 0..1
instance uniform float len = 1.0;         // beam length (m)
instance uniform float wmul = 1.0;        // ribbon width / factory width
instance uniform float seed = 0.0;
instance uniform int kind = 1;
instance uniform vec4 tint : source_color = vec4(1.0, 0.78, 0.42, 1.0);
instance uniform vec4 tint2 : source_color = vec4(1.0);
instance uniform vec4 tint3 : source_color = vec4(1.0);
uniform float streak = 5.0;               // head streak length (m)
uniform float energy = 1.6;
uniform float px_width = 0.0026;          // factory width per metre of distance (≈ 2 px at 1080p)

varying float along;
varying float across;

void vertex() {
	vec3 a = MODEL_MATRIX[3].xyz;
	vec3 zaxis = MODEL_MATRIX[2].xyz;
	float l = length(zaxis);
	vec3 dir = -zaxis / max(l, 1e-4);
	float t = -VERTEX.z;                  // 0..1 along the beam
	vec3 p = a + dir * (t * l);
	vec3 tc = CAMERA_POSITION_WORLD - p;
	float d = length(tc);
	vec3 side = normalize(cross(dir, tc / max(d, 1e-4)));
	float w = clamp(d * px_width, 0.0015, 0.05) * wmul;
	p += side * VERTEX.x * w * 2.0;
	along = t * l;
	across = VERTEX.x * 2.0;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
}

float h1(float x) { return fract(sin(x * 127.1 + seed * 31.7) * 43758.5453); }
float jag(float x, float s) { float i = floor(x); return mix(h1(i + s), h1(i + 1.0 + s), fract(x)) * 2.0 - 1.0; }
float gl(float x, float k) { return exp(-x * x * k); }

void fragment() {
	float xs = across * wmul;                       // in factory half-widths: |xs| <= 1 is the factory ribbon
	float behind = head - along;
	float hd = behind >= 0.0 ? exp(-behind / streak * 2.5) : 0.0;
	float shown = behind >= 0.0 ? 1.0 : 0.0;
	float trail = 0.18 * fade * shown;
	float start = smoothstep(0.2, 2.5, along);     // soft start right at the muzzle
	float endk = 1.0 - smoothstep(len * 0.72, len * 0.97, along);  // decoration fades into the plain core near the hit
	float core = gl(xs, 5.0);
	float base = (hd * fade + trail);
	vec3 col = tint.rgb;
	float a = 0.0;
	if (kind == 1) {
		// Glasswork: a rod of stained glass: 0.7 m panes in three jewel colours with dim lead joints
		float seg = floor(along / 0.7);
		float k = h1(seg);
		col = k < 0.34 ? tint.rgb : k < 0.67 ? tint2.rgb : tint3.rgb;
		float joint = smoothstep(0.0, 0.08, fract(along / 0.7)) * smoothstep(1.0, 0.92, fract(along / 0.7));
		a = gl(xs, 3.0) * base * (0.35 + 0.65 * joint) * 1.3;
	} else if (kind == 2) {
		// Thunderhead: a forked crackle: a jagged path that re-strikes every frame or so, with a fainter branch
		float tq = floor(TIME * 45.0);
		float o = (jag(along / 0.8, tq) * 2.6 + jag(along / 0.25, tq + 7.0) * 0.8) * endk;
		float fk = step(0.55, h1(floor(along / 3.0) + tq)) * endk;
		float o2 = o + (jag(along / 0.5, tq + 3.0) + 1.2) * 2.0 * fract(along / 3.0);
		float bolt = gl(xs - o, 3.0) + gl(xs - o2, 4.0) * fk * 0.6;
		col = mix(tint.rgb, tint2.rgb, gl(xs - o, 12.0));
		a = (bolt * (0.6 + 0.4 * h1(tq)) + core * 0.6) * base * 1.4;
	} else if (kind == 3) {
		// Basalt: a dim red core with embers strewn along the trail
		float cell = floor(along / 0.45);
		float pos = (cell + 0.2 + 0.6 * h1(cell)) * 0.45;
		float off = (h1(cell + 5.0) - 0.5) * 3.0 * endk;
		float ember = exp(-pow((along - pos) / 0.04, 2.0) - pow(xs - off, 2.0) * 2.0) * (0.6 + 0.4 * sin(TIME * 40.0 + cell));
		col = mix(tint.rgb * 0.85, tint2.rgb, ember);
		a = (gl(xs, 4.0) * 0.8 + ember * 1.6) * base;
	} else if (kind == 4) {
		// Oscilloscope: a green sine trace riding along a faint baseline
		float o = 2.0 * sin(along * 6.0 - TIME * 70.0 + seed) * endk;
		col = mix(tint.rgb, tint2.rgb, gl(xs - o, 14.0));
		a = (gl(xs - o, 4.0) + core * 0.3) * base * 1.3;
	} else if (kind == 5) {
		// Rally: twin livery stripes either side of a white core
		float s1 = gl(xs - 1.5 * endk - 0.01, 6.0);
		float s2 = gl(xs + 1.5 * endk + 0.01, 6.0);
		vec3 c = tint.rgb * s1 + tint3.rgb * s2 + tint2.rgb * core;
		float aa = s1 + s2 + core;
		col = c / max(aa, 1e-3);
		a = aa * base;
	} else if (kind == 6) {
		// Frostbite: a pale icy core with crystal glints twinkling along a lingering trail
		float cell = floor(along / 0.35);
		float pos = (cell + 0.5) * 0.35;
		float off = (h1(cell) - 0.5) * 2.2 * endk;
		float glint = exp(-pow((along - pos) / 0.03, 2.0) - pow(xs - off, 2.0) * 3.0) * step(0.4, h1(cell + 2.0)) * (0.5 + 0.5 * sin(TIME * 50.0 + cell * 3.0));
		col = mix(tint.rgb, tint2.rgb, glint + core * 0.3);
		a = (gl(xs, 3.0) + glint * 1.8) * base;
	} else if (kind == 7) {
		// Hive: honey beads strung along an amber core
		float bead = exp(-pow((fract(along / 0.45) - 0.5) * 5.0, 2.0)) * gl(xs, 1.4) * (0.4 + 0.6 * endk);
		col = mix(tint.rgb, tint2.rgb, bead * 0.4);
		a = (bead + core * 0.6) * base * 1.2;
	} else if (kind == 8) {
		// Foundry: a white-hot head, an orange trail and spark dashes flicking off it
		float cell = floor(along / 0.6);
		float pos = (cell + h1(cell)) * 0.6;
		float side = (h1(cell + 3.0) < 0.5 ? -1.0 : 1.0) * (1.0 + 1.2 * h1(cell + 4.0)) * endk;
		float dash = exp(-pow((along - pos) / 0.12, 2.0) - pow(xs - side, 2.0) * 6.0) * step(0.35, h1(cell + 6.0));
		col = mix(tint.rgb, tint2.rgb, clamp(hd * 1.3, 0.0, 1.0));
		a = (core * 1.2 + dash * 1.4) * base;
	} else if (kind == 9) {
		// Aurora: a shimmering ribbon of northern light curling round the core (green to violet across its width)
		float o = 1.3 * sin(along * 1.7 + TIME * 8.0 + seed) * endk;
		float bw = 1.6 * endk + 0.4;
		float v = (xs - o) / bw;
		float band = (1.0 - smoothstep(0.6, 1.0, abs(v)));
		float sh = 0.55 + 0.45 * sin(along * 9.0 - TIME * 22.0);
		col = mix(tint.rgb, tint3.rgb, clamp(v * 0.5 + 0.5, 0.0, 1.0));
		col = mix(col, tint2.rgb, core * 0.5);
		a = (band * sh * 0.55 + core * 0.7) * base;
	} else {
		// Orrery: a thin gold line, and a tiny mote orbiting the head with its faint helix behind it
		float ph = TIME * 45.0 + seed;
		float am = head - 0.6;
		float mx = 2.8 * sin(ph) * endk;
		float mote = exp(-pow((along - am) / 0.07, 2.0) - pow(xs - mx, 2.0) * 1.5) * step(0.0, head - 0.6) * step(am, len);
		float helix = gl(xs - 2.8 * sin(ph - (head - along) * 6.0) * endk, 3.0) * step(0.0, behind) * step(behind, 2.0) * 0.3;
		col = mix(tint.rgb, tint2.rgb, mote);
		a = (gl(xs, 9.0) * base + (mote * 1.6 + helix) * fade);
	}
	ALBEDO = col * energy * a * start;
}
