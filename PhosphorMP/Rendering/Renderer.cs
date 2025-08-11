using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using C5;
using ImGuiNET;
using ManagedBass;
using ManagedBass.Midi;
using PhosphorMP.Audio;
using PhosphorMP.Parser;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Vortice.Mathematics.PackedVector;
using MidiEventType = PhosphorMP.Parser.MidiEventType;

namespace PhosphorMP.Rendering
{
    public class Renderer : IDisposable
    {
        public static Renderer Singleton { get; private set; }
        public GraphicsDevice GraphicsDevice { get; private set; }
        public ResourceFactory ResourceFactory { get; private set; }
        public CommandList CommandList { get; private set; }
        public ImGuiRenderer ImGuiRendererSwapchain { get; private set; }
        public ImGuiRenderer ImGuiRendererFramebufferSpecific { get; private set; }
        public Framedumper Framedumper { get; private set; }
        public Vector3 ClearColor { get; internal set; } = new Vector3(0f, 0f, 0f);
        public ArrayList<VisualNote> VisualNotes { get; internal set; } = [];
        internal VisualizationFramebuffer VisualizationFramebuffer;
        private static Sdl2Window BaseWindow => Window.Singleton.BaseSdl2Window;
        private Logic Logic => Logic.Singleton;
        
        private HashDictionary<int, DeviceBuffer> _trackVertexBuffers = []; // TODO: Make this only apply to how much colors are in a palette, a lot of note buffers can cause useless CPU overhead, also a lot of notes in a buffer is bad because there are limits
        private DeviceBuffer _uniformBuffer;
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
                    Logic.CurrentMidiFile = new MidiFile(@"/home/memfrag/Downloads/Mary had a little lamb of DEATH.mid"); // TODO: Remove in Release
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
            if (BaseWindow == null) throw new NullReferenceException("Window is null");
            GraphicsDevice = VeldridStartup.CreateGraphicsDevice(BaseWindow, GraphicsBackend.OpenGL);
            ResourceFactory = GraphicsDevice.ResourceFactory;
            VisualizationFramebuffer = new VisualizationFramebuffer();
            CreateCompositePipeline();
            CommandList = ResourceFactory.CreateCommandList();
            ImGuiRendererSwapchain = new ImGuiRenderer(
                GraphicsDevice,
                GraphicsDevice.SwapchainFramebuffer.OutputDescription,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Width,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Height);
            ImGuiRendererFramebufferSpecific = new ImGuiRenderer(
                GraphicsDevice,
                VisualizationFramebuffer.Base.OutputDescription,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Width,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Height);
            
            CreatePipeline();
            Framedumper = new Framedumper();
            Console.WriteLine("Using device: " + GraphicsDevice.DeviceName);
        }
        
