// Renders ordinary imported PNGs with shared sunlight, wind and brushing.
// Eclipse darkness remains handled by the existing world screen pass.
shader_type canvas_item;
render_mode unshaded, blend_mix;

#include "res://VISUALS/Vegetation/VegetationBrush.gdshaderinc"

uniform bool lighting_enabled = true;
uniform bool normal_enabled = false;
uniform sampler2D surface_normal : hint_normal, repeat_disable, filter_linear;
uniform float normal_strength = 0.65;
uniform float brightness = 0.8;

uniform vec2 sun_direction = vec2(-1.0, -0.7);
uniform vec4 sun_color : source_color = vec4(1.0, 0.96, 0.87, 1.0);
uniform float ambient_strength = 0.65;
uniform float sunlight_strength = 0.55;

uniform bool wind_enabled = true;
uniform float wind_strength = 4.0;
uniform float wind_speed = 1.4;
uniform float brush_amount = 9.0;
uniform float image_height = 1024.0;
uniform float image_anchor_y = 0.96;

varying vec4 artwork_modulate;
varying vec2 normal_axis_x;
varying vec2 normal_axis_y;

// =========================================================
// Move foliage in world units while retaining the sprite's ground anchor.
void vertex() {
    artwork_modulate = COLOR;

    mat2 axes = mat2(MODEL_MATRIX[0].xy, MODEL_MATRIX[1].xy);
    normal_axis_x = normalize(MODEL_MATRIX[0].xy);
    normal_axis_y = normalize(MODEL_MATRIX[1].xy);

    float height_weight = clamp(
        -VERTEX.y / max(image_height * image_anchor_y, 1.0),
        0.0, 1.0);

    vec2 location = MODEL_MATRIX[3].xy;
    float phase = dot(location, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    if (wind_enabled) {
        vec2 world_wind = vec2(
            height_weight * sway * gust * wind_strength, 0.0);
        VERTEX += inverse(axes) * world_wind;
    }

    VERTEX += vegetation_brush(
        MODEL_MATRIX, height_weight, brush_amount);
}

// =========================================================
// Apply approximate or mapped surface normals without changing sprite opacity.
void fragment() {
    vec4 pixel = texture(TEXTURE, UV) * artwork_modulate;

    if (lighting_enabled) {
        // Rounded fallback is an approximation, not individual leaf geometry.
        vec2 slope = (UV - vec2(0.5)) * normal_strength;
        vec3 surface = normalize(vec3(slope, 1.0));

        if (normal_enabled) {
            vec3 mapped = texture(surface_normal, UV).rgb * 2.0 - 1.0;
            // Normal-map green points upward; canvas Y points downward.
            mapped.y = -mapped.y;
            surface = normalize(vec3(
                mapped.xy * normal_strength,
                max(mapped.z, 0.001)));
        }

        // Account for mirrored artwork and its rotation.
        vec2 world_normal = normal_axis_x * surface.x
            + normal_axis_y * surface.y;
        surface = normalize(vec3(world_normal, surface.z));

        vec3 light = normalize(vec3(sun_direction, 0.75));
        float facing = max(dot(surface, light), 0.0);

        vec3 illumination = vec3(max(ambient_strength, 0.0))
            + sun_color.rgb * max(sunlight_strength, 0.0) * facing;

        pixel.rgb *= illumination;
    }

    pixel.rgb *= max(brightness, 0.0);
    COLOR = pixel;
}