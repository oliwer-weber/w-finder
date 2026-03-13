using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace w_finder.Helpers;

/// <summary>
/// Allows WPF (off-thread) code to safely execute actions on the Revit main thread.
/// Uses a queue so multiple Raise() calls never overwrite each other.
/// Usage: call RevitBackgroundTask.Raise(action) from WPF.
/// </summary>
public class RevitBackgroundTask : IExternalEventHandler
{
    private readonly ConcurrentQueue<Action<UIApplication>> _queue = new();

    // Singleton instance and its ExternalEvent wrapper.
    private static RevitBackgroundTask? _instance;
    private static ExternalEvent? _externalEvent;

    /// <summary>
    /// Call once during App.OnStartup to initialize.
    /// </summary>
    public static void Initialize()
    {
        _instance = new RevitBackgroundTask();
        _externalEvent = ExternalEvent.Create(_instance);
    }

    /// <summary>
    /// Schedule an action to run on the Revit main thread.
    /// </summary>
    public static void Raise(Action<UIApplication> action)
    {
        if (_instance == null || _externalEvent == null)
            throw new InvalidOperationException("RevitBackgroundTask has not been initialized.");

        _instance._queue.Enqueue(action);
        _externalEvent.Raise();
    }

    public void Execute(UIApplication app)
    {
        // Only process actions that were queued BEFORE this Execute started.
        // Actions enqueued during execution (e.g. chained Raise calls) will be
        // picked up by the next ExternalEvent cycle, giving Revit an idle frame
        // to process state changes like selection updates.
        int count = _queue.Count;
        for (int i = 0; i < count; i++)
        {
            if (_queue.TryDequeue(out var action))
                action.Invoke(app);
        }
    }

    public string GetName() => nameof(RevitBackgroundTask);
}
