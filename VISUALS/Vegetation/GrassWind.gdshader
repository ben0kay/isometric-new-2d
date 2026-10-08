// Combines grass movement, biome recolouring and common world illumination.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

#include "res://VISUALS/Lighting/WorldSurface.gdshaderinc"
#include "res://VISUALS/Vegetation/VegetationBrush.gdshaderinc"

uniform float wind_strength : hint_range(0.0, 8.0) = 2.8;
uniform float wind_speed : hint_range(0.0, 4.0) = 1.4;
uniform float brush_amount : hint_range(0.0, 8.0) = 2.5;
uniform bool biome_tint_enabled = false;

varying vec4 grass_colour;
varying vec2 artwork_position;
varying vec2 normal_axis_x;
varying vec2 normal_axis_y;

// =========================================================
// Combine wind and optional player contact while keeping the base anchored.
void vertex() {
    grass_colour = COLOR;
    artwork_position = VERTEX;
    normal_axis_x = normalize(MODEL_MATRIX[0].xy);
    normal_axis_y = normalize(MODEL_MATRIX[1].xy);

    float phase = dot(MODEL_MATRIX[3].xy, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    float height_weight = clamp(-VERTEX.y / 46.0, 0.0, 1.0);
    VERTEX.x += sway * gust * wind_strength * height_weight;
    VERTEX += vegetation_brush(
        MODEL_MATRIX, height_weight, brush_amount
    );
}

// =========================================================
// Recolour grass before applying the same light as other world surfaces.
void fragment() {
    vec4 artwork = texture(TEXTURE, UV);
    vec4 pixel;

    if (biome_tint_enabled) {
        float brightness = max(artwork.r, max(artwork.g, artwork.b));
        vec3 tinted = vec3(brightness) * grass_colour.rgb;
        pixel = vec4(
            mix(artwork.rgb, tinted, grass_colour.a),
            artwork.a
        );
    } else {
        pixel = artwork * grass_colour;
        pixel.rgb *= grass_colour.a;
    }

    pixel.rgb = world_surface(
        pixel.rgb, artwork_position, normal_axis_x, normal_axis_y
    );
    COLOR = pixel;
}