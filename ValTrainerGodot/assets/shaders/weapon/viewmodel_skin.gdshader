// Viewmodel gun skin (GunSkins.cs): the viewmodel shader plus one procedural pattern chosen by a uniform. Medium and up.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;
#define SKIN
#include "res://assets/shaders/weapon/viewmodel.gdshaderinc"
