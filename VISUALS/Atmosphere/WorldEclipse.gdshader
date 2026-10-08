// Darkens the rendered world consistently, including baked artwork and water.
// The helmet cone reveals the original artwork without changing HUD colours.
shader_type canvas_item;
render_mode unshaded;

#include "res://VISUALS/Lighting/WorldLighting.gdshaderinc"

uniform sampler2D world_screen : hint_screen_texture, repeat_disable, filter_nearest;

uniform vec2 screen_to_world_origin = vec2(0.0);
uniform vec2 screen_to_world_x = vec2(1.0, 0.0);
uniform vec2 screen_to_world_y = vec2(0.0, 1.0);

uniform bool flashlight_enabled = false;
uniform vec2 flashlight_position = vec2(0.0);
uniform vec2 flashlight_direction = vec2(0.0, 1.0);
uniform float flashlight_range = 600.0;
uniform float flashlight_half_angle = 0.38;
uniform float flashlight_strength = 1.0;

// =========================================================
// Build a softened cone with a small pool of light around the helmet.
float helmet_light(vec2 world_position) {
    if (!flashlight_enabled) {
        return 0.0;
    }

    vec2 difference = world_position - flashlight_position;
    float distance_to_light = length(difference);
    vec2 direction = difference / max(distance_to_light, 0.001);

    float outer_angle = flashlight_half_angle;
    float inner_angle = outer_angle * 0.7;

    float cone = smoothstep(
        cos(outer_angle), cos(inner_angle),
        dot(direction, flashlight_direction)
    );

    float range_fade = 1.0 - smoothstep(
        flashlight_range * 0.65,
        max(flashlight_range, 1.0),
        distance_to_light
    );

    float nearby = 1.0 - smoothstep(12.0, 65.0, distance_to_light);

    return clamp(
        max(cone * range_fade, nearby) * flashlight_strength,
        0.0, 1.0
    );
}

// =========================================================
// Convert screen pixels back into stable world coordinates before shading.
void fragment() {
    vec2 screen_pixel = SCREEN_UV / SCREEN_PIXEL_SIZE;
    vec2 world_position =
        screen_to_world_origin +
        screen_to_world_x * screen_pixel.x +
        screen_to_world_y * screen_pixel.y;

    vec4 original = texture(world_screen, SCREEN_UV);
    float darkness = eclipse_darkness(world_position);

    vec3 eclipse_tint = vec3(0.06, 0.085, 0.12);
    vec3 shaded = original.rgb * mix(vec3(1.0), eclipse_tint, darkness);
    shaded = mix(shaded, original.rgb, helmet_light(world_position));

    COLOR = vec4(shaded, original.a);
}