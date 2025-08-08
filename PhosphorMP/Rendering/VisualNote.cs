namespace PhosphorMP.Rendering
{
    public struct VisualNote
    {
        public long StartingTick { get; set; }
        public int DurationTick { get; set; }
        public byte Key { get; set; }
        public byte Channel { get; set; }
        public int Track { get; set; }
    }
}