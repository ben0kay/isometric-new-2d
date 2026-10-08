// Builds biome ground surfaces and shades their encoded terrain normals.
// All illumination comes from the shared world lighting profile.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

#include "res://VISUALS/Lighting/WorldSunlight.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/BasaltSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/DustSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/MineralSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/GrassSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/StonyPathSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/SteepRockSurface.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/GroundResource.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/MudSurface.gdshaderinc"

uniform sampler2D ground_noise : repeat_enable, filter_linear_mipmap;
uniform sampler2D mist_texture : repeat_enable, filter_linear;
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

uniform sampler2D terrain_slope_map : repeat_disable, filter_linear;
uniform vec2 terrain_slope_origin = vec2(0.0);
uniform vec2 terrain_slope_size = vec2(1.0);

varying vec2 terrain_position;
varying vec4 surface_data;

// =========================================================
// Preserve absolute tile coordinates and encoded surface normals.
void vertex() {
    terrain_position = UV;
    surface_data = COLOR;
}

// =========================================================
// Build material colour first, then apply the common sunlight calculation.
void fragment() {
    vec2 p = terrain_position;
    vec4 settings = vec4(
        dust_amount, mineral_amount, detail_strength, 0.0
    );
    vec4 shade = vec4(0.0, 0.0, 0.0, 32.0);
    vec4 main_tint = vec4(0.0);
    float path_mask = 0.0;
    float steep_mask = 0.0;
    vec2 slope_gradient = vec2(0.0);

    if (biome_ground_enabled) {
        vec2 map_uv = (
            p - biome_map_origin + vec2(0.5)
        ) / biome_map_size;

        settings = texture(biome_settings_map, map_uv);
        shade = texture(biome_shade_map, map_uv);
        main_tint = texture(biome_main_map, map_uv);
        path_mask = texture(biome_mountain_map, map_uv).g;

        vec2 slope_uv = (
            p - terrain_slope_origin
        ) / terrain_slope_size;
        vec2 pixels = terrain_slope_size * 2.0;
        vec2 cell_uv = (
            floor(slope_uv * pixels) + vec2(0.5)
        ) / pixels;

        vec4 cell_slope = texture(terrain_slope_map, cell_uv);
        vec4 soft_slope = texture(terrain_slope_map, slope_uv);
        steep_mask = max(cell_slope.a, soft_slope.a * 0.35);
        slope_gradient = soft_slope.gb;
    }

    float dust_field = texture(
        ground_noise, p * 0.0039 + vec2(0.17, 0.43)
    ).r;
    float mineral_field = texture(
        ground_noise, p * 0.0061 + vec2(0.68, 0.21)
    ).r;
    float strata = texture(
        ground_noise, p * 0.035 + vec2(0.31, 0.74)
    ).r;
    float detail = texture(
        ground_noise, p * 0.22 + vec2(0.53, 0.11)
    ).r;
    float grain = texture(
        ground_noise, p * 2.7 + vec2(0.09, 0.62)
    ).r;

    float dust_threshold = mix(0.85, 0.30, settings.r);
    float dust_mask = smoothstep(
        dust_threshold - 0.12, dust_threshold + 0.12, dust_field
    );
    dust_mask *= smoothstep(0.0, 0.08, settings.r);

    float mineral_threshold = mix(0.88, 0.38, settings.g);
    float mineral_mask = smoothstep(
        mineral_threshold - 0.07,
        mineral_threshold + 0.09,
        mineral_field
    );
    mineral_mask *= smoothstep(0.0, 0.08, settings.g);

    vec3 stone = basalt_surface(strata, detail, grain);
    vec3 dust = dust_surface(strata, detail, grain);
    vec3 ground = mix(stone, dust, dust_mask);

    ground = mineral_surface(
        ground, strata, detail,
        mineral_mask * (1.0 - dust_mask * 0.75)
    );

    float grass_mask = 0.0;
    float patch_scale = 32.0 / max(shade.a, 4.0);
    vec2 patch_uv = p * 0.0039 * patch_scale;

    if (settings.a > 0.0001) {
        float grass_field = texture(
            ground_noise, patch_uv + vec2(0.29, 0.61)
        ).r;
        float threshold = mix(0.90, 0.10, settings.a);

        grass_mask = smoothstep(
            threshold - 0.14, threshold + 0.14, grass_field
        );
        grass_mask *= smoothstep(0.0, 0.08, settings.a);
        grass_mask = mix(
            grass_mask, 1.0, smoothstep(0.90, 1.0, settings.a)
        );

        ground = mix(
            ground,
            grass_surface(
                strata, detail, grain, shade.rgb, main_tint.rgb
            ),
            grass_mask
        );
    }

    ground = mud_surface(
        ground, ground_noise, patch_uv, main_tint.a, strata, grain
    );
    ground = mix(
        ground, stony_path_surface(strata, detail, grain),
        clamp(path_mask, 0.0, 1.0)
    );

    if (steep_mask > 0.001) {
        ground = mix(
            ground,
            steep_rock_surface(p, slope_gradient, detail, grain),
            clamp(steep_mask, 0.0, 1.0)
        );
    }

    ground = ground_resources(ground, p, detail, grain);

    vec2 direction = wl_sun_direction.xy;
    direction /= max(length(direction), 0.001);

    vec2 plane_direction = vec2(
        direction.x + direction.y * 2.0,
        -direction.x + direction.y * 2.0
    );
    plane_direction /= max(length(plane_direction), 0.001);

    float sunward_detail = texture(
        ground_noise,
        p * 0.22 + vec2(0.53, 0.11)
            + plane_direction * 0.003
    ).r;

    float relief = clamp(
        (detail - sunward_detail) * 7.0, -0.18, 0.18
    );
    relief *= settings.b * (1.0 - dust_mask * 0.70);
    relief *= 1.0 - grass_mask * 0.35;

    vec3 normal = normalize(surface_data.rgb * 2.0 - 1.0);
    vec3 result = ground * world_sunlight(normal) * (1.0 + relief);

    COLOR = vec4(result * surface_data.a, surface_data.a);
}