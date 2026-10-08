// Fades fixed sun shadows using the same global eclipse and screen sweep.
// Shadow polygons retain their existing shape and direction.
shader_type canvas_item;
render_mode unshaded;

#include "res://VISUALS/Lighting/WorldLighting.gdshaderinc"

varying vec4 shadow_colour;

// =========================================================
// Preserve the generated polygon's vertex colours.
void vertex() {
    shadow_colour = COLOR;
}

// =========================================================
// Fade sunlight shadows in step with the visible eclipse transition.
void fragment() {
    float sunlight = 1.0 - eclipse_darkness(SCREEN_UV.y);
    COLOR = vec4(shadow_colour.rgb, shadow_colour.a * sunlight);
}