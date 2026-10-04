// Renders smooth, continuous basalt, dust and mineral terrain.
// Applies broad sunlight with restrained surface detail.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

#include "res://VISUALS/Drawings/Ground/BasaltSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/DustSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/MineralSurface.gdshaderinc"

uniform sampler2D ground_noise : repeat_enable, filter_linear_mipmap;
uniform float dust_amount : hint_range(0.0, 1.0) = 0.55;
uniform float mineral_amount : hint_range(0.0, 1.0) = 0.32;
uniform float detail_strength : hint_range(0.0, 1.0) = 0.35;

uniform sampler2D mist_texture : repeat_enable, filter_linear;
uniform vec2 sun_direction = vec2(-1.0, -0.7);
uniform vec4 sun_color : source_color = vec4(1.0, 0.96, 0.87, 1.0);
uniform float sun_strength = 2.2;

varying vec2 terrain_position;
varying vec2 world_position;
varying vec4 surface_color;

// =========================================================
// Preserve continuous terrain coordinates and terrain height shading.
void vertex() {
    terrain_position = UV;
    world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    surface_color = COLOR;
}

// =========================================================
// Blend ground materials and apply soft sunlight with subtle relief.
void fragment() {
    vec2 p = terrain_position;

    float dust_field = texture(
        ground_noise, p * 0.0039 + vec2(0.17, 0.43)).r;
    float mineral_field = texture(
        ground_noise, p * 0.0061 + vec2(0.68, 0.21)).r;
    float strata = texture(
        ground_noise, p * 0.035 + vec2(0.31, 0.74)).r;
    float detail = texture(
        ground_noise, p * 0.22 + vec2(0.53, 0.11)).r;
    float grain = texture(
        ground_noise, p * 2.7 + vec2(0.09, 0.62)).r;

    float dust_threshold = mix(0.85, 0.30, dust_amount);
    float dust_mask = smoothstep(
        dust_threshold - 0.12, dust_threshold + 0.12, dust_field);
    dust_mask *= smoothstep(0.0, 0.08, dust_amount);

    float mineral_threshold = mix(0.88, 0.38, mineral_amount);
    float mineral_mask = smoothstep(
        mineral_threshold - 0.07, mineral_threshold + 0.09, mineral_field);
    mineral_mask *= smoothstep(0.0, 0.08, mineral_amount);

    vec3 stone = basalt_surface(strata, detail, grain);
    vec3 dust = dust_surface(strata, detail, grain);
    vec3 ground = mix(stone, dust, dust_mask);
    ground = mineral_surface(
        ground, strata, detail, mineral_mask * (1.0 - dust_mask * 0.75));

    // Convert sunlight into the logical isometric plane.
    vec2 direction = sun_direction / max(length(sun_direction), 0.001);
    vec2 plane_direction = vec2(
        direction.x + direction.y * 2.0,
        -direction.x + direction.y * 2.0);
    plane_direction /= max(length(plane_direction), 0.001);

    float sunward_detail = texture(
        ground_noise,
        p * 0.22 + vec2(0.53, 0.11) + plane_direction * 0.003).r;
    float relief = clamp(
        (detail - sunward_detail) * 7.0, -0.18, 0.18);
    relief *= detail_strength * (1.0 - dust_mask * 0.70);

    float light_field = texture(
        mist_texture, world_position * 0.00035).r;
    float sunlight = smoothstep(0.20, 0.80, light_field);
    float strength = sun_strength * mix(0.20, 0.62, sunlight);

    vec3 ambient = ground * vec3(0.72, 0.78, 0.86);
    vec3 lit = ground * sun_color.rgb * strength;
    vec3 result = (ambient + lit) * (1.0 + relief);
    result *= surface_color.rgb;

    COLOR = vec4(result * surface_color.a, surface_color.a);
}