namespace PhosphorMP.Parser
{
    public record TempoChangeEvent(
        long Tick,                       // The tick at which the tempo change occurs
        int MicrosecondsPerQuarterNote  // 3-byte tempo value from the MIDI file
        //double Bpm                       // Calculated tempo in Beats Per Minute
        // TODO: Do a method to calculate Bpm
    );
}