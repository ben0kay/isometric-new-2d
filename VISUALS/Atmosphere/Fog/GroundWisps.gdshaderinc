// Defines elongated, softly broken cloud patches from shared baked noise.

// =========================================================
// Advect two noise layers at different speeds to form faint moving wisps.
float ground_wisps(vec2 position) {
    vec2 broad_position = position - wind * TIME;
    vec2 detail_position = position - wind * TIME * 1.35;

    float broad = texture(fog_noise,
        broad_position * vec2(0.00065, 0.0022)).r;
    float detail = texture(fog_noise,
        detail_position * vec2(0.0026, 0.0085) + vec2(0.37, 0.61)).r;

    float patches = smoothstep(coverage, min(coverage + 0.22, 0.99), broad);
    float wisps = smoothstep(0.22, 0.76, detail);
    return patches * wisps;
}