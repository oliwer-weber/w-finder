using Autodesk.Revit.UI;

namespace w_finder.Helpers;

/// <summary>
/// Allows WPF (off-thread) code to safely execute actions on the Revit main thread.
/// Usage: call RevitBackgroundTask.Raise(action) from WPF.
/// </summary>
public class RevitBackgroundTask : IExternalEventHandler
{
    private Action<UIApplication>? _action;

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

        _instance._action = action;
        _externalEvent.Raise();
    }

    public void Execute(UIApplication app)
    {
        _action?.Invoke(app);
    }

    public string GetName() => nameof(RevitBackgroundTask);
}
