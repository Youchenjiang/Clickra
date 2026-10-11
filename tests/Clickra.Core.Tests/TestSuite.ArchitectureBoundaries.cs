using System.Text.RegularExpressions;
using Clickra.Core.Application;
using Clickra.Shared;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const int ArchitectureViolationBaselineCeiling = 0;
    private const int ProcessorGlobalStateBaselineCeiling = 0;
    private const string RepositoryRootMissingMessage = "Could not locate the repository root.";
    private const string ArchitectureCliProjectDirectory = "Clickra.CLI";
    private const string ArchitectureCoreProjectDirectory = "Clickra.Core";
    private const string ArchitectureApplicationDirectory = "Application";
    private const string ArchitectureFluentProjectDirectory = "Clickra.Fluent";
    private const string ArchitectureProgressDirectory = "Progress";
    private const string ArchitectureProcessorsDirectory = "Processors";
    private const string ArchitectureProgressProcessFile = "ProgressWindow.Process.cs";
    private const string ArchitectureNativeExecutionAdapterFile = "NativeConversionExecutionAdapter.cs";
    private const string ArchitectureCliSourceFile = "ClickraCli.cs";
    private const string GenericCatalogResolution = "ConversionUseCases.GetRequired(command)";
    private static readonly TimeSpan ArchitectureRegexTimeout = TimeSpan.FromSeconds(1);

    private sealed record ArchitectureViolationRule(
        string Name,
        Regex Pattern,
        IReadOnlyDictionary<string, int> Baseline);

    private sealed record ProcessorGlobalStateRule(
        string Name,
        Regex Pattern,
        IReadOnlyDictionary<string, int> Baseline);

    private static IReadOnlyDictionary<string, int> EmptyArchitectureBaseline() =>
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static ArchitectureViolationRule ArchitectureRule(string name, string pattern) =>
        new(name, new Regex(pattern, RegexOptions.Compiled, ArchitectureRegexTimeout), EmptyArchitectureBaseline());

    private static ProcessorGlobalStateRule ProcessorGlobalStateRuleFor(string name, string pattern) =>
        new(name, new Regex(pattern, RegexOptions.Compiled, ArchitectureRegexTimeout), EmptyArchitectureBaseline());

    private sealed record MigrationSurfaceSources(
        string Registry,
        string Native,
        string NativeAdapter,
        string Quiet,
        string FluentMain,
        string FluentTask);

    private static readonly ArchitectureViolationRule[] ArchitectureViolationRules =
    {
        ArchitectureRule("direct FileProcessor execution", @"\bFileProcessor\."),
        ArchitectureRule("direct task start", @"\bClickraStorage\.StartTask\("),
        ArchitectureRule("direct task completion", @"\bClickraStorage\.CompleteTask\("),
        ArchitectureRule("direct output planning", @"\bConvertCommandRegistry\.EstimateOutputs\(")
    };

    private static readonly ProcessorGlobalStateRule[] ProcessorGlobalStateRules =
    {
        ProcessorGlobalStateRuleFor("ClickraStorage.GetSetting", @"\bClickraStorage\.GetSetting\("),
        ProcessorGlobalStateRuleFor("ClickraStorage.GetSettingBool", @"\bClickraStorage\.GetSettingBool\("),
        ProcessorGlobalStateRuleFor("ClickraStorage.GetSettingInt", @"\bClickraStorage\.GetSettingInt\("),
        ProcessorGlobalStateRuleFor("ClickraStorage.SaveSetting", @"\bClickraStorage\.SaveSetting\(")
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
            "Architecture boundaries: processor global setting access stays on a shrinking baseline",
            TestProcessorGlobalStateBaseline);
        runner.RunGuard(
            "Architecture boundaries: processor global setting baseline invariants stay explicit",
            TestProcessorGlobalStateBaselineInvariants);
        runner.RunGuard(
            "Architecture boundaries: processors do not depend on ClickraStorage",
            TestProcessorsDoNotDependOnClickraStorage);
        runner.RunGuard(
            "Architecture boundaries: infrastructure settings access stays on an explicit key allowlist",
            TestInfrastructureSettingsAccessStaysOnAllowlist);
        runner.RunGuard(
            "Architecture boundaries: processors localize only with an explicit execution language",
            TestProcessorsLocalizeWithExplicitLanguage);
        runner.RunGuard(
            "Architecture boundaries: application contracts stay UI-independent",
            TestApplicationContractsStayUiIndependent);
        runner.RunGuard(
            "Architecture boundaries: settings persistence has one application gateway",
            TestSettingsPersistenceHasSingleApplicationGateway);
        runner.RunGuard(
            "Architecture boundaries: presentation settings use typed application access",
            TestPresentationSettingsUseTypedApplicationAccess);
        runner.RunGuard(
            "Architecture boundaries: parked task actions have one application gateway",
            TestParkedTaskActionsHaveSingleApplicationGateway);
        runner.RunGuard(
            "Architecture boundaries: presentation does not depend on raw ClickraStorage",
            TestPresentationDoesNotDependOnRawStorage);
        runner.RunGuard(
            "Architecture boundaries: Core bounded contexts declare owned namespaces",
            TestCoreBoundedContextsDeclareOwnedNamespaces);
        runner.RunGuard(
            "Architecture boundaries: storage facade delegates to focused persistence owners",
            TestStorageFacadeDelegatesToFocusedOwners);
        runner.RunGuard(
            "Architecture boundaries: storage does not own localized presentation text",
            TestStorageDoesNotOwnLocalizedPresentationText);
        runner.RunGuard(
            "Architecture boundaries: presentation and platform helpers have explicit ownership",
            TestPresentationAndPlatformHelpersHaveExplicitOwnership);
        runner.RunGuard(
            "Architecture boundaries: translation provider and PDF text contexts stay distinct",
            TestTranslationContextsStayDistinct);
        runner.RunGuard(
            "Architecture boundaries: parked task orchestration has one storage owner",
            TestParkedTaskOrchestrationHasSingleOwner);
        runner.RunGuard(
            "Architecture boundaries: task pruning has one storage owner",
            TestTaskPruningHasSingleOwner);
        runner.RunGuard(
            "Architecture boundaries: Markdown presentation options have one render owner",
            TestMarkdownPresentationOptionsHaveSingleOwner);
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
        runner.RunGuard(
            "Architecture boundaries: migrated Markdown Word workflow has one execution owner",
            TestMarkdownToWordWorkflowHasSingleExecutionOwner);
    }

    private static void TestParkedTaskOrchestrationHasSingleOwner()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string storageDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, "Storage");
        string facade = File.ReadAllText(Path.Combine(storageDir, "ClickraStorage.ActiveRecord.cs"));
        string coordinator = File.ReadAllText(Path.Combine(storageDir, "ParkedTaskCoordinator.cs"));

        foreach ((string publicCall, string delegation) in new[]
        {
            ("public static void ParkTask(", "ParkedTaskCoordinator.Park("),
            ("public static List<HistoryEntry> GetParkedTasks()", "ParkedTaskCoordinator.GetAll("),
            ("public static void CancelParkedTask(", "ParkedTaskCoordinator.Cancel("),
            ("public static ResumedTaskInfo? ClaimParkedTaskForResume(", "ParkedTaskCoordinator.ClaimForResume("),
            ("public static void SetParkedRetentionOverride(", "ParkedTaskCoordinator.SetRetentionOverride("),
            ("public static int? AdjustParkedRetention(", "ParkedTaskCoordinator.AdjustRetention("),
            ("public static ParkedRetentionInfo GetParkedRetentionInfo(", "ParkedTaskCoordinator.GetRetentionInfo(")
        })
        {
            int start = facade.IndexOf(publicCall, StringComparison.Ordinal);
            Assert.True(start >= 0, $"ClickraStorage facade must keep {publicCall}.");
            int end = facade.IndexOf(';', start);
            string body = end > start ? facade[start..(end + 1)] : facade[start..];
            Assert.True(body.Contains(delegation, StringComparison.Ordinal),
                $"{publicCall} must delegate parked-task orchestration to ParkedTaskCoordinator.");
            Assert.False(body.Contains("RunWithMutex", StringComparison.Ordinal),
                $"{publicCall} must stay a facade instead of reclaiming parked-task orchestration.");
        }

        Assert.True(coordinator.Contains("StorageRuntime.RunWithMutex", StringComparison.Ordinal),
            "ParkedTaskCoordinator must own atomic parked-task transitions.");
        Assert.True(coordinator.Contains("TaskRetentionPolicy.CalculateAdjustedDays(", StringComparison.Ordinal),
            "ParkedTaskCoordinator must own retention-policy orchestration.");
        Assert.True(coordinator.Contains("HistoryStore.AppendCanceled(", StringComparison.Ordinal),
            "ParkedTaskCoordinator must own parked-task cancellation bookkeeping.");
        Assert.False(facade.Contains("TaskRetentionPolicy.CalculateAdjustedDays(", StringComparison.Ordinal),
            "ClickraStorage.ActiveRecord must not take parked retention orchestration back from the coordinator.");
    }

    private static void TestTaskPruningHasSingleOwner()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string storageDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, "Storage");
        string facade = File.ReadAllText(Path.Combine(storageDir, "ClickraStorage.ActiveRecord.cs"));
        string queue = File.ReadAllText(Path.Combine(storageDir, "TaskQueueStore.cs"));
        string parked = File.ReadAllText(Path.Combine(storageDir, "ParkedTaskCoordinator.cs"));
        string pruner = File.ReadAllText(Path.Combine(storageDir, "TaskPruningCoordinator.cs"));

        Assert.True(queue.Contains("TaskPruningCoordinator.Prune()", StringComparison.Ordinal),
            "TaskQueueStore active reads must delegate stale-task cleanup to TaskPruningCoordinator.");
        Assert.True(parked.Contains("TaskPruningCoordinator.Prune()", StringComparison.Ordinal),
            "Parked task reads must delegate stale-task cleanup to TaskPruningCoordinator.");

        foreach (string pruningOwner in new[]
        {
            "Process.GetProcessById(",
            "AbandonedTaskTtlHours",
            "TaskRetentionPolicy.IsExpired("
        })
        {
            Assert.True(pruner.Contains(pruningOwner, StringComparison.Ordinal),
                $"TaskPruningCoordinator must own {pruningOwner}.");
            Assert.False(facade.Contains(pruningOwner, StringComparison.Ordinal),
                $"ClickraStorage.ActiveRecord must not reclaim {pruningOwner}.");
            Assert.False(queue.Contains(pruningOwner, StringComparison.Ordinal) && pruningOwner != "TaskRetentionPolicy.IsExpired(",
                $"TaskQueueStore must not reclaim pruning policy {pruningOwner}.");
            Assert.False(parked.Contains(pruningOwner, StringComparison.Ordinal),
                $"ParkedTaskCoordinator must not own {pruningOwner}.");
        }
    }

    private static void TestMarkdownPresentationOptionsHaveSingleOwner()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string processorsDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureProcessorsDirectory);
        string profile = File.ReadAllText(Path.Combine(processorsDir, "MarkdownRenderProfile.cs"));
        string fileProcessingDir = Path.Combine(processorsDir, "FileProcessing");
        string pdf = File.ReadAllText(Path.Combine(fileProcessingDir, "MarkdownToPdfProcessor.cs"));
        string word = File.ReadAllText(Path.Combine(fileProcessingDir, "MarkdownToWordProcessor.cs"));

        foreach (string resolverCall in new[]
        {
            "MarkdownTemplateCatalog.Resolve(",
            "MarkdownPdfOptions.GetTheme(",
            "MarkdownPdfOptions.GetTemplatePath(",
            "MarkdownPdfOptions.GetPaper(",
            "MarkdownPdfOptions.GetTextSize(",
            "MarkdownPdfOptions.GetCodeTheme(",
            "MarkdownResolvedLayout.Create(",
            "MarkdownResolvedPalette.Create("
        })
        {
            Assert.True(profile.Contains(resolverCall, StringComparison.Ordinal),
                $"MarkdownRenderProfile must own {resolverCall}.");
            Assert.False(pdf.Contains(resolverCall, StringComparison.Ordinal),
                $"Markdown PDF backend must consume the shared render profile instead of calling {resolverCall}.");
            Assert.False(word.Contains(resolverCall, StringComparison.Ordinal),
                $"Markdown Word backend must consume the shared render profile instead of calling {resolverCall}.");
        }

        Assert.True(pdf.Contains("MarkdownRenderProfile.Resolve(options)", StringComparison.Ordinal),
            "Markdown PDF conversion must resolve one shared render profile.");
        Assert.True(word.Contains("MarkdownRenderProfile.Resolve(options)", StringComparison.Ordinal),
            "Markdown Word conversion must resolve one shared render profile.");
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

    private static void TestProcessorGlobalStateBaseline()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string processorsDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureProcessorsDirectory);
        string[] sources = Directory.EnumerateFiles(processorsDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (ProcessorGlobalStateRule rule in ProcessorGlobalStateRules)
        {
            var actual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in sources)
            {
                int count = rule.Pattern.Matches(File.ReadAllText(file)).Count;
                if (count == 0) continue;
                actual[NormalizeRepoPath(root, file)] = count;
            }

            string expectedText = FormatArchitectureBaseline(rule.Baseline);
            string actualText = FormatArchitectureBaseline(actual);
            Assert.True(
                string.Equals(expectedText, actualText, StringComparison.Ordinal),
                $"Processor global setting baseline changed for {rule.Name}. Existing entries may only be removed or reduced; " +
                $"new processor-side global setting access is forbidden.{Environment.NewLine}" +
                $"  baseline: {expectedText}{Environment.NewLine}" +
                $"  actual:   {actualText}");
        }
    }

    private static void TestProcessorGlobalStateBaselineInvariants()
    {
        int baselineCount = ProcessorGlobalStateRules.Sum(rule => rule.Baseline.Values.Sum());
        Assert.True(baselineCount <= ProcessorGlobalStateBaselineCeiling,
            $"Processor global setting baseline grew to {baselineCount}; ceiling is {ProcessorGlobalStateBaselineCeiling}.");

        Assert.True(
            ProcessorGlobalStateRules
                .SelectMany(rule => rule.Baseline.Keys)
                .All(path => path.StartsWith("src/Clickra.Core/Processors/", StringComparison.Ordinal)),
            "Processor global setting exceptions must stay inside the processor tree.");

        string[] names = ProcessorGlobalStateRules.Select(rule => rule.Name).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());

        foreach (ProcessorGlobalStateRule rule in ProcessorGlobalStateRules)
        {
            string syntheticCall = $"var value = {rule.Name}(ClickraSettings.Language);";
            Assert.Equal(1, rule.Pattern.Matches(syntheticCall).Count);
        }
    }

    private static void TestProcessorsDoNotDependOnClickraStorage()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string processorsDirectory = Path.Combine(
            root,
            "src",
            ArchitectureCoreProjectDirectory,
            ArchitectureProcessorsDirectory);
        string[] violations = Directory.GetFiles(processorsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("ClickraStorage", StringComparison.Ordinal))
            .Select(path => NormalizeRepoPath(root, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Processors must receive storage-derived state explicitly instead of depending on ClickraStorage:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private sealed record InfrastructureSettingsAllowance(
        IReadOnlySet<string> OwnedKeys,
        IReadOnlySet<string> ReadOnlyKeys);

    // Infrastructure may persist its own lifecycle state (for example, which LibreOffice
    // installation Clickra manages), unlike processors. Every key is listed explicitly so
    // moving a file into Infrastructure/ cannot be used to escape the processor guard.
    private static readonly IReadOnlyDictionary<string, InfrastructureSettingsAllowance> InfrastructureSettingsAllowlist =
        new Dictionary<string, InfrastructureSettingsAllowance>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/Clickra.Core/Infrastructure/LibreOfficeEngineInstaller.cs"] = new(
                OwnedKeys: new HashSet<string>(StringComparer.Ordinal)
                {
                    "LibreOfficeInstalledByClickra",
                    "LibreOfficeManagedProductCode",
                    "LibreOfficeManagedSofficePath"
                },
                ReadOnlyKeys: new HashSet<string>(StringComparer.Ordinal)
                {
                    "LibreOfficeRemovalPendingRestart",
                    "LibreOfficePath"
                })
        };

    private static readonly Regex InfrastructureStorageReference =
        new(@"\bClickraStorage\.", RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static readonly Regex InfrastructureSettingsCall =
        new(@"\bClickraStorage\.(?<op>GetSetting|GetSettingBool|GetSettingInt|SaveSetting)\(\s*ClickraSettings\.(?<key>[A-Za-z0-9_]+)\b",
            RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static void TestInfrastructureSettingsAccessStaysOnAllowlist()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string infrastructureDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, "Infrastructure");
        if (!Directory.Exists(infrastructureDir)) return;

        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(infrastructureDir, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            ValidateInfrastructureSettingsFile(root, file, violations);
        }

        Assert.True(
            violations.Count == 0,
            "Infrastructure settings access must stay on the explicit per-owner key allowlist:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void ValidateInfrastructureSettingsFile(string root, string file, List<string> violations)
    {
        string path = NormalizeRepoPath(root, file);
        string source = File.ReadAllText(file);
        int references = InfrastructureStorageReference.Matches(source).Count;
        if (references == 0) return;

        MatchCollection calls = InfrastructureSettingsCall.Matches(source);
        if (calls.Count != references)
            violations.Add($"{path}: {references - calls.Count} ClickraStorage use(s) are not explicit ClickraSettings reads/writes.");

        if (!InfrastructureSettingsAllowlist.TryGetValue(path, out InfrastructureSettingsAllowance? allowance))
        {
            violations.Add($"{path}: settings access is not allowlisted for this infrastructure owner.");
            return;
        }

        foreach (Match call in calls)
            ValidateInfrastructureSettingsCall(path, call, allowance, violations);
    }

    private static void ValidateInfrastructureSettingsCall(
        string path,
        Match call,
        InfrastructureSettingsAllowance allowance,
        List<string> violations)
    {
        string op = call.Groups["op"].Value;
        string key = call.Groups["key"].Value;
        bool owned = allowance.OwnedKeys.Contains(key);
        bool readable = owned || allowance.ReadOnlyKeys.Contains(key);
        if ((op == "SaveSetting" && !owned) || (op != "SaveSetting" && !readable))
            violations.Add($"{path}: {op}({key}) is outside its allowlist.");
    }

    private static readonly Regex ProcessorLocalizationCall =
        new(@"\bLocalization\.T\(", RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static readonly Regex ExplicitLanguageArgument =
        new(@"^(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?:language|lang|Language)$", RegexOptions.Compiled, ArchitectureRegexTimeout);

    // The global-language overloads Localization.T(key) and Localization.T(key, params object[])
    // read ClickraStorage indirectly, and T(key, "value") silently binds a format argument as a
    // language code. Processors must therefore call only T(key, <language variable>).
    private static void TestProcessorsLocalizeWithExplicitLanguage()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string processorsDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureProcessorsDirectory);
        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(processorsDir, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            string source = File.ReadAllText(file);
            foreach (Match call in ProcessorLocalizationCall.Matches(source))
            {
                IReadOnlyList<string> arguments = ReadTopLevelArguments(source, call.Index + call.Length);
                bool explicitLanguage = arguments.Count == 2
                                        && ExplicitLanguageArgument.IsMatch(arguments[1]);
                if (!explicitLanguage)
                {
                    int line = source.AsSpan(0, call.Index).Count('\n') + 1;
                    violations.Add($"{NormalizeRepoPath(root, file)}:{line}: Localization.T({string.Join(", ", arguments)})");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Processors must localize through ExecutionLocalizationOptions.Text(language, key, args) or " +
            "Localization.T(key, language); global-language overloads are forbidden:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));

        Assert.Equal(2, ReadTopLevelArguments("Localization.T(\"k\", language)", "Localization.T(".Length).Count);
        Assert.Equal(1, ReadTopLevelArguments("Localization.T(\"a,(b)\")", "Localization.T(".Length).Count);
        Assert.Equal(3, ReadTopLevelArguments("Localization.T(\"k\", Path.Combine(a, b), 'x')", "Localization.T(".Length).Count);
    }

    private static readonly Regex DirectSettingsStorageAccess =
        new(@"\bClickraStorage\.(?:GetSetting|GetSettingBool|GetSettingInt|SaveSetting|SettingsReloaded|EnsureFreshSettings)\b",
            RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static readonly Regex SettingsGatewayStorageAccess =
        new(@"\bClickraStorage\.(?<member>[A-Za-z0-9_]+)\b",
            RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static readonly IReadOnlySet<string> AllowedSettingsGatewayMembers =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "GetSetting",
            "GetSettingBool",
            "GetSettingInt",
            "SaveSetting",
            "SettingsReloaded",
            "EnsureFreshSettings"
        };

    private static void TestSettingsPersistenceHasSingleApplicationGateway()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string settingsGatewayPath = Path.Combine(
            root,
            "src",
            ArchitectureCoreProjectDirectory,
            ArchitectureApplicationDirectory,
            "SettingsController.cs");
        Assert.True(File.Exists(settingsGatewayPath), "Application settings gateway must exist.");

        var violations = new List<string>();
        foreach (string directory in new[]
                 {
                     Path.Combine(root, "src", ArchitectureCliProjectDirectory),
                     Path.Combine(root, "src", ArchitectureFluentProjectDirectory),
                     Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory)
                 })
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(file, settingsGatewayPath, StringComparison.OrdinalIgnoreCase)) continue;
                string source = File.ReadAllText(file);
                if (!DirectSettingsStorageAccess.IsMatch(source)) continue;
                violations.Add(NormalizeRepoPath(root, file));
            }
        }

        string gatewaySource = File.ReadAllText(settingsGatewayPath);
        foreach (Match match in SettingsGatewayStorageAccess.Matches(gatewaySource))
        {
            string member = match.Groups["member"].Value;
            if (!AllowedSettingsGatewayMembers.Contains(member))
                violations.Add($"src/Clickra.Core/Application/SettingsController.cs: unexpected ClickraStorage.{member} access");
        }

        Assert.True(
            violations.Count == 0,
            "CLI, Fluent, and application workflows must use SettingsController for settings persistence; " +
            "the gateway itself may expose settings APIs only:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static readonly Regex RawPresentationSettingsAccess =
        new(@"\bSettingsController\.(?:Get|GetBool|GetInt|Set)\s*\(",
            RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static readonly Regex PersistedBooleanRepresentationInPresentation =
        new(@"\bClickraSettings\.(?:ValueTrue|ValueFalse)\b",
            RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static void TestPresentationSettingsUseTypedApplicationAccess()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        var violations = new List<string>();
        foreach (string directory in new[]
                 {
                     Path.Combine(root, "src", ArchitectureCliProjectDirectory),
                     Path.Combine(root, "src", ArchitectureFluentProjectDirectory)
                 })
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                     && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                string source = File.ReadAllText(file);
                if (RawPresentationSettingsAccess.IsMatch(source))
                    violations.Add($"{NormalizeRepoPath(root, file)}: raw SettingsController key access");
                if (PersistedBooleanRepresentationInPresentation.IsMatch(source))
                    violations.Add($"{NormalizeRepoPath(root, file)}: persisted true/false representation leaked into presentation");
            }
        }

        string gateway = File.ReadAllText(Path.Combine(
            root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory, "SettingsController.cs"));
        foreach (string typedContract in new[]
                 {
                     "public static string Language",
                     "public static bool QuietMode",
                     "public static bool NotificationsEnabled",
                     "public static OfficeEnginePreference OfficeEngine",
                     "public static OutputDirectoryPreference OutputDirectory",
                     "public static bool ReadBool(SettingDescriptor descriptor)",
                     "public static int ReadInt(SettingDescriptor descriptor)",
                     "public static void WriteBool(SettingDescriptor descriptor, bool value)",
                     "public static void WriteInt(SettingDescriptor descriptor, int value)"
                 })
        {
            if (!gateway.Contains(typedContract, StringComparison.Ordinal))
                violations.Add($"SettingsController missing typed contract: {typedContract}");
        }

        Assert.True(
            violations.Count == 0,
            "CLI and Fluent must consume typed application settings instead of persisted keys/boolean encodings:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static readonly Regex DirectParkedTaskActionStorageAccess =
        new(@"\bClickraStorage\.(?:GetParkedRetentionDays|AdjustParkedRetention|SetParkedRetentionOverride|CancelParkedTask|ClaimParkedTaskForResume)\b",
            RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static readonly Regex ParkedTaskGatewayStorageAccess =
        new(@"\bClickraStorage\.(?<member>[A-Za-z0-9_]+)\b",
            RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static readonly IReadOnlySet<string> AllowedParkedTaskGatewayMembers =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "GetParkedRetentionDays",
            "AdjustParkedRetention",
            "SetParkedRetentionOverride",
            "CancelParkedTask",
            "ClaimParkedTaskForResume"
        };

    private static void TestParkedTaskActionsHaveSingleApplicationGateway()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string gatewayPath = Path.Combine(
            root,
            "src",
            ArchitectureCoreProjectDirectory,
            ArchitectureApplicationDirectory,
            "ParkedTaskController.cs");
        Assert.True(File.Exists(gatewayPath), "Application parked-task gateway must exist.");

        var violations = new List<string>();
        foreach (string directory in new[]
                 {
                     Path.Combine(root, "src", ArchitectureCliProjectDirectory),
                     Path.Combine(root, "src", ArchitectureFluentProjectDirectory),
                     Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory)
                 })
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(file, gatewayPath, StringComparison.OrdinalIgnoreCase)) continue;
                string source = File.ReadAllText(file);
                if (!DirectParkedTaskActionStorageAccess.IsMatch(source)) continue;
                violations.Add(NormalizeRepoPath(root, file));
            }
        }

        string gatewaySource = File.ReadAllText(gatewayPath);
        foreach (Match match in ParkedTaskGatewayStorageAccess.Matches(gatewaySource))
        {
            string member = match.Groups["member"].Value;
            if (!AllowedParkedTaskGatewayMembers.Contains(member))
                violations.Add($"src/Clickra.Core/Application/ParkedTaskController.cs: unexpected ClickraStorage.{member} access");
        }

        Assert.True(
            violations.Count == 0,
            "CLI, Fluent, and application workflows must use ParkedTaskController for parked-task actions; " +
            "the gateway itself may expose only parked-task action APIs:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static readonly Regex RawPresentationStorageAccess =
        new(@"\bClickraStorage\b", RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static void TestPresentationDoesNotDependOnRawStorage()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        var violations = new List<string>();
        foreach (string directory in new[]
                 {
                     Path.Combine(root, "src", ArchitectureCliProjectDirectory),
                     Path.Combine(root, "src", ArchitectureFluentProjectDirectory)
                 })
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                if (!RawPresentationStorageAccess.IsMatch(source)) continue;
                violations.Add(NormalizeRepoPath(root, file));
            }
        }

        Assert.True(
            violations.Count == 0,
            "CLI and Fluent presentation must use narrow application owners instead of ClickraStorage calls or nested storage models:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void TestCoreBoundedContextsDeclareOwnedNamespaces()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string core = Path.Combine(root, "src", ArchitectureCoreProjectDirectory);
        var violations = new List<string>();
        foreach ((string folder, string expectedNamespace) in new[]
                 {
                     ("Storage", "Clickra.Core.Storage"),
                     ("Translation", "Clickra.Core.Translation"),
                     ("Diagnostics", "Clickra.Core.Diagnostics"),
                     ("Fonts", "Clickra.Core.Fonts"),
                     ("Infrastructure", "Clickra.Core.Infrastructure")
                 })
        {
            string directory = Path.Combine(core, folder);
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                if (Regex.IsMatch(
                        source,
                        $@"\bnamespace\s+{Regex.Escape(expectedNamespace)}\s*(?:;|\{{)",
                        RegexOptions.CultureInvariant,
                        ArchitectureRegexTimeout))
                    continue;
                violations.Add($"{NormalizeRepoPath(root, file)}: expected namespace {expectedNamespace}");
            }
        }

        foreach (string applicationFile in new[] { "HistoryFeed.cs", "ConversionTaskLifecycle.cs" })
        {
            string file = Path.Combine(core, ArchitectureApplicationDirectory, applicationFile);
            string source = File.ReadAllText(file);
            if (!source.Contains("namespace Clickra.Core.Application;", StringComparison.Ordinal))
                violations.Add($"{NormalizeRepoPath(root, file)}: expected namespace Clickra.Core.Application");
        }

        Assert.True(
            violations.Count == 0,
            "Physical Core bounded contexts must keep namespaces that encode their ownership:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void TestStorageFacadeDelegatesToFocusedOwners()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string storage = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, "Storage");

        foreach (string owner in new[]
                 {
                     "StorageRuntime.cs",
                     "SettingsStore.cs",
                     "StoragePathResolver.cs",
                     "HistoryStore.cs",
                     "TaskRecordStore.cs",
                     "TaskQueueStore.cs",
                     "LegacyActiveRecordStore.cs",
                     "ParkedTaskCoordinator.cs",
                     "TaskPruningCoordinator.cs"
                 })
        {
            Assert.True(File.Exists(Path.Combine(storage, owner)), $"Focused storage owner is missing: {owner}");
        }

        string facade = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(storage, "ClickraStorage*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        foreach (string implementationDetail in new[]
                 {
                     "FileSystemWatcher",
                     "new Mutex(",
                     "File.AppendAllText(",
                     "TaskRecordStore.Write(",
                     "Directory.GetFiles("
                 })
        {
            Assert.False(
                facade.Contains(implementationDetail, StringComparison.Ordinal),
                $"ClickraStorage compatibility facade reclaimed persistence implementation detail: {implementationDetail}");
        }

        Assert.True(facade.Contains("SettingsStore.Get(", StringComparison.Ordinal),
            "Settings persistence must be owned by SettingsStore behind ClickraStorage.");
        Assert.True(facade.Contains("TaskQueueStore.Start(", StringComparison.Ordinal),
            "Task lifecycle persistence must be owned by TaskQueueStore behind ClickraStorage.");
        Assert.True(facade.Contains("HistoryStore.Read(", StringComparison.Ordinal),
            "History persistence must be owned by HistoryStore behind ClickraStorage.");
        Assert.True(facade.Contains("LegacyActiveRecordStore.Start(", StringComparison.Ordinal),
            "legacy active.tmp compatibility must be owned by LegacyActiveRecordStore.");
    }

    private static void TestStorageDoesNotOwnLocalizedPresentationText()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string storage = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, "Storage");
        var violations = new List<string>();

        foreach (string file in Directory.EnumerateFiles(storage, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            if (source.Contains("Localization.T(", StringComparison.Ordinal))
                violations.Add(NormalizeRepoPath(root, file));
        }

        Assert.True(
            violations.Count == 0,
            "Storage must return facts/state; localized presentation wording belongs above storage:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void TestPresentationAndPlatformHelpersHaveExplicitOwnership()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string core = Path.Combine(root, "src", ArchitectureCoreProjectDirectory);
        var expectations = new[]
        {
            (Path.Combine(core, "Presentation", "Native", "DashboardLayout.cs"), "namespace Clickra.Core.Presentation.Native;"),
            (Path.Combine(core, "Presentation", "Native", "HistoryLayout.cs"), "namespace Clickra.Core.Presentation.Native;"),
            (Path.Combine(core, "Presentation", "Shared", "VectorIcons.cs"), "namespace Clickra.Core.Presentation;")
        };

        foreach ((string file, string namespaceDeclaration) in expectations)
        {
            Assert.True(File.Exists(file), $"Presentation owner is missing: {NormalizeRepoPath(root, file)}");
            Assert.True(
                File.ReadAllText(file).Contains(namespaceDeclaration, StringComparison.Ordinal),
                $"{NormalizeRepoPath(root, file)} must declare {namespaceDeclaration}");
        }

        Assert.False(
            File.Exists(Path.Combine(core, "FluentRuntimeHelper.cs")),
            "The zero-production-caller FluentRuntimeHelper must not return to domain/application Core.");

        foreach (string catalog in new[] { "Localization.Cli.cs", "Localization.Fluent.cs" })
        {
            string file = Path.Combine(core, "Localization", catalog);
            Assert.True(File.Exists(file), $"The centralized localization catalog must remain single-source: {catalog}");
            string source = File.ReadAllText(file);
            Assert.True(
                source.Contains("public static partial class Localization", StringComparison.Ordinal),
                $"{catalog} must stay in the shared Localization registry rather than being duplicated by a UI project.");
        }
    }

    private static void TestTranslationContextsStayDistinct()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string core = Path.Combine(root, "src", ArchitectureCoreProjectDirectory);
        string providerDirectory = Path.Combine(core, "Translation");
        string legacyPdfTranslationDirectory = Path.Combine(core, "Processors", "PdfTranslation", "Translation");
        string textProcessingDirectory = Path.Combine(core, "Processors", "PdfTranslation", "TextProcessing");

        Assert.False(
            Directory.Exists(legacyPdfTranslationDirectory) &&
            Directory.EnumerateFiles(legacyPdfTranslationDirectory, "*.cs", SearchOption.AllDirectories).Any(),
            "The ambiguous Processors/PdfTranslation/Translation source context must not return.");
        Assert.True(
            Directory.Exists(textProcessingDirectory),
            "PDF translation text processing must have an explicit TextProcessing context.");

        foreach (string file in Directory.EnumerateFiles(providerDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            Assert.True(
                Regex.IsMatch(
                    source,
                    @"\bnamespace\s+Clickra\.Core\.Translation\s*(?:;|\{)",
                    RegexOptions.CultureInvariant,
                    ArchitectureRegexTimeout),
                $"{NormalizeRepoPath(root, file)} must remain translation-provider infrastructure.");
        }

        foreach (string file in Directory.EnumerateFiles(textProcessingDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            Assert.True(
                Regex.IsMatch(
                    source,
                    @"\bnamespace\s+Clickra\.Core\.Processors\.PdfTranslation\.TextProcessing\s*(?:;|\{)",
                    RegexOptions.CultureInvariant,
                    ArchitectureRegexTimeout),
                $"{NormalizeRepoPath(root, file)} must remain inside the PDF TextProcessing context.");
        }
    }

    /// <summary>Splits a call's argument list at top-level commas, starting just after its opening parenthesis.</summary>
    private static IReadOnlyList<string> ReadTopLevelArguments(string source, int start)
    {
        var arguments = new List<string>();
        int depth = 0;
        int segmentStart = start;
        int i = start;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '"' || c == '\'')
            {
                i = SkipQuotedSegment(source, i, c);
                continue;
            }

            if (ProcessTopLevelArgumentCharacter(source, i, c, arguments, ref depth, ref segmentStart))
                return arguments;

            i++;
        }

        return arguments;
    }

    private static bool ProcessTopLevelArgumentCharacter(
        string source,
        int index,
        char character,
        List<string> arguments,
        ref int depth,
        ref int segmentStart)
    {
        if (character is '(' or '[' or '{')
        {
            depth++;
            return false;
        }

        if (character is ')' or ']' or '}')
        {
            if (depth > 0)
            {
                depth--;
                return false;
            }

            string last = source[segmentStart..index].Trim();
            if (last.Length > 0 || arguments.Count > 0)
                arguments.Add(last);
            return true;
        }

        if (character != ',' || depth != 0)
            return false;

        arguments.Add(source[segmentStart..index].Trim());
        segmentStart = index + 1;
        return false;
    }

    private static int SkipQuotedSegment(string source, int quoteIndex, char quote)
    {
        int i = quoteIndex + 1;
        while (i < source.Length)
        {
            if (source[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (source[i] == quote)
                return i + 1;

            i++;
        }

        return i;
    }

    private static void TestApplicationContractsStayUiIndependent()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string applicationDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory);
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
        string[] productCommands = new[] { "pdf", "word", "excel", "ppt", "markdown", "image" }
            .SelectMany(ConvertCommandMetadata.GetCommandsForType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] catalogCommands = ConversionUseCases.Commands
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(productCommands.SequenceEqual(catalogCommands, StringComparer.OrdinalIgnoreCase),
            "Every product conversion command must have exactly one application use-case owner before legacy fallbacks can remain retired.");

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
            "_decrypted.pdf",
            "FileProcessor.DecryptPdf",
            nameof(DecryptPdfUseCase),
            "decrypt");

    private static void TestSplitWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            "_split.pdf",
            "FileProcessor.SplitPdf",
            nameof(SplitPdfUseCase),
            "split");

    private static void TestCompressWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            "_compressed.pdf",
            "FileProcessor.CompressPdf",
            nameof(CompressPdfUseCase),
            "compression");

    private static void AssertMigratedWorkflowHasSingleExecutionOwner(
        string legacyOutputSuffix,
        string processorCall,
        string? useCaseTypeName,
        string workflowName)
    {
        MigrationSurfaceSources sources = ReadMigrationSurfaceSources();

        Assert.False(sources.Registry.Contains(legacyOutputSuffix, StringComparison.Ordinal),
            $"Legacy ConvertCommandRegistry must not retain {workflowName} output-path policy after application migration.");
        Assert.False((sources.Native + sources.NativeAdapter).Contains(processorCall, StringComparison.Ordinal),
            $"Native presentation must not execute the {workflowName} processor directly.");
        Assert.False(sources.Quiet.Contains(processorCall, StringComparison.Ordinal),
            $"Headless CLI must not execute the {workflowName} processor directly.");
        string? specificCatalogResolution = useCaseTypeName is null
            ? null
            : $"ConversionUseCases.GetRequired({useCaseTypeName}.CommandName)";
        bool nativeCatalogResolution =
            sources.NativeAdapter.Contains(GenericCatalogResolution, StringComparison.Ordinal)
            || (specificCatalogResolution is not null
                && sources.NativeAdapter.Contains(specificCatalogResolution, StringComparison.Ordinal));
        bool quietCatalogResolution =
            sources.Quiet.Contains(GenericCatalogResolution, StringComparison.Ordinal)
            || (specificCatalogResolution is not null
                && sources.Quiet.Contains(specificCatalogResolution, StringComparison.Ordinal));
        Assert.True(sources.FluentMain.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                    && sources.FluentTask.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                    && nativeCatalogResolution
                    && quietCatalogResolution,
            $"All product surfaces must resolve {workflowName} execution through the application use-case catalog.");
    }

    private static void TestImg2PdfWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            "\"img2pdf\" => files.Select",
            "FileProcessor.ConvertImagesToPdf(new List<string> { f }",
            nameof(Img2PdfUseCase),
            "img2pdf");

    private static void TestMergeWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            "Merged_PDF.pdf",
            "FileProcessor.MergePdfs",
            nameof(MergePdfUseCase),
            "merge");

    private static void TestImgMergeWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            "Merged_Images.pdf",
            "FileProcessor.ConvertImagesToPdf(files",
            nameof(ImgMergeUseCase),
            "img-merge");

    private static void TestImgStitchWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
            "Stitched_Image.png",
            "FileProcessor.StitchImages",
            nameof(ImgStitchUseCase),
            "img-stitch");

    private static void TestImgCompressWorkflowHasSingleExecutionOwner()
        => AssertMigratedWorkflowHasSingleExecutionOwner(
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
                "EstimateImageFormatOutputs",
                "FileProcessor.ConvertImageFormat",
                null,
                command);
        }
    }

    private static void TestTranslatePdfWorkflowHasSingleExecutionOwner()
        => AssertDocumentWorkflowHasSingleExecutionOwner(
            "FileProcessor.TranslatePdf",
            nameof(TranslatePdfUseCase),
            "PDF translation");

    private static void TestWordToPdfWorkflowHasSingleExecutionOwner()
        => AssertDocumentWorkflowHasSingleExecutionOwner(
            "FileProcessor.ConvertWordToPdf",
            nameof(WordToPdfUseCase),
            "Word conversion");

    private static void TestPptToPdfWorkflowHasSingleExecutionOwner()
        => AssertDocumentWorkflowHasSingleExecutionOwner(
            "FileProcessor.ConvertPptToPdf",
            nameof(PptToPdfUseCase),
            "PowerPoint conversion");

    private static void TestExcelToPdfWorkflowHasSingleExecutionOwner()
        => AssertDocumentWorkflowHasSingleExecutionOwner(
            "FileProcessor.ConvertExcelToPdf",
            nameof(ExcelToPdfUseCase),
            "Excel conversion");

    private static void TestMarkdownToPdfWorkflowHasSingleExecutionOwner()
    {
        AssertDocumentWorkflowHasSingleExecutionOwner(
            "FileProcessor.ConvertMarkdownToPdf",
            nameof(MarkdownToPdfUseCase),
            "Markdown PDF conversion");
        AssertMarkdownWorkflowPreservesPresentationOptions(MarkdownToPdfUseCase.CommandName, "md2pdf");
    }

    private static void TestMarkdownToWordWorkflowHasSingleExecutionOwner()
    {
        AssertDocumentWorkflowHasSingleExecutionOwner(
            "FileProcessor.ConvertMarkdownToWord",
            nameof(MarkdownToWordUseCase),
            "Markdown Word conversion");
        AssertMarkdownWorkflowPreservesPresentationOptions(MarkdownToWordUseCase.CommandName, "md2word");
    }

    private static void AssertDocumentWorkflowHasSingleExecutionOwner(
        string processorCall,
        string useCaseTypeName,
        string workflowName)
    {
        MigrationSurfaceSources sources = ReadMigrationSurfaceSources();

        Assert.False(sources.Registry.Contains("EstimateOutputs(", StringComparison.Ordinal),
            "Legacy ConvertCommandRegistry must not retain output planning after application migration.");
        Assert.False((sources.Native + sources.NativeAdapter).Contains(processorCall, StringComparison.Ordinal),
            $"Native presentation must not execute {workflowName} directly.");
        Assert.False(sources.Quiet.Contains(processorCall, StringComparison.Ordinal),
            $"Headless CLI must not execute {workflowName} directly.");

        string specificCatalogResolution = $"ConversionUseCases.GetRequired({useCaseTypeName}.CommandName)";
        Assert.True(sources.FluentMain.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                    && sources.FluentTask.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                    && (sources.NativeAdapter.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                        || sources.NativeAdapter.Contains(specificCatalogResolution, StringComparison.Ordinal))
                    && (sources.Quiet.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                        || sources.Quiet.Contains(specificCatalogResolution, StringComparison.Ordinal)),
            $"All product surfaces must resolve {workflowName} through the application use-case catalog.");
    }

    private static MigrationSurfaceSources ReadMigrationSurfaceSources()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        return new MigrationSurfaceSources(
            File.ReadAllText(Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureProcessorsDirectory, "ConvertCommandRegistry.cs")),
            File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureProgressDirectory, ArchitectureProgressProcessFile)),
            File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureProgressDirectory, ArchitectureNativeExecutionAdapterFile)),
            File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, "Cli", ArchitectureCliSourceFile)),
            File.ReadAllText(Path.Combine(root, "src", ArchitectureFluentProjectDirectory, "MainPage.xaml.cs")),
            File.ReadAllText(Path.Combine(root, "src", ArchitectureFluentProjectDirectory, "TaskProgressPage.xaml.cs")));
    }

    private static void AssertMarkdownWorkflowPreservesPresentationOptions(string command, string workflowName)
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string native = File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureProgressDirectory, ArchitectureProgressProcessFile));
        string nativeAdapter = File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureProgressDirectory, ArchitectureNativeExecutionAdapterFile));
        string quiet = File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, "Cli", ArchitectureCliSourceFile));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", ArchitectureFluentProjectDirectory, "MainPage.xaml.cs"));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", ArchitectureFluentProjectDirectory, "TaskProgressPage.xaml.cs"));

        Assert.True(native.Contains("new NativeConversionExecutionAdapter(", StringComparison.Ordinal)
                    && nativeAdapter.Contains("CommandOptions: _commandOptions", StringComparison.Ordinal)
                    && nativeAdapter.Contains("new TaskIdExecutionObserver(_taskStarted, _recordTaskIndex)", StringComparison.Ordinal)
                    && nativeAdapter.Contains("ResumeStartIndex = _startIndex", StringComparison.Ordinal),
            $"Native {workflowName} must route its option snapshot and resume checkpoint through the application owner.");
        Assert.True((quiet.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                     || quiet.Contains($"ConversionUseCases.GetRequired({command})", StringComparison.Ordinal)
                     || quiet.Contains($"ConversionUseCases.GetRequired({(command == MarkdownToPdfUseCase.CommandName ? nameof(MarkdownToPdfUseCase) : nameof(MarkdownToWordUseCase))}.CommandName)", StringComparison.Ordinal))
                    && quiet.Contains("MarkdownPdfOptions.Create()", StringComparison.Ordinal)
                    && quiet.Contains("TrackTaskLifecycle: false", StringComparison.Ordinal),
            $"Headless {workflowName} must use default Markdown options without task lifecycle ownership.");
        Assert.True(fluentMain.Contains("commandOptions));", StringComparison.Ordinal)
                    && fluentTask.Contains("commandOptions,", StringComparison.Ordinal)
                    && fluentTask.Contains("ExistingTaskId: existingTaskId", StringComparison.Ordinal),
            $"Both Fluent {workflowName} paths must forward the already-prompted Markdown option snapshot into planning.");
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
