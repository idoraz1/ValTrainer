// Bullet tracer: camera-facing ribbon along the node's -Z axis (scale.z = length).
// A bright head streak travels from the muzzle to the hit while a faint full-length trail fades.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

instance uniform float head = 0.0;        // head position along the beam (m)
instance uniform float fade = 1.0;        // overall fade 0..1
instance uniform vec4 tint : source_color = vec4(1.0, 0.78, 0.42, 1.0);
uniform float streak = 5.0;               // head streak length (m)
uniform float energy = 1.6;
uniform float px_width = 0.0026;          // width per metre of distance (≈ 2 px at 1080p)

varying float along;
varying float across;

void vertex() {
	vec3 a = MODEL_MATRIX[3].xyz;
	vec3 zaxis = MODEL_MATRIX[2].xyz;
	float len = length(zaxis);
	vec3 dir = -zaxis / max(len, 1e-4);
	float t = -VERTEX.z;                  // 0..1 along the beam
	vec3 p = a + dir * (t * len);
	vec3 tc = CAMERA_POSITION_WORLD - p;
	float d = length(tc);
	vec3 side = normalize(cross(dir, tc / max(d, 1e-4)));
	float w = clamp(d * px_width, 0.0015, 0.05);
	p += side * VERTEX.x * w * 2.0;
	along = t * len;
	across = VERTEX.x * 2.0;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
}

void fragment() {
	float core = exp(-across * across * 5.0);
	float behind = head - along;
	float h = behind >= 0.0 ? exp(-behind / streak * 2.5) : 0.0;
	float trail = (behind >= 0.0 ? 0.18 : 0.0) * fade;
	float start = smoothstep(0.2, 2.5, along);    // soft start right at the muzzle
	float a = (h * fade + trail) * core * start;
	ALBEDO = tint.rgb * energy * a;
}
