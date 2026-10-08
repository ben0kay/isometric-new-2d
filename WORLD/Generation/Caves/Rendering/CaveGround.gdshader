// Builds neutral cave rock using the shared cave ambient-light setting.
shader_type canvas_item;
render_mode unshaded;

#include "res://VISUALS/Lighting/WorldSunlight.gdshaderinc"
#include "res://VISUALS/Drawings/Ground/GreyRockSurface.gdshaderinc"

// =========================================================
// Produce stable cave surface variation.
float cave_hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// =========================================================
// Smooth seeded lattice variation.
float cave_noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    return mix(
        mix(cave_hash(cell), cave_hash(cell + vec2(1.0, 0.0)), f.x),
        mix(
            cave_hash(cell + vec2(0.0, 1.0)),
            cave_hash(cell + vec2(1.0, 1.0)),
            f.x
        ),
        f.y
    );
}

// =========================================================
// Shade the cave floor with ambient light and no surface sun.
void fragment() {
    float strata = cave_noise(UV * 0.35);
    float detail = cave_noise(UV * 2.8);
    float grain = cave_noise(UV * 21.0);
    vec3 rock = grey_rock_surface(strata, detail, grain);

    COLOR = vec4(
        rock * 1.2 * world_sunlight(vec3(0.0, 0.0, 1.0)),
        1.0
    );
}