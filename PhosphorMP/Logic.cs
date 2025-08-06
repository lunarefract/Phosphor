using System.Globalization;
using PhosphorMP.Parser;

namespace PhosphorMP
{
    public class Logic : IDisposable
    {
        public static Logic Singleton { get; private set; }
        // ---
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
        public ulong PassedNotes { get; internal set; } = 0; // TODO: Check if can be made private
        public long CurrentTick { get; internal set; } = 0;
        public bool Playing { get; internal set; } = false;
        
        private double _tickRemainder = 0.0;
        private MidiFile _currentMidiFile;
        public int StartupDelayTicks { get; private set; } = -4800;
        
        public Logic()
        {
            Singleton ??= this;
        }
        ~Logic() => Dispose();
        
        public void PlaybackLogic()
        {
            if (!Playing || CurrentMidiFile == null)
                return;
            
            if (CurrentTick >= (long)CurrentMidiFile.TickCount)
            {
                Playing = false;
                return;
            }
            
            int tempo = CurrentMidiFile.GetCurrentTempoAtTick(CurrentTick);
            double microsecondsPerTick = tempo / (double)CurrentMidiFile.TimeDivision;

            double totalTicks = (Program.DeltaTime * 1_000_000) / microsecondsPerTick + _tickRemainder;
            long ticksToAdvance = (long)totalTicks;
            _tickRemainder = totalTicks - ticksToAdvance;

            if (ticksToAdvance == 0)
                return;
            
            // Parse new events between last tick and now + bar
            long from = CurrentMidiFile.LastParsedTick;
            long to = CurrentTick;
            var events = CurrentMidiFile.ParseEventsBetweenTicks(from, to);
            
            foreach (var midiEvent in events)
            {
                if (midiEvent.GetEventType() == MidiEventType.Channel && midiEvent.Data?.Length >= 2)
                {
                    byte status = midiEvent.StatusByte;
                    byte velocity = midiEvent.Data[1];
                    byte note = midiEvent.Data[0];
                    int channel = status & 0x0F;
                    
                    if ((status & 0xF0) == 0x90 && velocity > 0)
                    {
                        PassedNotes++;
                    }
                    else if ((status & 0xF0) == 0x80 || ((status & 0xF0) == 0x90 && velocity == 0))
                    {
                        
                    }
                }
            }
            
            CurrentTick += ticksToAdvance;
        }
        
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}