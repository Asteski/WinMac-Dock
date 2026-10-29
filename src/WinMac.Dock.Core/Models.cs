using System.Text.Json.Serialization;

namespace WinMac.Dock.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemKind { Application, File, Folder, RecycleBin, StartMenu, Separator, TaskView, Search }
public enum AdditionalCommandsVisibility { MainMenu, Submenu, Hidden }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HideMode { AlwaysVisible, AfterDelay, Maximized, ActiveWindowOverlap, EdgeToggle }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ManualRevealBehavior { TemporaryOverlay, RestoreReservation }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DockTheme { System, Light, Dark }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DockMaterial { None, Acrylic, Mica, Default }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DockEdge { Bottom, Left, Right, Top }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DockAlignment { Start, Center, End }
[JsonConverter(typeof(FolderViewConverter))]
public enum FolderView { Grid = 0, List = 2, Menu = 3 }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FolderSort { Name, Size, Modified, Created, Type }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DockClickAction { Default, Nothing, Minimize, Close, BringToFront, BringToFrontOrMinimize, OpenTaskMenu, MinimizeSimilar, RestoreSimilar, CloseSimilar, Open }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IndicatorAccentMode { None, Focused, All }
public enum DockMouseButton { Left, Right, Middle }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DockHighlightMode { Background, Border, BackgroundAndBorder }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GroupedClickBehavior { ShowWindowList, ActivateLastUsed, Sequential }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GroupIndicatorCorner { TopRight, TopLeft, BottomRight, BottomLeft }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GroupIndicatorShape { Circle, Square, Squircle }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AttentionAnimation { Blink, HighBounce }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CapturePinKind { None, Screenshot, Recording }

public sealed record SnippingOptions
{
    public bool PinNewCaptures { get; set; }
    public bool UnpinAfterOpening { get; set; } = true;
    public int PinLimit { get; set; } = 5;
}

public sealed record ClickActions
{
    public DockClickAction Left { get; set; } = DockClickAction.Default;
    public DockClickAction Right { get; set; } = DockClickAction.OpenTaskMenu;
    public DockClickAction Middle { get; set; } = DockClickAction.Close;
    public DockClickAction For(DockMouseButton button) => button switch { DockMouseButton.Right => Right, DockMouseButton.Middle => Middle, _ => Left };
    public void Validate()
    {
        if (!Enum.IsDefined(Left)) Left = DockClickAction.Default;
        if (!Enum.IsDefined(Right)) Right = DockClickAction.OpenTaskMenu;
        if (!Enum.IsDefined(Middle)) Middle = DockClickAction.Close;
    }
}
public sealed record TaskClickOverride
{
    public string Identity { get; set; } = "";
    public string Name { get; set; } = "";
    public ClickActions Actions { get; set; } = new();
}

public sealed record FolderOptions
{
    public bool FilesOnly { get; set; }
    public FolderView View { get; set; } = FolderView.Grid;
    public FolderSort Sort { get; set; } = FolderSort.Name;
    public bool Descending { get; set; }
    public bool StackIcon { get; set; }
    public int GridRows { get; set; } = 5;
    public int GridColumns { get; set; } = 5;
    public bool? HideFolderNameInGrid { get; set; } = false;
    public bool ShowDownloadsIconUnderStack { get; set; }
    public bool OpenNewDownloadsAutomatically { get; set; }
    public bool PinNewDownloads { get; set; } = true;
    public bool UnpinDownloadsAfterOpening { get; set; } = true;
    public int DownloadPinLimit { get; set; } = 5;
}

