#version 450

layout(set = 0, binding = 0) uniform texture2D SourceTex;
layout(set = 0, binding = 1) uniform sampler SourceSampler;

layout(location = 0) in vec2 fsin_TexCoord;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = texture(sampler2D(SourceTex, SourceSampler), fsin_TexCoord);
}