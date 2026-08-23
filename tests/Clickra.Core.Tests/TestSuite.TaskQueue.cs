using System;
using System.IO;
using System.Linq;
using Clickra.Core;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterTaskQueueTests(TestRunner runner)
    {
        runner.Run("Task queue: concurrent tasks keep independent progress files", () =>
        {
            try
            {
                // 兩個並行任務各自有獨立的進度檔，互不覆蓋。
                string a = ClickraStorage.StartTask("split-pdf", 1, @"C:\in\a.pdf");
                string b = ClickraStorage.StartTask("decrypt-pdf", 2, @"C:\in\b1.pdf;C:\in\b2.pdf");
                ClickraStorage.SetTaskInProgress(a);
                ClickraStorage.SetTaskInProgress(b);

                var active = ClickraStorage.GetActiveTasks();
                Assert.True(active.Count == 2, $"Expected 2 active tasks, got {active.Count}.");
                Assert.True(active.Any(t => t.Id == a && t.Command == "split-pdf"), "Task A missing from the queue.");
                Assert.True(active.Any(t => t.Id == b && t.Command == "decrypt-pdf"), "Task B missing from the queue.");

                // 索引更新各走各的。
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
            finally
            {
                foreach (var task in ClickraStorage.GetActiveTasks())
                {
                    ClickraStorage.DeleteTask(task.Id);
                }
            }
        });

        runner.Run("Task queue: completing a task leaves the active queue and writes history", () =>
        {
            string a = ClickraStorage.StartTask("split-pdf", 1, @"C:\in\a.pdf");
            string b = ClickraStorage.StartTask("decrypt-pdf", 1, @"C:\in\b.pdf");
            try
            {
                ClickraStorage.CompleteTask(a, "split-pdf", "2026-08-16 12:00:00", true, "", null, 1234,
                    @"C:\in\a.pdf", @"C:\out\a_split.pdf");

                var active = ClickraStorage.GetActiveTasks();
                Assert.True(active.All(t => t.Id != a), "Completed task A must leave the active queue.");
                Assert.True(active.Any(t => t.Id == b), "Task B must stay in the queue while A completes.");

                var finishedA = ClickraStorage.GetTask(a);
                Assert.True(finishedA.HasValue && finishedA.Value.Status == ConversionStatus.Success,
                    "Completed task A should keep its Success status file.");

                var history = ClickraStorage.GetHistory(10);
                Assert.True(history.Any(h => h.Command == "split-pdf" && h.IsSuccess && h.OutputPath == @"C:\out\a_split.pdf"),
                    "History line missing for the completed task A.");
            }
            finally
            {
                ClickraStorage.DeleteTask(a);
                ClickraStorage.DeleteTask(b);
            }
        });

        runner.Run("Task queue: deleting a task removes its progress file", () =>
        {
            string a = ClickraStorage.StartTask("merge-pdf", 2, @"C:\in\x.pdf;C:\in\y.pdf");
            Assert.True(ClickraStorage.GetTask(a) != null, "Task should be readable right after StartTask.");
            ClickraStorage.DeleteTask(a);
            Assert.True(ClickraStorage.GetTask(a) == null, "Task must not be readable after DeleteTask.");
            Assert.True(ClickraStorage.GetActiveTasks().All(t => t.Id != a), "Deleted task must not appear in the queue.");
        });

        runner.Run("Task queue: parking moves a task out of active into parked with next index", () =>
        {
            string a = ClickraStorage.StartTask("decrypt-pdf", 2, @"C:\in\a1.pdf;C:\in\a2.pdf");
            try
            {
                ClickraStorage.SetTaskInProgress(a);
                ClickraStorage.ParkTask(a, "Waiting for input", 1);

                var active = ClickraStorage.GetActiveTasks();
                Assert.True(active.All(t => t.Id != a), "Parked task must leave the active queue.");

                var parked = ClickraStorage.GetParkedTasks();
                var entry = parked.FirstOrDefault(t => t.Id == a);
                Assert.True(entry.Id == a, "Parked task missing from GetParkedTasks.");
                Assert.True(entry.Status == ConversionStatus.Parked, "Parked task status must be Parked.");
                Assert.True(entry.ErrorMessage == "Waiting for input", $"Park reason lost: '{entry.ErrorMessage}'.");
                Assert.True(entry.CurrentIndex == 1, $"Next index should be 1, got {entry.CurrentIndex}.");

                // 暫存不寫歷史（可恢復/取消，不是失敗）。
                var history = ClickraStorage.GetHistory(10);
                Assert.True(history.All(h => h.Command != "decrypt-pdf"), "Parking must not write a history line.");
            }
            finally
            {
                ClickraStorage.DeleteTask(a);
            }
        });

        runner.Run("Task queue: resuming a parked task reuses its identity and index", () =>
        {
            string a = ClickraStorage.StartTask("decrypt-pdf", 2, @"C:\in\a1.pdf;C:\in\a2.pdf");
            try
            {
                ClickraStorage.SetTaskInProgress(a);
                ClickraStorage.ParkTask(a, "Waiting for input", 1);

                // 恢復 = 把 Parked 切回 InProgress（沿用同一 task 檔）。
                ClickraStorage.SetTaskInProgress(a);
                var resumed = ClickraStorage.GetTask(a);
                Assert.True(resumed.HasValue && resumed.Value.Status == ConversionStatus.InProgress,
                    "Resumed task must be InProgress.");
                Assert.True(resumed.HasValue && resumed.Value.CurrentIndex == 1, "Resumed task must keep its next index.");

                ClickraStorage.CompleteTask(a, "decrypt-pdf", "2026-08-16 12:00:00", true, "", null, 900,
                    @"C:\in\a1.pdf;C:\in\a2.pdf", @"C:\out\a1.pdf;C:\out\a2.pdf");

                Assert.True(ClickraStorage.GetParkedTasks().All(t => t.Id != a), "Completed resumed task must not stay parked.");
                var history = ClickraStorage.GetHistory(10);
                Assert.True(history.Count(h => h.Command == "decrypt-pdf") == 1,
                    $"Resume+complete must write exactly one history line, got {history.Count(h => h.Command == "decrypt-pdf")}.");
            }
            finally
            {
                ClickraStorage.DeleteTask(a);
            }
        });

        runner.Run("Task queue: parked retention days come from the setting (0 = unlimited)", () =>
        {
            ClickraStorage.SaveSetting("ParkedTaskRetention", "0");
            Assert.True(ClickraStorage.GetParkedRetentionDays() == 0, "0 should mean unlimited (no pruning).");
            ClickraStorage.SaveSetting("ParkedTaskRetention", "14");
            Assert.True(ClickraStorage.GetParkedRetentionDays() == 14, "Custom days should be honored.");
            ClickraStorage.SaveSetting("ParkedTaskRetention", "abc");
            Assert.True(ClickraStorage.GetParkedRetentionDays() == 7, "Invalid value should fall back to 7 days.");
            ClickraStorage.SaveSetting("ParkedTaskRetention", "7");
        });

        runner.Run("Task queue: SetTaskInProgress refreshes the owning pid (resume safety)", () =>
        {
            string a = ClickraStorage.StartTask("split-pdf", 1, @"C:\in\a.pdf");
            try
            {
                ClickraStorage.SetTaskInProgress(a);
                var entry = ClickraStorage.GetTask(a);
                Assert.True(entry.HasValue && entry.Value.Pid == Environment.ProcessId,
                    $"InProgress task must carry the current pid, got {entry?.Pid}.");

                // 暫存後恢復（沿用原 task 檔）仍必須更新為目前進程，
                // 否則遺棄清理會把正在跑的恢復任務當成死任務刪掉。
                ClickraStorage.ParkTask(a, "Waiting for input", 0);
                ClickraStorage.SetTaskInProgress(a);
                var resumed = ClickraStorage.GetTask(a);
                Assert.True(resumed.HasValue && resumed.Value.Pid == Environment.ProcessId,
                    $"Resumed task must refresh its pid to the current process, got {resumed?.Pid}.");
            }
            finally
            {
                ClickraStorage.DeleteTask(a);
            }
        });

        runner.Run("Task queue: active tasks whose owner process died are pruned as Canceled", () =>
        {
            string a = ClickraStorage.StartTask("split-pdf", 1, @"C:\in\a.pdf");
            try
            {
                ClickraStorage.SetTaskInProgress(a);
                // 直接改寫 task 檔，把 Pid 換成必定不存在的進程（模擬崩潰/被強殺）。
                string path = Path.Combine(ClickraStorage.GetDataDir(), "tasks", $"task-{a}.tmp");
                var lines = File.ReadAllLines(path)
                    .Select(l => l.StartsWith("Pid=", StringComparison.Ordinal) ? "Pid=2147483647" : l)
                    .ToArray();
                File.WriteAllLines(path, lines);

                var active = ClickraStorage.GetActiveTasks();
                Assert.True(active.All(t => t.Id != a),
                    "Task with a dead owner pid must be pruned from the active queue.");

                var history = ClickraStorage.GetHistory(10);
                Assert.True(history.Any(h => h.Command == "split-pdf" && !h.IsSuccess && h.ErrorMessage == "Abandoned"),
                    "Abandoned task must be recorded in history as Abandoned.");
            }
            finally
            {
                ClickraStorage.DeleteTask(a);
            }
        });

        runner.Run("Task queue: legacy active.tmp is preserved and queue orders newest first", () =>
        {
            string dataDir = ClickraStorage.GetDataDir();
            string legacy = Path.Combine(dataDir, "active.tmp");
            File.WriteAllText(legacy, "Time=2026-08-16 11:00:00\nCommand=merge-pdf\nStatus=InProgress\n");

            string first = ClickraStorage.StartTask("merge-pdf", 2, @"C:\in\x.pdf;C:\in\y.pdf");
            string second = ClickraStorage.StartTask("compress-pdf", 1, @"C:\in\z.pdf");
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
                try { File.Delete(legacy); } catch { }
            }
        });
    }
}
