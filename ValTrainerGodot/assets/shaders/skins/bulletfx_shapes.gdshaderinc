// Skin Bullet FX: the per-line flash shapes shared by the muzzle overlay (bulletfx_muzzle.gdshader) and the impact flash
// (bulletfx_flash.gdshader). Procedural, no textures, constant loops. kind = BulletFx.Kind (1..10, the line order of
// skins3-contracts §1: glasswork, thunderhead, basalt, oscilloscope, rally, frostbite, hive, foundry, aurora, orrery).
// uv: -1..1 (already rotated by the caller), k: 0..1 over the flash's life, sd: random seed, tm: TIME.
// Returns the colour already weighted by coverage (rgb) and the coverage (a); the caller adds it.

float bfx_h(float x, float sd) { return fract(sin(x * 127.1 + sd * 311.7) * 43758.5453); }

// angular distance to the ray at angle a0 (radians), scaled by the radius: a width in uv units
float bfx_ray(vec2 uv, float a0) {
	vec2 d = vec2(cos(a0), sin(a0));
	float along = dot(uv, d);
	float across = abs(uv.x * d.y - uv.y * d.x);
	return along > 0.0 ? across : 9.0;
}

float bfx_soft(float d, float w) { return 1.0 - smoothstep(0.0, w, d); }

