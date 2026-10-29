namespace WinMac.Dock.Core;

/// <summary>A continuous lens calculated in resting coordinates, never from animated centers.</summary>
public sealed class MagnificationWave
{
    private readonly double[] starts, widths, targets;
    private readonly bool[] icons;
    public double[] Growth { get; }
    public double Length { get; }
    public int Count => widths.Length;
    public double TotalGrowth => Growth.Sum();

    public MagnificationWave(double[] widths, bool[] icons, double spacing)
    {
        if (widths.Length != icons.Length || widths.Any(w => !double.IsFinite(w) || w <= 0))
            throw new ArgumentException("Each slot must have a positive width and an icon flag.");
        this.widths = (double[])widths.Clone(); this.icons = (bool[])icons.Clone();
        starts = new double[widths.Length]; targets = new double[widths.Length]; Growth = new double[widths.Length];
        double length = 0;
        for (int i = 0; i < Count; i++) { starts[i] = length; length += widths[i] + (i + 1 < Count ? (spacing < 0 && !(icons[i] && icons[i + 1]) ? 0 : spacing) : 0); }
        Length = length;
    }

    public static double Weight(double distanceInSlots, int span)
    {
        double t = Math.Abs(distanceInSlots) / ((Math.Clamp(span, 1, 9) + 1) / 2d);
        return t >= 1 ? 0 : (1 + Math.Cos(Math.PI * t)) / 2;
    }

    public void Target(double pointer, double pitch, double peakGrowth, int span, double anchor, double availableExpansion, bool fillExpansion = false)
    {
        availableExpansion = Math.Max(0, availableExpansion);
        ConstrainGrowth(availableExpansion, peakGrowth);
        int firstIcon = Array.FindIndex(icons, icon => icon), lastIcon = Array.FindLastIndex(icons, icon => icon);
        double fillGoal = fillExpansion ? Math.Min(availableExpansion, Math.Max(0, peakGrowth) * icons.Count(icon => icon)) : 0;
        double Map(double focus)
        {
            if (fillGoal > 0 && firstIcon >= 0)
                focus = Math.Clamp(focus, starts[firstIcon] + widths[firstIcon] / 2, starts[lastIcon] + widths[lastIcon] / 2);
            double radius = (Math.Clamp(span, 1, 9) + 1) / 2d;
            double WeightAt(int i, double extent)
            {
                double distance = Math.Abs((focus - starts[i] - widths[i] / 2) / pitch);
                return distance >= extent ? 0 : (1 + Math.Cos(Math.PI * distance / extent)) / 2;
            }
            double Sum(double extent)
            {
                double sum = 0;
                for (int i = 0; i < Count; i++) if (icons[i]) sum += peakGrowth * WeightAt(i, extent);
                return sum;
            }
            // A crowded row uses its measured remaining width with real icon
            // growth. Broaden the lens at the ends instead of stretching an
            // empty background or increasing the configured peak icon size.
            if (fillGoal > 0 && Sum(radius) < fillGoal - .000001)
            {
                double narrow = radius, wide = Math.Max(radius, Count);
                for (int i = 0; i < 12 && Sum(wide) < fillGoal - .000001; i++) wide *= 2;
                for (int i = 0; i < 22; i++)
                {
                    double mid = (narrow + wide) / 2;
                    if (Sum(mid) < fillGoal) narrow = mid; else wide = mid;
                }
                radius = wide;
            }
            double before = 0, total = 0;
            for (int i = 0; i < Count; i++)
            {
                targets[i] = icons[i] ? peakGrowth * WeightAt(i, radius) : 0;
                total += targets[i];
            }
            if (total > availableExpansion)
            {
                double factor = availableExpansion / total;
                for (int i = 0; i < Count; i++) targets[i] *= factor;
                total = availableExpansion;
            }
            for (int i = 0; i < Count; i++) before += targets[i] * Math.Clamp((focus - starts[i]) / widths[i], 0, 1);
            return focus + before - anchor * total;
        }
        // Invert the target lens. Feeding the previous frame's animated
        // positions back into this calculation would create a hover oscillation.
        double low = Math.Min(0, pointer) - peakGrowth * Count;
        double high = Math.Max(Length, pointer) + peakGrowth * Count;
        for (int step = 0; step < 32; step++)
        {
            double mid = (low + high) / 2;
            if (Map(mid) < pointer) low = mid; else high = mid;
        }
        Map((low + high) / 2);
    }

    public void ClearTarget() => Array.Clear(targets);
    public void RestoreGrowth(double[] values, double availableExpansion = double.PositiveInfinity, double maxGrowth = double.PositiveInfinity)
    {
        if (values.Length != Count) throw new ArgumentException("One growth value is required per slot.");
        for (int i = 0; i < Count; i++) Growth[i] = targets[i] = icons[i] && double.IsFinite(values[i]) ? Math.Max(0, values[i]) : 0;
        ConstrainGrowth(availableExpansion, maxGrowth);
    }
    public void ConstrainGrowth(double availableExpansion, double maxGrowth)
    {
        void Limit(double[] values)
        {
            double total = 0;
            for (int i = 0; i < Count; i++) { values[i] = icons[i] ? Math.Clamp(values[i], 0, Math.Max(0, maxGrowth)) : 0; total += values[i]; }
            if (total > Math.Max(0, availableExpansion))
            {
                double factor = Math.Max(0, availableExpansion) / total;
                for (int i = 0; i < Count; i++) values[i] *= factor;
            }
        }
        Limit(Growth); Limit(targets);
    }
    public bool Advance(double elapsedMs, int transitionMs)
    {
        double blend = transitionMs <= 0 ? 1 : 1 - Math.Exp(-Math.Max(0, elapsedMs) * 5 / transitionMs);
        bool moving = false;
        for (int i = 0; i < Count; i++)
        {
            Growth[i] += (targets[i] - Growth[i]) * blend;
            if (Math.Abs(Growth[i] - targets[i]) < .001) Growth[i] = targets[i]; else moving = true;
        }
        return moving;
    }

    public double Offset(int index, double anchor, double availableExpansion)
    {
        double before = 0;
        for (int i = 0; i < index; i++) before += Growth[i];
        return before + Growth[index] / 2 - anchor * Math.Min(TotalGrowth, availableExpansion);
    }
}
