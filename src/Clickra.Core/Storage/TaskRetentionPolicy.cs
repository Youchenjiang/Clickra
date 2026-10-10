using System;

namespace Clickra.Core;

/// <summary>Pure parked-task retention calculations kept separate from task-file persistence.</summary>
internal static class TaskRetentionPolicy
{
    internal static ClickraStorage.ParkedRetentionInfo CalculateInfo(
        int retentionDays,
        TimeSpan age,
        bool isTaskOverride = false)
    {
        if (retentionDays <= 0)
        {
            return new ClickraStorage.ParkedRetentionInfo(
                IsUnlimited: true,
                RemainingDays: 0,
                RemainingTime: TimeSpan.Zero,
                IsExpiringSoon: false,
                HasExpired: false,
                IsTaskOverride: isTaskOverride);
        }

        TimeSpan remaining = TimeSpan.FromDays(retentionDays) - age;
        bool isExpired = remaining.TotalSeconds <= 0;
        int remainingDays = isExpired ? 0 : Math.Max(1, (int)Math.Ceiling(remaining.TotalDays));
        bool isExpiringSoon = !isExpired && remaining.TotalHours < 24.0;

        return new ClickraStorage.ParkedRetentionInfo(
            IsUnlimited: false,
            RemainingDays: remainingDays,
            RemainingTime: remaining,
            IsExpiringSoon: isExpiringSoon,
            HasExpired: isExpired,
            IsTaskOverride: isTaskOverride);
    }

    internal static int? CalculateAdjustedDays(int currentDays, TimeSpan age, int deltaDays)
    {
        if (deltaDays == 0) return null;

        int next;
        if (currentDays <= 0)
        {
            if (deltaDays > 0) return null;
            next = ClickraSettings.MinParkedRetentionDays + 1;
        }
        else
        {
            double shortest = Math.Floor(age.TotalDays) + 1.0;
            next = (int)Math.Ceiling(Math.Max(currentDays + (double)deltaDays, shortest));
        }

        return ClickraSettings.ClampNumericSetting(ClickraSettings.ParkedTaskRetention, next);
    }

    internal static bool IsExpired(
        bool finished,
        bool parked,
        TimeSpan age,
        int parkedRetentionDays,
        int completedTaskTtlMinutes)
    {
        if (finished) return age.TotalMinutes > completedTaskTtlMinutes;
        return parked
            && parkedRetentionDays > 0
            && CalculateInfo(parkedRetentionDays, age).HasExpired;
    }
}
