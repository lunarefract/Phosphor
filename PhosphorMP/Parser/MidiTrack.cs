using FastLINQ;
using PhosphorMP.Extensions;

namespace PhosphorMP.Parser
{
    public class MidiTrack : IDisposable
    {
        public int Id { get; init; }
        public long TrackStartPosition { get; init; }
        public int DataLength { get; init; }
        public long LengthInTicks { get; private set; } = 0;
        public static List<TempoChangeEvent> TempoChanges { get; } = [];
        public ulong NoteCount { get; private set; } = 0;
        public long LastReaderStreamPosition { get; internal set; } = 0;

        private readonly RestrictedFileStream _trackData;
        private readonly BinaryReader _reader;
        private long _lastParsedTick = 0;
        private byte _lastRunningStatus = 0;
        private readonly ManualResetEventSlim _waitHandle = new(false);
        private bool _wait = false;

        public MidiTrack(string baseMidiPath, long position, int dataLength, int trackId)
        {
            TrackStartPosition = position;
            DataLength = dataLength;
            Id = trackId;

            _trackData = new RestrictedFileStream(baseMidiPath, FileMode.Open, position, dataLength);
            _reader = new BinaryReader(_trackData);
        }
        
        public FastList<MidiEvent> ParseEventsBetweenTicks(long startingTick, long endingTick, bool resetPosition = false) // TODO: BUGFIX -> Find noteoffs of noteons even if beyond endingTick!!!!!!!!!!!
        {
            _wait = true;
            _waitHandle.Reset();
            FastList<MidiEvent> events = [];
            
            if (resetPosition || startingTick < _lastParsedTick)
            {
                _reader.BaseStream.Position = 0;
                _lastParsedTick = 0;
                LastReaderStreamPosition = 0;
                _lastRunningStatus = 0;
            }
            else
            {
                // Resume where we left off
                _reader.BaseStream.Position = LastReaderStreamPosition;
            }

            long ticks = _lastParsedTick;
            byte runningStatus = _lastRunningStatus;

            while (_reader.BaseStream.Position < DataLength)
            {
                int deltaTime = ReadVariableLength(_reader);
                long eventTick = ticks + deltaTime; // Tick at which this event occurs

                if (eventTick > endingTick)
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

                MidiEvent? midiEvent = null;

                if (statusByte == 0xFF) // Meta
                {
                    byte metaType = _reader.ReadByte();
                    int metaLength = ReadVariableLength(_reader);
                    byte[] metaData = _reader.ReadBytes(metaLength);

                    if (metaType == 0x2F)
                        break;

                    midiEvent = new MidiEvent(eventTick, (int)deltaTime, statusByte, metaType: metaType, metaData: metaData, track: Id);
                }
                else if (statusByte >= 0x80 && statusByte <= 0xEF) // Channel
                {
                    int dataBytes = statusByte switch
                    {
                        >= 0x80 and < 0xC0 => 2,
                        >= 0xC0 and < 0xE0 => 1,
                        >= 0xE0 and <= 0xEF => 2,
                        _ => throw new InvalidDataException($"Unknown MIDI status byte: {statusByte:X2}")
                    };

                    byte[] data = _reader.ReadBytes(dataBytes);
                    midiEvent = new MidiEvent(eventTick, (int)deltaTime, statusByte, data: data, track: Id);
                }
                else if (statusByte == 0xF0 || statusByte == 0xF7) // SysEx
                {
                    int sysexLength = ReadVariableLength(_reader);
                    byte[] sysexData = _reader.ReadBytes(sysexLength);
                    midiEvent = new MidiEvent(eventTick, (int)deltaTime, statusByte, sysExData: sysexData, track: Id);
                }

                // Only add events if their tick is in the requested range
                if (midiEvent != null && eventTick >= startingTick)
                    events.Add(midiEvent.Value);

                ticks = eventTick; // update current tick
                _lastParsedTick = ticks;
                LastReaderStreamPosition = _reader.BaseStream.Position;
                _lastRunningStatus = runningStatus;
            }
            _wait = false;
            _waitHandle.Set(); // signal that parsing is complete
            return events;
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
        
        public void WaitTilDone()
        {
            _waitHandle.Wait();
        }
        
        private int ReadVariableLength(BinaryReader reader, out int bytesRead)
        {
            int value = 0;
            byte b;
            bytesRead = 0;
            do
            {
                if (reader.BaseStream.Position >= DataLength)
                    throw new EndOfStreamException("Reached end of track data while reading variable length quantity.");

                b = reader.ReadByte();
                bytesRead++;
                value = (value << 7) | (b & 0x7F);
            } while ((b & 0x80) != 0);
            return value;
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
        
        public void Dispose()
        {
            _trackData?.Dispose();
            _reader?.Dispose();
            //Events.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
