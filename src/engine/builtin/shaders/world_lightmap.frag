#version 330

in vec2 fragTexCoord;    // albedo UVs
in vec2 fragTexCoord2;   // lightmap UVs
in vec4 fragColor;

uniform sampler2D texture0;   // albedo (checkerboard placeholder for now)
uniform sampler2D lightmap;   // baked lightmap atlas
uniform vec4 colDiffuse;

uniform vec3 ambientColor;    // small floor so unlit corners aren't pure black

out vec4 finalColor;

vec3 toLinear(vec3 c) { return pow(c, vec3(2.2)); }

void main()
{
    vec3 albedo = toLinear(texture(texture0, fragTexCoord).rgb) * toLinear(colDiffuse.rgb);

    // The atlas PNG was already tonemapped+gamma'd by the baker, so decode to
    // linear here, then the pipeline's tonemap pass re-encodes once at the end.
    vec3 light = toLinear(texture(lightmap, fragTexCoord2).rgb);

    vec3 color = albedo * (ambientColor + light);
    finalColor = vec4(color, 1.0);
}
