using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clickra.Core;

namespace Clickra.Core.Tests;

/// <summary>
/// 「一份清單、兩個呈現層」的守門。
///
/// Fluent 的 History 頁與 Win32 dashboard 的 History 頁都只讀 Core 的 HistoryFeed，所以
/// 「有幾件在跑」、「這一件是失敗還是被取消」、「下一件什麼時候過期」在兩個介面之間只有
/// 一個答案。以前兩邊各自從三份儲存查詢重新推導，於是同一件任務可以一邊說「錯誤」另一邊
/// 說「失敗」，一邊認得新的取消旗標另一邊只認得舊的。
///
/// 前四條是行為測試（真的寫任務與歷史，再讀清單），後兩條是消費端掃描，用來防止某個介面
/// 又自己列舉一次儲存或自己決定用字。
/// </summary>
static partial class TestSuite
{
    private const string FeedCommand = "split-pdf";
    private const string FeedParkReason = "Waiting for input";
    private const string FeedInDir = @"C:\feed\in";
    private const string FeedOutDir = @"C:\feed\out";
    private const string FeedDoneInput = FeedInDir + @"\done.pdf";
    private const string FeedCanceledInput = FeedInDir + @"\cancel.pdf";
    private const string CliDashboardSurface = "CLI dashboard";
    private const string FluentHistorySurface = "Fluent History page";

    public static void RegisterHistoryFeedTests(TestRunner runner)
    {
        runner.Run("History feed: one list carries active, parked and completed items in display order",
            TestHistoryFeedCarriesAllThreeKinds);
        runner.Run("History feed: row status wording comes from the shared item, and canceled is recognized",
            TestHistoryFeedStatusWording);
        runner.Run("History feed: file description and stopped-on index are computed once for both UIs",
            TestHistoryFeedRowFacts);
        runner.Run("History feed: the slices and the overview counts agree with storage",
            TestHistoryFeedCounts);
        runner.Run("History feed: both History pages read the feed and neither enumerates storage",
            TestHistoryFeedIsTheOnlySource);
        runner.Run("History feed: neither History page words status, file counts or deadlines itself",
            TestHistoryFeedOwnsTheWording);
    }

    private static void TestHistoryFeedCarriesAllThreeKinds()
    {
        string activeId = ClickraStorage.StartTask(FeedCommand, 1, FeedInDir + @"\active.pdf");
        string parkedId = ClickraStorage.StartTask(FeedCommand, 2, FeedInDir + @"\parked-1.pdf;" + FeedInDir + @"\parked-2.pdf");
        string doneId = ClickraStorage.StartTask(FeedCommand, 1, FeedDoneInput);
        try
        {
            ClickraStorage.SetTaskInProgress(parkedId);
            ClickraStorage.ParkTask(parkedId, FeedParkReason, 1);
            ClickraStorage.CompleteTask(doneId, FeedCommand, new ClickraStorage.CompleteTaskRequest
            {
                StartTime = "2026-09-17 12:00:00",
                IsSuccess = true,
                ElapsedMs = 1234,
                InputPaths = FeedDoneInput,
                OutputPath = FeedOutDir + @"\done.pdf"
            });

            var feed = HistoryFeed.Load(50);

            Assert.True(feed.OfKind(HistoryItemKind.Active).Any(i => i.Entry.Id == activeId),
                "The running conversions must be in the same list the History page renders, not a separate query.");
            Assert.True(feed.OfKind(HistoryItemKind.Parked).Any(i => i.Entry.Id == parkedId),
                "Parked conversions must be in the shared list.");
            Assert.True(feed.OfKind(HistoryItemKind.Completed).Any(i => i.Entry.InputPaths == FeedDoneInput),
                "Completed conversions must be in the shared list.");

            // 顯示順序是清單的一部分：兩個呈現層都照 Items 的順序畫，不各自排序。
            int activeAt = IndexOf(feed, HistoryItemKind.Active, activeId);
            int parkedAt = IndexOf(feed, HistoryItemKind.Parked, parkedId);
            int completedAt = IndexOf(feed, HistoryItemKind.Completed, FeedDoneInput);
            Assert.True(activeAt >= 0 && parkedAt > activeAt && completedAt > parkedAt,
                $"The feed must keep the display order active -> parked -> completed, got {activeAt}, {parkedAt}, {completedAt}.");
        }
        finally
        {
            ClickraStorage.DeleteTask(activeId);
            ClickraStorage.DeleteTask(parkedId);
            ClickraStorage.DeleteTask(doneId);
        }
    }

