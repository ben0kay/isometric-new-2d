// Fades the existing fixed sun-shadow polygons beneath the eclipse.
// Shape and direction remain unchanged.
shader_type canvas_item;
render_mode unshaded;

#include "res://VISUALS/Lighting/WorldLighting.gdshaderinc"

varying vec2 shadow_world_position;
varying vec4 shadow_colour;

// =========================================================
// Preserve the generated polygon colours and projected world position.
void vertex() {
    shadow_world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    shadow_colour = COLOR;
}

// =========================================================
// Remove direct-sun shadows as the eclipse blocks sunlight.
void fragment() {
    float sunlight = 1.0 - eclipse_darkness(shadow_world_position);
    COLOR = vec4(shadow_colour.rgb, shadow_colour.a * sunlight);
}