        private void CreatePipeline()
        {
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
                        new VertexLayoutDescription(
                            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                            new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float3),
                            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UShort2_Norm),
                            new VertexElementDescription("NoteSize", VertexElementSemantic.Position, VertexElementFormat.UShort2_Norm)
                        )
                    ],
                    shaders),
                Outputs = VisualizationFramebuffer.Base.OutputDescription
            };
            _visualizationPipeline = ResourceFactory.CreateGraphicsPipeline(ref pipelineDesc);
            GraphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref uniforms);
        }
        
        private void CreateCompositePipeline()
        {
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
            var input = BaseWindow.PumpEvents();

            ImGuiRendererSwapchain.Update(Program.DeltaTime, input);

            CommandList.Begin();
            CommandList.SetFramebuffer(VisualizationFramebuffer.Base);
            CommandList.ClearColorTarget(0, new RgbaFloat(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f));
            CommandList.ClearDepthStencil(1f);
            UpdateVisualization();
            CommandList.SetPipeline(_visualizationPipeline);
            foreach (var kvp in _trackVertexBuffers)
            {
                int vertexCount = (int)(kvp.Value.SizeInBytes / Unsafe.SizeOf<NoteVertex>());
                CommandList.SetVertexBuffer(0, kvp.Value);
                CommandList.SetGraphicsResourceSet(0, _resourceSet); // update if needed per track
                CommandList.Draw((uint)vertexCount);
            }
            RenderOverlay();
            ImGuiRendererFramebufferSpecific.Render(GraphicsDevice, CommandList);
            Framedumper.HandleFrame();
            
            CommandList.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            CommandList.ClearColorTarget(0, RgbaFloat.Black);

            CommandList.SetPipeline(_compositePipeline);
            CommandList.SetVertexBuffer(0, _fullscreenQuadBuffer);
            CommandList.SetGraphicsResourceSet(0, _compositeResourceSet);
            CommandList.Draw(6);

            RenderUi();
            ImGuiRendererSwapchain.Render(GraphicsDevice, CommandList);

            CommandList.End();
            GraphicsDevice.SubmitCommands(CommandList);
            GraphicsDevice.SwapBuffers();
        }

        private void UpdateVisualization()
        {
            var threadLocalTrackVertices = new ThreadLocal<HashDictionary<int, ArrayList<NoteVertex>>>(() => new HashDictionary<int, ArrayList<NoteVertex>>(), trackAllValues: true);

            Parallel.ForEach(VisualNotes, visualNote =>
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
        
        private void RenderOverlay()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Once);
            ImGui.Begin("Overlay", ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
            if (Logic.CurrentMidiFile != null)
            {
                ImGui.Text($"Time: {Utils.Utils.FormatTime(TimeSpan.FromSeconds(Logic.CurrentMidiFile.GetTimeInSeconds(Logic.CurrentTick)))} / {Utils.Utils.FormatTime(Logic.CurrentMidiFile.Length)}");
                ImGui.Text($"Ticks: {Logic.CurrentTick} / {Logic.CurrentMidiFile.TickCount}");
                ImGui.Text($"Time Division: {Logic.CurrentMidiFile.TimeDivision} PPQ");
                ImGui.Text($"Tempo: {60_000_000.0 / Logic.CurrentMidiFile.GetCurrentTempoAtTick(Logic.CurrentTick)} BPM");
                ImGui.Text($"Notes: {Logic.PassedNotes} / {Logic.CurrentMidiFile.NoteCount}");
            }
            else
            {
                ImGui.Text($"MIDI file not loaded.");
            }
            
            ImGui.Text($"FPS: {(1f / Program.DeltaTime):0.0}");
            ImGui.Text($"Heap Memory Usage: {GC.GetTotalMemory(false) / 1024f / 1024f:F2} MB");
            ImGui.Text($"visualNotes count: {VisualNotes.Count}");
            //ImGui.Text($"Bass Rendertime: {Bass.CPUUsage}");
            ImGui.End();
        }

        private void RenderUi()
        {
            ImGui.SetNextWindowPos(new Vector2(100, 100), ImGuiCond.Once);
            ImGui.Begin("Controls", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);

            // Playback buttons
            if (ImGui.Button(Logic.Playing ? "Pause" : "Play"))
            {
                if (Logic.CurrentMidiFile == null) return;
                if (Logic.CurrentTick == Logic.CurrentMidiFile.TickCount)
                {
                    Logic.CurrentTick = Logic.StartupDelayTicks;
                }
                Logic.Playing = !Logic.Playing;
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
            {
                if (Logic.CurrentMidiFile == null) return;
                Logic.Playing = false;
                Logic.CurrentTick = Logic.StartupDelayTicks;
            }
            
            if (ImGui.Button("Render (W.I.P)"))
            {
                if (Logic.CurrentMidiFile == null) return;
                Logic.Playing = false;
                Logic.CurrentTick = Logic.StartupDelayTicks;
                Framedumper.FPS = 60;
                Framedumper.Start();
                Logic.Playing = true;
            }
            
            if (ImGui.Button("Load"))
            {
                Logic.CurrentMidiFile?.Dispose();
                _showFileDialog = true;
                ImGui.OpenPopup("Load File");
            }

            bool parserPopup = ParserStats.Stage != ParserStage.Idle && ParserStats.Stage != ParserStage.Streaming;            
            if (parserPopup)
            {
                if (ImGui.BeginPopupModal("Parsing", ref parserPopup, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    if (ParserStats.Stage == ParserStage.CheckingHeader) ImGui.Text("[1 / 4] Checking header data...");
                    if (ParserStats.Stage == ParserStage.FindingTracksPositions) ImGui.Text($"[2 / 4] Finding track positions... 0 / {ParserStats.FoundTrackPositions}");
                    if (ParserStats.Stage == ParserStage.CreatingTrackClasses) ImGui.Text($"[3 / 4] Finding track positions... {Utils.Utils.GetPercentage(ParserStats.CreatedTrackClasses, ParserStats.FoundTrackPositions)} | {ParserStats.CreatedTrackClasses} / {ParserStats.FoundTrackPositions}");
                    if (ParserStats.Stage == ParserStage.PreparingForStreaming) ImGui.Text($"[4 / 4] Preparing for streaming... {Utils.Utils.GetPercentage(ParserStats.PreparingForStreamingCount, ParserStats.CreatedTrackClasses)} | {ParserStats.PreparingForStreamingCount} / {ParserStats.CreatedTrackClasses}");
                    ImGui.Text($"Heap Memory Usage: {GC.GetTotalMemory(false) / 1024f / 1024f:F2} MB");
                    
                    if (ImGui.Button("Cancel"))
                    {
                        ImGui.CloseCurrentPopup();
                        if (Logic.CurrentMidiFile != null)
                        {
                            Logic.CurrentMidiFile.Dispose();
                            Logic.CurrentMidiFile = null;
                        }
                    }

                    ImGui.EndPopup();
                }
                ImGui.OpenPopup("Parsing");
            }

            if (_showFileDialog)
            {
                if (ImGui.BeginPopupModal("Load File", ref _showFileDialog, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.InputText("File Path", ref _filePathInput, 256);

                    if (ImGui.Button("OK"))
                    {
                        _showFileDialog = false;
                        ImGui.CloseCurrentPopup();

                        if (!string.IsNullOrWhiteSpace(_filePathInput))
                        {
                            try
                            {
                                Logic.CurrentMidiFile = new MidiFile(_filePathInput);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to load file: {ex.Message}");
                            }
                        }
                    }
                    ImGui.SameLine();

                    if (ImGui.Button("Cancel"))
                    {
                        ImGui.CloseCurrentPopup();
                        _showFileDialog = false;
                    }
                    ImGui.EndPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Unload"))
            {
                if (Logic.CurrentMidiFile != null)
                {
                    Logic.CurrentMidiFile.Dispose();
                    Logic.CurrentMidiFile = null;
                }
            }
            
            ImGui.End();
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