public sealed record DockItem
{
    public AdditionalCommandsVisibility AdditionalCommands { get; set; }
    public AdditionalCommandsVisibility EdgeWorkspacesCommands { get; set; } = AdditionalCommandsVisibility.Submenu;
    public AdditionalCommandsVisibility EdgeProfilesCommands { get; set; } = AdditionalCommandsVisibility.Submenu;
    public AdditionalCommandsVisibility EdgeFavoritesCommands { get; set; } = AdditionalCommandsVisibility.Submenu;
    public AdditionalCommandsVisibility ExplorerRecentCommands { get; set; } = AdditionalCommandsVisibility.Submenu;
    public AdditionalCommandsVisibility ExplorerPinnedCommands { get; set; } = AdditionalCommandsVisibility.Submenu;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ItemKind Kind { get; set; }
    public string Name { get; set; } = "Item";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string AppUserModelId { get; set; } = "";
    public string CustomIcon { get; set; } = "";
    public string DarkModeIcon { get; set; } = "";
    public string RecycleEmptyIcon { get; set; } = "";
    public string RecycleFullIcon { get; set; } = "";
    public string RecycleEmptyDarkIcon { get; set; } = "";
    public string RecycleFullDarkIcon { get; set; } = "";
    public bool RunAsAdministrator { get; set; }
    public bool ConfirmRecycleBinEmpty { get; set; } = true;
    public bool PlayRecycleBinSounds { get; set; }
    public int RecycleBinSoundVolumePercent { get; set; } = 300;
    public bool ShowThumbnail { get; set; }
    public bool PinUsbDrives { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAutoUsbPin { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public AttachedDriveKind AttachedDriveKind { get; set; }
    public bool IsDownloadedPin { get; set; }
    public CapturePinKind CapturePin { get; set; }
    public SnippingOptions Snipping { get; set; } = new();
    public FolderOptions Folder { get; set; } = new();
    public ClickActions? ClickActions { get; set; }
}

public sealed record DisplayDockLayout
{
    public HideMode? HideMode { get; set; }
    public bool? MagnificationEnabled { get; set; }
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    public DockAlignment Alignment { get; set; } = DockAlignment.Center;
}

public sealed record DockSettings
{
    public Dictionary<string, DockItem> RememberedItemProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 1;
    public int IconSize { get; set; } = 32;
    public int FolderFlyoutIconSize { get; set; } = 40;
    public int MagnifiedSize { get; set; } = 48;
    // 0 keeps icons at their normal size; 100 uses the largest enlargement
    // that fits inside the dock without moving its layout.
    public int HoverMagnificationPercent { get; set; } = 100;
    public int MagnificationSpan { get; set; } = 5;
    public int MagnificationMs { get; set; } = 120;
    public int AnimationMs { get; set; } = 200;
    public int RunningClickAnimationMs { get; set; } = 667;
    public int LaunchBounceMs { get; set; } = 940;
    public int HideDelayMs { get; set; } = 500;
    public int ManualOverlayHideDelayMs { get; set; } = 500;
    public int RevealDelayMs { get; set; } = 150;
    public HideMode HideMode { get; set; } = HideMode.Maximized;
    public bool StartHidden { get; set; }
    public bool HideWindowsTaskbar { get; set; }
    public bool PreventAttentionBlinking { get; set; }
    public AttentionAnimation AttentionAnimation { get; set; } = AttentionAnimation.HighBounce;
    public bool ShowDockForAttention { get; set; }
    public bool ReserveSpace { get; set; } = true;
    public ManualRevealBehavior ManualRevealBehavior { get; set; } = ManualRevealBehavior.TemporaryOverlay;
    public string Monitor { get; set; } = "";
    public bool ShowOnAllMonitors { get; set; }
    public Dictionary<string, DisplayDockLayout> DisplayLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DisplayDockLayout LayoutForDisplay(string identity)
    {
        if (!DisplayLayouts.TryGetValue(identity, out var layout))
            DisplayLayouts[identity] = layout = new() { Edge = Edge, Alignment = Alignment, HideMode = HideMode, MagnificationEnabled = MagnificationEnabled };
        return layout;
    }
    public bool HotkeyEnabled { get; set; }
    public uint HotkeyModifiers { get; set; } = 3;
    public int HotkeyKey { get; set; } = 0x44;
    public bool ShowMenuGlyphs { get; set; }
    public bool StartAtLogin { get; set; }
    public bool AlwaysRunAsAdministrator { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public bool MagnificationEnabled { get; set; } = true;
    public bool ShowDockSettingsInItemMenus { get; set; }
    public bool LinkDockMargins { get; set; }
    public bool GroupWindows { get; set; }
    public GroupedClickBehavior GroupedClickBehavior { get; set; } = GroupedClickBehavior.ShowWindowList;
    public bool CombinePinnedIconsWithRunningApplications { get; set; } = true;
    public bool ShowItemsFromAllVirtualDesktops { get; set; }
    public bool ShowInTaskView { get; set; }
    public bool LockIconPositions { get; set; }
    public int IconSpacing { get; set; } = 6;
    public DockTheme Theme { get; set; } = DockTheme.System;
    public int BackgroundStrength { get; set; } = 65;
    public DockMaterial Material { get; set; } = DockMaterial.Acrylic;
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    public DockAlignment Alignment { get; set; } = DockAlignment.Center;
    public int FloatingGap { get; set; }
    public bool ActivateThroughScreenEdgeGap { get; set; } = true;
    public bool KeepHiddenEdgeTrigger { get; set; } = true;
    public bool FullScreenEdgeTrigger { get; set; }
    public int DockMarginLeft { get; set; } = 4;
    public int DockMarginTop { get; set; } = 4;
    public int DockMarginRight { get; set; } = 4;
    public int DockMarginBottom { get; set; } = 4;
    public bool ShowDockBorder { get; set; }
    public string DockBorderColor { get; set; } = "";
    public int DockBorderOpacity { get; set; } = 100;
    public string ItemIndicatorColor { get; set; } = "";
    public int ItemIndicatorOpacity { get; set; } = 100;
    public bool RoundedCorners { get; set; } = true;
    public bool SpanFullWidth { get; set; }
    public bool SpanFullWidthOnlyWhenMaximized { get; set; }
    public bool SquareCornersWhenSpanning { get; set; }
    public GroupIndicatorCorner GroupIndicatorCorner { get; set; } = GroupIndicatorCorner.TopRight;
    public GroupIndicatorShape GroupIndicatorShape { get; set; } = GroupIndicatorShape.Circle;
    public bool SquareGroupIndicatorsWhenUnrounded { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool WindowsTaskbarHighlights { get => true; set { } }
    public bool FollowWindowsAnimations { get; set; } = true;
    public bool ShowIconHighlights { get; set; }
    public DockHighlightMode HighlightMode { get; set; } = DockHighlightMode.Background;
    public bool ShowIconLabels { get; set; } = true;
    public bool ShowFileExtensionsInTooltips { get; set; }
    public int IndicatorPosition { get; set; }
    public bool ForceIndicatorsToItemBase { get; set; }
    public IndicatorAccentMode IndicatorAccent { get; set; } = IndicatorAccentMode.Focused;
    public bool HideImmediatelyForFullscreen { get; set; } = true;
    public List<string> FullscreenExclusions { get; set; } = [];
    public bool RedirectWindowAnimations { get; set; } = true;
    public bool ShowMinimizedApplicationsOnly { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool UseWindowsMaterialDefaults { get => Material == DockMaterial.Default; set { } }
    public int MaterialLuminosity { get; set; } = 20;
    public List<DockItem> Items { get; set; } = [];
    public ClickActions DefaultClickActions { get; set; } = new();
    public List<TaskClickOverride> TaskClickOverrides { get; set; } = [];

    public void Validate()
    {
        DisplayLayouts = new(DisplayLayouts ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var key in DisplayLayouts.Keys.ToArray())
        {
            var layout = DisplayLayouts[key] ?? new DisplayDockLayout { Edge = Edge, Alignment = Alignment };
            layout.HideMode ??= HideMode;
            layout.MagnificationEnabled ??= MagnificationEnabled;
            if (!Enum.IsDefined(layout.HideMode.Value)) layout.HideMode = HideMode;
            if (!Enum.IsDefined(layout.Edge)) layout.Edge = DockEdge.Bottom;
            if (!Enum.IsDefined(layout.Alignment)) layout.Alignment = DockAlignment.Center;
            DisplayLayouts[key] = layout;
        }
        if (Version != 1) throw new InvalidDataException($"Unsupported settings version {Version}.");
        IconSize = Math.Clamp(IconSize, 16, 80);
        FolderFlyoutIconSize = Math.Clamp(FolderFlyoutIconSize, 16, 64);
        IconSpacing = Math.Clamp(IconSpacing, -16, 8);
        BackgroundStrength = Math.Clamp(BackgroundStrength, 0, 100);
        DockBorderOpacity = Math.Clamp(DockBorderOpacity, 0, 100);
        ItemIndicatorOpacity = Math.Clamp(ItemIndicatorOpacity, 0, 100);
        DockBorderColor = ValidateColor(DockBorderColor);
        ItemIndicatorColor = ValidateColor(ItemIndicatorColor);
        MaterialLuminosity = Math.Clamp(MaterialLuminosity, 0, 100);
        if (!Enum.IsDefined(Material)) Material = DockMaterial.Acrylic;
        if (!Enum.IsDefined(Edge)) Edge = DockEdge.Bottom;
        if (!Enum.IsDefined(Alignment)) Alignment = DockAlignment.Center;
        if (!Enum.IsDefined(GroupIndicatorCorner)) GroupIndicatorCorner = GroupIndicatorCorner.TopRight;
        if (!Enum.IsDefined(GroupIndicatorShape)) GroupIndicatorShape = GroupIndicatorShape.Circle;
        FloatingGap = Math.Clamp(FloatingGap, 0, 8);
        DockMarginLeft = Math.Clamp(DockMarginLeft, 0, 8);
        DockMarginTop = Math.Clamp(DockMarginTop, 0, 8);
        DockMarginRight = Math.Clamp(DockMarginRight, 0, 8);
        DockMarginBottom = Math.Clamp(DockMarginBottom, 0, 8);
        if (!Enum.IsDefined(IndicatorAccent)) IndicatorAccent = IndicatorAccentMode.Focused;
        if (!Enum.IsDefined(Theme)) Theme = DockTheme.System;
        MagnifiedSize = Math.Clamp(MagnifiedSize, IconSize, 112);
        HoverMagnificationPercent = Math.Clamp(HoverMagnificationPercent, 0, 100);
        IndicatorPosition = Math.Clamp(IndicatorPosition, 0, 100);
        MagnificationSpan = Math.Clamp(MagnificationSpan, 1, 9);
        MagnificationMs = Math.Clamp(MagnificationMs, 0, 1000);
        AnimationMs = Math.Clamp(AnimationMs, 0, 1000);
        RunningClickAnimationMs = Math.Clamp(RunningClickAnimationMs == 280 ? 667 : RunningClickAnimationMs, 50, 2000);
        if (!Enum.IsDefined(AttentionAnimation)) AttentionAnimation = AttentionAnimation.HighBounce;
        LaunchBounceMs = Math.Clamp(LaunchBounceMs, 100, 2000);
        HideDelayMs = Math.Clamp(HideDelayMs, 0, 5000);
        ManualOverlayHideDelayMs = Math.Clamp(ManualOverlayHideDelayMs, 0, 5000);
        RevealDelayMs = Math.Clamp(RevealDelayMs, 0, 2000);
        if (!Enum.IsDefined(HideMode)) HideMode = HideMode.Maximized;
        if (!Enum.IsDefined(ManualRevealBehavior)) ManualRevealBehavior = ManualRevealBehavior.TemporaryOverlay;
        if (HotkeyModifiers == 0 || (HotkeyModifiers & ~7u) != 0) HotkeyModifiers = 3;
        if (!DockHotkey.IsValidKey(HotkeyKey)) HotkeyKey = 0x44;
        FullscreenExclusions = (FullscreenExclusions ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().Trim('"')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Items ??= [];
        RememberedItemProperties = new(RememberedItemProperties ?? [], StringComparer.OrdinalIgnoreCase);
        DefaultClickActions ??= new(); DefaultClickActions.Validate();
        TaskClickOverrides ??= [];
        foreach (var rule in TaskClickOverrides) { rule.Actions ??= new(); rule.Actions.Validate(); }
        for (int index = 0; index < Items.Count; index++) Items[index] = DockApplicationIdentity.NormalizePin(Items[index]);
        var ids = new HashSet<string>();
        foreach (var item in Items.Concat(RememberedItemProperties.Values)) ModuleIconDefaults.Upgrade(item);
        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)) { item.Id = Guid.NewGuid().ToString("N"); ids.Add(item.Id); }
            if (!Enum.IsDefined(item.EdgeFavoritesCommands)) item.EdgeFavoritesCommands = AdditionalCommandsVisibility.Submenu;
            item.Folder ??= new();
            item.Snipping ??= new(); item.Snipping.PinLimit = Math.Clamp(item.Snipping.PinLimit, 1, 9);
            if (!Enum.IsDefined(item.CapturePin)) item.CapturePin = CapturePinKind.None;
            item.RecycleBinSoundVolumePercent = Math.Clamp(item.RecycleBinSoundVolumePercent, 0, 600);
            item.Folder.GridRows = item.Folder.GridRows <= 0 ? 5 : Math.Clamp(item.Folder.GridRows, 1, 7);
            item.Folder.DownloadPinLimit = Math.Clamp(item.Folder.DownloadPinLimit, 1, 9);
            item.Folder.GridColumns = item.Folder.GridColumns <= 0 ? 5 : Math.Clamp(item.Folder.GridColumns, 1, 7);
            item.ClickActions?.Validate();
        }
    }
    private static string ValidateColor(string? value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit) ? value.ToUpperInvariant() : "";
}

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Intersects(PixelRect other) => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public sealed record TaskWindow(nint Handle, string Title, string ExecutablePath, string AppUserModelId, PixelRect Bounds, bool Minimized, bool Maximized);
public sealed record DockEntry(DockItem Item, TaskWindow? Window, bool IsPinned, IReadOnlyList<TaskWindow>? Group = null)
{
    public IReadOnlyList<TaskWindow> Windows => Group ?? (Window == null ? Array.Empty<TaskWindow>() : new[] { Window! });
}

public static class TaskReconciler
{
    public static bool Matches(DockItem pin, TaskWindow window)
    {
        if (pin.Kind != ItemKind.Application) return false;
        if (!string.IsNullOrEmpty(pin.AppUserModelId) && !string.IsNullOrEmpty(window.AppUserModelId))
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(pin.AppUserModelId, window.AppUserModelId)) return true;
            // Packaged apps can expose a different window entry point while
            // keeping the same package family (for example Windows Terminal).
            // Require the executable to agree as well, so browser/PWA windows
            // with unrelated explicit identities still remain separate.
            var separator = pin.AppUserModelId.IndexOf('!');
            var windowSeparator = window.AppUserModelId.IndexOf('!');
            var pinExecutable = string.IsNullOrEmpty(pin.ExecutablePath) ? pin.Target : pin.ExecutablePath;
            if (separator > 0 && windowSeparator > 0
                && StringComparer.OrdinalIgnoreCase.Equals(pin.AppUserModelId[..separator], window.AppUserModelId[..windowSeparator])
                && StringComparer.OrdinalIgnoreCase.Equals(pinExecutable.Replace('/', '\\'), window.ExecutablePath.Replace('/', '\\'))) return true;
            return false;
        }
        // A browser app shortcut must never consume an arbitrary browser window.
        if (pin.Arguments.Contains("--app", StringComparison.OrdinalIgnoreCase)) return false;
        var path = string.IsNullOrEmpty(pin.ExecutablePath) ? pin.Target : pin.ExecutablePath;
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(window.ExecutablePath)) return false;
        return StringComparer.OrdinalIgnoreCase.Equals(path.Replace('/', '\\'), window.ExecutablePath.Replace('/', '\\'));
    }

