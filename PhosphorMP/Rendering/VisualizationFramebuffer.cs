using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using Veldrid;

namespace PhosphorMP.Rendering
{
    public class VisualizationFramebuffer : IDisposable
    {
        public Framebuffer Base { get; internal set; }
        private Texture _colorTarget;
        private Texture _depthTarget;
        private TextureView _colorTargetView;
        private readonly ConcurrentQueue<SaveRequest> _saveQueue = new();
        private bool _isSaving = false;

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

        public void CaptureOutputAsync(string filePath)
        {
            var gd = Renderer.Singleton.GraphicsDevice;
            var factory = gd.ResourceFactory;

            using CommandList cl = factory.CreateCommandList();
            cl.Begin();

            Texture staging = factory.CreateTexture(TextureDescription.Texture2D(
                _colorTarget.Width,
                _colorTarget.Height,
                mipLevels: 1,
                arrayLayers: 1,
                format: _colorTarget.Format,
                usage: TextureUsage.Staging));

            cl.CopyTexture(_colorTarget, staging);
            cl.End();
            gd.SubmitCommands(cl);
            gd.WaitForIdle();

            MappedResource map = gd.Map(staging, MapMode.Read);

            // Copy data immediately to managed array to free GPU resource ASAP
            byte[] dataCopy = new byte[(int)(map.RowPitch * _colorTarget.Height)];
            unsafe
            {
                System.Runtime.InteropServices.Marshal.Copy(map.Data, dataCopy, 0, dataCopy.Length);
            }

            gd.Unmap(staging);
            staging.Dispose();

            // Enqueue the save request
            _saveQueue.Enqueue(new SaveRequest
            {
                Data = dataCopy,
                Width = _colorTarget.Width,
                Height = _colorTarget.Height,
                RowPitch = map.RowPitch,
                FilePath = filePath
            });
            
            if (!_isSaving)
            {
                _isSaving = true;
                Task.Run(ProcessSaveQueue);
            }
        }
        
        private void ProcessSaveQueue()
        {
            while (_saveQueue.TryDequeue(out var request))
            {
                SaveToFile(request);
            }
            _isSaving = false;
        }
        
        private void SaveToFile(SaveRequest request)
        {
            using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                (int)request.Width, (int)request.Height);

            unsafe
            {
                fixed (byte* dataPtr = request.Data)
                {
                    for (int y = 0; y < request.Height; y++)
                    {
                        byte* rowPtr = dataPtr + (y * request.RowPitch);

                        for (int x = 0; x < request.Width; x++)
                        {
                            int i = x * 4;
                            byte r = rowPtr[i + 0];
                            byte g = rowPtr[i + 1];
                            byte b = rowPtr[i + 2];
                            byte a = rowPtr[i + 3];

                            image[x, (int)request.Height - 1 - y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(r, g, b, a);
                        }
                    }
                }
            }

            using var fs = File.OpenWrite(request.FilePath);
            image.SaveAsPng(fs);
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