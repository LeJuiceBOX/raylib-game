#version 330

in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragNormal;
in vec3 fragWorldPos;

// Raylib default: bound to MaterialMapIndex.Albedo
uniform sampler2D texture0;
uniform vec4 colDiffuse;

// --- Our pipeline uniforms ---
uniform vec3 viewPos;          // camera world position

uniform vec3 dirLightDir;      // direction the light travels (normalized)
uniform vec3 dirLightColor;    // linear RGB * intensity
uniform vec3 ambientColor;     // linear RGB flat ambient

uniform float specStrength;    // Blinn-Phong specular weight
uniform float shininess;       // specular exponent

out vec4 finalColor;

// sRGB texture -> linear, so all lighting math is linear (tonemap converts back)
vec3 toLinear(vec3 c) { return pow(c, vec3(2.2)); }

void main()
{
    vec4 tex = texture(texture0, fragTexCoord);
    vec3 albedo = toLinear(tex.rgb) * toLinear(colDiffuse.rgb) * toLinear(fragColor.rgb);

    vec3 N = normalize(fragNormal);
    vec3 L = normalize(-dirLightDir);          // from surface toward light
    vec3 V = normalize(viewPos - fragWorldPos);
    vec3 H = normalize(L + V);

    float ndl = max(dot(N, L), 0.0);
    vec3 diffuse = dirLightColor * ndl;

    float ndh = max(dot(N, H), 0.0);
    float spec = (ndl > 0.0) ? pow(ndh, shininess) * specStrength : 0.0;
    vec3 specular = dirLightColor * spec;

    vec3 color = albedo * (ambientColor + diffuse) + specular;

    // Linear HDR out; tonemap pass compresses + gamma-encodes.
    finalColor = vec4(color, tex.a * colDiffuse.a);
}