    public static List<DockEntry> Reconcile(IReadOnlyList<DockItem> pins, IReadOnlyList<TaskWindow> windows, bool groupWindows = false, bool combinePinnedIconsWithRunningApplications = true, bool minimizedOnly = false)
    {
        var remaining = windows.Where(w => !minimizedOnly || w.Minimized || pins.Any(p => Matches(p, w))).ToList();
        var entries = new List<DockEntry>();
        foreach (var pin in pins)
        {
            var match = remaining.FirstOrDefault(w => Matches(pin, w));
            var group = groupWindows ? remaining.Where(w => Matches(pin, w)).ToList() : null;
            entries.Add(new(pin, combinePinnedIconsWithRunningApplications ? match : null, true, combinePinnedIconsWithRunningApplications ? group : null));
            if (combinePinnedIconsWithRunningApplications)
            {
                if (group != null) remaining.RemoveAll(w => group.Contains(w));
                else if (match != null) remaining.Remove(match);
            }
        }
        while (remaining.Count > 0)
        {
            var w = remaining[0];
            var item = new DockItem
            {
                Id = $"window-{w.Handle}", Kind = ItemKind.Application, Name = w.Title,
                Target = w.ExecutablePath, ExecutablePath = w.ExecutablePath, AppUserModelId = w.AppUserModelId
            };
            item = DockApplicationIdentity.NormalizePin(item);
            var group = groupWindows ? remaining.Where(other => other.Handle == w.Handle || Matches(item, other)).ToList() : new List<TaskWindow> { w };
            entries.Add(new(item, w, false, group));
            remaining.RemoveAll(other => group.Contains(other));
        }
        return entries;
    }
}

