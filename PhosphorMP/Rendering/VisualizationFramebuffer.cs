using Veldrid;

namespace PhosphorMP.Rendering
{
    public class VisualizationFramebuffer : IDisposable
    {
        public Framebuffer Base { get; internal set; }
        private Texture _colorTarget;
        private Texture _depthTarget;
        private TextureView _colorTargetView;

        private uint _width;
        private uint _height;
        
        public TextureView ColorTargetView => _colorTargetView;

        public VisualizationFramebuffer()
        {
            _width = (uint)Renderer.Singleton.GraphicsDevice.MainSwapchain.Framebuffer.Width;
            _height = (uint)Renderer.Singleton.GraphicsDevice.MainSwapchain.Framebuffer.Height;

            CreateFramebuffer();
        }

        private void CreateFramebuffer()
        {
            // Dispose old resources if they exist
            Base?.Dispose();
            _colorTarget?.Dispose();
            _depthTarget?.Dispose();
            _colorTargetView?.Dispose();

            // Create color texture
            _colorTarget = Renderer.Singleton.GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                _width,
                _height,
                mipLevels: 1,
                arrayLayers: 1,
                format: PixelFormat.R8_G8_B8_A8_UNorm, // Can be HDR if needed
                usage: TextureUsage.RenderTarget | TextureUsage.Sampled));

            // Create depth texture
            _depthTarget = Renderer.Singleton.GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                _width,
                _height,
                mipLevels: 1,
                arrayLayers: 1,
                format: PixelFormat.D32_Float_S8_UInt,
                usage: TextureUsage.DepthStencil));

            // Create texture view for sampling later
            _colorTargetView = Renderer.Singleton.GraphicsDevice.ResourceFactory.CreateTextureView(_colorTarget);

            // Create framebuffer object
            Base = Renderer.Singleton.GraphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(
                depthTarget: _depthTarget,
                colorTargets: [_colorTarget]));
        }

        public void Resize(uint newWidth, uint newHeight)
        {
            if (newWidth != _width || newHeight != _height)
            {
                _width = newWidth;
                _height = newHeight;
                CreateFramebuffer();
            }
        }

        public void CaptureOutput()
        {
            //_colorTarget.
        }

        public void Dispose()
        {
            Base?.Dispose();
            _colorTarget?.Dispose();
            _depthTarget?.Dispose();
            _colorTargetView?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

}