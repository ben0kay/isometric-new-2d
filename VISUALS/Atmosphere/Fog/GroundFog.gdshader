// Draws faint animated mist only on the existing walkable ground geometry.
shader_type canvas_item;
render_mode unshaded, blend_mix;

uniform sampler2D fog_noise : repeat_enable, filter_linear;
uniform vec4 fog_color : source_color = vec4(0.52, 0.61, 0.65, 1.0);
uniform vec2 wind = vec2(10.0, -3.0);
uniform vec2 tile_size = vec2(128.0, 64.0);
uniform float opacity = 0.10;
uniform float coverage = 0.48;

#include "res://VISUALS/Atmosphere/Fog/GroundWisps.gdshaderinc"

varying vec2 fog_position;

// =========================================================
// Project continuous logical terrain coordinates onto the horizontal world plane.
void vertex() {
    fog_position = vec2(
        (UV.x - UV.y) * tile_size.x * 0.5,
        (UV.x + UV.y) * tile_size.y * 0.5
    );
}

// =========================================================
// Render soft cloud patches without exposing chunk edges.
void fragment() {
    float alpha = ground_wisps(fog_position) * opacity * fog_color.a;
    if (alpha < 0.001) discard;
    COLOR = vec4(fog_color.rgb, alpha);
}