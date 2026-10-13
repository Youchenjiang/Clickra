namespace Clickra.UI.Layout;

/// <summary>Native progress-window paint and interaction geometry in logical pixels.</summary>
public static class ProgressLayout
{
    public static LayoutRect TrayButton => new(456, 36, 28, 28);
    public const int MessageLeft = 36;
    public const int MessageMaxWidth = 448;
    public const int ScrollbarDrawY = 152;
    public const int ScrollbarHitTop = 148;
    public const int ScrollbarHitBottom = 158;

    public static float MessageAvailableWidth(float percentageWidth) =>
        percentageWidth > 0f ? MessageMaxWidth - percentageWidth - 10f : MessageMaxWidth;

    public static bool ScrollbarHit(int mouseX, int mouseY, float availableWidth) =>
        mouseX >= MessageLeft && mouseX <= MessageLeft + availableWidth &&
        mouseY >= ScrollbarHitTop && mouseY <= ScrollbarHitBottom;

    // The legacy click handler includes the right and bottom border pixel.
    public static bool TrayButtonHit(int x, int y) =>
        x >= TrayButton.X && x <= TrayButton.Right &&
        y >= TrayButton.Y && y <= TrayButton.Bottom;
}
