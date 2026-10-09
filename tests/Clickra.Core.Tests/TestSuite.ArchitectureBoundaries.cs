using System.Text.RegularExpressions;
using Clickra.Core.Application;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const int ArchitectureViolationBaselineCeiling = 27;
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
                ["src/Clickra.CLI/Cli/ClickraCli.cs"] = 13,
                ["src/Clickra.CLI/Progress/ProgressWindow.Process.cs"] = 4,
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
        IConversionUseCase decrypt = ConversionUseCases.GetRequired(DecryptPdfUseCase.CommandName);
        Assert.True(decrypt is DecryptPdfUseCase,
            "decrypt-pdf must resolve through the product-wide application use-case catalog.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(DecryptPdfUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one decrypt-pdf owner.");
    }

    private static void TestDecryptWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root.");

        string runner = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRunner.cs"));
        string native = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
        string quiet = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Cli", "ClickraCli.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));

        Assert.False(runner.Contains("case \"decrypt-pdf\"", StringComparison.Ordinal),
            "Legacy ConvertCommandRunner must not retain a decrypt execution branch after migration.");
        string registry = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "ConvertCommandRegistry.cs"));
        Assert.False(registry.Contains("_decrypted.pdf", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must not retain decrypt output-path policy after application migration.");
        Assert.False(native.Contains("FileProcessor.DecryptPdf", StringComparison.Ordinal),
            "Native presentation must not execute the decrypt processor directly.");
        Assert.False(quiet.Contains("FileProcessor.DecryptPdf", StringComparison.Ordinal),
            "Headless CLI must not execute the decrypt processor directly.");
        Assert.True(fluentMain.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal)
                    && fluentTask.Contains("ConversionUseCases.GetRequired(command)", StringComparison.Ordinal)
                    && native.Contains("ConversionUseCases.GetRequired(DecryptPdfUseCase.CommandName)", StringComparison.Ordinal)
                    && quiet.Contains("ConversionUseCases.GetRequired(DecryptPdfUseCase.CommandName)", StringComparison.Ordinal),
            "All product surfaces must resolve decrypt execution through the application use-case catalog.");
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
