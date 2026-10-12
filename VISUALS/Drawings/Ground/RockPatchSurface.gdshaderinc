// Bake-only soil and gravel. No TIME, lighting, runtime rock loops or ground mask.
float rp_hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
float rp_noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(rp_hash(i), rp_hash(i + vec2(1.0, 0.0)), f.x),
        mix(rp_hash(i + vec2(0.0, 1.0)), rp_hash(i + vec2(1.0)), f.x), f.y);
}
vec4 rock_patch_surface(vec2 uv, float seed, float density, vec3 soil, vec3 stone) {
    vec2 q = (uv - vec2(0.5)) * 2.0;
    vec2 offset = vec2(seed * 17.3, seed * 31.7);
    float ragged = rp_noise(q * 5.0 + offset);
    float radius = length(q) + (ragged - 0.5) * 0.20;
    float alpha = 1.0 - smoothstep(0.65, 0.97, radius);
    float grit = rp_noise(uv * 170.0 + offset);
    float broad = rp_noise(uv * 12.0 + offset);
    vec3 colour = soil * (0.80 + broad * 0.30 + grit * 0.16);
    // Neighbouring cells let each irregular pebble cross cell boundaries naturally.
    vec2 grid = uv * 27.0;
    vec2 cell = floor(grid);
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 id = cell + vec2(float(x), float(y));
            float pick = rp_hash(id + offset);
            vec2 centre = id + vec2(rp_hash(id + offset + 9.1), rp_hash(id + offset + 4.7));
            vec2 d = grid - centre;
            float size = mix(0.07, 0.32, rp_hash(id + offset + 2.3));
            float shape = length(d * vec2(1.0, 1.25))
                + 0.035 * sin(atan(d.y, d.x) * 5.0 + pick * 6.28);
            float pebble = (1.0 - smoothstep(size - 0.035, size + 0.035, shape))
                * step(pick, density);
            // Material variation, without baking a fixed sun direction or cast shadow.
            colour = mix(colour, stone * (0.75 + pick * 0.45), pebble);
        }
    }
    return vec4(colour, alpha);
}
