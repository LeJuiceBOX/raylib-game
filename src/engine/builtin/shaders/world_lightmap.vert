#version 330

in vec3 vertexPosition;
in vec2 vertexTexCoord;     // albedo UVs (Valve texture axes)
in vec2 vertexTexCoord2;    // lightmap UVs (baked atlas, 0..1)
in vec3 vertexNormal;
in vec4 vertexColor;

uniform mat4 mvp;
uniform mat4 matModel;

out vec2 fragTexCoord;
out vec2 fragTexCoord2;
out vec4 fragColor;

void main()
{
    fragTexCoord  = vertexTexCoord;
    fragTexCoord2 = vertexTexCoord2;
    fragColor     = vertexColor;
    gl_Position   = mvp * vec4(vertexPosition, 1.0);
}
