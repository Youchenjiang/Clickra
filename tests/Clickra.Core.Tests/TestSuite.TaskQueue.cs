using System;
using System.IO;
using System.Linq;
using System.Threading;
using Clickra.Core;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string CmdSplitPdf = "split-pdf";
    private const string CmdDecryptPdf = "decrypt-pdf";
    private const string ParkReason = "Waiting for input";
    private const string SettingParkedRetention = "ParkedTaskRetention";
    private const string TestInDir = @"C:\in";
    private const string TestOutDir = @"C:\out";
    private const string FileA = @"\a.pdf";
    private const string FileA1 = @"\a1.pdf;";
    private const string FileA2 = @"\a2.pdf";
    private const string FluentProjectDirectory = "Clickra.Fluent";
    private const string RepoRootNotFoundMessage = "Could not locate the repository root from the test output directory.";
    private const string FluentMainPageFile = "MainPage.xaml.cs";
    private const string CliProjectDirectory = "Clickra.CLI";
    private const string DashboardDirectory = "Dashboard";
    private const string DashboardEventsClickFile = "DashboardWindow.Events.Click.cs";
    private const string DashboardHistoryPaintFile = "DashboardWindow.Paint.History.cs";
    public static void RegisterTaskQueueTests(TestRunner runner)
    {
        runner.Run("Task queue: concurrent tasks keep independent progress files",
            TestConcurrentTasksKeepIndependentProgressFiles);
        runner.Run("Task queue: completing a task leaves the active queue and writes history",
            TestCompletingTaskLeavesQueueAndWritesHistory);
        runner.Run("Task queue: deleting a task removes its progress file",
            TestDeletingTaskRemovesProgressFile);
        runner.Run("Task queue: parking moves a task out of active into parked with next index",
            TestParkingMovesTaskToParkedWithIndex);
        runner.Run("Task queue: resuming a parked task reuses its identity and index",
            TestResumingParkedTaskReusesIdentity);
        runner.Run("Task queue: parked retention days come from the setting (0 = unlimited)",
            TestParkedRetentionDaysFromSetting);
        runner.Run("Task queue: SetTaskInProgress refreshes the owning pid (resume safety)",
            TestSetTaskInProgressRefreshesPid);
        runner.Run("Task queue: active tasks whose owner process died are pruned as Canceled",
            TestDeadPidTaskPrunedAsAbandoned);
        runner.Run("Task queue: legacy active.tmp is preserved and queue orders newest first",
            TestLegacyActiveTmpPreserved);
        runner.Run("Task queue: cancelling a parked task records one canceled line and removes it",
            TestCancellingParkedTaskRecordsCanceledLine);
        runner.Run("Fluent History page exposes resume and cancel for parked conversions",
            TestFluentParkedTaskEntryPoint);
        runner.Run("Fluent settings page exposes the parked retention control",
            TestFluentParkedRetentionControl);
        runner.Run("CLI Dashboard settings page exposes the parked retention control",
            TestCliDashboardParkedRetentionControl);
        runner.Run("Parked retention: both settings pages share one range",
            TestParkedRetentionRangeIsShared);
        runner.Run("Parked retention: the accessor clamps to the range both settings pages can set",
            TestParkedRetentionAccessorClampsToSharedRange);
        runner.Run("Task queue: parked retention calculation handles unlimited, active, expiring-soon and expired states",
            TestParkedRetentionInfoCalculation);
        runner.Run("Fluent History page renders expiration days and alerts user when parked tasks expire soon",
            TestFluentParkedExpirationUiContract);
        runner.Run("CLI Dashboard History shows every parked task with its own remaining retention",
            TestCliDashboardParkedTaskVisibility);
    }

    private static void TestCancellingParkedTaskRecordsCanceledLine()
    {
        string a = ClickraStorage.StartTask(CmdDecryptPdf, 2, TestInDir + FileA1 + TestInDir + FileA2);
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            ClickraStorage.ParkTask(a, ParkReason, 1);
            Assert.True(ClickraStorage.GetParkedTasks().Any(t => t.Id == a), "Task must be parked before it can be cancelled.");

            int linesBefore = ClickraStorage.GetHistory(100).Count;
            ClickraStorage.CancelParkedTask(a);
            var after = ClickraStorage.GetHistory(100);

            Assert.True(after.Count == linesBefore + 1, $"Cancel must append exactly one history line, got {after.Count - linesBefore}.");
            Assert.True(after.Any(h => h.Command == CmdDecryptPdf && !h.IsSuccess && ClickraStorage.IsUserCanceledReason(h.ErrorMessage)),
                "Cancelling a parked task must be recorded as cancelled, not as a failure or a success.");
            Assert.True(ClickraStorage.IsUserCanceledReason(ClickraStorage.CanceledReason) && ClickraStorage.IsUserCanceledReason(ClickraStorage.LegacyUserAbortedReason),
                "Both the shared and the CLI's legacy cancel markers must be recognised as cancellations.");
            Assert.False(ClickraStorage.IsUserCanceledReason(null) || ClickraStorage.IsUserCanceledReason("boom"),
                "Ordinary failures must not be mistaken for cancellations.");
            Assert.True(ClickraStorage.GetParkedTasks().All(t => t.Id != a), "Cancelled task must leave the parked list.");
            Assert.True(ClickraStorage.GetTask(a) == null, "Cancelled task file must be removed so it cannot be resumed later.");

            // Cancelling again (already removed) must stay a no-op instead of writing more history.
            ClickraStorage.CancelParkedTask(a);
            Assert.True(ClickraStorage.GetHistory(100).Count == after.Count, "Cancelling a removed task must not write another history line.");
        }
        finally { ClickraStorage.DeleteTask(a); }
    }

    /// <summary>The park toast promises the History page can resume or cancel a parked conversion;
    /// this guards the entry point so the promise cannot silently become a dead end again.</summary>
    private static void TestFluentParkedTaskEntryPoint()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string code = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, FluentMainPageFile));
        string xaml = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml"));

        Assert.True(xaml.Contains("ParkedTasksSection", StringComparison.Ordinal), "The History page must render a parked-conversions section.");
        Assert.True(code.Contains("ClickraStorage.GetParkedTasks", StringComparison.Ordinal), "The History page must list parked conversions.");
        Assert.True(code.Contains("ClickraStorage.CancelParkedTask", StringComparison.Ordinal), "The History page must offer cancel for parked conversions.");
        Assert.True(code.Contains("OpenTaskProgressWindow($\"resume {", StringComparison.Ordinal), "Resume must go through the shared resume entry point.");
        Assert.True(code.Contains("ClickraStorage.IsUserCanceledReason", StringComparison.Ordinal), "Cancelled rows must be recognised by the shared history marker, including the CLI's legacy one.");

        // TaskProgressPage.TryParseResume only accepts a task whose status is still Parked, so the
        // UI has to open the resume window before anything flips the status; otherwise the entry
        // point silently does nothing. Guard the method body rather than the whole file.
        int resumeStart = code.IndexOf("private void ResumeParkedTask", StringComparison.Ordinal);
        Assert.True(resumeStart >= 0, "ResumeParkedTask must exist on the History page.");
        int resumeEnd = code.IndexOf("private ", resumeStart + 1, StringComparison.Ordinal);
        string resumeBody = resumeEnd > resumeStart ? code[resumeStart..resumeEnd] : code[resumeStart..];
        Assert.True(resumeBody.Contains("OpenTaskProgressWindow", StringComparison.Ordinal), "Resume must open the shared task window.");
        Assert.False(resumeBody.Contains("SetTaskInProgress", StringComparison.Ordinal),
            "Resume must not mark the task InProgress first; TaskProgressPage only accepts a Parked task.");
        foreach (string key in new[] { "task_parked_title", "task_parked_desc", "task_parked_cancel_confirm", "fluent_task_resume", "fluent_status_canceled" })
        {
            Assert.True(code.Contains(key, StringComparison.Ordinal), $"{key} must be rendered by the parked-conversion UI.");
        }
    }

    /// <summary>A parked conversion silently disappears once it ages past its retention, so the user
    /// must be able to see and change that window. The control has to read the value through the same
    /// accessor the pruner uses, otherwise the number on screen can drift from the actual behaviour.</summary>
    private static void TestFluentParkedRetentionControl()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string code = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, FluentMainPageFile));
        string xaml = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml"));

        Assert.True(xaml.Contains("x:Name=\"ParkedRetentionBox\"", StringComparison.Ordinal) && xaml.Contains("<NumberBox", StringComparison.Ordinal),
            "The settings page must offer a numeric control for the parked-conversion retention.");
        Assert.True(code.Contains("ParkedRetentionBox.Value = ClickraStorage.GetParkedRetentionDays()", StringComparison.Ordinal),
            "The retention control must show the same value the task pruner enforces.");
        Assert.True(code.Contains("ParkedRetentionBox.ValueChanged += OnParkedRetentionChanged", StringComparison.Ordinal),
            "Editing the retention control must persist the setting.");
        Assert.True(code.Contains("ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention", StringComparison.Ordinal),
            "Changing the retention control must write the ParkedTaskRetention setting.");
        Assert.False(code.Contains("const string ParkedRetentionSettingKey", StringComparison.Ordinal),
            "The key must come from the ClickraSettings registry, not a local constant.");

        foreach (string key in new[] { "setting_parked_ttl_title", "setting_parked_ttl_desc" })
        {
            Assert.True(code.Contains(key, StringComparison.Ordinal), $"{key} must be rendered by the retention control.");
        }

        // Two full-width cards now share the settings grid, so the layout must give a spanning card its
        // own row; the old fixed i/2 pairing would have drawn it on top of the LibreOffice card.
        int layoutStart = code.IndexOf("private void ApplySettingsResponsiveLayout", StringComparison.Ordinal);
        Assert.True(layoutStart >= 0, "ApplySettingsResponsiveLayout must exist.");
        int layoutEnd = code.IndexOf("\n    private ", layoutStart + 1, StringComparison.Ordinal);
        string layoutBody = layoutEnd > layoutStart ? code[layoutStart..layoutEnd] : code[layoutStart..];
        Assert.True(layoutBody.Contains("Grid.GetColumnSpan", StringComparison.Ordinal),
            "The settings layout must place ColumnSpan=2 cards on their own row.");

        string[] fullWidthCards = xaml.Split('\n')
            .Where(l => l.Contains("Grid.ColumnSpan=\"2\"", StringComparison.Ordinal) && l.Contains("<Grid ", StringComparison.Ordinal))
            .ToArray();
        Assert.True(fullWidthCards.Length == 2,
            $"Exactly two settings cards span both columns (retention and LibreOffice), found {fullWidthCards.Length}.");
    }

    /// <summary>The Win32 CLI Dashboard settings page must also expose parked retention
    /// controls, using the central ClickraSettings.ParkedTaskRetention key and clamping to [0, 365].</summary>
    private static void TestCliDashboardParkedRetentionControl()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string paintCode = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, "DashboardWindow.Paint.Settings.cs"));
        string clickCode = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, DashboardEventsClickFile));

        // Must render the section title and description using localization keys
        foreach (string key in new[] { "setting_parked_ttl_title", "setting_parked_ttl_desc" })
        {
            Assert.True(paintCode.Contains(key, StringComparison.Ordinal),
                $"The CLI settings paint file must render {key}.");
        }

        // Must read current retention via ClickraStorage.GetParkedRetentionDays()
        Assert.True(paintCode.Contains("ClickraStorage.GetParkedRetentionDays()", StringComparison.Ordinal),
            "CLI dashboard settings must read the retention setting via ClickraStorage.GetParkedRetentionDays().");

        // Must offer both stepper hit targets and the five preset IDs through the shared preset loop.
        Assert.True(paintCode.Contains("AddHitRect(90,", StringComparison.Ordinal) &&
                    paintCode.Contains("AddHitRect(91,", StringComparison.Ordinal),
            "CLI settings must register both retention stepper hit targets.");
        foreach (string preset in new[] { "(0, 92,", "(3, 93,", "(defaultDays, 94,", "(14, 95,", "(30, 96," })
        {
            Assert.True(paintCode.Contains(preset, StringComparison.Ordinal),
                $"CLI settings must declare retention preset {preset}.");
        }
        Assert.True(paintCode.Contains("AddHitRect(elemId,", StringComparison.Ordinal),
            "CLI settings must register each preset element ID with the shared hit-target loop.");

        foreach (int id in Enumerable.Range(90, 7))
        {
            Assert.True(clickCode.Contains($"case {id}:", StringComparison.Ordinal),
                $"CLI settings click handler must route retention element {id}.");
        }
        Assert.True(clickCode.Contains("element >= 90 && element <= 96", StringComparison.Ordinal),
            "IsSettingsElement must route the entire retention control ID range.");

        // Must write using the central registry constant ClickraSettings.ParkedTaskRetention
        Assert.True(clickCode.Contains("ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention", StringComparison.Ordinal),
            "Click handler must persist retention using ClickraSettings.ParkedTaskRetention.");

        // The default preset must come from the registry rather than embedding seven days locally.
        Assert.True(paintCode.Contains("GetDefaultParkedRetentionDays()", StringComparison.Ordinal) &&
                    clickCode.Contains("ClickraSettings.GetDefaultInt(ClickraSettings.ParkedTaskRetention)", StringComparison.Ordinal),
            "CLI retention default must derive from the registered ParkedTaskRetention default.");

        // Must clamp through the centralized range used by both CLI and Fluent.
        Assert.True(clickCode.Contains("ClickraSettings.MinParkedRetentionDays", StringComparison.Ordinal) &&
                    clickCode.Contains("ClickraSettings.MaxParkedRetentionDays", StringComparison.Ordinal),
            "Retention click handler must clamp through the centralized range.");

        // No local hardcoded settings-key literal; localization-key constants are allowed.
        Assert.False(clickCode.Contains("\"ParkedTaskRetention\"", StringComparison.Ordinal) ||
                     paintCode.Contains("\"ParkedTaskRetention\"", StringComparison.Ordinal),
            "ParkedTaskRetention must come from ClickraSettings, not a local string literal.");
    }

    private static void TestParkedRetentionRangeIsShared()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string fluentCode = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, FluentMainPageFile));
        string fluentXaml = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml"));
        string cliClick = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, DashboardEventsClickFile));

        Assert.True(ClickraSettings.MaxParkedRetentionDays > 0, "The shared retention bound must be positive.");
        Assert.Equal(ClickraSettings.MaxParkedTaskRetentionDays, ClickraSettings.MaxParkedRetentionDays);
        foreach ((string name, string source) in new[] { ("Fluent", fluentCode), ("CLI dashboard", cliClick) })
        {
            Assert.False(source.Contains("365", StringComparison.Ordinal),
                $"{name} must not keep a second copy of the retention bound.");
        }

        Assert.True(fluentCode.Contains("ParkedRetentionBox.Minimum = MinParkedRetentionDays", StringComparison.Ordinal),
            "The Fluent control must set its runtime minimum from the shared bound.");
        Assert.True(fluentCode.Contains("ParkedRetentionBox.Maximum = MaxParkedRetentionDays", StringComparison.Ordinal),
            "The Fluent control must set its runtime maximum from the shared bound.");
        Assert.True(fluentXaml.Contains("x:Name=\"ParkedRetentionBox\"", StringComparison.Ordinal) &&
                    !fluentXaml.Contains("Minimum=\"0\" Maximum=\"365\"", StringComparison.Ordinal),
            "The Fluent markup must not keep a second copy of the retention bounds.");
        Assert.True(cliClick.Contains("ClickraSettings.MinParkedRetentionDays", StringComparison.Ordinal) &&
                    cliClick.Contains("ClickraSettings.MaxParkedRetentionDays", StringComparison.Ordinal),
            "The CLI retention click handler must use the shared retention bounds.");
    }

    private static void TestParkedRetentionAccessorClampsToSharedRange()
    {
        int max = ClickraSettings.MaxParkedTaskRetentionDays;
        (string Stored, int Expected)[] cases =
        {
            ("0", 0),
            ("-5", 0),
            ("7", 7),
            ($"{max}", max),
            ($"{max + 1}", max),
            ("99999", max),
            ("abc", 7)
        };

        try
        {
            foreach ((string stored, int expected) in cases)
            {
                ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, stored);
                int days = ClickraStorage.GetParkedRetentionDays();
                Assert.True(days == expected, $"Stored '{stored}' must read back as {expected} days, got {days}.");
                Assert.True(days >= 0 && days <= max,
                    $"Stored '{stored}' produced {days}, which the settings pages cannot show or set.");
            }
        }
        finally
        {
            ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, ClickraSettings.DefaultParkedTaskRetention);
        }
    }
    /// <summary>Unit test verifying that parked retention calculations correctly identify
    /// unlimited retention, remaining days, expiring-soon alerts, and expiration.</summary>
    private static void TestParkedRetentionInfoCalculation()
    {
        // 1. Unlimited retention (0 days)
        var unlimited = ClickraStorage.CalculateRetentionInfo(0, TimeSpan.FromDays(100));
        Assert.True(unlimited.IsUnlimited, "Retention <= 0 must be flagged as unlimited.");
        Assert.False(unlimited.IsExpiringSoon, "Unlimited retention cannot expire soon.");
        Assert.False(unlimited.HasExpired, "Unlimited retention cannot be expired.");

        // 2. Active retention with multiple days remaining
        var active = ClickraStorage.CalculateRetentionInfo(7, TimeSpan.FromDays(2));
        Assert.False(active.IsUnlimited, "Active 7-day retention is not unlimited.");
        Assert.Equal(5, active.RemainingDays);
        Assert.False(active.IsExpiringSoon, "5 days remaining is not expiring soon.");
        Assert.False(active.HasExpired, "5 days remaining is not expired.");

        // 3. Exactly one day remaining: show the singular day state, not "less than 1 day".
        var oneDay = ClickraStorage.CalculateRetentionInfo(7, TimeSpan.FromDays(6));
        Assert.Equal(1, oneDay.RemainingDays);
        Assert.False(oneDay.IsExpiringSoon, "Exactly 24 hours left is not less than one day.");
        Assert.False(oneDay.HasExpired, "Exactly one day remaining is still active.");

        // 4. Expiring soon (strictly less than 24 hours remaining)
        var expiringSoon = ClickraStorage.CalculateRetentionInfo(7, TimeSpan.FromDays(6.3));
        Assert.False(expiringSoon.IsUnlimited, "Active retention is not unlimited.");
        Assert.Equal(1, expiringSoon.RemainingDays);
        Assert.True(expiringSoon.IsExpiringSoon, "< 24 hours left must be flagged as expiring soon.");
        Assert.False(expiringSoon.HasExpired, "Still within retention window.");

        // 5. Exactly at the retention deadline: expiration and pruning share this boundary.
        var atDeadline = ClickraStorage.CalculateRetentionInfo(7, TimeSpan.FromDays(7));
        Assert.Equal(0, atDeadline.RemainingDays);
        Assert.True(atDeadline.HasExpired, "Age equal to retentionDays must be expired.");

        // 6. Expired (past retention limit)
        var expired = ClickraStorage.CalculateRetentionInfo(7, TimeSpan.FromDays(7.2));
        Assert.False(expired.IsUnlimited, "Active retention is not unlimited.");
        Assert.Equal(0, expired.RemainingDays);
        Assert.False(expired.IsExpiringSoon, "Expired tasks are expired, not merely expiring soon.");
        Assert.True(expired.HasExpired, "Age > retentionDays must be flagged as expired.");
    }

    /// <summary>Contract test ensuring that the Fluent History page displays remaining expiration
    /// and uses proper localization keys and visual alerts when tasks are expiring soon.</summary>
    private static void TestFluentParkedExpirationUiContract()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string fluentCode = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, FluentMainPageFile));
        string cliHistory = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory, DashboardHistoryPaintFile));
        string storageCode = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "ClickraStorage.ActiveRecord.cs"));

        Assert.True(fluentCode.Contains("ClickraStorage.GetParkedRetentionInfo", StringComparison.Ordinal),
            "Fluent History page must read task retention info via ClickraStorage.GetParkedRetentionInfo.");

        foreach (string key in new[]
        {
            "task_parked_ttl_days",
            "task_parked_ttl_days_one",
            "task_parked_ttl_expiring_soon",
            "task_parked_ttl_unlimited",
            "task_parked_ttl_expired",
            "task_parked_expiring_warning",
            "task_parked_badge_expiring"
        })
        {
            string owner = key is "task_parked_expiring_warning" or "task_parked_badge_expiring"
                ? fluentCode
                : storageCode;
            Assert.True(owner.Contains(key, StringComparison.Ordinal),
                $"{key} must be referenced by the surface that renders it.");
        }

        Assert.True(storageCode.Contains("DescribeParkedRetention", StringComparison.Ordinal),
            "ClickraStorage must expose one formatter for parked-task retention text.");
        Assert.True(fluentCode.Contains("ClickraStorage.DescribeParkedRetention", StringComparison.Ordinal),
            "The Fluent History row must render the deadline via the shared formatter.");
        Assert.True(cliHistory.Contains("ClickraStorage.DescribeParkedRetention", StringComparison.Ordinal),
            "The CLI History row must render the deadline via the shared formatter.");

        foreach ((string name, string source) in new[] { ("Fluent", fluentCode), ("CLI dashboard", cliHistory) })
        {
            foreach (string ttlKey in new[]
            {
                "task_parked_ttl_days",
                "task_parked_ttl_days_one",
                "task_parked_ttl_unlimited",
                "task_parked_ttl_expired",
                "task_parked_ttl_expiring_soon"
            })
            {
                Assert.False(source.Contains(ttlKey, StringComparison.Ordinal),
                    $"{name} must not duplicate shared deadline wording for {ttlKey}.");
            }
        }

        Assert.True(fluentCode.Contains("info.IsExpiringSoon", StringComparison.Ordinal) &&
                    fluentCode.Contains("info.HasExpired", StringComparison.Ordinal),
            "MainPage must alert for both expiring-soon and expired parked tasks.");
    }

    private static void TestCliDashboardParkedTaskVisibility()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string dir = Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory);
        string paint = File.ReadAllText(Path.Combine(dir, DashboardHistoryPaintFile));
        string lifecycle = File.ReadAllText(Path.Combine(dir, "DashboardWindow.Lifecycle.cs"));

        Assert.True(lifecycle.Contains("ClickraStorage.GetParkedTasks()", StringComparison.Ordinal),
            "The dashboard must load parked conversions with the history snapshot.");
        Assert.True(paint.Contains("ClickraStorage.GetParkedRetentionInfo", StringComparison.Ordinal),
            "Each parked row must carry its own remaining retention.");
        Assert.True(paint.Contains("task_parked_title", StringComparison.Ordinal),
            "The parked block needs its localized heading.");
        void AssertPaintContains(string token, string failure) =>
            Assert.True(paint.Contains(token, StringComparison.Ordinal), failure);

        AssertPaintContains("task_parked_desc",
            "The parked block must show its description when no tasks are expiring.");
        Assert.True(paint.Contains("task_parked_expiring_warning", StringComparison.Ordinal),
            "Parked rows about to be pruned must raise the aggregate warning.");
        AssertPaintContains("task_parked_badge_expiring",
            "Parked tasks that are expiring soon must render the expiring badge.");
        AssertPaintContains("info.IsExpiringSoon",
            "CLI dashboard must check IsExpiringSoon to highlight expiring tasks.");
        AssertPaintContains("fluent_task_file_index",
            "CLI dashboard must display the file index matching Fluent format.");
        string parkedDetails = MethodBody(paint, "private static void DrawParkedTaskDetails(");
        Assert.True(parkedDetails.Contains("DrawHistoryRowText(g, displayText, fileX, ttlX, currentY, s, suppressWhenNarrow: true)", StringComparison.Ordinal),
            "Parked task details must only render when space remains before the right-aligned retention label.");
        string sharedRowText = MethodBody(paint, "private static void DrawHistoryRowText(");
        int widthGuard = sharedRowText.IndexOf("if (suppressWhenNarrow && maxW <= 20) return;", StringComparison.Ordinal);
        int truncateCall = sharedRowText.IndexOf("UIHelper.TruncateFileName", StringComparison.Ordinal);
        int drawCall = sharedRowText.IndexOf("g.DrawString(displayText", StringComparison.Ordinal);
        Assert.True(widthGuard >= 0 && truncateCall > widthGuard && drawCall > truncateCall,
            "Shared history row text must preserve the narrow-row guard before truncation and drawing.");
        Assert.True(paint.Contains("DrawParkedQueue(g,", StringComparison.Ordinal),
            "The History page must call the parked block.");

        string layout = File.ReadAllText(Path.Combine(dir, "DashboardWindow.cs"));
        int startYStart = layout.IndexOf("static int GetHistoryListStartY()", StringComparison.Ordinal);
        Assert.True(startYStart >= 0, "GetHistoryListStartY must exist.");
        int startYEnd = layout.IndexOf(';', startYStart);
        string startYBody = startYEnd > startYStart ? layout[startYStart..startYEnd] : layout[startYStart..];
        Assert.True(startYBody.Contains("ParkedBlockHeight()", StringComparison.Ordinal),
            "GetHistoryListStartY must count the parked block's height.");
        Assert.True(startYBody.Contains("HistoryRowStride", StringComparison.Ordinal),
            "GetHistoryListStartY must derive active rows from the shared stride.");

        foreach (string file in new[] { DashboardHistoryPaintFile, "DashboardWindow.HitTesting.cs", DashboardEventsClickFile, "DashboardWindow.Events.cs" })
        {
            string source = File.ReadAllText(Path.Combine(dir, file));
            Assert.True(source.Contains("GetHistoryListStartY()", StringComparison.Ordinal),
                $"{file} must take the persisted-history start from GetHistoryListStartY().");
        }

        foreach (string file in new[] { DashboardHistoryPaintFile, "DashboardWindow.HitTesting.cs", DashboardEventsClickFile, "DashboardWindow.Events.cs", "DashboardWindow.cs" })
        {
            string source = File.ReadAllText(Path.Combine(dir, file));
            Assert.False(source.Contains("GetActiveHistoryCount", StringComparison.Ordinal),
                $"{file} still computes the history start separately.");
            Assert.False(source.Contains("* 52", StringComparison.Ordinal),
                $"{file} still hardcodes the queue row stride.");
        }
    }

    private static void CleanupActiveTasks()
    {
        foreach (var task in ClickraStorage.GetActiveTasks())
            ClickraStorage.DeleteTask(task.Id);
    }

    private static void TestConcurrentTasksKeepIndependentProgressFiles()
    {
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + FileA);
        string b = ClickraStorage.StartTask(CmdDecryptPdf, 2, TestInDir + "\\b1.pdf;" + TestInDir + "\\b2.pdf");
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            ClickraStorage.SetTaskInProgress(b);
            var active = ClickraStorage.GetActiveTasks();
            Assert.True(active.Count == 2, $"Expected 2 active tasks, got {active.Count}.");
            Assert.True(active.Any(t => t.Id == a && t.Command == CmdSplitPdf), "Task A missing from the queue.");
            Assert.True(active.Any(t => t.Id == b && t.Command == CmdDecryptPdf), "Task B missing from the queue.");
            ClickraStorage.SetTaskIndex(a, 1);
            ClickraStorage.SetTaskIndex(b, 3);
            var ta = ClickraStorage.GetTask(a);
            var tb = ClickraStorage.GetTask(b);
            Assert.True(ta.HasValue && ta.Value.CurrentIndex == 1, $"Task A CurrentIndex should be 1, got {ta?.CurrentIndex}.");
            Assert.True(tb.HasValue && tb.Value.CurrentIndex == 3, $"Task B CurrentIndex should be 3, got {tb?.CurrentIndex}.");
            Assert.True(ClickraStorage.GetActiveTasks().First(t => t.Id == a).CurrentIndex == 1,
                "Queue view of A lost its own index.");
            Assert.True(ClickraStorage.GetActiveTasks().First(t => t.Id == b).CurrentIndex == 3,
                "Queue view of B lost its own index.");
        }
        finally { CleanupActiveTasks(); }
    }

    private static void TestCompletingTaskLeavesQueueAndWritesHistory()
    {
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + FileA);
        string b = ClickraStorage.StartTask(CmdDecryptPdf, 1, TestInDir + "\\b.pdf");
        try
        {
            ClickraStorage.CompleteTask(a, CmdSplitPdf, new ClickraStorage.CompleteTaskRequest
            {
                StartTime = "2026-08-16 12:00:00",
                IsSuccess = true,
                ElapsedMs = 1234,
                InputPaths = TestInDir + FileA,
                OutputPath = TestOutDir + "\\a_split.pdf"
            });
            var active = ClickraStorage.GetActiveTasks();
            Assert.True(active.All(t => t.Id != a), "Completed task A must leave the active queue.");
            Assert.True(active.Any(t => t.Id == b), "Task B must stay in the queue while A completes.");
            var finishedA = ClickraStorage.GetTask(a);
            Assert.True(finishedA.HasValue && finishedA.Value.Status == ConversionStatus.Success,
                "Completed task A should keep its Success status file.");
            var history = ClickraStorage.GetHistory(10);
            Assert.True(history.Any(h => h.Command == CmdSplitPdf && h.IsSuccess && h.OutputPath == TestOutDir + "\\a_split.pdf"),
                "History line missing for the completed task A.");
        }
        finally { ClickraStorage.DeleteTask(a); ClickraStorage.DeleteTask(b); }
    }

    private static void TestDeletingTaskRemovesProgressFile()
    {
        string a = ClickraStorage.StartTask("merge-pdf", 2, TestInDir + "\\x.pdf;" + TestInDir + "\\y.pdf");
        Assert.True(ClickraStorage.GetTask(a) != null, "Task should be readable right after StartTask.");
        ClickraStorage.DeleteTask(a);
        Assert.True(ClickraStorage.GetTask(a) == null, "Task must not be readable after DeleteTask.");
        Assert.True(ClickraStorage.GetActiveTasks().All(t => t.Id != a), "Deleted task must not appear in the queue.");
    }

    private static void TestParkingMovesTaskToParkedWithIndex()
    {
        string a = ClickraStorage.StartTask(CmdDecryptPdf, 2, TestInDir + FileA1 + TestInDir + FileA2);
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            ClickraStorage.ParkTask(a, ParkReason, 1);
            var active = ClickraStorage.GetActiveTasks();
            Assert.True(active.All(t => t.Id != a), "Parked task must leave the active queue.");
            var parked = ClickraStorage.GetParkedTasks();
            var entry = parked.FirstOrDefault(t => t.Id == a);
            Assert.True(entry.Id == a, "Parked task missing from GetParkedTasks.");
            Assert.True(entry.Status == ConversionStatus.Parked, "Parked task status must be Parked.");
            Assert.True(entry.ErrorMessage == ParkReason, $"Park reason lost: '{entry.ErrorMessage}'.");
            Assert.True(entry.CurrentIndex == 1, $"Next index should be 1, got {entry.CurrentIndex}.");
            var history = ClickraStorage.GetHistory(10);
            Assert.True(history.All(h => h.Command != CmdDecryptPdf), "Parking must not write a history line.");
        }
        finally { ClickraStorage.DeleteTask(a); }
    }

    private static void TestResumingParkedTaskReusesIdentity()
    {
        string a = ClickraStorage.StartTask(CmdDecryptPdf, 2, TestInDir + FileA1 + TestInDir + FileA2);
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            ClickraStorage.ParkTask(a, ParkReason, 1);
            ClickraStorage.SetTaskInProgress(a);
            var resumed = ClickraStorage.GetTask(a);
            Assert.True(resumed.HasValue && resumed.Value.Status == ConversionStatus.InProgress,
                "Resumed task must be InProgress.");
            Assert.True(resumed.HasValue && resumed.Value.CurrentIndex == 1, "Resumed task must keep its next index.");
            ClickraStorage.CompleteTask(a, CmdDecryptPdf, new ClickraStorage.CompleteTaskRequest
            {
                StartTime = "2026-08-16 12:00:00",
                IsSuccess = true,
                ElapsedMs = 900,
                InputPaths = TestInDir + FileA1 + TestInDir + FileA2,
                OutputPath = TestOutDir + FileA1 + TestOutDir + FileA2
            });
            Assert.True(ClickraStorage.GetParkedTasks().All(t => t.Id != a), "Completed resumed task must not stay parked.");
            var history = ClickraStorage.GetHistory(10);
            Assert.True(history.Count(h => h.Command == CmdDecryptPdf) == 1,
                $"Resume+complete must write exactly one history line, got {history.Count(h => h.Command == CmdDecryptPdf)}.");
        }
        finally { ClickraStorage.DeleteTask(a); }
    }

    private static void TestParkedRetentionDaysFromSetting()
    {
        ClickraStorage.SaveSetting(SettingParkedRetention, "0");
        Assert.True(ClickraStorage.GetParkedRetentionDays() == 0, "0 should mean unlimited (no pruning).");
        ClickraStorage.SaveSetting(SettingParkedRetention, "14");
        Assert.True(ClickraStorage.GetParkedRetentionDays() == 14, "Custom days should be honored.");
        ClickraStorage.SaveSetting(SettingParkedRetention, "999");
        Assert.True(ClickraStorage.GetParkedRetentionDays() == ClickraSettings.MaxParkedTaskRetentionDays,
            "Retention above the supported range should clamp to the shared maximum.");
        ClickraStorage.SaveSetting(SettingParkedRetention, "abc");
        Assert.True(ClickraStorage.GetParkedRetentionDays() == 7, "Invalid value should fall back to 7 days.");
        ClickraStorage.SaveSetting(SettingParkedRetention, "7");
    }

    private static void TestSetTaskInProgressRefreshesPid()
    {
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + FileA);
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            var entry = ClickraStorage.GetTask(a);
            Assert.True(entry.HasValue && entry.Value.Pid == Environment.ProcessId,
                $"InProgress task must carry the current pid, got {entry?.Pid}.");
            ClickraStorage.ParkTask(a, ParkReason, 0);
            ClickraStorage.SetTaskInProgress(a);
            var resumed = ClickraStorage.GetTask(a);
            Assert.True(resumed.HasValue && resumed.Value.Pid == Environment.ProcessId,
                $"Resumed task must refresh its pid to the current process, got {resumed?.Pid}.");
        }
        finally { ClickraStorage.DeleteTask(a); }
    }

    private static void TestDeadPidTaskPrunedAsAbandoned()
    {
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + FileA);
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            string path = Path.Combine(ClickraStorage.GetDataDir(), "tasks", $"task-{a}.tmp");
            var lines = File.ReadAllLines(path)
                .Select(l => l.StartsWith("Pid=", StringComparison.Ordinal) ? "Pid=2147483647" : l)
                .ToArray();
            File.WriteAllLines(path, lines);
            var active = ClickraStorage.GetActiveTasks();
            Assert.True(active.All(t => t.Id != a),
                "Task with a dead owner pid must be pruned from the active queue.");
            var history = ClickraStorage.GetHistory(10);
            Assert.True(history.Any(h => h.Command == CmdSplitPdf && !h.IsSuccess && h.ErrorMessage == "Abandoned"),
                "Abandoned task must be recorded in history as Abandoned.");
        }
        finally { ClickraStorage.DeleteTask(a); }
    }

    private static void TestLegacyActiveTmpPreserved()
    {
        string dataDir = ClickraStorage.GetDataDir();
        string legacy = Path.Combine(dataDir, "active.tmp");
        File.WriteAllText(legacy, "Time=2026-08-16 11:00:00\nCommand=merge-pdf\nStatus=InProgress\n");
        string first = ClickraStorage.StartTask("merge-pdf", 2, TestInDir + "\\x.pdf;" + TestInDir + "\\y.pdf");
        // Ensure the two tasks get distinct timestamps (DateTime.UtcNow has ~15ms resolution on Windows).
        Thread.Sleep(20);
        string second = ClickraStorage.StartTask("compress-pdf", 1, TestInDir + "\\z.pdf");
        try
        {
            Assert.True(File.Exists(legacy), "Legacy active.tmp must be preserved for CLI compatibility.");
            var active = ClickraStorage.GetActiveTasks();
            Assert.True(active.Count == 2, $"Expected 2 active tasks, got {active.Count}.");
            Assert.True(active[0].Id == second, "Newest task must come first in the queue.");
            Assert.True(active[1].Id == first, "Older task must come second in the queue.");
        }
        finally
        {
            ClickraStorage.DeleteTask(first);
            ClickraStorage.DeleteTask(second);
            try { File.Delete(legacy); } catch { /* legacy file may already be removed */ }
        }
    }
}
