using System.Diagnostics;
using PhosphorMP.Audio;
using PhosphorMP.Rendering;

namespace PhosphorMP
{
    public class Program
    {
        public static float DeltaTime { get; private set; }
        
        static void Main() // TODO: Console window Title Suffix field
        {
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
                //window.BaseSdl2Window.Title = $"Phosphor [{(1f / Program.DeltaTime):0.0} FPS]"; TODO: Remove soon or replace, fucks up waybar
                double currentTime = stopwatch.Elapsed.TotalSeconds;
                DeltaTime = (float)(currentTime - lastTime);
                if (logic.CurrentMidiFile != null)
                {
                    logic.PlaybackLogic();
                }
                Renderer.Singleton.Render();
                lastTime = currentTime;
            }
        }
    }
}