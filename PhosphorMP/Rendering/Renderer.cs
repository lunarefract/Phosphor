using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using ManagedBass;
using ManagedBass.Midi;
using PhosphorMP.Audio;
using PhosphorMP.Parser;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using MidiEventType = PhosphorMP.Parser.MidiEventType;

namespace PhosphorMP.Rendering
{
    public class Renderer
    {
        public static Renderer Singleton { get; private set; }
        public GraphicsDevice GraphicsDevice { get; private set; }
        public ResourceFactory ResourceFactory { get; private set; }
        public CommandList CommandList { get; private set; }
        public ImGuiRenderer ImGuiRendererSwapchain { get; private set; }
        public ImGuiRenderer ImGuiRendererFramebufferSpecific { get; private set; }
        public Sdl2Window BaseWindow { get; private set; }
        public Vector3 ClearColor = new Vector3(0f, 0f, 0f); // Black by default (RGB)       
        
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _uniformBuffer;
        private ResourceSet _resourceSet;
        private Pipeline _pipeline;
        private VisualizationFramebuffer _visualizationFramebuffer;
        private DeviceBuffer _fullscreenQuadBuffer;
        private Pipeline _compositePipeline;
        private ResourceSet _compositeResourceSet;
        private ResourceLayout _compositeLayout;
        private Sampler _sampler;
        private List<VisualNote> _visualNotes = [];

        private bool _showFileDialog = false;
        private string _filePathInput = "";
        private DeviceBuffer _noteVertexBuffer;
        private Logic Logic => Logic.Singleton;
        
        //TODO: Move of some of the player logic to different file ^
        
        public Renderer()
        {
            if (Singleton == null)
                Singleton = this;
            else
                throw new Exception("Renderer already initialized.");
            
            Init();
            _ = Task.Run(() =>
            {
                Logic.CurrentMidiFile = new MidiFile(@"/home/memfrag/Pi.mid"); // TODO: Remove in Release
            });

            _visualNotes = Utils.Utils.GenerateKeyboardSweep();
        }
        
        private void Init()
        {
            BaseWindow = Window.Singleton.BaseSdl2Window;
            if (BaseWindow == null) throw new NullReferenceException("Window is null");
            GraphicsDevice = VeldridStartup.CreateGraphicsDevice(BaseWindow, GraphicsBackend.OpenGL);
            ResourceFactory = GraphicsDevice.ResourceFactory;
            _visualizationFramebuffer = new VisualizationFramebuffer();
            CreateCompositePipeline();
            CommandList = ResourceFactory.CreateCommandList();
            ImGuiRendererSwapchain = new ImGuiRenderer(
                GraphicsDevice,
                GraphicsDevice.SwapchainFramebuffer.OutputDescription,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Width,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Height);
            ImGuiRendererFramebufferSpecific = new ImGuiRenderer(
                GraphicsDevice,
                _visualizationFramebuffer.Base.OutputDescription,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Width,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Height);
            
            CreateVertexBuffer();
            CreatePipeline();

            Console.WriteLine("Using device: " + GraphicsDevice.DeviceName);
        }

        private void CreateVertexBuffer()
        {
            Vector2[] vertices =
            [
                new Vector2(-500, -100),
                new Vector2( 500, -100),
                new Vector2( 500,  100),
                new Vector2(-500, -100),
                new Vector2( 500,  100),
                new Vector2(-500,  100)
            ];

            _vertexBuffer = ResourceFactory.CreateBuffer(new BufferDescription((uint)(vertices.Length * sizeof(float) * 2), BufferUsage.VertexBuffer));
            GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);
        }

