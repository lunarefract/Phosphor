using System.Diagnostics;
using PhosphorMP.Extensions;

namespace PhosphorMP.Parser
{
    public class MidiTrack : IDisposable
    {
        public long TrackStartPosition { get; init; }
        public int DataLength { get; init; }
        public ulong LastParsedTick { get; set; } = 0;
        public ulong LengthInTicks { get; private set; } = 0;
        //public List<MidiEvent> Events { get; private set; } = [];
        public static List<TempoChangeEvent> TempoChanges { get; } = [];
        public ulong NoteCount { get; private set; } = 0;

        public byte LastStatusByte { get; set; } = 0;

        private readonly RestrictedFileStream _trackData;
        private readonly BinaryReader _reader;

        public MidiTrack(string baseMidiPath, long position, int dataLength)
        {
            TrackStartPosition = position;
            DataLength = dataLength;

            _trackData = new RestrictedFileStream(baseMidiPath, FileMode.Open, position, dataLength);
            _reader = new BinaryReader(_trackData);
        }
        
        public List<MidiEvent> ParseEventsBetweenTicks(ulong startingTick, ulong endingTick)
        {
            List<MidiEvent> events = [];
            byte runningStatus = LastStatusByte;
            ulong ticks = LastParsedTick;
            _reader.BaseStream.Position = 0; // TODO: Restore last stream position (Optimization)
            
            while (_reader.BaseStream.Position < DataLength)
            {
                // Read delta time
                uint deltaTime = (uint)ReadVariableLength(_reader);
                byte statusByte = _reader.ReadByte();

                ticks += deltaTime;

                // Check if we've passed ending tick
                if (ticks > endingTick)
                {
                    LastParsedTick = ticks - deltaTime; // Don't consume this event yet
                    break;
                }

                // Peek next byte to determine status or running status
                if (_reader.BaseStream.Position >= DataLength)
                    break;
                
                if (statusByte < 0x80)
                {
                    if (runningStatus == 0)
                        throw new InvalidDataException("Running status used before status byte.");

                    // Running status applies, unread this byte for data parsing
                    _reader.BaseStream.Position -= 1;
                    statusByte = runningStatus;
                }
                else
                {
                    runningStatus = statusByte;
                }

                LastStatusByte = runningStatus;

                // If ticks < startingTick, skip event without storing
                if (ticks < startingTick)
                {
                    // Skip event based on type
                    if (statusByte == 0xFF) // Meta event
                    {
                        if (_reader.BaseStream.Position >= DataLength)
                            break;

                        byte metaType = _reader.ReadByte();
                        int metaLength = ReadVariableLength(_reader);

                        if (_reader.BaseStream.Position + metaLength > DataLength)
                            break;

                        _reader.BaseStream.Position += metaLength; // Skip meta data
                    }
                    else if (statusByte >= 0x80 && statusByte <= 0xEF) // MIDI channel event
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

                        _reader.BaseStream.Position += dataBytes; // Skip data bytes
                    }
                    else if (statusByte == 0xF0 || statusByte == 0xF7) // SysEx event
                    {
                        int sysexLength = ReadVariableLength(_reader);
                        if (_reader.BaseStream.Position + sysexLength > DataLength)
                            break;

                        _reader.BaseStream.Position += sysexLength; // Skip sysex data
                    }
                    else
                    {
                        throw new InvalidDataException($"Unknown status byte encountered: {statusByte:X2}");
                    }

                    continue; // Skip event storing
                }

                // Now, parse the event and add it

                if (statusByte == 0xFF) // Meta event
                {
                    if (_reader.BaseStream.Position >= DataLength)
                        break;

                    byte metaType = _reader.ReadByte();
                    int metaLength = ReadVariableLength(_reader);

                    if (_reader.BaseStream.Position + metaLength > DataLength)
                        break;

                    byte[] metaData = _reader.ReadBytes(metaLength);
                    if (metaType == 0x2F) // End of track
                        break;

                    events.Add(new MidiEvent(ticks, (int)deltaTime, statusByte, metaType: metaType, metaData: metaData));
                }
                else if (statusByte >= 0x80 && statusByte <= 0xEF) // MIDI channel event
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
                    events.Add(new MidiEvent(ticks, (int)deltaTime, statusByte, data: data));

                    byte eventType = (byte)(statusByte & 0xF0);
                    byte velocity = data.Length > 1 ? data[1] : (byte)0;

                    if (eventType == 0x90 && velocity > 0)
                    {
                        //Console.WriteLine("noteon occured");
                    }
                }
                else if (statusByte == 0xF0 || statusByte == 0xF7) // SysEx event
                {
                    int sysexLength = ReadVariableLength(_reader);
                    if (_reader.BaseStream.Position + sysexLength > DataLength)
                        break;

                    byte[] sysexData = _reader.ReadBytes(sysexLength);
                    events.Add(new MidiEvent(ticks, (int)deltaTime, statusByte, sysExData: sysexData));
                }
                else
                {
                    throw new InvalidDataException($"Unknown status byte encountered: {statusByte:X2}");
                }
            }

            LastParsedTick = ticks;
            //Events.AddRange(events);
            return events;
        }
        
        public void ParseEventsFast()
        {
            ulong noteCountLocal = 0;
            byte runningStatus = 0;
            ulong ticks = 0;

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
