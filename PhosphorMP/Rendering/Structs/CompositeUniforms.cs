using System.Numerics;
using System.Runtime.InteropServices;

namespace PhosphorMP.Rendering.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CompositeUniforms
    {
        public Matrix4x4 MVP;
        public Vector2 FramebufferSize;
        private Vector2 _padding; // Padding to align to 16 bytes (vk wants: 16*x)
    }
}