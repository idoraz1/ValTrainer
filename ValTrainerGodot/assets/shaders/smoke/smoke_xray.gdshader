// ValTrainer — targeting markers that show through walls (original shader): Omen / Harbor smoke marker (fresnel
// sphere outline), Astra's stars (billboard star sprite), the drop line under an aimed marker.
shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, blend_add, cull_back, fog_disabled;

uniform int kind = 0;            // 0 = fresnel sphere, 2 = star sprite (billboard quad), 4 = beam (quad, UV.x across)
uniform vec3 color : source_color = vec3(0.35, 0.6, 1.0);
uniform float energy = 1.0;
uniform float opacity = 1.0;

void vertex() {
	if (kind == 2) {
		// camera-facing quad of the node's scale
		MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
		MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
			vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	}
}

void fragment() {
	float a = 0.0;
	if (kind == 0) {
		float f = 1.0 - abs(dot(NORMAL, VIEW));
		a = pow(f, 3.0) * 0.9 + 0.05;
		a *= 0.75 + 0.25 * sin(TIME * 4.0);
	} else if (kind == 2) {
		vec2 p = UV * 2.0 - 1.0;
		float ang = atan(p.y, p.x);
		float r = length(p);
		float star = 0.32 + 0.22 * pow(abs(cos(ang * 2.5)), 6.0);
		a = smoothstep(star, star * 0.55, r) + 0.35 * exp(-r * r * 6.0);
		a *= 0.85 + 0.15 * sin(TIME * 3.0);
	} else {
		float x = abs(UV.x * 2.0 - 1.0);
		a = (1.0 - x) * (1.0 - x) * 0.8;
	}
	ALBEDO = color * energy;
	ALPHA = clamp(a * opacity, 0.0, 1.0);
}
