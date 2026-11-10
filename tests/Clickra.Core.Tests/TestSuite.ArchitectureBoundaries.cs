using System.Text.RegularExpressions;
using Clickra.Core.Application;
using Clickra.Core.Modules.Pdf.Print.Application;
using Clickra.Core.Modules.Pdf.ToImage.Application;
using Clickra.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const int ArchitectureViolationBaselineCeiling = 0;
    private const int ProcessorGlobalStateBaselineCeiling = 0;
    private const string RepositoryRootMissingMessage = "Could not locate the repository root.";
    private const string BatchRenameCommandIdentifier = "BatchRenameCommand";
    private const string ArchitectureCliProjectDirectory = "Clickra.CLI";
    private const string ArchitectureCoreProjectDirectory = "Clickra.Core";
    private const string ArchitectureApplicationDirectory = "Application";
    private const string ArchitectureProcessingDirectory = "Processing";
    private const string ArchitectureTranslationDirectory = "Translation";
    private const string ArchitectureSharedDirectory = "Shared";
    private const string ArchitectureExecutionDirectory = "Execution";
    private const string ArchitectureCompressionSubmodule = "Compression";
    private const string ArchitectureMergeSubmodule = "Merge";
    private const string ArchitectureModulesDirectory = "Modules";
    private const string ArchitectureFluentProjectDirectory = "Clickra.Fluent";
    private const string ArchitectureDashboardDirectory = "Dashboard";
    private const string ArchitectureLayoutDirectory = "Layout";
    private const string ArchitectureProgressDirectory = "Progress";
    private const string ArchitectureProcessorsDirectory = "Processors";
    private const string ArchitectureProgressProcessFile = "ProgressWindow.Process.cs";
    private const string ArchitectureNativeExecutionAdapterFile = "NativeConversionExecutionAdapter.cs";
    private const string ArchitectureCliSourceFile = "ClickraCli.cs";
    private const string GenericCatalogResolution = "ConversionUseCases.GetRequired(command)";
    private const string ArchitectureStorageDirectory = "Storage";
    private const string ArchitecturePresentationDirectory = "Presentation";
    private const string ArchitectureInfrastructureDirectory = "Infrastructure";
    private const string ArchitectureSettingsModuleName = "Settings";
    private const string ArchitecturePdfTranslationModuleName = "PdfTranslation";
    private const string ArchitectureLocalizationCall = "Localization.T(";
    private const string ArchitectureConvertRegistryFile = "ConvertCommandRegistry.cs";
    private const string ArchitectureConversionExecutionName = "ConversionExecutionController";
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
        string PresentationExecution,
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
            "Architecture boundaries: primary renderers depend on shared Presentation owners",
            TestPrimaryRenderersDependOnPresentationOwners);
        runner.RunGuard(
            "Architecture boundaries: Presentation depends inward through Application contracts",
            TestPresentationDependsInwardThroughApplication);
        runner.RunGuard(
            "Architecture boundaries: command registry belongs to Application",
            TestCommandRegistryBelongsToApplication);
        runner.RunGuard(
            "Architecture boundaries: legacy layer-first buckets do not expand",
            TestModuleFirstOrganizationDoesNotExpandLegacyBuckets);
        runner.RunGuard(
            "Architecture boundaries: Modules keep capability as the first directory axis",
            TestModuleFirstModulesKeepCapabilityAsFirstAxis);
        runner.RunGuard(
            "Architecture boundaries: conversion modules conform to module architecture conventions",
            TestModuleConventionsEnforced);
        runner.RunGuard(
            "Architecture boundaries: presentation layers forbid hardcoded command branching",
            TestPresentationForbidsHardcodedCommands);
        runner.RunGuard(
            "Architecture boundaries: conversion execution families share lifecycle result mapping",
            TestConversionExecutionFamiliesShareLifecycleResultMapping);
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
            "Architecture boundaries: migrated batch rename workflow has one execution owner",
            TestBatchRenameWorkflowHasSingleExecutionOwner);
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
        runner.RunGuard(
            "Architecture boundaries: domain roots have no loose source files",
            TestDomainRootsHaveNoLooseSourceFiles);
        runner.RunGuard(
            "Architecture boundaries: shared submodules forbid conversion use cases and cross-domain leakage",
            TestSharedSubmodulesForbidConversionUseCasesAndCrossDomainLeakage);
        runner.RunGuard(
            "Architecture boundaries: functional submodules are symmetrical",
            TestFunctionalSubmodulesAreSymmetrical);
    }

    private static void TestConversionExecutionFamiliesShareLifecycleResultMapping()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string application = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory);
        string[] familyPaths =
        {
            Path.Combine(application, "PerFileConversionUseCaseBase.cs"),
            Path.Combine(application, "SingleOutputConversionUseCaseBase.cs"),
            Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory,
                "Doc", ArchitectureSharedDirectory, ArchitectureApplicationDirectory, "OfficeToPdfUseCaseBase.cs")
        };
        foreach (string path in familyPaths)
        {
            string source = File.ReadAllText(path);
            Assert.True(
                source.Contains("ConversionLifecycleResultRunner.RunAsync(", StringComparison.Ordinal),
                $"{NormalizeRepoPath(root, path)} must share lifecycle result mapping.");
            Assert.False(
                source.Contains("catch (OperationCanceledException)", StringComparison.Ordinal)
                || source.Contains("lifecycle?.CompleteSuccess(", StringComparison.Ordinal)
                || source.Contains("lifecycle?.CompleteFailure(", StringComparison.Ordinal),
                $"{NormalizeRepoPath(root, path)} must not duplicate lifecycle completion.");
        }
        string lifecycle = File.ReadAllText(Path.Combine(application, "ConversionTaskLifecycle.cs"));
        foreach (string required in new[]
                 {
                     "internal static class ConversionLifecycleResultRunner",
                     "catch (ConversionParkedException ex) when (parkOnSignal)",
                     "catch (OperationCanceledException)",
                     "lifecycle?.CompleteSuccess(outputRecord)",
                     "lifecycle?.CompleteFailure(\"Canceled\", outputRecord)",
                     "lifecycle?.Park(ex.Message, ex.NextFileIndex)"
                 })
        {
            Assert.True(lifecycle.Contains(required, StringComparison.Ordinal),
                $"Shared lifecycle mapping must preserve: {required}");
        }
    }

    private static void TestModuleFirstOrganizationDoesNotExpandLegacyBuckets()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string baselinePath = Path.Combine(
            root,
            "tests",
            "Clickra.Core.Tests",
            "Architecture.ModuleFirstLegacyBuckets.txt");
        Assert.True(
            File.Exists(baselinePath),
            "Module-first migration guard baseline must exist.");

        var allowedPrefixes = new[]
        {
            "src/Clickra.Core/Application/",
            "src/Clickra.Core/Presentation/",
            "src/Clickra.Core/Processors/",
            "src/Clickra.Core/Storage/",
            "src/Clickra.Core/Models/"
        };

        HashSet<string> baseline = File.ReadAllLines(baselinePath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(
            baseline.All(path => allowedPrefixes.Any(prefix =>
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))),
            "Module-first migration baseline may only contain the explicitly frozen legacy generic buckets.");

        string coreRoot = Path.Combine(root, "src", ArchitectureCoreProjectDirectory);
        string[] legacyDirectories =
        {
            ArchitectureApplicationDirectory,
            ArchitecturePresentationDirectory,
            ArchitectureProcessorsDirectory,
            "Storage",
            "Models"
        };

        string[] additions = legacyDirectories
            .Select(directory => Path.Combine(coreRoot, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !baseline.Contains(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            additions.Length == 0,
            "Module-first architecture freezes the legacy layer-first buckets. " +
            "Move new feature-private code under src/Clickra.Core/Modules/<Capability>/... instead of expanding " +
            $"Application/Presentation/Processors/Storage/Models. Unexpected files: {string.Join(", ", additions)}");
    }

    private static void TestModuleFirstModulesKeepCapabilityAsFirstAxis()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string modulesRoot = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory);
        if (!Directory.Exists(modulesRoot))
            return;

        string[] filesAtModulesRoot = Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(
            filesAtModulesRoot.Length == 0,
            "Production code under Modules must first choose a capability directory. " +
            $"Files found directly under Modules: {string.Join(", ", filesAtModulesRoot)}");

        var technicalRoleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ArchitectureApplicationDirectory,
            ArchitecturePresentationDirectory,
            "Model",
            "Models",
            "Analysis",
            "Rendering",
            "Persistence",
            "Storage",
            "Processor",
            "Processors",
            ArchitectureInfrastructureDirectory,
            "Ports",
            "Adapters"
        };

        string[] invalidFirstAxis = Directory.EnumerateDirectories(modulesRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path))
            .Where(name => !string.IsNullOrWhiteSpace(name) && technicalRoleNames.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            invalidFirstAxis.Length == 0,
            "Modules are capability-first. Technical roles belong inside a capability, for example " +
            "Modules/Pdf/Translation/Model rather than Modules/Models/Pdf/Translation. " +
            $"Invalid first-axis directories: {string.Join(", ", invalidFirstAxis)}");

        var namespaceViolations = new List<string>();
        foreach (string capabilityDirectory in Directory.EnumerateDirectories(modulesRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string capability = Path.GetFileName(capabilityDirectory);
            string expectedPrefix = $"Clickra.Core.Modules.{capability}";
            string pattern = $@"\bnamespace\s+{Regex.Escape(expectedPrefix)}(?:\.|\s*(?:;|\{{))";
            foreach (string file in Directory.EnumerateFiles(capabilityDirectory, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                if (Regex.IsMatch(
                        source,
                        pattern,
                        RegexOptions.CultureInvariant,
                        ArchitectureRegexTimeout))
                    continue;
                namespaceViolations.Add(
                    $"{NormalizeRepoPath(root, file)}: expected namespace rooted at {expectedPrefix}");
            }
        }

        Assert.True(
            namespaceViolations.Count == 0,
            "Module files must declare the namespace of their owning capability:" +
            Environment.NewLine + string.Join(Environment.NewLine, namespaceViolations));
    }

    private static void TestModuleConventionsEnforced()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string modulesRoot = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory);
        if (!Directory.Exists(modulesRoot))
            return;

        var violations = new List<string>();
        ValidateConversionModules(modulesRoot, violations);
        ValidateModuleOptionFiles(root, modulesRoot, violations);
        ValidateAuthorizedModuleDirectories(modulesRoot, violations);
        ValidatePresentationReferences(root, modulesRoot, violations);

        Assert.True(
            violations.Count == 0,
            "Module architecture conventions violated:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void ValidateConversionModules(string modulesRoot, List<string> violations)
    {
        string[] conversionModules =
        {
            Path.Combine("Doc", "Word"),
            Path.Combine("Doc", "Excel"),
            Path.Combine("Doc", "Ppt"),
            Path.Combine("Doc", "Markdown"),
            Path.Combine("File", "BatchRename"),
            Path.Combine("Img", ArchitectureCompressionSubmodule),
            Path.Combine("Img", "FormatConvert"),
            Path.Combine("Img", ArchitectureMergeSubmodule),
            Path.Combine("Img", "Stitch"),
            Path.Combine("Img", "ToPdf"),
            Path.Combine("Pdf", ArchitectureCompressionSubmodule),
            Path.Combine("Pdf", "Decrypt"),
            Path.Combine("Pdf", ArchitectureMergeSubmodule),
            Path.Combine("Pdf", "Print"),
            Path.Combine("Pdf", "Split"),
            Path.Combine("Pdf", "ToImage"),
            Path.Combine("Pdf", ArchitectureTranslationDirectory)
        };

        foreach (string module in conversionModules)
        {
            string moduleDir = Path.Combine(modulesRoot, module);
            Assert.True(Directory.Exists(moduleDir), $"Expected conversion module directory: {module}");

            ValidateModuleApplicationLayer(module, moduleDir, violations);
            ValidateModuleProcessingLayer(module, moduleDir, violations);
        }
    }

    private static void ValidateModuleApplicationLayer(string module, string moduleDir, List<string> violations)
    {
        string appDir = Path.Combine(moduleDir, ArchitectureApplicationDirectory);
        if (!Directory.Exists(appDir))
        {
            violations.Add($"{module}: missing mandatory {ArchitectureApplicationDirectory}/ directory.");
            return;
        }

        string[] appFiles = Directory.GetFiles(appDir, "*UseCase*.cs", SearchOption.AllDirectories);
        if (appFiles.Length == 0)
        {
            violations.Add($"{module}: Application directory must contain at least one UseCase file.");
            return;
        }

        var allowedBases = new[]
        {
            "ConversionUseCaseBase",
            "PerFileConversionUseCaseBase",
            "SingleOutputConversionUseCaseBase",
            "OfficeToPdfUseCaseBase",
            "MarkdownConversionUseCaseBase",
            "IConversionUseCase"
        };

        foreach (string file in appFiles)
        {
            string source = File.ReadAllText(file);
            if (source.Contains("abstract class", StringComparison.Ordinal) || source.Contains("interface ", StringComparison.Ordinal))
                continue;

            bool inheritsRequiredBase = allowedBases.Any(b =>
                Regex.IsMatch(source, $@":\s*([A-Za-z0-9_.,<>\s]*\b)?{b}\b", RegexOptions.CultureInvariant, ArchitectureRegexTimeout));

            if (!inheritsRequiredBase)
            {
                violations.Add($"{Path.GetFileName(file)} in {module}: use case class must inherit from a required conversion use-case base class.");
            }
        }
    }

    private static void ValidateModuleProcessingLayer(string module, string moduleDir, List<string> violations)
    {
        string procDir = Path.Combine(moduleDir, ArchitectureProcessingDirectory);
        string execDir = Path.Combine(moduleDir, ArchitectureExecutionDirectory);

        if (Directory.Exists(execDir))
        {
            violations.Add($"{module}: Execution/ directory is strictly prohibited across all modules; processors must reside in {ArchitectureProcessingDirectory}/.");
        }

        if (!Directory.Exists(procDir))
        {
            violations.Add($"{module}: missing mandatory {ArchitectureProcessingDirectory}/ directory.");
            return;
        }

        string[] procFiles = Directory.GetFiles(procDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => (Path.GetFileName(f).Contains("Processor") || Path.GetFileName(f).Contains("Executor"))
                     && !Path.GetFileName(f).EndsWith("Engine.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (procFiles.Length == 0)
        {
            violations.Add($"{module}: Processing directory must contain at least one Processor/Executor file.");
            return;
        }

        var allowedProcessorBases = new[]
        {
            "SingleFileProcessorBase",
            "MultiFileProcessorBase",
            "MarkdownSingleFileProcessorBase",
            "FileProcessor"
        };

        var classRegex = new Regex(
            @"\b(?:public|internal|private|protected)?\s*(?:(static|abstract|sealed)\s+)?class\s+([A-Za-z0-9_]+)(?:\s*:\s*([^{]+))?\{",
            RegexOptions.Compiled,
            ArchitectureRegexTimeout);

        foreach (string file in procFiles)
        {
            ValidateProcessorFile(file, module, classRegex, allowedProcessorBases, violations);
        }
    }

    private static void ValidateProcessorFile(string file, string module, Regex classRegex, string[] allowedProcessorBases, List<string> violations)
    {
        string source = File.ReadAllText(file);
        foreach (Match match in classRegex.Matches(source))
        {
            ValidateProcessorClass(match, file, module, allowedProcessorBases, violations);
        }
    }

    private static void ValidateProcessorClass(Match match, string file, string module, string[] allowedProcessorBases, List<string> violations)
    {
        string modifier = match.Groups[1].Value;
        if (modifier is "static" or "abstract")
            return;

        string className = match.Groups[2].Value;
        if (!className.Contains("Processor") && !className.Contains("Executor"))
            return;

        string baseList = match.Groups[3].Value;
        bool inheritsRequiredBase = allowedProcessorBases.Any(b =>
            Regex.IsMatch(baseList, $@"\b{b}\b", RegexOptions.CultureInvariant, ArchitectureRegexTimeout));

        if (!inheritsRequiredBase)
        {
            violations.Add($"{className} in {Path.GetFileName(file)} ({module}): processor class must inherit from SingleFileProcessorBase or MultiFileProcessorBase.");
        }
    }

    private static void ValidateModuleOptionFiles(string root, string modulesRoot, List<string> violations)
    {
        string[] allOptionFiles = Directory.GetFiles(modulesRoot, "*Options.cs", SearchOption.AllDirectories);
        foreach (string file in allOptionFiles)
        {
            string relative = NormalizeRepoPath(root, file);
            if (!relative.Contains($"/{ArchitectureApplicationDirectory}/", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"Option file {relative} must reside under an Application/ directory.");
            }
        }
    }

    private static void ValidateAuthorizedModuleDirectories(string modulesRoot, List<string> violations)
    {
        var appAndProc = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, ArchitectureProcessingDirectory };
        var authorizedByDomainAndSubmodule = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Doc"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Excel"] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                ["Markdown"] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                ["Ppt"] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                [ArchitectureSharedDirectory] = new(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, ArchitectureProcessingDirectory, ArchitectureInfrastructureDirectory },
                ["Word"] = new(appAndProc, StringComparer.OrdinalIgnoreCase)
            },
            ["File"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["BatchRename"] = new(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, ArchitectureProcessingDirectory, "Domain" }
            },
            ["Img"] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ArchitectureCompressionSubmodule] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                ["FormatConvert"] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                [ArchitectureMergeSubmodule] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                [ArchitectureSharedDirectory] = new(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, ArchitectureInfrastructureDirectory },
                ["Stitch"] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                ["ToPdf"] = new(appAndProc, StringComparer.OrdinalIgnoreCase)
            },
            ["Pdf"] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ArchitectureCompressionSubmodule] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                ["Decrypt"] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                [ArchitectureMergeSubmodule] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                ["Print"] = new(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, ArchitectureProcessingDirectory, "Ports" },
                ["Split"] = new(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, ArchitectureProcessingDirectory, ArchitecturePresentationDirectory },
                ["ToImage"] = new(appAndProc, StringComparer.OrdinalIgnoreCase),
                [ArchitectureTranslationDirectory] = new(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, ArchitectureProcessingDirectory, "Model", "Pipeline", ArchitectureTranslationDirectory }
            },
            ["Sys"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["HistoryTasks"] = new(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, ArchitecturePresentationDirectory },
                [ArchitectureSettingsModuleName] = new(StringComparer.OrdinalIgnoreCase) { ArchitectureApplicationDirectory, "Model", ArchitecturePresentationDirectory }
            }
        };

        foreach (string domainDir in Directory.GetDirectories(modulesRoot))
        {
            string domainName = Path.GetFileName(domainDir);
            if (!authorizedByDomainAndSubmodule.TryGetValue(domainName, out var allowedSubmodules))
            {
                violations.Add($"Domain {domainName} is not recognized in authorized module directory configuration.");
                continue;
            }

            ValidateDomainSubmodules(domainName, domainDir, allowedSubmodules, violations);
        }
    }

    private static void ValidateDomainSubmodules(
        string domainName,
        string domainDir,
        Dictionary<string, HashSet<string>> allowedSubmodules,
        List<string> violations)
    {
        foreach (string subDir in Directory.GetDirectories(domainDir))
        {
            string subName = Path.GetFileName(subDir);
            if (subName.Equals(ArchitectureExecutionDirectory, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"Domain {domainName} contains forbidden Execution/ directory.");
                continue;
            }

            if (!allowedSubmodules.TryGetValue(subName, out var allowedLayers))
            {
                violations.Add($"Domain {domainName} contains unauthorized submodule directory: {subName}/.");
                continue;
            }

            ValidateSubmoduleLayers(domainName, subName, subDir, allowedLayers, violations);
        }
    }

    private static void ValidateSubmoduleLayers(
        string domainName,
        string subName,
        string subDir,
        HashSet<string> allowedLayers,
        List<string> violations)
    {
        foreach (string layerDir in Directory.GetDirectories(subDir))
        {
            string layerName = Path.GetFileName(layerDir);
            if (layerName.Equals(ArchitectureExecutionDirectory, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"Submodule {domainName}/{subName} contains forbidden Execution/ directory.");
            }
            else if (!allowedLayers.Contains(layerName))
            {
                violations.Add($"Submodule {domainName}/{subName} contains unauthorized layer directory: {layerName}/.");
            }
        }
    }

    private static void ValidatePresentationReferences(string root, string modulesRoot, List<string> violations)
    {
        var forbiddenPattern = new Regex(
            @"\busing\s+Clickra\.Core\.Modules\.[A-Za-z0-9_.]+\.(?:Processing|Infrastructure|Execution)\s*;|\bClickra\.Core\.Modules\.[A-Za-z0-9_.]+\.(?:Processing|Infrastructure|Execution)\b",
            RegexOptions.Compiled,
            ArchitectureRegexTimeout);

        foreach (string presDir in Directory.EnumerateDirectories(modulesRoot, ArchitecturePresentationDirectory, SearchOption.AllDirectories))
        {
            foreach (string file in Directory.GetFiles(presDir, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                if (forbiddenPattern.IsMatch(source))
                {
                    violations.Add($"{NormalizeRepoPath(root, file)}: Presentation layer must not reference Processing or Infrastructure.");
                }
            }
        }
    }

    private static void TestPresentationForbidsHardcodedCommands()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);

        var forbiddenCommands = new HashSet<string>(
            ConversionUseCases.Commands
                .Append("batch-rename"),
            StringComparer.OrdinalIgnoreCase);

        var forbiddenIdentifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            BatchRenameCommandIdentifier,
            "CommandName",
            "CmdMergePdf",
            "CmdCompressPdf",
            "CmdImg2Pdf",
            "CmdImgMerge",
            "PngCommand",
            "JpgCommand",
            "WebpCommand",
            "GifCommand",
            "HeicCommand"
        };

        var violations = new List<string>();
        var presentationFiles = CollectPresentationSourceFiles(root);

        foreach (string file in presentationFiles)
        {
            string source = File.ReadAllText(file);
            var walker = new HardcodedCommandBranchingWalker(
                forbiddenCommands,
                forbiddenIdentifiers,
                violation => violations.Add($"{NormalizeRepoPath(root, file)}:{violation}"));
            InspectSourceTree(source, walker);
        }

        Assert.True(
            violations.Count == 0,
            "Presentation code must remain 100% polymorphic and must not contain hardcoded command branching:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));

        VerifyHardcodedCommandDetectionCoverage(forbiddenCommands, forbiddenIdentifiers);
    }

    private static void InspectSourceTree(string source, HardcodedCommandBranchingWalker walker)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        SyntaxNode astRoot = tree.GetRoot();
        walker.Visit(astRoot);

        foreach (var trivia in astRoot.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.DisabledTextTrivia)))
        {
            string disabled = trivia.ToString();
            if (string.IsNullOrWhiteSpace(disabled)) continue;
            SyntaxTree disabledTree = CSharpSyntaxTree.ParseText(
                disabled,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
            walker.Visit(disabledTree.GetRoot());
        }
    }

    private static List<string> CollectPresentationSourceFiles(string root)
    {
        var scanDirs = new List<string>
        {
            Path.Combine(root, "src", ArchitectureFluentProjectDirectory),
            Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureDashboardDirectory),
            Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureProgressDirectory),
            Path.Combine(root, "src", ArchitectureCliProjectDirectory, "UI", ArchitectureLayoutDirectory),
            Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitecturePresentationDirectory)
        };

        string modulesDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory);
        if (Directory.Exists(modulesDir))
        {
            scanDirs.AddRange(
                Directory.EnumerateDirectories(modulesDir, ArchitecturePresentationDirectory, SearchOption.AllDirectories));
        }

        var files = new List<string>();
        foreach (string dir in scanDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string norm = file.Replace('\\', '/');
                if (norm.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                    norm.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                files.Add(file);
            }
        }
        return files;
    }

    private static void VerifyHardcodedCommandDetectionCoverage(
        HashSet<string> forbiddenCommands,
        HashSet<string> forbiddenIdentifiers)
    {
        string[] testSnippets =
        {
            "class C { void M(string c) { if (c == \"md2pdf\") {} } }",
            "class C { void M(string c) { if (\"batch-rename\" == c) {} } }",
            "class C { void M(string c) { if (c != \"split-pdf\") {} } }",
            "class C { void M(string c) { if (c.Equals(\"decrypt-pdf\")) {} } }",
            "class C { void M(string c) { if (string.Equals(c, \"compress-pdf\")) {} } }",
            "class C { void M(string c) { switch(c) { case \"translate-pdf\": break; } } }",
            "class C { void M(string c) { var x = c switch { \"print-pdf\" => 1, _ => 0 }; } }",
            "class C { void M(string c) { if (c is \"pdf2img\") {} } }",
            "class C { void M(string c) { if (c is not \"img2pdf\") {} } }",
            "class C { void M(string c) { if (c is \"img-merge\" or \"img-stitch\") {} } }",
            "class C { void M(object o) { if (o is { Command: \"excel2pdf\" }) {} } }",
            "class C { int M(object o) => o switch { (\"md2pdf\", _) => 1, _ => 0 }; }",
            "class C { void M(object o) { switch (o) { case (\"split-pdf\", _): break; } } }",
            "class C { void M(string c) { if (c == MarkdownToPdfUseCase.CommandName) {} } }",
            "class C { void M(string c) { if (c == ImageFormatConvertUseCase.PngCommand) {} } }",
            $"class C {{ void M(string c) {{ if (c == {BatchRenameCommandIdentifier}) {{}} }} }}",
            $"class C {{ void M(string c) {{ if (c.Equals({BatchRenameCommandIdentifier})) {{}} }} }}",
            "#if NEVER\nclass C { void M(string c) { if (c == \"compress-pdf\") {} } }\n#endif"
        };

        foreach (string snippet in testSnippets)
        {
            var detected = new List<string>();
            var walker = new HardcodedCommandBranchingWalker(
                forbiddenCommands,
                forbiddenIdentifiers,
                detected.Add);
            InspectSourceTree(snippet, walker);

            Assert.True(
                detected.Count > 0,
                $"Guard 4 AST detection must catch hardcoded command snippet: {snippet}");
        }

        string cleanSnippet = "class C { void M(string c) { var catalog = new[] { new Item(\"md2pdf\", 1) }; if (catalog.Length > 0) {} } }";
        var cleanDetected = new List<string>();
        var cleanWalker = new HardcodedCommandBranchingWalker(
            forbiddenCommands,
            forbiddenIdentifiers,
            cleanDetected.Add);
        InspectSourceTree(cleanSnippet, cleanWalker);

        Assert.True(
            cleanDetected.Count == 0,
            "Non-branching catalog initialization must not be flagged by Guard 4.");
    }

    private sealed class HardcodedCommandBranchingWalker : CSharpSyntaxWalker
    {
        private readonly HashSet<string> _forbiddenCommands;
        private readonly HashSet<string> _forbiddenIdentifiers;
        private readonly Action<string> _onViolation;

        public HardcodedCommandBranchingWalker(
            HashSet<string> forbiddenCommands,
            HashSet<string> forbiddenIdentifiers,
            Action<string> onViolation)
        {
            _forbiddenCommands = forbiddenCommands;
            _forbiddenIdentifiers = forbiddenIdentifiers;
            _onViolation = onViolation;
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if ((node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression)) &&
                (IsForbiddenExpression(node.Left) || IsForbiddenExpression(node.Right)))
            {
                Report(node, $"Binary comparison against command '{node.ToString().Trim()}'");
            }
            base.VisitBinaryExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (IsEqualsInvocation(node, out ExpressionSyntax? forbiddenExpr))
            {
                Report(node, $"Equals invocation against command '{forbiddenExpr?.ToString().Trim()}'");
            }
            base.VisitInvocationExpression(node);
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            foreach (SwitchSectionSyntax section in node.Sections)
            {
                foreach (SwitchLabelSyntax label in section.Labels)
                {
                    if (label is CaseSwitchLabelSyntax cs && IsForbiddenExpression(cs.Value))
                    {
                        Report(label, $"Switch case against command '{cs.Value.ToString().Trim()}'");
                    }
                    else if (label is CasePatternSwitchLabelSyntax cp &&
                             ContainsForbiddenPattern(cp.Pattern, out ExpressionSyntax? expr))
                    {
                        Report(label, $"Switch pattern case against command '{expr?.ToString().Trim()}'");
                    }
                }
            }
            base.VisitSwitchStatement(node);
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            foreach (SwitchExpressionArmSyntax arm in node.Arms)
            {
                if (ContainsForbiddenPattern(arm.Pattern, out ExpressionSyntax? expr))
                {
                    Report(arm, $"Switch expression arm against command '{expr?.ToString().Trim()}'");
                }
            }
            base.VisitSwitchExpression(node);
        }

        public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            if (ContainsForbiddenPattern(node.Pattern, out ExpressionSyntax? expr))
            {
                Report(node, $"Pattern match against command '{expr?.ToString().Trim()}'");
            }
            base.VisitIsPatternExpression(node);
        }

        private bool IsEqualsInvocation(InvocationExpressionSyntax node, out ExpressionSyntax? forbiddenExpr)
        {
            forbiddenExpr = null;
            bool isEquals = false;

            if (node.Expression is MemberAccessExpressionSyntax ma &&
                string.Equals(ma.Name.Identifier.ValueText, "Equals", StringComparison.OrdinalIgnoreCase))
            {
                isEquals = true;
                if (IsForbiddenExpression(ma.Expression))
                {
                    forbiddenExpr = ma.Expression;
                    return true;
                }
            }
            else if (node.Expression is IdentifierNameSyntax id &&
                     string.Equals(id.Identifier.ValueText, "Equals", StringComparison.OrdinalIgnoreCase))
            {
                isEquals = true;
            }

            if (isEquals)
            {
                ArgumentSyntax? matchingArg = node.ArgumentList.Arguments
                    .FirstOrDefault(arg => IsForbiddenExpression(arg.Expression));
                if (matchingArg is not null)
                {
                    forbiddenExpr = matchingArg.Expression;
                    return true;
                }
            }
            return false;
        }

        private bool ContainsForbiddenPattern(PatternSyntax? pattern, out ExpressionSyntax? forbiddenExpr)
        {
            forbiddenExpr = null;
            if (pattern is null) return false;

            while (pattern is ParenthesizedPatternSyntax pp)
            {
                pattern = pp.Pattern;
            }

            if (pattern is ConstantPatternSyntax cp && IsForbiddenExpression(cp.Expression))
            {
                forbiddenExpr = cp.Expression;
                return true;
            }

            if (pattern is UnaryPatternSyntax up)
            {
                return ContainsForbiddenPattern(up.Pattern, out forbiddenExpr);
            }

            if (pattern is BinaryPatternSyntax bp)
            {
                return ContainsForbiddenPattern(bp.Left, out forbiddenExpr) ||
                       ContainsForbiddenPattern(bp.Right, out forbiddenExpr);
            }

            if (pattern is RecursivePatternSyntax rp)
            {
                return ContainsForbiddenSubpattern(rp.PropertyPatternClause?.Subpatterns, out forbiddenExpr) ||
                       ContainsForbiddenSubpattern(rp.PositionalPatternClause?.Subpatterns, out forbiddenExpr);
            }

            return false;
        }

        private bool ContainsForbiddenSubpattern(
            IEnumerable<SubpatternSyntax>? subpatterns,
            out ExpressionSyntax? forbiddenExpr)
        {
            forbiddenExpr = null;
            if (subpatterns is null) return false;

            foreach (SubpatternSyntax sub in subpatterns)
            {
                if (ContainsForbiddenPattern(sub.Pattern, out forbiddenExpr))
                    return true;
            }
            return false;
        }

        private static ExpressionSyntax Unwrap(ExpressionSyntax? expr)
        {
            while (expr is ParenthesizedExpressionSyntax parenthesized)
            {
                expr = parenthesized.Expression;
            }
            return expr!;
        }

        private bool IsForbiddenExpression(ExpressionSyntax? expr)
        {
            if (expr is null) return false;
            expr = Unwrap(expr);

            if (expr is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression) &&
                _forbiddenCommands.Contains(literal.Token.ValueText))
            {
                return true;
            }

            if (expr is IdentifierNameSyntax id && _forbiddenIdentifiers.Contains(id.Identifier.ValueText))
            {
                return true;
            }

            return expr is MemberAccessExpressionSyntax ma && IsForbiddenMemberAccess(ma);
        }

        private bool IsForbiddenMemberAccess(MemberAccessExpressionSyntax ma)
        {
            string member = ma.Name.Identifier.ValueText;
            if (_forbiddenIdentifiers.Contains(member))
                return true;

            if (ma.Expression is not IdentifierNameSyntax target)
                return false;

            string targetName = target.Identifier.ValueText;
            bool isTargetAllowed = targetName.EndsWith("UseCase", StringComparison.Ordinal) ||
                                  targetName.Equals("ConvertCommandMetadata", StringComparison.Ordinal) ||
                                  targetName.Equals("ConvertWorkspaceController", StringComparison.Ordinal);

            bool isMemberForbidden = member.EndsWith("Command", StringComparison.Ordinal) ||
                                     member.Equals("CommandName", StringComparison.Ordinal);

            return isTargetAllowed && isMemberForbidden;
        }

        private void Report(SyntaxNode node, string message)
        {
            int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            _onViolation($"line {line}: {message}");
        }
    }

    private static void TestParkedTaskOrchestrationHasSingleOwner()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string storageDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureStorageDirectory);
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
        string storageDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureStorageDirectory);
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
        string processingDir = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory, "Doc", "Markdown", ArchitectureProcessingDirectory);
        string profile = File.ReadAllText(Path.Combine(processingDir, "MarkdownRenderProfile.cs"));
        string pdf = File.ReadAllText(Path.Combine(processingDir, "MarkdownToPdfProcessor.cs"));
        string word = File.ReadAllText(Path.Combine(processingDir, "MarkdownToWordProcessor.cs"));

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
            ["src/Clickra.Core/Modules/Doc/Shared/Infrastructure/LibreOfficeEngineInstaller.cs"] = new(
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

        var violations = new List<string>();
        string coreDirectory = Path.Combine(root, "src", ArchitectureCoreProjectDirectory);
        var infrastructureDirectories = new List<string>
        {
            Path.Combine(coreDirectory, "Infrastructure")
        };
        string modulesDirectory = Path.Combine(coreDirectory, ArchitectureModulesDirectory);
        if (Directory.Exists(modulesDirectory))
        {
            infrastructureDirectories.AddRange(
                Directory.EnumerateDirectories(modulesDirectory, "Infrastructure", SearchOption.AllDirectories));
        }

        foreach (string file in infrastructureDirectories
                     .Where(Directory.Exists)
                     .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
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

        Assert.Equal(2, ReadTopLevelArguments("Localization.T(\"k\", language)", ArchitectureLocalizationCall.Length).Count);
        Assert.Equal(1, ReadTopLevelArguments("Localization.T(\"a,(b)\")", ArchitectureLocalizationCall.Length).Count);
        Assert.Equal(3, ReadTopLevelArguments("Localization.T(\"k\", Path.Combine(a, b), 'x')", ArchitectureLocalizationCall.Length).Count);
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

    private static bool IsSettingsGatewayViolation(string file, string gatewayPath)
    {
        if (string.Equals(file, gatewayPath, StringComparison.OrdinalIgnoreCase)) return false;
        if (file.Contains($"{Path.DirectorySeparatorChar}Infrastructure{Path.DirectorySeparatorChar}",
                          StringComparison.OrdinalIgnoreCase)) return false;
        return DirectSettingsStorageAccess.IsMatch(File.ReadAllText(file));
    }

    private static void TestSettingsPersistenceHasSingleApplicationGateway()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string settingsGatewayPath = Path.Combine(
            root,
            "src",
            ArchitectureCoreProjectDirectory,
            ArchitectureModulesDirectory,
            "Sys",
            ArchitectureSettingsModuleName,
            ArchitectureApplicationDirectory,
            "SettingsController.cs");
        Assert.True(File.Exists(settingsGatewayPath), "Application settings gateway must exist.");

        var violations = new List<string>();
        foreach (string directory in new[]
                 {
                     Path.Combine(root, "src", ArchitectureCliProjectDirectory),
                     Path.Combine(root, "src", ArchitectureFluentProjectDirectory),
                     Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory),
                     Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory)
                 })
        {
            violations.AddRange(Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(file => IsSettingsGatewayViolation(file, settingsGatewayPath))
                .Select(file => NormalizeRepoPath(root, file)));
        }

        string gatewaySource = File.ReadAllText(settingsGatewayPath);
        foreach (Match match in SettingsGatewayStorageAccess.Matches(gatewaySource))
        {
            string member = match.Groups["member"].Value;
            if (!AllowedSettingsGatewayMembers.Contains(member))
                violations.Add($"src/Clickra.Core/Modules/Sys/{ArchitectureSettingsModuleName}/Application/SettingsController.cs: unexpected ClickraStorage.{member} access");
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

    private static IEnumerable<string> PresentationSettingsViolations(string root)
    {
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
                    yield return $"{NormalizeRepoPath(root, file)}: raw SettingsController key access";
                if (PersistedBooleanRepresentationInPresentation.IsMatch(source))
                    yield return $"{NormalizeRepoPath(root, file)}: persisted true/false representation leaked into presentation";
            }
        }
    }

    private static void TestPresentationSettingsUseTypedApplicationAccess()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        var violations = PresentationSettingsViolations(root).ToList();

        string gateway = File.ReadAllText(Path.Combine(
            root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory, "Sys", ArchitectureSettingsModuleName, ArchitectureApplicationDirectory, "SettingsController.cs"));
        foreach (string typedContract in new[]
                 {
                     "public static string Language",
                     "public static bool QuietMode",
                     "public static bool NotificationsEnabled",
                     "public static OfficeEnginePreference OfficeEngine",
                     "public static OutputDirectoryPreference OutputDirectory"
                 }.Where(contract => !gateway.Contains(contract, StringComparison.Ordinal)))
            violations.Add($"SettingsController missing typed contract: {typedContract}");

        string presentationGateway = File.ReadAllText(Path.Combine(
            root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory, "Sys", ArchitectureSettingsModuleName, ArchitecturePresentationDirectory, "SettingsPresentationController.cs"));

        foreach (string typedContract in new[]
                 {
                     "public bool ReadBool(SettingDescriptor descriptor)",
                     "public int ReadInt(SettingDescriptor descriptor)",
                     "public void WriteBool(SettingDescriptor descriptor, bool value)",
                     "public void WriteInt(SettingDescriptor descriptor, int value)"
                 }.Where(contract => !presentationGateway.Contains(contract, StringComparison.Ordinal)))
            violations.Add($"SettingsPresentationController missing typed descriptor contract: {typedContract}");

        Assert.True(
            violations.Count == 0,
            "CLI and Fluent must consume typed Presentation settings backed by Application values instead of persisted keys/boolean encodings:" +
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
            ArchitectureModulesDirectory,
            "Sys",
            "HistoryTasks",
            ArchitectureApplicationDirectory,
            "ParkedTaskController.cs");
        Assert.True(File.Exists(gatewayPath), "Application parked-task gateway must exist.");

        var violations = new List<string>();
        foreach (string directory in new[]
                 {
                     Path.Combine(root, "src", ArchitectureCliProjectDirectory),
                     Path.Combine(root, "src", ArchitectureFluentProjectDirectory),
                     Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory),
                     Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory)
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
                violations.Add($"src/Clickra.Core/Modules/Sys/HistoryTasks/Application/ParkedTaskController.cs: unexpected ClickraStorage.{member} access");
        }

        Assert.True(
            violations.Count == 0,
            "CLI, Fluent, and application workflows must use ParkedTaskController for parked-task actions; " +
            "the gateway itself may expose only parked-task action APIs:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static readonly Regex RawPresentationStorageAccess =
        new(@"\bClickraStorage\b", RegexOptions.Compiled, ArchitectureRegexTimeout);

    private static readonly Regex GlobalPresentationStorageImport =
        new(@"\bglobal\s+using\s+Clickra\.Core\.Storage\s*;",
            RegexOptions.Compiled,
            ArchitectureRegexTimeout);

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
                if (!RawPresentationStorageAccess.IsMatch(source) &&
                    !GlobalPresentationStorageImport.IsMatch(source))
                    continue;
                violations.Add(NormalizeRepoPath(root, file));
            }
        }

        Assert.True(
            violations.Count == 0,
            "CLI and Fluent presentation must use narrow application owners instead of ClickraStorage calls, " +
            "nested storage models, or global Storage imports:" +
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
                     (ArchitectureStorageDirectory, "Clickra.Core.Storage"),
                     ("Diagnostics", "Clickra.Core.Diagnostics"),
                     ("Fonts", "Clickra.Core.Fonts")
                 })
        {
            string directory = Path.Combine(core, folder);
            Assert.True(Directory.Exists(directory), $"Expected Core namespace folder is missing: {NormalizeRepoPath(root, directory)}");
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

        foreach (string applicationFile in new[] { "ConversionTaskLifecycle.cs", "ConvertCommandRegistry.cs" })

        {
            string file = Path.Combine(core, ArchitectureApplicationDirectory, applicationFile);
            string source = File.ReadAllText(file);
            if (!source.Contains("namespace Clickra.Core.Application;", StringComparison.Ordinal))
                violations.Add($"{NormalizeRepoPath(root, file)}: expected namespace Clickra.Core.Application");
        }

        foreach ((string relativePath, string expectedNamespace) in new[]
                 {
                     (Path.Combine(ArchitecturePresentationDirectory, "Convert", "ConvertWorkspaceController.cs"), "Clickra.Core.Presentation.Convert"),
                     (Path.Combine(ArchitecturePresentationDirectory, "Convert", "ConversionExecutionController.cs"), "Clickra.Core.Presentation.Convert"),
                     (Path.Combine(ArchitecturePresentationDirectory, "Progress", "TaskProgressController.cs"), "Clickra.Core.Presentation.Progress"),
                     (Path.Combine(ArchitecturePresentationDirectory, ArchitectureDashboardDirectory, "DashboardPresentationController.cs"), "Clickra.Core.Presentation.Dashboard")

                 })
        {
            string file = Path.Combine(core, relativePath);
            Assert.True(File.Exists(file), $"Presentation owner is missing: {NormalizeRepoPath(root, file)}");
            string source = File.ReadAllText(file);
            if (!source.Contains($"namespace {expectedNamespace};", StringComparison.Ordinal))
                violations.Add($"{NormalizeRepoPath(root, file)}: expected namespace {expectedNamespace}");
        }

        Assert.True(
            violations.Count == 0,
            "Physical Core bounded contexts must keep namespaces that encode their ownership:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void TestStorageFacadeDelegatesToFocusedOwners()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string storage = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureStorageDirectory);

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
        string storage = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureStorageDirectory);
        var violations = new List<string>();

        foreach (string file in Directory.EnumerateFiles(storage, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            if (source.Contains(ArchitectureLocalizationCall, StringComparison.Ordinal))
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
            (Path.Combine(root, "src", ArchitectureCliProjectDirectory, "UI", ArchitectureLayoutDirectory, "DashboardLayout.cs"), "namespace Clickra.UI.Layout;"),
            (Path.Combine(root, "src", ArchitectureCliProjectDirectory, "UI", ArchitectureLayoutDirectory, "HistoryLayout.cs"), "namespace Clickra.UI.Layout;"),
            (Path.Combine(root, "src", ArchitectureCliProjectDirectory, "UI", ArchitectureLayoutDirectory, "VectorIcons.cs"), "namespace Clickra.UI.Layout;")
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

    private static void TestPrimaryRenderersDependOnPresentationOwners()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        var files = new List<string>();
        files.AddRange(Directory.EnumerateFiles(
            Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureDashboardDirectory),
            "*.cs",
            SearchOption.TopDirectoryOnly));

        string fluent = Path.Combine(root, "src", ArchitectureFluentProjectDirectory);
        files.AddRange(Directory.EnumerateFiles(fluent, "MainPage*.cs", SearchOption.TopDirectoryOnly));
        files.Add(Path.Combine(fluent, "TaskProgressPage.xaml.cs"));
        files.Add(Path.Combine(fluent, "DynamicSettingsController.cs"));
        files.Add(Path.Combine(fluent, "OfficeEnginePreflight.cs"));

        var prohibited = new (string Name, Regex Pattern)[]
        {
            ("raw settings application access", new(@"\bSettingsController\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("raw parked-task application access", new(@"\bParkedTaskController\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("raw history application access", new(@"\bHistoryController\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("raw storage-path application access", new(@"\bStoragePathController\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("use-case catalog orchestration", new(@"\bConversionUseCases\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("application request construction", new(@"\bnew\s+ConversionRequest\s*\(", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("command registry policy", new(@"\bConvertCommandRegistry\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("Office detector policy", new(@"\bOfficeEngineDetector\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("LibreOffice infrastructure", new(@"\bLibreOffice(?:EngineInstaller|Helper)\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout)),
            ("raw storage", new(@"\bClickraStorage\s*\.", RegexOptions.Compiled, ArchitectureRegexTimeout))
        };

        var violations = new List<string>();
        foreach (string file in files.Where(File.Exists))
        {
            string source = File.ReadAllText(file);
            foreach ((string name, Regex pattern) in prohibited)
            {
                if (pattern.IsMatch(source))
                    violations.Add($"{NormalizeRepoPath(root, file)}: {name}");
            }
        }

        string nativeDashboard = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureDashboardDirectory), "*.cs")
                .Select(File.ReadAllText));
        string nativeProgress = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureProgressDirectory), "ProgressWindow*.cs")
                .Select(File.ReadAllText));
        string fluentPrimary = string.Join(Environment.NewLine, files.Where(path => path.Contains(ArchitectureFluentProjectDirectory, StringComparison.OrdinalIgnoreCase)).Select(File.ReadAllText));

        foreach (string owner in new[] { "ConvertWorkspaceController", "HistoryPresentationController", "SettingsPresentationController", "DashboardPresentationController" })
        {
            if (!nativeDashboard.Contains(owner, StringComparison.Ordinal)) violations.Add($"Native dashboard missing Presentation owner {owner}");
            if (!fluentPrimary.Contains(owner, StringComparison.Ordinal)) violations.Add($"Fluent primary renderer missing Presentation owner {owner}");
        }
        if (!nativeProgress.Contains("TaskProgressController", StringComparison.Ordinal))
            violations.Add("Native ProgressWindow must derive semantic state from TaskProgressController");
        if (!fluentPrimary.Contains("TaskProgressController", StringComparison.Ordinal))
            violations.Add("Fluent TaskProgressPage must derive semantic state from TaskProgressController");
        if (!fluentPrimary.Contains(ArchitectureConversionExecutionName, StringComparison.Ordinal))
            violations.Add("Fluent conversion execution must delegate Application orchestration to ConversionExecutionController");

        Assert.True(
            violations.Count == 0,
            "Primary Native/Fluent renderers must render shared Presentation owners rather than own Application/technical policy:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void TestPresentationDependsInwardThroughApplication()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string presentation = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitecturePresentationDirectory);
        var violations = new List<string>();

        var forbidden = new Regex(
            @"\b(?:ClickraStorage|ClickraSettings|ConversionStatus)\b|using\s+Clickra\.Core\.(?:Storage|Processors|Infrastructure)\s*;|using\s+Clickra\.Core\.Modules\.[A-Za-z0-9_.]+\.(?:Processing|Infrastructure)\s*;",
            RegexOptions.Compiled,
            ArchitectureRegexTimeout);

        var presentationDirectories = new List<string> { presentation };
        string modulesDirectory = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory);
        if (Directory.Exists(modulesDirectory))
        {
            presentationDirectories.AddRange(
                Directory.EnumerateDirectories(modulesDirectory, ArchitecturePresentationDirectory, SearchOption.AllDirectories));
        }

        foreach (string directory in presentationDirectories)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                if (forbidden.IsMatch(source))
                    violations.Add(NormalizeRepoPath(root, file));
            }
        }

        Assert.True(
            violations.Count == 0,
            "Presentation must consume Application contracts and presentation-neutral shared types, not Storage/Processor/Infrastructure implementation details:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void TestCommandRegistryBelongsToApplication()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string application = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory, ArchitectureConvertRegistryFile);
        string oldProcessor = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureProcessorsDirectory, ArchitectureConvertRegistryFile);
        Assert.True(File.Exists(application), "ConvertCommandRegistry must be owned by Application.");
        Assert.False(File.Exists(oldProcessor), "ConvertCommandRegistry must not return to Processors.");
        Assert.True(
            File.ReadAllText(application).Contains("namespace Clickra.Core.Application;", StringComparison.Ordinal),
            "ConvertCommandRegistry must declare the Application namespace.");
    }

    private static void TestTranslationContextsStayDistinct()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string core = Path.Combine(root, "src", ArchitectureCoreProjectDirectory);
        string legacyProviderDirectory = Path.Combine(core, ArchitectureTranslationDirectory);
        string module = Path.Combine(core, ArchitectureModulesDirectory, "Pdf", ArchitectureTranslationDirectory);
        string providerDirectory = Path.Combine(module, ArchitectureTranslationDirectory);
        string legacyPdfTranslationDirectory = Path.Combine(module, "Pipeline", ArchitectureTranslationDirectory);
        string textProcessingDirectory = Path.Combine(module, "Pipeline", "TextProcessing");

        Assert.False(
            Directory.Exists(legacyProviderDirectory) &&
            Directory.EnumerateFiles(legacyProviderDirectory, "*.cs", SearchOption.AllDirectories).Any(),
            "PDF translation providers must stay inside Modules/Pdf/Translation rather than returning to Core/Translation.");
        Assert.False(
            Directory.Exists(legacyPdfTranslationDirectory) &&
            Directory.EnumerateFiles(legacyPdfTranslationDirectory, "*.cs", SearchOption.AllDirectories).Any(),
            "The ambiguous Pdf/Translation/Pipeline/Translation source context must not return.");
        Assert.True(
            Directory.Exists(textProcessingDirectory),
            "PDF translation text processing must have an explicit TextProcessing context.");

        foreach (string file in Directory.EnumerateFiles(providerDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            Assert.True(
                Regex.IsMatch(
                    source,
                    @"\bnamespace\s+Clickra\.Core\.Modules\.Pdf\.Translation\.Translation\s*(?:;|\{)",
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
                    @"\bnamespace\s+Clickra\.Core\.Modules\.Pdf\.Translation\.Pipeline\.TextProcessing\s*(?:;|\{)",
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
            .Append(Clickra.Core.Modules.File.BatchRename.Application.BatchRenameUseCase.CommandName)
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
        IConversionUseCase print = ConversionUseCases.GetRequired(PrintPdfUseCase.CommandName);
        IConversionUseCase split = ConversionUseCases.GetRequired(SplitPdfUseCase.CommandName);
        IConversionUseCase translate = ConversionUseCases.GetRequired(TranslatePdfUseCase.CommandName);
        IConversionUseCase excel = ConversionUseCases.GetRequired(ExcelToPdfUseCase.CommandName);
        IConversionUseCase ppt = ConversionUseCases.GetRequired(PptToPdfUseCase.CommandName);
        IConversionUseCase word = ConversionUseCases.GetRequired(WordToPdfUseCase.CommandName);
        IConversionUseCase pdf2img = ConversionUseCases.GetRequired(PdfToImageUseCase.CommandName);
        IConversionUseCase batchRename = ConversionUseCases.GetRequired(Clickra.Core.Modules.File.BatchRename.Application.BatchRenameUseCase.CommandName);
        Assert.True(batchRename is Clickra.Core.Modules.File.BatchRename.Application.BatchRenameUseCase,
            "batch-rename must resolve through the product-wide application use-case catalog.");
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
        Assert.True(print is PrintPdfUseCase,
            "print-pdf must resolve through the product-wide application use-case catalog.");
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
        Assert.True(pdf2img is PdfToImageUseCase,
            "pdf2img must resolve through the product-wide application use-case catalog.");
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
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(PdfToImageUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one pdf2img owner.");
        Assert.True(ConversionUseCases.Commands.Count(command =>
                command.Equals(Clickra.Core.Modules.File.BatchRename.Application.BatchRenameUseCase.CommandName, StringComparison.OrdinalIgnoreCase)) == 1,
            "The product catalog must expose exactly one batch-rename owner.");

        Assert.True(batchRename.Capabilities.Contains(ConversionCapability.BatchRename),
            "batch-rename must declare the BatchRename capability.");
        Assert.False(batchRename.GeneratesHistoryTask,
            "batch-rename must declare GeneratesHistoryTask => false.");
        Assert.True(batchRename.AcceptsAllFileExtensions,
            "batch-rename must declare AcceptsAllFileExtensions => true.");
        Assert.True(print.Capabilities.Contains(ConversionCapability.PrintOptions),
            "print-pdf must declare the PrintOptions capability.");
        Assert.True(decrypt.Capabilities.Contains(ConversionCapability.Password),
            "decrypt-pdf must declare the Password capability.");
        Assert.True(split.Capabilities.Contains(ConversionCapability.SplitPages),
            "split-pdf must declare the SplitPages capability.");
        Assert.True(markdownPdf.Capabilities.Contains(ConversionCapability.MarkdownOptions),
            "md2pdf must declare the MarkdownOptions capability.");
        Assert.True(markdownWord.Capabilities.Contains(ConversionCapability.MarkdownOptions),
            "md2word must declare the MarkdownOptions capability.");

        Assert.True(decrypt.InputRequirement == RequiredInputType.Password,
            "decrypt-pdf input requirement must be Password.");
        Assert.True(split.InputRequirement == RequiredInputType.PageRange,
            "split-pdf input requirement must be PageRange.");
        Assert.True(markdownPdf.InputRequirement == RequiredInputType.CustomOptions,
            "md2pdf input requirement must be CustomOptions.");
        Assert.True(batchRename.InputRequirement == RequiredInputType.CustomOptions,
            "batch-rename input requirement must be CustomOptions.");
        Assert.True(compress.InputRequirement == RequiredInputType.None,
            "compress-pdf input requirement must be None.");

        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.RequiresOptions(MarkdownToPdfUseCase.CommandName),
            "ConversionExecutionController.RequiresOptions must return true for md2pdf.");
        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.RequiresOptions(MarkdownToWordUseCase.CommandName),
            "ConversionExecutionController.RequiresOptions must return true for md2word.");
        Assert.False(Clickra.Core.Presentation.Convert.ConversionExecutionController.RequiresOptions(WordToPdfUseCase.CommandName),
            "ConversionExecutionController.RequiresOptions must return false for commands without option requirements.");
        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.HasCapability(DecryptPdfUseCase.CommandName, ConversionCapability.Password),
            "ConversionExecutionController.HasCapability must recognize Password on decrypt-pdf.");
        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.HasCapability(SplitPdfUseCase.CommandName, ConversionCapability.SplitPages),
            "ConversionExecutionController.HasCapability must recognize SplitPages on split-pdf.");
        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.HasCapability(PrintPdfUseCase.CommandName, ConversionCapability.PrintOptions),
            "ConversionExecutionController.HasCapability must recognize PrintOptions on print-pdf.");
        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.HasCapability(Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase.CommandName, ConversionCapability.Compression),
            "ConversionExecutionController.HasCapability must recognize Compression on compress-pdf.");
        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.HasCapability(Clickra.Core.Modules.File.BatchRename.Application.BatchRenameUseCase.CommandName, ConversionCapability.BatchRename),
            "ConversionExecutionController.HasCapability must recognize BatchRename on batch-rename.");
        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.AcceptsAllFileExtensions(Clickra.Core.Modules.File.BatchRename.Application.BatchRenameUseCase.CommandName),
            "ConversionExecutionController.AcceptsAllFileExtensions must return true for batch-rename.");
        Assert.False(Clickra.Core.Presentation.Convert.ConversionExecutionController.AcceptsAllFileExtensions(WordToPdfUseCase.CommandName),
            "ConversionExecutionController.AcceptsAllFileExtensions must return false for word2pdf.");
        Assert.False(Clickra.Core.Presentation.Convert.ConversionExecutionController.GeneratesHistoryTask(Clickra.Core.Modules.File.BatchRename.Application.BatchRenameUseCase.CommandName),
            "ConversionExecutionController.GeneratesHistoryTask must return false for batch-rename.");
        Assert.True(Clickra.Core.Presentation.Convert.ConversionExecutionController.GeneratesHistoryTask(WordToPdfUseCase.CommandName),
            "ConversionExecutionController.GeneratesHistoryTask must return true for word2pdf.");
        Assert.False(Clickra.Core.Presentation.Convert.ConversionExecutionController.HasCapability(WordToPdfUseCase.CommandName, ConversionCapability.Password),
            "ConversionExecutionController.HasCapability must return false for undeclared capability.");
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

    private static void TestBatchRenameWorkflowHasSingleExecutionOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        string nativeAdapter = File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, ArchitectureProgressDirectory, ArchitectureNativeExecutionAdapterFile));
        string cliBatchRename = File.ReadAllText(Path.Combine(root, "src", ArchitectureCliProjectDirectory, "Cli", "ClickraStartup.BatchRename.cs"));

        string nativeMethod = ExtractMethodBody(nativeAdapter, "void RunBatchRename(");
        string cliMethod = ExtractMethodBody(cliBatchRename, "void ExecuteCliPlan(");

        AssertBatchRenameExecutionRouted(nativeMethod, cliMethod);

        // Negative verification: demonstrate that direct executor/use-case calls or missing service calls are rejected
        Assert.Throws<InvalidOperationException>(() =>
            AssertBatchRenameExecutionRouted("void RunBatchRename() { /* missing service call */ }", cliMethod));
        Assert.Throws<InvalidOperationException>(() =>
            AssertBatchRenameExecutionRouted("void RunBatchRename() { BatchRenameExecutor.ExecuteAsync(); ConversionExecutionService.Execute(); }", cliMethod));
        Assert.Throws<InvalidOperationException>(() =>
            AssertBatchRenameExecutionRouted("void RunBatchRename() { BatchRenameUseCase.ExecuteAsync(); ConversionExecutionService.Execute(); }", cliMethod));
        Assert.Throws<InvalidOperationException>(() =>
            AssertBatchRenameExecutionRouted(nativeMethod, "void ExecuteCliPlan() { /* missing service call */ }"));
        Assert.Throws<InvalidOperationException>(() =>
            AssertBatchRenameExecutionRouted(nativeMethod, "void ExecuteCliPlan() { BatchRenameExecutor.ExecuteAsync(); ConversionExecutionService.Execute(); }"));
        Assert.Throws<InvalidOperationException>(() =>
            AssertBatchRenameExecutionRouted(nativeMethod, "void ExecuteCliPlan() { BatchRenameUseCase.ExecuteAsync(); ConversionExecutionService.Execute(); }"));
    }

    private static void AssertBatchRenameExecutionRouted(string nativeMethod, string cliMethod)
    {
        Assert.False(nativeMethod.Contains("BatchRenameUseCase.ExecuteAsync", StringComparison.Ordinal),
            "Native presentation must not execute BatchRenameUseCase.ExecuteAsync directly; it must delegate to ConversionExecutionService.");
        Assert.False(cliMethod.Contains("BatchRenameUseCase.ExecuteAsync", StringComparison.Ordinal),
            "CLI batch-rename execution must route through ConversionExecutionService.");

        Assert.False(nativeMethod.Contains("BatchRenameExecutor.ExecuteAsync", StringComparison.Ordinal),
            "Native presentation must not invoke BatchRenameExecutor.ExecuteAsync directly.");
        Assert.False(cliMethod.Contains("BatchRenameExecutor.ExecuteAsync", StringComparison.Ordinal),
            "CLI presentation must not invoke BatchRenameExecutor.ExecuteAsync directly.");

        Assert.True(nativeMethod.Contains("ConversionExecutionService.Execute(", StringComparison.Ordinal),
            "Native RunBatchRename method body must execute via ConversionExecutionService.Execute.");
        Assert.True(cliMethod.Contains("ConversionExecutionService.Execute(", StringComparison.Ordinal),
            "CLI ExecuteCliPlan method body must execute via ConversionExecutionService.Execute.");
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        int start = source.IndexOf(methodName, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        int openBrace = source.IndexOf('{', start);
        if (openBrace < 0) return string.Empty;
        int depth = 1;
        for (int i = openBrace + 1; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source[openBrace..(i + 1)];
            }
        }
        return source[openBrace..];
    }

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
        Assert.True(sources.PresentationExecution.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                    && sources.FluentMain.Contains(ArchitectureConversionExecutionName, StringComparison.Ordinal)
                    && sources.FluentTask.Contains(ArchitectureConversionExecutionName, StringComparison.Ordinal)
                    && nativeCatalogResolution
                    && quietCatalogResolution,
            $"Fluent surfaces must delegate {workflowName} to the shared Presentation execution owner, which resolves the Application use-case catalog; Native/headless adapters may resolve the catalog directly.");
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
        Assert.True(sources.PresentationExecution.Contains(GenericCatalogResolution, StringComparison.Ordinal)
                    && sources.FluentMain.Contains(ArchitectureConversionExecutionName, StringComparison.Ordinal)
                    && sources.FluentTask.Contains(ArchitectureConversionExecutionName, StringComparison.Ordinal)
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
            File.ReadAllText(Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureApplicationDirectory, ArchitectureConvertRegistryFile)),
            File.ReadAllText(Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitecturePresentationDirectory, "Convert", "ConversionExecutionController.cs")),
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
        Assert.True(fluentMain.Contains("execution.Prepare(command, files, commandOptions)", StringComparison.Ordinal)
                    && fluentTask.Contains("ConversionExecutionPlanPresentation executionPlan = _execution.Prepare(", StringComparison.Ordinal)
                    && fluentTask.Contains("commandOptions,", StringComparison.Ordinal)
                    && fluentTask.Contains("existingTaskId,", StringComparison.Ordinal),
            $"Both Fluent {workflowName} paths must forward the already-prompted Markdown option snapshot into the shared Presentation execution planner.");
    }

    private sealed class StubConversionUseCase(string command) : IConversionUseCase
    {
        public string Command { get; } = command;
        public IReadOnlySet<ConversionCapability> Capabilities => new HashSet<ConversionCapability>();

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

    private static void TestDomainRootsHaveNoLooseSourceFiles()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string modulesRoot = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory);
        if (!Directory.Exists(modulesRoot))
            return;

        string[] authorizedDomains = { "Doc", "File", "Img", "Pdf", "Sys" };
        var violations = new List<string>();

        foreach (string dir in Directory.EnumerateDirectories(modulesRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string domainName = Path.GetFileName(dir);
            if (!authorizedDomains.Contains(domainName, StringComparer.OrdinalIgnoreCase))
            {
                violations.Add($"Unauthorized domain directory under Modules: {domainName}");
            }
        }

        foreach (string domain in authorizedDomains)
        {
            string domainDir = Path.Combine(modulesRoot, domain);
            if (!Directory.Exists(domainDir)) continue;

            string[] looseFiles = Directory.EnumerateFiles(domainDir, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(f => NormalizeRepoPath(root, f))
                .ToArray();

            if (looseFiles.Length > 0)
            {
                violations.Add($"Domain root '{domain}' contains loose source files: {string.Join(", ", looseFiles)}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Defense Line 1: Domain roots must be clean with no loose source files:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void TestSharedSubmodulesForbidConversionUseCasesAndCrossDomainLeakage()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string modulesRoot = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory);
        if (!Directory.Exists(modulesRoot))
            return;

        var violations = new List<string>();
        CheckSharedDirectoriesDoNotImplementUseCases(modulesRoot, root, violations);
        CheckCrossDomainSharedReferences(modulesRoot, root, violations);

        Assert.True(
            violations.Count == 0,
            "Defense Line 2: Shared sub-modules must forbid conversion use cases and cross-domain leakage:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void CheckSharedDirectoriesDoNotImplementUseCases(string modulesRoot, string root, List<string> violations)
    {
        string[] sharedDirs = Directory.EnumerateDirectories(modulesRoot, ArchitectureSharedDirectory, SearchOption.AllDirectories).ToArray();
        foreach (string sharedDir in sharedDirs)
        {
            foreach (string file in Directory.EnumerateFiles(sharedDir, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                if (Regex.IsMatch(source, @":\s*([A-Za-z0-9_.,<>\s]*\b)?IConversionUseCase\b", RegexOptions.CultureInvariant, ArchitectureRegexTimeout))
                {
                    violations.Add($"{NormalizeRepoPath(root, file)}: Shared sub-directory must not implement IConversionUseCase.");
                }
            }
        }
    }

    private static void CheckCrossDomainSharedReferences(string modulesRoot, string root, List<string> violations)
    {
        var conversionDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Doc", "File", "Img", "Pdf" };
        var allowlistedCrossDomainSharedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src/Clickra.Core/Modules/Sys/Settings/Application/LibreOfficeSettingsController.cs"
        };

        string[] domainDirs = Directory.EnumerateDirectories(modulesRoot, "*", SearchOption.TopDirectoryOnly).ToArray();
        foreach (string domainDir in domainDirs)
        {
            string domainName = Path.GetFileName(domainDir);
            foreach (string file in Directory.EnumerateFiles(domainDir, "*.cs", SearchOption.AllDirectories))
            {
                string relative = NormalizeRepoPath(root, file);
                string source = File.ReadAllText(file);
                foreach (string otherDomainDir in domainDirs)
                {
                    string otherDomain = Path.GetFileName(otherDomainDir);
                    if (string.Equals(domainName, otherDomain, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string forbiddenShared = $"Clickra.Core.Modules.{otherDomain}.Shared";
                    if (source.Contains(forbiddenShared, StringComparison.Ordinal)
                        && (conversionDomains.Contains(domainName) || !allowlistedCrossDomainSharedReferences.Contains(relative)))
                    {
                        violations.Add($"{relative}: domain '{domainName}' must not reference foreign domain shared namespace '{forbiddenShared}'.");
                    }
                }
            }
        }
    }

    private static void TestFunctionalSubmodulesAreSymmetrical()
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepositoryRootMissingMessage);
        string modulesRoot = Path.Combine(root, "src", ArchitectureCoreProjectDirectory, ArchitectureModulesDirectory);
        if (!Directory.Exists(modulesRoot))
            return;

        var violations = new List<string>();
        var conversionDomains = new[] { "Doc", "File", "Img", "Pdf" };

        foreach (string domain in conversionDomains)
        {
            string domainDir = Path.Combine(modulesRoot, domain);
            if (!Directory.Exists(domainDir)) continue;

            foreach (string subDir in Directory.EnumerateDirectories(domainDir, "*", SearchOption.TopDirectoryOnly))
            {
                string subName = Path.GetFileName(subDir);
                if (string.Equals(subName, ArchitectureSharedDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;

                ValidateFunctionalSubmodule(domain, subName, subDir, violations);
            }
        }

        Assert.True(
            violations.Count == 0,
            "Defense Line 3: Functional sub-modules must be symmetrical and follow golden structure:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void ValidateFunctionalSubmodule(string domain, string subName, string subDir, List<string> violations)
    {
        string appDir = Path.Combine(subDir, ArchitectureApplicationDirectory);
        string procDir = Path.Combine(subDir, ArchitectureProcessingDirectory);
        string execDir = Path.Combine(subDir, ArchitectureExecutionDirectory);

        if (Directory.Exists(execDir))
        {
            violations.Add($"{domain}/{subName}: contains forbidden Execution/ directory.");
        }

        if (!Directory.Exists(appDir))
        {
            violations.Add($"{domain}/{subName}: missing mandatory {ArchitectureApplicationDirectory}/ directory.");
        }

        if (!Directory.Exists(procDir))
        {
            violations.Add($"{domain}/{subName}: missing mandatory {ArchitectureProcessingDirectory}/ directory.");
        }

        if (Directory.Exists(appDir))
        {
            var useCases = Directory.EnumerateFiles(appDir, "*UseCase*.cs", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).Contains("Base"))
                .Select(f => Path.GetFileName(f))
                .ToList();

            if (useCases.Count == 0)
            {
                violations.Add($"{domain}/{subName}: functional sub-module must have at least one conversion UseCase.");
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
