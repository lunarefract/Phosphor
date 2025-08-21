using System.Numerics;
using Vortice.Mathematics.PackedVector;

namespace PhosphorMP.Rendering.Structs
{
    public struct NoteVertex
    {
        public Vector2 Position;     // layout(location = 0)
        public uint Color;        // layout ...
        public Half2 TexCoord;       // 16-bit floats
    }
}