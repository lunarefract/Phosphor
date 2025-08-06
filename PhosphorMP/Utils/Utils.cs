using PhosphorMP.Rendering;

namespace PhosphorMP.Utils
{
    public static class Utils
    {
        public static string FormatTime(TimeSpan time)
        {
            TimeSpan absTime = time.Duration(); // absolute value of time
            string sign = time.Ticks < 0 ? "-" : "";

            return $"{sign}{(int)absTime.TotalMinutes:00}:{absTime.Seconds:00}.{absTime.Milliseconds / 100}";
        }
        
        public static int MakeLong(int low, int high)
        {
            return (high << 16) | (low & 0xFFFF);
        }

        public static void FreeGarbageHarder()
        {
            GC.Collect();                   // Forces a garbage collection.
            GC.WaitForPendingFinalizers();  // Waits for finalizers to complete (if any objects need it).
            GC.Collect();                   // Optional second pass to collect finalized objects.
        }
        
        public static string GetPercentage(double part, double total)
        {
            if (total == 0)
                return "N/A";
            double percentage = (part / total) * 100;
            return $"{percentage:F2}%";
        }
        
        public static List<VisualNote> GenerateKeyboardSweep()
        {
            const int minKey = 0;
            const int maxKey = 127;
            const int noteDuration = 10;
            const int tickGap = 10;

            List<VisualNote> sweepNotes = [];

            for (int i = 0; i <= maxKey - minKey; i++)
            {
                int startTick = i * (noteDuration + tickGap);
                byte key = (byte)(minKey + i);

                sweepNotes.Add(new VisualNote
                {
                    StartingTick = startTick,
                    DurationTick = noteDuration,
                    Key = key
                });
            }

            return sweepNotes;
        }

        public static int CalculateTicks(double seconds, double bpm, int ppq)
        {
            double beatsPerSecond = bpm / 60.0;
            double totalBeats = beatsPerSecond * seconds;
            int ticks = (int)(totalBeats * ppq);
            return ticks;
        }
    }
}

