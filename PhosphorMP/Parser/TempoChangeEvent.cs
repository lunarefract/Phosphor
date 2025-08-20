namespace PhosphorMP.Parser
{
    public record TempoChangeEvent(
        long Tick,
        int MicrosecondsPerQuarterNote
    )
    {
        public double Bpm => 60_000_000.0 / MicrosecondsPerQuarterNote;
        
        public static double CalculateBpm(int microsecondsPerQuarterNote)
        {
            return 60_000_000.0 / microsecondsPerQuarterNote;
        }
    }
}