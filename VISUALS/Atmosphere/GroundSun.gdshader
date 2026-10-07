// Blends shared ground surfaces using cached biome settings.
// Mountain rock and paths retain existing terrain coordinates and lighting.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

#include "res://VISUALS/Drawings/Ground/BasaltSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/DustSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/MineralSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/GrassSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/GreyRockSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/StonyPathSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/GroundResource.gdshaderinc"

uniform sampler2D ground_noise : repeat_enable, filter_linear_mipmap;
uniform float dust_amount : hint_range(0.0, 1.0) = 0.55;
uniform float mineral_amount : hint_range(0.0, 1.0) = 0.32;
uniform float detail_strength : hint_range(0.0, 1.0) = 0.35;

uniform bool biome_ground_enabled = false;
uniform sampler2D biome_settings_map : repeat_disable, filter_linear;
uniform sampler2D biome_shade_map : repeat_disable, filter_linear;
uniform sampler2D biome_main_map : repeat_disable, filter_linear;
uniform sampler2D biome_mountain_map : repeat_disable, filter_linear;
uniform vec2 biome_map_origin = vec2(0.0);
uniform vec2 biome_map_size = vec2(1.0);

uniform sampler2D mist_texture : repeat_enable, filter_linear;
uniform vec2 sun_direction = vec2(-1.0, -0.7);
uniform vec4 sun_color : source_color = vec4(1.0, 0.96, 0.87, 1.0);
uniform float sun_strength = 2.2;

varying vec2 terrain_position;
varying vec2 world_position;
varying vec4 surface_color;

// =========================================================
// Preserve terrain coordinates and existing height shading.
void vertex() {
    terrain_position = UV;
    world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    surface_color = COLOR;
}

// =========================================================
// Blend shared ground surfaces without changing terrain geometry.
void fragment() {
    vec2 p = terrain_position;

    vec4 settings = vec4(
        dust_amount, mineral_amount, detail_strength, 0.0);
    vec4 shade = vec4(0.0, 0.0, 0.0, 32.0);
    vec4 main_tint = vec4(0.0);
    vec2 mountain_masks = vec2(0.0);

    if (biome_ground_enabled) {
        vec2 map_uv = (
            p - biome_map_origin + vec2(0.5)) / biome_map_size;

        settings = texture(biome_settings_map, map_uv);
        shade = texture(biome_shade_map, map_uv);
        main_tint = texture(biome_main_map, map_uv);
        mountain_masks = texture(biome_mountain_map, map_uv).rg;
    }

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

    float dust_threshold = mix(0.85, 0.30, settings.r);
    float dust_mask = smoothstep(
        dust_threshold - 0.12, dust_threshold + 0.12, dust_field);
    dust_mask *= smoothstep(0.0, 0.08, settings.r);

    float mineral_threshold = mix(0.88, 0.38, settings.g);
    float mineral_mask = smoothstep(
        mineral_threshold - 0.07,
        mineral_threshold + 0.09, mineral_field);
    mineral_mask *= smoothstep(0.0, 0.08, settings.g);

    vec3 stone = basalt_surface(strata, detail, grain);
    vec3 dust = dust_surface(strata, detail, grain);
    vec3 ground = mix(stone, dust, dust_mask);
    ground = mineral_surface(
        ground, strata, detail,
        mineral_mask * (1.0 - dust_mask * 0.75));

    float grass_mask = 0.0;
    float patch_scale = 32.0 / max(shade.a, 4.0);
    vec2 patch_uv = p * 0.0039 * patch_scale;

    if (settings.a > 0.0001) {
        float grass_field = texture(
            ground_noise, patch_uv + vec2(0.29, 0.61)).r;
        float threshold = mix(0.90, 0.10, settings.a);

        grass_mask = smoothstep(
            threshold - 0.14, threshold + 0.14, grass_field);
        grass_mask *= smoothstep(0.0, 0.08, settings.a);
        grass_mask = mix(
            grass_mask, 1.0, smoothstep(0.90, 1.0, settings.a));

        vec3 grass = grass_surface(
            strata, detail, grain, shade.rgb, main_tint.rgb);
        ground = mix(ground, grass, grass_mask);
    }

    if (main_tint.a > 0.0001) {
        vec2 warp = vec2(
            texture(
                ground_noise, patch_uv * 0.7 + vec2(0.13, 0.39)).r,
            texture(
                ground_noise, patch_uv * 0.7 + vec2(0.71, 0.17)).r
        ) - vec2(0.5);

        float channel = texture(
            ground_noise,
            patch_uv * 0.8 + warp * 0.16 + vec2(0.61, 0.28)).r;

        float mud_mask = 1.0 - smoothstep(
            0.035, 0.060, abs(channel - 0.5));
        mud_mask *= main_tint.a;

        vec3 mud = mix(
            vec3(0.035, 0.036, 0.032),
            vec3(0.078, 0.071, 0.056), strata);
        mud += vec3((grain - 0.5) * 0.007);

        ground = mix(ground, mud, mud_mask);
    }

    ground = mix(
        ground,
        grey_rock_surface(strata, detail, grain),
        clamp(mountain_masks.r, 0.0, 1.0));

    ground = mix(
        ground,
        stony_path_surface(strata, detail, grain),
        clamp(mountain_masks.g, 0.0, 1.0));

    ground = ground_resources(ground, p, detail, grain);

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
    relief *= settings.b * (1.0 - dust_mask * 0.70);
    relief *= 1.0 - grass_mask * 0.35;

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