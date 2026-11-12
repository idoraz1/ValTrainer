// Skin kill effects and finishers (skins 3: skins3-contracts.md): ONE uber shader for every quad and shell that
// KillFxWorld draws (the particles use skinfx_particle.gdshader). Premultiplied: each kind gives a bright coverage `a`
// (drawn in its colours, emission up to ENERGY) and a dark under-glow `dk` that dims what is behind it, so the effect
// reads on bright floors as well as dark ones (the skins-2 playtest found additive-only effects too faint on bright maps).
// At least 1 - ALPHA_MAX of whatever is behind still shows: an effect never hides a bot. Unshaded, never writes depth;
// walls still occlude it. Every kind is a cheap SDF with fwidth anti-aliasing and constant loops (GL Compatibility safe).
shader_type spatial;
render_mode blend_premul_alpha, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

instance uniform int kind = 0;
instance uniform float t = 0.0;              // 0..1 over the part's life
instance uniform float fade = 1.0;           // spawn-clear fast fade / slot fade
instance uniform vec4 col_main : source_color = vec4(1.0);
instance uniform vec4 col_accent : source_color = vec4(1.0);
instance uniform vec4 col_alt : source_color = vec4(1.0);
instance uniform float seed = 0.0;
instance uniform int bb = 0;                 // 0 = node basis, 1 = faces the camera around Y, 2 = faces the camera

const float ALPHA_MAX = 0.62;  // never hides more than ~60 % of what is behind it
const float ENERGY = 1.35;     // a little above 1: blooms slightly at High / Ultra, stays readable on bright floors
const float DARK = 0.55;       // strength of the dark under-glow

// kinds (KillFxRecipes.K)
const int K_ROSE = 1;
const int K_BOLT = 2;
const int K_SCORCH = 3;
const int K_BASALT = 4;
const int K_SCOPE = 5;
const int K_CHECKER = 6;
const int K_FROST = 7;
const int K_ICESPIKES = 8;
const int K_COMB = 9;
const int K_POOL = 10;
const int K_CURTAIN = 11;
const int K_CURTAIN_RING = 12;
const int K_GLOW = 13;
const int K_ORBIT = 14;
const int K_CHART = 15;
const int K_PULSE = 16;
const int K_STREAM = 17;
const int K_BEAM = 18;

varying vec3 lp;      // object-space position (shells)
varying vec2 msz;     // object scale in metres (x, y): quads are 1 x 1 units
varying vec2 q;       // quad coordinates -1..1, y up

void vertex() {
	lp = VERTEX;
	q = VERTEX.xy * 2.0;
	msz = vec2(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz));
	if (bb > 0) {
		vec3 c = MODEL_MATRIX[3].xyz;
		vec3 right;
		vec3 up;
		if (bb == 1) {
			vec3 tc = CAMERA_POSITION_WORLD - c;
			tc.y = 0.0;
			tc = normalize(tc + vec3(1e-4, 0.0, 0.0));
			right = normalize(cross(vec3(0.0, 1.0, 0.0), tc));
			up = vec3(0.0, 1.0, 0.0);
		} else {
			right = INV_VIEW_MATRIX[0].xyz;
			up = INV_VIEW_MATRIX[1].xyz;
		}
		vec3 p = c + right * VERTEX.x * msz.x + up * VERTEX.y * msz.y;
		POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
	} else {
		POSITION = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)); // POSITION is written on every path
	}
}

float h11(float x) { return fract(sin(x * 127.1 + seed * 3.7) * 43758.5453); }
float h21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// Soft line of half-width w around d = 0 (d in the same units as fwidth(d)).
float line(float d, float w) {
	float aa = max(fwidth(d), 1e-4);
	float ww = max(w, aa * 0.9);                   // at a distance a line stays about a pixel wide (and dims) instead of vanishing
	return (1.0 - smoothstep(ww, ww + aa * 1.5, abs(d))) * mix(1.0, w / ww, 0.5);
}

float ring(float r, float r0, float w) { return line(r - r0, w); }
float halo(float d, float w) { return exp(-abs(d) / w); }

