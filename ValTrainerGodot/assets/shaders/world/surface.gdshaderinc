// Shared body of surface.gdshader / surface_lite.gdshader (LITE = Low preset: albedo + normal only).
// PBR set (albedo / normal GL / ARM) on box geometry whose UV is in world metres. The palette tint replaces
// the texture's average colour (keeps its detail), contrast can be reduced for a cleaner stylized look, and a
// large-scale world noise breaks visible tiling. UV2.x = height above the box base, UV2.y = 1 on vertical faces.
#if defined(LITE_LAMBERT) && defined(LITE_NOSPEC)
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert, specular_disabled;
#elif defined(LITE_LAMBERT)
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert, specular_schlick_ggx;
#elif defined(LITE_NOSPEC)
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_disabled;
#else
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;
#endif

uniform sampler2D tex_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D tex_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#ifndef LITE
uniform sampler2D tex_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D tex_macro : hint_default_white, filter_linear_mipmap, repeat_enable;
#endif

uniform vec3 tint : source_color = vec3(1.0);
uniform vec3 tex_mean : source_color = vec3(0.5);
uniform float tint_amount : hint_range(0.0, 1.0) = 0.8;
uniform float contrast : hint_range(0.0, 2.0) = 1.0;
uniform float tile_m = 2.0;
uniform float normal_depth = 1.0;
uniform float rough_scale = 1.0;
uniform float rough_offset = 0.0;
uniform float rough_mean = 0.85;
uniform float metal_scale = 1.0;
uniform float metal_mean = 0.0;
uniform float ao_strength = 1.0;
uniform float macro_strength = 0.1;
uniform float macro_tile_m = 13.0;
uniform float grime_height = 0.8;
uniform float grime_strength = 0.22;
uniform float specular_level : hint_range(0.0, 1.0) = 0.5;

void fragment() {
	vec2 uv = UV / tile_m;
	vec3 m = max(tex_mean, vec3(0.02));
	vec3 a = texture(tex_albedo, uv).rgb;
	a = max(m + (a - m) * contrast, vec3(0.0));
	a = mix(a, a * (tint / m), tint_amount);
	a *= COLOR.rgb;
	float g = UV2.y * (1.0 - smoothstep(0.0, grime_height, UV2.x));
#ifdef LITE
	a *= 1.0 - grime_strength * g;
	ROUGHNESS = clamp(rough_mean * rough_scale + rough_offset, 0.04, 1.0);
	METALLIC = clamp(metal_mean * metal_scale, 0.0, 1.0);
#else
	vec2 macro = texture(tex_macro, UV / macro_tile_m).rg;
	a *= 1.0 + macro_strength * (macro.x * 2.0 - 1.0);
	a *= 1.0 - grime_strength * g * (0.7 + 0.6 * macro.y);
	vec3 arm = texture(tex_arm, uv).rgb;
	AO = mix(1.0, arm.r, ao_strength);
	ROUGHNESS = clamp(arm.g * rough_scale + rough_offset + (macro.y - 0.5) * 0.1, 0.04, 1.0);
	METALLIC = clamp(arm.b * metal_scale, 0.0, 1.0);
#endif
	ALBEDO = clamp(a, vec3(0.0), vec3(1.0));
	SPECULAR = specular_level;
#ifndef LITE_NONORMAL
	NORMAL_MAP = texture(tex_normal, uv).rgb;
	NORMAL_MAP_DEPTH = normal_depth;
#endif
}
