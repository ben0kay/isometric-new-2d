// Applies rock texture during the initial atlas bake only.
shader_type canvas_item;
render_mode unshaded, blend_mix;

uniform sampler2D rock_noise : repeat_enable, filter_linear;
uniform vec2 seed_offset = vec2(0.0);

#include "res://VISUALS/Drawings/Props/RockSurface.gdshaderinc"

varying vec2 rock_position;
varying vec4 face_color;

// =========================================================
// Preserve the drawing's local coordinates and sun-shaded face colors.
void vertex() {
    rock_position = VERTEX;
    face_color = COLOR;
}

// =========================================================
// Bake stone detail into the visible rock polygons.
void fragment() {
    COLOR = vec4(rock_surface(face_color.rgb, rock_position), face_color.a);
}