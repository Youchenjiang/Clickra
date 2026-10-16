using System;
using System.Collections.Generic;

namespace Clickra.Core.Storage;

public static partial class ClickraStorage
{

    public static string StartTask(string command, int fileCount, string? inputPaths = null) =>
        TaskQueueStore.Start(command, fileCount, inputPaths);

    public static void SetTaskInProgress(string taskId) => TaskQueueStore.SetInProgress(taskId);

    public static void SetTaskIndex(string taskId, int index) => TaskQueueStore.SetIndex(taskId, index);

    public static void CompleteTask(string taskId, string command, CompleteTaskRequest request) =>
        TaskQueueStore.Complete(taskId, command, request);

    public static void ParkTask(string taskId, string reason, int nextIndex) =>
        ParkedTaskCoordinator.Park(taskId, reason, nextIndex);

    public static List<HistoryEntry> GetParkedTasks() => ParkedTaskCoordinator.GetAll();

    public static void CancelParkedTask(string taskId) => ParkedTaskCoordinator.Cancel(taskId);

    public static ResumedTaskInfo? ClaimParkedTaskForResume(string taskId) =>
        ParkedTaskCoordinator.ClaimForResume(taskId);

    public static int GetParkedRetentionDays() => ParkedTaskCoordinator.GetRetentionDays();

    public static void SetParkedRetentionOverride(string taskId, int? days) =>
        ParkedTaskCoordinator.SetRetentionOverride(taskId, days);

    public static int? AdjustParkedRetention(string taskId, int deltaDays) =>
        ParkedTaskCoordinator.AdjustRetention(taskId, deltaDays);

    public static ParkedRetentionInfo CalculateRetentionInfo(int retentionDays, TimeSpan age, bool isTaskOverride = false) =>
        TaskRetentionPolicy.CalculateInfo(retentionDays, age, isTaskOverride);

    public static ParkedRetentionInfo GetParkedRetentionInfo(string taskId) =>
        ParkedTaskCoordinator.GetRetentionInfo(taskId);

    public static void DeleteTask(string taskId) => TaskQueueStore.Delete(taskId);

    public static HistoryEntry? GetTask(string taskId) => TaskQueueStore.Get(taskId);

    public static List<HistoryEntry> GetActiveTasks() => TaskQueueStore.GetActive();
}
