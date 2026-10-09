using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterMarkdownUiIntegrationTests(TestRunner runner)
    {
        runner.RunGuard("Markdown NativeAOT prompt: start, cancel, close and resume preserve one decision", TestMarkdownNativePromptLifecycle);
        runner.RunGuard("Markdown templates: both UIs revalidate imports at confirmation and isolate layout source", TestMarkdownTemplateSelectionContracts);
        runner.RunGuard("Dashboard file import: picker and drag-drop share registry routing", TestDashboardFileImportRouting);
        runner.RunGuard("Dashboard sticky footer: viewport hit precedes scrolled content and swallows inactive hits", TestMarkdownStickyFooterHitOrder);
        runner.RunGuard("Fluent Markdown dialog: both entry points await a cancellable options dialog", TestMarkdownFluentDialogContract);
        runner.RunGuard("Shell Markdown commands: menu keys, arguments, and icons remain aligned", TestMarkdownShellMenuContract);
    }

    private static string MarkdownSource(string relative)
    {
        string root = FindRepoRoot() ?? throw new TestSkippedException(RepoRootNotFoundMessage);
        return File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void RequireInOrder(string source, params string[] tokens)
    {
        int index = 0;
        foreach (string token in tokens)
        {
            int next = source.IndexOf(token, index, StringComparison.Ordinal);
            Assert.True(next >= index, $"Expected ordered operation '{token}'.");
            index = next + token.Length;
        }
    }

    private static void TestMarkdownNativePromptLifecycle()
    {
        string window = MarkdownSource("src/Clickra.CLI/Progress/ProgressWindow.cs");
        string options = MarkdownSource("src/Clickra.CLI/Progress/ProgressWindow.MarkdownOptions.cs");
        string controls = MarkdownSource("src/Clickra.CLI/Progress/ProgressWindow.Controls.cs");
        RequireInOrder(window, "_isPromptingMarkdownOptions = existingTaskId is null", "&& _commandOptions is null", "&& IsMarkdownCommand(command)");
        RequireInOrder(window, "if (!_isPromptingMarkdownOptions)", "StartProcessingThread(_hwnd)");
        RequireInOrder(window, "private void StartProcessingThread", "if (_processingStarted) return;", "_processingStarted = true;", "bgThread.Start()");
        RequireInOrder(window, "private void ResolveMarkdownDecision", "if (_markdownDecisionResolved) return;", "_markdownDecisionResolved = true;", "_markdownDecision?.Invoke(startConversion)");
        RequireInOrder(options, "private void StartMarkdownConversion", "ResolveMarkdownDecision(true);", "_isPromptingMarkdownOptions = false;", "StartProcessingThread(hwnd)");
        RequireInOrder(options, "new RectangleF(386, 398, 98, 32)", "ResolveMarkdownDecision(false);", "DestroyWindow(hwnd)");
        RequireInOrder(controls, "private IntPtr HandleClose", "if (_isPromptingMarkdownOptions)", "ResolveMarkdownDecision(false);", "DestroyWindow(hwnd)", "return IntPtr.Zero;");
        RequireInOrder(controls, "private IntPtr HandleLButtonDown", "if (_isPromptingMarkdownOptions && HandleMarkdownOptionsClick", "if (_isPromptingVisualSplitter", "HandleProgressClick");
    }

    private static void TestMarkdownTemplateSelectionContracts()
    {
        string options = MarkdownSource("src/Clickra.CLI/Progress/ProgressWindow.MarkdownOptions.cs");
        string fluent = MarkdownSource("src/Clickra.Fluent/Controls/FluentDialogs.cs");
        RequireInOrder(options, "if (_markdownLayoutSourceIndex == 1 && string.IsNullOrWhiteSpace(_markdownTemplatePath))", "md_options_template_required", "return true;");
        RequireInOrder(options, "if (_markdownLayoutSourceIndex == 1)", "MarkdownTemplateSource.Load(_markdownTemplatePath!)", "_markdownTemplatePath = null;", "md_options_template_invalid", "return true;");
        Assert.True(options.Contains("StartMarkdownConversion(hwnd, _markdownLayoutSourceIndex == 1 ? _markdownTemplatePath : null)", StringComparison.Ordinal),
            "Choosing a built-in style must not forward a stale template path.");
        RequireInOrder(fluent, "layoutSource.SelectionChanged", "stylePanel.Visibility = useTemplate ? Visibility.Collapsed : Visibility.Visible;", "templatePanel.Visibility = useTemplate ? Visibility.Visible : Visibility.Collapsed;");
        RequireInOrder(fluent, "dialog.PrimaryButtonClick", "if (layoutSource?.SelectedIndex != 1) return;", "if (templatePath is null)", "args.Cancel = true;", "MarkdownTemplateSource.Load(templatePath)", "catch", "args.Cancel = true;");
        Assert.True(fluent.Contains("string? selectedTemplatePath = layoutSource?.SelectedIndex == 1 ? templatePath : null;", StringComparison.Ordinal),
            "Built-in layout must ignore a previously selected DOCX template.");
    }

    private static void TestDashboardFileImportRouting()
    {
        string convert = MarkdownSource("src/Clickra.CLI/Dashboard/DashboardWindow.Convert.cs");
        string clicks = MarkdownSource("src/Clickra.CLI/Dashboard/DashboardWindow.Events.Click.cs");

        Assert.True(clicks.Contains("OpenFiles(hwnd, GetSupportedFilesFilter(), title)", StringComparison.Ordinal)
                    && clicks.Contains("ImportFiles(chosen);", StringComparison.Ordinal),
            "The dashboard picker must use the shared supported-file filter and import routing.");
        Assert.False(clicks.Contains("const string allFilter", StringComparison.Ordinal),
            "The dashboard picker must not keep a second hardcoded extension list.");
        Assert.True(convert.Contains("HandleDroppedFiles(List<string> files) => ImportFiles(files);", StringComparison.Ordinal),
            "Drag/drop must use the same file-import routing as the picker.");
        Assert.True(convert.Contains("ConvertCommands", StringComparison.Ordinal)
                    && convert.Contains("SelectMany(command => command.Extensions)", StringComparison.Ordinal)
                    && convert.Contains("Distinct(StringComparer.OrdinalIgnoreCase)", StringComparison.Ordinal),
            "The supported-file picker filter must derive from the command registry.");
        Assert.True(convert.Contains("ext is \".md\" or \".markdown\"", StringComparison.Ordinal)
                    && convert.Contains("GetCommandIndex(\"md2pdf\")", StringComparison.Ordinal),
            "Markdown imports must default to Markdown-to-PDF when no compatible command is already selected.");
        Assert.True(convert.Contains("ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)", StringComparison.Ordinal),
            "Image import routing must follow the shared image-extension registry, including newly supported formats.");
    }

    private static void TestMarkdownStickyFooterHitOrder()
    {
        string clicks = MarkdownSource("src/Clickra.CLI/Dashboard/DashboardWindow.Events.Click.cs");
        string hit = MarkdownSource("src/Clickra.CLI/Dashboard/DashboardWindow.HitTesting.cs");
        string hover = MarkdownSource("src/Clickra.CLI/Dashboard/DashboardWindow.Events.cs");
        RequireInOrder(clicks, "if (HandleScrollbarClick(hwnd, mouseX, mouseY)) return;", "IsInsideConvertStickyFooter(mouseX, mouseY, logW, logH, contentX)", "HitTestConvertStickyAction(mouseX, mouseY, logW, logH, contentX)", "if (fixedElement == 19)", "HandleConvertClick(hwnd, fixedElement);", "return;", "int adjMouseX =", "HitTest(hwnd, adjMouseX, adjMouseY)");
        RequireInOrder(hover, "IsInsideConvertStickyFooter(mouseX, mouseY, logW, logH, contentX)", "HitTestConvertStickyAction(mouseX, mouseY, logW, logH, contentX)", "HitTest(hwnd, adjMouseX, adjMouseY)");
        Assert.True(hit.Contains("DashboardLayout.ConvertStickyFooterRect", StringComparison.Ordinal)
                    && hit.Contains("DashboardLayout.ConvertStickyStartButtonRect", StringComparison.Ordinal),
            "Fixed footer and fixed start button must use their shared geometry.");
    }

    private static void TestMarkdownFluentDialogContract()
    {
        string main = MarkdownSource("src/Clickra.Fluent/MainPage.xaml.cs");
        string task = MarkdownSource("src/Clickra.Fluent/TaskProgressPage.xaml.cs");
        string fluent = MarkdownSource("src/Clickra.Fluent/Controls/FluentDialogs.cs");
        Assert.True(main.Contains("PromptMarkdownPdfOptionsAsync", StringComparison.Ordinal)
                    && task.Contains("PromptMarkdownPdfOptionsAsync", StringComparison.Ordinal),
            "Both Fluent conversion entry points must prompt for Markdown settings.");
        RequireInOrder(fluent, "dialog.PrimaryButtonClick", "trackDialog?.Invoke(dialog);", "if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;", "MarkdownPdfOptions.Create(theme, paperValue, textValue, codeValue, selectedTemplatePath)");
        RequireInOrder(fluent, "templateBrowse.Click +=", "FileTypeFilter.Add(\".docx\")", "PickSingleFileAsync()", "if (file is null) return;", "MarkdownTemplateSource.Load(file.Path)");
    }

    private static void TestMarkdownShellMenuContract()
    {
        string shell = MarkdownSource("src/ClickraShell/ComMethods.cs");
        Match keys = Regex.Match(shell, @"MenuKeys\s*=\s*\{(?<items>[^}]+)\}", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        Match commands = Regex.Match(shell, @"SubArgs\s*=\s*\{(?<items>[^}]+)\}", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        Match icons = Regex.Match(shell, @"IconFiles\s*=\s*\{(?<items>[^}]+)\}", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        Assert.True(keys.Success && commands.Success && icons.Success, "Shell metadata arrays must exist.");
        static string[] Items(Match match) => Regex.Matches(match.Groups["items"].Value, "\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(item => item.Groups[1].Value).ToArray();
        string[] keyItems = Items(keys);
        string[] commandItems = Items(commands);
        string[] iconItems = Items(icons);
        Assert.Equal(commandItems.Length, keyItems.Length);
        Assert.Equal(commandItems.Length, iconItems.Length);
        foreach (var (command, key, icon) in new[] { ("md2pdf", "Menu_Md2Pdf", "menu-md2pdf.ico"), ("md2word", "Menu_Md2Word", "menu-word2pdf.ico") })
        {
            int index = Array.IndexOf(commandItems, command);
            Assert.True(index >= 0, $"Explorer must expose {command}.");
            Assert.Equal(key, keyItems[index]);
            Assert.Equal(icon, iconItems[index]);
        }
        RequireInOrder(shell, "StringBuilder sb = new StringBuilder();", "sb.Append(SubArgs[idx]);", "var files = GetFiles(psi);", "foreach (var f in files)");
        Assert.True(shell.Contains("sb.Append(\" \\\"\").Append(f).Append(\"\\\"\");", StringComparison.Ordinal),
            "Explorer must quote each selected file path when forwarding a Markdown command.");
    }
}
