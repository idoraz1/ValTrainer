// ValTrainer sky: HDRI panorama with its sun hot-spot clamped out (the DirectionalLight3D is the sun, so
// ambient/reflections are not lit twice), a stylized sun disc drawn at LIGHT0, a grade (tint/saturation)
// and a ground colour below the horizon (bounce light for undersides, hides the HDRI's fake lower half).
shader_type sky;

uniform sampler2D panorama : filter_linear, source_color, hint_default_black;
uniform float exposure = 1.0;
uniform float clamp_lum = 5.0;
uniform float saturation = 1.0;
uniform vec3 tint : source_color = vec3(1.0);
uniform vec3 ground_color : source_color = vec3(0.32, 0.3, 0.27);
uniform float ground_energy = 1.0;
uniform float sun_cos = 0.99985;
uniform float sun_energy = 30.0;
uniform float sun_glow = 1.0;

void sky() {
	vec3 c = texture(panorama, SKY_COORDS).rgb;
	float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
	c *= min(1.0, clamp_lum / max(l, 1e-4));
	l = dot(c, vec3(0.2126, 0.7152, 0.0722));
	c = max(mix(vec3(l), c, saturation), vec3(0.0)) * tint * exposure;
	c = mix(c, ground_color * ground_energy, smoothstep(0.0, -0.1, EYEDIR.y));
	if (!AT_CUBEMAP_PASS && LIGHT0_ENABLED) {
		float d = max(dot(EYEDIR, LIGHT0_DIRECTION), 0.0);
		float disc = smoothstep(sun_cos - 0.00008, sun_cos + 0.00002, d);
		float glow = pow(d, 900.0) * 1.5 + pow(d, 60.0) * 0.18;
		c += LIGHT0_COLOR * (disc * sun_energy + glow * sun_glow);
	}
	COLOR = c;
}
