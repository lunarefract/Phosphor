using Veldrid;
using Veldrid.SPIRV;

namespace PhosphorMP.Rendering
{
    public static class Shaders
    {
        public static List<Shader[]> CompiledShaders { get; private set; } = [];
        
        public static Shader[] CompileShaders(GraphicsDevice gd, ResourceFactory factory) // TODO: Accept wc and fc as args and compile. Move shader code somewhere else.
        {
            string vertexCode = @"
            #version 450

            layout(location = 0) in vec2 in_Position;
            layout(location = 1) in vec3 in_Color;
            layout(location = 2) in vec2 in_TexCoord;
            layout(location = 3) in vec2 in_NoteSize;

            layout(location = 0) out vec3 fragColor;
            layout(location = 1) out vec2 fragTexCoord;
            layout(location = 2) out vec2 fragFramebufferSize;
            layout(location = 3) out vec2 fragNoteSize;

            layout(std140, set = 0, binding = 0) uniform Uniforms {
                mat4 MVP;
                vec2 FramebufferSize;
            };

            void main()
            {
                fragColor = in_Color;
                fragTexCoord = in_TexCoord;
                fragFramebufferSize = FramebufferSize;
                fragNoteSize = in_NoteSize;
                gl_Position = MVP * vec4(in_Position, 0.0, 1.0);
            }
            ";
            
            // Credit -> https://github.com/BlackMIDIDevs/wasabi/blob/master/shaders/notes/notes.frag
            string fragmentCode = @"
            #version 450

            layout(location = 0) in vec3 fragColor;
            layout(location = 1) in vec2 fragTexCoord;
            layout(location = 2) in vec2 fragFramebufferSize;
            layout(location = 3) in vec2 fragNoteSize;

            layout(location = 0) out vec4 fsout_Color;

            const float pi = 3.14159265358979323846;

            void main()
            {
                vec2 v_uv = fragTexCoord;
                vec3 color = fragColor;

                float border_width_pixels = 4.0;

                // Note size in pixels
                float horiz_width_pixels = fragNoteSize.x * fragFramebufferSize.x;
                float vert_width_pixels = fragNoteSize.y * fragFramebufferSize.y;

                // Border width in UV space (normalized)
                float horiz_border_uv = border_width_pixels / horiz_width_pixels;
                float vert_border_uv = border_width_pixels / vert_width_pixels;

                // Check if pixel is inside the border region
                bool isBorder = 
                    (v_uv.x <= horiz_border_uv) ||
                    (v_uv.x >= 1.0 - horiz_border_uv) ||
                    (v_uv.y <= vert_border_uv) ||
                    (v_uv.y >= 1.0 - vert_border_uv);

                if (isBorder)
                {
                    // Set border color — for example, solid black
                    fsout_Color = vec4(0.0, 0.0, 0.0, 1.0);
                    return;
                }

                // Otherwise, apply your existing color modulation
                color *= (1.0 + cos(pi * 0.5 * v_uv.x)) * 0.5;
                color *= color;

                fsout_Color = vec4(color, 1.0);
            }
            ";

            ShaderDescription vsDesc = new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main");
            ShaderDescription fsDesc = new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main");

            var compiled = factory.CreateFromSpirv(vsDesc, fsDesc);
            CompiledShaders.Add(compiled);
            return compiled;
        }
        
        public static Shader[] CompileCompositeShaders(GraphicsDevice gd, ResourceFactory factory)
        {
            string vertexCode = @"
            #version 450

            layout(location = 0) in vec2 Position;
            layout(location = 0) out vec2 fsin_TexCoord;

            void main()
            {
                fsin_TexCoord = Position * 0.5 + 0.5;
                gl_Position = vec4(Position, 0.0, 1.0);
            }
            ";

            string fragmentCode = @"
            #version 450

            layout(set = 0, binding = 0) uniform texture2D SourceTex;
            layout(set = 0, binding = 1) uniform sampler SourceSampler;

            layout(location = 0) in vec2 fsin_TexCoord;
            layout(location = 0) out vec4 fsout_Color;

            void main()
            {
                fsout_Color = texture(sampler2D(SourceTex, SourceSampler), fsin_TexCoord);
            }
            ";

            ShaderDescription vsDesc = new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main");
            ShaderDescription fsDesc = new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main");

            var compiled = factory.CreateFromSpirv(vsDesc, fsDesc);
            CompiledShaders.Add(compiled);
            return compiled;
        }
    }
}

