// Applies the same lighting to premultiplied baked world artwork.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

#include "res://VISUALS/Lighting/WorldSurface.gdshaderinc"

varying vec4 artwork_colour;
varying vec2 artwork_position;
varying vec2 normal_axis_x;
varying vec2 normal_axis_y;

// =========================================================
// Retain surface coordinates and modulation before texture sampling.
void vertex() {
    artwork_colour = COLOR;
    artwork_position = VERTEX;
    normal_axis_x = normalize(MODEL_MATRIX[0].xy);
    normal_axis_y = normalize(MODEL_MATRIX[1].xy);
}

// =========================================================
// Fade premultiplied colour and alpha together before illumination.
void fragment() {
    vec4 pixel = texture(TEXTURE, UV) * artwork_colour;
    pixel.rgb *= artwork_colour.a;

    pixel.rgb = world_surface(
        pixel.rgb, artwork_position, normal_axis_x, normal_axis_y
    );

    COLOR = pixel;
}