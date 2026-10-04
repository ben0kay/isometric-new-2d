// Adds broad directional sunlight across cached terrain without rebuilding chunks.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform sampler2D mist_texture : repeat_enable, filter_linear;
uniform vec2 sun_direction = vec2(-1.0, -0.7);
uniform vec4 sun_color : source_color = vec4(1.0, 0.96, 0.87, 1.0);
uniform float sun_strength = 2.2;

varying vec2 world_position;
varying vec4 surface_color;

// =========================================================
// Preserve terrain shading and locate the lighting pattern in world space.
void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
	surface_color = COLOR;
}

// =========================================================
// Add broad sunlit patches and subtle surface variation.
void fragment() {
	vec4 base = texture(TEXTURE, UV) * surface_color;
	vec2 direction = normalize(sun_direction);
	vec2 across = vec2(-direction.y, direction.x);

	float variation = texture(mist_texture, world_position * 0.0008).r;
	float band = sin(dot(world_position, across) * 0.005 + variation * 1.4);
	float sunlight = smoothstep(-0.55, 0.75, band);
	float strength = sun_strength * (0.18 + sunlight * 0.82);

	vec3 ambient = base.rgb * vec3(0.78, 0.88, 1.0);
	vec3 lit = base.rgb * sun_color.rgb * strength;
	COLOR = vec4(ambient + lit, base.a);
}