using System;
using System.IO;
using System.Linq;
using System.Threading;
using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string CmdSplitPdf = "split-pdf";
    private const string CmdDecryptPdf = "decrypt-pdf";
    private const string CmdMergePdf = "merge-pdf";
    private const string CoreProjectDirectory = "Clickra.Core";
    private const string ApplicationDirectory = "Application";
    private const string StorageDirectory = "Storage";
    private const string ParkReason = "Waiting for input";
    private const string TestInDir = @"C:\in";
    private const string TestOutDir = @"C:\out";
    private const string FileA = @"\a.pdf";
    private const string FileA1 = @"\a1.pdf;";
    private const string FileA2 = @"\a2.pdf";
    private const string FluentProjectDirectory = "Clickra.Fluent";
    private const string RepoRootNotFoundMessage = "Could not locate the repository root from the test output directory.";
    private const string LifecycleFixtureContent = "fixture";
    private const string FluentMainPageFile = "MainPage.xaml.cs";
    private const string FluentHistoryPageFile = "MainPage.History.cs";
    private const string CliProjectDirectory = "Clickra.CLI";
    private const string DashboardDirectory = "Dashboard";
    private const string DashboardEventsClickFile = "DashboardWindow.Events.Click.cs";
    private const string DashboardEventsHistoryFile = "DashboardWindow.Events.History.cs";
    private const string DashboardEventsSettingsFile = "DashboardWindow.Events.Settings.cs";
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
        runner.Run("Task queue: claimed parked task is returned to parked when execution cannot proceed",
            TestClaimedParkedTaskReturnedToParkedWhenExecutionCannotProceed);
        runner.Run("Task queue: resume claim refreshes the owning pid",
            TestResumeClaimRefreshesPid);
        runner.Run("Task queue: active tasks whose owner process died are pruned as Canceled",
            TestDeadPidTaskPrunedAsAbandoned);
        runner.Run("Task queue: legacy active.tmp is preserved and queue orders newest first",
            TestLegacyActiveTmpPreserved);
        runner.Run("Task queue: cancelling a parked task records one canceled line and removes it",
            TestCancellingParkedTaskRecordsCanceledLine);
        runner.Run("Task queue: parked tasks never expire while completed tasks are pruned after TTL",
            TestParkedTasksNeverExpireWhileCompletedArePruned);
        runner.Run("Fluent History page exposes resume and cancel for parked conversions",
            TestFluentParkedTaskEntryPoint);
        runner.Run("CLI Dashboard History shows every parked task",
            TestCliDashboardParkedTaskVisibility);
        runner.Run("Task queue: resume claim validates status, files, index, and single ownership",
            TestClaimParkedTaskForResume);
        runner.Run("CLI Dashboard History page exposes resume and cancel for parked conversions",
            TestCliDashboardParkedTaskResumeAndCancelEntryPoint);
        runner.Run("Conversion lifecycle preserves an explicit start time",
            TestConversionLifecyclePreservesExplicitStartTime);
        runner.Run("Conversion lifecycle records dispatch failure to history and cleans up task tracking",
            TestConversionLifecycleRecordsDispatchFailure);
        runner.Run("Conversion lifecycle owns per-file resume checkpoints",
            TestConversionLifecycleOwnsResumeCheckpoint);
        runner.Run("Conversion task cleanup removes completed presentation records",
            TestConversionTaskCleanupRemovesTask);
        runner.Run("Conversion lifecycle task id matches its persisted record",
            TestConversionLifecycleTaskIdMatchesPersistedRecord);
        runner.Run("Conversion output planning preserves each source directory",
            TestConversionOutputPlanningPreservesSourceDirectories);
        runner.Run("Conversion lifecycle runner preserves per-file parking semantics",
            TestConversionLifecycleRunnerPreservesParking);
        runner.Run("Conversion lifecycle runner preserves single-output cancellation semantics",
            TestConversionLifecycleRunnerPreservesCancellation);
        runner.Run("Conversion lifecycle runner preserves per-file failure classification",
            TestConversionLifecycleRunnerPreservesFailureClassification);
        runner.Run("Conversion lifecycle delegates to custom task lifecycle repository",
            TestConversionLifecycleDelegatesToCustomRepository);
        runner.Run("Conversion task cleanup delegates to custom task lifecycle repository",
            TestConversionTaskCleanupDelegatesToCustomRepository);
        runner.Run("Conversion lifecycle rolls back created task when in-progress transition fails",
            TestConversionLifecycleRollsBackTaskWhenInProgressFails);
        runner.RunGuard("Conversion lifecycle: Native and Fluent runners share task tracking",
            TestConversionLifecycleIsShared);
    }

    private static void TestConversionTaskCleanupRemovesTask()
    {
        string taskId = ClickraStorage.StartTask(CmdDecryptPdf, 1, TestInDir + FileA);
        Assert.True(ClickraStorage.GetTask(taskId).HasValue, "Task must exist before application cleanup.");
        ConversionTaskCleanup.Delete(taskId);
        Assert.False(ClickraStorage.GetTask(taskId).HasValue, "Application cleanup must remove the task record.");
    }

    private static void TestConversionLifecycleOwnsResumeCheckpoint()
    {
        ConversionTaskLifecycle lifecycle = ConversionTaskLifecycle.Start(
            CmdDecryptPdf,
            new[] { TestInDir + FileA1, TestInDir + FileA2 });
        try
        {
            lifecycle.RecordFileStarting(1);
            HistoryEntry? entry = ClickraStorage.GetTask(lifecycle.TaskId);
            Assert.True(entry.HasValue && entry.Value.CurrentIndex == 1,
                "The shared lifecycle must persist the current file index for resume.");
        }
        finally
        {
            lifecycle.Delete();
        }
    }

    private static void TestConversionLifecycleTaskIdMatchesPersistedRecord()
    {
        ConversionTaskLifecycle lifecycle = ConversionTaskLifecycle.Start(
            CmdDecryptPdf,
            new[] { TestInDir + FileA });
        try
        {
            HistoryEntry? entry = ClickraStorage.GetTask(lifecycle.TaskId);
            Assert.True(entry.HasValue, "A started conversion lifecycle must persist its task record.");
            Assert.Equal(lifecycle.TaskId, entry!.Value.Id);
        }
        finally
        {
            lifecycle.Delete();
        }
    }

    private static void TestConversionLifecyclePreservesExplicitStartTime()
    {
        const string expectedStartTime = "2026-10-10 02:30:00";
        ConversionTaskLifecycle lifecycle = ConversionTaskLifecycle.Start(
            CmdMergePdf,
            Array.Empty<string>(),
            startTimeOverride: expectedStartTime);
        try
        {
            Assert.Equal(expectedStartTime, lifecycle.StartTime);
        }
        finally
        {
            lifecycle.Delete();
        }
    }

    private static void TestConversionLifecycleRecordsDispatchFailure()
    {
        const string expectedStartTime = "2026-10-10 03:00:00";
        const string expectedError = "Invalid CLI dispatch argument";
        ConversionTaskLifecycle.RecordDispatchFailure(
            CmdMergePdf,
            expectedError,
            startTime: expectedStartTime);

        var history = ClickraStorage.GetHistory(10);
        Assert.True(
            history.Any(h => h.Command == CmdMergePdf
                             && !h.IsSuccess
                             && h.Time == expectedStartTime
                             && h.ErrorMessage == expectedError),
            "RecordDispatchFailure must append a failed history entry.");

        var activeTasks = ClickraStorage.GetActiveTasks();
        Assert.False(
            activeTasks.Any(t => t.Command == CmdMergePdf && t.Time == expectedStartTime),
            "RecordDispatchFailure must not leave active task tracking records behind.");
    }

    private const string MockTaskId = "mock-task-123";
    private const string MockCleanupTaskId = "mock-task-456";

    private static void TestConversionLifecycleDelegatesToCustomRepository()
    {
        var repo = new RecordingTaskLifecycleRepository();
        var lifecycle = ConversionTaskLifecycle.Start(
            CmdDecryptPdf,
            new[] { "file1.pdf", "file2.pdf" },
            repository: repo);

        Assert.Equal(MockTaskId, lifecycle.TaskId);
        Assert.True(repo.StartedTasks.Count == 1, "Must start task in repository.");
        Assert.Equal(CmdDecryptPdf, repo.StartedTasks[0].Command);
        Assert.Equal(2L, repo.StartedTasks[0].FileCount);
        Assert.Equal("file1.pdf;file2.pdf", repo.StartedTasks[0].InputPaths ?? "");

        Assert.True(repo.InProgressTasks.Count == 1, "Must mark in progress exactly once.");
        Assert.Equal(MockTaskId, repo.InProgressTasks[0]);

        lifecycle.RecordFileStarting(1);
        Assert.True(repo.IndexUpdates.Count == 1, "Must update index exactly once.");
        Assert.Equal(MockTaskId, repo.IndexUpdates[0].TaskId);
        Assert.Equal(1L, repo.IndexUpdates[0].Index);

        lifecycle.CompleteSuccess("output.pdf");
        Assert.True(repo.CompletedTasks.Count == 1, "Must complete task exactly once.");
        Assert.Equal(MockTaskId, repo.CompletedTasks[0].TaskId);
        Assert.True(repo.CompletedTasks[0].Request.IsSuccess, "Completion must be marked success.");
        Assert.Equal("output.pdf", repo.CompletedTasks[0].Request.OutputPath ?? "");

        lifecycle.Park("Need password", 2);
        Assert.True(repo.ParkedTasks.Count == 1, "Must park task exactly once.");
        Assert.Equal(MockTaskId, repo.ParkedTasks[0].TaskId);
        Assert.Equal("Need password", repo.ParkedTasks[0].Reason);
        Assert.Equal(2L, repo.ParkedTasks[0].NextIndex);

        lifecycle.Delete();
        Assert.True(repo.DeletedTasks.Count == 1, "Must delete task exactly once.");
        Assert.Equal(MockTaskId, repo.DeletedTasks[0]);
    }

    private static void TestConversionTaskCleanupDelegatesToCustomRepository()
    {
        var repo = new RecordingTaskLifecycleRepository();
        ConversionTaskCleanup.Delete(MockCleanupTaskId, repository: repo);
        Assert.True(repo.DeletedTasks.Count == 1, "Must delete task via repository.");
        Assert.Equal(MockCleanupTaskId, repo.DeletedTasks[0]);
    }

    private static void TestConversionLifecycleRollsBackTaskWhenInProgressFails()
    {
        var repo = new FailingInProgressLifecycleRepository();
        Assert.Throws<InvalidOperationException>(() => ConversionTaskLifecycle.Start(
            CmdDecryptPdf,
            new[] { "file1.pdf" },
            bestEffort: false,
            repository: repo));

        Assert.True(repo.StartCalled, "Start must be called.");
        Assert.Equal(MockTaskId, repo.DeletedTaskId ?? "");
    }

    private sealed class RecordingTaskLifecycleRepository : ITaskLifecycleRepository
    {
        public readonly List<(string Command, int FileCount, string? InputPaths)> StartedTasks = new();
        public readonly List<string> InProgressTasks = new();
        public readonly List<(string TaskId, int Index)> IndexUpdates = new();
        public readonly List<(string TaskId, string Command, TaskCompletionRequest Request)> CompletedTasks = new();
        public readonly List<(string TaskId, string Reason, int NextIndex)> ParkedTasks = new();
        public readonly List<string> DeletedTasks = new();

        public string Start(string command, int fileCount, string? inputPaths = null)
        {
            StartedTasks.Add((command, fileCount, inputPaths));
            return MockTaskId;
        }

        public void SetInProgress(string taskId) => InProgressTasks.Add(taskId);

        public void SetIndex(string taskId, int index) => IndexUpdates.Add((taskId, index));

        public void Complete(string taskId, string command, TaskCompletionRequest request) =>
            CompletedTasks.Add((taskId, command, request));

        public void Park(string taskId, string reason, int nextIndex) =>
            ParkedTasks.Add((taskId, reason, nextIndex));

        public void Delete(string taskId) => DeletedTasks.Add(taskId);
    }

    private sealed class FailingInProgressLifecycleRepository : ITaskLifecycleRepository
    {
        public bool StartCalled { get; private set; }
        public string? DeletedTaskId { get; private set; }

        public string Start(string command, int fileCount, string? inputPaths = null)
        {
            StartCalled = true;
            return MockTaskId;
        }

        public void SetInProgress(string taskId) => throw new InvalidOperationException("Failed to set in progress");
        public void SetIndex(string taskId, int index) { }
        public void Complete(string taskId, string command, TaskCompletionRequest request) { }
        public void Park(string taskId, string reason, int nextIndex) { }
        public void Delete(string taskId) => DeletedTaskId = taskId;
    }

    private static void TestConversionOutputPlanningPreservesSourceDirectories()
    {
        string previous = ClickraStorage.GetSetting(ClickraSettings.OutputDir);
        try
        {
            ClickraStorage.SaveSetting(ClickraSettings.OutputDir, ClickraSettings.DefaultOutputDirSource);
            RunWithTempDirectory(tempDir =>
            {
                string firstDir = Path.Combine(tempDir, "first");
                string secondDir = Path.Combine(tempDir, "second");
                Directory.CreateDirectory(firstDir);
                Directory.CreateDirectory(secondDir);
                string first = Path.Combine(firstDir, "a.pdf");
                string second = Path.Combine(secondDir, "b.pdf");

                ConversionPlan plan = new CompressPdfUseCase().Plan(new ConversionRequest(
                    CompressPdfUseCase.CommandName,
                    new List<string> { first, second }));
                IReadOnlyList<string> outputs = plan.Outputs;

                Assert.Equal(Path.Combine(firstDir, "a_compressed.pdf"), outputs[0]);
                Assert.Equal(Path.Combine(secondDir, "b_compressed.pdf"), outputs[1]);
            });
        }
        finally
        {
            ClickraStorage.SaveSetting(ClickraSettings.OutputDir, previous);
        }
    }

    private static void TestConversionLifecycleRunnerPreservesParking()
    {
        RunWithTempDirectory(tempDir =>
        {
            string input = Path.Combine(tempDir, "park.pdf");
            File.WriteAllText(input, LifecycleFixtureContent);
            var useCase = new ParkingPerFileUseCase();
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                CompressPdfUseCase.CommandName,
                new[] { input },
                TrackTaskLifecycle: false));

            ConversionResult result = useCase.ExecuteAsync(
                    plan,
                    CreateNoOpConversionInteraction(),
                    progress: null)
                .GetAwaiter()
                .GetResult();

            Assert.True(result.Status == ConversionResultStatus.Parked, $"Expected Parked, got {result.Status}.");
            Assert.Equal("test park", result.Error ?? "");
            Assert.Equal(0, result.CompletedFiles);
        });
    }

    private static void TestConversionLifecycleRunnerPreservesCancellation()
    {
        RunWithTempDirectory(tempDir =>
        {
            string first = Path.Combine(tempDir, "a.pdf");
            string second = Path.Combine(tempDir, "b.pdf");
            File.WriteAllText(first, LifecycleFixtureContent);
            File.WriteAllText(second, LifecycleFixtureContent);
            var useCase = new CancelingSingleOutputUseCase();
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                MergePdfUseCase.CommandName,
                new[] { first, second },
                TrackTaskLifecycle: false));

            ConversionResult result = useCase.ExecuteAsync(
                    plan,
                    CreateNoOpConversionInteraction(),
                    progress: null)
                .GetAwaiter()
                .GetResult();

            Assert.True(result.Status == ConversionResultStatus.Canceled, $"Expected Canceled, got {result.Status}.");
            Assert.True(result.Error is null, "Cancellation must preserve a null presentation error.");
            Assert.Equal(0, result.CompletedFiles);
        });
    }

    private static void TestConversionLifecycleRunnerPreservesFailureClassification()
    {
        RunWithTempDirectory(tempDir =>
        {
            string input = Path.Combine(tempDir, "missing.pdf");
            File.WriteAllText(input, LifecycleFixtureContent);
            var useCase = new ClassifiedFailurePerFileUseCase();
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                CompressPdfUseCase.CommandName,
                new[] { input },
                TrackTaskLifecycle: false));

            ConversionResult result = useCase.ExecuteAsync(
                    plan,
                    CreateNoOpConversionInteraction(),
                    progress: null)
                .GetAwaiter()
                .GetResult();

            Assert.True(result.Status == ConversionResultStatus.Failed, $"Expected Failed, got {result.Status}.");
            Assert.True(
                result.FailureKind == ConversionFailureKind.FileNotFound,
                $"Expected FileNotFound classification, got {result.FailureKind}.");
            Assert.Equal(0, result.CompletedFiles);
        });
    }

    private static void TestConversionLifecycleIsShared()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string native = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, "Progress", "ProgressWindow.Process.cs"));
        string nativeAdapter = File.ReadAllText(Path.Combine(root, "src", CliProjectDirectory, "Progress", "NativeConversionExecutionAdapter.cs"));
        string fluentMain = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, FluentMainPageFile));
        string fluentTask = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "TaskProgressPage.xaml.cs"));
        string lifecycle = File.ReadAllText(Path.Combine(root, "src", CoreProjectDirectory, ApplicationDirectory, "ConversionTaskLifecycle.cs"));
        string perFileUseCaseBase = File.ReadAllText(Path.Combine(root, "src", CoreProjectDirectory, ApplicationDirectory, "PerFileConversionUseCaseBase.cs"));
        string markdownUseCaseBase = File.ReadAllText(Path.Combine(root, "src", CoreProjectDirectory, "Modules", "Doc", "Shared", "Application", "MarkdownConversionUseCaseBase.cs"));

        foreach ((string name, string source) in new[] { ("ProgressWindow", native), ("FluentMain", fluentMain), ("FluentTask", fluentTask) })
        {
            Assert.False(source.Contains("ClickraStorage.StartTask(", StringComparison.Ordinal),
                $"{name} must not create task records outside the shared conversion lifecycle.");
            Assert.False(source.Contains("ClickraStorage.CompleteTask(", StringComparison.Ordinal),
                $"{name} must not complete task records outside the shared conversion lifecycle.");
        }

        Assert.True(lifecycle.Contains("ClickraStorage.StartTask(", StringComparison.Ordinal)
                    && lifecycle.Contains("ClickraStorage.CompleteTask(", StringComparison.Ordinal)
                    && lifecycle.Contains("ClickraStorage.ParkTask(", StringComparison.Ordinal),
            "ConversionTaskLifecycle must own the shared task state transitions.");

        Assert.False(native.Contains("ConvertCommandRegistry.EstimateOutputs(cmd, currentFiles, _outputDirOverride)", StringComparison.Ordinal),
            "Native product commands must not fall back to legacy registry output planning after catalog migration.");
        Assert.False(native.Contains("private static string GetOutputPath(", StringComparison.Ordinal),
            "Native conversion history must not keep a second command-to-output policy.");

        Assert.False(native.Contains("RunSharedCommand(", StringComparison.Ordinal)
                     || native.Contains("ConvertCommandRunner.Run(", StringComparison.Ordinal),
            "Native product commands must not retain the legacy generic runner fallback.");
        Assert.False(native.Contains("ConversionUseCases.", StringComparison.Ordinal)
                     || native.Contains(".ExecuteAsync(", StringComparison.Ordinal),
            "ProgressWindow must delegate application-use-case routing and execution to NativeConversionExecutionAdapter.");
        Assert.True(native.Contains("new NativeConversionExecutionAdapter(", StringComparison.Ordinal)
                    && nativeAdapter.Contains("ConversionUseCases.GetRequired", StringComparison.Ordinal)
                    && (nativeAdapter.Contains("ExecuteAsync(", StringComparison.Ordinal) || nativeAdapter.Contains("ConversionExecutionService.Execute(", StringComparison.Ordinal)),
            "NativeConversionExecutionAdapter must own the application-use-case routing moved out of ProgressWindow.");
        Assert.False(fluentMain.Contains("ConvertCommandRunner", StringComparison.Ordinal)
                     || fluentTask.Contains("ConvertCommandRunner", StringComparison.Ordinal)
                     || fluentMain.Contains("ConvertCommandRegistry.EstimateOutputs", StringComparison.Ordinal)
                     || fluentTask.Contains("ConvertCommandRegistry.EstimateOutputs", StringComparison.Ordinal),
            "Fluent product commands must not retain legacy runner or output-planning fallbacks.");
        Assert.True(native.Contains("new NativeConversionExecutionAdapter(", StringComparison.Ordinal)
                    && nativeAdapter.Contains("new TaskIdExecutionObserver(_taskStarted, _recordTaskIndex)", StringComparison.Ordinal)
                    && perFileUseCaseBase.Contains("observer?.OnFileStarting(i)", StringComparison.Ordinal)
                    && markdownUseCaseBase.Contains(": PerFileConversionUseCaseBase", StringComparison.Ordinal)
                    && markdownUseCaseBase.Contains("plan.ResumeStartIndex;", StringComparison.Ordinal),
            "Migrated per-file dispatch must preserve the Native resume checkpoint before processing each file.");
        foreach (string sharedCommand in new[] { "ppt2pdf", "word2pdf", "excel2pdf", "md2pdf", "md2word", CmdMergePdf, "img-merge", "img-stitch", "img-compress", "img-to-png" })
        {
            Assert.False(native.Contains($"case \"{sharedCommand}\":", StringComparison.Ordinal),
                $"{sharedCommand} must not keep a second Native dispatch branch.");
        }
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
            ParkedTaskController.Cancel(a);
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
            ParkedTaskController.Cancel(a);
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

        string code = string.Concat(
            File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, FluentMainPageFile)),
            File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, FluentHistoryPageFile)));
        string xaml = File.ReadAllText(Path.Combine(root, "src", FluentProjectDirectory, "MainPage.xaml"));

        Assert.True(xaml.Contains("ParkedTasksSection", StringComparison.Ordinal), "The History page must render a parked-conversions section.");
        Assert.True(code.Contains("HistoryPresentationController", StringComparison.Ordinal)
                    && code.Contains("_historyPresentation.ParkedItems", StringComparison.Ordinal),
            "The History page must list parked conversions through the shared Presentation owner.");
        Assert.False(code.Contains("ClickraStorage.GetParkedTasks", StringComparison.Ordinal),
            "The History page must not enumerate parked conversions itself; HistoryFeed is the one source for both UIs.");
        Assert.True(code.Contains("_historyPresentation.CancelParked", StringComparison.Ordinal),
            "The History page must offer cancel through the shared Presentation history owner.");
        Assert.True(code.Contains("OpenTaskProgressWindow($\"resume {", StringComparison.Ordinal), "Resume must go through the shared resume entry point.");
        string feed = File.ReadAllText(Path.Combine(root, "src", CoreProjectDirectory, "Modules", "Sys", "HistoryTasks", "Presentation", "HistoryFeed.cs"));
        Assert.True(feed.Contains("HistoryQueryController.IsUserCanceledReason", StringComparison.Ordinal),
            "Cancelled rows must be recognised through the Application history-query boundary, including the CLI's legacy marker.");
        Assert.True(feed.Contains("status_canceled", StringComparison.Ordinal),
            "A cancelled row must resolve to the shared canceled status that both interfaces render.");

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
        foreach (string key in new[] { "task_parked_title", "task_parked_desc", "task_parked_cancel_confirm", "fluent_task_resume" })
        {
            Assert.True(code.Contains(key, StringComparison.Ordinal), $"{key} must be rendered by the parked-conversion UI.");
        }
        Assert.True(code.Contains("L(item.StatusKey)", StringComparison.Ordinal),
            "Row status wording must come from the shared item (status_canceled included), so both interfaces use one key.");
    }

    private static void TestParkedTasksNeverExpireWhileCompletedArePruned()
    {
        string parkedId = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + FileA);
        string completedId = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + FileA);
        try
        {
            ClickraStorage.SetTaskInProgress(parkedId);
            ClickraStorage.ParkTask(parkedId, "TestParked", 0);
            ClickraStorage.CompleteTask(completedId, CmdSplitPdf, new CompleteTaskRequest
            {
                StartTime = DateTime.UtcNow.AddMinutes(-30).ToString(StorageRuntime.DateTimeFormat),
                IsSuccess = true,
                EndTime = DateTime.UtcNow.AddMinutes(-20).ToString(StorageRuntime.DateTimeFormat),
                ElapsedMs = 100
            });

            string parkedPath = TaskRecordStore.PathFor(parkedId);
            string completedPath = TaskRecordStore.PathFor(completedId);

            Assert.True(File.Exists(parkedPath), "Parked task file must exist.");
            Assert.True(File.Exists(completedPath), "Completed task file must exist.");

            File.SetLastWriteTimeUtc(parkedPath, DateTime.UtcNow.AddDays(-400));
            File.SetLastWriteTimeUtc(completedPath, DateTime.UtcNow.AddMinutes(-20));

            TaskPruningCoordinator.Prune();

            Assert.True(File.Exists(parkedPath), "Parked task must never be auto-deleted by time even after 400 days.");
            HistoryEntry? parkedEntry = TaskRecordStore.Read(parkedId);
            Assert.True(parkedEntry.HasValue && parkedEntry.Value.Status == ConversionStatus.Parked,
                "Parked task must still be readable and parked.");

            Assert.False(File.Exists(completedPath), "Completed task file must be pruned after TTL.");
        }
        finally
        {
            ClickraStorage.DeleteTask(parkedId);
            ClickraStorage.DeleteTask(completedId);
        }
    }

    private static void TestCliDashboardParkedTaskVisibility()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepoRootNotFoundMessage);

        string dir = Path.Combine(root, "src", CliProjectDirectory, DashboardDirectory);
        string paint = File.ReadAllText(Path.Combine(dir, DashboardHistoryPaintFile));
        string lifecycle = File.ReadAllText(Path.Combine(dir, "DashboardWindow.Lifecycle.cs"));

        Assert.True(lifecycle.Contains("_historyPresentation.Refresh(", StringComparison.Ordinal),
            "The dashboard must refresh parked conversions through the shared Presentation history owner.");
        Assert.False(lifecycle.Contains("ClickraStorage.GetParkedTasks()", StringComparison.Ordinal),
            "The dashboard must not enumerate parked conversions itself; HistoryFeed is the one source for both UIs.");
        Assert.True(paint.Contains("ParkedItems", StringComparison.Ordinal),
            "The parked block must render ParkedItems.");
        Assert.True(paint.Contains("task_parked_title", StringComparison.Ordinal),
            "The parked block needs its localized heading.");
        void AssertPaintContains(string token, string failure) =>
            Assert.True(paint.Contains(token, StringComparison.Ordinal), failure);

        AssertPaintContains("task_parked_desc",
            "The parked block must show its description.");
        AssertPaintContains("item.Subtitle",
            "The parked row must render the shared subtitle instead of composing the file index itself.");
        Assert.False(paint.Contains("task_file_index", StringComparison.Ordinal),
            "The dashboard must not word the file index itself; HistoryItem owns that phrase.");
        Assert.True(paint.Contains("DrawParkedQueue(g,", StringComparison.Ordinal),
            "The History page must call the parked block.");

        string layout = File.ReadAllText(Path.Combine(dir, "DashboardWindow.cs"));
        Assert.True(layout.Contains("HistoryLayout.Build(", StringComparison.Ordinal),
            "The History column must be built through the shared Core layout.");
        Assert.True(layout.Contains("_historyPresentation.Feed.ParkedCount", StringComparison.Ordinal),
            "The parked rows must be counted into the column so the list below cannot overlap them.");
        Assert.True(layout.Contains("HistoryLayout.ContentHeight(", StringComparison.Ordinal),
            "The scroll height must come from the same shared History column.");

        foreach (string file in new[] { DashboardHistoryPaintFile, "DashboardWindow.HitTesting.cs", DashboardEventsHistoryFile, "DashboardWindow.Events.cs" })
        {
            string source = File.ReadAllText(Path.Combine(dir, file));
            Assert.True(source.Contains("HistoryBlock", StringComparison.Ordinal),
                $"{file} must take history row positions from the shared column.");
        }

        foreach (string file in new[] { DashboardHistoryPaintFile, "DashboardWindow.HitTesting.cs", DashboardEventsHistoryFile, "DashboardWindow.Events.cs", "DashboardWindow.cs" })
        {
            string source = File.ReadAllText(Path.Combine(dir, file));
            Assert.False(source.Contains("GetHistoryListStartY", StringComparison.Ordinal),
                $"{file} still computes the history start separately.");
            Assert.False(source.Contains("GetActiveHistoryCount", StringComparison.Ordinal),
                $"{file} still computes the history start separately.");
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
            ClickraStorage.CompleteTask(a, CmdSplitPdf, new CompleteTaskRequest
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
        string a = ClickraStorage.StartTask(CmdMergePdf, 2, TestInDir + "\\x.pdf;" + TestInDir + "\\y.pdf");
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
            var claim = ParkedTaskController.ClaimForResume(a);
            Assert.True(claim != null, "Parked task must be claimed before it resumes.");
            var resumed = ClickraStorage.GetTask(a);
            Assert.True(resumed.HasValue && resumed.Value.Status == ConversionStatus.InProgress,
                "Resumed task must be InProgress.");
            Assert.True(resumed.HasValue && resumed.Value.CurrentIndex == 1, "Resumed task must keep its next index.");
            ClickraStorage.CompleteTask(a, CmdDecryptPdf, new CompleteTaskRequest
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

    private static void TestClaimedParkedTaskReturnedToParkedWhenExecutionCannotProceed()
    {
        string a = ClickraStorage.StartTask(CmdDecryptPdf, 2, TestInDir + FileA1 + TestInDir + FileA2);
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            ClickraStorage.ParkTask(a, ParkReason, 1);
            var claim = ParkedTaskController.ClaimForResume(a);
            Assert.True(claim != null, "Parked task must be claimed before it resumes.");
            Assert.True(ClickraStorage.GetParkedTasks().All(t => t.Id != a), "Claimed task is no longer in parked list.");

            ParkedTaskController.ReturnToParked(a, "Busy with other files", 1);
            var returned = ClickraStorage.GetParkedTasks().FirstOrDefault(t => t.Id == a);
            Assert.True(returned.Id == a, "Returned task must be present in parked tasks.");
            Assert.True(returned.Status == ConversionStatus.Parked, $"Expected status Parked, got {returned.Status}.");
            Assert.Equal(1, returned.CurrentIndex);
        }
        finally { ClickraStorage.DeleteTask(a); }
    }

    private static void TestResumeClaimRefreshesPid()
    {
        string a = ClickraStorage.StartTask(CmdSplitPdf, 1, TestInDir + FileA);
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            var entry = ClickraStorage.GetTask(a);
            Assert.True(entry.HasValue && entry.Value.Pid == Environment.ProcessId,
                $"InProgress task must carry the current pid, got {entry?.Pid}.");
            ClickraStorage.ParkTask(a, ParkReason, 0);

            string path = Path.Combine(ClickraStorage.GetDataDir(), "tasks", $"task-{a}.tmp");
            var lines = File.ReadAllLines(path)
                .Select(line => line.StartsWith("Pid=", StringComparison.Ordinal) ? "Pid=0" : line)
                .ToArray();
            File.WriteAllLines(path, lines);

            Assert.True(ClickraStorage.ClaimParkedTaskForResume(a) != null,
                "A parked task must be claimed before it resumes.");
            var resumed = ClickraStorage.GetTask(a);
            Assert.True(resumed.HasValue && resumed.Value.Pid == Environment.ProcessId,
                $"Resume claim must refresh its pid to the current process, got {resumed?.Pid}.");
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
        File.WriteAllText(legacy, $"Time=2026-08-16 11:00:00\nCommand={CmdMergePdf}\nStatus=InProgress\n");
        string first = ClickraStorage.StartTask(CmdMergePdf, 2, TestInDir + "\\x.pdf;" + TestInDir + "\\y.pdf");
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

    private static void TestClaimParkedTaskForResume()
    {
        // 1. Invalid or missing taskId
        Assert.True(ClickraStorage.ClaimParkedTaskForResume(null!) == null, "Null taskId must return null.");
        Assert.True(ClickraStorage.ClaimParkedTaskForResume("") == null, "Empty taskId must return null.");
        Assert.True(ClickraStorage.ClaimParkedTaskForResume("non_existent_id") == null, "Non-existent taskId must return null.");

        // 2. Active (in progress) task cannot be resumed
        string a = ClickraStorage.StartTask(CmdSplitPdf, 2, TestInDir + FileA1 + TestInDir + FileA2);
        try
        {
            ClickraStorage.SetTaskInProgress(a);
            Assert.True(ClickraStorage.ClaimParkedTaskForResume(a) == null, "InProgress task cannot be resumed.");

            // 3. Parked task can be claimed with valid files and its persisted next index.
            ClickraStorage.ParkTask(a, ParkReason, 1);
            var info = ClickraStorage.ClaimParkedTaskForResume(a);
            Assert.True(info != null, "Parked task must return ResumedTaskInfo.");
            Assert.Equal(a, info!.TaskId);
            Assert.Equal(CmdSplitPdf, info.Command);
            Assert.Equal(2, info.Files.Count);
            Assert.Equal(1, info.StartIndex);
            Assert.True(ClickraStorage.GetTask(a)?.Status == ConversionStatus.InProgress,
                "A successful resume claim must atomically transition the task to InProgress.");
            Assert.True(ClickraStorage.ClaimParkedTaskForResume(a) == null,
                "A second consumer must not be able to claim the same parked task.");

            // 4. Re-park, then verify out-of-range index clamping on the next claim.
            ClickraStorage.ParkTask(a, ParkReason, 1);
            ClickraStorage.SetTaskIndex(a, 99);
            var clamped = ClickraStorage.ClaimParkedTaskForResume(a);
            Assert.True(clamped?.StartIndex == 2, "Index beyond files count must be clamped to file count.");

            // 5. Re-park and cancel; a removed task cannot be claimed.
            ClickraStorage.ParkTask(a, ParkReason, 1);
            ClickraStorage.CancelParkedTask(a);
            Assert.True(ClickraStorage.ClaimParkedTaskForResume(a) == null, "Cancelled task cannot be resumed.");
        }
        finally
        {
            ClickraStorage.DeleteTask(a);
        }
    }

    private static void TestCliDashboardParkedTaskResumeAndCancelEntryPoint()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        string dir = Path.Combine(root, "src", "Clickra.CLI", "Dashboard");
        string click = File.ReadAllText(Path.Combine(dir, DashboardEventsHistoryFile));
        string paint = File.ReadAllText(Path.Combine(dir, "DashboardWindow.Paint.History.cs"));
        string progress = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.cs"));

        // CLI Dashboard paints Resume and Cancel buttons using the shared localization keys
        Assert.True(paint.Contains("fluent_task_resume", StringComparison.Ordinal),
            "CLI Dashboard must render the Resume button using fluent_task_resume.");
        Assert.True(paint.Contains("dialog_cancel", StringComparison.Ordinal),
            "CLI Dashboard must render the Cancel button using dialog_cancel.");

        // Confirmation copy matches Fluent
        Assert.True(click.Contains("task_parked_cancel_confirm", StringComparison.Ordinal),
            "CLI Dashboard cancel click handler must prompt confirmation using task_parked_cancel_confirm.");

        // Cancellation invokes the shared Presentation history owner.
        Assert.True(click.Contains("_historyPresentation.CancelParked", StringComparison.Ordinal),
            "CLI Dashboard cancel click handler must invoke HistoryPresentationController.CancelParked.");

        // Resume launches ProgressWindow; the window owns the atomic Core claim at execution time.
        Assert.False(click.Contains("ClaimParkedTaskForResume", StringComparison.Ordinal),
            "CLI Dashboard must not claim a task before the progress window is ready to execute it.");
        Assert.True(click.Contains("ProgressWindow.ShowResume", StringComparison.Ordinal),
            "CLI Dashboard resume click handler must launch ProgressWindow.ShowResume.");
        Assert.True(progress.Contains("ParkedTaskController.ClaimForResume", StringComparison.Ordinal),
            "ProgressWindow.ShowResume must atomically claim the parked task through the application boundary before execution.");

        // Resume must not mark the task InProgress prematurely
        int resumeStart = click.IndexOf("if (action == ParkedActionResume)", StringComparison.Ordinal);
        Assert.True(resumeStart >= 0, "ParkedActionResume branch must exist in HandleParkedActionClick.");
        int resumeEnd = click.IndexOf("RefreshHistoryData()", resumeStart, StringComparison.Ordinal);
        string resumeBody = resumeEnd > resumeStart ? click[resumeStart..resumeEnd] : click[resumeStart..];
        Assert.False(resumeBody.Contains("SetTaskInProgress", StringComparison.Ordinal),
            "CLI click handler must not mark task InProgress before launching the progress window.");

        // ProgressWindow defines ShowResume
        Assert.True(progress.Contains("ShowResume(string taskId)", StringComparison.Ordinal),
            "ProgressWindow must declare ShowResume(string taskId).");
    }

    private sealed class ParkingPerFileUseCase : PerFileConversionUseCaseBase
    {
        public override string Command => CompressPdfUseCase.CommandName;
        protected override string UseCaseName => nameof(ParkingPerFileUseCase);
        protected override string InputRequirementError => "input required";
        protected override string UnsupportedInputError(string path) => path;
        protected override string OutputCountError => "output mismatch";
        protected override string GetOutputFileName(string input) =>
            Path.GetFileNameWithoutExtension(input) + "_park.pdf";

        protected override Task ExecuteFileAsync(
            int index,
            ConversionPlan plan,
            IConversionInteraction interaction,
            IProgress<ConversionProgress>? progress,
            object? executionState,
            CancellationToken cancellationToken) =>
            throw new ConversionParkedException("test park", index);
    }

    private sealed class ClassifiedFailurePerFileUseCase : PerFileConversionUseCaseBase
    {
        public override string Command => CompressPdfUseCase.CommandName;
        protected override string UseCaseName => nameof(ClassifiedFailurePerFileUseCase);
        protected override string InputRequirementError => "input required";
        protected override string UnsupportedInputError(string path) => path;
        protected override string OutputCountError => "output mismatch";
        protected override string GetOutputFileName(string input) =>
            Path.GetFileNameWithoutExtension(input) + "_failed.pdf";
        protected override ConversionFailureKind ClassifyFailure(Exception exception) =>
            exception is FileNotFoundException
                ? ConversionFailureKind.FileNotFound
                : ConversionFailureKind.None;

        protected override Task ExecuteFileAsync(
            int index,
            ConversionPlan plan,
            IConversionInteraction interaction,
            IProgress<ConversionProgress>? progress,
            object? executionState,
            CancellationToken cancellationToken) =>
            throw new FileNotFoundException("test missing");
    }

    private sealed class CancelingSingleOutputUseCase : SingleOutputConversionUseCaseBase
    {
        public override string Command => MergePdfUseCase.CommandName;
        protected override string UseCaseName => nameof(CancelingSingleOutputUseCase);
        protected override string InputRequirementError => "two inputs required";
        protected override string UnsupportedInputError(string path) => path;
        protected override string PlannedOutputFileName => "canceled.pdf";
        protected override string OutputCountError => "output mismatch";

        protected override Task ExecuteSingleAsync(
            ConversionPlan plan,
            IProgress<ConversionProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new OperationCanceledException();
    }
}
