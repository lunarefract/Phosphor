using System.Diagnostics;
using C5;
using FastLINQ;
using PhosphorMP.Extensions;

namespace PhosphorMP.Parser
{
    public class MidiFile : IDisposable
    {
        private readonly Stream _stream;
        private readonly BinaryReader _reader;
        private readonly ManualResetEventSlim _waitHandle = new(false);
        
        // Public data here
        public ushort FormatType { get; private set; } = 0;
        public ulong TrackCount => (ulong)Tracks.Count;
        public ushort TrackCountHeader { get; private set; } = 0;
        public ushort TimeDivision { get; private set; } = 0;
        public readonly ArrayList<MidiTrack> Tracks = [];
        public TimeSpan Length { get; private set; }
        public long LastParsedTick { get; private set; } = 0;
        public List<TempoChangeEvent> TempoChanges { get; } = [];
        public double StartingTickrate { get; private set; }
        public string FilePath { get; init; }
        public string FileName => Path.GetFileName(FilePath);
        public const int FileStreamBufferSize = 1024 * 1024; // 1 MB buffer (default is 4 KB)

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
        
        public long TickCount
        {
            get
            {
                if (Tracks.Count == 0) return 0;
                return Tracks.Max(track => track.LengthInTicks);
            }
        }
        
        public MidiFile(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("File does not exist: " + filePath);
            FilePath = filePath;
            Console.WriteLine("Parsing: " + FileName);
            _stream = new MemoryMappedFileStream(filePath);
            _reader = new BinaryReader(_stream);
            ParserStats.Stage = ParserStage.CheckingHeader;
            ParseHeader();
            FindTracks();
            ParserStats.Stage = ParserStage.PreparingForStreaming;
            
            Parallel.ForEach(Tracks, Program.ParallelOptions, track =>
            {
                track.ParseEventsFast();
                ParserStats.PreparingForStreamingCount++;
            });
            
            TempoChanges.Sort((a, b) => a.Tick.CompareTo(b.Tick)); // TODO: Add parser stage for this

            Length = TimeSpan.FromSeconds(GetTimeInSeconds(TickCount));
            StartingTickrate = GetTickRateAtTick(-1);
            Utils.Utils.FreeGarbageHarder();
            Console.WriteLine($"Parsed with {TrackCount} (Header: {TrackCountHeader}) tracks, {TickCount} ticks ({Utils.Utils.FormatTime(Length)}) and {TempoChanges.Count} tempo changes with PPQ of {TimeDivision} and {NoteCount} notes, streaming prepared.");
            ParserStats.Stage = ParserStage.Streaming;
            ParserStats.CreatedTrackClasses = 0;
            ParserStats.FoundTrackPositions = 0;
            ParserStats.PreparingForStreamingCount = 0;
        }

        public FastList<MidiEvent> ParseEventsBetweenTicks(long startingTick, long endingTick)
        {
            _waitHandle.Reset();

            int trackCount = Tracks.Count;
            var results = new FastList<MidiEvent>[trackCount];
            
            Parallel.For(0, trackCount, Program.ParallelOptions, i =>
            {
                results[i] = Tracks[i].ParseEventsBetweenTicks(startingTick, endingTick);
            });
            
            for (int i = 0; i < trackCount; i++) // TODO: Remove this sanity check
            {
                Tracks[i].WaitTilDone();
            }

            var events = new FastList<MidiEvent>();

            // Merge efficiently using FastList.Iterator
            for (int i = 0; i < trackCount; i++)
            {
                var iter = results[i].Iterate();
                while (iter.MoveNext(out var ev))
                {
                    events.Add(ev);
                }
            }

            LastParsedTick = endingTick;
            _waitHandle.Set(); // signal that parsing is complete
            return events;
        }
        
        public void WaitTilDone()
        {
            _waitHandle.Wait();
        }
        
        public int GetCurrentTempoAtTick(long currentTick)
        {
            int currentTempo = 500_000; // Start with default
            if (TempoChanges.Count == 0) return currentTempo;
            
            if (currentTick >= 0)
            {
                foreach (var tempoEvent in TempoChanges)
                {
                    if (tempoEvent.Tick > currentTick)
                        break;

                    currentTempo = tempoEvent.MicrosecondsPerQuarterNote;
                }
            }
            else
            {
                currentTempo = TempoChanges[0].MicrosecondsPerQuarterNote;
            }
            return currentTempo;
        }
        
        public double GetTimeInSeconds(long targetTick)
        {
            double totalTimeSeconds = 0;
            long lastTick = 0;
            uint ppq = TimeDivision;

            int index = 0;
            while (index < TempoChanges.Count && TempoChanges[index].Tick <= targetTick)
            {
                var tempoEvent = TempoChanges[index];

                long deltaTicks = tempoEvent.Tick - lastTick;
                double seconds = deltaTicks * tempoEvent.MicrosecondsPerQuarterNote / (ppq * 1_000_000.0);
                totalTimeSeconds += seconds;

                lastTick = tempoEvent.Tick;
                index++;
            }

            // If the last tempo event is before the target tick, continue with its tempo
            if (lastTick < targetTick)
            {
                int tempo = TempoChanges.Count > 0 
                    ? TempoChanges[Math.Max(0, index - 1)].MicrosecondsPerQuarterNote 
                    : 500_000; // default tempo

                long deltaTicks = targetTick - lastTick;
                double seconds = deltaTicks * tempo / (ppq * 1_000_000.0);
                totalTimeSeconds += seconds;
            }

            return totalTimeSeconds;
        }
        
        public double GetTickRateAtTick(long tick)
        {
            int tempo = GetCurrentTempoAtTick(tick); // microseconds per quarter note
            double ticksPerSecond = TimeDivision * (1_000_000.0 / tempo);
            return ticksPerSecond;
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
            ParserStats.Stage = ParserStage.FindingTracksPositions;
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
                ParserStats.FoundTrackPositions++;

                // Advance stream position to the end of the track chunk
                _reader.BaseStream.Seek(trackStart + length, SeekOrigin.Begin);
            }

            // Sort by track start position (smallest to largest)
            var sortedTrackPositions = trackPositions
                .OrderBy(tp => tp.Key)
                .ToList();

            Console.WriteLine($"Found {sortedTrackPositions.Count} track positions, now creating MidiTrack classes.");

            ParserStats.Stage = ParserStage.CreatingTrackClasses;
            Parallel.For(0, sortedTrackPositions.Count, Program.ParallelOptions, i =>
            {
                var tp = sortedTrackPositions[i];
                var track = new MidiTrack(FilePath, tp.Key, tp.Value, i, this);
                lock (Tracks)
                {
                    Tracks.Add(track);
                    ParserStats.CreatedTrackClasses++;
                }
            });
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
            ParserStats.Stage = ParserStage.Idle;
            GC.SuppressFinalize(this);
        }
    }
}
