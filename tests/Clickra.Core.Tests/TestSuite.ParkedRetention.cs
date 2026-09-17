using System;
using System.IO;
using System.Linq;
using Clickra.Core;

namespace Clickra.Core.Tests;

/// <summary>
/// 單一待繼續任務的保留期限：以前只能改全域政策（設定頁的一個數字），
/// 現在每一件任務都可以自己延長或縮短，而不必動到整個政策。
///
/// 這裡守的是三件事，缺一不可：
/// 1. 顯示的期限與清理用的期限是同一份（否則畫面說還剩 10 天，清理卻依全域政策把它刪掉）。
/// 2. 期限調整不會直接刪掉任務 —— 唯一具破壞性的路徑仍是明確的取消。
/// 3. 天數的加減、下限與夾取只在 Core 宣告，兩個介面按一下移動的天數必然相同。
///
/// 前四條是行為測試（真的寫任務檔，必要時把暫存時間回填到過去），後三條是消費端掃描。
/// </summary>
static partial class TestSuite
{
    private static readonly string[] ParkedRetentionUiFiles =
    {
        Path.Combine("src", "Clickra.Fluent", "MainPage.xaml.cs"),
        Path.Combine("src", "Clickra.CLI", "Dashboard", "DashboardWindow.Events.Click.cs"),
        Path.Combine("src", "Clickra.CLI", "Dashboard", "DashboardWindow.Paint.History.cs")
    };

    public static void RegisterParkedRetentionTests(TestRunner runner)
    {
        runner.Run("Parked retention: a task's own deadline overrides the global policy without resetting its age",
            TestParkedRetentionOverrideBeatsPolicy);
        runner.Run("Parked retention: extend and shorten move the shared step and never delete the task",
            TestParkedRetentionAdjustNeverDeletes);
        runner.Run("Parked retention: an unlimited task takes a window only when shortened",
            TestParkedRetentionUnlimitedTask);
        runner.Run("Parked retention: the pruner uses each task's own deadline",
            TestParkedRetentionPrunerHonoursOverride);
        runner.Run("Parked retention: both interfaces move the deadline through one Core operation",
            TestParkedRetentionHasOneOperation);
        runner.Run("Parked retention: the deadline shown and the deadline enforced share one window",
            TestParkedRetentionHasOneWindow);
        runner.Run("Parked retention: the override marker is worded once, in Core",
            TestParkedRetentionMarkerHasOneOwner);
    }

