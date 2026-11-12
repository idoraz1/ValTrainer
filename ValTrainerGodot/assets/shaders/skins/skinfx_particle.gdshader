// Skin effect particles: the kill / finisher bursts (KillFxWorld) and the Bullet FX impacts (BulletFxWorld). Camera-facing
// sprites (motes, glass shards, rock grit, crystals, droplets, steam, embers, livery chips) and velocity-aligned streaks.
// Premultiplied and unshaded like skinfx.gdshader: bright coverage glows (ENERGY), solid shapes also dim what is behind
// them (dark rim / body), so they read on bright and dark floors; coverage capped at ALPHA_MAX. The process material gives
// the rotation in INSTANCE_CUSTOM.x and the life phase in INSTANCE_CUSTOM.y (ParticleProcessMaterial convention).
shader_type spatial;
render_mode blend_premul_alpha, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

instance uniform vec4 tint : source_color = vec4(1.0);
instance uniform vec4 tint2 : source_color = vec4(1.0);
instance uniform vec4 tint3 : source_color = vec4(1.0);
// 0 mote, 1 glass shard, 2 rock grit, 3 streak, 4 crystal, 5 droplet, 6 steam, 7 ember, 8 livery chip
instance uniform int shape = 0;
instance uniform float fade = 1.0;

const float ALPHA_MAX = 0.75;
const float ENERGY = 1.4;

varying float life;
varying vec2 q;
varying float rnd;

void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	life = INSTANCE_CUSTOM.y;
	rnd = fract(sin(float(INSTANCE_ID) * 12.9898 + 4.1) * 43758.5453);
	q = VERTEX.xy * 2.0;
	vec3 p;
	if (shape == 3 || shape == 5) {
		// streak / droplet: stretched along the velocity (particle_flag_align_y), turned to face the camera
		vec3 ax = MODEL_MATRIX[1].xyz;
		float ls = length(ax);
		ax = ax / max(ls, 1e-5);
		vec3 tc = normalize(CAMERA_POSITION_WORLD - c);
		vec3 side = normalize(cross(ax, tc) + vec3(1e-5));
		float wl = shape == 3 ? 0.3 : 0.7;
		float ll = shape == 3 ? 2.2 : 1.3;
		p = c + side * VERTEX.x * ls * wl + ax * VERTEX.y * ls * ll;
	} else {
		float a = INSTANCE_CUSTOM.x;
		vec2 v = VERTEX.xy;
		// fake tumble: shards flatten and widen as they spin
		if (shape == 1 || shape == 2 || shape == 4 || shape == 8) v.x *= 0.3 + 0.7 * abs(cos(life * 9.0 + rnd * 6.28));
		if (shape == 6) v *= 0.6 + 0.9 * life; // steam billows out
		v = vec2(v.x * cos(a) - v.y * sin(a), v.x * sin(a) + v.y * cos(a));
		p = c + (INV_VIEW_MATRIX[0].xyz * v.x + INV_VIEW_MATRIX[1].xyz * v.y) * s;
	}
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
}

float polyd(vec2 p, float n) {
	float an = 3.14159265 / n;
	float a = atan(p.y, p.x) + 3.14159265;
	float bn = mod(a, 2.0 * an) - an;
	return length(p) * cos(bn);
}

vec3 pick3(float k) { return k < 0.34 ? tint.rgb : k < 0.67 ? tint2.rgb : tint3.rgb; }

