using Microsoft.Maui.Controls.Shapes;

namespace SampleApp.Controls;

/// <summary>A small custom control — the cookbook lists it in Controls and the recipes use it.</summary>
public class PillBadge : ContentView
{
	public static readonly BindableProperty TextProperty = BindableProperty.Create(
		nameof(Text), typeof(string), typeof(PillBadge), "Badge", propertyChanged: (b, _, _) => ((PillBadge)b).Render());

	public static readonly BindableProperty TintProperty = BindableProperty.Create(
		nameof(Tint), typeof(Color), typeof(PillBadge), Color.FromArgb("#512BD4"), propertyChanged: (b, _, _) => ((PillBadge)b).Render());

	readonly Border _pill;
	readonly Label _label;

	public PillBadge()
	{
		_label = new Label { TextColor = Colors.White, FontSize = 12, FontAttributes = FontAttributes.Bold };
		_pill = new Border
		{
			Padding = new Thickness(10, 3),
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
			HorizontalOptions = LayoutOptions.Start,
			Content = _label,
		};
		Content = _pill;
		Render();
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public Color Tint
	{
		get => (Color)GetValue(TintProperty);
		set => SetValue(TintProperty, value);
	}

	void Render()
	{
		_label.Text = Text;
		_pill.Background = new SolidColorBrush(Tint);
	}
}
