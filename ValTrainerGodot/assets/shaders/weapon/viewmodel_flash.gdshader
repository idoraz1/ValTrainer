// Muzzle flash sprite drawn in viewmodel space (same projection/depth trick as viewmodel.gdshader).
// Procedural: no texture. mode 0 = front star (camera-facing), 1 = side flame (quad along the barrel, UV.y = along).
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform float vm_focal = 1.428;
uniform float vm_depth = 0.01;
uniform int mode = 0;
uniform float intensity = 0.0;      // 0 = invisible
uniform float rot = 0.0;            // random rotation (radians) of the star
uniform float seed = 0.0;
uniform vec4 core_color : source_color = vec4(1.0, 0.93, 0.75, 1.0);
uniform vec4 edge_color : source_color = vec4(1.0, 0.48, 0.12, 1.0);
uniform float energy = 4.0;

void vertex() {
	vec4 vp;
	if (mode == 0) {
		// Camera-facing billboard around the node origin.
		vec4 c = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
		float s = length(MODEL_MATRIX[0].xyz);
		vp = c + vec4(VERTEX.xy * s, 0.0, 0.0);
	} else {
		vp = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	}
	mat4 p = PROJECTION_MATRIX;
	// Scale the focal terms (keeps the aspect, the API's axis signs and any jitter/offset terms).
	float k = vm_focal / abs(p[1][1]);
	p[0][0] *= k;
	p[1][1] *= k;
	vec4 clip = p * vp;
	float z1 = PROJECTION_MATRIX[2][2] * -1.0 + PROJECTION_MATRIX[3][2];
	float z2 = (PROJECTION_MATRIX[2][2] * -2.0 + PROJECTION_MATRIX[3][2]) * 0.5;
	float near_ndc = z1 > z2 ? 1.0 : 0.0;
	clip.z = mix(near_ndc * clip.w, clip.z, vm_depth);
	POSITION = clip;
}

float h11(float x) { return fract(sin(x * 127.1 + seed * 311.7) * 43758.5453); }

void fragment() {
	vec2 uv = UV * 2.0 - 1.0;
	float a = 0.0;
	vec3 col;
	if (mode == 0) {
		float cs = cos(rot), sn = sin(rot);
		uv = mat2(vec2(cs, -sn), vec2(sn, cs)) * uv;
		float r = length(uv);
		float ang = atan(uv.y, uv.x);
		// 5-7 uneven spikes
		float spikes = 0.0;
		for (int i = 0; i < 6; i++) {
			float fi = float(i);
			float sa = fi * 1.0472 + (h11(fi) - 0.5) * 0.7;
			float d = abs(mod(ang - sa + 3.14159, 6.28318) - 3.14159);
			float len = 0.5 + 0.4 * h11(fi + 9.0);
			float w = 0.22 + 0.12 * h11(fi + 3.0);
			spikes = max(spikes, smoothstep(w, 0.0, d * (0.5 + r * 1.6)) * smoothstep(len, len * 0.2, r));
		}
		float core = smoothstep(0.42, 0.0, r);
		float glow = exp(-r * r * 7.0) * 0.55;
		a = max(max(core, spikes), glow);
		col = mix(edge_color.rgb, core_color.rgb, clamp(core * 1.6, 0.0, 1.0));
	} else {
		// Side flame: UV.y 0 = muzzle, 1 = tip. Tapered, flickering lobes.
		float t = UV.y;
		float width = mix(0.55, 0.05, t) * (0.8 + 0.4 * h11(floor(t * 5.0) + 2.0));
		float x = abs(uv.x) / max(width, 0.02);
		float body = smoothstep(1.0, 0.25, x) * smoothstep(1.0, 0.55, t) * smoothstep(0.0, 0.08, t);
		a = body;
		col = mix(core_color.rgb, edge_color.rgb, clamp(t * 1.3 + x * 0.3, 0.0, 1.0));
	}
	ALBEDO = col * a * intensity * energy;
}
