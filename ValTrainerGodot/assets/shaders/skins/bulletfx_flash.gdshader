// Skin Bullet FX: the line's impact flash on a world surface (BulletFxWorld): a small camera-facing quad with the line's
// shape (bulletfx_shapes.gdshaderinc), additive, ~0.1 s. Pooled; driven by instance uniforms. Manual depth test against
// its centre like fx_flash.gdshader (hidden behind walls, not cut by the surface it sits on).
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, depth_test_disabled;

#include "res://assets/shaders/skins/bulletfx_shapes.gdshaderinc"

instance uniform float intensity = 0.0;
instance uniform float k = 0.0;
instance uniform float rot = 0.0;
instance uniform float seed = 0.0;
instance uniform int kind = 1;
instance uniform vec4 tint : source_color = vec4(1.0);
instance uniform vec4 tint2 : source_color = vec4(1.0);
instance uniform vec4 tint3 : source_color = vec4(1.0);
uniform float energy = 2.2;
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
	float z = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 vp = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, z, 1.0);
	float scene = -vp.z / vp.w;
	float vis = step(-center_view.z - 0.12, scene);
	vec2 uv = UV * 2.0 - 1.0;
	float cs = cos(rot), sn = sin(rot);
	uv = mat2(vec2(cs, -sn), vec2(sn, cs)) * uv;
	vec4 s = bfx_shape(kind, uv, k, seed, TIME, tint.rgb, tint2.rgb, tint3.rgb);
	ALBEDO = s.rgb * energy * intensity * vis;
}
