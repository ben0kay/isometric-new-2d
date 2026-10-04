// Defines dark basalt coloration, fine grain and sparse hairline fractures.

// =========================================================
// Produce restrained stone variation without isolated tile-centred shapes.
vec3 basalt_surface(float strata, float detail, float grain) {
    vec3 stone = mix(
        vec3(0.075, 0.086, 0.098),
        vec3(0.135, 0.148, 0.157),
        strata
    );

    float fracture = 1.0 - smoothstep(0.008, 0.026, abs(detail - 0.49));
    fracture *= smoothstep(0.59, 0.77, strata);

    stone *= 1.0 - fracture * 0.24;
    stone += vec3((detail - 0.5) * 0.014);
    stone += vec3((grain - 0.5) * 0.018);
    return stone;
}