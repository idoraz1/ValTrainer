// ValTrainer — controller smoke volume (original shader, CC0 like the rest of the project's own code).
// Ray-marched sphere of billowing smoke drawn on the back faces of a bounding sphere mesh, so it also renders while the
// camera is inside it. Depth testing is done here against the scene depth (not by the GPU), so a smoke that pokes through
// a wall is cut exactly at the wall and the front half never disappears.
// Look: dense core, soft billowy edge (3D noise displaces the boundary), slow swirl around the vertical axis and a slow
// rise, sun-side lighting from the path length through the sphere toward the sun, a tinted rim. Optional per-agent
// touches: sparkles (Astra), water ripples (Harbor), sound rings (Miks).
shader_type spatial;
render_mode unshaded, cull_front, depth_test_disabled, depth_draw_never, blend_mix, fog_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform sampler3D noise_tex : filter_linear, repeat_enable;

uniform vec3 light_color : source_color = vec3(0.78, 0.75, 0.72);
uniform vec3 dark_color : source_color = vec3(0.34, 0.31, 0.30);
uniform vec3 rim_color : source_color = vec3(1.0, 0.65, 0.4);
uniform vec3 sun_dir = vec3(0.35, 0.85, 0.25);     // direction TO the sun (normalized)
uniform vec3 sun_color : source_color = vec3(1.0, 0.95, 0.88);
uniform float sun_energy = 1.0;
uniform vec3 ambient : source_color = vec3(0.56, 0.6, 0.66);
uniform float brightness = 1.0;

uniform float radius = 4.1;      // current radius (grows / shrinks)
uniform float density = 3.2;     // extinction per metre in the core
uniform float edge = 0.9;        // soft-edge width (m)
uniform float swirl = 0.07;      // rad / s around the vertical axis
uniform float seed = 0.0;
uniform float fade = 1.0;        // overall opacity (0 = invisible, e.g. shader pre-warm)
uniform float thin = 0.0;        // 0..1 thinning (expiry warning flicker)
uniform int steps = 24;
uniform float sparkle = 0.0;
uniform float ripple = 0.0;
uniform float pulse = 0.0;

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec2 sphere_hit(vec3 ro, vec3 rd, vec3 c, float r) {
	vec3 oc = ro - c;
	float b = dot(oc, rd);
	float h = b * b - (dot(oc, oc) - r * r);
	if (h < 0.0) return vec2(1e9, -1e9);
	h = sqrt(h);
	return vec2(-b - h, -b + h);
}

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

void fragment() {
	vec3 c = NODE_POSITION_WORLD;
	vec3 ro = CAMERA_POSITION_WORLD;
	vec3 rd = normalize(v_world - ro);
	float R = max(radius, 0.05);
	vec2 hit = sphere_hit(ro, rd, c, R + edge * 0.6);
	float t0 = max(hit.x, 0.0);
	float t1 = hit.y;

	// Opaque scene depth along this ray.
	float d = texture(depth_tex, SCREEN_UV).r;
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	vec4 vp = INV_PROJECTION_MATRIX * vec4(vec3(SCREEN_UV, d) * 2.0 - 1.0, 1.0);
#else
	vec4 vp = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, d, 1.0); // reverse-Z NDC depth
#endif
	vp.xyz /= vp.w;
	float scene_t = length(vp.xyz);
	t1 = min(t1, scene_t);
	if (t1 <= t0 || fade <= 0.001) discard;

	int n = max(steps, 4);
	float dt = (t1 - t0) / float(n);
	float jitter = hash12(FRAGCOORD.xy + seed * 17.0);
	float t = t0 + dt * jitter;
	float T = 1.0;
	vec3 col = vec3(0.0);
	float ang0 = TIME * swirl + seed * 6.2831;
	float rise = TIME * 0.035;
	float dens_k = density * (1.0 - 0.45 * thin);
	vec3 sd = normalize(sun_dir);

	for (int i = 0; i < n; i++) {
		vec3 q = ro + rd * t - c;
		float r = length(q);
		// swirl: rotate around Y, a little faster near the top
		float a = ang0 + q.y * 0.09;
		float cs = cos(a), sn = sin(a);
		vec3 qs = vec3(cs * q.x - sn * q.z, q.y, sn * q.x + cs * q.z);
		vec3 np = qs * (0.095 / max(R / 4.1, 0.4)) + vec3(seed, seed * 1.7 - rise, seed * 0.37);
		float n1 = texture(noise_tex, np).r;
		float n2 = texture(noise_tex, np * 2.37 + vec3(0.31, 0.17, 0.53)).r;
		float nz = n1 * 0.68 + n2 * 0.32;
		float rr = r + (nz - 0.5) * edge * 1.9;
		if (pulse > 0.0) rr += pulse * 0.22 * sin(r * 3.2 - TIME * 5.5);
		float shape = smoothstep(R, R - edge, rr);
		float dens = shape * dens_k * (0.55 + 0.9 * nz);
		if (dens > 0.002) {
			// path length through the sphere toward the sun → self-shadowing
			float b2 = dot(q, sd);
			float s_exit = -b2 + sqrt(max(b2 * b2 - (r * r - R * R), 0.0));
			float lightT = exp(-s_exit * 0.42);
			float h = clamp(q.y / R * 0.5 + 0.5, 0.0, 1.0);
			vec3 base = mix(dark_color, light_color, clamp(lightT * 0.85 + h * 0.25, 0.0, 1.0));
			vec3 lit = base * (ambient * (0.75 + 0.35 * h) + sun_color * sun_energy * lightT * 0.75);
			if (ripple > 0.0) {
				float band = sin(q.y * 5.0 + nz * 9.0 - TIME * 2.2) * 0.5 + 0.5;
				lit += rim_color * ripple * 0.18 * band * lightT;
			}
			float shell = smoothstep(R - edge * 1.2, R - edge * 0.2, rr) * (1.0 - smoothstep(R - edge * 0.2, R, rr));
			lit += rim_color * shell * 0.16;
			if (sparkle > 0.0) {
				float s = texture(noise_tex, qs * 0.9 + vec3(TIME * 0.02, seed, 0.0)).r;
				lit += vec3(1.0, 0.9, 1.0) * sparkle * smoothstep(0.78, 0.86, s) * 2.5;
			}
			float alpha = 1.0 - exp(-dens * dt);
			col += T * alpha * lit;
			T *= 1.0 - alpha;
			if (T < 0.01) break;
		}
		t += dt;
	}
	float A = (1.0 - T) * fade;
	if (A < 0.002) discard;
	ALBEDO = col / max(1.0 - T, 1e-4) * brightness;
	ALPHA = clamp(A, 0.0, 1.0);
}
