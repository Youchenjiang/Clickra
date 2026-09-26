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
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        string code = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml.cs"));
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
        foreach (string key in new[] { "fluent_task_parked_title", "fluent_task_parked_desc", "fluent_task_parked_cancel_confirm", "fluent_task_resume", "fluent_status_canceled" })
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
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        string code = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml.cs"));
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
