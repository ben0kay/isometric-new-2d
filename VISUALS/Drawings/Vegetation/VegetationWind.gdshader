// Sways baked vegetation around its ground contact.
// Shared gusts combine with position-based variation between plants.
shader_type canvas_item;
render_mode blend_premul_alpha;

uniform float wind_strength : hint_range(0.0, 24.0) = 10.0;
uniform float wind_speed : hint_range(0.0, 4.0) = 1.4;

// =========================================================
// Move upper foliage while anchoring the artwork at local ground level.
void vertex() {
    vec2 location = MODEL_MATRIX[3].xy;
    float phase = dot(location, vec2(0.017, 0.011));
    float time = TIME * wind_speed;

    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    float height = -VERTEX.y / 180.0;
    VERTEX.x += height * sway * gust * wind_strength;
}