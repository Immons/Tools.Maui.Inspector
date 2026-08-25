using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>A focused sample rendered headlessly: the control alone, at the window's width, on the stage.</summary>
internal sealed class StagedSample : IFocusedSample
{
    public CookbookItem Item { get; }

    public SampleHost Host { get; } = new()
    {
        Padding = new Thickness(12),
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Start,
    };

    public View? Sample { get; }

    public string? Error { get; }

    public StagedSample(CookbookItem item, double width, object? sampleContext)
    {
        Item = item;
        Host.WidthRequest = width;
        Host.BindingContext = sampleContext;
        try
        {
            Sample = item.CreateSample?.Invoke();
        }
        catch (Exception ex)
        {
            Error = $"{ex.GetType().Name}: {ex.Message}";
        }
        if (Sample == null)
            return;
        if (CookbookKinds.IsCentered(item.Kind))
        {
            Sample.HorizontalOptions = LayoutOptions.Center;
            Sample.VerticalOptions = LayoutOptions.Center;
        }
        Host.Add(Sample);
    }
}
