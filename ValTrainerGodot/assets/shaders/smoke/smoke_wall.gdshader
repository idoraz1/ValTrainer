// ValTrainer — gas / water wall (Viper's Toxic Screen, Harbor's High Tide). Original shader.
// Drawn on a few parallel vertical strips; UV.x = metres along the wall, UV.y = 0 at the floor .. 1 at the current top.
// Noise drifts up (gas) or sideways (water) and eats a ragged, soft top edge; water adds foam and streaks.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, fog_disabled;

uniform sampler3D noise_tex : filter_linear, repeat_enable;
uniform vec3 light_color : source_color = vec3(0.45, 0.75, 0.38);
uniform vec3 dark_color : source_color = vec3(0.1, 0.26, 0.1);
uniform vec3 rim_color : source_color = vec3(0.7, 1.0, 0.4);
uniform float opacity = 0.62;
uniform float rise = 0.22;       // upward drift (gas)
uniform float flow = 0.0;        // sideways flow (water)
uniform float water = 0.0;
uniform float seed = 0.0;
uniform float fade = 1.0;
uniform float brightness = 1.0;

void fragment() {
	float along = UV.x;
	float hgt = UV.y;
	vec3 np = vec3(along * 0.16 - TIME * flow, hgt * 0.75 - TIME * rise, seed);
	float n = texture(noise_tex, np).r * 0.66 + texture(noise_tex, np * 2.3 + vec3(0.37, 0.11, 0.5)).r * 0.34;
	float top = 1.0 - smoothstep(0.62 + 0.25 * n, 1.0, hgt);
	float a = opacity * top * (0.72 + 0.56 * n);
	vec3 col = mix(dark_color, light_color, clamp(hgt * 0.55 + n * 0.65, 0.0, 1.0));
	if (water > 0.0) {
		float streak = texture(noise_tex, vec3(along * 0.45 - TIME * flow * 2.0, hgt * 3.0, seed + 0.5)).r;
		col += rim_color * water * (smoothstep(0.78, 0.98, hgt + n * 0.12) * 0.7 + smoothstep(0.62, 0.8, streak) * 0.25);
		a = max(a, opacity * 0.55 * top);
	} else {
		col += rim_color * smoothstep(0.8, 1.0, n) * 0.25;
	}
	if (a * fade < 0.003) discard;
	ALBEDO = col * brightness;
	ALPHA = clamp(a * fade, 0.0, 1.0);
}
