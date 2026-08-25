namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// Gives a bare control instance something to show — text, items, a value — without touching
/// anything a style would set, so the tile shows the style and not the sample's own choices.
/// </summary>
internal static class SampleContent
{
    public const string Text = "The quick brown fox jumps over the lazy dog";
    public const string SpanText = "styled span";

    public static void Configure(View view)
    {
        switch (view)
        {
            case Label label when string.IsNullOrEmpty(label.Text) && label.FormattedText == null:
                label.Text = Text;
                break;
            case Button button when string.IsNullOrEmpty(button.Text) && button.ImageSource == null:
                button.Text = "Button";
                break;
            case Entry entry:
                if (string.IsNullOrEmpty(entry.Text))
                    entry.Text = "Entry text";
                entry.Placeholder ??= "Placeholder";
                break;
            case Editor editor when string.IsNullOrEmpty(editor.Text):
                editor.Text = "Editor text\nSecond line";
                break;
            case SearchBar bar:
                bar.Placeholder ??= "Search…";
                break;
            case Picker picker when picker.Items.Count == 0:
                picker.Items.Add("Option A");
                picker.Items.Add("Option B");
                picker.Items.Add("Option C");
                picker.SelectedIndex = 0;
                picker.Title ??= "Pick one";
                break;
            case Switch toggle:
                toggle.IsToggled = true;
                break;
            case CheckBox box:
                box.IsChecked = true;
                break;
            case RadioButton radio:
                radio.Content ??= "Radio";
                radio.IsChecked = true;
                break;
            case Slider slider:
                slider.Value = slider.Minimum + (slider.Maximum - slider.Minimum) * 0.6;
                break;
            case Stepper stepper:
                stepper.Value = Math.Min(stepper.Minimum + 3 * stepper.Increment, stepper.Maximum);
                break;
            case ProgressBar progress:
                progress.Progress = 0.6;
                break;
            case ActivityIndicator spinner:
                spinner.IsRunning = true;
                break;
            case BoxView box:
                if (box.WidthRequest < 0)
                    box.WidthRequest = 64;
                if (box.HeightRequest < 0)
                    box.HeightRequest = 32;
                break;
            case IndicatorView indicator:
                indicator.Count = 3;
                indicator.Position = 1;
                break;
            case ItemsView items when items.ItemsSource == null:
                items.ItemsSource = new[] { "First item", "Second item", "Third item" };
                if (items.HeightRequest < 0)
                    items.HeightRequest = 132;
                break;
            case ScrollView scroll when scroll.Content == null:
                scroll.Content = ContentLabel();
                break;
            case Border border when border.Content == null:
                border.Content = ContentLabel();
                break;
            case RefreshView refresh when refresh.Content == null:
                refresh.Content = ContentLabel();
                break;
            case SwipeView swipe when swipe.Content == null:
                swipe.Content = ContentLabel();
                break;
            case ContentView content when content.GetType() == typeof(ContentView) && content.Content == null:
                content.Content = ContentLabel();
                break;
            case Layout layout when layout.Count == 0 && IsBuiltIn(layout.GetType()):
                Fill(layout);
                break;
        }
    }

    /// <summary>Plain (not chrome) content: inside a sampled container it should look like app content does.</summary>
    static Label ContentLabel() => new() { Text = "Content" };

    static void Fill(Layout layout)
    {
        if (layout is Grid { ColumnDefinitions.Count: 0, RowDefinitions.Count: 0 } grid)
        {
            for (var column = 0; column < 3; column++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                grid.Add(new Label { Text = $"Cell {column + 1}" }, column);
            }
            return;
        }
        for (var i = 1; i <= 3; i++)
            layout.Add(new Label { Text = $"Child {i}" });
    }

    static bool IsBuiltIn(Type type) =>
        type.Namespace?.StartsWith("Microsoft.Maui.Controls", StringComparison.Ordinal) == true;
}
