using Microsoft.Maui.Controls.Shapes;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

/// <summary>
/// One card: the live sample — with its Disabled twin for control-like items — and the
/// name and detail underneath. Sample construction failures land on the card, not in a crash.
/// </summary>
internal sealed class CookbookTile : Border
{
    public CookbookItem Item { get; }

    /// <summary>The element the web previews capture: the samples, none of the captions.</summary>
    public SampleHost Host { get; } = new() { Padding = new Thickness(6), MinimumHeightRequest = 44 };

    /// <summary>The sample in its normal state (visual states are forced on it); null when nothing rendered.</summary>
    public View? Normal { get; private set; }

    public string? Error { get; private set; }

    /// <summary>The caption (name, detail) was tapped — the page opens the property popup.</summary>
    public event Action? Tapped;

    /// <summary>Captures need the page's backdrop under the sample, whatever the theme makes it.</summary>
    public void AttachBackdrop(VisualElement page)
    {
        Host.SetBinding(BackgroundColorProperty, new Binding(nameof(BackgroundColor), source: page));
        Host.SetBinding(BackgroundProperty, new Binding(nameof(Background), source: page));
    }

    public CookbookTile(CookbookItem item, double width)
    {
        Item = item;
        AutomationId = item.Id; // findable in the tree ("#style-PrimaryButton") and by UI tests
        WidthRequest = width;
        Padding = new Thickness(10);
        Stroke = new SolidColorBrush(CookbookChrome.Stroke);
        StrokeThickness = 1;
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) };

        BuildSample();

        var caption = new ChromeStack { Spacing = 3 };
        caption.Add(CookbookChrome.Label(item.Name, 12, bold: true));
        if (!string.IsNullOrEmpty(item.Detail))
            caption.Add(CookbookChrome.Label(item.Detail, 11, caption: true));
        if (Error != null)
        {
            caption.Add(new ChromeLabel
            {
                Text = "⚠ " + Error,
                FontSize = 11,
                TextColor = CookbookChrome.Warning,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 4,
            });
        }
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Tapped?.Invoke();
        caption.GestureRecognizers.Add(tap);

        var stack = new ChromeStack { Spacing = 3 };
        stack.Add(Host);
        stack.Add(caption);
        Content = stack;
    }

    void BuildSample()
    {
        if (Item.CreateSample == null)
        {
            Host.Add(CookbookChrome.Label("no visual form — setters only", 11, caption: true));
            return;
        }

        try
        {
            Normal = Item.CreateSample();
            if (Normal == null)
            {
                Host.Add(CookbookChrome.Label("nothing to render", 11, caption: true));
                return;
            }
            if (!Item.HasStates)
            {
                Host.Add(Centered(Normal));
                return;
            }

            var disabled = Item.CreateSample();
            if (disabled == null)
            {
                Host.Add(Normal);
                return;
            }
            disabled.IsEnabled = false;

            var states = new ChromeStack { Spacing = 6 };
            states.Add(Captioned(Normal, "normal"));
            states.Add(Captioned(disabled, "disabled"));
            Host.Add(states);
        }
        catch (Exception ex)
        {
            Normal = null;
            Error = $"{ex.GetType().Name}: {ex.Message}";
            Host.Clear();
        }
    }

    /// <summary>Swatches, insets, shadows and images sit in the middle of the tile; controls keep their own alignment.</summary>
    View Centered(View sample)
    {
        if (!CookbookKinds.IsCentered(Item.Kind))
            return sample;
        sample.HorizontalOptions = LayoutOptions.Center;
        sample.VerticalOptions = LayoutOptions.Center;
        if (sample is Label label)
            label.HorizontalTextAlignment = TextAlignment.Center;
        return sample;
    }

    static ChromeStack Captioned(View sample, string state)
    {
        var stack = new ChromeStack { Spacing = 1 };
        stack.Add(sample);
        stack.Add(CookbookChrome.Label(state, 9, caption: true));
        return stack;
    }
}