    private static void TestHistoryFeedStatusWording()
    {
        var vocabulary = new[] { "status_pending", "status_converting", "status_success", "status_failed", "status_canceled" };
        var declared = Localization.GetAllKeys();
        foreach (string key in vocabulary)
        {
            Assert.True(declared.Contains(key),
                $"{key} must stay declared: both History pages show it through the shared item, and an undeclared key renders verbatim.");
            foreach (string lang in Localization.SupportedLanguages)
                Assert.True(Localization.HasExactTranslation(key, lang), $"{key} has no {lang} translation.");
        }

        // 取消用的旗標有新舊兩種；舊旗標還躺在既有的 history.log 裡，Core 必須兩者都認得。
        Assert.True(ClickraStorage.IsUserCanceledReason(ClickraStorage.CanceledReason),
            "The current canceled marker must be recognized.");
        Assert.True(ClickraStorage.IsUserCanceledReason(ClickraStorage.LegacyUserAbortedReason),
            "The legacy user-abort marker must keep counting as canceled.");

        string canceledId = ClickraStorage.StartTask(FeedCommand, 2, FeedCanceledInput + ";" + FeedInDir + @"\cancel-2.pdf");
        string failedId = ClickraStorage.StartTask(FeedCommand, 1, FeedInDir + @"\failed.pdf");
        string pendingId = ClickraStorage.StartTask(FeedCommand, 1, FeedInDir + @"\pending.pdf");
        try
        {
            // 取消只對已暫存的任務成立，所以先暫存再取消（與 UI 的操作順序一致）。
            ClickraStorage.SetTaskInProgress(canceledId);
            ClickraStorage.ParkTask(canceledId, FeedParkReason, 1);
            ClickraStorage.CancelParkedTask(canceledId);
            ClickraStorage.CompleteTask(failedId, FeedCommand, new ClickraStorage.CompleteTaskRequest
            {
                StartTime = "2026-09-17 13:00:00",
                IsSuccess = false,
                ErrorMsg = "boom",
                InputPaths = FeedInDir + @"\failed.pdf"
            });

            var feed = HistoryFeed.Load(50);

            var canceled = feed.OfKind(HistoryItemKind.Completed)
                .First(i => i.Entry.InputPaths.Contains(FeedCanceledInput, StringComparison.Ordinal));
            Assert.True(canceled.IsCanceled, "A conversion canceled through CancelParkedTask must be recognized as canceled.");
            Assert.True(canceled.StatusKey == "status_canceled",
                $"A canceled conversion must say so, got '{canceled.StatusKey}' (this used to read as a failure in one of the two pages).");

            var failed = feed.OfKind(HistoryItemKind.Completed)
                .First(i => i.Entry.InputPaths == FeedInDir + @"\failed.pdf");
            Assert.True(!failed.IsCanceled, "A genuine failure is not a cancellation.");
            Assert.True(failed.StatusKey == "status_failed", $"A failed conversion must read as failed, got '{failed.StatusKey}'.");

            var pending = feed.OfKind(HistoryItemKind.Active).First(i => i.Entry.Id == pendingId);
            Assert.True(pending.StatusKey == "status_pending", $"An untouched task is pending, got '{pending.StatusKey}'.");
        }
        finally
        {
            ClickraStorage.DeleteTask(canceledId);
            ClickraStorage.DeleteTask(failedId);
            ClickraStorage.DeleteTask(pendingId);
        }
    }

