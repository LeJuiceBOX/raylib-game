#version 330

// Fullscreen pass. Raylib's BeginShaderMode + DrawTextureRec feeds these.
in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;   // the HDR color target
uniform vec4 colDiffuse;

uniform float exposure;

out vec4 finalColor;

void main()
{
    vec3 hdr = texture(texture0, fragTexCoord).rgb * exposure;

    // Reinhard tonemap + 2.2 gamma — matches the baker's Output.ToByte curve,
    // so baked lightmaps and dynamic lighting land on the same response curve.
    vec3 mapped = hdr / (vec3(1.0) + hdr);
    mapped = pow(mapped, vec3(1.0 / 2.2));

    finalColor = vec4(mapped, 1.0);
}
