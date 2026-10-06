// Combines existing plant wind with subtle player-contact bending.
// Ground contact remains anchored.
shader_type canvas_item;
render_mode blend_premul_alpha;

#include "res://VISUALS/Drawings/Vegetation/VegetationBrush.gdshaderinc"

uniform float wind_strength : hint_range(0.0, 24.0) = 10.0;
uniform float wind_speed : hint_range(0.0, 4.0) = 1.4;
uniform float brush_amount : hint_range(0.0, 16.0) = 6.0;

// =========================================================
// Move upper foliage with wind and a restrained shared brushing effect.
void vertex() {
    vec2 location = MODEL_MATRIX[3].xy;
    float phase = dot(location, vec2(0.017, 0.011));
    float time = TIME * wind_speed;

    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    float wind_height = -VERTEX.y / 180.0;
    float brush_height = clamp(wind_height, 0.0, 1.0);

    vec2 push = vegetation_brush(
        MODEL_MATRIX, brush_height, brush_amount);

    VERTEX.x += wind_height * sway * gust * wind_strength;
    VERTEX.xy += push;
}