    private static void TestHistoryFeedRowFacts()
    {
        string parkedId = ClickraStorage.StartTask(FeedCommand, 3,
            FeedInDir + @"\one.pdf;" + FeedInDir + @"\two.pdf;" + FeedInDir + @"\three.pdf");
        string singleId = ClickraStorage.StartTask(FeedCommand, 1, FeedInDir + @"\solo.pdf");
        try
        {
            ClickraStorage.SetTaskInProgress(parkedId);
            ClickraStorage.ParkTask(parkedId, FeedParkReason, 1);

            var feed = HistoryFeed.Load(50);

            var parked = feed.OfKind(HistoryItemKind.Parked).First(i => i.Entry.Id == parkedId);
            string stoppedOn = string.Format(Localization.T("task_file_index"), 2, 3);
            Assert.True(parked.StoppedOnText == stoppedOn,
                $"The parked row must say which file it stopped on using the shared phrase, got '{parked.StoppedOnText}'.");
            Assert.True(parked.Subtitle.Contains("one.pdf", StringComparison.Ordinal)
                && parked.Subtitle.Contains(stoppedOn, StringComparison.Ordinal)
                && parked.Subtitle.Contains(FeedParkReason, StringComparison.Ordinal),
                $"The parked subtitle must carry the file, the stopped-on index and the reason, got '{parked.Subtitle}'.");
            Assert.True(parked.FileCountText.StartsWith("one.pdf + 2 ", StringComparison.Ordinal),
                $"A multi-file row shows the first file plus the remainder, got '{parked.FileCountText}'.");
            Assert.True(parked.RetentionText.Length > 0, "Every parked row carries its own remaining retention.");
            Assert.False(parked.NeedsAttention, "A freshly parked task with the default retention is not expiring yet.");
            Assert.True(parked.StatusKey.Length == 0, "A parked row shows its deadline instead of a status word.");

            var single = feed.OfKind(HistoryItemKind.Active).First(i => i.Entry.Id == singleId);
            Assert.True(single.StoppedOnText.Length == 0, "A single-file task has no 'file i/n' phrase.");
            Assert.True(single.FileCountText == "solo.pdf", $"A single input is shown by its name, got '{single.FileCountText}'.");
            Assert.True(single.RetentionText.Length == 0, "Only parked rows carry a retention phrase.");
            Assert.True(single.CommandLabelKey != single.Entry.Command && Localization.T(single.CommandLabelKey) != single.CommandLabelKey,
                $"The command tag must resolve through the registry, got '{single.CommandLabelKey}'.");
        }
        finally
        {
            ClickraStorage.DeleteTask(parkedId);
            ClickraStorage.DeleteTask(singleId);
        }
    }

    private static void TestHistoryFeedCounts()
    {
        var feed = HistoryFeed.Load(50);
        var history = ClickraStorage.GetHistory(50);

        Assert.True(feed.ActiveCount == ClickraStorage.GetActiveTasks().Count,
            "The running slice must be exactly the active queue the progress windows write.");
        Assert.True(feed.ParkedCount == ClickraStorage.GetParkedTasks().Count,
            "The parked slice must be exactly the parked queue.");
        Assert.True(feed.CompletedCount == history.Count,
            "The completed slice must be exactly the persisted history the list is limited to.");
        Assert.True(feed.SuccessCount == history.Count(h => h.IsSuccess),
            "The overview success count must be counted off the same list the rows come from.");
        Assert.True(feed.FailedCount == history.Count(h => !h.IsSuccess),
            "The overview failure count must be counted off the same list the rows come from.");
        Assert.True(feed.ExpiringSoonCount == feed.OfKind(HistoryItemKind.Parked).Count(i => i.NeedsAttention),
            "The aggregate warning must count the very rows it warns about.");
    }

