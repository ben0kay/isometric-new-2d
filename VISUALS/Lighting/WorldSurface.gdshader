// Applies shared directional lighting to ordinary world sprites and drawings.
shader_type canvas_item;
render_mode unshaded, blend_mix;

#include "res://VISUALS/Lighting/WorldSurface.gdshaderinc"

varying vec4 artwork_colour;
varying vec2 artwork_position;
varying vec2 normal_axis_x;
varying vec2 normal_axis_y;

// =========================================================
// Retain the undeformed surface and its world orientation.
void vertex() {
    artwork_colour = COLOR;
    artwork_position = VERTEX;
    normal_axis_x = normalize(MODEL_MATRIX[0].xy);
    normal_axis_y = normalize(MODEL_MATRIX[1].xy);
}

// =========================================================
// Illuminate the surface while preserving ordinary sprite opacity.
void fragment() {
    vec4 pixel = texture(TEXTURE, UV) * artwork_colour;

    pixel.rgb = world_surface(
        pixel.rgb, artwork_position, normal_axis_x, normal_axis_y
    );

    COLOR = pixel;
}