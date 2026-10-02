// ValTrainer world surface, Low/Competitive preset: albedo only (no ARM/normal sampling, constant roughness),
// Lambert diffuse, no specular. Measured ≈0.6 ms cheaper per 1080p frame than the full variant on a GTX 1050-class GPU.
shader_type spatial;
#define LITE
#define LITE_LAMBERT
#define LITE_NOSPEC
#define LITE_NONORMAL
#include "res://assets/shaders/world/surface.gdshaderinc"
