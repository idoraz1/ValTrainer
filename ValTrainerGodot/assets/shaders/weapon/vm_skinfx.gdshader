// Skin inspect flourish / special reload particles (Game.Skins.VmParticles: InspectFx, ReloadFx), one MultiMesh per
// viewmodel, drawn with the viewmodel projection and depth squeeze like viewmodel_flash.gdshader, so they sit exactly on the
// drawn gun at any viewmodel FOV and never go through walls. Additive, procedural: no texture.
// Per instance: COLOR = rgb colour, a = intensity (0 = invisible); INSTANCE_CUSTOM = (shape, spin radians, softness, seed).
// Shapes: 0 soft glow dot, 1 four-point glint, 2 streak (billboarded around the instance's local Y; length |Y|, width |X|),
// 3 ring (an annulus in the instance's XY plane, not billboarded), 4 six-point frost flake.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform float vm_focal = 1.428;
uniform float vm_depth = 0.01;
uniform float energy = 2.2;

varying vec4 v_col;
varying vec4 v_cus;

void vertex() {
	v_col = COLOR;
	v_cus = INSTANCE_CUSTOM;
	float shape = INSTANCE_CUSTOM.x;
	vec4 vp;
	if (shape > 2.5 && shape < 3.5) {
		vp = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	} else if (shape > 1.5 && shape < 2.5) {
		vec3 c = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
		vec3 ax = (MODELVIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
		float w = length((MODELVIEW_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xyz);
		vec3 side = cross(ax, normalize(c));
		float sl = length(side);
		side = sl > 1e-6 ? side / sl * w : vec3(w, 0.0, 0.0);
		vp = vec4(c + ax * VERTEX.y + side * VERTEX.x, 1.0);
	} else {
		vec3 c = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
		float s = length((MODELVIEW_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xyz);
		float cs = cos(INSTANCE_CUSTOM.y), sn = sin(INSTANCE_CUSTOM.y);
		vec2 q = mat2(vec2(cs, sn), vec2(-sn, cs)) * VERTEX.xy;
		vp = vec4(c + vec3(q * s, 0.0), 1.0);
	}
	mat4 p = PROJECTION_MATRIX;
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

void fragment() {
	vec2 uv = UV * 2.0 - 1.0;
	float shape = v_cus.x;
	float soft = max(v_cus.z, 0.05);
	float r = length(uv);
	float a;
	if (shape < 0.5) {
		a = exp(-r * r * 4.0 / soft);
	} else if (shape < 1.5) {
		float arms = max(exp(-abs(uv.x) * 18.0) * (1.0 - abs(uv.y)), exp(-abs(uv.y) * 18.0) * (1.0 - abs(uv.x)));
		a = max(arms, exp(-r * r * 22.0)) * smoothstep(1.0, 0.6, r);
	} else if (shape < 2.5) {
		float along = 1.0 - abs(uv.y);
		a = exp(-uv.x * uv.x * 5.0 / soft) * smoothstep(0.0, 0.35, along);
	} else if (shape < 3.5) {
		float band = 0.07 * soft;
		a = exp(-pow((r - 0.86) / band, 2.0));
		// ticks around the ring (a brass dial look): 24 marks
		float ang = atan(uv.y, uv.x);
		float tick = smoothstep(0.6, 1.0, cos(ang * 24.0)) * smoothstep(0.08, 0.0, abs(r - 0.76));
		a = max(a, tick * 0.7);
	} else {
		float ang = atan(uv.y, uv.x);
		float arm = abs(cos(ang * 3.0));
		float rays = pow(arm, 14.0) * smoothstep(1.0, 0.15, r);
		a = max(rays, exp(-r * r * 30.0));
	}
	ALBEDO = v_col.rgb * a * v_col.a * energy;
}
