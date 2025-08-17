namespace PhosphorMP.Rendering.Structs
{
    public struct SaveRequest
    {
        public byte[] Data;
        public uint Width;
        public uint Height;
        public uint RowPitch;
        public string FilePath;
    }
}