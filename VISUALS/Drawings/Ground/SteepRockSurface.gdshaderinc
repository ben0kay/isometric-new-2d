// Creates irregular grey fractured rock using fixed terrain coordinates.
// Local slope affects contrast without bending the texture into contour patterns.

// =========================================================
// Generate deterministic scalar variation.
float steep_rock_hash(vec2 cell) {
    return fract(sin(dot(cell, vec2(127.1, 311.7))) * 43758.5453);
}

// =========================================================
// Generate irregular fracture-cell centres.
vec2 steep_rock_site(vec2 cell) {
    return vec2(
        steep_rock_hash(cell),
        steep_rock_hash(cell + vec2(41.7, 83.1))
    ) * 0.7 + vec2(0.15);
}

// =========================================================
// Produce smooth domain variation without using slope-dependent coordinates.
float steep_rock_noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 local = fract(p);
    local = local * local * (3.0 - 2.0 * local);

    float a = steep_rock_hash(cell);
    float b = steep_rock_hash(cell + vec2(1.0, 0.0));
    float c = steep_rock_hash(cell + vec2(0.0, 1.0));
    float d = steep_rock_hash(cell + vec2(1.0, 1.0));

    return mix(mix(a, b, local.x), mix(c, d, local.x), local.y);
}

// =========================================================
// Draw uneven angular blocks, deep joints and chipped grey edges.
vec3 steep_rock_surface(
    vec2 tile, vec2 gradient, float detail, float grain
) {
    vec2 plane = vec2(
        (tile.x - tile.y) * 0.5,
        (tile.x + tile.y) * 0.25);

    // Taller fracture blocks resemble exposed rock-face joints.
    vec2 p = plane * vec2(3.0, 1.8);
    vec2 warp = vec2(
        steep_rock_noise(p * 0.65),
        steep_rock_noise(p * 0.65 + vec2(17.3, 29.1))
    ) - vec2(0.5);
    p += warp * 0.45;

    vec2 base = floor(p);
    vec2 local = fract(p);
    vec2 nearest = vec2(0.0);
    vec2 nearest_cell = vec2(0.0);
    float nearest_squared = 1000.0;

    // Find the nearest irregular rock block.
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 offset = vec2(float(x), float(y));
            vec2 cell = base + offset;
            vec2 difference = offset + steep_rock_site(cell) - local;
            float squared = dot(difference, difference);

            if (squared < nearest_squared) {
                nearest_squared = squared;
                nearest = difference;
                nearest_cell = cell;
            }
        }
    }

    // Measure distance to the block's angular fracture boundaries.
    float edge_distance = 1000.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 cell = nearest_cell + vec2(float(x), float(y));
            vec2 other = cell + steep_rock_site(cell) - p;
            vec2 separation = other - nearest;
            float squared = dot(separation, separation);

            if (squared > 0.0001) {
                float boundary = dot(
                    (nearest + other) * 0.5,
                    separation / sqrt(squared));
                edge_distance = min(edge_distance, boundary);
            }
        }
    }

    edge_distance = max(edge_distance, 0.0);
    float variation = steep_rock_hash(nearest_cell + vec2(9.2, 4.6));

    vec3 rock = mix(
        vec3(0.135, 0.145, 0.155),
        vec3(0.265, 0.275, 0.28),
        variation);

    float face = clamp(
        nearest.x * 0.22 - nearest.y * 0.18, -0.12, 0.12);
    rock *= 1.0 + face;

    float fracture_width = mix(
        0.025, 0.045,
        steep_rock_noise(p * 2.5 + vec2(7.0, 13.0)));
    float joint = 1.0 - smoothstep(
        fracture_width, fracture_width + 0.035, edge_distance);

    float chipped_edge = smoothstep(
        fracture_width + 0.015,
        fracture_width + 0.05,
        edge_distance);
    chipped_edge *= 1.0 - smoothstep(
        fracture_width + 0.08,
        fracture_width + 0.15,
        edge_distance);

    rock *= 1.0 - joint * 0.65;
    rock += vec3(chipped_edge * 0.025);

    float weathering = steep_rock_noise(
        plane * vec2(7.0, 2.0) + vec2(31.0, 19.0));
    rock *= mix(0.88, 1.05, weathering);

    float slope_contrast = clamp(length(gradient), 0.0, 1.0);
    rock *= 1.0 + face * slope_contrast * 0.25;

    rock += vec3((detail - 0.5) * 0.015);
    rock += vec3((grain - 0.5) * 0.009);
    return rock;
}