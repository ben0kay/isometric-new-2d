// Blends finite ground deposits into the existing lit terrain surface.
// CPU digging uses the same irregular outline and remaining-radius calculation.

uniform vec4 deposit_0 = vec4(0.0);
uniform vec4 deposit_1 = vec4(0.0);
uniform vec4 deposit_2 = vec4(0.0);
uniform vec4 deposit_3 = vec4(0.0);

uniform vec4 deposit_color_0 = vec4(0.0);
uniform vec4 deposit_color_1 = vec4(0.0);
uniform vec4 deposit_color_2 = vec4(0.0);
uniform vec4 deposit_color_3 = vec4(0.0);

// =========================================================
// Blend one softly feathered material patch without changing terrain height.
vec3 blend_deposit(vec3 ground, vec2 p, vec4 patch,
        vec3 tint, float detail, float grain) {
    if (patch.z <= 0.001) {
        return ground;
    }

    vec2 delta = p - patch.xy;
    float angle = atan(delta.y, delta.x);
    float edge = 1.0
        + sin(angle * 3.0 + patch.w) * 0.06
        + sin(angle * 5.0 - patch.w * 1.7) * 0.04;

    float radius = patch.z * edge;
    float feather = max(0.03, patch.z * 0.22);
    float coverage = 1.0 - smoothstep(
        radius - feather, radius, length(delta));

    vec3 surface = tint
        * (0.90 + detail * 0.12 + grain * 0.08);
    return mix(ground, surface, coverage);
}

// =========================================================
// Apply the chunk's bounded patch set before existing sunlight calculations.
vec3 ground_resources(vec3 ground, vec2 p, float detail, float grain) {
    ground = blend_deposit(
        ground, p, deposit_0, deposit_color_0.rgb, detail, grain);
    ground = blend_deposit(
        ground, p, deposit_1, deposit_color_1.rgb, detail, grain);
    ground = blend_deposit(
        ground, p, deposit_2, deposit_color_2.rgb, detail, grain);
    ground = blend_deposit(
        ground, p, deposit_3, deposit_color_3.rgb, detail, grain);
    return ground;
}