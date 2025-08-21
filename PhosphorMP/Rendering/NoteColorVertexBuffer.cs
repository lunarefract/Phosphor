using Veldrid;

namespace PhosphorMP.Rendering
{
    public class NoteColorVertexBuffer
    {
        public DeviceBuffer Buffer { get; internal set; }
        public int ColorIndex { get; internal set; }
        public bool NeedsRender { get; internal set; }
    }
}