// VALORANT-style enemy fresnel rim ("enemies have a red fresnel") + brief hit flash.
// Additive MaterialOverlay pass over every enemy mesh (skinned meshes supported). Depth-tested, so it never
// shows through walls. rim_strength = 0 when the player disables outlines/fresnel; the hit flash still works.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled, ambient_light_disabled, fog_disabled;

uniform vec4 rim_color : source_color = vec4(1.0, 0.22, 0.25, 1.0);
uniform float rim_strength = 0.6;
uniform float rim_power = 3.0;
/// Per-bot: hit flash (0..1) and highlight fade (death).
instance uniform float hl_flash : instance_index(0) = 0.0;
instance uniform float hl_fade : instance_index(1) = 1.0;

void fragment() {
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float rim = pow(1.0 - ndv, rim_power) * rim_strength * hl_fade;
	float flash = hl_flash * (0.35 + 0.65 * (1.0 - ndv));
	ALBEDO = rim_color.rgb * (rim + flash);
}
