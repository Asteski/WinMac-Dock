using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckItemTransitions()
    {
        if (rowTesting || hoverTestInput != null) return;
        var edge = PositionEdge; var alignment = PositionAlignment; var group = Settings.GroupWindows;
        bool reserved = Settings.ReserveSpace, reduce = Settings.ReduceMotion, inspected = inspection, hitTest = root.IsHitTestVisible;
        int duration = Settings.AnimationMs;
        Exception? failure = null; var checks = new List<string>();
        var fixtures = new List<Process>();
        long animations = rowAnimations, frames = rowFrames, intermediate = rowIntermediateFrames, clips = rowClipErrors;
        long handoffResizes = rowHandoffResizes; int completed = rowCompletedDurations.Count;
        try
        {
            inspection = true; root.IsHitTestVisible = false; rowTesting = true;
            hoverTestInput = () => (0, false);
            Settings.ReserveSpace = false; Settings.GroupWindows = false; Settings.ReduceMotion = false; Settings.AnimationMs = 240;
            ResetMagnification(); ApplySettings();
            if (!Motion) throw new Exception("Windows animations must be enabled for the row-transition test");
            string fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
            async Task Wait(Func<bool> predicate, string reason)
            {
                for (int i = 0; i < 100; i++) { if (predicate()) return; await Task.Delay(30); }
                throw new Exception(reason);
            }
            bool Represents(uint pid) => buttons.Any(b => b.Entry.Windows.Any(w => { Native.GetWindowThreadProcessId(w.Handle, out uint owner); return owner == pid; }));
            void Settled()
            {
                if (rowAnimating || MaterialPanel != resting || visibleBounds != resting || strip.Opacity != 1 || hoverArtwork.Opacity != 1 || rowArtwork.Children.Count != 0)
                    throw new Exception($"Row/background did not settle without hover: panel={MaterialPanel}, resting={resting}, input={visibleBounds}");
            }
            foreach (var testEdge in Enum.GetValues<DockEdge>())
            foreach (var testAlignment in Enum.GetValues<DockAlignment>())
            {
                FinishRowTransition(); PositionEdge = testEdge; PositionAlignment = testAlignment; ApplySettings();
                await Task.Delay(200); FinishRowTransition(); ResetMagnification(); visibleBounds = resting; SetHoverClip(); PaintMaterialSurface(0);
                long before = rowAnimations;
                var process = Process.Start(new ProcessStartInfo(fixture) { UseShellExecute = true })!; fixtures.Add(process);
                await Wait(() => Represents((uint)process.Id) && !rowAnimating, "New fixture did not enter and settle");
                if (rowAnimations <= before) throw new Exception("New app appeared without an item transition");
                Settled(); checks.Add($"{testEdge}/{testAlignment}: external launch animates and settles");
                var tasks = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == process.Id; }).ToArray();
                if (tasks.Length != 2) throw new Exception("Fixture did not expose both windows");
                foreach (var task in tasks)
                {
                    before = rowAnimations;
                    Native.PostMessage(task.Handle, 0x10, 0, 0); // Window/taskbar close, without the dock action or manual Reconcile.
                    await Wait(() => !buttons.Any(b => b.Entry.Windows.Any(w => w.Handle == task.Handle)) && !rowAnimating, "Closed window left an item or a stale width");
                    if (rowAnimations <= before) throw new Exception("Closed app disappeared without an item transition");
                    Settled(); checks.Add($"{testEdge}/{testAlignment}: external close animates and updates native material width");
                }
            }
            PositionEdge = DockEdge.Bottom; PositionAlignment = DockAlignment.Center; ApplySettings();
            var rapid = Process.Start(new ProcessStartInfo(fixture) { UseShellExecute = true })!; fixtures.Add(rapid);
            await Wait(() => Represents((uint)rapid.Id) && rowAnimating, "Rapid-change fixture did not animate");
            var rapidTasks = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == rapid.Id; }).ToArray();
            foreach (var task in rapidTasks) SetStressWindowTitle(task.Handle, "Title changed while entering");
            await Wait(() => !rowAnimating, "Title changes did not settle the current transition"); Settled();
            checks.Add("Title changes during entry retarget the row without leaving stale artwork");
            long unchanged = rowAnimations;
            foreach (var task in rapidTasks) Native.ShowWindow(task.Handle, 6);
            await Task.Delay(400);
            if (rowAnimations != unchanged) throw new Exception("Minimize changed the row membership");
            foreach (var task in rapidTasks) Native.ShowWindow(task.Handle, 9);
            await Task.Delay(300);
            checks.Add("Minimize and restore retain their existing dock items");
            Native.PostMessage(rapidTasks[0].Handle, 0x10, 0, 0);
            await Wait(() => rowAnimating, "Close did not start a transition for hover takeover");
            hoverTestInput = () => ((hoverWave?.Length ?? 1) * .5, true);
            UpdateMagnification(0);
            if (rowAnimating || hoverWave == null) throw new Exception("Hover did not take over the active row transition");
            ValidateHoverGeometry();
            hoverTestInput = () => (0, false); ResetMagnification();
            checks.Add("Hover takes over an active close transition with valid artwork geometry");
            Native.PostMessage(rapidTasks[1].Handle, 0x10, 0, 0);
            await Wait(() => !Represents((uint)rapid.Id) && !rowAnimating, "Rapid fixture did not fully close"); Settled();
            hoverTestInput = () => ((hoverWave?.Length ?? ButtonSize * 3) * .15, true);
            UpdateMagnification(0); await Task.Delay(300);
            var hovered = Process.Start(new ProcessStartInfo(fixture) { UseShellExecute = true })!; fixtures.Add(hovered);
            await Wait(() => Represents((uint)hovered.Id) && rowAnimating, "Hovered row did not animate fixture entry");
            await Wait(() => !rowAnimating, "Hovered entry did not settle"); ValidateHoverGeometry();
            foreach (var task in tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == hovered.Id; }))
            {
                Native.PostMessage(task.Handle, 0x10, 0, 0);
                await Wait(() => !buttons.Any(b => b.Entry.Windows.Any(w => w.Handle == task.Handle)) && !rowAnimating, "Hovered external close did not settle");
                ValidateHoverGeometry();
            }
            checks.Add("Entry and external closes while magnified preserve valid native and hover geometry");
            hoverTestInput = () => (0, false); ResetMagnification();
            Settings.ReduceMotion = true; ApplySettings();
            long disabled = rowAnimations;
            var reduced = Process.Start(new ProcessStartInfo(fixture) { UseShellExecute = true })!; fixtures.Add(reduced);
            await Wait(() => Represents((uint)reduced.Id), "Reduce-motion fixture did not appear");
            if (rowAnimating || rowAnimations != disabled) throw new Exception("Reduce motion still started an item animation");
            Settled(); checks.Add("Reduce motion applies row and background immediately");
            foreach (var task in tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == reduced.Id; })) Native.PostMessage(task.Handle, 0x10, 0, 0);
            await Wait(() => !Represents((uint)reduced.Id), "Reduce-motion close left an item"); Settled();
            if (rowClipErrors != clips || rowIntermediateFrames == intermediate) throw new Exception($"Transition frame coverage failed: clips={rowClipErrors - clips}, intermediate={rowIntermediateFrames - intermediate}");
            if (rowHandoffResizes != handoffResizes) throw new Exception($"Transition handoff resized the native surfaces {rowHandoffResizes - handoffResizes} times");
            if (rowCompletedDurations.Count == completed || rowCompletedDurations.Skip(completed).Min() < DockMotion.RearrangeMs) throw new Exception("Item animations completed faster than pinned rearrangement");
            checks.Add("Entry and exit use pinned rearrangement timing, with no native surface resize at handoff");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            FinishRowTransition(); rowTesting = false; hoverTestInput = null;
            foreach (var process in fixtures)
            {
                foreach (var task in tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == process.Id; })) Native.PostMessage(task.Handle, 0x10, 0, 0);
                process.Dispose();
            }
            PositionEdge = edge; PositionAlignment = alignment; Settings.GroupWindows = group; Settings.ReserveSpace = reserved;
            Settings.ReduceMotion = reduce; Settings.AnimationMs = duration; inspection = inspected; root.IsHitTestVisible = hitTest;
            ResetMagnification(); ApplySettings();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "item-transitions-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Animations = rowAnimations - animations,
            Frames = rowFrames - frames, IntermediateFrames = rowIntermediateFrames - intermediate, ClipViolations = rowClipErrors - clips,
            HandoffSurfaceResizes = rowHandoffResizes - handoffResizes, ShortestCompletedAnimationMs = rowCompletedDurations.Skip(completed).DefaultIfEmpty(0).Min(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
