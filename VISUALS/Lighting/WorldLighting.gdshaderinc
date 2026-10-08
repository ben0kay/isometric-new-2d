// Shares one global eclipse cycle between world lighting and sun shadows.
// Screen height only controls the brief top-to-bottom transition effect.

uniform float eclipse_phase = 0.0;
uniform float eclipse_fraction = 0.25;
uniform float eclipse_transition_fraction = 0.01;
uniform float eclipse_darkness_multiplier = 1.0;
uniform bool eclipse_enabled = true;

// =========================================================
// Move a soft transition edge from above the screen to below it.
float eclipse_sweep(float progress, float screen_y) {
    float softness = 0.08;
    float edge = mix(
        -softness,
        1.0 + softness,
        clamp(progress, 0.0, 1.0)
    );

    return smoothstep(
        screen_y - softness,
        screen_y + softness,
        edge
    );
}

// =========================================================
// Apply the same eclipse timing everywhere, starting in full daylight.
float eclipse_darkness(float screen_y) {
    if (!eclipse_enabled || eclipse_fraction <= 0.0) {
        return 0.0;
    }

    float duration = clamp(eclipse_fraction, 0.0001, 0.9999);
    float phase = clamp(eclipse_phase, 0.0, 1.0);
    float beginning = 1.0 - duration;

    if (phase <= beginning) {
        return 0.0;
    }

    float transition = clamp(
        eclipse_transition_fraction,
        0.000001,
        duration * 0.25
    );

    float arrival = eclipse_sweep(
        (phase - beginning) / transition,
        clamp(screen_y, 0.0, 1.0)
    );

    float departure = eclipse_sweep(
        (phase - (1.0 - transition)) / transition,
        clamp(screen_y, 0.0, 1.0)
    );

    return clamp(
        arrival * (1.0 - departure) *
        max(eclipse_darkness_multiplier, 0.0),
        0.0, 1.0
    );
}