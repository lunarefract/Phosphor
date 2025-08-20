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
                    StartupDelayTicks = -Utils.Utils.CalculateTicks(3f, 120, _currentMidiFile.TimeDivision);
                    CurrentTick = StartupDelayTicks;
                }
            }
        }

        public ulong PassedNotes { get; internal set; } = 0;
        public long CurrentTick { get; internal set; }
        public bool Playing { get; internal set; }

        private double _tickRemainder;
        private MidiFile _currentMidiFile;
        public int StartupDelayTicks { get; private set; } = -4800;
        private FastList<MidiEvent> _events = [];
        
        private readonly ArrayList<(byte note, long startTick, int colorIndex)> _activeNotes = [];

        public Logic()
        {
            Singleton ??= this;
        }

        ~Logic() => Dispose();

        public void PlaybackLogic()
        {
            if (CurrentMidiFile == null)
                return;

            // If at the end of the song and nothing left to show → clear visual notes
            if (CurrentTick >= CurrentMidiFile.TickCount && Renderer.Singleton.VisualNotes.Count != 0)
            {
                Renderer.Singleton.VisualNotes.Clear();
            }

            if (!Playing)
                return;

            Renderer.Singleton.VisualNotes.Clear();

            // End of song reached
            if (CurrentTick >= CurrentMidiFile.TickCount)
            {
                //CloseRemainingNotes();
                Playing = false;
                return;
            }

            // ---- Timing calculation ----
            int tempo = CurrentMidiFile.GetCurrentTempoAtTick(CurrentTick);
            double microsecondsPerTick = tempo / (double)CurrentMidiFile.TimeDivision;

            double totalTicks = (Program.TargetDeltaTime * 1_000_000) / microsecondsPerTick + _tickRemainder;
            long ticksToAdvance = (long)totalTicks;
            _tickRemainder = totalTicks - ticksToAdvance;

            if (ticksToAdvance <= 0)
                return;

            // ---- Event parsing ----
            _events = CurrentMidiFile.ParseEventsBetweenTicks(
                CurrentTick,
                CurrentTick + CurrentMidiFile.TimeDivision * 2
            );
            CurrentMidiFile.WaitTilDone();

            var localVisualNotes = new ArrayList<(long startTick, int duration, byte note, int colorIndex)>();

            // ---- Process events ----
            foreach (var midiEvent in _events.OrderBy(e => e.Tick).ThenBy(e => e.StatusByte))
            {
                if (midiEvent is { EventType: MidiEventType.Channel, Data.Length: >= 2 })
                {
                    byte note = midiEvent.Data[0];
                    byte velocity = midiEvent.Data[1];
                    int track = midiEvent.Track;

                    if (midiEvent.IsNoteOn(velocity))
                    {
                        // Register active note (allow overlaps: stack-like behavior)
                        _activeNotes.Add((note, midiEvent.Tick, track));
                    }
                    else if (midiEvent.IsNoteOff(velocity))
                    {
                        // Match most recent active note with same track+note
                        int index = _activeNotes.FindLastIndex(x => x.note == note && x.colorIndex == track);
                        if (index >= 0)
                        {
                            var noteInfo = _activeNotes[index];
                            _activeNotes.RemoveAt(index);

                            localVisualNotes.Add((
                                noteInfo.startTick,
                                (int)(midiEvent.Tick - noteInfo.startTick),
                                noteInfo.note,
                                noteInfo.colorIndex
                            ));
                        }
                    }
                }
            }

            // ---- Add finished visual notes ----
            foreach (var vn in localVisualNotes.OrderBy(v => v.startTick))
            {
                AddVisualNote(vn.startTick, vn.duration, vn.note, vn.colorIndex);
            }

            // Advance playback time
            CurrentTick += ticksToAdvance;
        }
        private void AddVisualNote(long startTick, int durationTicks, byte note, int track)
        {
            Renderer.Singleton.VisualNotes.Add(new VisualNote
            {
                StartingTick = startTick,
                DurationTick = durationTicks,
                Key = note,
                ColorIndex = track
            });
        }

        private void CloseRemainingNotes()
        {
            foreach (var noteInfo in _activeNotes)
            {
                AddVisualNote(noteInfo.startTick, (int)(CurrentMidiFile.TickCount - noteInfo.startTick), noteInfo.note, noteInfo.colorIndex);
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
