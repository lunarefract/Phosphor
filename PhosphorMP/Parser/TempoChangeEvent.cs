namespace PhosphorMP.Parser
{
    public record TempoChangeEvent(
        ulong Tick,                       // The tick at which the tempo change occurs
        int MicrosecondsPerQuarterNote,  // 3-byte tempo value from the MIDI file
        double Bpm                       // Calculated tempo in Beats Per Minute
    );
}