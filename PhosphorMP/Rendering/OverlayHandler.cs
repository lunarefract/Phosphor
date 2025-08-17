using System.Numerics;
using C5;
using ImGuiNET;
using Veldrid;

namespace PhosphorMP.Rendering
{
    public class OverlayHandler
    {
        public ImGuiRenderer ImGuiRendererOverlay { get; private set; }
        public ArrayList<string> Lines { get; private set; }
        public Vector2 Position { get; internal set; }
        public GraphicsDevice GraphicsDevice => Renderer.Singleton.GraphicsDevice;
        public VisualizationFramebuffer  VisualizationFramebuffer => Renderer.Singleton.VisualizationFramebuffer;
        public Logic Logic => Logic.Singleton;
        public Vector4 WindowBackgroundColor { get; set; } = new(0.0275f, 0.6824f, 0.0275f, 0.1f);
        public Vector4 FrameWindowBackgroundColor { get; set; } = new(0.0275f, 0.6824f, 0.0275f, 0.25f);

        public const ImGuiWindowFlags OverlayFlags = ImGuiWindowFlags.NoInputs | 
                                                     ImGuiWindowFlags.NoDecoration | 
                                                     ImGuiWindowFlags.NoSavedSettings |
                                                     ImGuiWindowFlags.AlwaysAutoResize |
                                                     ImGuiWindowFlags.NoFocusOnAppearing | 
                                                     ImGuiWindowFlags.NoNav;

        public OverlayHandler()
        {
            Position = new Vector2(10, 10);
            Lines =
            [
                "Time: {Time}",
                "Ticks: {Ticks}",
                "Time Division: {TimeDivision}",
                "Tempo: {Tempo}",
                "Notes: {Notes}",
                "FPS: {FPS}",
                "Heap Memory Usage: {HeapMemoryUsage}",
                "visualNotes count: {VisualNotesCount}"
            ];
            Init();
        }

        public void Init(bool dispose = false)
        {
            if (dispose)
            {
                ImGuiRendererOverlay.Dispose();
            }
            ImGuiRendererOverlay = new ImGuiRenderer(
                GraphicsDevice,
                VisualizationFramebuffer.Base.OutputDescription,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Width,
                (int)GraphicsDevice.MainSwapchain.Framebuffer.Height);
        }

        public ArrayList<string> ReplacePlaceholders()
        {
            ArrayList<string> replaced = [];
            foreach (var line in Lines)
            {
                string processed = line;

                if (Logic.CurrentMidiFile != null)
                {
                    processed = processed
                        .Replace("{Time}", $"{Utils.Utils.FormatTime(TimeSpan.FromSeconds(Logic.CurrentMidiFile.GetTimeInSeconds(Logic.CurrentTick)))} / {Utils.Utils.FormatTime(Logic.CurrentMidiFile.Length)}")
                        .Replace("{Ticks}", $"{Logic.CurrentTick} / {Logic.CurrentMidiFile.TickCount}")
                        .Replace("{TimeDivision}", $"{Logic.CurrentMidiFile.TimeDivision} PPQ")
                        .Replace("{Tempo}", $"{60_000_000.0 / Logic.CurrentMidiFile.GetCurrentTempoAtTick(Logic.CurrentTick):0.00} BPM")
                        .Replace("{Notes}", $"{Logic.PassedNotes} / {Logic.CurrentMidiFile.NoteCount}");
                }
                else
                {
                    processed = processed
                        .Replace("{Time}", "N/A")
                        .Replace("{Ticks}", "N/A")
                        .Replace("{TimeDivision}", "N/A")
                        .Replace("{Tempo}", "N/A")
                        .Replace("{Notes}", "N/A");
                }

                processed = processed
                    .Replace("{FPS}", $"{(1f / Program.TargetDeltaTime):0.0}")
                    .Replace("{HeapMemoryUsage}", $"{GC.GetTotalMemory(false) / 1024f / 1024f:F2} MB")
                    .Replace("{VisualNotesCount}", $"{Renderer.Singleton.VisualNotes.Count}");

                replaced.Add(processed);
            }
            return replaced;
        }

        public void DefineOverlay()
        {
            if (Lines.Count == 0) return;
            ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBackgroundColor);
            ImGui.PushStyleColor(ImGuiCol.Border, FrameWindowBackgroundColor);
            ImGui.SetNextWindowPos(Position, ImGuiCond.Once);
            ImGui.Begin("Overlay", OverlayFlags);
            var lines = ReplacePlaceholders();
            foreach (var line in lines)
            {
                ImGui.Text(line);
            }
            ImGui.End();
        }
    }
}