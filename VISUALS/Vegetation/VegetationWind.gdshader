// Combines baked plant movement, contact bending and shared surface lighting.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

#include "res://VISUALS/Lighting/WorldSurface.gdshaderinc"
#include "res://VISUALS/Vegetation/VegetationBrush.gdshaderinc"

uniform float wind_strength : hint_range(0.0, 24.0) = 10.0;
uniform float wind_speed : hint_range(0.0, 4.0) = 1.4;
uniform float brush_amount : hint_range(0.0, 16.0) = 6.0;

varying vec4 artwork_colour;
varying vec2 artwork_position;
varying vec2 normal_axis_x;
varying vec2 normal_axis_y;

// =========================================================
// Move upper foliage while retaining its original surface coordinates.
void vertex() {
    artwork_colour = COLOR;
    artwork_position = VERTEX;
    normal_axis_x = normalize(MODEL_MATRIX[0].xy);
    normal_axis_y = normalize(MODEL_MATRIX[1].xy);

    float phase = dot(MODEL_MATRIX[3].xy, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    float wind_height = -VERTEX.y / 180.0;
    float brush_height = clamp(wind_height, 0.0, 1.0);

    VERTEX.x += wind_height * sway * gust * wind_strength;
    VERTEX += vegetation_brush(
        MODEL_MATRIX, brush_height, brush_amount
    );
}

// =========================================================
// Preserve premultiplied fading and apply the common illumination.
void fragment() {
    vec4 pixel = texture(TEXTURE, UV) * artwork_colour;
    pixel.rgb *= artwork_colour.a;
    pixel.rgb = world_surface(
        pixel.rgb, artwork_position, normal_axis_x, normal_axis_y
    );
    COLOR = pixel;
}