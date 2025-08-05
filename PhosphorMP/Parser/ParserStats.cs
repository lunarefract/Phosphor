namespace PhosphorMP.Parser
{
    public static class ParserStats
    {
        public static ParserStage Stage { get; internal set; } = ParserStage.Idle;
        
        public static long FoundTrackPositions { get; internal set; } = 0;
        public static long CreatedTrackClasses { get; internal set; } = 0;
        public static long PreparingForStreamingCount { get; internal set; } = 0;
    }

    public enum ParserStage : byte
    {
        Idle,
        CheckingHeader,
        FindingTracksPositions,
        CreatingTrackClasses,
        PreparingForStreaming,
        Streaming,
    }
}

