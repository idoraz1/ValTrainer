// First-person viewmodel material (gun, hands, shells), factory finishes. The body is shared with the gun-skin
// variants: see viewmodel.gdshaderinc.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;
#include "res://assets/shaders/weapon/viewmodel.gdshaderinc"
