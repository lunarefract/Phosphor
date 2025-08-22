using System.Numerics;
using System.Runtime.CompilerServices;
using C5;
using PhosphorMP.Parser;
using PhosphorMP.Rendering.Structs;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace PhosphorMP.Rendering
{
    public class Renderer : IDisposable
    {
        public static Renderer Singleton { get; private set; }
        public GraphicsDevice GraphicsDevice { get; private set; }
        public ResourceFactory ResourceFactory { get; private set; }
        public CommandList CommandList { get; private set; }
        public OverlayHandler OverlayHandler { get; private set; }
        public UserInterfaceHandler UserInterfaceHandler { get; private set; }
        public Framedumper Framedumper { get; private set; }
        public Vector3 ClearColor { get; internal set; } = new Vector3(0f, 0f, 0f);
        public ArrayList<VisualNote> VisualNotes { get; internal set; } = [];
        internal VisualizationFramebuffer VisualizationFramebuffer;
        private static Sdl2Window BaseWindow => Window.Singleton.BaseSdl2Window;
        private Logic Logic => Logic.Singleton;
        
        private List<NoteColorVertexBuffer> _noteColorVertexBuffers = [];
        private DeviceBuffer _uniformBuffer;
        private DeviceBuffer _uniformBufferComposite;
        private DeviceBuffer _fullscreenQuadBuffer;
        private ResourceSet _resourceSet;
        private ResourceSet _compositeResourceSet;
        private Pipeline _visualizationPipeline;
        private Pipeline _compositePipeline;
        //private ResourceLayout _compositeLayout;
        //private Sampler _sampler;
        private Matrix4x4 _projectionMatrix;

        private static readonly Vector2[] QuadTexCoords = [
            new Vector2(0, 0), // top-left
            new Vector2(1, 0), // top-right
            new Vector2(1, 1), // bottom-right
            new Vector2(0, 1)  // bottom-left
        ];

        public Renderer()
        {
            if (Singleton == null)
                Singleton = this;
            else
                throw new Exception("Renderer already initialized.");
            
            Init();
            _ = Task.Run(() =>
            {
                try
                {
                    Logic.CurrentMidiFile = new MidiFile(@"mokou.mid"); // TODO: Remove in Release
                }
                catch (Exception)
                {
                    // ignored
                }
            });
        }

        private GraphicsBackend GetCorrectBackend() // TODO: Extend this further
        {
            return GraphicsBackend.Vulkan;
        }
        
        private void Init()
        {
            var options = new GraphicsDeviceOptions(
#if DEBUG
                debug: true,          // <--- enables validation layer debug output
#else
                debug: false,
#endif
                swapchainDepthFormat: null,
                syncToVerticalBlank: false,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true); // PATCH: Support for depth/luma mask later
            
            Window.Singleton.BaseSdl2Window.Resized += HandleWindowResize;
            if (BaseWindow == null) throw new NullReferenceException("Window is null");
            // ln -s /usr/lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so
#if  WINDOWS
            GraphicsDevice = VeldridStartup.CreateGraphicsDevice(BaseWindow, options, GetCorrectBackend());
#else
            try
            {
                GraphicsDevice = VeldridStartup.CreateGraphicsDevice(BaseWindow, options, GetCorrectBackend());
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                if (e.Message.Contains("Vulkan.VulkanNative"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(@"
// ================================================================================
//  Veldrid, the graphics rendering wrapper we use was unable to load 'libdl',
//  which is required for Vulkan backend.
// 
//  On many Linux distributions, only `libdl.so.2` is shipped by default, but 
//  Veldrid expects a plain `libdl.so` symlink.
// 
//  To fix this issue, run the following command as root user or using sudo:
// 
//    ln -s /usr/lib/libdl.so.2 /usr/lib/libdl.so
// 
//  If you're on a different architecture or distro, adjust the path accordingly.
//  After creating the symlink, start the application again.
// ================================================================================
");
                    Console.ReadKey();
                    Environment.Exit(1);
#endif
                }
            }
            ResourceFactory = GraphicsDevice.ResourceFactory;
            VisualizationFramebuffer = new VisualizationFramebuffer();
            _projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(
                0, VisualizationFramebuffer.Base.Width,     // Left to Right
                VisualizationFramebuffer.Base.Height, 0,  // Bottom to Top (Y grows down)
                0f, 1f);
            CreateCompositePipeline();
            CommandList = ResourceFactory.CreateCommandList();
            OverlayHandler = new OverlayHandler();
            UserInterfaceHandler = new UserInterfaceHandler();
            CreatePipeline();
            Framedumper = new Framedumper();
            Console.WriteLine("Using device: " + GraphicsDevice.DeviceName);
        }

        private void HandleWindowResize()
        {
            if (GraphicsDevice?.MainSwapchain == null) return;
            
            uint width = (uint)BaseWindow.Width;
            uint height = (uint)BaseWindow.Height;
            
            //GraphicsDevice.ResizeMainWindow(width, height);
            GraphicsDevice.MainSwapchain.Resize(width, height);
            UserInterfaceHandler.ImGuiUserInterfaceRenderer.WindowResized((int)width, (int)height);
            CreateCompositePipeline(true);
            UpdateUniforms();
            UpdateUniforms(true);

            Console.WriteLine($"Window resized to {width}x{height}");
        }

        private Uniforms FillUniforms()
        {
            return new Uniforms
            {
                MVP = _projectionMatrix,
                FramebufferSize = new Vector2(VisualizationFramebuffer.Base.Width, VisualizationFramebuffer.Base.Height)
            };
        }
        
        private CompositeUniforms FillCompositeUniforms()
        {
            return new CompositeUniforms
            {
                MVP = _projectionMatrix,
                FramebufferSize = new Vector2(BaseWindow.Width, BaseWindow.Height)
            };
        }

        private void UpdateUniforms(bool composite = false)
        {
            if (composite)
            {
                var uniforms = FillCompositeUniforms();
                GraphicsDevice.UpdateBuffer(_uniformBufferComposite, 0, ref uniforms);
            }
            else
            {
                var uniforms = FillUniforms();
                GraphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref uniforms);
            }
        }

        private void CreatePipeline(bool dispose = false)
        {
            if (dispose) _visualizationPipeline.Dispose();
            
            Shader[] shaders = Shaders.CompileShaders(GraphicsDevice, ResourceFactory);
            var uniformLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MVP", ResourceKind.UniformBuffer, ShaderStages.Vertex))
            );

            var uniforms = FillUniforms();

            _uniformBuffer = ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)Unsafe.SizeOf<Uniforms>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic)
            );
            
            _resourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(uniformLayout, _uniformBuffer));

            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.Back,
                    PolygonFillMode.Solid,
                    FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = [uniformLayout],
                ShaderSet = new ShaderSetDescription(
                    [
                        new VertexLayoutDescription( // VertexElementSemantic Note: https://veldrid.dev/api/Veldrid.VertexElementSemantic.html
                            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                            new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.UInt1),
                            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UShort2_Norm)
                        )
                    ],
                    shaders),
                Outputs = VisualizationFramebuffer.Base.OutputDescription
            };
            _visualizationPipeline = ResourceFactory.CreateGraphicsPipeline(ref pipelineDesc);
            GraphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref uniforms);
        }
        
        private void CreateCompositePipeline(bool dispose = false)
        {
            if (dispose) _compositePipeline.Dispose();
            Vector2[] quadVertices = // Create fullscreen quad vertices (clip space)
            [
                new Vector2(-1, -1),
                new Vector2( 1, -1),
                new Vector2( 1,  1),
                new Vector2(-1, -1),
                new Vector2( 1,  1),
                new Vector2(-1,  1),
            ];

            _fullscreenQuadBuffer = ResourceFactory.CreateBuffer(new BufferDescription((uint)(quadVertices.Length * sizeof(float) * 2), BufferUsage.VertexBuffer));
            GraphicsDevice.UpdateBuffer(_fullscreenQuadBuffer, 0, quadVertices);
            var uniforms = FillCompositeUniforms();
            Shader[] shaders = Shaders.CompileCompositeShaders(GraphicsDevice, ResourceFactory);

            // Create texture sampler layout
            var textureLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)
            ));
            
            _uniformBufferComposite = ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)Unsafe.SizeOf<CompositeUniforms>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic)
            );

            var textureView = VisualizationFramebuffer.ColorTargetView;
            var sampler = ResourceFactory.CreateSampler(new SamplerDescription());

            _compositeResourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                textureLayout,
                textureView,
                sampler
            ));

            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.Front,
                    PolygonFillMode.Solid,
                    FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = [textureLayout],
                ShaderSet = new ShaderSetDescription(
                    [
                        new VertexLayoutDescription(
                            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2)
                        )
                    ],
                    shaders),
                Outputs = GraphicsDevice.SwapchainFramebuffer.OutputDescription // ← Match swapchain
            };
            _compositePipeline = ResourceFactory.CreateGraphicsPipeline(ref pipelineDesc);
            GraphicsDevice.UpdateBuffer(_uniformBufferComposite, 0, ref uniforms);
        }
        
        public void Render()
        {
            if (Framedumper.Active) // TODO: Remove and make it into overlay placeholder
            {
                Console.WriteLine("Rendering speed: " + Framedumper.Speed);

                double totalSeconds = Logic.CurrentMidiFile.GetTimeInSeconds(Logic.CurrentMidiFile.TickCount);
                double currentSeconds = Logic.CurrentMidiFile.GetTimeInSeconds(Logic.CurrentTick);
                double remainingSeconds = totalSeconds - currentSeconds;
                double etaSeconds = remainingSeconds / Framedumper.Speed;
                Console.WriteLine("ETA Completed: " + Utils.Utils.FormatTime(TimeSpan.FromSeconds(etaSeconds))); // TODO: Fix crash while trying to render when playback has never been started
                Console.WriteLine("Frames to save queue: " + VisualizationFramebuffer.SaveQueue.Count);
            }
            var input = BaseWindow.PumpEvents();
            
            UserInterfaceHandler.ImGuiUserInterfaceRenderer.Update(Program.DeltaTime, input);

            CommandList.Begin();
            CommandList.SetFramebuffer(VisualizationFramebuffer.Base);
            CommandList.ClearColorTarget(0, new RgbaFloat(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f));
            CommandList.ClearDepthStencil(1f);
            UpdateVisualization();
            CommandList.SetPipeline(_visualizationPipeline);
            foreach (var vertexBuffer in _noteColorVertexBuffers)
            {
                if (!vertexBuffer.NeedsRender)
                    continue;
                
                CommandList.SetVertexBuffer(0, vertexBuffer.Buffer);
                CommandList.SetGraphicsResourceSet(0, _resourceSet);
                CommandList.Draw((uint)vertexBuffer.VertexCount);

                vertexBuffer.NeedsRender = false; // reset after rendering
            }

            OverlayHandler.DefineOverlay();
            OverlayHandler.ImGuiRendererOverlay.Render(GraphicsDevice, CommandList);
            Framedumper.HandleFrame();
            
            RenderVisualizationToSwapchain();

            UserInterfaceHandler.DefineUserInterface();
            UserInterfaceHandler.ImGuiUserInterfaceRenderer.Render(GraphicsDevice, CommandList);

            CommandList.End();
            GraphicsDevice.SubmitCommands(CommandList);
            GraphicsDevice.SwapBuffers();
        }
        
        private void RenderVisualizationToSwapchain()
        {
            float windowWidth = BaseWindow.Width;
            float windowHeight = BaseWindow.Height;
            float fbWidth = VisualizationFramebuffer.Base.Width;
            float fbHeight = VisualizationFramebuffer.Base.Height;
            
            float windowAspect = windowWidth / windowHeight;
            float fbAspect = fbWidth / fbHeight;
            
            float scaleX = 1f, scaleY = 1f;
            if (windowAspect > fbAspect)
                scaleX = fbAspect / windowAspect;
            else
                scaleY = windowAspect / fbAspect;
            
            Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(scaleX, scaleY, 1f);
            
            Vector3 translation = new Vector3(0, 0, 0); // modify if needed
            Matrix4x4 modelMatrix = scaleMatrix * Matrix4x4.CreateTranslation(translation);
            
            var uniforms = new CompositeUniforms
            {
                MVP = modelMatrix,
                FramebufferSize = new Vector2(windowWidth, windowHeight)
            };
            GraphicsDevice.UpdateBuffer(_uniformBufferComposite, 0, ref uniforms);

            // Render framebuffer to swapchain
            CommandList.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            CommandList.ClearColorTarget(0, RgbaFloat.Black);

            CommandList.SetPipeline(_compositePipeline);
            CommandList.SetVertexBuffer(0, _fullscreenQuadBuffer);
            CommandList.SetGraphicsResourceSet(0, _compositeResourceSet);
            CommandList.Draw(6);
        }
        
        private void UpdateVisualization()
        {
            var threadLocalTrackVertices = new ThreadLocal<HashDictionary<int, ArrayList<NoteVertex>>>(
                () => new HashDictionary<int, ArrayList<NoteVertex>>(), trackAllValues: true
            );

            Parallel.ForEach(VisualNotes, Program.ParallelOptions, visualNote =>
            {
                float y = GetVerticalPositionFromTick(visualNote.StartingTick);
                float height = GetHeightFromDuration(visualNote.DurationTick);
                if (y + height < 0 || y > VisualizationFramebuffer.Base.Height)
                    return;

                float fbWidth = VisualizationFramebuffer.Base.Width;
                var noteWidth = fbWidth / (RendererSettings.MaxKey - RendererSettings.MinKey);
                float x = (visualNote.Key - RendererSettings.MinKey) * noteWidth;

                uint color = GetColorByTrack(visualNote.ColorIndex);

                var localList = new[]
                {
                    new NoteVertex { Position = new Vector2(x, y), Color = color, TexCoord = QuadTexCoords[0]},
                    new NoteVertex { Position = new Vector2(x + noteWidth, y), Color = color, TexCoord = QuadTexCoords[1]},
                    new NoteVertex { Position = new Vector2(x + noteWidth, y + height), Color = color, TexCoord = QuadTexCoords[2]},
                    new NoteVertex { Position = new Vector2(x, y), Color = color, TexCoord = QuadTexCoords[0]},
                    new NoteVertex { Position = new Vector2(x + noteWidth, y + height), Color = color, TexCoord = QuadTexCoords[2]},
                    new NoteVertex { Position = new Vector2(x, y + height), Color = color, TexCoord = QuadTexCoords[3]}
                };

                var dict = threadLocalTrackVertices.Value;
                if (dict.Contains(visualNote.ColorIndex))
                {
                    dict[visualNote.ColorIndex].AddAll(localList);
                }
                else
                {
                    var list = new ArrayList<NoteVertex>();
                    list.AddAll(localList);
                    dict[visualNote.ColorIndex] = list;
                }
            });

            // Merge all thread-local dictionaries
            var mergedTrackVertices = new HashDictionary<int, ArrayList<NoteVertex>>();
            foreach (var dict in threadLocalTrackVertices.Values)
            {
                foreach (var kvp in dict)
                {
                    if (mergedTrackVertices.Contains(kvp.Key))
                        mergedTrackVertices[kvp.Key].AddAll(kvp.Value);
                    else
                    {
                        var list = new ArrayList<NoteVertex>();
                        list.AddAll(kvp.Value);
                        mergedTrackVertices[kvp.Key] = list;
                    }
                }
            }

            // Update or create vertex buffers
            foreach (var kvp in mergedTrackVertices)
            {
                int colorIndex = kvp.Key;
                var verts = kvp.Value;

                // Try to find existing buffer
                var buffer = _noteColorVertexBuffers.FirstOrDefault(b => b.ColorIndex == colorIndex);

                if (buffer == null)
                {
                    buffer = new NoteColorVertexBuffer { ColorIndex = colorIndex };
                    _noteColorVertexBuffers.Add(buffer);
                }

                // Ensure buffer is created or resized
                bool needsNewBuffer = buffer.Buffer == null || buffer.Buffer.SizeInBytes < verts.Count * Unsafe.SizeOf<NoteVertex>();
                if (needsNewBuffer)
                {
                    buffer.Buffer?.Dispose();
                    buffer.Buffer = GraphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(
                        (uint)(verts.Count * Unsafe.SizeOf<NoteVertex>()),
                        BufferUsage.VertexBuffer | BufferUsage.Dynamic
                    ));
                }
                
                GraphicsDevice.UpdateBuffer(buffer.Buffer, 0, verts.ToArray());
                buffer.VertexCount = verts.Count; 
                buffer.NeedsRender = true;
            }
        }
        
        private uint GetColorByTrack(int trackNumber) // TODO: Precalculate in another class
        {
            Vector3[] trackColors =
            [
                new Vector3(1.0f, 0.0f, 0.0f),   // Red
                new Vector3(0.0f, 1.0f, 0.0f),   // Green
                new Vector3(0.0f, 0.0f, 1.0f),   // Blue
                new Vector3(1.0f, 1.0f, 0.0f),   // Yellow
                new Vector3(1.0f, 0.0f, 1.0f),   // Magenta
                new Vector3(0.0f, 1.0f, 1.0f),   // Cyan
                new Vector3(1.0f, 0.5f, 0.0f),   // Orange
                new Vector3(0.5f, 0.0f, 1.0f)    // Purple
            ];

            int index = trackNumber % trackColors.Length;
            Vector3 color = trackColors[index];

            byte r = (byte)(color.X * 255f);
            byte g = (byte)(color.Y * 255f);
            byte b = (byte)(color.Z * 255f);

            return (uint)((r << 16) | (g << 8) | b);
        }

        
        private float GetVerticalPositionFromTick(long tick)
        {
            float relativeTick = tick - Logic.CurrentTick;
            float normalizedTick = relativeTick * (960f / Logic.CurrentMidiFile.TimeDivision);
            float yPos = VisualizationFramebuffer.Base.Height - (normalizedTick * RendererSettings.ScrollSpeed);
            return yPos;
        }

        private float GetHeightFromDuration(long durationTick)
        {
            float height = durationTick * RendererSettings.ScrollSpeed;
            const float minHeight = 2f;
            return MathF.Max(height, minHeight);
        }
        
        public void Dispose() // TODO: Dispose everything
        {
            foreach (var shader in Shaders.CompiledShaders.SelectMany(shaderPair => shaderPair))
            {
                shader.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}