void fragment() {
	float a = 0.0;    // bright coverage
	float dk = 0.0;   // dark coverage
	vec3 col = tint.rgb;
	float f = 1.0 - smoothstep(0.55, 1.0, life);
	if (shape == 1) {
		// glass shard: a sliver triangle in one of three jewel colours with a dark lead edge
		float d = polyd(q * vec2(1.0, 0.8), 3.0);
		float body = 1.0 - smoothstep(0.62, 0.72, d);
		float edge = smoothstep(0.45, 0.66, d) * body;
		col = pick3(rnd);
		a = body * 0.75 + (1.0 - smoothstep(0.0, 0.25, length(q - vec2(-0.15, 0.1)))) * 0.6;
		dk = (1.0 - smoothstep(0.7, 0.9, d)) * 0.9;
		a *= 1.0 - edge * 0.5;
	} else if (shape == 2) {
		// rock grit: a dark stone chip with a hot rim (the palette tints the rim)
		float d = polyd(q, 4.0 + floor(rnd * 3.0));
		float body = 1.0 - smoothstep(0.62, 0.78, d);
		float rim = smoothstep(0.4, 0.68, d) * body;
		a = rim * 0.55 * (1.0 - life);
		dk = body * 1.3;
		col = tint.rgb;
	} else if (shape == 3) {
		// spark streak
		a = exp(-q.x * q.x * 5.0) * smoothstep(1.0, 0.3, abs(q.y));
		col = mix(tint.rgb, tint2.rgb, smoothstep(0.0, 0.8, q.y) * 0.7);
		dk = a * 0.2;
	} else if (shape == 4) {
		// ice crystal: an elongated diamond, pale with a bright edge
		vec2 p = abs(q) * vec2(2.4, 1.0);
		float d = p.x + p.y;
		float body = 1.0 - smoothstep(0.85, 1.0, d);
		float edge = smoothstep(0.55, 0.9, d) * body;
		a = body * 0.5 + edge * 0.7;
		dk = (1.0 - smoothstep(0.9, 1.25, d)) * 0.8;
		col = mix(tint2.rgb, tint.rgb, edge);
	} else if (shape == 5) {
		// droplet: a glossy blob stretched along its flight, with a highlight
		vec2 p = q * vec2(1.0, 0.8);
		float d = length(p + vec2(0.0, 0.25 * p.y));
		float body = 1.0 - smoothstep(0.65, 0.85, d);
		float hl = 1.0 - smoothstep(0.0, 0.25, length(q - vec2(-0.2, 0.25)));
		a = body * 0.8 + hl * 0.6;
		dk = (1.0 - smoothstep(0.75, 1.0, d)) * 0.7;
		col = mix(tint.rgb, tint2.rgb, hl);
	} else if (shape == 6) {
		// steam: a soft pale puff that dims and whitens what is behind it
		float d = length(q);
		float lump = 0.85 + 0.15 * sin(atan(q.y, q.x) * 5.0 + rnd * 20.0);
		float puff = 1.0 - smoothstep(0.2, 1.0, d / lump);
		a = puff * 0.45 * (1.0 - life * 0.7);
		dk = puff * 0.35;
		col = mix(vec3(0.82, 0.85, 0.88), tint2.rgb, 0.25);
		f = smoothstep(0.0, 0.1, life) * (1.0 - smoothstep(0.35, 1.0, life));
	} else if (shape == 7) {
		// ember: a hot dot that flickers and cools
		float d = length(q);
		float flick = 0.6 + 0.4 * sin(TIME * 30.0 + rnd * 50.0);
		a = (exp(-d * d * 9.0) * 1.2 + exp(-d * d * 2.5) * 0.3) * flick;
		col = mix(tint2.rgb, tint.rgb, life);
		dk = exp(-d * d * 3.0) * 0.3;
	} else if (shape == 8) {
		// livery chip: a small rectangle in one of the three palette colours
		vec2 p = abs(q) - vec2(0.45, 0.8);
		float body = 1.0 - smoothstep(-0.05, 0.05, max(p.x, p.y));
		col = pick3(rnd);
		a = body * 0.85;
		dk = (1.0 - smoothstep(0.0, 0.15, max(p.x, p.y))) * 0.8;
	} else {
		// mote
		a = exp(-dot(q, q) * 3.5);
		col = mix(tint.rgb, tint2.rgb, rnd * 0.5);
		dk = exp(-dot(q, q) * 1.5) * 0.25;
	}
	float ca = clamp(a, 0.0, 1.0) * f * fade;
	float cd = clamp(dk * 0.6, 0.0, 1.0) * f * fade;
	ALBEDO = col * ca * ENERGY;
	ALPHA = min(max(ca * 0.8, cd), ALPHA_MAX);
}