// Hex grid: x = distance to the cell edge (0 at the edge, 0.5 at the centre), yz = cell id.
vec3 hexgrid(vec2 p) {
	const vec2 s = vec2(1.0, 1.7320508);
	vec4 hc = floor(vec4(p, p - vec2(0.5, 1.0)) / s.xyxy) + 0.5;
	vec4 h = vec4(p - hc.xy * s, p - (hc.zw + 0.5) * s);
	vec2 g;
	vec2 id;
	if (dot(h.xy, h.xy) < dot(h.zw, h.zw)) { g = h.xy; id = hc.xy; } else { g = h.zw; id = hc.zw + 0.5; }
	vec2 ag = abs(g);
	return vec3(0.5 - max(dot(ag, s * 0.5), ag.x), id);
}

float seg(vec2 p, vec2 a, vec2 b) {
	vec2 pa = p - a, ba = b - a;
	float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
	return length(pa - ba * h);
}

float env(float x, float a, float b) { return smoothstep(0.0, a, x) * (1.0 - smoothstep(b, 1.0, x)); }

// Jagged 1D noise: piecewise linear between hashed knots (n knots over u in 0..1), -1..1.
float jag(float u, float n, float s) {
	float x = u * n;
	float i = floor(x);
	return mix(h11(i + s), h11(i + 1.0 + s), fract(x)) * 2.0 - 1.0;
}

vec3 pick3(float k) { return k < 0.34 ? col_main.rgb : k < 0.67 ? col_accent.rgb : col_alt.rgb; }

