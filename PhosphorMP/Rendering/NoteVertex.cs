using System.Numerics;
using Vortice.Mathematics.PackedVector;

namespace PhosphorMP.Rendering
{
    public struct NoteVertex
    {
        public Vector2 Position;     // layout(location = 0)
        public Vector3 Color;        // layout ...
        public Half2 TexCoord;       // 16-bit floats
        public Half2 NoteSize;       // 16-bit floats
    }
}