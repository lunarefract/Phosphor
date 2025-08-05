namespace PhosphorMP.Utils
{
    public static class Utils
    {
        public static string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
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
    }
}

