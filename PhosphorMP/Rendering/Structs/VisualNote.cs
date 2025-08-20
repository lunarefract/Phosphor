namespace PhosphorMP.Rendering.Structs
{
    public struct VisualNote
    {
        public long StartingTick { get; set; }
        public int DurationTick { get; set; }
        public byte Key { get; set; }
        public int ColorIndex { get; set; }
    }
}