    private static void TestHistoryFeedIsTheOnlySource()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        // 兩個呈現層都載入同一份清單。
        foreach ((string surface, string path) in new[]
        {
            (CliDashboardSurface, Path.Combine(root, "src", "Clickra.CLI", "Dashboard", "DashboardWindow.Lifecycle.cs")),
            (FluentHistorySurface, Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"))
        })
        {
            string source = File.ReadAllText(path);
            Assert.True(source.Contains("HistoryFeed.Load(", StringComparison.Ordinal),
                $"{surface} must load the shared Core feed instead of querying storage itself.");
        }

        // 而且都不得自行列舉那三份清單：兩邊各查一次儲存，就有兩個時間點、兩種過濾、兩種順序，
        // 同一個任務在兩個介面可以是不同答案。
        foreach (string project in new[] { "Clickra.CLI", "Clickra.Fluent" })
        {
            foreach (string file in SourceFilesUnder(Path.Combine(root, "src", project)))
            {
                string source = File.ReadAllText(file);
                foreach (string call in new[] { "ClickraStorage.GetHistory(", "GetParkedTasks()", "GetActiveTasks()" })
                {
                    Assert.False(source.Contains(call, StringComparison.Ordinal),
                        $"{Path.GetFileName(file)} enumerates the task/history lists itself; HistoryFeed is the one source for both UIs.");
                }
            }
        }

        // 清單本身住在 Core，兩個介面才可能共用；放進任一個介面就不可能被另一個使用。
        string feed = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "HistoryFeed.cs"));
        foreach (string member in new[] { "HistoryItemKind.Active", "HistoryItemKind.Parked", "HistoryItemKind.Completed", "DescribeParkedRetention" })
        {
            Assert.True(feed.Contains(member, StringComparison.Ordinal),
                $"HistoryFeed must carry all three kinds and reuse the shared wording ({member}).");
        }
    }

    private static void TestHistoryFeedOwnsTheWording()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        string cli = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Dashboard", "DashboardWindow.Paint.History.cs"));
        string fluent = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
        string model = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Storage", "HistoryFeed.cs"));

        // 每一列的事實都取自項目，而不是當場重算。
        foreach ((string surface, string source) in new[] { (CliDashboardSurface, cli), (FluentHistorySurface, fluent) })
        {
            foreach (string fact in new[] { "item.StatusKey", "item.FileCountText" })
            {
                Assert.True(source.Contains(fact, StringComparison.Ordinal),
                    $"{surface} must render {fact} from the shared item.");
            }
        }
        foreach ((string fact, string source, string surface) in new[]
        {
            ("item.CommandLabelKey", cli, CliDashboardSurface),
            ("item.CommandLabelKey", fluent, FluentHistorySurface),
            ("item.Subtitle", cli, CliDashboardSurface),
            ("item.Subtitle", fluent, FluentHistorySurface),
            ("item.RetentionText", cli, CliDashboardSurface),
            ("item.RetentionText", fluent, FluentHistorySurface),
            ("item.NeedsAttention", cli, CliDashboardSurface),
            ("item.NeedsAttention", fluent, FluentHistorySurface),
        })
        {
            Assert.True(source.Contains(fact, StringComparison.Ordinal),
                $"{surface} must render {fact}; the other interface renders the same fact from the same list.");
        }

        // 結果用字（成功／失敗／已取消）與檔案描述都不得在介面裡再寫一份。這正是過去兩頁
        // 對同一件任務說出不同字的原因（一邊「錯誤」、一邊「失敗」；一邊不認得取消旗標）。
        foreach ((string surface, string source) in new[] { (CliDashboardSurface, cli), (FluentHistorySurface, fluent) })
        {
            foreach (string worded in new[]
            {
                "\"status_success\"", "\"status_failed\"", "\"status_canceled\"",
                "\"label_files\"", "\"task_file_index\"", "\"fluent_task_file_index\""
            })
            {
                Assert.False(source.Contains(worded, StringComparison.Ordinal),
                    $"{surface} words {worded} itself; the shared item owns it.");
            }
        }

        // 模型是那句話的出處，而且不得再長出 Fluent 專屬前綴 —— 同一句話由兩個介面共用。
        foreach (string key in new[] { "status_success", "status_failed", "status_canceled", "label_files", "task_file_index" })
        {
            Assert.True(model.Contains(key, StringComparison.Ordinal), $"HistoryFeed must be the place that decides {key}.");
        }
        Assert.False(model.Contains("fluent_", StringComparison.Ordinal),
            "The feed is shared by both interfaces, so its keys must not carry a Fluent-only prefix.");
    }

    private static int IndexOf(HistoryFeed feed, HistoryItemKind kind, string entryKey)
    {
        var items = feed.Items;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind != kind) continue;
            string key = kind == HistoryItemKind.Completed ? items[i].Entry.InputPaths : items[i].Entry.Id;
            if (key == entryKey) return i;
        }
        return -1;
    }

    private static IEnumerable<string> SourceFilesUnder(string directory)
        => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
