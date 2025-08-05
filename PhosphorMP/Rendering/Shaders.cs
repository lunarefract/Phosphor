using Veldrid;
using Veldrid.SPIRV;

namespace PhosphorMP.Rendering
{
    public static class Shaders
    {
        public static List<Shader[]> CompiledShaders = [];
        
        public static Shader[] CompileShaders(GraphicsDevice gd, ResourceFactory factory)
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
    }
}