vec4 bfx_shape(int kind, vec2 uv, float k, float sd, float tm, vec3 c1, vec3 c2, vec3 c3) {
	float r = length(uv);
	float ang = atan(uv.y, uv.x);
	vec3 col = c1;
	float a = 0.0;
	float core = exp(-r * r * 22.0);
	if (kind == 1) {
		// Glasswork: a prism star: six thin rays, each a different jewel colour, and a faint hexagonal lens ring
		vec3 acc = vec3(0.0);
		for (int i = 0; i < 6; i++) {
			float fi = float(i);
			float len = 0.55 + 0.35 * bfx_h(fi, sd);
			float ray = bfx_soft(bfx_ray(uv, fi * 1.0472 + 0.2), 0.045 * (1.0 - r / len)) * step(r, len);
			vec3 c = i % 3 == 0 ? c1 : i % 3 == 1 ? c2 : c3;
			acc += c * ray;
			a += ray;
		}
		vec2 h = abs(uv);
		float hex = max(dot(h, vec2(0.866, 0.5)), h.y);
		float lens = bfx_soft(abs(hex - 0.42), 0.025) * 0.5;
		acc += c2 * lens + vec3(1.0) * core;
		a += lens + core;
		return vec4(acc, a);
	} else if (kind == 2) {
		// Thunderhead: three forked mini-bolts crackle out of the core
		float b = 0.0;
		for (int i = 0; i < 3; i++) {
			float fi = float(i);
			float a0 = fi * 2.094 + bfx_h(fi, sd) * 0.8;
			vec2 d = vec2(cos(a0), sin(a0));
			float along = dot(uv, d);
			float across = uv.x * d.y - uv.y * d.x;
			float x = along * 6.0;
			float j = mix(bfx_h(floor(x) + fi * 7.0 + floor(tm * 40.0), sd), bfx_h(floor(x) + 1.0 + fi * 7.0 + floor(tm * 40.0), sd), fract(x)) - 0.5;
			float len = 0.6 + 0.35 * bfx_h(fi + 3.0, sd);
			b = max(b, bfx_soft(abs(across - j * 0.22 * along), 0.03) * step(0.0, along) * step(along, len));
		}
		col = mix(c1, c2, core + b * 0.5);
		a = b + core * 1.2;
	} else if (kind == 3) {
		// Basalt: a dull red burst with blunt spikes and flying grit
		float sp = 0.0;
		for (int i = 0; i < 5; i++) sp = max(sp, bfx_soft(bfx_ray(uv, float(i) * 1.2566 + bfx_h(float(i), sd)), 0.09) * step(r, 0.5 + 0.2 * bfx_h(float(i) + 4.0, sd)));
		float pc = floor(ang / 6.2832 * 14.0);
		float grit = step(0.6, bfx_h(pc, sd)) * bfx_soft(abs(r - (0.35 + 0.5 * k + 0.2 * bfx_h(pc + 9.0, sd))), 0.04);
		col = mix(c1 * 0.8, c2, core);
		a = sp * 0.7 + grit * 0.9 + core + exp(-r * r * 5.0) * 0.3;
	} else if (kind == 4) {
		// Oscilloscope: an electric snap: a polar sine ring and a hard little core
		float tr = 0.36 + 0.09 * sin(ang * 8.0 + sd * 6.0 + tm * 60.0);
		float ring = bfx_soft(abs(r - tr), 0.035);
		float zig = bfx_soft(abs(uv.y - 0.12 * sign(sin(uv.x * 22.0 + sd * 9.0))), 0.03) * step(abs(uv.x), 0.75) * step(0.2, abs(uv.x)) * 0.8;
		col = mix(c1, c2, core);
		a = ring + zig + core * 1.3;
	} else if (kind == 5) {
		// Rally: two livery streaks (the stripe colours) either side of a crisp white core
		float s1 = bfx_soft(abs(uv.y - 0.1), 0.045) * (1.0 - smoothstep(0.2, 0.95, abs(uv.x)));
		float s2 = bfx_soft(abs(uv.y + 0.1), 0.045) * (1.0 - smoothstep(0.2, 0.95, abs(uv.x)));
		return vec4(c1 * s1 + c3 * s2 + vec3(1.0) * core * 1.3, s1 + s2 + core * 1.3);
	} else if (kind == 6) {
		// Frostbite: a sharp six-armed ice crystal with side branches
		float sa = mod(ang + 0.5236, 1.0472) - 0.5236;
		vec2 p = vec2(cos(sa), abs(sin(sa))) * r;
		float arm = bfx_soft(p.y, 0.025 * (1.0 - r)) * step(r, 0.9);
		float br = 0.0;
		for (int i = 1; i < 3; i++) {
			float x0 = float(i) * 0.25;
			vec2 q = p - vec2(x0, 0.0);
			float along = dot(q, vec2(0.5, 0.866));
			float across = abs(q.x * 0.866 - q.y * 0.5);
			br = max(br, bfx_soft(across, 0.02) * step(0.0, along) * step(along, 0.22 - float(i) * 0.05));
		}
		col = mix(c1, c2, core + arm * 0.4);
		a = arm + br * 0.8 + core;
	} else if (kind == 7) {
		// Hive: an amber hexagon flash with a soft honey glow
		vec2 h = abs(uv);
		float hex = max(dot(h, vec2(0.866, 0.5)), h.y);
		float rim = bfx_soft(abs(hex - 0.45 - 0.15 * k), 0.04);
		col = mix(c1, c2, core + rim * 0.3);
		a = rim + exp(-r * r * 6.0) * 0.6 + core;
	} else if (kind == 8) {
		// Foundry: a spray of hot sparks fanning out of an orange-white core
		float sp = 0.0;
		for (int i = 0; i < 12; i++) {
			float fi = float(i);
			float a0 = (bfx_h(fi, sd) - 0.5) * 6.2832;
			float len = 0.35 + 0.6 * bfx_h(fi + 13.0, sd);
			float st = len * (0.25 + 0.5 * k);
			vec2 d = vec2(cos(a0), sin(a0));
			float along = dot(uv, d);
			float across = abs(uv.x * d.y - uv.y * d.x);
			sp = max(sp, bfx_soft(across, 0.018) * step(st, along) * step(along, st + 0.18));
		}
		col = mix(c1, c2, core + sp * 0.4);
		a = sp + core * 1.3 + exp(-r * r * 7.0) * 0.4;
	} else if (kind == 9) {
		// Aurora: a short ribbon of northern light curling through the flash
		float y = uv.y - 0.22 * sin(uv.x * 3.2 + sd * 6.0 + tm * 6.0);
		float band = bfx_soft(abs(y), 0.12) * (1.0 - smoothstep(0.3, 0.95, abs(uv.x)));
		float rays = 0.6 + 0.4 * sin(uv.x * 30.0 + tm * 20.0);
		col = mix(c1, c3, clamp(y * 4.0 + 0.5, 0.0, 1.0));
		col = mix(col, vec3(1.0), core);
		a = band * rays + core;
	} else {
		// Orrery: a tilted gold orbit with a mote on it and a small four-point star
		vec2 e = vec2(uv.x, uv.y * 2.6);
		float er = length(e);
		float orbit = bfx_soft(abs(er - 0.6), 0.03);
		float ma = sd * 6.2832 + k * 4.0;
		vec2 mp = vec2(cos(ma) * 0.6, sin(ma) * 0.6 / 2.6);
		float mote = exp(-dot(uv - mp, uv - mp) * 400.0) * 1.5;
		float star = max(bfx_soft(abs(uv.x), 0.02) * (1.0 - smoothstep(0.0, 0.45, abs(uv.y))), bfx_soft(abs(uv.y), 0.02) * (1.0 - smoothstep(0.0, 0.45, abs(uv.x))));
		col = mix(c1, c2, core + mote);
		a = orbit * 0.8 + mote + star + core;
	}
	return vec4(col * a, a);
}
