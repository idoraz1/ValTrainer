// Small camera-facing impact flash / glow (additive, procedural star). Pooled; driven by instance uniforms.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, depth_test_disabled;

instance uniform float intensity = 0.0;
instance uniform float rot = 0.0;
instance uniform vec4 tint : source_color = vec4(1.0, 0.75, 0.4, 1.0);
uniform float energy = 3.0;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

varying vec3 center_view;

void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], vec4(c, 1.0));
	VERTEX *= s;
	center_view = (VIEW_MATRIX * vec4(c, 1.0)).xyz;
}

void fragment() {
	// Manual depth test against the flash centre (so it is hidden behind walls but not cut by the surface it sits on).
	float z = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 vp = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, z, 1.0);
	float scene = -vp.z / vp.w;
	float vis = step(-center_view.z - 0.12, scene);
	vec2 uv = UV * 2.0 - 1.0;
	float cs = cos(rot), sn = sin(rot);
	uv = mat2(vec2(cs, -sn), vec2(sn, cs)) * uv;
	float r = length(uv);
	float ang = atan(uv.y, uv.x);
	float spikes = pow(abs(cos(ang * 2.0)), 18.0) * smoothstep(1.0, 0.1, r);
	float core = exp(-r * r * 14.0);
	float glow = exp(-r * r * 3.5) * 0.35;
	float a = (core + spikes * 0.7 + glow) * intensity * vis;
	ALBEDO = mix(tint.rgb, vec3(1.0, 0.97, 0.9), core * 0.6) * energy * a;
}
