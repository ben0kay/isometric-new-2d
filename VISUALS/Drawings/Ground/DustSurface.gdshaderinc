// Defines muted sediment coloration with fine grain and broad tonal variation.

// =========================================================
// Produce a dusty surface that blends into basalt without bright golden blobs.
vec3 dust_surface(float strata, float detail, float grain) {
    vec3 dust = mix(
        vec3(0.112, 0.105, 0.094),
        vec3(0.177, 0.158, 0.130),
        strata
    );

    dust += vec3((detail - 0.5) * 0.009);
    dust += vec3((grain - 0.5) * 0.012);
    return dust;
}