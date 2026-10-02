// Bot muzzle flash: additive, camera-facing star-burst quad (procedural, no texture).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, ambient_light_disabled, fog_disabled;

uniform vec4 flash_color : source_color = vec4(1.0, 0.72, 0.35, 1.0);
instance uniform float seed = 0.0;

void vertex() {
	// Billboard (keep the node's scale).
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float a = atan(p.y, p.x) + seed * 6.2831;
	float spikes = pow(abs(cos(a * 3.0)), 6.0) * 0.6 + 0.4;
	float core = exp(-r * r * 18.0);
	float burst = (1.0 - smoothstep(0.0, 1.0, r / spikes)) * 0.8;
	float k = core * 1.6 + burst;
	ALBEDO = flash_color.rgb * k + vec3(1.0, 0.95, 0.85) * core * 0.8;
}
