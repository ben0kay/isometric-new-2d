shader_type canvas_item;
render_mode unshaded, blend_mix;
#include "res://VISUALS/Drawings/Ground/RockPatchSurface.gdshaderinc"
uniform float patch_seed = 0.0;
uniform float pebble_density = 0.55;
uniform vec4 soil_colour : source_color = vec4(0.24, 0.20, 0.15, 1.0);
uniform vec4 stone_colour : source_color = vec4(0.40, 0.39, 0.35, 1.0);
void fragment() {
    COLOR = rock_patch_surface(UV, patch_seed, pebble_density, soil_colour.rgb, stone_colour.rgb);
}
