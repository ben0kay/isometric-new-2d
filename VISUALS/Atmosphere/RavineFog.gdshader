// Adds moving depth haze to the cached cliff and ravine floor artwork.
shader_type canvas_item;
render_mode unshaded;

uniform vec4 fog_color : source_color = vec4(0.16, 0.32, 0.43, 1.0);
uniform float fog_density : hint_range(0.0, 1.0) = 0.65;
uniform float noise_scale = 0.012;
uniform vec2 drift = vec2(3.0, -1.0);

varying vec2 world_position;
varying vec4 surface_color;

// =========================================================
// Preserve mesh shading and locate noise in world coordinates.
void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
	surface_color = COLOR;
}

// =========================================================
// Generate a repeatable lattice value.
float hash_value(vec2 point) {
	return fract(sin(dot(point, vec2(127.1, 311.7))) * 43758.5453);
}

// =========================================================
// Smooth neighbouring lattice values into flowing noise.
float value_noise(vec2 point) {
	vec2 cell = floor(point);
	vec2 fraction = fract(point);
	fraction = fraction * fraction * (3.0 - 2.0 * fraction);

	float a = hash_value(cell);
	float b = hash_value(cell + vec2(1.0, 0.0));
	float c = hash_value(cell + vec2(0.0, 1.0));
	float d = hash_value(cell + vec2(1.0, 1.0));
	return mix(mix(a, b, fraction.x), mix(c, d, fraction.x), fraction.y);
}

// =========================================================
// Blend blue haze into deep surfaces while leaving cliff tops readable.
void fragment() {
	vec2 sample_position = (world_position + drift * TIME) * noise_scale;
	float broad = value_noise(sample_position);
	float detail = value_noise(sample_position * 2.03 + vec2(19.0, 37.0));
	float mist = broad * 0.75 + detail * 0.25;

	// The current cliff mesh already grades from bright tops to dark bottoms.
	float brightness = max(surface_color.r, max(surface_color.g, surface_color.b));
	float depth = 1.0 - smoothstep(0.05, 0.25, brightness);
	float amount = clamp(fog_density * depth * (0.55 + mist * 0.65), 0.0, 0.9);
	vec3 haze = fog_color.rgb * (0.75 + mist * 0.45);

	COLOR = vec4(mix(surface_color.rgb, haze, amount), surface_color.a);
}