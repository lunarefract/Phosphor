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

    public enum MidiChannelCommand : byte
    {
        NoteOff = 0x80,
        NoteOn = 0x90,
        PolyAftertouch = 0xA0,
        ControlChange = 0xB0,
        ProgramChange = 0xC0,
        ChannelAftertouch = 0xD0,
        PitchBend = 0xE0
    }

    public readonly struct MidiEvent
    {
        public long Tick { get; }
        public int DeltaTime { get; }
        public byte StatusByte { get; }
        public int Track { get; }
        public byte[]? Data { get; }
        public byte? MetaType { get; }
        public byte[]? MetaData { get; }
        public byte[]? SysExData { get; }

        public MidiEventType EventType
        {
            get
            {
                return StatusByte switch
                {
                    0xFF => MidiEventType.Meta,
                    0xF0 or 0xF7 => MidiEventType.SysEx,
                    _ when (StatusByte & 0xF0) >= 0x80 && (StatusByte & 0xF0) <= 0xE0 => MidiEventType.Channel,
                    _ => MidiEventType.Unknown
                };
            }
        }

        /// <summary>
        /// The MIDI channel (0–15) if this is a channel message.
        /// </summary>
        public int Channel => EventType == MidiEventType.Channel ? (StatusByte & 0x0F) : -1;

        /// <summary>
        /// The channel command (NoteOn, NoteOff, etc.) if this is a channel message.
        /// </summary>
        public MidiChannelCommand? ChannelCommand =>
            EventType == MidiEventType.Channel ? (MidiChannelCommand)(StatusByte & 0xF0) : null;

        /// <summary>
        /// First data byte (note number, controller number, etc.).
        /// </summary>
        public byte? Data1 => Data != null && Data.Length > 0 ? Data[0] : null;

        /// <summary>
        /// Second data byte (velocity, controller value, etc.).
        /// </summary>
        public byte? Data2 => Data != null && Data.Length > 1 ? Data[1] : null;
        
        public bool IsNoteOn => ChannelCommand == MidiChannelCommand.NoteOn && Data2 > 0;
        public bool IsNoteOff => ChannelCommand == MidiChannelCommand.NoteOff || 
                                  (ChannelCommand == MidiChannelCommand.NoteOn && Data2 == 0);

        public int NoteNumber => IsNoteOn || IsNoteOff ? Data1 ?? -1 : -1;
        public int Velocity => IsNoteOn || IsNoteOff ? Data2 ?? 0 : 0;
        
        public bool IsTempoChange => EventType == MidiEventType.Meta && MetaType == 0x51;
        public double TempoBPM
        {
            get
            {
                if (!IsTempoChange || MetaData == null || MetaData.Length != 3)
                    return 0;
                int mpqn = (MetaData[0] << 16) | (MetaData[1] << 8) | MetaData[2];
                return 60000000.0 / mpqn;
            }
        }

        public bool IsTimeSignature => EventType == MidiEventType.Meta && MetaType == 0x58;
        public (int numerator, int denominator) TimeSignature
        {
            get
            {
                if (!IsTimeSignature || MetaData == null || MetaData.Length < 2)
                    return (4, 4);
                return (MetaData[0], 1 << MetaData[1]);
            }
        }
        
        public MidiEvent(long tick, int deltaTime, byte statusByte, int track,
            byte[]? data = null,
            byte? metaType = null,
            byte[]? metaData = null,
            byte[]? sysExData = null)
        {
            Tick = tick;
            DeltaTime = deltaTime;
            StatusByte = statusByte;
            Track = track;
            Data = data;
            MetaType = metaType;
            MetaData = metaData;
            SysExData = sysExData;
        }

        public override string ToString()
        {
            return EventType switch
            {
                MidiEventType.Channel => $"{Tick} [{ChannelCommand}] Ch{Channel + 1} D1={Data1} D2={Data2}",
                MidiEventType.Meta => $"{Tick} [Meta 0x{MetaType:X2}] Len={MetaData?.Length ?? 0}",
                MidiEventType.SysEx => $"{Tick} [SysEx] Len={SysExData?.Length ?? 0}",
                _ => $"{Tick} [Unknown]"
            };
        }
    }
}
