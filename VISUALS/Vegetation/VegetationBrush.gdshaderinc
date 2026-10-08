// Supplies one shared player contact field to every vegetation shader.
global uniform vec2 wl_brush_position;
global uniform vec2 wl_brush_direction;
global uniform vec2 wl_brush_radius;
global uniform float wl_brush_strength;

uniform bool brush_enabled = true;

// =========================================================
// Bend upper foliage away from the player while preserving ground anchors.
vec2 vegetation_brush(mat4 model, float height_weight, float amount) {
    if (!brush_enabled || wl_brush_strength <= 0.0) {
        return vec2(0.0);
    }

    vec2 delta = model[3].xy - wl_brush_position;
    vec2 relative = delta / max(wl_brush_radius, vec2(1.0));
    float contact = 1.0 - smoothstep(0.0, 1.0, length(relative));

    vec2 direction = length(delta) < 2.0
        ? wl_brush_direction : delta;
    direction /= max(length(direction), 0.001);

    vec2 world_push = direction * vec2(amount, amount * 0.35)
        * contact * wl_brush_strength * height_weight;

    mat2 axes = mat2(model[0].xy, model[1].xy);
    return inverse(axes) * world_push;
}