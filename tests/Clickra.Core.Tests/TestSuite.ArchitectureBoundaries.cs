using System.Text.RegularExpressions;
using Clickra.Core.Application;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const int ArchitectureViolationBaselineCeiling = 19;
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
                ["src/Clickra.CLI/Cli/ClickraCli.cs"] = 8,
                ["src/Clickra.CLI/Progress/ProgressWindow.Process.cs"] = 1,
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
                ["src/Clickra.CLI/Cli/ClickraCli.cs"] = 3,
                ["src/Clickra.CLI/Progress/ProgressWindow.Process.cs"] = 1
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
    }

    private static void TestArchitectureViolationBaseline()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string[] surfaceRoots =
        {
            Path.Combine(root, "src", "Clickra.CLI"),
            Path.Combine(root, "src", "Clickra.Fluent")
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
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string applicationDir = Path.Combine(root, "src", "Clickra.Core", "Application");
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
        IConversionUseCase img2Pdf = ConversionUseCases.GetRequired(Img2PdfUseCase.CommandName);
        IConversionUseCase merge = ConversionUseCases.GetRequired(MergePdfUseCase.CommandName);
        IConversionUseCase split = ConversionUseCases.GetRequired(SplitPdfUseCase.CommandName);
        Assert.True(compress is CompressPdfUseCase,
            "compress-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(decrypt is DecryptPdfUseCase,
            "decrypt-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(img2Pdf is Img2PdfUseCase,
            "img2pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(merge is MergePdfUseCase,
            "merge-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(split is SplitPdfUseCase,
            "split-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(CompressPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one compress-pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(DecryptPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one decrypt-pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(Img2PdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one img2pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(MergePdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one merge-pdf owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(SplitPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one split-pdf owner.");
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
        string useCaseTypeName,
        string workflowName)
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains($"case \"{command}\"", StringComparison.Ordinal),
            $"Legacy ConvertCommandRunner must not retain a {workflowName} execution branch after migration.");
        Assert.False(registry.Contains(legacyOutputSuffix, StringComparison.Ordinal),
            $"Legacy ConvertCommandRegistry must not retain {workflowName} output-path policy after application migration.");
        Assert.False(native.Contains(processorCall, StringComparison.Ordinal),
            $"Native presentation must not execute the {workflowName} processor directly.");
        Assert.False(quiet.Contains(processorCall, StringComparison.Ordinal),
            $"Headless CLI must not execute the {workflowName} processor directly.");
        string catalogResolution = $"ConversionUseCases.GetRequired({useCaseTypeName}.CommandName)";
        Assert.True((fluentMain.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                     || fluentMain.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && (fluentTask.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                        || fluentTask.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && native.Contains(catalogResolution, StringComparison.Ordinal)
                    && quiet.Contains(catalogResolution, StringComparison.Ordinal),
            $"All product surfaces must resolve {workflowName} execution through the application use-case catalog.");
    }

    private static void TestImg2PdfWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains("case \"img2pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain an img2pdf execution branch after migration.");
        Assert.False(registry.Contains("\"img2pdf\" => files.Select", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must not retain img2pdf output-path policy after application migration.");
        Assert.False(native.Contains("FileProcessor.ConvertImagesToPdf(new List<string> { f }", StringComparison.Ordinal),
            "Native presentation must not retain the old single-image img2pdf processor call.");
        Assert.False(quiet.Contains("FileProcessor.ConvertImagesToPdf(new List<string> { f }", StringComparison.Ordinal),
            "Headless CLI must not retain the old single-image img2pdf processor call.");
        Assert.True((fluentMain.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                     || fluentMain.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && (fluentTask.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                        || fluentTask.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && native.Contains("ConversionUseCases.GetRequired(Img2PdfUseCase.CommandName)", StringComparison.Ordinal)
                    && quiet.Contains("ConversionUseCases.GetRequired(Img2PdfUseCase.CommandName)", StringComparison.Ordinal),
            "All product surfaces must resolve img2pdf execution through the application use-case catalog.");
    }

    private static void TestMergeWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains("case \"merge-pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain a merge execution branch after migration.");
        Assert.False(registry.Contains("Merged_PDF.pdf", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must not retain merge output-path policy after application migration.");
        Assert.False(native.Contains("FileProcessor.MergePdfs", StringComparison.Ordinal),
            "Native presentation must not execute the merge processor directly.");
        Assert.False(quiet.Contains("FileProcessor.MergePdfs", StringComparison.Ordinal),
            "Headless CLI must not execute the merge processor directly.");
        Assert.True((fluentMain.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                     || fluentMain.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && (fluentTask.Contains("ConversionUseCases.TryGet(command", StringComparison.Ordinal)
                        || fluentTask.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal))
                    && native.Contains("ConversionUseCases.GetRequired(MergePdfUseCase.CommandName)", StringComparison.Ordinal)
                    && quiet.Contains("ConversionUseCases.GetRequired(MergePdfUseCase.CommandName)", StringComparison.Ordinal),
            "All product surfaces must resolve merge execution through the application use-case catalog.");
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