public static class VisibilityPolicy
{
    public static bool ShouldHide(HideMode mode, PixelRect dock, TaskWindow? active, bool interacting, bool fullScreen, PixelRect? monitorBounds = null, bool fullscreenExcluded = false)
    {
        if (interacting) return false;
        if (fullScreen) return !fullscreenExcluded;
        if (mode == HideMode.AfterDelay) return true;
        if (active == null || active.Minimized) return false;
        if (monitorBounds.HasValue && !active.Bounds.Intersects(monitorBounds.Value)) return false;
        return mode switch
        {
            HideMode.ActiveWindowOverlap => dock.Intersects(active.Bounds),
            HideMode.Maximized => active.Maximized && active.Bounds.Left < dock.Right && active.Bounds.Right > dock.Left,
            _ => false
        };
    }

    public static bool Reserve(DockSettings settings, bool manuallyHidden = false, HideMode? hideMode = null) => !manuallyHidden && settings.ReserveSpace && (hideMode ?? settings.HideMode) is HideMode.AlwaysVisible or HideMode.Maximized;
}

public static class DockOrdering
{
    // The insertion slot refers to the original list, before removing the source.
    public static bool Move(DockSettings settings, string itemId, int insertionSlot, bool overrideLock = false)
    {
        if (settings.LockIconPositions && !overrideLock) return false;
        var from = settings.Items.FindIndex(item => item.Id == itemId);
        if (from < 0) return false;
        var to = Math.Clamp(insertionSlot, 0, settings.Items.Count);
        if (from < to) to--;
        if (from == to) return false;
        var item = settings.Items[from];
        settings.Items.RemoveAt(from);
        settings.Items.Insert(to, item);
        return true;
    }
}

