using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace PhosphorMP.Rendering
{
    public class Window
    {
        public static Window Singleton { get; private set; }
        public Sdl2Window BaseSdl2Window { get; private set; }
        
        public Window()
        {
            if (Singleton == null)
            {
                Singleton = this;
            }
            // Else throw exception maybe
            Init();
        }

        private void Init()
        {
            var windowCI = new WindowCreateInfo(100, 100, 1920, 1080, WindowState.Normal, "Phosphor");
            BaseSdl2Window = VeldridStartup.CreateWindow(ref windowCI);
        }
    }
}

