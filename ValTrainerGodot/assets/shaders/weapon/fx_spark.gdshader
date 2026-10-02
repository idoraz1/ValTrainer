// Spark streak for GPUParticles3D draw passes. The process material aligns the particle Y axis with its
// velocity (particle_flag_align_y); the quad is stretched along it and turned to face the camera.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

instance uniform vec4 tint : source_color = vec4(1.0, 0.7, 0.35, 1.0);
instance uniform float boost = 1.0;
instance uniform float size = 1.0;       // width/length multiplier
instance uniform float whiten = 0.35;    // hot-white core amount
uniform float energy = 6.0;
uniform float width = 0.009;
uniform float streak_len = 0.12;

varying float life;
varying vec2 q;

void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 ax = MODEL_MATRIX[1].xyz;
	float s = length(ax);
	ax = ax / max(s, 1e-5);
	vec3 tc = normalize(CAMERA_POSITION_WORLD - c);
	vec3 side = normalize(cross(ax, tc));
	vec3 p = c + side * VERTEX.x * width * 2.0 * s * size + ax * (VERTEX.y - 0.5) * streak_len * s * size;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
	life = INSTANCE_CUSTOM.y;
	q = VERTEX.xy * 2.0;
}

void fragment() {
	float across = exp(-q.x * q.x * 4.0);
	float along = smoothstep(-1.0, 0.2, q.y) * smoothstep(1.0, 0.6, q.y);   // hot head, tapered tail
	float f = 1.0 - smoothstep(0.35, 1.0, life);
	vec3 hot = mix(tint.rgb, vec3(1.0, 0.95, 0.85), whiten * (1.0 - life));
	ALBEDO = hot * energy * boost * across * along * f * COLOR.a;
}
