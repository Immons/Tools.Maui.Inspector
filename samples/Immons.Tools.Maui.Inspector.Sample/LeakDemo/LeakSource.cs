namespace SampleApp.LeakDemo;

/// <summary>
/// A static event — the classic MAUI leak. Whoever subscribes with an instance handler is kept
/// alive by the event's invocation list for the rest of the process, page and view model included.
/// </summary>
public static class LeakSource
{
	public static event EventHandler? Tick;

	public static int Subscribers => Tick?.GetInvocationList().Length ?? 0;

	public static void Raise() => Tick?.Invoke(null, EventArgs.Empty);
}
