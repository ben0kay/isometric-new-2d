// Applies subtle shared wind to premultiplied baked vegetation.
// Artwork is authored with its ground contact at local position (0, 0).
shader_type canvas_item;
render_mode blend_premul_alpha;

uniform float wind_strength : hint_range(0.0, 12.0) = 3.2;
uniform float wind_speed : hint_range(0.0, 3.0) = 0.75;

// =========================================================
// Shear the upper artwork while keeping its ground contact anchored.
void vertex() {
    vec2 location = MODEL_MATRIX[3].xy;
    float phase = dot(location, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float sway = sin(time + phase) * 0.7
        + sin(time * 1.73 + phase * 0.61) * 0.3;
    VERTEX.x += (-VERTEX.y / 180.0) * sway * wind_strength;
}