// Draws polygon or sprite silhouettes using the shared eclipse timing.
// Untextured polygons use the default white texture.
shader_type canvas_item;
render_mode unshaded;

#include "res://VISUALS/Lighting/WorldLighting.gdshaderinc"

varying vec4 shadow_colour;

// =========================================================
// Preserve the shadow's tint and per-vertex opacity.
void vertex() {
    shadow_colour = COLOR;
}

// =========================================================
// Apply texture coverage and fade cast shadows during the eclipse.
void fragment() {
    float coverage = texture(TEXTURE, UV).a;
    float sunlight = 1.0 - eclipse_darkness(SCREEN_UV.y);

    COLOR = vec4(
        shadow_colour.rgb,
        shadow_colour.a * coverage * sunlight
    );
}