        private void CreatePipeline()
        {
            Matrix4x4 ortho = Matrix4x4.CreateOrthographicOffCenter(
                0, _visualizationFramebuffer.Base.Width,     // Left to Right
                _visualizationFramebuffer.Base.Height, 0,  // Bottom to Top (Y grows down)
                0f, 1f);
            
            Shader[] shaders = Shaders.CompileShaders(GraphicsDevice, ResourceFactory);

            var uniformLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MVP", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            _uniformBuffer = ResourceFactory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _resourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(uniformLayout, _uniformBuffer));

            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.None, // TODO: Cull back faces
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
                            new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float3)
                        )
                    ],
                    shaders),
                Outputs = _visualizationFramebuffer.Base.OutputDescription
            };
            _pipeline = ResourceFactory.CreateGraphicsPipeline(ref pipelineDesc);
            GraphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref ortho);
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

            var textureView = _visualizationFramebuffer.ColorTargetView;
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
                    FaceCullMode.None, // TODO: Cull back faces
                    PolygonFillMode.Solid,
                    FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = [textureLayout],
                ShaderSet = new ShaderSetDescription(
                    [
                        new VertexLayoutDescription(
                            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
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

            // Update ImGui contexts
            ImGuiRendererSwapchain.Update(Program.DeltaTime, input);

            CommandList.Begin();
            // 1. Render to offscreen framebuffer
            CommandList.SetFramebuffer(_visualizationFramebuffer.Base);
            CommandList.ClearColorTarget(0, new RgbaFloat(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f));
            CommandList.ClearDepthStencil(1f);
            
            List<NoteVertex> noteVertices = [];
            foreach (var note in _visualNotes) // TODO: Fix desync
            {
                float x = GetNoteXPosition(note.Key);
                float y = GetNoteYPosition(note.StartingTick);
                float height = GetNoteHeight(note.DurationTick);
                float fbWidth = _visualizationFramebuffer.Base.Width;
                float width = fbWidth / (RendererSettings.MaxKey - RendererSettings.MinKey);

                var topLeft = new Vector2(x, y);
                var topRight = new Vector2(x + width, y);
                var bottomLeft = new Vector2(x, y + height);
                var bottomRight = new Vector2(x + width, y + height);

                Vector3 color = new Vector3(1, 0, 0); // red for now
                
                noteVertices.Add(new NoteVertex { Position = topLeft, Color = color });
                noteVertices.Add(new NoteVertex { Position = topRight, Color = color });
                noteVertices.Add(new NoteVertex { Position = bottomRight, Color = color });
                noteVertices.Add(new NoteVertex { Position = topLeft, Color = color });
                noteVertices.Add(new NoteVertex { Position = bottomRight, Color = color });
                noteVertices.Add(new NoteVertex { Position = bottomLeft, Color = color });
            }
            
            if (_noteVertexBuffer == null || _noteVertexBuffer.SizeInBytes < noteVertices.Count * sizeof(float) * 5)
            {
                _noteVertexBuffer?.Dispose();
                _noteVertexBuffer = ResourceFactory.CreateBuffer(new BufferDescription((uint)(noteVertices.Count * sizeof(float) * 5), BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            }

            GraphicsDevice.UpdateBuffer(_noteVertexBuffer, 0, noteVertices.ToArray());

            RenderOverlay();
            ImGuiRendererFramebufferSpecific.Render(GraphicsDevice, CommandList);
            
            CommandList.SetPipeline(_pipeline);
            CommandList.SetVertexBuffer(0, _noteVertexBuffer);
            CommandList.SetGraphicsResourceSet(0, _resourceSet);
            CommandList.Draw((uint)noteVertices.Count);

            // 2. Composite pass: render framebuffer texture to swapchain
            CommandList.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            CommandList.ClearColorTarget(0, RgbaFloat.Black);

            CommandList.SetPipeline(_compositePipeline);
            CommandList.SetVertexBuffer(0, _fullscreenQuadBuffer);
            CommandList.SetGraphicsResourceSet(0, _compositeResourceSet);
            CommandList.Draw(6);

            // 3. Render ImGui (on top of final output)
            RenderUi();
            ImGuiRendererSwapchain.Render(GraphicsDevice, CommandList);

            CommandList.End();
            GraphicsDevice.SubmitCommands(CommandList);
            GraphicsDevice.SwapBuffers();
        }
        
        private float GetNoteXPosition(byte key)
        {
            int keyCount = RendererSettings.MaxKey - RendererSettings.MinKey + 1;
            float totalWidth = _visualizationFramebuffer.Base.Width;
            float keyWidth = totalWidth / keyCount;
            return (key - RendererSettings.MinKey) * keyWidth;
        }
        
        private float GetNoteYPosition(long noteTick)
        {
            float deltaTicks = noteTick - Logic.CurrentTick;
            return _visualizationFramebuffer.Base.Height - deltaTicks * RendererSettings.ScrollSpeed;
        }
        
        private float GetNoteHeight(long durationTicks)
        {
            return durationTicks * RendererSettings.ScrollSpeed;
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
                if (Logic.CurrentTick == Logic.CurrentMidiFile.TickCount)
                {
                    Logic.CurrentTick = Logic.StartupDelayTicks;
                }
                Logic.Playing = !Logic.Playing;
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
            {
                Logic.Playing = false;
                Logic.CurrentTick = Logic.StartupDelayTicks;
            }
            
            if (ImGui.Button("Load"))
            {
                _showFileDialog = true;
                ImGui.OpenPopup("Load File");
            }

            bool parserPopup = ParserStats.Stage != ParserStage.Idle && ParserStats.Stage != ParserStage.Streaming;            
            if (parserPopup)
            {
                if (ImGui.BeginPopupModal("Parsing", ref parserPopup, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    if (ParserStats.Stage == ParserStage.CheckingHeader)
                    {
                        ImGui.Text("[1 / 4] Checking header data...");
                    }
                    if (ParserStats.Stage == ParserStage.FindingTracksPositions)
                    {
                        ImGui.Text($"[2 / 4] Finding track positions... 0 / {ParserStats.FoundTrackPositions}");
                    }
                    if (ParserStats.Stage == ParserStage.CreatingTrackClasses)
                    {
                        ImGui.Text($"[3 / 4] Finding track positions... {Utils.Utils.GetPercentage(ParserStats.CreatedTrackClasses, ParserStats.FoundTrackPositions)} | {ParserStats.CreatedTrackClasses} / {ParserStats.FoundTrackPositions}");
                    }

                    if (ParserStats.Stage == ParserStage.PreparingForStreaming)
                    {
                        ImGui.Text($"[4 / 4] Preparing for streaming... {Utils.Utils.GetPercentage(ParserStats.PreparingForStreamingCount, ParserStats.CreatedTrackClasses)} | {ParserStats.PreparingForStreamingCount} / {ParserStats.CreatedTrackClasses}");
                    }
                    ImGui.Text($"Heap Memory Usage: {GC.GetTotalMemory(false) / 1024f / 1024f:F2} MB");
                    
                    if (ImGui.Button("Cancel"))
                    {
                        ImGui.CloseCurrentPopup();
                        Logic.CurrentMidiFile.Dispose();
                        Logic.CurrentMidiFile = null;
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
    }
}
