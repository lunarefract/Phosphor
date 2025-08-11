using System.Collections.Concurrent;
using C5;
using PhosphorMP.Parser;
using PhosphorMP.Rendering;

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
                    StartupDelayTicks = -Utils.Utils.CalculateTicks(3f, 120, _currentMidiFile.TimeDivision);
                    CurrentTick = StartupDelayTicks;
                    _events = CurrentMidiFile.ParseEventsBetweenTicks(0, CurrentMidiFile.TickCount).ToArray();
                }
            }
        }

        public ulong PassedNotes { get; internal set; } = 0;
        public long CurrentTick { get; internal set; }
        public bool Playing { get; internal set; }

        private double _tickRemainder;
        private MidiFile _currentMidiFile;
        public int StartupDelayTicks { get; private set; } = -4800;
        private MidiEvent[] _events = [];
        
        private readonly HashDictionary<(byte channel, byte note), (long startTick, int track)> _activeNotes = [];
        
        public Logic()
        {
            Singleton ??= this;
        }

        ~Logic() => Dispose();

        public void PlaybackLogic()
        {
            if (!Playing || CurrentMidiFile == null)
                return;

            Renderer.Singleton.VisualNotes.Clear();

            if (CurrentTick >= CurrentMidiFile.TickCount)
            {
                CloseRemainingNotes();
                Playing = false;
                return;
            }

            int tempo = CurrentMidiFile.GetCurrentTempoAtTick(CurrentTick);
            double microsecondsPerTick = tempo / (double)CurrentMidiFile.TimeDivision;

            double totalTicks = (Program.DeltaTime * 1_000_000) / microsecondsPerTick + _tickRemainder;
            long ticksToAdvance = (long)totalTicks;
            _tickRemainder = totalTicks - ticksToAdvance;
            
            if (ticksToAdvance <= 0)
                return;
            
            //_events = CurrentMidiFile.ParseEventsBetweenTicks(CurrentTick, CurrentTick + (CurrentMidiFile.TimeDivision * 4));

            var trackGroups = _events.GroupBy(e => e.Track).ToArray();

            var localVisualNotes = new ConcurrentBag<(long startTick, int duration, byte note, byte channel, int track)>();
            //int passedNotesCounter = 0;

            Parallel.ForEach(trackGroups, trackEvents =>
            {
                var localActiveNotes = new HashDictionary<(byte channel, byte note), (long startTick, int track)>();

                foreach (var midiEvent in trackEvents) // .OrderBy(e => e.Tick)
                {
                    if (midiEvent.EventType == MidiEventType.Channel && midiEvent.Data?.Length >= 2)
                    {
                        byte note = midiEvent.Data[0];
                        byte velocity = midiEvent.Data[1];
                        byte channel = (byte)(midiEvent.StatusByte & 0x0F);

                        if (midiEvent.IsNoteOn(velocity))
                        {
                            //Interlocked.Increment(ref passedNotesCounter);
                            localActiveNotes[(channel, note)] = (midiEvent.Tick, midiEvent.Track);
                        }
                        else if (midiEvent.IsNoteOff(velocity))
                        {
                            var key = (channel, note);
                            if (localActiveNotes.Find(ref key, out var noteInfo))
                            {
                                localVisualNotes.Add((noteInfo.startTick, (int)(midiEvent.Tick - noteInfo.startTick), note, channel, noteInfo.track));
                                localActiveNotes.Remove((channel, note));
                            }
                        }
                    }
                }

                // Any remaining active notes on this track will stay open for now
                lock (_activeNotes)
                {
                    foreach (var kv in localActiveNotes)
                        _activeNotes[kv.Key] = kv.Value;
                }
            });

            //PassedNotes += (ulong)passedNotesCounter;

            // Merge visual notes in tick order
            foreach (var vn in localVisualNotes.OrderBy(v => v.startTick))
                AddVisualNote(vn.startTick, vn.duration, vn.note, vn.channel, vn.track);

            CurrentTick += ticksToAdvance;
        }

        private void AddVisualNote(long startTick, int durationTicks, byte note, byte channel, int track)
        {
            Renderer.Singleton.VisualNotes.Add(new VisualNote
            {
                StartingTick = startTick,
                DurationTick = durationTicks,
                Key = note,
                Channel = channel,
                Track = track
            });
        }

        private void CloseRemainingNotes()
        {
            foreach (var kvp in _activeNotes)
            {
                var (channel, note) = kvp.Key;
                var (startTick, track) = kvp.Value;
                AddVisualNote(startTick, (int)(CurrentMidiFile.TickCount - startTick), note, channel, track);
            }
            _activeNotes.Clear();
        }
        
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
    
    public static class MidiEventExtensions // TODO: Shrug? Is this a util?
    {
        public static bool IsNoteOn(this MidiEvent e, byte velocity) =>
            (e.StatusByte & 0xF0) == 0x90 && velocity > 0;

        public static bool IsNoteOff(this MidiEvent e, byte velocity) =>
            (e.StatusByte & 0xF0) == 0x80 || ((e.StatusByte & 0xF0) == 0x90 && velocity == 0);
    }
}
