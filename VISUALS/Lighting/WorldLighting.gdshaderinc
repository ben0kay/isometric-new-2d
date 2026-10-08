// Shares the world-space eclipse band between lighting and sun shadows.
// Positive world Y makes the eclipse advance toward the bottom of the world.

uniform float eclipse_phase = 0.0;
uniform float eclipse_fraction = 0.25;
uniform float eclipse_world_span = 240000.0;
uniform float eclipse_edge_width = 3000.0;
uniform float eclipse_darkness_multiplier = 1.0;
uniform bool eclipse_enabled = true;

// =========================================================
// Return a smooth eclipse mask without a visible cycle-wrap jump.
float eclipse_darkness(vec2 world_position) {
    if (!eclipse_enabled || eclipse_fraction <= 0.0) {
        return 0.0;
    }

    float span = max(eclipse_world_span, 1.0);
    float duration = clamp(eclipse_fraction, 0.0001, 0.9999);
    float phase = fract(eclipse_phase - world_position.y / span);
    float beginning = 1.0 - duration;
    float edge = max(
        0.00001,
        min(eclipse_edge_width / span, duration * 0.2)
    );

    float arrival = smoothstep(beginning, beginning + edge, phase);
    float departure = 1.0 - smoothstep(1.0 - edge, 1.0, phase);

    return clamp(
        arrival * departure * max(eclipse_darkness_multiplier, 0.0),
        0.0, 1.0
    );
}