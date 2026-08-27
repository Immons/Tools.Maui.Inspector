namespace SampleApp.LeakDemo;

/// <summary>
/// State the app parks between screens: built once, bound to a control while the overlay is up,
/// and kept by the page's view model afterwards. Nothing is bound to it in between — which used to
/// make it look detached, although it is exactly what the next screen will use.
/// </summary>
public sealed class SharedFilterState
{
	public string Query { get; set; } = "";

	public int Uses { get; private set; }

	public void Use() => Uses++;
}