public static class DockGeometry
{
    public static PixelRect PlaceEdge(PixelRect area, int length, int thickness, DockEdge edge, int gap = 0, DockAlignment alignment = DockAlignment.Center)
    {
        int extent = edge is DockEdge.Bottom or DockEdge.Top ? area.Width : area.Height;
        length = Math.Min(length, extent);
        int start = alignment switch { DockAlignment.Start => edge is DockEdge.Bottom or DockEdge.Top ? area.Left : area.Top, DockAlignment.End => (edge is DockEdge.Bottom or DockEdge.Top ? area.Right : area.Bottom) - length, _ => (edge is DockEdge.Bottom or DockEdge.Top ? area.Left : area.Top) + (extent - length) / 2 };
        if (edge == DockEdge.Top) return new(start, area.Top + gap, start + length, area.Top + gap + thickness);
        if (edge == DockEdge.Bottom) return new(start, area.Bottom - gap - thickness, start + length, area.Bottom - gap);
        return edge == DockEdge.Left ? new(area.Left + gap, start, area.Left + gap + thickness, start + length) : new(area.Right - gap - thickness, start, area.Right - gap, start + length);
    }
    public static PixelRect Slide(PixelRect rect, DockEdge edge, double fraction, int gap = 0, bool keepEdgeTrigger = true)
    {
        int trigger = keepEdgeTrigger ? 2 : 1;
        int offset = (int)Math.Round(((edge is DockEdge.Bottom or DockEdge.Top ? rect.Height : rect.Width) - trigger + gap) * Math.Clamp(fraction, 0, 1));
        return edge switch { DockEdge.Top => new(rect.Left, rect.Top - offset, rect.Right, rect.Bottom - offset), DockEdge.Left => new(rect.Left - offset, rect.Top, rect.Right - offset, rect.Bottom), DockEdge.Right => new(rect.Left + offset, rect.Top, rect.Right + offset, rect.Bottom), _ => new(rect.Left, rect.Top + offset, rect.Right, rect.Bottom + offset) };
    }
    public static PixelRect Hidden(PixelRect rect, DockEdge edge, int gap = 0, bool keepEdgeTrigger = true)
    {
        int trigger = keepEdgeTrigger ? 2 : 1;
        return edge switch { DockEdge.Top => new(rect.Left, rect.Top - gap, rect.Right, rect.Top - gap + trigger), DockEdge.Left => new(rect.Left - gap, rect.Top, rect.Left - gap + trigger, rect.Bottom), DockEdge.Right => new(rect.Right + gap - trigger, rect.Top, rect.Right + gap, rect.Bottom), _ => new(rect.Left, rect.Bottom + gap - trigger, rect.Right, rect.Bottom + gap) };
    }
    public static PixelRect ExpandEdgeTrigger(PixelRect rect, DockEdge edge, PixelRect screen) => edge is DockEdge.Top or DockEdge.Bottom
        ? new(screen.Left, rect.Top, screen.Right, rect.Bottom)
        : new(rect.Left, screen.Top, rect.Right, screen.Bottom);
    public static bool AtEdge(PixelRect rect, DockEdge edge, int x, int y, int gap = 0, bool keepEdgeTrigger = true) => keepEdgeTrigger && (edge switch
    {
        DockEdge.Top => y >= rect.Top - gap - 2 && y < rect.Top - gap + 2 && x >= rect.Left && x < rect.Right,
        DockEdge.Left => x >= rect.Left - gap - 2 && x < rect.Left - gap + 2 && y >= rect.Top && y < rect.Bottom,
        DockEdge.Right => x >= rect.Right + gap - 2 && x < rect.Right + gap + 2 && y >= rect.Top && y < rect.Bottom,
        _ => y >= rect.Bottom + gap - 2 && y < rect.Bottom + gap + 2 && x >= rect.Left && x < rect.Right
    });
    public static int Pixels(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);
    public static PixelRect Place(PixelRect area, int width, int height) => new(area.Left + (area.Width - Math.Min(width, area.Width)) / 2, area.Bottom - height, area.Left + (area.Width + Math.Min(width, area.Width)) / 2, area.Bottom);
    public static double Magnification(double distanceInIcons, int span) => Math.Pow(Math.Max(0, 1 - Math.Abs(distanceInIcons) / ((span + 1) / 2d)), 2);
    // The icon's baseline is eight DIPs above the bottom edge; all enlargement
    // must fit in the remaining space without changing the window or hit targets.
    public static double HoverSize(int iconSize, int magnifiedSize, double strength) => iconSize + (Math.Min(magnifiedSize, iconSize + 8) - iconSize) * Math.Clamp(strength, 0, 1);
    // Magnification is allowed to grow outside the dock background. A value
    // of 100 doubles the icon, matching the familiar macOS/Nexus behavior.
    public static double HoverSizePercent(double iconSize, int percent, double strength) => iconSize * (1 + Math.Clamp(percent, 0, 100) / 100d * Math.Clamp(strength, 0, 1));
}

