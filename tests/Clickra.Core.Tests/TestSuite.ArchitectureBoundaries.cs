using System.Text.RegularExpressions;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const int ArchitectureViolationBaselineCeiling = 29;
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
                ["src/Clickra.CLI/Cli/ClickraCli.cs"] = 14,
                ["src/Clickra.CLI/Progress/ProgressWindow.Process.cs"] = 5,
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

    private static string NormalizeRepoPath(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string FormatArchitectureBaseline(IReadOnlyDictionary<string, int> entries) =>
        string.Join(
            ", ",
            entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value}"));
}
