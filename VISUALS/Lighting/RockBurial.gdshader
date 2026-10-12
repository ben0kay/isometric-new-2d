// Optional rock base clipping; retains the existing shared world lighting.
shader_type canvas_item;
render_mode unshaded, blend_mix;
#include "res://VISUALS/Lighting/WorldSurface.gdshaderinc"

uniform float burial_cutoff_y = 0.0;
uniform float burial_edge_pixels = 2.0;
uniform float burial_softness_pixels = 1.0;
varying vec4 artwork_colour;
varying vec2 artwork_position;
varying vec2 normal_axis_x;
varying vec2 normal_axis_y;

// =========================================================
// Use authored local pixels so burial follows uniform scale and mirroring.
void vertex() {
    artwork_colour = COLOR;
    artwork_position = VERTEX;
    normal_axis_x = normalize(MODEL_MATRIX[0].xy);
    normal_axis_y = normalize(MODEL_MATRIX[1].xy);
}

// =========================================================
// Hide the buried base with a small fixed uneven edge; no extra textures or scans.
void fragment() {
    vec4 pixel = texture(TEXTURE, UV) * artwork_colour;
    float uneven = (sin(artwork_position.x * 0.11)
        + 0.35 * sin(artwork_position.x * 0.29)) / 1.35;
    float edge = burial_cutoff_y + uneven * burial_edge_pixels;
    float soft = max(burial_softness_pixels, 0.01);
    pixel.a *= 1.0 - smoothstep(edge - soft, edge + soft, artwork_position.y);
    pixel.rgb = world_surface(pixel.rgb, artwork_position, normal_axis_x, normal_axis_y);
    COLOR = pixel;
}
