// Applies shared organic surface detail while capturing vegetation.
// Gameplay uses the captured texture and its separate wind material.
shader_type canvas_item;
render_mode unshaded;

#include "res://VISUALS/Drawings/Vegetation/Surface/VegetationSurface.gdshaderinc"

uniform float surface_strength : hint_range(0.0, 2.0) = 1.0;

// =========================================================
// Preserve geometry alpha while detailing the drawn leaf colours.
void fragment() {
    COLOR.rgb = vegetation_surface(
        COLOR.rgb, FRAGCOORD.xy, surface_strength);
}