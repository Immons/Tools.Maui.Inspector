using Microsoft.Extensions.Logging;

namespace SampleApp;

public partial class MainPage : ContentPage
{
	int _count;
	bool _autoShown;
	bool _leaked;
	bool _overlaid;

	/// <summary>Parked between screens on purpose: bound to the overlay while it is up, kept here after.</summary>
	readonly LeakDemo.SharedFilterState _sharedFilter = new();

	public MainPage()
	{
		InitializeComponent();
		BindingContext = new DemoViewModel();

		var tap = new TapGestureRecognizer();
		tap.Tapped += async (_, _) =>
		{
			if (Immons.Tools.Maui.Inspector.MauiInspector.WebServerUrl is not { } url)
				return;
			try
			{
				await Launcher.Default.OpenAsync(url);
			}
			catch
			{
				// no browser available on this device/simulator
			}
		};
		WebServerLabel.GestureRecognizers.Add(tap);
	}

	static readonly HttpClient DemoClient = new(new Immons.Tools.Maui.Inspector.MauiInspectorHttpHandler());

	async void OnCounterClicked(object? sender, EventArgs e)
	{
		_count++;
		CounterBtn.Text = _count == 1 ? "Clicked 1 time" : $"Clicked {_count} times";

		// Demo traffic for the inspector's Network and Logs tabs.
		Handler?.MauiContext?.Services
			.GetService<Microsoft.Extensions.Logging.ILogger<MainPage>>()
			?.LogInformation("Counter clicked {Count} time(s)", _count);
		try
		{
			DemoClient.DefaultRequestHeaders.UserAgent.ParseAdd("Inspector.Sample");
			await DemoClient.GetStringAsync("https://raw.githubusercontent.com/dotnet/maui/main/README.md");
		}
		catch
		{
			// offline is fine — the failed request still shows up in the Network tab
		}
	}

	abstract class DemoViewModelBase
	{
		public string? NavigationParameter { get; set; }
	}

	/// <summary>Tiny view model so the inspector's ViewModel section has something to show.
	/// Hides a base property on purpose — regression test for AmbiguousMatchException.</summary>
	sealed class DemoViewModel : DemoViewModelBase, System.ComponentModel.INotifyPropertyChanged
	{
		public new string NavigationParameter { get; set; } = "hidden-property-test";
		string _greeting = "Hello from the view model";
		int _counter = 7;
		bool _isBusy;

		public sealed record Fruit(int Id, string Name);

		public System.Collections.Generic.List<Fruit> Fruits { get; } =
		[
			new(1, "Apple"),
			new(2, "Banana"),
			new(3, "Cherry"),
		];

		public string Greeting
		{
			get => _greeting;
			set { _greeting = value; Raise(nameof(Greeting)); }
		}

		public int Counter
		{
			get => _counter;
			set { _counter = value; Raise(nameof(Counter)); }
		}

		public bool IsBusy
		{
			get => _isBusy;
			set { _isBusy = value; Raise(nameof(IsBusy)); }
		}

		public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

		void Raise(string name) =>
			PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
	}

	void UpdateWebServerLabel()
	{
		WebServerLabel.Text = Immons.Tools.Maui.Inspector.MauiInspector.WebServerUrl is { } url
			? $"Web inspector: {url}"
			: Immons.Tools.Maui.Inspector.MauiInspector.WebServerStartError is { } error
				? $"Web inspector failed: {error}"
				: "Web inspector: starting…";
	}

	void OnOpenInspectorClicked(object? sender, EventArgs e)
	{
		Immons.Tools.Maui.Inspector.MauiInspector.Show();
	}

	async void OnLeakClicked(object? sender, EventArgs e) => await Navigation.PushAsync(new LeakDemo.LeakyPage());

	/// <summary>
	/// The overlay case: a layer added to this page and removed again — never a Page, so the
	/// inspector only learns about it because the app reports it.
	/// </summary>
	async void OnOverlayClicked(object? sender, EventArgs e) => await ShowAndHideOverlays(2);

	async Task ShowAndHideOverlays(int count)
	{
		// A generic host page too: the ledger must name it after what it shows, not after itself.
		await Navigation.PushAsync(new LeakDemo.PopupHostPage());
		await Task.Delay(500);
		await Navigation.PopAsync();

		for (var i = 0; i < count; i++)
		{
			var overlay = new LeakDemo.LeakyOverlay(_sharedFilter);
			RootStack.Add(overlay);
			Immons.Tools.Maui.Inspector.Navigation.ReportPushed(overlay, $"LeakyOverlay #{overlay.Number}");
			await Task.Delay(700);
			RootStack.Remove(overlay);
			Immons.Tools.Maui.Inspector.Navigation.ReportPopped(overlay);
			await Task.Delay(300);
		}
	}

	/// <summary>Push and pop a few leaky pages in a row — what a Memory snapshot should then flag.</summary>
	async void OnLeakBatchClicked(object? sender, EventArgs e) => await PushAndPopLeakyPages(3);

	async Task PushAndPopLeakyPages(int count)
	{
		for (var i = 0; i < count; i++)
		{
			await Navigation.PushAsync(new LeakDemo.LeakyPage());
			await Task.Delay(400);
			await Navigation.PopAsync();
			await Task.Delay(200);
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		UpdateWebServerLabel();
		Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1), UpdateWebServerLabel);

		// Test hook: HV_AUTOSHOW=1 opens the inspector automatically (used by simulator smoke tests).
		if (!_autoShown && Environment.GetEnvironmentVariable("HV_AUTOSHOW") == "1")
		{
			_autoShown = true;
			Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(2), () =>
				Immons.Tools.Maui.Inspector.MauiInspector.Inspect(CardBorder));
		}

		// Test hook: HV_OVERLAY=n shows and hides n reported overlays after startup.
		if (!_overlaid && int.TryParse(Environment.GetEnvironmentVariable("HV_OVERLAY"), out var overlays) && overlays > 0)
		{
			_overlaid = true;
			Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), async () => await ShowAndHideOverlays(overlays));
		}

		// Test hook: HV_LEAK=n pushes and pops n leaky pages after startup (Memory view smoke tests).
		if (!_leaked && int.TryParse(Environment.GetEnvironmentVariable("HV_LEAK"), out var leaks) && leaks > 0)
		{
			_leaked = true;
			Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(2), async () => await PushAndPopLeakyPages(leaks));
		}
	}
}
