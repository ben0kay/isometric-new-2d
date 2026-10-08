// Adds optional normal maps and a rounded fallback to the shared sunlight.
// Local surface normals are transformed with the artwork, including mirroring.

#include "res://VISUALS/Lighting/WorldSunlight.gdshaderinc"

uniform bool surface_lighting_enabled = true;
uniform bool surface_normal_enabled = false;
uniform sampler2D surface_normal : hint_normal, repeat_disable, filter_linear;

uniform vec4 surface_rect = vec4(-0.5, -1.0, 1.0, 1.0);
uniform float surface_normal_strength = 1.0;
uniform float surface_shape_strength = 1.0;
uniform float surface_brightness = 1.0;

// =========================================================
// Shade the artwork without imposing a texture-specific sunlight direction.
vec3 world_surface(
    vec3 colour, vec2 local_position,
    vec2 normal_axis_x, vec2 normal_axis_y
) {
    if (!surface_lighting_enabled) {
        return colour * max(surface_brightness, 0.0);
    }

    vec2 artwork_uv = (
        local_position - surface_rect.xy
    ) / max(surface_rect.zw, vec2(0.001));

    vec2 slope = (artwork_uv - vec2(0.5)) * 2.0
        * max(wl_shape_strength, 0.0)
        * max(surface_shape_strength, 0.0);

    vec3 normal = normalize(vec3(slope, 1.0));

    if (surface_normal_enabled) {
        vec3 mapped = texture(surface_normal, artwork_uv).rgb * 2.0 - 1.0;
        mapped.y = -mapped.y;

        normal = normalize(vec3(
            mapped.xy * max(surface_normal_strength, 0.0),
            max(mapped.z, 0.001)
        ));
    }

    vec2 world_xy = normal_axis_x * normal.x
        + normal_axis_y * normal.y;

    vec3 world_normal = normalize(vec3(world_xy, normal.z));
    return colour * world_sunlight(world_normal)
        * max(surface_brightness, 0.0);
}