// Defines broad drifting cloud patches with softly curled edges and finer wisps.

// =========================================================
// Combine large cloud bodies, gentle distortion and independently drifting detail.
float ground_wisps(vec2 position) {
    vec2 broad_position = position - wind * TIME;
    vec2 detail_position = position - wind * TIME * 1.35;
    vec2 curl_position = position - wind * TIME * 0.72;

    float curl = texture(fog_noise,
        curl_position * vec2(0.00045, 0.0011) + vec2(0.63, 0.29)).r;

    vec2 distortion = vec2(
        (curl - 0.5) * 240.0,
        (curl - 0.5) * 90.0
    );

    float broad = texture(fog_noise,
        (broad_position + distortion) * vec2(0.00028, 0.00065)).r;

    float detail = texture(fog_noise,
        detail_position * vec2(0.0012, 0.0035) + vec2(0.37, 0.61)).r;

    float patches = smoothstep(
        coverage, min(coverage + 0.22, 0.99), broad);
    float wisps = smoothstep(0.22, 0.76, detail);

    return patches * wisps;
}