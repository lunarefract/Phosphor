using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using PhosphorMP.Audio;
using PhosphorMP.Rendering;
using PhosphorMP.Rendering.Enums;

namespace PhosphorMP
{
    public class Program
    {
        public static float TargetDeltaTime
        {
            get
            {
                var fd = Renderer.Singleton.Framedumper;
                if (fd is { Active: true })
                {
                    switch (fd.DeltaTimeType)
                    {
                        case FramedumpDeltaTimeType.NonRealtime:
                            return 1f / fd.FPS;
                        case FramedumpDeltaTimeType.RealtimeSlowdown:
                            throw new NotImplementedException();
                    }
                }
                return DeltaTime;
            }
        }

        public static float DeltaTime { get; private set; }
        
        public static ParallelOptions ParallelOptions { get; private set; } = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 16, // TODO: Make this internally customizable
            TaskScheduler = null // TODO: Tune this later
        };

        static void Main() // TODO: Console window Title Suffix field
        {
            Console.WriteLine($"Using {Environment.ProcessorCount * 16} threads by default");
            var config = new SerializableConfig(true);
            var logic = new Logic();
            var window = new Window();
            //_ = new BassMidiWrapper();
            var renderer = new Renderer();
            var stopwatch = new Stopwatch();
            
            stopwatch.Start();
            double lastTime = stopwatch.Elapsed.TotalSeconds;
            
            while (Window.Singleton.BaseSdl2Window.Exists)
            {
                double currentTime = stopwatch.Elapsed.TotalSeconds;
                double deltaSeconds = currentTime - lastTime;

                if (deltaSeconds <= 0 || double.IsInfinity(deltaSeconds) || double.IsNaN(deltaSeconds))
                    deltaSeconds = 1.0 / 60.0;

                DeltaTime = (float)deltaSeconds;
                
                logic.Run();
                Renderer.Singleton.Render();

                lastTime = currentTime;
            }
        }
    }
}