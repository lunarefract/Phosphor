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
        
        private HashDictionary<int, DeviceBuffer> _trackVertexBuffers = []; // TODO: Make this only apply to how much colors are in a palette, a lot of note buffers can cause useless CPU overhead, also a lot of notes in a buffer is bad because there are limits
        private DeviceBuffer _uniformBuffer;
        private DeviceBuffer _uniformBufferComposite;
        private DeviceBuffer _fullscreenQuadBuffer;
        private ResourceSet _resourceSet;
        private ResourceSet _compositeResourceSet;
        private Pipeline _visualizationPipeline;
        private Pipeline _compositePipeline;
        private ResourceLayout _compositeLayout;
        private Sampler _sampler;
        
        private bool _showFileDialog = false;
        private string _filePathInput = "";
        
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
                    Logic.CurrentMidiFile = new MidiFile(@"/home/memfrag/mokou.mid"); // TODO: Remove in Release
                }
                catch (Exception e)
                {
                    // ignored
                }
            });

            //_visualNotes = Utils.Utils.GenerateKeyboardSweep();
        }
        
        private void Init()
        {
            Window.Singleton.BaseSdl2Window.Resized += HandleWindowResize;
            if (BaseWindow == null) throw new NullReferenceException("Window is null");
            GraphicsDevice = VeldridStartup.CreateGraphicsDevice(BaseWindow, GraphicsBackend.OpenGL);
            ResourceFactory = GraphicsDevice.ResourceFactory;
            VisualizationFramebuffer = new VisualizationFramebuffer();
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
            //OverlayHandler.ImGuiRendererOverlay.WindowResized((int)width, (int)height);
            CreateCompositePipeline(true);

            Console.WriteLine($"Window resized to {width}x{height}");
        }

        private void CreatePipeline(bool dispose = false)
        {
            if (dispose) _visualizationPipeline.Dispose();
            Matrix4x4 ortho = Matrix4x4.CreateOrthographicOffCenter(
                0, VisualizationFramebuffer.Base.Width,     // Left to Right
                VisualizationFramebuffer.Base.Height, 0,  // Bottom to Top (Y grows down)
                0f, 1f);
            
            Shader[] shaders = Shaders.CompileShaders(GraphicsDevice, ResourceFactory);
            var uniformLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MVP", ResourceKind.UniformBuffer, ShaderStages.Vertex))
            );

            var uniforms = new Uniforms
            {
                MVP = ortho,
                FramebufferSize = new Vector2(VisualizationFramebuffer.Base.Width, VisualizationFramebuffer.Base.Height)
            };

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
                            new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float3),
                            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UShort2_Norm),
                            new VertexElementDescription("NoteSize", VertexElementSemantic.Position, VertexElementFormat.UShort2_Norm) // TODO: We need to remove this I think
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
            foreach (var kvp in _trackVertexBuffers.Keys.OrderBy(k => k).Select(k => new KeyValuePair<int, DeviceBuffer>(k, _trackVertexBuffers[k])))
            {
                int vertexCount = (int)(kvp.Value.SizeInBytes / Unsafe.SizeOf<NoteVertex>());
                CommandList.SetVertexBuffer(0, kvp.Value);
                CommandList.SetGraphicsResourceSet(0, _resourceSet);
                CommandList.Draw((uint)vertexCount);
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
            
            var uniforms = new Uniforms
            {
                MVP = modelMatrix,
                FramebufferSize = new Vector2(fbWidth, fbHeight)
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
            var threadLocalTrackVertices = new ThreadLocal<HashDictionary<int, ArrayList<NoteVertex>>>(() => new HashDictionary<int, ArrayList<NoteVertex>>(), trackAllValues: true);

            Parallel.ForEach(VisualNotes, Program.ParallelOptions, visualNote =>
            {
                float y = GetVerticalPositionFromTick(visualNote.StartingTick);
                float height = GetHeightFromDuration(visualNote.DurationTick);
                if (y + height < 0 || y > VisualizationFramebuffer.Base.Height)
                    return;

                float fbWidth = VisualizationFramebuffer.Base.Width;
                var noteWidth = fbWidth / (RendererSettings.MaxKey - RendererSettings.MinKey);
                float x = (visualNote.Key - RendererSettings.MinKey) * noteWidth;

                Vector3 color = GetColorByTrack(visualNote.Track);

                Vector2 topLeft = new Vector2(x, y);
                Vector2 topRight = new Vector2(x + noteWidth, y);
                Vector2 bottomLeft = new Vector2(x, y + height);
                Vector2 bottomRight = new Vector2(x + noteWidth, y + height);

                var noteHeight = height;
                var noteSizeInPixels = new Vector2(noteWidth, noteHeight);

                var localList = new[]
                {
                    new NoteVertex { Position = topLeft, Color = color, TexCoord = new Vector2(0, 0), NoteSize = noteSizeInPixels },
                    new NoteVertex { Position = topRight, Color = color, TexCoord = new Vector2(1, 0), NoteSize = noteSizeInPixels },
                    new NoteVertex { Position = bottomRight, Color = color, TexCoord = new Vector2(1, 1), NoteSize = noteSizeInPixels },

                    new NoteVertex { Position = topLeft, Color = color, TexCoord = new Vector2(0, 0), NoteSize = noteSizeInPixels },
                    new NoteVertex { Position = bottomRight, Color = color, TexCoord = new Vector2(1, 1), NoteSize = noteSizeInPixels },
                    new NoteVertex { Position = bottomLeft, Color = color, TexCoord = new Vector2(0, 1), NoteSize = noteSizeInPixels }
                };

                var dict = threadLocalTrackVertices.Value;
                if (dict.Contains(visualNote.Track))
                {
                    var list = dict[visualNote.Track];
                    list.AddAll(localList);
                }
                else
                {
                    var list = new ArrayList<NoteVertex>();
                    list.AddAll(localList);
                    dict[visualNote.Track] = list;
                }
            });

            // Merge all thread local dictionaries into one
            var mergedTrackVertices = new HashDictionary<int, ArrayList<NoteVertex>>();
            foreach (var dict in threadLocalTrackVertices.Values)
            {
                foreach (var kvp in dict)
                {
                    if (mergedTrackVertices.Contains(kvp.Key))
                    {
                        mergedTrackVertices[kvp.Key].AddAll(kvp.Value);
                    }
                    else
                    {
                        var list = new ArrayList<NoteVertex>();
                        list.AddAll(kvp.Value);
                        mergedTrackVertices[kvp.Key] = list;
                    }
                }
            }

            // Create/update vertex buffers per track
            _trackVertexBuffers = new HashDictionary<int, DeviceBuffer>();
            foreach (var kvp in mergedTrackVertices)
            {
                var verts = kvp.Value;
                var buffer = GraphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(
                    (uint)(verts.Count * Unsafe.SizeOf<NoteVertex>()),
                    BufferUsage.VertexBuffer | BufferUsage.Dynamic));
                GraphicsDevice.UpdateBuffer(buffer, 0, verts.ToArray());
                _trackVertexBuffers[kvp.Key] = buffer;
            }
        }
        
        private Vector3 GetColorByTrack(int trackNumber)
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
            return trackColors[index];
        }
        
        private float GetVerticalPositionFromTick(long tick)
        {
            float relativeTick = tick - Logic.CurrentTick;
            float yPos = VisualizationFramebuffer.Base.Height - (relativeTick * RendererSettings.ScrollSpeed);
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
