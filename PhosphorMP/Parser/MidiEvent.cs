#nullable enable
namespace PhosphorMP.Parser
{
    public enum MidiEventType : byte
    {
        Meta,
        Channel,
        SysEx,
        Unknown
    }

    public class MidiEvent
    {
        public ulong Tick { get; }
        public int DeltaTime { get; }
        public byte StatusByte { get; }
        public byte? MetaType { get; }
        public byte[]? MetaData { get; }
        public byte[]? Data { get; }
        public byte[]? SysExData { get; }

        public MidiEvent(ulong tick, int deltaTime, byte statusByte,
            byte[]? data = null,
            byte? metaType = null,
            byte[]? metaData = null,
            byte[]? sysExData = null)
        {
            Tick = tick;
            DeltaTime = deltaTime;
            StatusByte = statusByte;
            Data = data;
            MetaType = metaType;
            MetaData = metaData;
            SysExData = sysExData;
        }

        public MidiEventType GetEventType()
        {
            switch (StatusByte)
            {
                case 0xFF:
                    return MidiEventType.Meta;
                case 0xF0:
                case 0xF7:
                    return MidiEventType.SysEx;
                default:
                {
                    if ((StatusByte & 0xF0) >= 0x80 && (StatusByte & 0xF0) <= 0xE0)
                        return MidiEventType.Channel;
                    else
                        return MidiEventType.Unknown;
                }
            }
        }
    }
}