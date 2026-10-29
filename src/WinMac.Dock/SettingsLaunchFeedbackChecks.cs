namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckSettingsLaunchFeedback()
    {
        bool inspected = inspection, wasHidden = hidden, manual = manuallyHidden;
        Exception? failure = null; var results = new List<object>();
        try
        {
            var item = Settings.Items.First(ShellService.IsSystemSettings);
            if (!Motion || dockOverflow) throw new InvalidOperationException("Launch feedback check requires enabled animations and no overflow");
            inspection = true; ShowDock();
            foreach (var uri in new[] { "ms-settings:", "ms-settings:network-status" })
            {
                LaunchSystemSettings(item, uri);
                double maximum = 0;
                for (int frame = 0; frame < 40; frame++)
                {
                    await Task.Delay(20);
                    var slot = hoverSlots.FirstOrDefault(s => s.Element.Tag is WinMac.Dock.Core.DockEntry entry && entry.Item.Id == item.Id);
                    if (slot != null) maximum = Math.Max(maximum, Math.Abs(Vertical ? slot.ArtworkShift.X : slot.ArtworkShift.Y));
                }
                results.Add(new { Uri = uri, MaximumBounceDips = maximum });
                if (maximum < .5) throw new InvalidOperationException("Settings launch did not visibly bounce: " + uri);
                await Task.Delay(Settings.LaunchBounceMs + 50);
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            inspection = inspected; manuallyHidden = manual;
            if (wasHidden) HideDock(true); else ShowDock(!manual);
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "settings-launch-feedback-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
