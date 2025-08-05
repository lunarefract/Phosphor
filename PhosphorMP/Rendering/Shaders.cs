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
            layout(location = 0) in vec2 Position;
            layout(set = 0, binding = 0) uniform MVPBuffer {
                mat4 MVP;
            };
            void main()
            {
                gl_Position = MVP * vec4(Position, 0, 1);
            }
            ";

            string fragmentCode = @"
            #version 450
            layout(location = 0) out vec4 fsout_Color;
            void main()
            {
                fsout_Color = vec4(0.0, 1.0, 0.5, 1.0);
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

