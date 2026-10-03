// ValTrainer — "inside a smoke" screen fog. Original shader.
// The smoke volume itself already hides everything beyond a metre or two; this adds the feeling of being inside:
// the screen edges blur (screen-texture mip) and fade toward the smoke's tint with a slow drifting noise, while the
// centre (your gun, the crosshair area) stays sharp. amount = 0 → hidden by the caller.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform sampler3D noise_tex : filter_linear, repeat_enable;
uniform vec3 tint : source_color = vec3(0.5, 0.5, 0.5);
uniform float amount = 0.0;
uniform float aspect = 1.7778;

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 p = (uv - 0.5) * vec2(aspect, 1.0);
	float r = length(p);
	float vig = smoothstep(0.28, 1.05, r);
	vec3 sharp = texture(screen_tex, uv).rgb;
	vec3 blur = textureLod(screen_tex, uv, 3.0 * amount).rgb;
	vec3 scene = mix(sharp, blur, vig);
	float n = texture(noise_tex, vec3(p * 0.55, TIME * 0.035)).r;
	float k = amount * (0.12 + 0.7 * vig + 0.18 * (n - 0.5));
	vec3 fogc = tint * (0.8 + 0.4 * n);
	COLOR = vec4(mix(scene, fogc, clamp(k, 0.0, 0.9)), 1.0);
}
