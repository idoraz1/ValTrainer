// Recon reveal (initiator drills: Sova's Recon Bolt, Fade's Haunt). The revealed enemy is drawn ON TOP of everything —
// through walls — as a glowing silhouette with a bright rim and a slow scan shimmer, for as long as the reveal lasts.
// Chained in front of the enemy highlight overlay (MaterialOverlay) of every enemy mesh; per-bot instance uniforms:
// hl_reveal (0 = off: the pass collapses and draws nothing), hl_fade (death fade, shared with the enemy overlay).
shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, depth_draw_never, cull_back, shadows_disabled, ambient_light_disabled, fog_disabled;

uniform vec4 reveal_color : source_color = vec4(1.0, 0.25, 0.28, 1.0);
/// Interior glow (the rim is added on top).
uniform float fill = 0.16;
uniform float rim_power = 2.2;
instance uniform float hl_fade : instance_index(1) = 1.0;
instance uniform float hl_reveal : instance_index(2) = 0.0;

void vertex() {
	float a = hl_reveal * hl_fade;
	// Not revealed: push every vertex outside the clip volume so the pass costs (almost) nothing.
	POSITION = a > 0.001 ? PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)) : vec4(2.0, 2.0, 2.0, 1.0);
}

void fragment() {
	float a = clamp(hl_reveal * hl_fade, 0.0, 1.0);
	float ndv = clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0);
	float rim = pow(1.0 - ndv, rim_power);
	float scan = 0.82 + 0.18 * sin(TIME * 7.0 - FRAGCOORD.y * 0.12);
	ALBEDO = reveal_color.rgb * (fill + 1.7 * rim) * a * scan;
}
