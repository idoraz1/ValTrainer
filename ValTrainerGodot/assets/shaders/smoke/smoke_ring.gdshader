// ValTrainer — world-space smoke indicators (original shader): the landing ring on the floor while a smoke deploys
// (kind 1, flat quad, UV centre = ring centre) and the glowing projectile / emitter (kind 3, sphere).
shader_type spatial;
render_mode unshaded, depth_draw_never, blend_add, cull_back, fog_disabled;

uniform int kind = 1;
uniform vec3 color : source_color = vec3(1.0, 0.6, 0.3);
uniform float energy = 1.0;
uniform float opacity = 1.0;
uniform float progress = 0.0;    // ring: 0 → 1 while deploying (inner sweep)

void fragment() {
	float a = 0.0;
	if (kind == 1) {
		float r = length(UV * 2.0 - 1.0);
		float ring = smoothstep(0.86, 0.93, r) * (1.0 - smoothstep(0.96, 1.0, r));
		float fill = (1.0 - smoothstep(progress - 0.08, progress, r)) * 0.18 * step(r, 1.0);
		float ticks = step(0.5, fract(atan(UV.y - 0.5, UV.x - 0.5) / 6.2831 * 24.0 + TIME * 0.3)) * smoothstep(0.78, 0.82, r) * (1.0 - smoothstep(0.84, 0.86, r));
		a = ring + fill + ticks * 0.6;
	} else {
		float f = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
		a = 0.55 + 0.45 * f;
	}
	ALBEDO = color * energy;
	ALPHA = clamp(a * opacity, 0.0, 1.0);
}
