// Masks lower legs at the waterline without moving the actor or its collision.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform bool submerged = false;
uniform float waterline_y = 0.0;
uniform vec4 water_color : source_color = vec4(0.12, 0.30, 0.35, 1.0);

varying vec2 artwork_world_position;
varying vec4 artwork_modulate;

// =========================================================
// Use world coordinates so mirroring and camera zoom do not shift the waterline.
void vertex() {
    artwork_world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    artwork_modulate = COLOR;
}

// =========================================================
// Tint the waterline and fade pixels beneath it.
void fragment() {
    vec4 color = texture(TEXTURE, UV) * artwork_modulate;

    if (submerged) {
        float line = waterline_y
            + sin(artwork_world_position.x * 0.12 + TIME * 1.2) * 0.5;
        float band = smoothstep(line - 5.0, line, artwork_world_position.y);
        color.rgb = mix(color.rgb, water_color.rgb * color.a, band * 0.45);
        float visibility = 1.0 - smoothstep(
            line - 1.0, line + 1.0, artwork_world_position.y);
        color *= visibility;
    }

    COLOR = color;
}