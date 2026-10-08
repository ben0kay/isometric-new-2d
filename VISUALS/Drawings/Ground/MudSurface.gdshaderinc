// Creates winding mud patches using the shared ground noise.
// The parent shader supplies patch scale, coverage, and surface detail.

// =========================================================
// Blend dark mud into the existing ground surface.
vec3 mud_surface(
    vec3 ground,
    sampler2D noise_texture,
    vec2 patch_uv,
    float coverage,
    float strata,
    float grain
) {
    if (coverage <= 0.0001) {
        return ground;
    }

    vec2 warp = vec2(
        texture(
            noise_texture,
            patch_uv * 0.7 + vec2(0.13, 0.39)
        ).r,
        texture(
            noise_texture,
            patch_uv * 0.7 + vec2(0.71, 0.17)
        ).r
    ) - vec2(0.5);

    float channel = texture(
        noise_texture,
        patch_uv * 0.8 + warp * 0.16 + vec2(0.61, 0.28)
    ).r;

    float mud_mask = 1.0 - smoothstep(
        0.035, 0.060, abs(channel - 0.5)
    );
    mud_mask *= coverage;

    vec3 mud = mix(
        vec3(0.035, 0.036, 0.032),
        vec3(0.078, 0.071, 0.056),
        strata
    );

    mud += vec3((grain - 0.5) * 0.007);

    return mix(ground, mud, mud_mask);
}