// Viewmodel gun skin, Low quality: one noise octave, no flake sparkle, no animation, less metal (no sky reflections).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;
#define SKIN
#define SKIN_LITE
#include "res://assets/shaders/weapon/viewmodel.gdshaderinc"
