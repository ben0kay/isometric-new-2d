// Draws water on a flat isometric quad with a soft irregular shoreline.
shader_type canvas_item;
render_mode unshaded;

uniform vec4 water_color : source_color = vec4(0.12, 0.30, 0.35, 1.0);
uniform vec2 radius_tiles = vec2(3.0, 2.4);
uniform float phase = 0.0;
uniform float wave_speed = 0.6;
uniform float wave_strength = 0.12;
uniform float opacity = 0.85;

// =========================================================
// Match the gameplay boundary; the quad includes padding around this shape.
void fragment() {
    vec2 q = (UV * 2.0 - 1.0) * 1.15;
    float angle = atan(q.y, q.x);
    float edge = 1.0
        + sin(angle * 3.0 + phase) * 0.06
        + sin(angle * 5.0 - phase * 1.7) * 0.04;
    float distance_to_edge = edge - length(q);
    float coverage = smoothstep(0.0, 0.18, distance_to_edge);

    vec2 p = q * radius_tiles;
    float t = TIME * wave_speed;
    float waves =
        sin(p.x * 8.0 + p.y * 4.0 + t) * 0.5
        + sin(p.x * -3.0 + p.y * 11.0 - t * 0.7) * 0.5;

    float glint = pow(max(0.0, waves), 8.0);
    float shore = exp(-max(distance_to_edge, 0.0) * 18.0);

    vec3 color = water_color.rgb * (0.85 + waves * wave_strength);
    color += vec3(0.25, 0.40, 0.43) * glint * 0.22;
    color = mix(color, vec3(0.31, 0.38, 0.30), shore * 0.35);

    COLOR = vec4(color, coverage * opacity);
}