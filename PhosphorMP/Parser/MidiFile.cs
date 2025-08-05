using System;
using System.Collections.Concurrent;
using System.IO;

namespace PhosphorMP.Parser
{
    public class MidiFile : IDisposable
    {
        private readonly Stream _stream;
        private readonly BinaryReader _reader;
        
        // Public data here
        public ushort FormatType { get; private set; } = 0;
        public ulong TrackCount => (ulong)Tracks.Count;
        public ushort TrackCountHeader { get; private set; } = 0;
        public ushort TimeDivision { get; private set; } = 0;
        public readonly List<MidiTrack> Tracks = [];
        public TimeSpan Length { get; private set; }
        public ulong LastParsedTick { get; private set; } = 0;
        public string FilePath { get; init; }
        public string FileName => Path.GetFileName(FilePath);

        public ulong NoteCount
        {
            get
            {
                ulong count = 0;
                foreach (var track in Tracks)
                {
                    count += track.NoteCount;
                }
                return count;
            }
        }
        
        public ulong TickCount
        {
            get
            {
                if (Tracks.Count == 0)
                    return 0;

                ulong maxTicks = Tracks.Max(track => track.LengthInTicks);
                return (ulong)maxTicks;
            }
        }
        
        public MidiFile(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
            FilePath = filePath;
            Console.WriteLine("Parsing: " + FileName);
            _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_stream);
            ParseHeader();
            FindTracks();
            
            if (true)
            {
                Parallel.ForEach(Tracks, track =>
                {
                    track.ParseEventsFast();
                });
            }
            else
            {
                foreach (var track in Tracks)
                {
                    track.ParseEventsFast();
                }
            }
            
            MidiTrack.TempoChanges.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            Length = TimeSpan.FromSeconds(GetTimeInSeconds(TickCount));
            Utils.Utils.FreeGarbageHarder();
            Console.WriteLine($"Parsed with {TrackCount} (Header: {TrackCountHeader}) tracks, {TickCount} ticks ({Utils.Utils.FormatTime(Length)}) and {MidiTrack.TempoChanges.Count} tempo changes with PPQ of {TimeDivision} and {NoteCount} notes, streaming prepared.");
        }

        public List<MidiEvent> ParseEventsBetweenTicks(ulong startingTick, ulong endingTick)
        {
            List<MidiEvent> events = [];
            foreach (var track in Tracks)
            {
                events.AddRange(track.ParseEventsBetweenTicks(startingTick, endingTick));
            }
            LastParsedTick = endingTick;
            return events;
        }
        
        public int GetCurrentTempoAtTick(ulong currentTick)
        {
            if (MidiTrack.TempoChanges.Count == 0)
                return 500_000; // Default 120 BPM

            int currentTempo = 500_000; // Start with default
            foreach (var tempoEvent in MidiTrack.TempoChanges)
            {
                if (tempoEvent.Tick > currentTick)
                    break;

                currentTempo = tempoEvent.MicrosecondsPerQuarterNote;
            }

            return currentTempo;
        }
        
        public ulong GetTickAtElapsedSeconds(double seconds)
        {
            ulong tick = 0;
            double elapsed = 0;
            int currentTempo = 500_000; // default tempo (µs per quarter note)
            ulong lastTick = 0;

            foreach (var tempoEvent in MidiTrack.TempoChanges)
            {
                double deltaTime = ((tempoEvent.Tick - lastTick) * (ulong)currentTempo) / 1_000_000.0 / TimeDivision;

                if (elapsed + deltaTime > seconds)
                {
                    double remaining = seconds - elapsed;
                    tick += (ulong)((remaining * 1_000_000.0 * TimeDivision) / currentTempo);
                    return tick;
                }

                elapsed += deltaTime;
                tick = tempoEvent.Tick;
                lastTick = tempoEvent.Tick;
                
                currentTempo = tempoEvent.MicrosecondsPerQuarterNote;
            }

            // After last tempo change
            double remainingAfter = seconds - elapsed;
            tick += (ulong)((remainingAfter * 1_000_000.0 * TimeDivision) / currentTempo);
            return tick;
        }
        
