// Adds gentle wind to baked tree crowns while anchoring the lower trunk.
// Animation runs on the GPU with one shared material.
shader_type canvas_item;
render_mode blend_premul_alpha;

uniform float wind_strength : hint_range(0.0, 12.0) = 4.0;
uniform float wind_speed : hint_range(0.0, 4.0) = 0.85;

// =========================================================
// Bend upper branches with a position-dependent phase and shared gusts.
void vertex() {
    float phase = dot(MODEL_MATRIX[3].xy, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.83 + phase * 0.63) * 0.25;

    float crown_weight = smoothstep(85.0, 330.0, -VERTEX.y);
    VERTEX.x += sway * gust * wind_strength * crown_weight;
}