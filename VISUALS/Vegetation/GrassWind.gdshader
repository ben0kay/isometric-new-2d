// Combines grass wind, optional biome colouring and player-contact bending.
// Shared short-grass materials disable brushing.
shader_type canvas_item;
render_mode blend_premul_alpha;

#include "res://VISUALS/Drawings/Vegetation/VegetationBrush.gdshaderinc"

uniform float wind_strength : hint_range(0.0, 8.0) = 2.8;
uniform float wind_speed : hint_range(0.0, 4.0) = 1.4;
uniform float brush_amount : hint_range(0.0, 8.0) = 2.5;
uniform bool biome_tint_enabled = false;

varying vec4 grass_colour;

// =========================================================
// Anchor grass bases and combine wind with optional contact bending.
void vertex() {
    grass_colour = COLOR;

    float phase = dot(MODEL_MATRIX[3].xy, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    float height_weight = clamp(-VERTEX.y / 46.0, 0.0, 1.0);
    vec2 push = vegetation_brush(
        MODEL_MATRIX, height_weight, brush_amount);

    VERTEX.x += sway * gust * wind_strength * height_weight;
    VERTEX.xy += push;
}

// =========================================================
// Preserve optional biome recolouring and premultiplied artwork alpha.
void fragment() {
    vec4 artwork = texture(TEXTURE, UV);
    vec3 colour = artwork.rgb;

    if (biome_tint_enabled) {
        float brightness = max(
            artwork.r, max(artwork.g, artwork.b));
        vec3 tinted = vec3(brightness) * grass_colour.rgb;

        colour = mix(artwork.rgb, tinted, grass_colour.a);
        COLOR = vec4(colour, artwork.a);
    } else {
        COLOR = artwork * grass_colour;
    }
}