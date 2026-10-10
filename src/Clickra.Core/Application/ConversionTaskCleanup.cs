namespace Clickra.Core.Application;

/// <summary>Application-level task cleanup used by presentation adapters after result display.</summary>
public static class ConversionTaskCleanup
{
    public static void Delete(string taskId, bool bestEffort = false)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return;

        if (!bestEffort)
        {
            ClickraStorage.DeleteTask(taskId);
            return;
        }

        try
        {
            ClickraStorage.DeleteTask(taskId);
        }
        catch
        {
            // Native presentation treats task cleanup as advisory after the conversion result is final.
        }
    }
}
