namespace SampleApp.LeakDemo;

/// <summary>
/// A "screen" that is not a Page: a layer an app's own overlay host adds to the current page and
/// takes away again. The inspector cannot recognise it as navigation, so the app reports it with
/// Immons.Tools.Maui.Inspector.Navigation — and it then gets a ledger entry and a verdict like any pushed page.
/// It leaks on purpose: its view model subscribes to the static event and never unsubscribes.
/// </summary>
public sealed class LeakyOverlay : Border
{
	static int _created;

	public LeakyOverlay(SharedFilterState? shared = null)
	{
		Number = ++_created;
		BindingContext = new LeakyViewModel();
		if (shared != null)
		{
			// Bound to the shared state for as long as the layer is up — the case that used to be
			// mistaken for a leak once the layer went away.
			shared.Use();
			Content = new Label { Text = $"Shared filter used {shared.Uses}×", BindingContext = shared };
		}
		Padding = new Thickness(14, 10);
		Stroke = Color.FromArgb("#512BD4");
		Background = Color.FromArgb("#EFEAFB");
		Content ??= new Label
		{
			Text = $"Leaky overlay #{Number} — a layer, not a page. Reported through Inspector.Navigation.",
			FontSize = 13,
			TextColor = Color.FromArgb("#26193F"),
		};
		LeakSource.Tick += OnTick;
	}

	public int Number { get; }

	void OnTick(object? sender, EventArgs e) => Opacity = Opacity > 0.5 ? 0.9 : 1;
}
