namespace WinMac.Dock.Core;

public static class IndicatorGeometry
{
    public static int PillLength(IndicatorPillSize size, bool focused, double iconSize = 40) => !focused ? 6 : size switch
    {
        IndicatorPillSize.Small => 6,
        IndicatorPillSize.Large => DefaultAutomaticLength(iconSize),
        _ => 14
    };

    public static int DefaultAutomaticLength(double iconSize) => (int)System.Math.Round(System.Math.Max(16, iconSize * .6));

    public static int AutomaticLength(ItemIndicatorShape? shape, ItemIndicatorShape itemShape, IndicatorPillSize size, double iconSize = 40)
    {
        if (shape == ItemIndicatorShape.Default) return DefaultAutomaticLength(iconSize);
        return (shape ?? itemShape) == ItemIndicatorShape.Pill ? PillLength(size, true, iconSize) : 4;
    }
}
