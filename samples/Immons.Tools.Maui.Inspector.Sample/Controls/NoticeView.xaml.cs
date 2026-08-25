namespace SampleApp.Controls;

/// <summary>A XAML-defined custom control (title + subtitle) — the cookbook renders it from its bindable properties.</summary>
public partial class NoticeView : ContentView
{
	public static readonly BindableProperty TitleProperty = BindableProperty.Create(
		nameof(Title), typeof(string), typeof(NoticeView), "Notice");

	public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
		nameof(Subtitle), typeof(string), typeof(NoticeView), "Something happened");

	public NoticeView()
	{
		InitializeComponent();
	}

	public string Title
	{
		get => (string)GetValue(TitleProperty);
		set => SetValue(TitleProperty, value);
	}

	public string Subtitle
	{
		get => (string)GetValue(SubtitleProperty);
		set => SetValue(SubtitleProperty, value);
	}
}