void fragment() {
	vec3 col = col_main.rgb;
	float a = 0.0;    // bright coverage
	float dk = 0.0;   // dark under-glow
	float r = length(q);
	float ang = atan(q.y, q.x);

	if (kind == K_ROSE) {
		// Glasswork: a stained-glass rose window lights up ring by ring on the ground, then shatters.
		float u = ang / TAU + 0.5;
		float ringId = r < 0.3 ? 0.0 : r < 0.64 ? 1.0 : 2.0;
		float nSeg = ringId == 0.0 ? 1.0 : ringId == 1.0 ? 6.0 : 12.0;
		float cellU = floor(u * nSeg + ringId * 0.5);
		float lead = max(max(ring(r, 0.3, 0.018), ring(r, 0.64, 0.018)), ring(r, 0.96, 0.025));
		float segD = (fract(u * nSeg + ringId * 0.5) - 0.5) / nSeg * TAU * r;
		if (ringId > 0.0) lead = max(lead, line(abs(segD) - (0.5 / nSeg * TAU * r), 0.016));
		float front = smoothstep(0.0, 0.35, t) * 1.15;
		float lit = step(r, front) * step(r, 0.97);
		float id = h21(vec2(cellU, ringId) + seed);
		float shat = step(id, smoothstep(0.55, 0.95, t));
		float pane = (0.55 + 0.25 * (1.0 - r)) * lit * (1.0 - shat) * step(0.03, abs(segD) + float(ringId == 0.0));
		col = ringId == 0.0 ? col_accent.rgb : pick3(id);
		a = pane + ring(r, front, 0.03) * (1.0 - smoothstep(0.3, 0.45, t)) * 0.8;
		dk = max(lead * lit, halo(r - 0.97, 0.05) * lit) * (1.0 - smoothstep(0.75, 1.0, t)) * 1.3;
		a *= 1.0 - smoothstep(0.85, 1.0, t);
	} else if (kind == K_BOLT) {
		// Thunderhead: a forked lightning bolt strikes down, flickers three times, leaves a fading afterglow.
		float y = q.y * 0.5 + 0.5;
		float xb = (jag(y, 9.0, 1.0) * 0.18 + jag(y, 23.0, 5.0) * 0.06) * (0.4 + 0.6 * y);
		float d = (q.x - xb) * msz.x * 0.5;
		float fy = clamp((0.55 - y) / 0.4, 0.0, 1.0);    // a fork leaves the bolt at 55 % height
		float xf = xb + fy * (0.45 + 0.1 * h11(9.0)) * sign(h11(4.0) - 0.5) + jag(y, 17.0, 11.0) * 0.05 * fy;
		float df = (q.x - xf) * msz.x * 0.5;
		float fork = step(y, 0.55) * step(0.15, y);
		float fl = step(t, 0.1) + step(0.18, t) * step(t, 0.27) * 0.8 + step(0.36, t) * step(t, 0.42) * 0.6;
		float after = (1.0 - smoothstep(0.0, 1.0, t)) * 0.25;
		float core = line(d, 0.012) + line(df, 0.008) * fork * 0.8;
		float gl = halo(d, 0.07) * 0.6 + halo(df, 0.05) * fork * 0.35;
		a = (core + gl) * (fl + after) * (1.0 - smoothstep(0.85, 1.0, abs(q.y)));
		dk = (halo(d, 0.22) + halo(df, 0.15) * fork * 0.6) * (fl * 0.6 + after) * (1.0 - smoothstep(0.9, 1.0, abs(q.y)));
		col = mix(col_main.rgb, col_accent.rgb, clamp(core, 0.0, 1.0));
	} else if (kind == K_SCORCH) {
		// Thunderhead ground strike: Lichtenberg branches race outward, glow white-blue, cool to a dark scorch.
		float grow = smoothstep(0.0, 0.2, t) * 0.95;
		float best = 9.0;
		for (int i = 0; i < 7; i++) {
			float fi = float(i);
			float a0 = fi / 7.0 * TAU + h11(fi) * 0.6;
			float wig = jag(r, 6.0, fi * 3.0) * 0.35 * r;
			float da = abs(mod(ang - a0 - wig + PI, TAU) - PI) * r;
			best = min(best, da);
		}
		float branch = line(best, 0.02 * (1.0 - r * 0.6)) * step(r, grow);
		float hot = 1.0 - smoothstep(0.15, 0.6, t);
		a = (branch + ring(r, grow, 0.03) * hot + exp(-r * r * 40.0) * hot) * (0.35 + 0.65 * hot) * (1.0 - smoothstep(0.7, 1.0, t));
		dk = (halo(best, 0.06) * step(r, grow) + exp(-r * r * 8.0) * 0.6) * (1.0 - smoothstep(0.75, 1.0, t)) * 1.1;
		col = mix(col_main.rgb, col_accent.rgb, hot * branch);
	} else if (kind == K_BASALT) {
		// Basalt: charcoal hex columns at the feet; dim red magma seams crack open from the centre outward.
		vec3 hg = hexgrid(q * 3.4);
		vec2 cc = hg.yz * vec2(1.0, 1.7320508) / 3.4;
		float rc = length(cc);
		float front = smoothstep(0.0, 0.4, t) * 1.05;
		float on = step(rc, front) * (1.0 - smoothstep(0.82, 0.98, r));
		float seam = line(hg.x, 0.035) * on;
		float glowSeam = halo(hg.x, 0.05) * on;
		float flick = 0.75 + 0.25 * sin(TIME * 23.0 + h21(hg.yz) * 30.0);
		float cool = 1.0 - smoothstep(0.45, 1.0, t);
		a = (seam * 0.9 + glowSeam * 0.35) * flick * cool;
		a += ring(rc, front, 0.05) * (1.0 - smoothstep(0.3, 0.45, t)) * 0.6 * step(r, 0.95);
		dk = (0.8 * step(0.04, hg.x) + 0.3) * on * (1.0 - smoothstep(0.7, 1.0, t));
		col = mix(col_main.rgb, col_accent.rgb, seam * cool * 0.6);
	} else if (kind == K_SCOPE) {
		// Oscilloscope: a dark CRT disc on the ground with a graticule; a polar trace spikes, flatlines into a ring, shrinks.
		float amp = 0.18 * (1.0 - smoothstep(0.15, 0.55, t));
		float r0 = mix(0.62, 0.05, smoothstep(0.65, 1.0, t));
		float tr = r0 + amp * sin(ang * 9.0 + t * 40.0) * sin(ang * 2.0 + seed);
		float d = r - tr;
		float trace = line(d, 0.022) + halo(d, 0.05) * 0.45;
		float grat = max(line(q.x, 0.006), line(q.y, 0.006)) * step(r, 0.9) * 0.4;
		float ticks = line(fract(ang / TAU * 24.0 + 0.5) - 0.5, 0.06) * step(0.86, r) * step(r, 0.93) * 0.6;
		float on = smoothstep(0.0, 0.08, t) * (1.0 - smoothstep(0.85, 1.0, t));
		a = (trace + grat + ticks + ring(r, 0.95, 0.012) * 0.5) * on;
		dk = (1.0 - smoothstep(0.9, 1.0, r)) * 0.8 * on;
		col = mix(col_main.rgb, col_accent.rgb, line(d, 0.01));
	} else if (kind == K_CHECKER) {
		// Rally: a chequered band rolls outward from the feet, with the livery's two stripes sweeping round inside it.
		float R = mix(0.25, 0.95, smoothstep(0.0, 0.55, t));
		float band = step(R - 0.16, r) * step(r, R);
		float cu = floor((ang / TAU + 0.5) * 28.0 + t * 3.0);
		float cv = floor((r - R) / 0.08);
		float white = mod(cu + cv, 2.0);
		float sweep = step(ang / TAU + 0.5, smoothstep(0.05, 0.4, t));
		float s1 = line(r - (R - 0.22), 0.022) * sweep;
		float s2 = line(r - (R - 0.29), 0.022) * sweep;
		float life = 1.0 - smoothstep(0.7, 1.0, t);
		a = (band * white * 0.85 + s1 + s2) * life;
		dk = (band * (1.0 - white) * 1.4 + band * 0.2 + halo(r - (R - 0.255), 0.06) * sweep * 0.5) * life;
		col = band > 0.5 ? col_accent.rgb : (s1 > s2 ? col_main.rgb : col_alt.rgb);
	} else if (kind == K_FROST) {
		// Frostbite: a six-armed ice fracture grows across the floor, a frosted disc with twinkling crystals under it.
		float sa = mod(ang + PI / 6.0, PI / 3.0) - PI / 6.0;
		vec2 p = vec2(cos(sa), abs(sin(sa))) * r;
		float L = smoothstep(0.0, 0.3, t) * 0.95;
		float arm = line(p.y, 0.02) * step(p.x, L);
		float br = 0.0;
		for (int i = 1; i < 4; i++) {
			float x0 = float(i) * 0.22;
			float bl = (0.3 - float(i) * 0.06) * smoothstep(x0, x0 + 0.15, L);
			vec2 b = vec2(x0, 0.0) + vec2(0.5, 0.866) * bl;
			br = max(br, line(seg(p, vec2(x0, 0.0), b), 0.014) * step(x0, L));
		}
		float frost = (1.0 - smoothstep(L * 0.8, L, r)) * 0.18;
		vec2 sp = floor(q * 14.0);
		float tw = step(0.93, h21(sp + seed)) * (0.5 + 0.5 * sin(TIME * 12.0 + h21(sp) * 40.0)) * step(r, L);
		float life = 1.0 - smoothstep(0.65, 1.0, t);
		a = (arm + br + frost + tw * 0.7) * life;
		dk = (halo(p.y, 0.05) * step(p.x, L) + halo(r - L, 0.06) * 0.4) * life;
		col = mix(col_main.rgb, col_accent.rgb, clamp(arm + tw, 0.0, 1.0));
	} else if (kind == K_ICESPIKES) {
		// Frostbite: a cluster of ice crystals shoots up from the floor (to ~1 m), holds, then shatters away.
		float best = 0.0;
		float edge = 0.0;
		float body = 0.0;
		for (int i = 0; i < 5; i++) {
			float fi = float(i);
			float x0 = (fi - 2.0) * 0.36 + (h11(fi) - 0.5) * 0.15;
			float h = (0.55 + 0.45 * h11(fi + 7.0)) * (fi == 2.0 ? 1.0 : 0.75);
			float grow = smoothstep(0.0, 0.12 + fi * 0.02, t) * h;
			float w = 0.13 * (0.7 + 0.3 * h11(fi + 3.0));
			float lean = (x0) * 0.25;
			float yy = q.y * 0.5 + 0.5;                 // 0 at the floor, 1 at the top
			float cx = x0 + lean * yy;
			float half_w = w * (1.0 - yy / max(grow * 2.0, 1e-3)) ;
			float dx = abs(q.x - cx) - max(half_w, 0.0);
			float inside = step(dx, 0.0) * step(yy, grow * 2.0);
			body = max(body, inside * (0.45 + 0.4 * yy));
			edge = max(edge, line(dx, 0.012) * step(yy, grow * 2.0) * step(0.0, half_w));
			best = max(best, halo(dx, 0.04) * step(yy, grow * 2.0 + 0.03));
		}
		float shat = smoothstep(0.6, 0.95, t);
		float crack = step(h21(floor(q * vec2(9.0, 14.0)) + seed), shat);
		float life = (1.0 - crack) * (1.0 - smoothstep(0.88, 1.0, t));
		a = (body * 0.6 + edge) * life;
		dk = best * 0.8 * life;
		col = mix(col_accent.rgb, col_main.rgb, body);
	} else if (kind == K_COMB) {
		// Hive: honeycomb cells around the feet fill with glowing amber from the centre outward, waxy rims, then drain.
		vec3 hg = hexgrid(q * 3.6);
		vec2 cc = hg.yz * vec2(1.0, 1.7320508) / 3.6;
		float rc = length(cc);
		float st = rc * 0.45 + h21(hg.yz + seed) * 0.08;
		float fill = smoothstep(st, st + 0.18, t) * (1.0 - smoothstep(0.7, 0.98, t));
		float in_r = step(rc, 0.85);
		float rim = line(hg.x - 0.03, 0.025) * in_r * smoothstep(st - 0.05, st + 0.05, t);
		float face = step(0.06, hg.x) * fill * in_r;
		float level = step(fract((q.y * 3.6) / 1.7320508 + 0.5) , mix(0.0, 1.0, fill) + 0.2);
		a = (face * (0.55 + 0.35 * level) + rim * 0.8) * (1.0 - smoothstep(0.85, 1.0, t));
		dk = (line(hg.x, 0.05) * 1.2 + 0.2) * in_r * smoothstep(st - 0.05, st + 0.1, t) * (1.0 - smoothstep(0.8, 1.0, t));
		col = mix(col_main.rgb, col_accent.rgb, rim);
	} else if (kind == K_POOL) {
		// Foundry: a molten pool spreads (white-hot centre, orange edge), crusts over in dark plates with glowing seams.
		float spread = smoothstep(0.0, 0.25, t);
		float wob = 0.12 * jag(ang / TAU + 0.5, 11.0, 2.0) + 0.05 * sin(ang * 3.0 + seed);
		float R = spread * (0.8 + wob);
		float inside = 1.0 - smoothstep(R - 0.03, R, r);
		float cool = smoothstep(0.35, 0.9, t);
		vec3 hg = hexgrid(q * 4.0 + vec2(jag(q.y * 0.5 + 0.5, 7.0, 3.0), jag(q.x * 0.5 + 0.5, 7.0, 8.0)) * 0.25);
		float seam = line(hg.x, 0.04 + 0.05 * (1.0 - cool));
		float crust = smoothstep(0.0, 0.25, hg.x) * cool;
		float heat = (1.0 - crust) * (0.7 + 0.3 * sin(TIME * 7.0 + r * 9.0));
		a = inside * (heat * 0.9 + seam * 0.6 * (1.0 - cool * 0.4)) * (1.0 - smoothstep(0.85, 1.0, t));
		a += line(r - R, 0.03) * (1.0 - cool) * spread;
		dk = (inside * (0.4 + crust * 1.1) + halo(r - R, 0.06) * 0.5) * (1.0 - smoothstep(0.85, 1.0, t));
		col = mix(col_main.rgb, col_accent.rgb, (1.0 - r / max(R, 0.01)) * (1.0 - cool));
	} else if (kind == K_CURTAIN || kind == K_CURTAIN_RING) {
		// Aurora: a curtain of northern light on a cylinder (unit mesh: radius 0.5, height 1). Vertical rays, a bright
		// green lower hem that ripples, violet towards the top. The finisher's curtain sweeps around the body (a moving
		// angular window); the kill's ring rises from the floor and fades.
		float an = atan(lp.z, lp.x);
		float y = lp.y + 0.5;
		float hem = 0.06 + 0.05 * sin(an * 5.0 + TIME * 2.2 + seed) + 0.03 * sin(an * 13.0 - TIME * 3.1);
		float fold = sin(an * 7.0 + sin(an * 3.0 + TIME * 0.9) * 2.0 + TIME * 0.7);
		float rays = 0.45 + 0.55 * pow(0.5 + 0.5 * sin(an * 41.0 + fold * 3.0 + TIME * 1.7), 3.0);
		float prof = smoothstep(hem - 0.02, hem + 0.03, y) * (1.0 - smoothstep(0.25, 1.0, y));
		float hemLine = exp(-pow((y - hem - 0.03) * 22.0, 2.0));
		float win;
		if (kind == K_CURTAIN) {
			float c = mix(-PI * 1.15, PI * 0.35, smoothstep(0.05, 0.85, t));   // sweeps across, right to left in the frame
			float da = abs(mod(an - c + PI, TAU) - PI);
			win = 1.0 - smoothstep(0.9, 2.2, da);
			win *= smoothstep(0.0, 0.1, t) * (1.0 - smoothstep(0.75, 1.0, t));
		} else {
			win = smoothstep(0.0, 0.15, t) * (1.0 - smoothstep(0.55, 1.0, t));
			prof *= step(y, smoothstep(0.0, 0.4, t) * 1.1);
		}
		a = (rays * prof * 0.75 + hemLine * 0.9) * win;
		dk = prof * 0.25 * win;
		col = mix(col_main.rgb, col_alt.rgb, smoothstep(0.15, 0.7, y));
		col = mix(col, col_accent.rgb, hemLine * 0.5);
	} else if (kind == K_GLOW) {
		// A soft ground glow with a thin ring (Aurora's floor light, Foundry's heat, Orrery's chart backing light).
		float g = exp(-r * r * 3.0);
		float e = env(t, 0.15, 0.6);
		a = (g * 0.55 + ring(r, 0.85 * smoothstep(0.0, 0.4, t), 0.015) * 0.6) * e * step(r, 1.0);
		dk = g * 0.35 * e;
		col = mix(col_main.rgb, col_alt.rgb, r);
	} else if (kind == K_ORBIT) {
		// Orrery: an orbit ring with degree ticks and a planet bead travelling on it (the node's basis tilts it).
		float draw = smoothstep(0.0, 0.18, t);
		float u = ang / TAU + 0.5;
		float shown = step(u, draw);
		float orbit = ring(r, 0.9, 0.012) * shown;
		float ticks = line(fract(u * 36.0) - 0.5, 0.05) * step(0.9, r) * step(r, 0.96) * shown * 0.7;
		float pa = seed + t * TAU * (1.2 + h11(2.0));
		vec2 pp = vec2(cos(pa), sin(pa)) * 0.9;
		float pd = length(q - pp);
		float planet = (1.0 - smoothstep(0.06, 0.08, pd)) + halo(pd - 0.07, 0.04) * 0.6;
		float life = 1.0 - smoothstep(0.85, 1.0, t);
		a = (orbit + ticks + planet * draw) * life;
		dk = (halo(r - 0.9, 0.035) * shown + halo(pd, 0.12) * draw) * 0.8 * life;
		col = mix(col_main.rgb, col_accent.rgb, planet);
	} else if (kind == K_CHART) {
		// Orrery: a star chart on the floor: rings with degree ticks, a compass star, a small constellation drawn in.
		float on = env(t, 0.12, 0.7);
		float rings = ring(r, 0.95, 0.012) + ring(r, 0.82, 0.008) + ring(r, 0.42, 0.006) * 0.6;
		float u = ang / TAU + 0.5;
		float ticks = line(fract(u * 72.0) - 0.5, 0.08) * step(0.82, r) * step(r, 0.95) * (0.5 + 0.5 * step(0.5, fract(u * 8.0 + 0.5) * 9.0 - 8.0));
		float rot = ang + t * 0.6;
		float star = max(0.0, 1.0 - r / (0.32 * pow(abs(cos(rot * 2.0)), 6.0) + 0.04));
		float con = 0.0;
		vec2 prev = vec2(0.0);
		for (int i = 0; i < 6; i++) {
			float fi = float(i);
			float sa = h11(fi + 20.0) * TAU;
			vec2 s = vec2(cos(sa), sin(sa)) * (0.45 + 0.3 * h11(fi + 30.0));
			con = max(con, (1.0 - smoothstep(0.02, 0.035, length(q - s))));
			if (i > 0) con = max(con, line(seg(q, prev, s), 0.006) * step(fi / 6.0, smoothstep(0.1, 0.6, t)) * 0.7);
			prev = s;
		}
		a = (rings + ticks + star * 0.9 + con) * on;
		dk = (1.0 - smoothstep(0.9, 1.0, r)) * 0.7 * on;
		col = mix(col_main.rgb, col_accent.rgb, star);
	} else if (kind == K_PULSE) {
		// A light pulse: a white core flash and an expanding ring (Orrery's alignment, Foundry's quench, Glasswork's flash).
		float core = exp(-r * r * 9.0) * (1.0 - smoothstep(0.0, 0.45, t));
		float rr = ring(r, smoothstep(0.0, 0.7, t) * 0.95, 0.025 + 0.04 * t) * (1.0 - smoothstep(0.5, 1.0, t));
		a = core * 1.2 + rr;
		dk = halo(r - smoothstep(0.0, 0.7, t) * 0.95, 0.06) * 0.5 * (1.0 - smoothstep(0.5, 1.0, t));
		col = mix(col_main.rgb, col_accent.rgb, core);
	} else if (kind == K_STREAM) {
		// Foundry: a molten pour (cylinder: radius 0.5, height 1 unit) runs down from the spout, flows, thins from the top.
		float y = lp.y + 0.5;
		float an = atan(lp.z, lp.x);
		float head = 1.0 - smoothstep(0.0, 0.12, t) * 1.05;          // the pour's front runs down
		float tail = smoothstep(0.7, 0.95, t) * 1.05;                 // and ends from the top
		float vis = step(head, y) * step(y, 1.0 - tail);
		float flow = 0.5 + 0.5 * sin(y * 26.0 + TIME * 14.0 + sin(an * 3.0 + y * 4.0) * 1.5);
		float crust = smoothstep(0.75, 0.95, 0.5 + 0.5 * sin(y * 11.0 + TIME * 9.0 + an * 2.0));
		a = vis * (0.7 + 0.3 * flow) * (1.0 - crust * 0.6);
		dk = vis * (0.35 + crust * 0.6);
		col = mix(col_main.rgb, col_accent.rgb, flow * (1.0 - crust));
	} else if (kind == K_BEAM) {
		// A vertical beam of light (Orrery's alignment).
		float x = q.x * msz.x * 0.5;
		float core = exp(-x * x / 0.0012) + exp(-x * x / 0.02) * 0.4;
		float vy = (1.0 - smoothstep(0.5, 1.0, q.y)) * smoothstep(-1.0, -0.8, q.y);
		float e = env(t, 0.08, 0.35);
		a = core * vy * e;
		dk = exp(-x * x / 0.08) * vy * e * 0.3;
		col = mix(col_main.rgb, col_accent.rgb, exp(-x * x / 0.0012));
	}

	// premultiplied: bright parts glow on top, the under-glow dims what is behind (readable on bright floors), and at
	// least 1 - ALPHA_MAX of the background always shows through
	float ca = clamp(a, 0.0, 1.0) * fade;
	float cd = clamp(dk * DARK, 0.0, 1.0) * fade;
	ALBEDO = col * ca * ENERGY;
	ALPHA = min(max(ca * 0.8, cd), ALPHA_MAX);
}
