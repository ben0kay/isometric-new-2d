// Defines baked stone grain, tonal variation and sparse fractures.

// =========================================================
// Texture a rock face while preserving its baked directional shading.
vec3 rock_surface(vec3 face, vec2 position) {
    vec2 p = position + seed_offset;

    float strata = texture(rock_noise, p * 0.0025).r;
    float detail = texture(rock_noise, p * 0.012 + vec2(0.37, 0.61)).r;
    float grain = texture(rock_noise, p * 0.075 + vec2(0.73, 0.19)).r;

    float fracture = 1.0 - smoothstep(0.006, 0.020, abs(detail - 0.49));
    fracture *= smoothstep(0.56, 0.76, strata);

    float variation = mix(0.84, 1.13, strata);
    variation += (grain - 0.5) * 0.17;
    variation *= 1.0 - fracture * 0.36;

    return max(face * variation, vec3(0.0));
}