// Skin Bullet FX: the line's muzzle overlay, drawn on top of the (re-tinted) factory flash in viewmodel space (the same
// projection / depth trick as viewmodel_flash.gdshader, so it sits exactly on the barrel tip at any zoom). Camera-facing,
// additive, small and short (BulletFx: ~60 ms; the kill pulse ~0.35 s), never near the crosshair.
// mode 0 = the shot's flash (bulletfx_shapes.gdshaderinc), 1 = the gun-side kill pulse (a themed puff at the muzzle).
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

#include "res://assets/shaders/skins/bulletfx_shapes.gdshaderinc"

uniform float vm_focal = 1.428;
uniform float vm_depth = 0.01;
uniform int mode = 0;
uniform int kind = 1;
uniform float intensity = 0.0;      // 0 = invisible
uniform float k = 0.0;              // 0..1 over the flash / pulse life
uniform float rot = 0.0;
uniform float seed = 0.0;
uniform vec4 col_main : source_color = vec4(1.0, 0.6, 0.2, 1.0);
uniform vec4 col_accent : source_color = vec4(1.0, 0.9, 0.7, 1.0);
uniform vec4 col_alt : source_color = vec4(0.6, 0.8, 1.0, 1.0);
uniform float energy = 3.0;

void vertex() {
	vec4 c = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	float s = length(MODEL_MATRIX[0].xyz);
	vec4 vp = c + vec4(VERTEX.xy * s, 0.0, 0.0);
	mat4 p = PROJECTION_MATRIX;
	float f = vm_focal / abs(p[1][1]);
	p[0][0] *= f;
	p[1][1] *= f;
	vec4 clip = p * vp;
	float z1 = PROJECTION_MATRIX[2][2] * -1.0 + PROJECTION_MATRIX[3][2];
	float z2 = (PROJECTION_MATRIX[2][2] * -2.0 + PROJECTION_MATRIX[3][2]) * 0.5;
	float near_ndc = z1 > z2 ? 1.0 : 0.0;
	clip.z = mix(near_ndc * clip.w, clip.z, vm_depth);
	POSITION = clip;
}

void fragment() {
	vec2 uv = UV * 2.0 - 1.0;
	float cs = cos(rot), sn = sin(rot);
	uv = mat2(vec2(cs, -sn), vec2(sn, cs)) * uv;
	vec3 add;
	if (mode == 0) {
		vec4 s = bfx_shape(kind, uv, k, seed, TIME, col_main.rgb, col_accent.rgb, col_alt.rgb);
		add = s.rgb;
	} else {
		// kill pulse: a soft ring opening round the muzzle, with the line's touch: embers rise (basalt, foundry), sparkles
		// twinkle (glass, frost, aurora, orrery), a crackle (thunder, oscilloscope) or the stripes (rally)
		float r = length(uv);
		float rr = 0.25 + 0.6 * k;
		float ring = exp(-pow((r - rr) * 14.0, 2.0)) * (1.0 - k);
		float glow = exp(-r * r * 6.0) * (1.0 - k) * 0.4;
		float extra = 0.0;
		float ang = atan(uv.y, uv.x);
		if (kind == 3 || kind == 8) {
			for (int i = 0; i < 6; i++) {
				float fi = float(i);
				vec2 e = vec2((bfx_h(fi, seed) - 0.5) * 0.9, -0.3 + k * (0.6 + 0.6 * bfx_h(fi + 5.0, seed)));
				extra += exp(-dot(uv - e, uv - e) * 600.0);
			}
		} else if (kind == 2 || kind == 4) {
			extra = bfx_soft(abs(r - rr - 0.05 * sin(ang * 13.0 + TIME * 70.0)), 0.03) * (1.0 - k);
		} else if (kind == 5) {
			extra = (bfx_soft(abs(uv.y - 0.12), 0.03) + bfx_soft(abs(uv.y + 0.12), 0.03)) * step(abs(uv.x), rr) * (1.0 - k);
		} else {
			vec2 cell = floor((uv + 1.0) * 6.0);
			extra = step(0.86, bfx_h(cell.x * 13.0 + cell.y, seed)) * (0.5 + 0.5 * sin(TIME * 40.0 + cell.x)) * step(r, rr + 0.1) * (1.0 - k);
		}
		add = mix(col_main.rgb, col_accent.rgb, 0.3) * (ring + glow) + col_accent.rgb * extra;
	}
	ALBEDO = add * intensity * energy;
}
