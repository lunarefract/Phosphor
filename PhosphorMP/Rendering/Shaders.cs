using Veldrid;
using Veldrid.SPIRV;

namespace PhosphorMP.Rendering
{
    public static class Shaders
    {
        public static List<Shader[]> CompiledShaders { get; private set; } = [];
        public const string GlslVersionSuffix = "#version 450\n";
        
        public static Shader[] CompileShaders(GraphicsDevice gd, ResourceFactory factory) // TODO: Accept wc and fc as args and compile. Move shader code somewhere else.
        {
            string vertexCode = @"
            layout(location = 0) in vec2 in_Position;
            layout(location = 1) in vec3 in_Color;
            layout(location = 2) in vec2 in_TexCoord;

            layout(location = 0) out vec3 fragColor;

            layout(std140, set = 0, binding = 0) uniform Uniforms {
                mat4 MVP;
                vec2 FramebufferSize;
            };

            void main()
            {
                fragColor = in_Color;
                gl_Position = MVP * vec4(in_Position, 0.0, 1.0);
            }
            ";
            
            string fragmentCode = @"
            layout(location = 0) in vec3 fragColor;

            layout(location = 0) out vec4 fsout_Color;

            const float pi = 3.1415;

            void main()
            {
                fsout_Color = vec4(fragColor, 1.0);
            }
            ";

            ShaderDescription vsDesc = new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(GlslVersionSuffix + vertexCode), "main");
            ShaderDescription fsDesc = new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(GlslVersionSuffix + fragmentCode), "main");

            var compiled = factory.CreateFromSpirv(vsDesc, fsDesc);
            CompiledShaders.Add(compiled);
            return compiled;
        }
        
        public static Shader[] CompileCompositeShaders(GraphicsDevice gd, ResourceFactory factory)
        {
            string vertexCode = @"
            layout(location = 0) in vec2 Position;
            layout(location = 0) out vec2 fsin_TexCoord;

            void main()
            {
                fsin_TexCoord = Position * 0.5 + 0.5;
                gl_Position = vec4(Position, 0.0, 1.0);
            }
            ";

            string fragmentCode = @"
            layout(set = 0, binding = 0) uniform sampler2D uTexture;
            layout(location = 0) in vec2 fragTexCoord;
            layout(location = 0) out vec4 outColor;

            void main() {
                outColor = texture(uTexture, fragTexCoord);
            }
            ";

            ShaderDescription vsDesc = new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(GlslVersionSuffix + vertexCode), "main");
            ShaderDescription fsDesc = new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(GlslVersionSuffix + fragmentCode), "main");

            var compiled = factory.CreateFromSpirv(vsDesc, fsDesc);
            CompiledShaders.Add(compiled);
            return compiled;
        }
    }
}