// Creates layered grey rock plates aligned with the local slope.
// Absolute terrain coordinates keep the pattern independent of chunk ownership.

// =========================================================
// Generate stable variation for individual rock slabs.
float steep_rock_hash(vec2 cell) {
    return fract(sin(dot(cell, vec2(127.1, 311.7))) * 43758.5453);
}

// =========================================================
// Draw large staggered plates, layered edges and dark crevices.
vec3 steep_rock_surface(
    vec2 tile, vec2 gradient, float detail, float grain
) {
    vec2 downhill = gradient / max(length(gradient), 0.001);
    vec2 across = vec2(-downhill.y, downhill.x);

    // Match the proportions of the current isometric ground projection.
    vec2 plane = vec2(
        (tile.x - tile.y) * 0.5,
        (tile.x + tile.y) * 0.25);

    vec2 slabs = vec2(
        dot(plane, across) / 1.6,
        dot(plane, downhill) / 0.55);

    float row = floor(slabs.y);
    slabs.x += steep_rock_hash(vec2(row, 19.0)) * 0.8;

    vec2 cell = floor(slabs);
    vec2 local = fract(slabs);
    float variation = steep_rock_hash(cell);

    float side_edge = min(local.x, 1.0 - local.x);
    float layer_edge = min(local.y, 1.0 - local.y);
    float side_crack = 1.0 - smoothstep(0.015, 0.065, side_edge);
    float layer_crack = 1.0 - smoothstep(0.015, 0.085, layer_edge);
    float crack = max(side_crack * 0.75, layer_crack);

    vec3 rock = mix(
        vec3(0.13, 0.145, 0.16),
        vec3(0.25, 0.265, 0.28),
        variation);

    float plate_shading = mix(0.78, 1.12, local.y);
    rock *= plate_shading;
    rock *= 1.0 - crack * 0.55;

    float lip = smoothstep(0.08, 0.13, local.y) *
        (1.0 - smoothstep(0.13, 0.20, local.y));
    rock += vec3(lip * 0.025);

    rock += vec3((detail - 0.5) * 0.018);
    rock += vec3((grain - 0.5) * 0.009);
    return rock;
}