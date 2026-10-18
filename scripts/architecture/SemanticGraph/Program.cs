using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

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

string outPath = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(root, ".tmp-architecture-graph", "semantic-msbuild.json");
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
await File.WriteAllTextAsync(
    outPath,
    JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine(outPath);
Console.WriteLine($"projects={projects.Length} files={allFiles.Count} edges={edges.Count} sccs={sccs.Count}");
foreach ((string projectName, int errorCount) in projectErrors)
    Console.WriteLine($"{projectName}: semantic-errors={errorCount}");
Console.WriteLine($"workspace-failures={workspaceFailures.Count}");
foreach (string failure in workspaceFailures.Take(20))
    Console.WriteLine($"workspace: {failure}");
foreach (List<string> component in sccs.Take(20))
    Console.WriteLine($"SCC[{component.Count}]: {string.Join(" | ", component)}");

return projectErrors.Values.Any(count => count != 0) || workspaceFailures.Count != 0 ? 1 : 0;

static Dictionary<string, HashSet<string>> CollectDeclarationFiles(
    IEnumerable<Compilation> compilations,
    Func<string, string> rel,
    Func<string, bool> isProductionSource)
{
    var declarationFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    void RecordSymbol(ISymbol symbol)
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

    void WalkType(INamedTypeSymbol type)
    {
        RecordSymbol(type);
        foreach (ISymbol member in type.GetMembers())
        {
            RecordSymbol(member);
            if (member is INamedTypeSymbol nested)
                WalkType(nested);
        }
    }

    void WalkNamespace(INamespaceSymbol ns)
    {
        foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
            WalkNamespace(child);

        foreach (INamedTypeSymbol type in ns.GetTypeMembers())
            WalkType(type);
    }

    foreach (Compilation compilation in compilations)
        WalkNamespace(compilation.Assembly.GlobalNamespace);

    return declarationFiles;
}

static void ExtractProjectDependencies(
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
}

static bool IsInterestingSyntax(SyntaxNode node) =>
    node is IdentifierNameSyntax or
            GenericNameSyntax or
            MemberAccessExpressionSyntax or
            InvocationExpressionSyntax or
            ObjectCreationExpressionSyntax or
            ImplicitObjectCreationExpressionSyntax or
            AttributeSyntax;

static ISymbol? ResolveSymbolDefinition(SemanticModel model, SyntaxNode node)
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

static void RecordDependencyEdges(
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

static Dictionary<string, HashSet<string>> BuildAdjacency(
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

static class TarjanScc
{
    public static List<List<string>> FindComponents(
        Dictionary<string, HashSet<string>> adjacency,
        HashSet<string> allFiles)
    {
        var indexByNode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lowLink = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        var sccs = new List<List<string>>();
        int index = 0;

        void StrongConnect(string node)
        {
            indexByNode[node] = index;
            lowLink[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (string target in adjacency[node])
            {
                if (!indexByNode.ContainsKey(target))
                {
                    StrongConnect(target);
                    lowLink[node] = Math.Min(lowLink[node], lowLink[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLink[node] = Math.Min(lowLink[node], indexByNode[target]);
                }
            }

            if (lowLink[node] != indexByNode[node])
                return;

            var component = new List<string>();
            while (stack.Count > 0)
            {
                string current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
                if (current.Equals(node, StringComparison.OrdinalIgnoreCase))
                    break;
            }

            if (component.Count > 1)
            {
                component.Sort(StringComparer.OrdinalIgnoreCase);
                sccs.Add(component);
            }
        }

        foreach (string file in allFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Where(f => !indexByNode.ContainsKey(f)))
        {
            StrongConnect(file);
        }

        sccs.Sort((left, right) =>
        {
            int sizeCompare = right.Count.CompareTo(left.Count);
            return sizeCompare != 0
                ? sizeCompare
                : StringComparer.OrdinalIgnoreCase.Compare(left[0], right[0]);
        });

        return sccs;
    }
}
