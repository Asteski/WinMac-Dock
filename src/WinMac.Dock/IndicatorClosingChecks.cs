using System.Text.Json;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private async Task CheckIndicatorClosing()
    {
        var originalItems = Settings.Items; var originalTasks = stressTasks;
        bool grouped = Settings.GroupWindows, combined = Settings.CombinePinnedIconsWithRunningApplications, inspected = inspection;
        var checks = new List<string>(); Exception? failure = null;
        try
        {
            inspection = true; Settings.GroupWindows = true; Settings.CombinePinnedIconsWithRunningApplications = true;
            var pin = new DockItem { Kind = ItemKind.Application, Name = "Indicator test", Target = @"C:\indicator-close-fixture.exe" };
            Settings.Items = [pin];
            TaskWindow First() => new(0x7fff0050, "Indicator test one", pin.Target, "", default, false, false);
            TaskWindow Second() => new(0x7fff0051, "Indicator test two", pin.Target, "", default, false, false);
            stressTasks = [First(), Second()]; ShowDock(false); ApplySettings();
            await Task.Delay(DockMotion.RearrangeMs + 100);
            stressTasks = [First()]; Reconcile();
            var badge = rowMoves.SingleOrDefault(m => m.ClosingIndicator && m.Sprite.Key.EndsWith(":badge"));
            if (badge == null || badge.To.Width != 0 || badge.To.Height != 0) throw new Exception("Grouped indicator did not start its native scale exit.");
            await Task.Delay(60);
            if (badge.Current.Width <= 0 || badge.Current.Width >= badge.From.Width) throw new Exception("Group indicator did not interpolate its closing scale.");
            checks.Add("Grouped indicator scales smoothly to zero when the group becomes one window");
            await Task.Delay(DockMotion.RearrangeMs + 50);
            foreach (bool focused in new[] { false, true })
            {
                stressTasks = [First()]; Reconcile(); await Task.Delay(DockMotion.RearrangeMs + 50);
                var border = runningIndicators.Values.Single(); StopIndicatorAnimations();
                bool rotated = Vertical && !Settings.ForceIndicatorsToItemBase;
                border.Width = rotated ? 3 : focused ? 16 : 6; border.Height = rotated ? focused ? 16 : 6 : 3;
                PositionIndicator(border); root.UpdateLayout();
                stressTasks = []; Reconcile();
                var indicator = rowMoves.SingleOrDefault(m => m.ClosingIndicator && m.Sprite.Key.EndsWith(":indicator"));
                if (indicator == null || (rotated ? indicator.To.Height != 0 || indicator.To.Width != indicator.From.Width : indicator.To.Width != 0 || indicator.To.Height != indicator.From.Height))
                    throw new Exception("Running indicator does not collapse along its long axis.");
                await Task.Delay(60);
                double size = rotated ? indicator.Current.Height : indicator.Current.Width;
                if (size <= 0 || size >= (rotated ? indicator.From.Height : indicator.From.Width)) throw new Exception("Running indicator did not shrink smoothly.");
                if (Math.Abs(indicator.Current.X + indicator.Current.Width / 2 - indicator.From.X - indicator.From.Width / 2) > .001
                    || Math.Abs(indicator.Current.Y + indicator.Current.Height / 2 - indicator.From.Y - indicator.From.Height / 2) > .001)
                    throw new Exception("Running indicator centre moved during collapse.");
                await Task.Delay(150);
                if (indicator.Opacity != 0) throw new Exception("Running indicator did not finish its 167ms exit.");
                checks.Add((focused ? "Focused" : "Unfocused") + " indicator collapses to its fixed centre and disappears in 167ms");
                await Task.Delay(DockMotion.RearrangeMs);
            }
            foreach (var kind in Enum.GetValues<CapturePinKind>())
            {
                var item = new DockItem { Kind = ItemKind.File, Name = "Arrival indicator test", Target = Environment.ProcessPath!, IsDownloadedPin = true, CapturePin = kind };
                Settings.Items = [item]; stressTasks = []; Reconcile();
                await Task.Delay(DockMotion.RearrangeMs + 50);
                downloadIndicators.Values.Single().Opacity = 1; root.UpdateLayout();
                Settings.Items = []; Reconcile();
                var arrival = rowMoves.SingleOrDefault(m => m.ClosingIndicator && m.Sprite.Key.EndsWith(":download"));
                if (arrival == null || arrival.To.Width != 0 || arrival.To.Height != arrival.From.Height)
                    throw new Exception("Arrival indicator does not collapse horizontally: " + kind);
                await Task.Delay(60);
                if (arrival.Current.Width <= 0 || arrival.Current.Width >= arrival.From.Width
                    || Math.Abs(arrival.Current.X + arrival.Current.Width / 2 - arrival.From.X - arrival.From.Width / 2) > .001)
                    throw new Exception("Arrival indicator does not shrink into its centre: " + kind);
                await Task.Delay(150);
                if (arrival.Opacity != 0) throw new Exception("Arrival indicator did not disappear: " + kind);
                checks.Add((kind == CapturePinKind.None ? "Download" : kind.ToString()) + " indicator collapses to its fixed centre and disappears in 167ms");
                await Task.Delay(DockMotion.RearrangeMs);
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            FinishRowTransition(); Settings.Items = originalItems; stressTasks = originalTasks;
            Settings.GroupWindows = grouped; Settings.CombinePinnedIconsWithRunningApplications = combined; inspection = inspected;
            ApplySettings();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "indicator-closing-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

