// Preserves grass wind and optionally recolours baked artwork from neutral brightness.
// Untinted grass retains its original atlas colours.
shader_type canvas_item;
render_mode blend_premul_alpha;

uniform float wind_strength : hint_range(0.0, 8.0) = 2.8;
uniform float wind_speed : hint_range(0.0, 4.0) = 1.4;
uniform bool biome_tint_enabled = false;

varying vec4 grass_colour;

// =========================================================
// Anchor grass bases and pass per-sprite colour to the fragment shader.
void vertex() {
    grass_colour = COLOR;

    float phase = dot(MODEL_MATRIX[3].xy, vec2(0.017, 0.011));
    float time = TIME * wind_speed;
    float gust = 0.75 + 0.25 * sin(time * 0.37);
    float sway = sin(time + phase) * 0.75
        + sin(time * 1.91 + phase * 0.63) * 0.25;

    float height_weight = clamp(-VERTEX.y / 46.0, 0.0, 1.0);
    VERTEX.x += sway * gust * wind_strength * height_weight;
}

// =========================================================
// Tint neutralized artwork while preserving existing premultiplied alpha.
void fragment() {
    vec4 artwork = texture(TEXTURE, UV);
    vec3 colour = artwork.rgb;

    if (biome_tint_enabled) {
        float brightness = max(
            artwork.r, max(artwork.g, artwork.b));
        vec3 tinted = vec3(brightness) * grass_colour.rgb;

        // Alpha of the sprite colour carries tint strength for this material.
        colour = mix(artwork.rgb, tinted, grass_colour.a);
        COLOR = vec4(colour, artwork.a);
    } else {
        COLOR = artwork * grass_colour;
    }
}