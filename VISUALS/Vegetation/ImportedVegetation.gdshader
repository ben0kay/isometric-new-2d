// Combines imported foliage movement with the common world surface lighting.
shader_type canvas_item;
render_mode unshaded, blend_mix;

#include "res://VISUALS/Lighting/WorldSurface.gdshaderinc"
#include "res://VISUALS/Vegetation/VegetationBrush.gdshaderinc"

uniform bool wind_enabled = true;
uniform float wind_strength = 4.0;
uniform float wind_speed = 1.4;
uniform float brush_amount = 9.0;
uniform float image_height = 1024.0;
uniform float image_anchor_y = 0.96;

varying vec4 artwork_colour;
varying vec2 artwork_position;
varying vec2 normal_axis_x;
varying vec2 normal_axis_y;

// =========================================================
// Bend foliage in world units without moving its ground anchor.
void vertex() {
    artwork_colour = COLOR;
    artwork_position = VERTEX;
    normal_axis_x = normalize(MODEL_MATRIX[0].xy);
    normal_axis_y = normalize(MODEL_MATRIX[1].xy);

    mat2 axes = mat2(MODEL_MATRIX[0].xy, MODEL_MATRIX[1].xy);
    float height_weight = clamp(
        -VERTEX.y / max(image_height * image_anchor_y, 1.0),
        0.0, 1.0
    );

    float phase = dot(MODEL_MATRIX[3].xy, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    if (wind_enabled) {
        VERTEX += inverse(axes) * vec2(
            height_weight * sway * gust * wind_strength, 0.0
        );
    }

    VERTEX += vegetation_brush(
        MODEL_MATRIX, height_weight, brush_amount
    );
}

// =========================================================
// Apply the same surface lighting used by entities and world objects.
void fragment() {
    vec4 pixel = texture(TEXTURE, UV) * artwork_colour;
    pixel.rgb = world_surface(
        pixel.rgb, artwork_position, normal_axis_x, normal_axis_y
    );
    COLOR = pixel;
}