using System.Drawing.Imaging;
using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Core.Modules.Pdf.Split.Application;
using Clickra.Core.Presentation.Convert;

namespace Clickra.TemplatePreviewGen;

/// <summary>
/// Converts one sample Markdown file with each built-in template and saves the first page as a small
/// PNG for the template cards on the desktop convert page. Re-run whenever a template changes.
/// </summary>
public static class Program
{
    private const int PreviewWidth = 240;

    private const string Sample = """
        # 期末報告

        這是一份範例文件，用來比較不同範本的字型、行距與標題樣式。Clickra 會把 Markdown 轉成排好版的文件。

        ## 研究方法

        - 蒐集資料並整理成表格
        - 比較三種做法的差異
        - 寫下結論與建議

        ## 程式碼

        ```csharp
        var result = Convert("report.md");
        Console.WriteLine(result);
        ```

        > 範本只影響外觀，內容不會改變。
        """;

    public static async Task<int> Main()
    {
        string root = FindRepoRoot() ?? throw new InvalidOperationException("Run inside the Clickra repository.");
        string outputDir = Path.Combine(root, "src", "Clickra.Desktop", "Assets", "TemplatePreviews");
        Directory.CreateDirectory(outputDir);

        // Keep the user's own settings and history untouched.
        string work = Path.Combine(Path.GetTempPath(), "clickra-template-previews-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        Environment.SetEnvironmentVariable("CLICKRA_DATA_DIR", Path.Combine(work, "data"));

        try
        {
            foreach (string theme in new[] { MarkdownPdfOptions.ThemeDefault, MarkdownPdfOptions.ThemeMinimal, MarkdownPdfOptions.ThemeAcademic })
            {
                string dir = Path.Combine(work, theme);
                Directory.CreateDirectory(dir);
                string markdown = Path.Combine(dir, "sample.md");
                await File.WriteAllTextAsync(markdown, Sample);

                var controller = new ConversionExecutionController();
                var plan = controller.Prepare("md2pdf", [markdown], new Dictionary<string, object>
                {
                    [MarkdownPdfOptions.ThemeKey] = theme,
                    [MarkdownPdfOptions.PaperKey] = MarkdownPdfOptions.PaperA4,
                });
                var result = await controller.ExecutePreparedAsync();
                if (result.Status != ConversionExecutionPresentationStatus.Succeeded)
                {
                    Console.Error.WriteLine($"[{theme}] conversion failed: {result.Error}");
                    return 1;
                }

                string pdf = plan.Outputs[0];
                using var page = PdfPageThumbnailQuery.RenderPageFromFile(pdf, 1, PreviewWidth);
                if (page is null)
                {
                    Console.Error.WriteLine($"[{theme}] could not render {pdf}");
                    return 1;
                }
                string png = Path.Combine(outputDir, theme + ".png");
                page.Save(png, ImageFormat.Png);
                Console.WriteLine($"[{theme}] {png}");
            }
            return 0;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp folder */ }
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.targets"))) dir = dir.Parent;
        return dir?.FullName;
    }
}
