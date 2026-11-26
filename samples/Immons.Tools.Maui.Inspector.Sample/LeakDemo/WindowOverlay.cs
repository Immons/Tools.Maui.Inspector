using Microsoft.Maui.Platform;

namespace SampleApp.LeakDemo;

/// <summary>
/// An overlay outside the page's visual tree, the way popup libraries portal into the window: the
/// root gets the MAUI window as its Parent (resources, Window) and its platform view goes straight
/// onto the platform window — a subview of the UIWindow on iOS, a child of the decor view on
/// Android. The window's tree events never see it; the Memory view has to find it by itself.
/// The leaky variant subscribes to the static event and, on close, takes the view off the platform
/// window but leaves Parent pointing at the window — so the tracker must not call it attached.
/// The reported variant has no Parent and sits in a native container of the page's own view, where
/// nothing can find it — the app reports it with Immons.Tools.Maui.Inspector.Memory.TrackVisualRoot.
/// </summary>
public static class WindowOverlay
{
	static WindowOverlayRoot? _shown;

	public static bool Show(Window window, bool leaky, bool reported = false)
	{
		if (_shown != null || window.Handler?.MauiContext is not { } context)
			return false;

		var root = new WindowOverlayRoot(leaky, reported);
		root.CloseRequested += (_, _) => Hide();
		if (!reported)
			root.Parent = window;
		var view = root.ToPlatform(context);
#if IOS
		if (window.Handler.PlatformView is not UIKit.UIWindow platformWindow)
			return false;
		var host = reported ? platformWindow.RootViewController?.View ?? platformWindow : platformWindow;
		view.Frame = host.Bounds;
		view.AutoresizingMask = UIKit.UIViewAutoresizing.FlexibleWidth | UIKit.UIViewAutoresizing.FlexibleHeight;
		host.AddSubview(view);
#elif ANDROID
		if (window.Handler.PlatformView is not Android.App.Activity { Window.DecorView: Android.Views.ViewGroup decor } activity)
			return false;
		var host = reported ? activity.FindViewById<Android.Views.ViewGroup>(Android.Resource.Id.Content) ?? decor : decor;
		host.AddView(view, new Android.Views.ViewGroup.LayoutParams(
			Android.Views.ViewGroup.LayoutParams.MatchParent, Android.Views.ViewGroup.LayoutParams.MatchParent));
#else
		return false;
#endif
		if (reported)
			Immons.Tools.Maui.Inspector.Memory.TrackVisualRoot(root);
		_shown = root;
		return true;
	}

	public static void Hide()
	{
		if (_shown is not { } root)
			return;
		_shown = null;
#if IOS
		(root.Handler?.PlatformView as UIKit.UIView)?.RemoveFromSuperview();
#elif ANDROID
		if (root.Handler?.PlatformView is Android.Views.View { Parent: Android.Views.ViewGroup parent } view)
			parent.RemoveView(view);
#endif
		if (root.Reported)
			Immons.Tools.Maui.Inspector.Memory.UntrackVisualRoot(root);
		if (root.Leaky)
			return;
		root.DisconnectHandlers();
		root.Parent = null;
	}
}

/// <summary>The overlay's root — a Grid with a backdrop and a card of a few controls.</summary>
public sealed class WindowOverlayRoot : Grid
{
	public WindowOverlayRoot(bool leaky, bool reported)
	{
		Leaky = leaky;
		Reported = reported;
		var close = new Button { Text = "Close overlay", AutomationId = "WindowOverlayCloseBtn" };
		close.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
		Children.Add(new BoxView { Color = Color.FromArgb("#80000000") });
		Children.Add(new WindowOverlayCard(leaky)
		{
			Content = new VerticalStackLayout
			{
				Spacing = 8,
				Children =
				{
					new Label { Text = reported ? "Reported overlay" : leaky ? "Leaky window overlay" : "Window overlay", FontSize = 18 },
					new Label { Text = "Hosted on the platform window, not in the page.", FontSize = 13 },
					new Entry { Placeholder = "Filter" },
					new Switch(),
					close,
				},
			},
		});
	}

	public bool Leaky { get; }

	public bool Reported { get; }

	public event EventHandler? CloseRequested;
}

public sealed class WindowOverlayCard : Border
{
	public WindowOverlayCard(bool leaky)
	{
		Padding = new Thickness(16);
		Margin = new Thickness(24);
		VerticalOptions = LayoutOptions.Center;
		Background = Colors.White;
		Stroke = Color.FromArgb("#512BD4");
		if (leaky)
			LeakSource.Tick += OnTick;
	}

	void OnTick(object? sender, EventArgs e) => Opacity = Opacity > 0.5 ? 0.9 : 1;
}