    private static void TestParkedRetentionOverrideBeatsPolicy()
    {
        ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, "7");
        string taskId = WriteParkedTaskFile(daysParked: 20, retentionOverride: null);
        try
        {
            Assert.True(ClickraStorage.GetParkedRetentionInfo(taskId).IsExpired,
                "With a 7 day policy, a task parked 20 days ago is expired.");

            // 這一件自己延長到 30 天。寫入會更新檔案，但暫存時間已經記下來了，
            // 所以年資仍然是 20 天 —— 不是「從現在起再 30 天」。
            ClickraStorage.SetParkedRetentionOverride(taskId, 30);
            var info = ClickraStorage.GetParkedRetentionInfo(taskId);
            Assert.False(info.IsExpired, "A task with its own 30 day deadline must survive a 7 day policy.");
            Assert.Equal(10, info.RemainingDays);
            Assert.True(info.IsTaskOverride, "The info must say the deadline is this task's own.");
            Assert.True(ClickraStorage.DescribeParkedRetention(info).EndsWith(Localization.T("task_parked_ttl_override"), StringComparison.Ordinal),
                "An overridden deadline must say so, so the number is not mistaken for the global policy.");

            // 回到全域政策：年資不變，所以它又是過期的。
            ClickraStorage.SetParkedRetentionOverride(taskId, null);
            var global = ClickraStorage.GetParkedRetentionInfo(taskId);
            Assert.True(global.IsExpired, "Back on the global policy the same 20 day old task is expired again.");
            Assert.False(global.IsTaskOverride, "Without an override the deadline is the global one.");
        }
        finally
        {
            ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, ClickraSettings.DefaultParkedTaskRetention);
            DeleteTaskFile(taskId);
        }
    }

    private static void TestParkedRetentionAdjustNeverDeletes()
    {
        ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, "7");
        string taskId = WriteParkedTaskFile(daysParked: 0, retentionOverride: null);
        try
        {
            int step = ClickraSettings.ParkedRetentionStepDays;

            Assert.Equal(7 + step, ClickraStorage.AdjustParkedRetention(taskId, step) ?? -1);
            Assert.Equal(7, ClickraStorage.AdjustParkedRetention(taskId, -step) ?? -1);

            // 再縮一格會撞到下限：任務不會被刪掉，而是變成「即將過期」。
            int floor = ClickraStorage.AdjustParkedRetention(taskId, -step) ?? -1;
            Assert.Equal(1, floor);
            var info = ClickraStorage.GetParkedRetentionInfo(taskId);
            Assert.False(info.IsExpired, "Shortening a deadline must not delete the task outright.");
            Assert.True(info.IsExpiringSoon, "The shortest window shows up as expiring soon, so the user can still extend it.");
            Assert.True(File.Exists(TaskFilePath(taskId)), "The task file must still exist after shortening past the floor.");

            Assert.True(ClickraStorage.AdjustParkedRetention(taskId, 0) is null, "A zero step is not an adjustment.");
            Assert.True(ClickraStorage.AdjustParkedRetention("no-such-task-id", step) is null,
                "Adjusting a task that does not exist is a no-op, not a crash.");
        }
        finally
        {
            ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, ClickraSettings.DefaultParkedTaskRetention);
            DeleteTaskFile(taskId);
        }
    }

    private static void TestParkedRetentionUnlimitedTask()
    {
        ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, "0");
        string taskId = WriteParkedTaskFile(daysParked: 0, retentionOverride: null);
        try
        {
            int step = ClickraSettings.ParkedRetentionStepDays;

            Assert.True(ClickraStorage.GetParkedRetentionInfo(taskId).IsUnlimited, "0 days means never expires.");
            Assert.True(ClickraStorage.AdjustParkedRetention(taskId, step) is null,
                "There is nothing to extend on a task that never expires.");

            Assert.Equal(1, ClickraStorage.AdjustParkedRetention(taskId, -step) ?? -1);
            var info = ClickraStorage.GetParkedRetentionInfo(taskId);
            Assert.False(info.IsUnlimited, "Shortening an unlimited task gives it a finite window.");
            Assert.True(info.IsTaskOverride, "That window belongs to the task, not to the policy.");
        }
        finally
        {
            ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, ClickraSettings.DefaultParkedTaskRetention);
            DeleteTaskFile(taskId);
        }
    }

    /// <summary>顯示的期限與清理用的期限必須是同一份：延長過的任務不能被全域政策刪掉，
    /// 縮短過的任務則要在自己的期限到時才被清掉。</summary>
    private static void TestParkedRetentionPrunerHonoursOverride()
    {
        ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, "7");
        string extended = WriteParkedTaskFile(daysParked: 20, retentionOverride: 30);
        string shortened = WriteParkedTaskFile(daysParked: 20, retentionOverride: 7);
        try
        {
            var parked = ClickraStorage.GetParkedTasks();
            Assert.True(parked.Any(t => t.Id == extended),
                "A task the user extended must survive the prune that runs with the list.");
            Assert.True(File.Exists(TaskFilePath(extended)), "The extended task's file must still be there.");
            Assert.True(parked.All(t => t.Id != shortened),
                "A task whose own deadline has passed must leave the list.");
            Assert.False(File.Exists(TaskFilePath(shortened)), "The expired task's file must be gone.");
        }
        finally
        {
            ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, ClickraSettings.DefaultParkedTaskRetention);
            DeleteTaskFile(extended);
            DeleteTaskFile(shortened);
        }
    }

    /// <summary>天數的加減、下限與夾取只在 Core：兩個介面的按鈕都必須呼叫同一個操作，
    /// 而且都要引用同一個步進常數。</summary>
    private static void TestParkedRetentionHasOneOperation()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        foreach ((string surface, string path) in new[]
        {
            ("Fluent History page", Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs")),
            ("CLI dashboard", Path.Combine(root, "src", "Clickra.CLI", "Dashboard", "DashboardWindow.Events.Click.cs"))
        })
        {
            string source = File.ReadAllText(path);
            Assert.True(source.Contains("ClickraStorage.AdjustParkedRetention(", StringComparison.Ordinal),
                $"{surface} must adjust the deadline through Core, not by writing the number itself.");
            Assert.True(source.Contains("ClickraSettings.ParkedRetentionStepDays", StringComparison.Ordinal),
                $"{surface} must move the shared step, so both interfaces move the same number of days.");
        }

        foreach (string relative in ParkedRetentionUiFiles)
        {
            string source = File.ReadAllText(Path.Combine(root, relative));
            // 記錄欄位是 entry.ParkedRetentionDays；介面碰它就會自己算期限（MaxParkedRetentionDays
            // 與 GetParkedRetentionDays() 是政策，不是這件任務的覆寫）。
            Assert.False(source.Contains(".ParkedRetentionDays", StringComparison.Ordinal),
                $"{relative} touches the task record's override field; that belongs to ClickraStorage.");
        }

        string core = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "ClickraStorage.ActiveRecord.cs"));
        Assert.True(core.Contains("public static int? AdjustParkedRetention(", StringComparison.Ordinal),
            "ClickraStorage must own the extend/shorten arithmetic.");
        Assert.True(core.Contains("public static void SetParkedRetentionOverride(", StringComparison.Ordinal),
            "ClickraStorage must own the override/reset operation.");
    }

    /// <summary>期限的顯示（GetParkedRetentionInfo）與過期清理（PruneSingleTaskFile）必須走
    /// 同一個窗口計算；兩邊各算一次的話，畫面會顯示新期限，清理卻依全域政策刪掉任務。</summary>
    private static void TestParkedRetentionHasOneWindow()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        string core = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "ClickraStorage.ActiveRecord.cs"));

        foreach (string method in new[] { "public static ParkedRetentionInfo GetParkedRetentionInfo(string taskId)", "private static void PruneSingleTaskFile(string file, DateTime now)" })
        {
            int start = core.IndexOf(method, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{method} must exist.");
            int end = core.IndexOf("\n        }", start, StringComparison.Ordinal);
            string body = end > start ? core[start..end] : core[start..];
            Assert.True(body.Contains("ParkedWindow(", StringComparison.Ordinal),
                $"{method} must take the task's effective window from the shared calculation.");
            Assert.False(body.Contains("GetParkedRetentionDays()", StringComparison.Ordinal),
                $"{method} must not fall back to the global policy on its own; the override lives in ParkedWindow.");
        }
    }

    /// <summary>「（此任務自訂）」這句話屬於 Core 的期限描述，兩個介面都不該自己拼。</summary>
    private static void TestParkedRetentionMarkerHasOneOwner()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        string core = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "ClickraStorage.ActiveRecord.cs"));
        Assert.True(core.Contains("task_parked_ttl_override", StringComparison.Ordinal),
            "DescribeParkedRetention must append the override marker for every interface.");

        foreach (string relative in ParkedRetentionUiFiles)
        {
            string source = File.ReadAllText(Path.Combine(root, relative));
            Assert.False(source.Contains("task_parked_ttl_override", StringComparison.Ordinal),
                $"{relative} words the override marker itself; the shared deadline sentence owns it.");
        }
    }

    // ─── 測試用的任務檔 ────────────────────────────────────────
    // 保留期限的判斷需要「已經擱了幾天」，而公開 API 只能建立剛暫存的任務，
    // 所以這裡直接寫任務檔（格式與 WriteTaskFileInternal 相同）把暫存時間回填到過去。

    private static string TaskFilePath(string taskId) =>
        Path.Combine(ClickraStorage.GetDataDir(), "tasks", $"task-{taskId}.tmp");

    private static void DeleteTaskFile(string taskId)
    {
        try { File.Delete(TaskFilePath(taskId)); } catch { }
    }

    private static string WriteParkedTaskFile(int daysParked, int? retentionOverride, int fileCount = 1)
    {
        // 檔名的時間戳前綴決定排序，格式與 NewTaskId() 相同（yyyyMMddHHmmssfff + '-' + 序號）。
        string taskId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-parked{Guid.NewGuid().ToString("N")[..6]}";
        string dir = Path.Combine(ClickraStorage.GetDataDir(), "tasks");
        Directory.CreateDirectory(dir);
        string parkedSince = DateTime.UtcNow.AddDays(-daysParked).ToString("yyyy-MM-dd HH:mm:ss");

        var lines = new[]
        {
            $"Id={taskId}",
            $"Time={parkedSince}",
            "Command=split-pdf",
            $"FileCount={fileCount}",
            "Status=Parked",
            "ErrorMessage=Waiting for input",
            @"InputPaths=C:\feed\in\a.pdf",
            "CurrentIndex=0",
            "OutputPath=",
            "EndTime=",
            "ElapsedMs=-1",
            "Pid=0",
            retentionOverride.HasValue ? $"ParkedRetentionDays={retentionOverride.Value}" : "",
            $"ParkedSince={parkedSince}"
        };

        File.WriteAllLines(TaskFilePath(taskId), lines.Where(line => line.Length > 0));
        return taskId;
    }
}
