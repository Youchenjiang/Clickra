using System.Text.RegularExpressions;
using Clickra.Core.Application;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const int ArchitectureViolationBaselineCeiling = 9;
    private const string RepositoryRootMissingMessage = "Could not locate the repository root.";
    private const string ArchitectureCliProjectDirectory = "Clickra.CLI";
    private const string ArchitectureCoreProjectDirectory = "Clickra.Core";
    private const string ArchitectureFluentProjectDirectory = "Clickra.Fluent";
    private const string ArchitectureProgressDirectory = "Progress";
    private const string ArchitectureProgressProcessFile = "ProgressWindow.Process.cs";
    private const string ArchitectureCliSourceFile = "ClickraCli.cs";
    private const string GenericCatalogResolution = "ConversionUseCases.GetRequired(command)";
    private static readonly TimeSpan ArchitectureRegexTimeout = TimeSpan.FromSeconds(1);

    private sealed record ArchitectureViolationRule(
        string Name,
        Regex Pattern,
        IReadOnlyDictionary<string, int> Baseline);

    private static readonly ArchitectureViolationRule[] ArchitectureViolationRules =
    {
        new(
            "direct FileProcessor execution",
            new Regex(@"\bFileProcessor\.", RegexOptions.Compiled, ArchitectureRegexTimeout),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/Clickra.CLI/Progress/ProgressWindow.VisualSplitter.cs"] = 1,
                ["src/Clickra.Fluent/Controls/VisualSplitterControl.xaml.cs"] = 1
            }),
        new(
            "direct task start",
            new Regex(@"\bClickraStorage\.StartTask\(", RegexOptions.Compiled, ArchitectureRegexTimeout),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
        new(
            "direct task completion",
            new Regex(@"\bClickraStorage\.CompleteTask\(", RegexOptions.Compiled, ArchitectureRegexTimeout),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
        new(
            "direct output planning",
            new Regex(@"\bConvertCommandRegistry\.EstimateOutputs\(", RegexOptions.Compiled, ArchitectureRegexTimeout),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/Clickra.CLI/Progress/ProgressWindow.Process.cs"] = 2,
                ["src/Clickra.Fluent/MainPage.xaml.cs"] = 1,
                ["src/Clickra.Fluent/TaskProgressPage.xaml.cs"] = 1
            }),
        new(
            "command execution switch",
            new Regex(@"\bswitch\s*\(\s*(?:cmd|command)\s*\)", RegexOptions.Compiled, ArchitectureRegexTimeout),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/Clickra.CLI/Cli/ClickraCli.cs"] = 3
            })
    };

    public static void RegisterArchitectureBoundaryTests(TestRunner runner)
    {
        runner.RunGuard(
            "Architecture boundaries: UI/application violations stay on a shrinking baseline",
            TestArchitectureViolationBaseline);
        runner.RunGuard(
            "Architecture boundaries: baseline invariants stay explicit",
            TestArchitectureViolationBaselineInvariants);
        runner.RunGuard(
            "Architecture boundaries: application contracts stay UI-independent",
            TestApplicationContractsStayUiIndependent);
        runner.RunGuard(
            "Architecture boundaries: conversion commands have one registry owner",
            TestConversionUseCaseRegistryRejectsDuplicateOwners);
        runner.RunGuard(
            "Architecture boundaries: product use case catalog owns migrated commands",
            TestProductUseCaseCatalogOwnsMigratedCommands);
        runner.RunGuard(
            "Architecture boundaries: migrated decrypt workflow has one execution owner",
            TestDecryptWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated split workflow has one execution owner",
            TestSplitWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated compress workflow has one execution owner",
            TestCompressWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated img2pdf workflow has one execution owner",
            TestImg2PdfWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated merge workflow has one execution owner",
            TestMergeWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated image merge workflow has one execution owner",
            TestImgMergeWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated image stitch workflow has one execution owner",
            TestImgStitchWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated image compression workflow has one execution owner",
            TestImgCompressWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated image format workflows have one execution owner",
            TestImageFormatWorkflowsHaveSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated PDF translation workflow has one execution owner",
            TestTranslatePdfWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated Word conversion workflow has one execution owner",
            TestWordToPdfWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated PowerPoint conversion workflow has one execution owner",
            TestPptToPdfWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated Excel conversion workflow has one execution owner",
            TestExcelToPdfWorkflowHasSingleExecutionOwner);
        runner.RunGuard(
            "Architecture boundaries: migrated Markdown PDF workflow has one execution owner",
            TestMarkdownToPdfWorkflowHasSingleExecutionOwner);
    }

    private static void TestArchitectureViolationBaseline()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string[] surfaceRoots =
        {
            Path.Combine(root, "src", ArchitectureCliProjectDirectory),
            Path.Combine(root, "src", ArchitectureFluentProjectDirectory)
        };
        string[] sources = surfaceRoots
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (ArchitectureViolationRule rule in ArchitectureViolationRules)
        {
            var actual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in sources)
            {
                string code = File.ReadAllText(file);
                int count = rule.Pattern.Matches(code).Count;
                if (count == 0) continue;
                actual[NormalizeRepoPath(root, file)] = count;
            }

            string expectedText = FormatArchitectureBaseline(rule.Baseline);
            string actualText = FormatArchitectureBaseline(actual);
            Assert.True(
                string.Equals(expectedText, actualText, StringComparison.Ordinal),
                $"Architecture violation baseline changed for {rule.Name}. The allowlist may only shrink; " +
                $"new UI/application policy sites are forbidden.{Environment.NewLine}" +
                $"  baseline: {expectedText}{Environment.NewLine}" +
                $"  actual:   {actualText}");
        }
    }

    private static void TestArchitectureViolationBaselineInvariants()
    {
        int baselineCount = ArchitectureViolationRules.Sum(rule => rule.Baseline.Values.Sum());
        Assert.Equal(ArchitectureViolationBaselineCeiling, baselineCount);

        Assert.True(
            ArchitectureViolationRules
                .SelectMany(rule => rule.Baseline.Keys.Select(path => (rule.Name, Path: path)))
                .All(entry => entry.Path.StartsWith("src/Clickra.CLI/", StringComparison.Ordinal)
                              || entry.Path.StartsWith("src/Clickra.Fluent/", StringComparison.Ordinal)),
            "The architecture baseline is only for UI/application surface debt, not a general exception list.");
    }

    private static void TestApplicationContractsStayUiIndependent()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string applicationDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, "Application");
        Assert.True(Directory.Exists(applicationDir), "The shared application contract directory must exist.");

        string[] forbidden =
        {
            "Microsoft.UI",
            "Clickra.UI",
            "System.Windows",
            "Windows.Win32",
            "HWND",
            "System.Drawing"
        };
        foreach (string file in Directory.EnumerateFiles(applicationDir, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            foreach (string token in forbidden)
            {
                Assert.False(
                    source.Contains(token, StringComparison.Ordinal),
                    $"{NormalizeRepoPath(root, file)} must not depend on UI/platform token '{token}'.");
            }
        }
    }

    private static void TestConversionUseCaseRegistryRejectsDuplicateOwners()
    {
        var first = new StubConversionUseCase("decrypt-pdf");
        var second = new StubConversionUseCase("split-pdf");
        var registry = new ConversionUseCaseRegistry(new IConversionUseCase[] { first, second });

        Assert.True(registry.TryGet("DECRYPT-PDF", out IConversionUseCase? resolved),
            "Use-case lookup must be case-insensitive like command routing.");
        Assert.True(ReferenceEquals(first, resolved), "The registry must return the single owner registered for a command.");
        Assert.True(ReferenceEquals(second, registry.GetRequired("split-pdf")),
            "GetRequired must return the registered command owner.");
        Assert.Throws<InvalidOperationException>(() =>
            new ConversionUseCaseRegistry(new IConversionUseCase[]
            {
                first,
                new StubConversionUseCase("DECRYPT-PDF")
            }));
    }

    private static void TestProductUseCaseCatalogOwnsMigratedCommands()
    {
        IConversionUseCase compress = ConversionUseCases.GetRequired(CompressPdfUseCase.CommandName);
        IConversionUseCase decrypt = ConversionUseCases.GetRequired(DecryptPdfUseCase.CommandName);
        IConversionUseCase imgCompress = ConversionUseCases.GetRequired(ImgCompressUseCase.CommandName);
        IConversionUseCase imgMerge = ConversionUseCases.GetRequired(ImgMergeUseCase.CommandName);
        IConversionUseCase img2Pdf = ConversionUseCases.GetRequired(Img2PdfUseCase.CommandName);
        IConversionUseCase imgStitch = ConversionUseCases.GetRequired(ImgStitchUseCase.CommandName);
        IConversionUseCase merge = ConversionUseCases.GetRequired(MergePdfUseCase.CommandName);
        IConversionUseCase markdownPdf = ConversionUseCases.GetRequired(MarkdownToPdfUseCase.CommandName);
        IConversionUseCase markdownWord = ConversionUseCases.GetRequired(MarkdownToWordUseCase.CommandName);
        IConversionUseCase split = ConversionUseCases.GetRequired(SplitPdfUseCase.CommandName);
        IConversionUseCase translate = ConversionUseCases.GetRequired(TranslatePdfUseCase.CommandName);
        IConversionUseCase excel = ConversionUseCases.GetRequired(ExcelToPdfUseCase.CommandName);
        IConversionUseCase ppt = ConversionUseCases.GetRequired(PptToPdfUseCase.CommandName);
        IConversionUseCase word = ConversionUseCases.GetRequired(WordToPdfUseCase.CommandName);
        Assert.True(compress is CompressPdfUseCase,
            "compress-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(decrypt is DecryptPdfUseCase,
            "decrypt-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(imgCompress is ImgCompressUseCase,
            "img-compress must resolve through the product-wide application use-case catalog.");
        Assert.True(imgMerge is ImgMergeUseCase,
            "img-merge must resolve through the product-wide application use-case catalog.");
        Assert.True(img2Pdf is Img2PdfUseCase,
            "img2pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(imgStitch is ImgStitchUseCase,
            "img-stitch must resolve through the product-wide application use-case catalog.");
        foreach (string imageFormatCommand in new[]
        {
            ImageFormatConvertUseCase.PngCommand,
            ImageFormatConvertUseCase.JpgCommand,
            ImageFormatConvertUseCase.WebpCommand,
            ImageFormatConvertUseCase.GifCommand,
            ImageFormatConvertUseCase.HeicCommand
        })
        {
            Assert.True(ConversionUseCases.GetRequired(imageFormatCommand) is ImageFormatConvertUseCase,
                $"{imageFormatCommand} must resolve through the product-wide application use-case catalog.");
            Assert.True(ConversionUseCases.Commands.Count(command =>
                    command.Equals(imageFormatCommand, StringComparison.OrdinalIgnoreCase)) == 1,
                $"The product catalog must expose exactly one {imageFormatCommand} owner.");
        }
        Assert.True(merge is MergePdfUseCase,
            "merge-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(markdownPdf is MarkdownToPdfUseCase,
            "md2pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(markdownWord is MarkdownToWordUseCase,
            "md2word must resolve through the product-wide application use-case catalog.");
        Assert.True(split is SplitPdfUseCase,
            "split-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(translate is TranslatePdfUseCase,
            "translate-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(excel is ExcelToPdfUseCase,
            "excel2pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(ppt is PptToPdfUseCase,
            "ppt2pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(word is WordToPdfUseCase,
            "word2pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(CompressPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one compress-pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(DecryptPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one decrypt-pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(ImgCompressUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one img-compress owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(ImgMergeUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one img-merge owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(Img2PdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one img2pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(ImgStitchUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one img-stitch owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(MergePdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one merge-pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(MarkdownToPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one md2pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(MarkdownToWordUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one md2word owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(SplitPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one split-pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(TranslatePdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one translate-pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(ExcelToPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one excel2pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(PptToPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one ppt2pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(WordToPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one word2pdf owner.");
    }

    private static void TestDecryptWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            DecryptPdfUseCase.CommandName,
            "_decrypted.pdf",
            "FileProcessor.DecryptPdf",
            nameof(DecryptPdfUseCase),
            "decrypt");

    private static void TestSplitWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            SplitPdfUseCase.CommandName,
            "_split.pdf",
            "FileProcessor.SplitPdf",
            nameof(SplitPdfUseCase),
            "split");

    private static void TestCompressWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            CompressPdfUseCase.CommandName,
            "_compressed.pdf",
            "FileProcessor.CompressPdf",
            nameof(CompressPdfUseCase),
            "compression");

    private static void AssertMigratedWorkflowHasSingleExecutionOwner(
        string command,
        string legacyOutputSuffix,
        string processorCall,
        string? useCaseTypeName,
        string workflowName)
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string runner = File.ReadAllText(Path.Combine(root, "src", ArchitectureCoreProjectDirectory, "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", ArchitectureCoreProjectDirectory, "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureProgressDirectory, ArchitectureProgressProcessFile));
        string quiet = File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, "Cli", ArchitectureCliSourceFile));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", ArchitectureFluentProjectDirectory, "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", ArchitectureFluentProjectDirectory, "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains($"case \"{command}\"", StringComparison.Ordinal),
            $"Legacy ConvertCommandRunner must not retain a {workflowName} execution branch after migration.");
        Assert.False(registry.Contains(legacyOutputSuffix, StringComparison.Ordinal),
            $"Legacy ConvertCommandRegistry must not retain {workflowName} output-path policy after application migration.");
        Assert.False(native.Contains(processorCall, StringComparison.Ordinal),
            $"Native presentation must not execute the {workflowName} processor directly.");
        Assert.False(quiet.Contains(processorCall, StringComparison.Ordinal),
            $"Headless CLI must not execute the {workflowName} processor directly.");
        string? specificCatalogResolution = useCaseTypeName is null
            ? null
            : $"ConversionUseCases.GetRequired({useCaseTypeName}.CommandName)";
        bool nativeCatalogResolution =
            native.Contains(GenericCatalogResolution, StringComparison.Ordinal)
            || (specificCatalogResolution is not null
                && native.Contains(specificCatalogResolution, StringComparison.Ordinal));
        bool quietCatalogResolution =
            quiet.Contains(GenericCatalogResolution, StringComparison.Ordinal)
            || (specificCatalogResolution is not null
                && quiet.Contains(specificCatalogResolution, StringComparison.Ordinal));
        Assert.True((fluentMain.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                     || fluentMain.Contains(GenericCatalogResolution, StringComparison.Ordinal))
                    && (fluentTask.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                        || fluentTask.Contains(GenericCatalogResolution, StringComparison.Ordinal))
                    && nativeCatalogResolution
                    && quietCatalogResolution,
            $"All product surfaces must resolve {workflowName} execution through the application use-case catalog.");
    }

    private static void TestImg2PdfWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            Img2PdfUseCase.CommandName,
            "\"img2pdf\" => files.Select",
            "FileProcessor.ConvertImagesToPdf(new List<string> { f }",
            nameof(Img2PdfUseCase),
            "img2pdf");

    private static void TestMergeWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            MergePdfUseCase.CommandName,
            "Merged_PDF.pdf",
            "FileProcessor.MergePdfs",
            nameof(MergePdfUseCase),
            "merge");

    private static void TestImgMergeWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            ImgMergeUseCase.CommandName,
            "Merged_Images.pdf",
            "FileProcessor.ConvertImagesToPdf(files",
            nameof(ImgMergeUseCase),
            "img-merge");

    private static void TestImgStitchWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            ImgStitchUseCase.CommandName,
            "Stitched_Image.png",
            "FileProcessor.StitchImages",
            nameof(ImgStitchUseCase),
            "img-stitch");

    private static void TestImgCompressWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            ImgCompressUseCase.CommandName,
            "EstimateImageCompressionOutputs",
            "FileProcessor.CompressImage",
            nameof(ImgCompressUseCase),
            "img-compress");

    private static void TestImageFormatWorkflowsHaveSingleExecutionOwner()
    {
        foreach (string command in new[]
        {
            ImageFormatConvertUseCase.PngCommand,
            ImageFormatConvertUseCase.JpgCommand,
            ImageFormatConvertUseCase.WebpCommand,
            ImageFormatConvertUseCase.GifCommand,
            ImageFormatConvertUseCase.HeicCommand
        })
        {
            AssertMigratedWorkflowHasSingleExecutionOwner(
                command,
                "EstimateImageFormatOutputs",
                "FileProcessor.ConvertImageFormat",
                null,
                command);
        }
    }

    private static void TestTranslatePdfWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains("case \"translate-pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain translate-pdf execution after application migration.");
        Assert.False(registry.Contains("Path.GetFileNameWithoutExtension(f) + \"_translated.pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must not retain translate-pdf output planning after application migration.");
        Assert.False(native.Contains("FileProcessor.TranslatePdf", StringComparison.Ordinal),
            "Native presentation must not execute PDF translation directly.");
        Assert.False(quiet.Contains("FileProcessor.TranslatePdf", StringComparison.Ordinal),
            "Headless CLI must not execute PDF translation directly.");
        Assert.True((fluentMain.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                     || fluentMain.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && (fluentTask.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                        || fluentTask.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && native.Contains("RunApplicationTranslate(hwnd, currentFiles, progressCallback)", StringComparison.Ordinal)
                    && native.Contains("ConversionUseCases.GetRequired(TranslatePdfUseCase.CommandName)", StringComparison.Ordinal)
                    && quiet.Contains("ConversionUseCases.GetRequired(TranslatePdfUseCase.CommandName)", StringComparison.Ordinal),
            "All product surfaces must resolve translate-pdf through the application use-case catalog.");
    }

    private static void TestWordToPdfWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains("case \"word2pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain word2pdf execution after application migration.");
        Assert.True(registry.Contains("word2pdf output planning is owned by the application use case.", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must fail closed instead of planning word2pdf outputs after migration.");
        Assert.False(native.Contains("FileProcessor.ConvertWordToPdf", StringComparison.Ordinal),
            "Native presentation must not execute Word conversion directly.");
        Assert.False(quiet.Contains("FileProcessor.ConvertWordToPdf", StringComparison.Ordinal),
            "Headless CLI must not execute Word conversion directly.");
        Assert.True((fluentMain.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                     || fluentMain.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && (fluentTask.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                        || fluentTask.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && native.Contains("RunApplicationWordToPdf(hwnd, currentFiles, progressCallback)", StringComparison.Ordinal)
                    && native.Contains("ConversionUseCases.GetRequired(WordToPdfUseCase.CommandName)", StringComparison.Ordinal)
                    && quiet.Contains("ConversionUseCases.GetRequired(WordToPdfUseCase.CommandName)", StringComparison.Ordinal),
            "All product surfaces must resolve word2pdf through the application use-case catalog.");
    }

    private static void TestPptToPdfWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains("case \"ppt2pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain ppt2pdf execution after application migration.");
        Assert.True(registry.Contains("ppt2pdf output planning is owned by the application use case.", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must fail closed instead of planning ppt2pdf outputs after migration.");
        Assert.False(native.Contains("FileProcessor.ConvertPptToPdf", StringComparison.Ordinal),
            "Native presentation must not execute PowerPoint conversion directly.");
        Assert.False(quiet.Contains("FileProcessor.ConvertPptToPdf", StringComparison.Ordinal),
            "Headless CLI must not execute PowerPoint conversion directly.");
        Assert.True((fluentMain.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                     || fluentMain.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && (fluentTask.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                        || fluentTask.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && native.Contains("RunApplicationPptToPdf(hwnd, currentFiles, progressCallback)", StringComparison.Ordinal)
                    && native.Contains("ConversionUseCases.GetRequired(PptToPdfUseCase.CommandName)", StringComparison.Ordinal)
                    && quiet.Contains("ConversionUseCases.GetRequired(PptToPdfUseCase.CommandName)", StringComparison.Ordinal),
            "All product surfaces must resolve ppt2pdf through the application use-case catalog.");
    }

    private static void TestExcelToPdfWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains("case \"excel2pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain excel2pdf execution after application migration.");
        Assert.True(registry.Contains("excel2pdf output planning is owned by the application use case.", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must fail closed instead of planning excel2pdf outputs after migration.");
        Assert.False(native.Contains("FileProcessor.ConvertExcelToPdf", StringComparison.Ordinal),
            "Native presentation must not execute Excel conversion directly.");
        Assert.False(quiet.Contains("FileProcessor.ConvertExcelToPdf", StringComparison.Ordinal),
            "Headless CLI must not execute Excel conversion directly.");
        Assert.True((fluentMain.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                     || fluentMain.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && (fluentTask.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                        || fluentTask.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && native.Contains("RunApplicationExcelToPdf(hwnd, currentFiles, progressCallback)", StringComparison.Ordinal)
                    && native.Contains("ConversionUseCases.GetRequired(ExcelToPdfUseCase.CommandName)", StringComparison.Ordinal)
                    && quiet.Contains("ConversionUseCases.GetRequired(ExcelToPdfUseCase.CommandName)", StringComparison.Ordinal),
            "All product surfaces must resolve excel2pdf through the application use-case catalog.");
    }

    private static void TestMarkdownToPdfWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains("case \"md2pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain md2pdf execution after application migration.");
        Assert.False(runner.Contains("case \"md2word\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain md2word execution after application migration.");
        Assert.True(registry.Contains("md2pdf output planning is owned by the application use case.", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must fail closed instead of planning md2pdf outputs after migration.");
        Assert.True(registry.Contains("CmdMdToWord => files.Select", StringComparison.Ordinal),
            "The independent md2word legacy planner must remain available until its own migration.");
        Assert.False(native.Contains("FileProcessor.ConvertMarkdownToPdf", StringComparison.Ordinal),
            "Native presentation must not execute Markdown PDF conversion directly.");
        Assert.False(quiet.Contains("FileProcessor.ConvertMarkdownToPdf", StringComparison.Ordinal),
            "Headless CLI must not execute Markdown PDF conversion directly.");
        Assert.True(native.Contains("RunApplicationMarkdownToPdf(hwnd, currentFiles, progressCallback)", StringComparison.Ordinal)
                    && native.Contains("ConversionUseCases.GetRequired(MarkdownToPdfUseCase.CommandName)", StringComparison.Ordinal)
                    && native.Contains("_commandOptions", StringComparison.Ordinal)
                    && native.Contains("ResumeStartIndex = _startIndex", StringComparison.Ordinal),
            "Native md2pdf must route its option snapshot and resume index through the application owner.");
        Assert.True(quiet.Contains("ConversionUseCases.GetRequired(MarkdownToPdfUseCase.CommandName)", StringComparison.Ordinal)
                    && quiet.Contains("MarkdownPdfOptions.Create()", StringComparison.Ordinal)
                    && quiet.Contains("TrackTaskLifecycle: false", StringComparison.Ordinal),
            "Headless md2pdf must use default Markdown options without task lifecycle ownership.");
        Assert.True(fluentMain.Contains("commandOptions));", StringComparison.Ordinal)
                    && fluentTask.Contains("commandOptions,", StringComparison.Ordinal)
                    && fluentTask.Contains("ExistingTaskId: existingTaskId", StringComparison.Ordinal),
            "Both Fluent md2pdf paths must forward the already-prompted Markdown option snapshot into planning.");
    }

    private sealed class StubConversionUseCase(string command) : IConversionUseCase
    {
        public string Command { get; } = command;

        public ConversionValidationResult Validate(ConversionRequest request) =>
            ConversionValidationResult.Success();

        public ConversionPlan Plan(ConversionRequest request) =>
            throw new NotSupportedException();

        public Task<ConversionResult> ExecuteAsync(
            ConversionPlan plan,
            IConversionInteraction interaction,
            IProgress<ConversionProgress>? progress,
            IConversionExecutionObserver? observer = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static string NormalizeRepoPath(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string FormatArchitectureBaseline(IReadOnlyDictionary<string, int> entries) =>
        string.Join(
            ", ",
            entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value}"));
}
