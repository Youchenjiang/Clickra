using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Desktop.Controls;

namespace Clickra.Desktop.ViewModels;

/// <summary>
/// One Markdown template card (spec 4.9, canvas MarkdownB / MarkdownWordB / MarkdownWordFailB): a page
/// on top, the name below. A built-in theme shows its generated preview (scripts/tools/TemplatePreviewGen)
/// or, until that PNG exists, a sketch of the page. The last card is 「Word 範本」: a dashed 「＋ .docx」
/// box until a file is chosen, then a sketch drawn from what was read (title alignment, line spacing,
/// margins) with a W mark, named after the file; a file that cannot be used shows a warning box.
/// </summary>
public sealed class TemplateCardViewModel : ViewModelBase
{
    private const string WordCardName = "Word 範本";
    private static readonly Color Title = Color.Parse("#1F1F1F");
    private static readonly Color Body = Color.Parse("#8A8A8A");

    private string _name;
    private IReadOnlyList<SketchLine>? _sketch;
    private Thickness _sketchPadding;
    private bool _hasWordFile;
    private bool _isBroken;

    public TemplateCardViewModel(string name, string? theme)
    {
        _name = name;
        IsWord = theme is null;
        if (theme is null) return;

        (_sketch, _sketchPadding) = ThemeSketch(theme);
        try
        {
            var uri = new Uri($"avares://Clickra.Desktop/Assets/TemplatePreviews/{theme}.png");
            if (AssetLoader.Exists(uri)) Preview = new Bitmap(AssetLoader.Open(uri));
        }
        catch
        {
            Preview = null;
        }
    }

    public string Name { get => _name; private set { _name = value; OnPropertyChanged(); } }
    public IImage? Preview { get; }
    public bool HasPreview => Preview is not null;
    public bool IsWord { get; }

    public IReadOnlyList<SketchLine>? Sketch => _sketch;
    public Thickness SketchPadding => _sketchPadding;

    /// <summary>The paper with a sketch: built-in themes without a preview, and a usable Word template.</summary>
    public bool ShowSketch => !HasPreview && (!IsWord || (_hasWordFile && !_isBroken));
    public bool ShowAddDocx => IsWord && !_hasWordFile;
    public bool ShowWordMark => IsWord && _hasWordFile && !_isBroken;
    public bool ShowBroken => IsWord && _hasWordFile && _isBroken;
    public bool ShowPaper => HasPreview || ShowSketch;

    /// <summary>
    /// Called when a Word template is chosen (<paramref name="fileName"/> null = none). A usable one gives
    /// its title alignment, line spacing (multiple of the text size) and margins in centimetres.
    /// </summary>
    internal void SetWordTemplate(string? fileName, bool usable, bool centerTitle = false, double lineMultiple = 1.15, double marginCm = 2.54)
    {
        _hasWordFile = fileName is not null;
        _isBroken = _hasWordFile && !usable;
        Name = fileName is null ? WordCardName : System.IO.Path.GetFileNameWithoutExtension(fileName);
        if (_hasWordFile && usable)
        {
            double gap = Math.Clamp(Math.Round(2 + (lineMultiple - 1) * 4), 2, 6);
            double side = Math.Clamp(Math.Round(4 + marginCm * 3), 6, 18);
            _sketch =
            [
                new SketchLine(0.56, 5, 0, Title, centerTitle),
                new SketchLine(1.00, 2, 8, Body),
                new SketchLine(1.00, 2, gap, Body),
                new SketchLine(0.92, 2, gap, Body),
                new SketchLine(1.00, 2, gap, Body),
                new SketchLine(0.70, 2, gap, Body),
            ];
            _sketchPadding = new Thickness(side, 9, side, 0);
        }
        else
        {
            _sketch = null;
        }
        OnPropertyChanged(nameof(Sketch));
        OnPropertyChanged(nameof(SketchPadding));
        OnPropertyChanged(nameof(ShowSketch));
        OnPropertyChanged(nameof(ShowAddDocx));
        OnPropertyChanged(nameof(ShowWordMark));
        OnPropertyChanged(nameof(ShowBroken));
        OnPropertyChanged(nameof(ShowPaper));
    }

    /// <summary>The three built-in themes as drawn on the canvas: link colour, line weight and title placement differ.</summary>
    private static (IReadOnlyList<SketchLine>, Thickness) ThemeSketch(string theme)
    {
        switch (theme)
        {
            case MarkdownPdfOptions.ThemeMinimal:
            {
                Color title = Color.Parse("#555555"), body = Color.Parse("#B5B5B5");
                return (new SketchLine[]
                {
                    new SketchLine(0.48, 4, 0, title),
                    new SketchLine(1.00, 2, 9, body),
                    new SketchLine(0.90, 2, 4, body),
                    new SketchLine(0.94, 2, 4, body),
                    new SketchLine(1.00, 2, 4, body),
                    new SketchLine(0.70, 2, 4, body),
                }, new Thickness(18, 12, 18, 0));
            }
            case MarkdownPdfOptions.ThemeAcademic:
                return (new SketchLine[]
                {
                    new SketchLine(0.70, 5, 0, Title, true),
                    new SketchLine(0.40, 2, 4, Color.Parse("#777777"), true),
                    new SketchLine(1.00, 2, 8, Body),
                    new SketchLine(1.00, 2, 3, Body),
                    new SketchLine(1.00, 2, 3, Body),
                    new SketchLine(1.00, 2, 3, Body),
                    new SketchLine(1.00, 2, 3, Body),
                    new SketchLine(0.60, 2, 3, Body),
                }, new Thickness(12, 8, 12, 0));
            default:
            {
                Color title = Color.Parse("#2B2B2B"), body = Color.Parse("#9A9A9A");
                return (new SketchLine[]
                {
                    new SketchLine(0.62, 6, 0, title),
                    new SketchLine(1.00, 3, 7, body),
                    new SketchLine(0.92, 3, 3, body),
                    new SketchLine(0.96, 3, 3, body),
                    new SketchLine(0.40, 4, 7, Color.Parse("#3D6FB4")),
                    new SketchLine(1.00, 3, 3, body),
                    new SketchLine(0.84, 3, 3, body),
                }, new Thickness(10, 8, 10, 0));
            }
        }
    }
}
