using System.Numerics;
using ImGuiNET;
using PhosphorMP.Parser;
using Veldrid;

namespace PhosphorMP.Rendering
{
    public class UserInterfaceHandler
    {
        public ImGuiRenderer ImGuiUserInterfaceRenderer { get; private set; }
        
        private bool _showFileDialog;
        private string _filePathInput = "";
        private uint _createdWidth;
        private uint _createdHeight;

        private Logic Logic => Logic.Singleton;
        private Framedumper Framedumper => Renderer.Singleton.Framedumper;
        public GraphicsDevice GraphicsDevice => Renderer.Singleton.GraphicsDevice;
        public VisualizationFramebuffer VisualizationFramebuffer => Renderer.Singleton.VisualizationFramebuffer;

        public UserInterfaceHandler()
        {
            Init();
        }

        public void Init(bool dispose = false)
        {
            var outputDesc = GraphicsDevice.SwapchainFramebuffer.OutputDescription;
            var width = (uint)GraphicsDevice.MainSwapchain.Framebuffer.Width; // TODO: Use VisualizationFramebuffer
            var height = (uint)GraphicsDevice.MainSwapchain.Framebuffer.Height;

            if (dispose && ImGuiUserInterfaceRenderer != null)
            {
                ImGuiUserInterfaceRenderer.Dispose();
                ImGuiUserInterfaceRenderer = null;
            }

            if (ImGuiUserInterfaceRenderer == null)
            {
                ImGuiUserInterfaceRenderer = new ImGuiRenderer(
                    GraphicsDevice,
                    outputDesc,
                    (int)width,
                    (int)height);
                _createdWidth = width;
                _createdHeight = height;
            }
        }

        // Call this from the render loop before calling ImGuiUserInterfaceRenderer.Update/Render
        public void HandleResizeIfNeeded()
        {
            var currentW = (uint)GraphicsDevice.MainSwapchain.Framebuffer.Width;
            var currentH = (uint)GraphicsDevice.MainSwapchain.Framebuffer.Height;
            if (ImGuiUserInterfaceRenderer == null || currentW != _createdWidth || currentH != _createdHeight)
            {
                Init(dispose: true);
            }
        }
        
        public void DefineUserInterface()
        {
            Vector4 greenBg = new Vector4(0.0275f, 0.6824f, 0.0275f, 0.25f); 
            Vector4 greenBorder = new Vector4(0.0275f, 0.6824f, 0.0275f, 0.75f); 
            Vector4 greenTitle = new Vector4(0.0275f, 0.6824f, 0.0275f, 0.8f); 
            Vector4 greenButton = new Vector4(0.0275f, 0.6824f, 0.0275f, 0.6f); 
            Vector4 greenButtonHovered = new Vector4(0.0275f, 0.6824f, 0.0275f, 0.8f); 
            Vector4 greenButtonActive = new Vector4(0.0275f, 0.6824f, 0.0275f, 1.0f); 

            ImGui.PushStyleColor(ImGuiCol.WindowBg, greenBg);
            ImGui.PushStyleColor(ImGuiCol.Border, greenBorder);
            ImGui.PushStyleColor(ImGuiCol.TitleBg, greenTitle);
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, greenTitle);
            ImGui.PushStyleColor(ImGuiCol.Button, greenButton);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, greenButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, greenButtonActive);

            // Begin window without setting position yet
            ImGui.Begin("UI", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoNav |  ImGuiWindowFlags.NoMove);

            // --- Playback buttons ---
            if (ImGui.Button(Logic.Playing ? "Pause" : "Play"))
            {
                if (Logic.CurrentMidiFile == null) return;
                if (Logic.CurrentTick == Logic.CurrentMidiFile.TickCount)
                    Logic.CurrentTick = Logic.StartupDelayTicks;
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
                Framedumper.FPS = 20;
                Framedumper.Start();
                Logic.Playing = true;
            }

            if (ImGui.Button("Load"))
            {
                Logic.CurrentMidiFile?.Dispose();
                _showFileDialog = true;
                ImGui.OpenPopup("Load File");
            }

            HandleParsingPopup();
            HandleFileDialog();

            ImGui.SameLine();
            if (ImGui.Button("Unload"))
            {
                if (Logic.CurrentMidiFile != null)
                {
                    Logic.CurrentMidiFile.Dispose();
                    Logic.CurrentMidiFile = null;
                }
            }

            // --- Center window dynamically ---
            Vector2 windowSize = ImGui.GetWindowSize(); // get actual window size after adding all buttons
            Vector2 screenSize = new Vector2(Window.Singleton.BaseSdl2Window.Width, Window.Singleton.BaseSdl2Window.Height);
            Vector2 windowPos = (screenSize - windowSize) / 2;
            ImGui.SetWindowPos(windowPos, ImGuiCond.Always); // reposition every frame

            ImGui.End();
        }


        private void HandleParsingPopup()
        {
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
        }

        private void HandleFileDialog()
        {
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
        }
    }
}
