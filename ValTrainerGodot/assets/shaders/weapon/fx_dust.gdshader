// Soft dust puff for GPUParticles3D (camera-facing billboard, tinted by the surface colour).
shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

instance uniform vec4 tint : source_color = vec4(0.6, 0.58, 0.55, 1.0);
uniform float opacity = 0.8;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

varying float life;
varying float rot;

void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], vec4(c, 1.0));
	VERTEX *= s;
	life = INSTANCE_CUSTOM.y;
	rot = INSTANCE_CUSTOM.x;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

void fragment() {
	vec2 uv = UV * 2.0 - 1.0;
	float cs = cos(rot), sn = sin(rot);
	uv = mat2(vec2(cs, -sn), vec2(sn, cs)) * uv;
	float r = length(uv);
	float lump = 0.85 + 0.15 * sin(atan(uv.y, uv.x) * 5.0 + rot * 3.0);
	float a = smoothstep(1.0, 0.25, r / lump);
	float f = smoothstep(0.0, 0.12, life) * (1.0 - smoothstep(0.3, 1.0, life));
	// soft intersection with the wall
	float z = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 vp = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, z, 1.0);
	float scene = -vp.z / vp.w;
	float soft = clamp((scene - (-VERTEX.z)) / 0.08, 0.0, 1.0);
	ALBEDO = tint.rgb * (1.15 + 0.3 * (1.0 - r));
	ALPHA = a * f * opacity * soft * COLOR.a;
}
