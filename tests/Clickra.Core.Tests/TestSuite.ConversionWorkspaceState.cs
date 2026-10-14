using Clickra.Core.Presentation.Convert;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string WorkspacePdfFile = "a.pdf";

    public static void RegisterConversionWorkspaceStateTests(TestRunner runner)
    {
        runner.Run("Conversion workspace keeps explicit compatible command selections", TestConversionWorkspaceKeepsCompatibleSelection);
        runner.Run("Conversion workspace reroutes incompatible imports to the shared default", TestConversionWorkspaceReroutesIncompatibleImport);
        runner.Run("Conversion workspace enforces minimum files and run state", TestConversionWorkspaceStartEligibility);
        runner.Run("Conversion workspace preserves command choice when files are cleared", TestConversionWorkspaceClearPreservesCommand);
        runner.Run("Conversion workspace exposes a live read-only file view", TestConversionWorkspaceSelectedFilesView);
    }

    private static void TestConversionWorkspaceKeepsCompatibleSelection()
    {
        var state = new ConvertWorkspaceController();
        state.AddFiles(new[] { WorkspacePdfFile, "b.pdf" });
        Assert.True(state.TrySelectCommand("merge-pdf"), "A two-PDF selection must accept merge-pdf.");

        state.AddFiles(new[] { "c.pdf", "A.PDF" });

        Assert.Equal(3, state.SelectedFiles.Count);
        Assert.Equal("merge-pdf", state.SelectedCommand ?? "");
        Assert.True(state.CanStart, "A compatible explicit command with files must be startable.");
    }

    private static void TestConversionWorkspaceReroutesIncompatibleImport()
    {
        var state = new ConvertWorkspaceController();
        Assert.False(state.TrySelectCommand("not-a-command"), "Unknown commands must never become selected workspace commands.");
        state.AddFiles(new[] { WorkspacePdfFile });
        Assert.True(state.TrySelectCommand("compress-pdf"), "A single PDF must accept compress-pdf.");

        state.AddFiles(new[] { "b.png" });

        Assert.True(state.SelectedCommand is null,
            "Mixed file types must clear an incompatible command when the shared default is undefined.");
        Assert.False(state.CanStart, "A mixed selection with no shared default must not be startable.");
    }

    private static void TestConversionWorkspaceStartEligibility()
    {
        var state = new ConvertWorkspaceController();
        state.AddFiles(new[] { WorkspacePdfFile });

        Assert.False(state.TrySelectCommand("merge-pdf"), "merge-pdf must reject a single PDF because it requires multiple files.");
        Assert.True(state.TrySelectCommand("compress-pdf"), "compress-pdf must accept a single PDF.");
        Assert.True(state.CanStart, "A compatible idle workspace must be startable.");

        state.BeginRun();
        Assert.True(state.IsRunning, "BeginRun must mark the workspace as running.");
        Assert.False(state.CanStart, "A running workspace must not expose another start action.");

        state.EndRun();
        Assert.True(state.CanStart, "EndRun must restore start eligibility for an unchanged valid selection.");
    }

    private static void TestConversionWorkspaceClearPreservesCommand()
    {
        var state = new ConvertWorkspaceController();
        state.AddFiles(new[] { "a.md" });
        Assert.True(state.TrySelectCommand("md2word"), "A Markdown selection must accept md2word.");

        state.ClearFiles();

        Assert.Equal(0, state.SelectedFiles.Count);
        Assert.Equal("md2word", state.SelectedCommand ?? "");
        Assert.False(state.CanStart, "An empty selection must not be startable even when a command remains selected.");
        Assert.True(state.IsCommandCompatible("md2word"), "Commands remain selectable while the workspace has no files.");
    }

    private static void TestConversionWorkspaceSelectedFilesView()
    {
        var state = new ConvertWorkspaceController();
        IReadOnlyList<string> selectedFiles = state.SelectedFiles;

        Assert.False(selectedFiles is List<string>,
            "SelectedFiles must not expose the mutable List backing instance.");
        Assert.True(selectedFiles is ICollection<string> collection && collection.IsReadOnly,
            "SelectedFiles must expose a collection view that rejects direct mutation.");

        state.AddFiles(new[] { WorkspacePdfFile });

        Assert.Equal(1, selectedFiles.Count);
        Assert.Equal(WorkspacePdfFile, selectedFiles[0]);
    }
}
