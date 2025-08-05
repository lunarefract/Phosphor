using PhosphorMP.Audio;
using PhosphorMP.Rendering;

namespace PhosphorMP
{
    public class Program
    {
        static void Main() // TODO: Console window Title Suffix field
        {
            _ = new SerializableConfig(true);
            _ = new Window();
            //_ = new BassMidiWrapper();
            _ = new Renderer();
            
            while (Window.Singleton.BaseSdl2Window.Exists)
            {
                Renderer.Singleton.Render();
                //Window.Singleton.BaseSdl2Window.PumpEvents();
            }
        }
    }
}