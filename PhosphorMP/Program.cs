using System.Diagnostics;
using System.Numerics;
using PhosphorMP.Audio;
using PhosphorMP.Parser;
using PhosphorMP.Rendering;
using Veldrid;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace PhosphorMP
{
    public class Program
    {
        static void Main()
        {
            // Create a window and graphics device
            _ = new Window();
            _ = new BassMidiWrapper();
            _ = new Renderer();
            //Console.WriteLine("Loading");
            //Stopwatch now = Stopwatch.StartNew();
            //MidiFile midi = new MidiFile(@"/run/media/memfrag/00AAB9F3AAB9E576/BA Rare ASDF Mode rev 1.1.mid");
            //Console.WriteLine($"Parsed in {now.ElapsedMilliseconds} ms");
            
            while (Window.Singleton.BaseSdl2Window.Exists)
            {
                Renderer.Singleton.Render();
                //Window.Singleton.BaseSdl2Window.PumpEvents();
            }
        }
    }
}