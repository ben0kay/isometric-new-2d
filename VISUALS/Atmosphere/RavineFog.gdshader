// Shades ravine walls and floors with smooth world-space mist and explicit depth.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D mist_texture : repeat_enable, filter_linear;
uniform vec4 fog_color : source_color = vec4(0.16, 0.31, 0.43, 1.0);
uniform float fog_density = 0.72;
uniform float noise_scale = 0.0012;
uniform vec2 drift = vec2(3.0, -1.0);

varying vec2 world_position;
varying vec4 surface_color;
varying float surface_depth;

// =========================================================
// Read mesh depth independently from its lighting and colour.
void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
	surface_color = COLOR;
	surface_depth = UV.y;
}

// =========================================================
// Apply broad, softly moving haze with a gradual fade toward the rim.
void fragment() {
	vec2 coordinates = world_position * noise_scale;
	float broad = texture(mist_texture,
		coordinates + drift * TIME * noise_scale).r;
	float detail = texture(mist_texture,
		coordinates * 1.93 - drift * TIME * noise_scale * 0.37
		+ vec2(0.31, 0.67)).r;

	float mist = broad * 0.8 + detail * 0.2;
	float depth = smoothstep(0.05, 1.0, surface_depth);
	float amount = clamp(fog_density * depth * (0.8 + mist * 0.25), 0.0, 0.92);
	vec3 haze = fog_color.rgb * (0.85 + mist * 0.25);

	COLOR = vec4(mix(surface_color.rgb, haze, amount), surface_color.a);
}