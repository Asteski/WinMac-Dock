using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private readonly HashSet<string> appearedDownloadIndicators = [];
    private void AnimateDownloadIndicator(Border indicator, string itemId)
    {
        if (indicator.Opacity > 0) return;
        if (!appearedDownloadIndicators.Add(itemId) || !Motion) { indicator.Opacity = 1; return; }
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(indicator);
        void Center() => visual.CenterPoint = new System.Numerics.Vector3((float)indicator.ActualWidth / 2, (float)indicator.ActualHeight / 2, 0);
        Center();
        indicator.SizeChanged += (_, _) => Center();
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, new System.Numerics.Vector3(0, 1, 1));
        animation.InsertKeyFrame(1, System.Numerics.Vector3.One,
            visual.Compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(.16f, 1), new System.Numerics.Vector2(.3f, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Scale", animation);
        indicator.Opacity = 1;
    }
    private sealed record IndicatorTransition(double FromWidth, double FromHeight, double Width, double Height, long Started);
    private readonly Dictionary<Border, IndicatorTransition> indicatorTransitions = [];
    private const double IndicatorTransitionMs = 160;
    private void SetIndicatorSize(Border indicator, double width, double height)
    {
        if (!Motion)
        {
            indicatorTransitions.Remove(indicator);
            indicator.Width = width; indicator.Height = height; PositionIndicator(indicator);
            if (indicatorTransitions.Count == 0) CompositionTarget.Rendering -= IndicatorRendering;
            return;
        }
        if (indicatorTransitions.TryGetValue(indicator, out var existing) && existing.Width == width && existing.Height == height) return;
        if (indicator.Width == width && indicator.Height == height)
        {
            indicatorTransitions.Remove(indicator);
            if (indicatorTransitions.Count == 0) CompositionTarget.Rendering -= IndicatorRendering;
            return;
        }
        if (indicatorTransitions.Count == 0) CompositionTarget.Rendering += IndicatorRendering;
        indicatorTransitions[indicator] = new(indicator.Width, indicator.Height, width, height, Stopwatch.GetTimestamp());
    }
    private void IndicatorRendering(object? sender, object args)
    {
        foreach (var (indicator, transition) in indicatorTransitions.ToArray())
        {
            double t = Motion ? Math.Clamp(Stopwatch.GetElapsedTime(transition.Started).TotalMilliseconds / IndicatorTransitionMs, 0, 1) : 1;
            double blend = 1 - Math.Pow(1 - t, 3);
            indicator.Width = transition.FromWidth + (transition.Width - transition.FromWidth) * blend;
            indicator.Height = transition.FromHeight + (transition.Height - transition.FromHeight) * blend;
            PositionIndicator(indicator);
            if (t == 1) indicatorTransitions.Remove(indicator);
        }
        if (indicatorTransitions.Count == 0) CompositionTarget.Rendering -= IndicatorRendering;
    }
    private void StopIndicatorAnimations()
    {
        CompositionTarget.Rendering -= IndicatorRendering;
        foreach (var (indicator, transition) in indicatorTransitions)
        {
            indicator.Width = transition.Width; indicator.Height = transition.Height; PositionIndicator(indicator);
        }
        indicatorTransitions.Clear();
    }
    private async Task CheckIndicatorSizeTransition(Border indicator)
    {
        StopIndicatorAnimations();
        bool rotated = Vertical && !Settings.ForceIndicatorsToItemBase;
        double activeLength = Settings.WindowsTaskbarHighlights ? 14 : 16;
        indicator.Width = rotated ? 3 : 6; indicator.Height = rotated ? 6 : 3; PositionIndicator(indicator);
        SetIndicatorSize(indicator, rotated ? 3 : activeLength, rotated ? activeLength : 3);
        SetIndicatorSize(indicator, rotated ? 3 : 6, rotated ? 6 : 3);
        if (indicatorTransitions.ContainsKey(indicator)) throw new InvalidOperationException("Immediate reversal did not cancel indicator animation.");
        SetIndicatorSize(indicator, rotated ? 3 : activeLength, rotated ? activeLength : 3);
        double Length() => rotated ? indicator.Height : indicator.Width;
        if (Length() != 6) throw new InvalidOperationException("Indicator size animation jumped to its target.");
        await Task.Delay(45);
        double intermediate = Length();
        if (intermediate <= 6 || intermediate >= activeLength) throw new InvalidOperationException("Indicator size did not interpolate.");
        root.UpdateLayout();
        var slot = hoverSlots.FirstOrDefault(s => s.Indicator == indicator);
        if (slot?.IndicatorCarrier != null && (Math.Abs(slot.IndicatorCarrier.ActualWidth - indicator.Width) > 1 || Math.Abs(slot.IndicatorCarrier.ActualHeight - indicator.Height) > 1))
            throw new InvalidOperationException("Animated hover indicator was clipped by its carrier.");
        SetIndicatorSize(indicator, rotated ? 3 : 6, rotated ? 6 : 3);
        if (Length() != intermediate) throw new InvalidOperationException("Reversing indicator animation caused a jump.");
        await Task.Delay(200);
        if (Length() != 6 || indicatorTransitions.ContainsKey(indicator)) throw new InvalidOperationException("Indicator size animation did not settle.");
        bool reduced = Settings.ReduceMotion;
        try
        {
            Settings.ReduceMotion = true;
            SetIndicatorSize(indicator, rotated ? 3 : activeLength, rotated ? activeLength : 3);
            if (Length() != activeLength || indicatorTransitions.ContainsKey(indicator)) throw new InvalidOperationException("Reduce motion did not disable indicator animation.");
        }
        finally { Settings.ReduceMotion = reduced; }
    }
}
