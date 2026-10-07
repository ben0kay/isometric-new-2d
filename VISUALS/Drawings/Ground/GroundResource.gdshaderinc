// Blends finite ground deposits into the existing lit terrain.
// Preserves the digging outline while softening its visual transition.

uniform vec4 deposit_0 = vec4(0.0);
uniform vec4 deposit_1 = vec4(0.0);
uniform vec4 deposit_2 = vec4(0.0);
uniform vec4 deposit_3 = vec4(0.0);

uniform vec4 deposit_color_0 = vec4(0.0);
uniform vec4 deposit_color_1 = vec4(0.0);
uniform vec4 deposit_color_2 = vec4(0.0);
uniform vec4 deposit_color_3 = vec4(0.0);

// Higher feathering creates a wider transition inside the deposit boundary.
uniform float deposit_edge_softness : hint_range(0.05, 0.95) = 0.48;

// Lower strength lets more of the original terrain show through.
uniform float deposit_tint_strength : hint_range(0.0, 1.0) = 0.65;

// =========================================================
// Blend a deposit with a broad edge fade and restrained surface contrast.
vec3 blend_deposit(
        vec3 ground, vec2 p, vec4 patch,
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
    float feather = max(0.03, radius * deposit_edge_softness);
    float coverage = 1.0 - smoothstep(
        max(0.0, radius - feather), radius, length(delta));

    // Retain subtle texture without making the patch sparkle or glow.
    float texture_detail =
        0.96 + (detail - 0.5) * 0.08 + (grain - 0.5) * 0.04;
    vec3 surface = tint * texture_detail;

    return mix(
        ground, surface,
        coverage * deposit_tint_strength);
}

// =========================================================
// Apply deposits before the existing terrain lighting.
vec3 ground_resources(
        vec3 ground, vec2 p, float detail, float grain) {
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