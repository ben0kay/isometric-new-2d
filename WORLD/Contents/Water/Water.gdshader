// Renders smooth water inside the basin's current filled contour.
shader_type canvas_item;
render_mode unshaded;

uniform vec4 water_color : source_color = vec4(0.12, 0.30, 0.35, 1.0);
uniform vec2 radius_tiles = vec2(6.0, 4.0);
uniform float phase = 1.7;
uniform float wave_speed = 0.6;
uniform float wave_strength = 0.12;
uniform float opacity = 0.85;
uniform float basin_depth = 24.0;
uniform float shore_width = 0.9;
uniform float water_drop = 6.0;

// =========================================================
// Produce continuous low-frequency variation rather than repeating bright dots.
float water_hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// =========================================================
// Smoothly interpolate random values between neighbouring lattice points.
float water_noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(water_hash(cell), water_hash(cell + vec2(1.0, 0.0)), f.x),
        mix(water_hash(cell + vec2(0.0, 1.0)),
            water_hash(cell + vec2(1.0, 1.0)), f.x),
        f.y);
}

// =========================================================
// Match basin depth, animate broad ripples and soften the shallow-water edge.
void fragment() {
    vec2 q = (UV * 2.0 - 1.0) * 1.15;
    float angle = atan(q.y, q.x);
    float edge = 1.0
        + sin(angle * 3.0 + phase) * 0.06
        + sin(angle * 5.0 - phase * 1.7) * 0.04;

    float inward = (edge - length(q)) * min(radius_tiles.x, radius_tiles.y);
    float bank = clamp(inward / shore_width, 0.0, 1.0);
    float floor_depth = basin_depth * bank * bank * (3.0 - 2.0 * bank);
    float water_depth = floor_depth - water_drop;
    float coverage = smoothstep(0.0, 1.5, water_depth);

    vec2 p = q * radius_tiles;
    float t = TIME * wave_speed;
    float broad = water_noise(p * 0.65 + vec2(t * 0.05, -t * 0.03));
    float fine = water_noise(p * 2.2 + vec2(-t * 0.09, t * 0.06));
    float ripple = sin(p.x * 2.4 + p.y * 1.1 + broad * 4.0 + t);
    float reflection = smoothstep(0.72, 0.95, fine) * (0.5 + broad * 0.5);

    float depth_mix = smoothstep(0.0, 16.0, water_depth);
    vec3 shallow = water_color.rgb * vec3(1.05, 1.15, 1.08);
    vec3 deep = water_color.rgb * vec3(0.45, 0.65, 0.75);
    vec3 color = mix(shallow, deep, depth_mix);
    color *= 1.0 + ripple * wave_strength * 0.25;
    color += vec3(0.18, 0.28, 0.30) * reflection * wave_strength;

    float shore = 1.0 - smoothstep(0.0, 3.0, water_depth);
    color = mix(color, vec3(0.21, 0.28, 0.25), shore * 0.25);

    COLOR = vec4(color, coverage * opacity);
}