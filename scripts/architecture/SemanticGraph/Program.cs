using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Clickra.Architecture.SemanticGraph;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            await Console.Error.WriteLineAsync("usage: SemanticGraph <repo-root> [output-json]");
            return 2;
        }

        string root = Path.GetFullPath(args[0]);
        string src = Path.Combine(root, "src");

        MSBuildLocator.RegisterDefaults();

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = "Release"
        };

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create(properties);
        var workspaceFailures = new List<string>();
        workspace.RegisterWorkspaceFailedHandler(eventArgs =>
        {
            workspaceFailures.Add($"{eventArgs.Diagnostic.Kind}: {eventArgs.Diagnostic.Message}");
        });

        string[] projectPaths =
        {
            Path.Combine(src, "Clickra.Core", "Clickra.Core.csproj"),
            Path.Combine(src, "Clickra.CLI", "Clickra.csproj"),
            Path.Combine(src, "Clickra.Fluent", "Clickra.Fluent.csproj"),
            Path.Combine(src, "ClickraShell", "ClickraShell.csproj"),
            Path.Combine(src, "ClickraLauncher", "ClickraLauncher.csproj")
        };

        foreach (string projectPath in projectPaths)
        {
            if (!File.Exists(projectPath))
            {
                await Console.Error.WriteLineAsync($"missing project: {projectPath}");
                return 3;
            }

            await workspace.OpenProjectAsync(projectPath);
        }

        Solution solution = workspace.CurrentSolution;
        Project[] projects = solution.Projects
            .Where(project => project.FilePath is not null &&
                projectPaths.Contains(Path.GetFullPath(project.FilePath), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        string Rel(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

        bool IsProductionSourcePath(string path)
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase))
                return false;
            return !full.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !full.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }

        var compilations = new Dictionary<ProjectId, Compilation>();
        var projectErrors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Project project in projects)
        {
            Compilation compilation = await project.GetCompilationAsync()
                ?? throw new InvalidOperationException($"Compilation unavailable for {project.Name}.");
            compilations[project.Id] = compilation;
            projectErrors[project.Name] = compilation.GetDiagnostics()
                .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        var declarationFiles = CollectDeclarationFiles(compilations.Values, Rel, IsProductionSourcePath);

        var edges = new Dictionary<(string Source, string Target), HashSet<string>>();
        var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Project project in projects)
        {
            Compilation compilation = compilations[project.Id];
            ExtractProjectDependencies(compilation, declarationFiles, edges, allFiles, Rel, IsProductionSourcePath);
        }

        Dictionary<string, HashSet<string>> adjacency = BuildAdjacency(allFiles, edges.Keys);
        List<List<string>> sccs = TarjanScc.FindComponents(adjacency, allFiles);

        string outPath = args.Length == 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(root, ".tmp-architecture-graph", "semantic-msbuild.json");

        await WriteOutputAsync(outPath, allFiles, edges, sccs, projectErrors, workspaceFailures);
        PrintSummary(outPath, projects.Length, allFiles.Count, edges.Count, sccs, projectErrors, workspaceFailures);

        return projectErrors.Values.Any(count => count != 0) || workspaceFailures.Count != 0 ? 1 : 0;
    }

    private static async Task WriteOutputAsync(
        string outPath,
        HashSet<string> allFiles,
        Dictionary<(string Source, string Target), HashSet<string>> edges,
        List<List<string>> sccs,
        Dictionary<string, int> projectErrors,
        List<string> workspaceFailures)
    {
        var output = new
        {
            files = allFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            edges = edges
                .OrderBy(edge => edge.Key.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.Key.Target, StringComparer.OrdinalIgnoreCase)
                .Select(edge => new
                {
                    source = edge.Key.Source,
                    target = edge.Key.Target,
                    symbols = edge.Value.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray()
                })
                .ToArray(),
            stronglyConnectedComponents = sccs,
            diagnostics = new
            {
                projectErrors,
                workspaceFailures
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(
            outPath,
            JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void PrintSummary(
        string outPath,
        int projectCount,
        int fileCount,
        int edgeCount,
        List<List<string>> sccs,
        Dictionary<string, int> projectErrors,
        List<string> workspaceFailures)
    {
        Console.WriteLine(outPath);
        Console.WriteLine($"projects={projectCount} files={fileCount} edges={edgeCount} sccs={sccs.Count}");
        foreach ((string projectName, int errorCount) in projectErrors)
            Console.WriteLine($"{projectName}: semantic-errors={errorCount}");
        Console.WriteLine($"workspace-failures={workspaceFailures.Count}");
        foreach (string failure in workspaceFailures.Take(20))
            Console.WriteLine($"workspace: {failure}");
        foreach (List<string> component in sccs.Take(20))
            Console.WriteLine($"SCC[{component.Count}]: {string.Join(" | ", component)}");
    }

    private static Dictionary<string, HashSet<string>> CollectDeclarationFiles(
        IEnumerable<Compilation> compilations,
        Func<string, string> rel,
        Func<string, bool> isProductionSource)
    {
        var declarationFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (Compilation compilation in compilations)
            WalkNamespace(compilation.Assembly.GlobalNamespace, declarationFiles, rel, isProductionSource);

        return declarationFiles;
    }

    private static void WalkNamespace(
        INamespaceSymbol ns,
        Dictionary<string, HashSet<string>> declarationFiles,
        Func<string, string> rel,
        Func<string, bool> isProductionSource)
    {
        foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
            WalkNamespace(child, declarationFiles, rel, isProductionSource);

        foreach (INamedTypeSymbol type in ns.GetTypeMembers())
            WalkType(type, declarationFiles, rel, isProductionSource);
    }

    private static void WalkType(
        INamedTypeSymbol type,
        Dictionary<string, HashSet<string>> declarationFiles,
        Func<string, string> rel,
        Func<string, bool> isProductionSource)
    {
        RecordSymbol(type, declarationFiles, rel, isProductionSource);
        foreach (ISymbol member in type.GetMembers())
        {
            RecordSymbol(member, declarationFiles, rel, isProductionSource);
            if (member is INamedTypeSymbol nested)
                WalkType(nested, declarationFiles, rel, isProductionSource);
        }
    }

    private static void RecordSymbol(
        ISymbol symbol,
        Dictionary<string, HashSet<string>> declarationFiles,
        Func<string, string> rel,
        Func<string, bool> isProductionSource)
    {
        if (symbol is INamespaceSymbol)
            return;

        string? declarationId = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
        if (string.IsNullOrWhiteSpace(declarationId))
            return;

        foreach (Location location in symbol.Locations)
        {
            string? filePath = location.SourceTree?.FilePath;
            if (filePath is null || !isProductionSource(filePath))
                continue;

            if (!declarationFiles.TryGetValue(declarationId, out HashSet<string>? files))
            {
                files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                declarationFiles[declarationId] = files;
            }
            files.Add(rel(filePath));
        }
    }

    private static void ExtractProjectDependencies(
        Compilation compilation,
        Dictionary<string, HashSet<string>> declarationFiles,
        Dictionary<(string Source, string Target), HashSet<string>> edges,
        HashSet<string> allFiles,
        Func<string, string> rel,
        Func<string, bool> isProductionSource)
    {
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (string.IsNullOrWhiteSpace(tree.FilePath) || !isProductionSource(tree.FilePath))
                continue;

            string sourcePath = rel(tree.FilePath);
            allFiles.Add(sourcePath);
            SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            ProcessSyntaxTreeNodes(tree, model, sourcePath, declarationFiles, edges);
        }
    }

    private static void ProcessSyntaxTreeNodes(
        SyntaxTree tree,
        SemanticModel model,
        string sourcePath,
        Dictionary<string, HashSet<string>> declarationFiles,
        Dictionary<(string Source, string Target), HashSet<string>> edges)
    {
        foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
        {
            if (!IsInterestingSyntax(node))
                continue;

            ISymbol? definition = ResolveSymbolDefinition(model, node);
            if (definition is null)
                continue;

            string? declarationId = DocumentationCommentId.CreateDeclarationId(definition);
            if (string.IsNullOrWhiteSpace(declarationId) ||
                !declarationFiles.TryGetValue(declarationId, out HashSet<string>? targets))
            {
                continue;
            }

            RecordDependencyEdges(sourcePath, targets, definition, edges);
        }
    }

    private static bool IsInterestingSyntax(SyntaxNode node) =>
        node is IdentifierNameSyntax or
                GenericNameSyntax or
                MemberAccessExpressionSyntax or
                InvocationExpressionSyntax or
                ObjectCreationExpressionSyntax or
                ImplicitObjectCreationExpressionSyntax or
                AttributeSyntax;

    private static ISymbol? ResolveSymbolDefinition(SemanticModel model, SyntaxNode node)
    {
        SymbolInfo info = model.GetSymbolInfo(node);
        ISymbol? symbol = info.Symbol;
        if (symbol is null && info.CandidateReason != CandidateReason.None)
            symbol = info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null;
        if (symbol is null)
            return null;

        if (symbol is IAliasSymbol alias)
            symbol = alias.Target;
        if (symbol is IMethodSymbol method && method.ReducedFrom is not null)
            symbol = method.ReducedFrom;
        if (symbol is INamespaceSymbol or ILocalSymbol or IParameterSymbol or ILabelSymbol or IRangeVariableSymbol)
            return null;

        return symbol.OriginalDefinition;
    }

    private static void RecordDependencyEdges(
        string sourcePath,
        HashSet<string> targets,
        ISymbol definition,
        Dictionary<(string Source, string Target), HashSet<string>> edges)
    {
        string symbolDisplay = definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        foreach (string targetPath in targets)
        {
            if (sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var key = (sourcePath, targetPath);
            if (!edges.TryGetValue(key, out HashSet<string>? symbols))
            {
                symbols = new HashSet<string>(StringComparer.Ordinal);
                edges[key] = symbols;
            }
            symbols.Add(symbolDisplay);
        }
    }

    private static Dictionary<string, HashSet<string>> BuildAdjacency(
        HashSet<string> allFiles,
        IEnumerable<(string Source, string Target)> edgeKeys)
    {
        var adjacency = allFiles.ToDictionary(
            file => file,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach ((string sourcePath, string targetPath) in edgeKeys)
        {
            if (adjacency.ContainsKey(sourcePath) && adjacency.ContainsKey(targetPath))
                adjacency[sourcePath].Add(targetPath);
        }

        return adjacency;
    }
}

internal static class TarjanScc
{
    public static List<List<string>> FindComponents(
        Dictionary<string, HashSet<string>> adjacency,
        HashSet<string> allFiles)
    {
        var context = new TarjanContext(adjacency);

        foreach (string file in allFiles.Where(f => !context.IndexByNode.ContainsKey(f)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            context.StrongConnect(file);
        }

        context.Components.Sort((left, right) =>
        {
            int sizeCompare = right.Count.CompareTo(left.Count);
            return sizeCompare != 0
                ? sizeCompare
                : StringComparer.OrdinalIgnoreCase.Compare(left[0], right[0]);
        });

        return context.Components;
    }

    private sealed class TarjanContext(Dictionary<string, HashSet<string>> adjacency)
    {
        public Dictionary<string, int> IndexByNode { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> LowLink { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OnStack { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Stack<string> Stack { get; } = new();
        public List<List<string>> Components { get; } = [];
        public int Index { get; set; }

        public void StrongConnect(string node)
        {
            IndexByNode[node] = Index;
            LowLink[node] = Index;
            Index++;
            Stack.Push(node);
            OnStack.Add(node);

            ExploreNeighbors(node);

            if (LowLink[node] == IndexByNode[node])
                PopComponent(node);
        }

        private void ExploreNeighbors(string node)
        {
            foreach (string target in adjacency[node])
            {
                if (!IndexByNode.ContainsKey(target))
                {
                    StrongConnect(target);
                    LowLink[node] = Math.Min(LowLink[node], LowLink[target]);
                }
                else if (OnStack.Contains(target))
                {
                    LowLink[node] = Math.Min(LowLink[node], IndexByNode[target]);
                }
            }
        }

        private void PopComponent(string node)
        {
            var component = new List<string>();
            while (Stack.Count > 0)
            {
                string current = Stack.Pop();
                OnStack.Remove(current);
                component.Add(current);
                if (current.Equals(node, StringComparison.OrdinalIgnoreCase))
                    break;
            }

            if (component.Count > 1)
            {
                component.Sort(StringComparer.OrdinalIgnoreCase);
                Components.Add(component);
            }
        }
    }
}