        public double GetTimeInSeconds(ulong targetTick)
        {
            double totalTimeSeconds = 0;
            ulong lastTick = 0;
            uint ppq = TimeDivision;

            foreach (var tempoEvent in MidiTrack.TempoChanges)
            {
                ulong deltaTicks = tempoEvent.Tick - lastTick;

                // If the tempo event is past the target, break
                if (tempoEvent.Tick > targetTick)
                    break;

                double seconds = (deltaTicks * (uint)tempoEvent.MicrosecondsPerQuarterNote) / (ppq * 1_000_000.0);
                totalTimeSeconds += seconds;

                lastTick = tempoEvent.Tick;
            }

            // Remaining time from last tempo to target tick
            if (lastTick < targetTick)
            {
                // Use last known tempo
                var lastTempo = MidiTrack.TempoChanges.Count > 0 
                    ? MidiTrack.TempoChanges.Last() 
                    : new TempoChangeEvent(0, 500000, 120.0);
                ulong deltaTicks = targetTick - lastTick;
                double seconds = (deltaTicks * (uint)lastTempo.MicrosecondsPerQuarterNote) / (ppq * 1_000_000.0);
                totalTimeSeconds += seconds;
            }
            return totalTimeSeconds;
        }

        private void ParseHeader()
        {
            var chunkType = new string(_reader.ReadChars(4));
            if (chunkType != "MThd")
                throw new InvalidDataException("Not a valid MIDI file.");

            int headerLength = ReadBigEndianInt32();
            if (headerLength != 6)
                throw new InvalidDataException("Invalid MIDI header length.");

            FormatType = ReadBigEndianUInt16();
            if (FormatType != 1) 
                throw new InvalidDataException($"Invalid MIDI format type. (MIDI Type is {FormatType})");
            
            TrackCountHeader = ReadBigEndianUInt16();
            TimeDivision = ReadBigEndianUInt16();
            
            if ((TimeDivision & 0x8000) != 0)
                throw new FormatException("Invalid timing mode.");
        }

        private void FindTracks()
        {
            Dictionary<long, int> trackPositions = [];
            while (_reader.BaseStream.Position < _reader.BaseStream.Length)
            {
                string chunkType = new string(_reader.ReadChars(4));
                if (chunkType != "MTrk")
                {
                    int skipLength = ReadBigEndianInt32();
                    _reader.BaseStream.Seek(skipLength, SeekOrigin.Current);
                    continue;
                }

                int length = ReadBigEndianInt32();
                long trackStart = _reader.BaseStream.Position;
                
                trackPositions.Add(trackStart, length);
                // Advance stream position to the end of the track chunk
                _reader.BaseStream.Seek(trackStart + length, SeekOrigin.Begin);
            }
            Console.WriteLine($"Found {trackPositions.Count} track positions, now creating MidiTrack classes.");
            
            foreach (var trackPosition in trackPositions)
            {
                MidiTrack track = new(FilePath, trackPosition.Key, trackPosition.Value);
                Tracks.Add(track);
            }
        }

        private int ReadVariableLengthQuantity()
        {
            int value = 0;
            byte b;
            do
            {
                b = _reader.ReadByte();
                value = (value << 7) | (b & 0x7F);
            } while ((b & 0x80) != 0);
            return value;
        }

        private ushort ReadBigEndianUInt16()
        {
            return (ushort)((_reader.ReadByte() << 8) | _reader.ReadByte());
        }

        private int ReadBigEndianInt32()
        {
            return (_reader.ReadByte() << 24) |
                   (_reader.ReadByte() << 16) |
                   (_reader.ReadByte() << 8) |
                   _reader.ReadByte();
        }

        ~MidiFile() => Dispose();

        public void Dispose()
        {
            _reader?.Dispose();
            _stream?.Dispose();
            foreach (var track in Tracks)
            {
                track.Dispose();
            }
            Utils.Utils.FreeGarbageHarder();
            GC.SuppressFinalize(this);
        }
    }
}
