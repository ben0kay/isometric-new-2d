// Adds a subtle player-contact bend to shared vegetation shaders.
// The controller supplies a smoothly fading interaction position and strength.

uniform bool brush_enabled = true;
uniform vec2 brush_position = vec2(0.0);
uniform vec2 brush_direction = vec2(1.0, 0.0);
uniform vec2 brush_radius = vec2(42.0, 24.0);
uniform float brush_strength = 0.0;

// =========================================================
// Bend foliage away from the player while respecting mirrored/scaled artwork.
vec2 vegetation_brush(mat4 model, float height_weight, float amount) {
    if (!brush_enabled || brush_strength < 0.001) {
        return vec2(0.0);
    }

    vec2 delta = model[3].xy - brush_position;
    vec2 relative = delta / max(brush_radius, vec2(1.0));
    float distance = length(relative);
    float contact = 1.0 - smoothstep(0.0, 1.0, distance);

    vec2 direction = delta;
    if (length(direction) < 2.0) {
        direction = brush_direction;
    }
    direction /= max(length(direction), 0.001);

    // Keep vertical displacement smaller than sideways bending.
    vec2 world_push = direction * vec2(amount, amount * 0.35);
    world_push *= contact * brush_strength * height_weight;

    mat2 axes = mat2(model[0].xy, model[1].xy);
    return inverse(axes) * world_push;
}