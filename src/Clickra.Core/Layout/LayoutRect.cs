namespace Clickra.Core.Layout;

/// <summary>
/// 版面表回傳的一個整數矩形。繪製與命中測試取用同一個值，所以兩邊不會各自
/// 再算一次而漂移 —— dashboard 的按鈕與卡片位置都由此型別傳遞。
/// </summary>
public readonly struct LayoutRect
{
    public LayoutRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => X + Width;
    public int Bottom => Y + Height;

    /// <summary>命中判定；右緣與下緣不含，與 Win32 的矩形慣例一致。</summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}
