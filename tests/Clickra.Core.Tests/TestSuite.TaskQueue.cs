using System;
using System.IO;
using System.Linq;
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
    }

    private static void CleanupActiveTasks()
    {
        foreach (var task in ClickraStorage.GetActiveTasks())
            ClickraStorage.DeleteTask(task.Id);
    }

    private static void TestConcurrentTasksKeepIndependentProgressFiles()
    {
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + "\\a.pdf");
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
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + "\\a.pdf");
        string b = ClickraStorage.StartTask(CmdDecryptPdf, 1, TestInDir + "\\b.pdf");
        try
        {
            ClickraStorage.CompleteTask(a, CmdSplitPdf, "2026-08-16 12:00:00", true, "", null, 1234,
                TestInDir + "\\a.pdf", TestOutDir + "\\a_split.pdf");
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
        string a = ClickraStorage.StartTask(CmdDecryptPdf, 2, TestInDir + "\\a1.pdf;" + TestInDir + "\\a2.pdf");
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
        string a = ClickraStorage.StartTask(CmdDecryptPdf, 2, TestInDir + "\\a1.pdf;" + TestInDir + "\\a2.pdf");
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            ClickraStorage.ParkTask(a, ParkReason, 1);
            ClickraStorage.SetTaskInProgress(a);
            var resumed = ClickraStorage.GetTask(a);
            Assert.True(resumed.HasValue && resumed.Value.Status == ConversionStatus.InProgress,
                "Resumed task must be InProgress.");
            Assert.True(resumed.HasValue && resumed.Value.CurrentIndex == 1, "Resumed task must keep its next index.");
            ClickraStorage.CompleteTask(a, CmdDecryptPdf, "2026-08-16 12:00:00", true, "", null, 900,
                TestInDir + "\\a1.pdf;" + TestInDir + "\\a2.pdf", TestOutDir + "\\a1.pdf;" + TestOutDir + "\\a2.pdf");
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
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + "\\a.pdf");
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
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + "\\a.pdf");
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
