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
        public float DeltaTime { get; private set; }
        public bool Playing { get; set; } = false;
        public MidiFile CurrentMidiFile { get; private set; }
        public ulong PassedNotes = 0;
        public ulong CurrentTick = 0;
        
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _uniformBuffer;
        private ResourceSet _resourceSet;
        private Pipeline _pipeline;
        private Stopwatch _stopwatch = new Stopwatch(); // TODO: Do NOT use stopwatches because it's using system clock (I need to make internal clock), can't make rendering pipeline with it
        private Vector3 _clearColor = new Vector3(0f, 0f, 0f); // Black by default (RGB)
        private double _tickRemainder = 0.0;
        private bool _showFileDialog = false;
        private string _filePathInput = "";
        private Stopwatch _playbackTimer = new();
        
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
                CurrentMidiFile = new MidiFile(@"/run/media/memfrag/00AAB9F3AAB9E576/BA.DECIMATIONMODE.mid"); // TODO: Remove in Release
            });
        }
        
        private void Init()
        {
            BaseWindow = Window.Singleton.BaseSdl2Window;
            if (BaseWindow == null)
                throw new NullReferenceException("Window is null");

            GraphicsDevice = VeldridStartup.CreateGraphicsDevice(BaseWindow, GraphicsBackend.OpenGL);
            ResourceFactory = GraphicsDevice.ResourceFactory;
            CommandList = ResourceFactory.CreateCommandList();
            ImGuiRendererSwapchain = new ImGuiRenderer(GraphicsDevice, GraphicsDevice.SwapchainFramebuffer.OutputDescription, (int)GraphicsDevice.MainSwapchain.Framebuffer.Width, (int)GraphicsDevice.MainSwapchain.Framebuffer.Height);
            ImGuiRendererFramebufferSpecific = new ImGuiRenderer(GraphicsDevice, GraphicsDevice.SwapchainFramebuffer.OutputDescription, (int)GraphicsDevice.MainSwapchain.Framebuffer.Width, (int)GraphicsDevice.MainSwapchain.Framebuffer.Height);

            CreateVertexBuffer();
            CreatePipeline();

            Console.WriteLine("Using device: " + GraphicsDevice.DeviceName);
            
            _stopwatch.Start();
        }

        private void CreateVertexBuffer()
        {
            Vector2[] vertices =
            [
                new Vector2(-50, -10),
                new Vector2( 50, -10),
                new Vector2( 50,  10),
                new Vector2(-50, -10),
                new Vector2( 50,  10),
                new Vector2(-50,  10)
            ];

            _vertexBuffer = ResourceFactory.CreateBuffer(new BufferDescription((uint)(vertices.Length * sizeof(float) * 2), BufferUsage.VertexBuffer));
            GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);
        }

        private void CreatePipeline()
        {
            Shader[] shaders = Shaders.CompileShaders(GraphicsDevice, ResourceFactory);

            // Create uniform layout (MVP matrix)
            var uniformLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MVP", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            _uniformBuffer = ResourceFactory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _resourceSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(uniformLayout, _uniformBuffer));

            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.None,
                    PolygonFillMode.Solid,
                    FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = [uniformLayout],
                ShaderSet = new ShaderSetDescription(
                    [
                        new VertexLayoutDescription(
                            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2)
                        )
                    ],
                    shaders),
                Outputs = GraphicsDevice.SwapchainFramebuffer.OutputDescription
            };

            _pipeline = ResourceFactory.CreateGraphicsPipeline(ref pipelineDesc);
        }

        public void Render()
        {
            DeltaTime = (float)_stopwatch.Elapsed.TotalSeconds;
            _stopwatch.Restart();

            var input = BaseWindow.PumpEvents();

            // Update ImGui contexts
            ImGuiRendererSwapchain.Update(DeltaTime, input);
            //ImGuiRendererFramebufferSpecific.Update(DeltaTime);

            if (CurrentMidiFile != null)
            {
                PlaybackLogic();
            }
            
            CommandList.Begin();
            CommandList.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            CommandList.ClearColorTarget(0, new RgbaFloat(_clearColor.X, _clearColor.Y, _clearColor.Z, 1f));

            CommandList.SetPipeline(_pipeline);
            CommandList.SetVertexBuffer(0, _vertexBuffer);
            CommandList.SetGraphicsResourceSet(0, _resourceSet);
            
            RenderOverlay();
            ImGuiRendererFramebufferSpecific.Render(GraphicsDevice, CommandList);
            RenderUI();
            ImGuiRendererSwapchain.Render(GraphicsDevice, CommandList);

            CommandList.End();
            GraphicsDevice.SubmitCommands(CommandList);
            GraphicsDevice.SwapBuffers();
        }

        private void PlaybackLogic()
        {
            if (!Playing || CurrentMidiFile == null)
                return;

            if (CurrentTick >= CurrentMidiFile.TickCount)
            {
                Playing = false;
                return;
            }
            
            int tempo = CurrentMidiFile.GetCurrentTempoAtTick(CurrentTick);
            double microsecondsPerTick = tempo / (double)CurrentMidiFile.TimeDivision;

            double totalTicks = (DeltaTime * 1_000_000) / microsecondsPerTick + _tickRemainder;
            ulong ticksToAdvance = (ulong)totalTicks;
            _tickRemainder = totalTicks - ticksToAdvance;

            if (ticksToAdvance == 0)
                return;
            
            // Parse new events between last tick and now + bar
            ulong from = CurrentMidiFile.LastParsedTick;
            ulong to = CurrentTick;
            var events = CurrentMidiFile.ParseEventsBetweenTicks(from, to);

            CurrentTick += ticksToAdvance;
            
            foreach (var midiEvent in events)
            {
                if (midiEvent.GetEventType() == MidiEventType.Channel && midiEvent.Data?.Length >= 2)
                {
                    byte status = midiEvent.StatusByte;
                    byte velocity = midiEvent.Data[1];
                    byte note = midiEvent.Data[0];
                    int channel = status & 0x0F;

                    //if (midiEvent.Tick == CurrentMidiFile.TickCount)
                    {
                        if ((status & 0xF0) == 0x90 && velocity > 0)
                        {
                            PassedNotes++;
                        }
                        else if ((status & 0xF0) == 0x80 || ((status & 0xF0) == 0x90 && velocity == 0))
                        {
                            
                        }
                    }
                }
            }
        }

        void RenderOverlay()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Once);
            ImGui.Begin("Overlay", ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
            if (CurrentMidiFile != null)
            {
                ImGui.Text($"Time: {Utils.Utils.FormatTime(TimeSpan.FromSeconds(CurrentMidiFile.GetTimeInSeconds(CurrentTick)))} / {Utils.Utils.FormatTime(CurrentMidiFile.Length)}");
                ImGui.Text($"Ticks: {CurrentTick} / {CurrentMidiFile.TickCount}");
                ImGui.Text($"Time Division: {CurrentMidiFile.TimeDivision} PPQ");
                ImGui.Text($"Tempo: {60_000_000.0 / CurrentMidiFile.GetCurrentTempoAtTick(CurrentTick)} BPM");
                ImGui.Text($"Notes: {PassedNotes} / {CurrentMidiFile.NoteCount}");
            }
            else
            {
                ImGui.Text($"MIDI file not loaded.");
            }
            
            ImGui.Text($"FPS: {(1f / DeltaTime):0.0}");
            ImGui.Text($"Heap Memory Usage: {GC.GetTotalMemory(false) / 1024f / 1024f:F2} MB");
            ImGui.Text($"Bass Rendertime: {Bass.CPUUsage}");
            ImGui.End();
        }

        void RenderUI()
        {
            ImGui.SetNextWindowPos(new Vector2(100, 100), ImGuiCond.Once);
            ImGui.Begin("Controls", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);

            // Playback buttons
            if (ImGui.Button(Playing ? "Pause" : "Play"))
            {
                if (CurrentTick == CurrentMidiFile.TickCount)
                {
                    CurrentTick = 0;
                }
                Playing = !Playing;
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
            {
                Playing = false;
                CurrentTick = 0;
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
                        CurrentMidiFile.Dispose();
                        CurrentMidiFile = null;
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
                                CurrentMidiFile = new MidiFile(_filePathInput);
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
                if (CurrentMidiFile != null)
                {
                    CurrentMidiFile.Dispose();
                    CurrentMidiFile = null;
                }
            }
            
            // Progress bar or slider
            //ImGui.SliderFloat("Progress", ref currentTime, 0f, totalTime, $"{Utils.Utils.FormatTime(TimeSpan.FromSeconds(currentTime))} / {Utils.Utils.FormatTime(TimeSpan.FromSeconds(totalTime))}");

            ImGui.End();
        }
    }
}
