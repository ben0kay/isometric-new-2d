// Runtime only samples finished premultiplied pixels and applies current world light.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;
#include "res://VISUALS/Lighting/WorldSunlight.gdshaderinc"
uniform bool patch_lighting = true;
varying vec4 tint;
void vertex() { tint = COLOR; }
void fragment() {
    vec4 pixel = texture(TEXTURE, UV);
    pixel.rgb *= tint.rgb * tint.a;
    pixel.a *= tint.a;
    if (patch_lighting) pixel.rgb *= world_sunlight(vec3(0.0, 0.0, 1.0));
    COLOR = pixel;
}
