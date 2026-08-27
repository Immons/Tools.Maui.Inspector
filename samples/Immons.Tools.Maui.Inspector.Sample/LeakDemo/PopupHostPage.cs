namespace SampleApp.LeakDemo;

/// <summary>
/// A generic host page: the pattern every popup library uses — one page type showing whatever
/// content it was handed. Its own name says nothing about what is on screen, which is what the
/// navigation ledger's label has to work around.
/// </summary>
public sealed class PopupHostPage : ContentPage
{
	public PopupHostPage()
	{
		BindingContext = new LeakyViewModel();
		Content = new LeakyOverlay();
		Padding = new Thickness(20, 60);
	}
}
