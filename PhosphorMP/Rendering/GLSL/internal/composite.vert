#version 450

layout(location = 0) in vec2 Position;
layout(location = 0) out vec2 fsin_TexCoord;

void main()
{
    fsin_TexCoord = Position * 0.5 + 0.5;
    gl_Position = vec4(Position, 0.0, 1.0);
}