public static class IconRaster
{
    public const uint NativeIconFlags = 0x1 | 0x4; // BIGGERSIZEOK | ICONONLY, never SCALEUP.
    public static int Pixels(double logicalSize, double scale) => Math.Clamp((int)Math.Ceiling(logicalSize * (double.IsFinite(scale) && scale > 0 ? scale : 1)), 16, 512);
    public static int DockPixels(int size, int magnifiedSize, double scale) => Pixels(DockGeometry.HoverSize(size, magnifiedSize, 1), scale);
    public static int ShellPixels(int requested, bool thumbnail) => thumbnail ? requested : requested <= 256 ? 256 : 512;
    public static int FrameIndex(IReadOnlyList<int> sizes, int requested)
    {
        if (sizes.Count == 0) throw new ArgumentException("An icon must have a frame.", nameof(sizes));
        var sufficient = Enumerable.Range(0, sizes.Count).Where(i => sizes[i] >= requested).OrderBy(i => sizes[i]).FirstOrDefault(-1);
        return sufficient >= 0 ? sufficient : Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i]).First();
    }
}

public static class DockHotkey
{
    public static bool IsValidKey(int key) => key == 0x20 || key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87;
    public static string KeyName(int key) => key == 0x20 ? "Space" : key >= 0x70 ? "F" + (key - 0x6F) : ((char)key).ToString();
    public static string Label(uint modifiers, int key) => string.Join("+", new[] { (modifiers & 2) != 0 ? "Ctrl" : "", (modifiers & 1) != 0 ? "Alt" : "", (modifiers & 4) != 0 ? "Shift" : "", KeyName(key) }.Where(x => x.Length > 0));
}
