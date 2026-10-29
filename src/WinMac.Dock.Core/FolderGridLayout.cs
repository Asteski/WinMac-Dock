namespace WinMac.Dock.Core;

public static class FolderGridLayout
{
    public static int VisibleRows(int rows, int requestedRows, double availableHeight, double reservedHeight, double cellHeight = 100) =>
        Math.Min(Math.Max(1, rows), Math.Min(Math.Clamp(requestedRows, 1, 7),
            Math.Max(1, (int)Math.Floor((availableHeight - reservedHeight) / cellHeight))));

    public static int LeadingEmptyCells(int count, int columns, bool bottomAligned) =>
        bottomAligned && count > 0 && columns > 1 ? (columns - count % columns) % columns : 0;
}
