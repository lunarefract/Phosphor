namespace PhosphorMP.Rendering
{
    public class Framedumper : IDisposable
    {
        public bool Active { get; private set; } = false;
        public ulong FrameCount { get; private set; } = 0;

        public FramedumpDeltaTimeType DeltaTimeType
        {
            get => _deltaTimeType;
            set
            {
                if (Active) throw new InvalidOperationException("Cannot change FramedumpDeltaTimeType while framedumper is active.");
                _deltaTimeType = value;
            }
        }
        private FramedumpDeltaTimeType _deltaTimeType = FramedumpDeltaTimeType.NonRealtime;

        public uint FPS
        {
            get => _fps;
            set
            {
                if (value >= 241) throw new ArgumentException("This value is too much for Frameduper to handle. (Max: 240)");
                _fps = value;
            }
        }
        private uint _fps;

        public Framedumper()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "framedump");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public void Start()
        {
            FrameCount = 0;
            Active = true;
        }

        public void Stop()
        {
            Active = false;
        }
        
        public void HandleFrame()
        {
            if (!Active) return;
            Renderer.Singleton.VisualizationFramebuffer.CaptureOutputAsync(Path.Combine(Directory.GetCurrentDirectory(), "framedump", $"{FrameCount}.png"));
            FrameCount++;
        }
        
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}

