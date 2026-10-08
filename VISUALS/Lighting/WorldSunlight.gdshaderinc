// Supplies one illumination calculation for terrain, water and world artwork.
// Eclipse darkness is applied once by the existing world screen compositor.

global uniform vec3 wl_sun_direction;
global uniform vec4 wl_sun_color : source_color;
global uniform float wl_ambient;
global uniform float wl_sun_strength;
global uniform float wl_shape_strength;

// =========================================================
// Illuminate a surface whose normal is expressed in world coordinates.
vec3 world_sunlight(vec3 normal) {
    float diffuse = max(dot(
        normalize(normal),
        normalize(wl_sun_direction)
    ), 0.0);

    return vec3(max(wl_ambient, 0.0))
        + wl_sun_color.rgb * max(wl_sun_strength, 0.0) * diffuse;
}