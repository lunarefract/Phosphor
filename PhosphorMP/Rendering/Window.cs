using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace PhosphorMP.Rendering
{
    public class Window // TODO: Window Title Suffix field
    {
        public static Window Singleton { get; private set; }
        public Sdl2Window BaseSdl2Window { get; private set; }
        
        public Window()
        {
            if (Singleton == null)
            {
                Singleton = this;
            }
            else
            {
                throw new Exception("Singleton already exists, there must be only one window instance.");
            }
            Init();
        }

        private void Init()
        {
            var windowCi = new WindowCreateInfo(100, 100, 1920, 1080, WindowState.Normal, "Phosphor");
            BaseSdl2Window = VeldridStartup.CreateWindow(ref windowCi);
        }
    }
}

