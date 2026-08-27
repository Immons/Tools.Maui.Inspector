namespace SampleApp.LeakDemo;

public partial class LeakyPage : ContentPage
{
	public LeakyPage()
	{
		InitializeComponent();
		BindingContext = new LeakyViewModel();
		// The page itself leaks too: an instance handler on a static event.
		LeakSource.Tick += OnTick;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		SubscribersLabel.Text = $"Static event subscribers so far: {LeakSource.Subscribers}";
	}

	void OnTick(object? sender, EventArgs e) => SubscribersLabel.Text = $"Ticked at {DateTime.Now:HH:mm:ss}";

	async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}
