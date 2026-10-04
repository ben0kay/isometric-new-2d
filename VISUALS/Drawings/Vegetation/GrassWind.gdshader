// Sways grass gently while anchoring each blade's ground contact.
// Longer grass naturally receives more displacement.
shader_type canvas_item;
render_mode blend_premul_alpha;

uniform float wind_strength : hint_range(0.0, 8.0) = 2.8;
uniform float wind_speed : hint_range(0.0, 4.0) = 1.4;

// =========================================================
// Apply shared gusts with position-dependent phase and height-based bending.
void vertex() {
    float phase = dot(MODEL_MATRIX[3].xy, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    float height_weight = clamp(-VERTEX.y / 46.0, 0.0, 1.0);
    VERTEX.x += sway * gust * wind_strength * height_weight;
}