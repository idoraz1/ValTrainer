// VALORANT-look aim target: a dark neutral body, the enemy-colour fresnel rim (same formula and strength as the
// bots' enemy_overlay.gdshader) and a crisp outline in the enemy colour, so drill targets carry the same visual
// signature as VALORANT enemies and the bots: luminance edge first, hue second.
// - The sphere is ray-traced per pixel against the EXACT hit sphere (Target.Ray: node origin, radius = node scale).
//   The mesh is only a slightly inflated proxy and pixels outside the true sphere are discarded, so the drawn
//   silhouette is the hitbox (the faceted mesh used to be a little smaller than it).
// - The outline is drawn INSIDE the silhouette (unlike the bots' outer hull), so it never makes the target look
//   bigger than it is. It stays ~outline_px wide at 1080p at any distance (scaled with the viewport height).
// - hot = tracking highlight (body lights up in the enemy colour), flash = white hit flash.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform vec3 body_color : source_color = vec3(0.17, 0.18, 0.2);
uniform vec3 enemy_color : source_color = vec3(1.0, 0.2, 0.2);
uniform float rim_strength = 0.6;
uniform float rim_power = 3.0;
/// Outline width in pixels at 1080p (never below 1 px).
uniform float outline_px = 2.0;
uniform float outline_energy = 1.6;
uniform float hot : hint_range(0.0, 1.0) = 0.0;
uniform float flash : hint_range(0.0, 1.0) = 0.0;

void vertex() {
	// The unit SphereMesh's flat facets dip up to ~1% inside the true sphere: cover them, the fragment trims back.
	VERTEX *= 1.03;
}

void fragment() {
	vec3 c = (VIEW_MATRIX * vec4(NODE_POSITION_WORLD, 1.0)).xyz;
	float r = length(MODEL_MATRIX[0].xyz);
	bool ortho = PROJECTION_MATRIX[3][3] > 0.5; // directional shadow pass
	vec3 ro = ortho ? vec3(VERTEX.xy, 0.0) : vec3(0.0);
	vec3 rd = ortho ? vec3(0.0, 0.0, -1.0) : normalize(VERTEX);
	vec3 oc = c - ro;
	float b = dot(oc, rd);
	vec3 perp = oc - rd * b;
	float p2 = dot(perp, perp);
	if (p2 > r * r) {
		discard;
	}
	// Front hit = ro + rd * (b - s), so hit - c = -rd * s - perp.
	vec3 n = normalize(-rd * sqrt(r * r - p2) - perp);
	float ndv = clamp(dot(n, -rd), 0.0, 1.0);

	// Distance to the silhouette in pixels: angular gap × pixels per radian (with a mild off-axis correction).
	float edge = 1e6;
	if (!ortho) {
		float L = length(oc);
		float ang = asin(min(r / L, 1.0)) - asin(min(sqrt(p2) / L, 1.0));
		float px_per_rad = abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y * 0.5 / max(-rd.z, 0.2); // abs: the RD projection is Y-flipped
		edge = ang * px_per_rad;
	}
	float w = max(1.0, outline_px * VIEWPORT_SIZE.y / 1080.0);
	float band = 1.0 - smoothstep(w - 0.5, w + 0.5, edge);

	float rim = pow(1.0 - ndv, rim_power) * rim_strength;
	vec3 body = mix(body_color, vec3(1.0), flash);
	NORMAL = n;
	ALBEDO = body * (1.0 - band);
	ROUGHNESS = 0.42;
	SPECULAR = 0.5 * (1.0 - band);
	EMISSION = enemy_color * (rim * (1.0 + hot) + band * outline_energy + hot * 0.22);
}
