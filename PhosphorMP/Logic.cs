using System.Collections.Concurrent;
using C5;
using FastLINQ;
using PhosphorMP.Parser;
using PhosphorMP.Rendering;
using PhosphorMP.Rendering.Structs;

namespace PhosphorMP
{
    public class Logic : IDisposable
    {
        public static Logic Singleton { get; private set; }

        public MidiFile CurrentMidiFile
        {
            get => _currentMidiFile;
            internal set
            {
                _currentMidiFile = value;
                if (_currentMidiFile != null)
                {
                    int tempo = CurrentMidiFile.GetCurrentTempoAtTick(-1);
                    StartupDelayTicks = -Utils.Utils.CalculateTicks(3f, TempoChangeEvent.CalculateBpm(tempo), _currentMidiFile.TimeDivision);
                    CurrentTick = StartupDelayTicks;
                }
            }
        }

        public ulong PassedNotes { get; internal set; } = 0;
        public long CurrentTick { get; internal set; } = -int.MaxValue;
        public bool Playing { get; internal set; }

        private double _tickRemainder;
        private MidiFile _currentMidiFile;
        public int StartupDelayTicks { get; private set; } = -int.MaxValue;
        private FastList<MidiEvent> _events = [];

        private ConcurrentDictionary<(byte channel, byte note), (long startTick, int colorIndex)> _activeNotes = [];

        public Logic()
        {
            Singleton ??= this;
        }

        ~Logic() => Dispose();

        public void Run()
        {
            if (!Playing || CurrentMidiFile == null)
                return;

            if (CurrentTick >= CurrentMidiFile.TickCount)
            {
                CloseRemainingNotes();
                Renderer.Singleton.VisualNotes.Clear();
                Playing = false;
                return;
            }

            // Calculate tick advancement
            int tempo = CurrentMidiFile.GetCurrentTempoAtTick(CurrentTick);
            double microsecondsPerTick = tempo / (double)CurrentMidiFile.TimeDivision;
            double totalTicks = (Program.TargetDeltaTime * 1_000_000) / microsecondsPerTick + _tickRemainder;
            long ticksToAdvance = (long)totalTicks;
            _tickRemainder = totalTicks - ticksToAdvance;
            if (ticksToAdvance <= 0) return;

            // Parse events in the upcoming window
            _events = CurrentMidiFile.ParseEventsBetweenTicks(CurrentTick, CurrentTick + (CurrentMidiFile.TimeDivision * 4));
            CurrentMidiFile.WaitTilDone();

            var localVisualNotes = new ConcurrentBag<VisualNote>();

            // Process each event in parallel
            Parallel.ForEach(_events, Program.ParallelOptions, midiEvent =>
            {
                if (midiEvent.EventType != MidiEventType.Channel || midiEvent.Data.Length < 2) 
                    return;

                byte note = midiEvent.Data[0];
                byte velocity = midiEvent.Data[1];
                byte channel = (byte)(midiEvent.StatusByte & 0x0F);
                var key = (channel, note);

                if (midiEvent.IsNoteOn(velocity))
                {
                    // Close previous note if still active
                    if (_activeNotes.TryGetValue(key, out var prev))
                    {
                        localVisualNotes.Add(new VisualNote
                        {
                            StartingTick = prev.startTick,
                            DurationTick = (int)(midiEvent.Tick - prev.startTick),
                            Key = note,
                            ColorIndex = prev.colorIndex
                        });
                    }

                    // Add/update active note
                    _activeNotes[key] = (midiEvent.Tick, midiEvent.Track);
                }
                else if (midiEvent.IsNoteOff(velocity))
                {
                    // Remove from active notes and create a visual note
                    if (_activeNotes.TryRemove(key, out var startInfo))
                    {
                        localVisualNotes.Add(new VisualNote
                        {
                            StartingTick = startInfo.startTick,
                            DurationTick = (int)(midiEvent.Tick - startInfo.startTick),
                            Key = note,
                            ColorIndex = startInfo.colorIndex
                        });
                    }
                    else
                    { 
                        Console.WriteLine($"NoteOff at tick {midiEvent.Tick} for note {note} on channel {channel} without a prior NoteOn.");
                    }
                }
            });

            // Merge collected visual notes in tick order
            foreach (var vn in localVisualNotes.OrderBy(v => v.StartingTick))
                Renderer.Singleton.VisualNotes.Add(vn);

            CurrentTick += ticksToAdvance;
        }
        
        private void AddVisualNote(long startTick, int durationTicks, byte note, int colorIndex)
        {
            Renderer.Singleton.VisualNotes.Add(new VisualNote
            {
                StartingTick = startTick,
                DurationTick = durationTicks,
                Key = note,
                ColorIndex = colorIndex
            });
        }

        private void CloseRemainingNotes()
        {
            foreach (var kv in _activeNotes)
            {
                var key = kv.Key;
                var value = kv.Value;

                AddVisualNote(
                    value.startTick,
                    (int)(CurrentMidiFile.TickCount - value.startTick),
                    key.note,
                    value.colorIndex
                );
            }
            _activeNotes.Clear();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }

    public static class MidiEventExtensions
    {
        public static bool IsNoteOn(this MidiEvent e, byte velocity) =>
            (e.StatusByte & 0xF0) == 0x90 && velocity > 0;

        public static bool IsNoteOff(this MidiEvent e, byte velocity) =>
            (e.StatusByte & 0xF0) == 0x80 || ((e.StatusByte & 0xF0) == 0x90 && velocity == 0);
    }
}
