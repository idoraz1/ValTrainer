// First-person viewmodel material (gun, hands, shells).
// - Re-projects the mesh with its own vertical FOV (vm_focal = 1/tan(fov/2)) so the gun keeps its size
//   when the world camera zooms, and squeezes its depth into the first 1% next to the near plane, so it is
//   always drawn in front of the world and never clips into walls.
// - Lighting stays physically placed: VERTEX/NORMAL are the real view-space values, so the scene's
//   directional light, sky ambient, reflections and omni lights (muzzle flash) shade it normally.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 albedo : source_color = vec4(0.2, 0.2, 0.2, 1.0);
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.6;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
uniform float detail : hint_range(0.0, 1.0) = 0.0;      // procedural micro-variation (wear / grain)
uniform float detail_scale = 90.0;
uniform float rim : hint_range(0.0, 1.0) = 0.0;         // subtle back-light rim for readability
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_energy = 0.0;
uniform float vm_focal = 1.428;          // 1 / tan(viewmodel_vfov / 2)
uniform float vm_depth = 0.01;           // fraction of the depth range the viewmodel may use
uniform float flash = 0.0;               // muzzle-flash light boost (0..1), adds warm fill to the front
uniform vec3 clip_min = vec3(0.0);       // object-space box hidden while clip_on (magazine out during reload)
uniform vec3 clip_max = vec3(0.0);
uniform float clip_on = 0.0;

varying vec3 obj_pos;

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float vnoise(vec3 p) {
	vec3 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash13(i), hash13(i + vec3(1, 0, 0)), f.x), mix(hash13(i + vec3(0, 1, 0)), hash13(i + vec3(1, 1, 0)), f.x), f.y),
	           mix(mix(hash13(i + vec3(0, 0, 1)), hash13(i + vec3(1, 0, 1)), f.x), mix(hash13(i + vec3(0, 1, 1)), hash13(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
	obj_pos = VERTEX;
	vec4 vp = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	mat4 p = PROJECTION_MATRIX;
	// Scale the focal terms (keeps the aspect, the API's axis signs and any jitter/offset terms).
	float k = vm_focal / abs(p[1][1]);
	p[0][0] *= k;
	p[1][1] *= k;
	vec4 clip = p * vp;
	// Which end of the depth range is "near"? (reverse-Z in Forward+: near = 1)
	float z1 = PROJECTION_MATRIX[2][2] * -1.0 + PROJECTION_MATRIX[3][2];
	float z2 = (PROJECTION_MATRIX[2][2] * -2.0 + PROJECTION_MATRIX[3][2]) * 0.5;
	float near_ndc = z1 > z2 ? 1.0 : 0.0;
	clip.z = mix(near_ndc * clip.w, clip.z, vm_depth);
	POSITION = clip;
}

void fragment() {
	if (clip_on > 0.5 && all(greaterThan(obj_pos, clip_min)) && all(lessThan(obj_pos, clip_max))) {
		discard;
	}
	vec3 col = albedo.rgb;
	float r = roughness;
	float m = metallic;
	if (detail > 0.0) {
		float n1 = vnoise(obj_pos * detail_scale);
		float n2 = vnoise(obj_pos * detail_scale * 4.3 + 7.1);
		float n = n1 * 0.65 + n2 * 0.35;
		col *= 1.0 + (n - 0.5) * 0.35 * detail;
		r = clamp(r + (n2 - 0.5) * 0.30 * detail, 0.05, 1.0);
	}
	ALBEDO = col;
	METALLIC = m;
	ROUGHNESS = r;
	SPECULAR = specular;
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	vec3 e = emission_color.rgb * emission_energy;
	e += col * rim * pow(1.0 - ndv, 3.0) * 0.6;
	e += col * vec3(1.0, 0.62, 0.3) * flash * 0.8 * (0.35 + 0.65 * ndv);
	EMISSION = e;
}
