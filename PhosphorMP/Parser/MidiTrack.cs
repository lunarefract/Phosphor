using FastLINQ;
using PhosphorMP.Extensions;

namespace PhosphorMP.Parser
{
    public class MidiTrack : IDisposable
    {
        public int Id { get; init; }
        public long TrackStartPosition { get; init; }
        public int DataLength { get; init; }
        public long LastParsedTick { get; set; } = 0;
        public long LengthInTicks { get; private set; } = 0;
        //public List<MidiEvent> Events { get; private set; } = [];
        public static List<TempoChangeEvent> TempoChanges { get; } = [];
        public ulong NoteCount { get; private set; } = 0;
        public byte LastStatusByte { get; internal set; } = 0;

        private readonly RestrictedFileStream _trackData;
        private readonly BinaryReader _reader;

        public MidiTrack(string baseMidiPath, long position, int dataLength, int trackId)
        {
            TrackStartPosition = position;
            DataLength = dataLength;
            Id = trackId;

            _trackData = new RestrictedFileStream(baseMidiPath, FileMode.Open, position, dataLength);
            _reader = new BinaryReader(_trackData);
        }
        
        public FastList<MidiEvent> ParseEventsBetweenTicks(long startingTick, long endingTick)
        {
            FastList<MidiEvent> events = [];

            // Always parse from start of track for a stable result
            _reader.BaseStream.Position = 0;
            long ticks = 0;
            byte runningStatus = 0;

            while (_reader.BaseStream.Position < DataLength)
            {
                uint deltaTime = (uint)ReadVariableLength(_reader);
                ticks += deltaTime;

                if (ticks > endingTick)
                    break;

                byte statusByte = _reader.ReadByte();
                if (statusByte < 0x80)
                {
                    // Running status
                    if (runningStatus == 0)
                        throw new InvalidDataException("Running status used before status byte.");
                    _reader.BaseStream.Position -= 1;
                    statusByte = runningStatus;
                }
                else
                {
                    runningStatus = statusByte;
                }

                // Meta event
                if (statusByte == 0xFF)
                {
                    byte metaType = _reader.ReadByte();
                    int metaLength = ReadVariableLength(_reader);
                    byte[] metaData = _reader.ReadBytes(metaLength);

                    if (metaType == 0x2F) // End of track
                        break;

                    if (ticks >= startingTick)
                        events.Add(new MidiEvent(ticks, (int)deltaTime, statusByte, metaType: metaType, metaData: metaData, track: Id));
                }
                // Channel event
                else if (statusByte >= 0x80 && statusByte <= 0xEF)
                {
                    int dataBytes = statusByte switch
                    {
                        >= 0x80 and < 0xC0 => 2,
                        >= 0xC0 and < 0xE0 => 1,
                        >= 0xE0 and <= 0xEF => 2,
                        _ => throw new InvalidDataException($"Unknown MIDI status byte: {statusByte:X2}")
                    };

                    byte[] data = _reader.ReadBytes(dataBytes);
                    if (ticks >= startingTick)
                        events.Add(new MidiEvent(ticks, (int)deltaTime, statusByte, data: data, track: Id));
                }
                // SysEx event
                else if (statusByte == 0xF0 || statusByte == 0xF7)
                {
                    int sysexLength = ReadVariableLength(_reader);
                    byte[] sysexData = _reader.ReadBytes(sysexLength);
                    if (ticks >= startingTick)
                        events.Add(new MidiEvent(ticks, (int)deltaTime, statusByte, sysExData: sysexData, track: Id));
                }
                else
                {
                    throw new InvalidDataException($"Unknown status byte encountered: {statusByte:X2}");
                }
            }
            return events;
        }

        private int GetLastReadEventLength(long startPos)
        {
            return (int)(_reader.BaseStream.Position - startPos);
        }
        
        public void ParseEventsFast()
        {
            ulong noteCountLocal = 0;
            byte runningStatus = 0;
            long ticks = 0;

            while (_reader.BaseStream.Position < DataLength)
            {
                // 1. Read delta time (variable length)
                uint deltaTime = (uint)ReadVariableLength(_reader);
                ticks += deltaTime;

                // 2. Peek next byte to check status or running status
                if (_reader.BaseStream.Position >= DataLength)
                    break;

                byte statusByte = _reader.ReadByte();
                if (statusByte < 0x80)
                {
                    if (runningStatus == 0)
                        throw new InvalidDataException("Running status used before status byte.");
                    _reader.BaseStream.Position -= 1;
                    statusByte = runningStatus;
                }
                else
                {
                    runningStatus = statusByte;
                }

                // 3. Parse events based on statusByte
                if (statusByte == 0xFF) // Meta event
                {
                    if (_reader.BaseStream.Position >= DataLength)
                        break;

                    byte metaType = _reader.ReadByte();
                    int metaLength = ReadVariableLength(_reader);

                    if (_reader.BaseStream.Position + metaLength > DataLength)
                        break;

                    byte[] metaData = _reader.ReadBytes(metaLength); // <- Read the data

                    if (metaType == 0x2F) // End of track
                        break;

                    if (metaType == 0x51 && metaLength == 3)
                    {
                        int tempo = (metaData[0] << 16) | (metaData[1] << 8) | metaData[2];
                        double bpm = 60_000_000.0 / tempo;
                        
                        TempoChanges.Add(new TempoChangeEvent(ticks, tempo));
                    }
                }
                else if (statusByte >= 0x80 && statusByte <= 0xEF) // MIDI channel message
                {
                    int dataBytes = statusByte switch
                    {
                        >= 0x80 and < 0xC0 => 2,
                        >= 0xC0 and < 0xE0 => 1,
                        >= 0xE0 and <= 0xEF => 2,
                        _ => throw new InvalidDataException($"Unknown MIDI status byte: {statusByte:X2}")
                    };

                    if (_reader.BaseStream.Position + dataBytes > DataLength)
                        break;

                    byte[] data = _reader.ReadBytes(dataBytes);
                    
                    if ((statusByte & 0xF0) == 0x90 && data[1] > 0)
                    {
                        noteCountLocal++;
                    }
                }
                else if (statusByte == 0xF0 || statusByte == 0xF7) // SysEx events
                {
                    int sysexLength = ReadVariableLength(_reader);
                    if (_reader.BaseStream.Position + sysexLength > DataLength)
                        break;

                    _reader.BaseStream.Position += sysexLength; // skip data
                }
                else
                {
                    throw new InvalidDataException($"Unknown status byte encountered: {statusByte:X2}");
                }
            }

            LengthInTicks = ticks;
            NoteCount = noteCountLocal;
        }
        
        private int ReadVariableLength(BinaryReader reader)
        {
            int value = 0;
            byte b;
            do
            {
                if (reader.BaseStream.Position >= DataLength)
                    throw new EndOfStreamException("Reached end of track data while reading variable length quantity.");

                b = reader.ReadByte();
                value = (value << 7) | (b & 0x7F);
            } while ((b & 0x80) != 0);
            return value;
        }

        private static int GetVariableLengthSize(int value)
        {
            int size = 1;
            while ((value >>= 7) != 0)
                size++;
            return size;
        }

        public void Dispose()
        {
            _trackData?.Dispose();
            _reader?.Dispose();
            //Events.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
