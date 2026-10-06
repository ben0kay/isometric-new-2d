// Creates neutral grass-ground detail.
// Biome colours are supplied by the parent ground shader.

// =========================================================
// Produce softly mottled ground without drawing individual grass blades.
vec3 grass_surface(
    float strata, float detail, float grain,
    vec3 shade_tint, vec3 main_tint
) {
    float mixture = smoothstep(0.15, 0.85, strata);
    vec3 surface = mix(shade_tint, main_tint, mixture);

    float fibres = smoothstep(0.48, 0.78, detail);
    float brightness = 0.92 + fibres * 0.08
        + (grain - 0.5) * 0.035;

    return surface * brightness;
}