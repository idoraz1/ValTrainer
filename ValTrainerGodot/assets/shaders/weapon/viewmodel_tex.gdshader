// First-person viewmodel material for modelled (textured) skins: see TEXTURED in viewmodel.gdshaderinc.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;
#define TEXTURED
#include "res://assets/shaders/weapon/viewmodel.gdshaderinc"
