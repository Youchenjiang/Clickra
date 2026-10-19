using System;
using Clickra.Core.Modules.HistoryTasks.Application;
using Clickra.Core.Storage;

namespace Clickra.Core.Application;

/// <summary>Application-level task cleanup used by presentation adapters after result display.</summary>
public static class ConversionTaskCleanup
{
    public static void Delete(string taskId, bool bestEffort = false, ITaskLifecycleRepository? repository = null)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return;

        Action action = repository != null
            ? () => repository.Delete(taskId)
            : () => ClickraStorage.DeleteTask(taskId);

        if (!bestEffort)
        {
            action();
            return;
        }

        try
        {
            action();
        }
        catch
        {
            // Native presentation treats task cleanup as advisory after the conversion result is final.
        }
    }
}
