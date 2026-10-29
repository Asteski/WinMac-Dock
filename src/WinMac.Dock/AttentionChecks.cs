using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private async Task CheckAttention()
    {
        bool blink = Settings.PreventAttentionBlinking, reveal = Settings.ShowDockForAttention;
        var originalAnimation = Settings.AttentionAnimation;
        var originalAttention = attentionWindows.ToArray();
        bool inspected = inspection, wasHidden = hidden, manual = manuallyHidden;
        Process? fixture = null; Exception? failure = null; var checks = new List<string>();
        try
        {
            inspection = true; Settings.PreventAttentionBlinking = true; Settings.ShowDockForAttention = true;
            attentionWindows.Clear(); attentionBounces.Clear(); attentionBounceEnds.Clear();
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
            fixture = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            TaskWindow? task = null;
            for (int i = 0; i < 100 && task == null; i++)
            {
                task = tracker.Read().FirstOrDefault(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == fixture!.Id; });
                if (task == null) await Task.Delay(30);
            }
            if (task == null) throw new Exception("Attention fixture did not appear");
            Reconcile(); HideDock(true); AttentionRequested(task.Handle);
            if (hidden || !attentionWindows.Contains(task.Handle)) throw new Exception("Attention did not reveal and latch the dock");
            checks.Add("Attention reveals a hidden dock and latches the requesting window");
            if (!AttentionRequiresDock(0)) throw new Exception("Attention hold ended before focus");
            checks.Add("Dock remains visible while the requesting app is unfocused");
            AttentionRequiresDock(task.Handle);
            if (attentionWindows.Contains(task.Handle)) throw new Exception("Focusing the requesting window did not clear attention");
            checks.Add("Focusing the requesting app clears the attention hold");
            Settings.ShowDockForAttention = false; HideDock(true); AttentionRequested(task.Handle);
            if (!hidden) throw new Exception("Disabled reveal option showed the dock");
            checks.Add("Reveal and blink suppression remain independent options");
            var unfocused = tracker.Read().FirstOrDefault(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == fixture!.Id && w.Handle != Native.GetForegroundWindow(); });
            if (unfocused == null) throw new Exception("No unfocused attention test window.");
            Settings.PreventAttentionBlinking = false; Settings.AttentionAnimation = AttentionAnimation.HighBounce;
            ShowDock(false); Reconcile(); FinishRowTransition(); FinishVisibilityAnimation(); AttentionRequested(unfocused.Handle);
            var entry = buttons.First(b => b.Entry.Windows.Any(w => w.Handle == unfocused.Handle)).Entry;
            if (!attentionBounces.ContainsKey(entry.Item.Id)) throw new Exception("High attention bounce did not start.");
            await Task.Delay(100);
            if (hoverWave == null) throw new Exception("Attention drawing surface was not prepared.");
            var hoverBounds = HoverInteractionBounds;
            var outside = PositionEdge switch
            {
                DockEdge.Top => new Native.POINT { X = hoverBounds.Left + hoverBounds.Width / 2, Y = hoverBounds.Bottom + 1 },
                DockEdge.Left => new Native.POINT { X = hoverBounds.Right + 1, Y = hoverBounds.Top + hoverBounds.Height / 2 },
                DockEdge.Right => new Native.POINT { X = hoverBounds.Left - 1, Y = hoverBounds.Top + hoverBounds.Height / 2 },
                _ => new Native.POINT { X = hoverBounds.Left + hoverBounds.Width / 2, Y = hoverBounds.Top - 1 }
            };
            if (!visibleBounds.Contains(outside.X, outside.Y) || HoverContains(outside))
                throw new Exception("Bounce-only drawing space still triggers the hover lens.");
            if (!BouncingArtworkContains(outside))
            {
                nint packed = (nint)((uint)(ushort)outside.X | ((uint)(ushort)outside.Y << 16));
                if (WindowMessage(Handle, 0x0084, 0, packed, 1, 0) != -1)
                    throw new Exception("Empty bounce drawing space still intercepts pointer input.");
            }
            checks.Add("Bounce drawing space is excluded from magnification and empty-space pointer hit testing");
            await Task.Delay(Settings.LaunchBounceMs + 300);
            if (!attentionBounces.ContainsKey(entry.Item.Id)) throw new Exception("High attention bounce stopped at the launch bounce limit.");
            checks.Add("High bounce continues beyond the finite launch animation");
            ClearClickedAttention(entry);
            if (!attentionBounces.ContainsKey(entry.Item.Id) || !attentionBounceEnds.ContainsKey(entry.Item.Id) || attentionWindows.Contains(unfocused.Handle))
                throw new Exception("Clicking attention did not clear the request while retaining the final bounce.");
            await Task.Delay(Settings.LaunchBounceMs + 150);
            if (attentionBounces.ContainsKey(entry.Item.Id) || attentionBounceEnds.ContainsKey(entry.Item.Id)) throw new Exception("Acknowledged bounce did not stop after finishing.");
            checks.Add("Clicking clears attention immediately while allowing the current bounce sequence to finish");
            AttentionRequested(unfocused.Handle); await Task.Delay(100);
            HoldHoverForClick(new Windows.Foundation.Point());
            if (!hoverRendering) throw new Exception("Pointer press paused the high-jump renderer.");
            ClearClickedAttention(entry);
            if (!attentionBounceEnds.ContainsKey(entry.Item.Id)) throw new Exception("Clicked high jump was interrupted.");
            await Task.Delay(550);
            if (!attentionBounces.ContainsKey(entry.Item.Id)) throw new Exception("Clicked high jump stopped before its settling bounces.");
            await Task.Delay(Settings.LaunchBounceMs + 150);
            if (attentionBounces.ContainsKey(entry.Item.Id)) throw new Exception("Clicked high jump did not finish its settling sequence.");
            checks.Add("Clicking during the first high jump preserves its landing and smaller settling bounces, then stops");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            attentionWindows.Clear(); attentionBounces.Clear(); attentionBounceEnds.Clear(); Settings.PreventAttentionBlinking = blink; Settings.ShowDockForAttention = reveal; Settings.AttentionAnimation = originalAnimation;
            attentionWindows.UnionWith(originalAttention.Where(Native.IsWindow));
            foreach (var task in tracker.Read(true)) { Native.GetWindowThreadProcessId(task.Handle, out uint pid); if (fixture != null && pid == fixture.Id) Native.PostMessage(task.Handle, 0x10, 0, 0); }
            fixture?.Dispose(); inspection = inspected; manuallyHidden = manual;
            if (wasHidden) HideDock(true); else ShowDock(!manual); ApplySettings();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "attention-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
