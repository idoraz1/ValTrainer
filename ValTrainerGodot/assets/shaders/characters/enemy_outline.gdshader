// VALORANT-style enemy highlight outline (silhouette only, NOT a wallhack).
// Drawn as a MaterialOverlay next_pass on every enemy mesh (works with skinned meshes: VERTEX/NORMAL are
// already skinned). The hull is pushed out in SCREEN space so the line stays ~width_px wide at any distance.
// - stencil: the character's base materials write stencil 1, so this pass only lands OUTSIDE the
//   silhouette (no interior lines between body parts / gun).
// - depth test stays on (transparent pass, depth_draw_never), so walls and cover hide the outline.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, ambient_light_disabled, fog_disabled;
stencil_mode read, compare_not_equal, 1;

uniform vec4 outline_color : source_color = vec4(1.0, 0.22, 0.25, 1.0);
/// Line width in pixels at 1080p (scaled with the viewport height, never below 1 px).
uniform float width_px = 1.6;
/// Per-bot fade (death).
instance uniform float hl_fade : instance_index(1) = 1.0;

void vertex() {
	vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec3 vn = MODELVIEW_NORMAL_MATRIX * NORMAL;
	vec2 n = (PROJECTION_MATRIX * vec4(vn, 0.0)).xy * VIEWPORT_SIZE; // direction in pixel space
	float len = length(n);
	n = len > 1e-6 ? n / len : vec2(0.0);
	float w = max(1.0, width_px * VIEWPORT_SIZE.y / 1080.0) * step(0.001, hl_fade);
	clip.xy += n * w * 2.0 / VIEWPORT_SIZE * clip.w;
	POSITION = clip;
}

void fragment() {
	ALBEDO = outline_color.rgb;
	ALPHA = outline_color.a * hl_fade;
}
