// Shared organic surface functions used during vegetation baking.
// Adds restrained mottling, grain and faint fibre-like detail.

// =========================================================
// Return deterministic grain for one sample position.
float vegetation_hash(vec2 point) {
    return fract(sin(dot(point, vec2(127.1, 311.7))) * 43758.5453);
}

// =========================================================
// Interpolate neighbouring grain samples into smooth mottling.
float vegetation_noise(vec2 point) {
    vec2 cell = floor(point);
    vec2 fraction = fract(point);
    vec2 blend = fraction * fraction * (3.0 - 2.0 * fraction);

    float a = vegetation_hash(cell);
    float b = vegetation_hash(cell + vec2(1.0, 0.0));
    float c = vegetation_hash(cell + vec2(0.0, 1.0));
    float d = vegetation_hash(cell + vec2(1.0, 1.0));

    return mix(mix(a, b, blend.x), mix(c, d, blend.x), blend.y);
}

// =========================================================
// Detail existing artwork without replacing its palette or baked lighting.
vec3 vegetation_surface(vec3 colour, vec2 pixel, float strength) {
    float mottling = vegetation_noise(pixel * 0.12) - 0.5;
    float grain = vegetation_hash(floor(pixel)) - 0.5;
    float fibres = vegetation_noise(
        vec2(pixel.x * 0.5, pixel.y * 0.045)) - 0.5;

    float variation = mottling * 0.22 + grain * 0.12 + fibres * 0.08;
    return max(colour * (1.0 + variation * strength), vec3(0.0));
}