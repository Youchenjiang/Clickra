using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clickra.Core.Processors;

public sealed class PdfTranslationHealthReport
{
    public string InputPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public int SourcePages { get; init; }
    public int OutputPages { get; init; }
    public int TranslatedParagraphs { get; init; }
    public int BypassedParagraphs { get; init; }
    public int RenderEntries { get; init; }
    public int GuardClipEntries { get; init; }
    public int OverflowEntries { get; init; }
    public int HeadingCount { get; init; }
    public double MinimumHeadingFontRatio { get; init; } = 1.0;
    public double MaximumAlignmentAnchorShift { get; init; }
    public int ShiftedParagraphCount { get; init; }
    public int FixedRegionCollisionCount { get; init; }
    public int BottomOverflowCount { get; init; }
    public string LayoutFailureReason { get; init; } = string.Empty;
    public IReadOnlyList<string> TranslationFailures { get; init; } = Array.Empty<string>();
    public bool Succeeded { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool HasLayoutDefects => GuardClipEntries > 0 || OverflowEntries > 0;

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString(nameof(InputPath), InputPath);
        writer.WriteString(nameof(OutputPath), OutputPath);
        writer.WriteString(nameof(Provider), Provider);
        writer.WriteNumber(nameof(SourcePages), SourcePages);
        writer.WriteNumber(nameof(OutputPages), OutputPages);
        writer.WriteNumber(nameof(TranslatedParagraphs), TranslatedParagraphs);
        writer.WriteNumber(nameof(BypassedParagraphs), BypassedParagraphs);
        writer.WriteNumber(nameof(RenderEntries), RenderEntries);
        writer.WriteNumber(nameof(GuardClipEntries), GuardClipEntries);
        writer.WriteNumber(nameof(OverflowEntries), OverflowEntries);
        writer.WriteNumber(nameof(HeadingCount), HeadingCount);
        writer.WriteNumber(nameof(MinimumHeadingFontRatio), MinimumHeadingFontRatio);
        writer.WriteNumber(nameof(MaximumAlignmentAnchorShift), MaximumAlignmentAnchorShift);
        writer.WriteNumber(nameof(ShiftedParagraphCount), ShiftedParagraphCount);
        writer.WriteNumber(nameof(FixedRegionCollisionCount), FixedRegionCollisionCount);
        writer.WriteNumber(nameof(BottomOverflowCount), BottomOverflowCount);
        writer.WriteString(nameof(LayoutFailureReason), LayoutFailureReason);
        writer.WriteStartArray(nameof(TranslationFailures));
        foreach (string failure in TranslationFailures)
            writer.WriteStringValue(failure);
        writer.WriteEndArray();
        writer.WriteBoolean(nameof(Succeeded), Succeeded);
        writer.WriteString(nameof(CompletedAtUtc), CompletedAtUtc);
        writer.WriteEndObject();
        writer.Flush();
    }
}
