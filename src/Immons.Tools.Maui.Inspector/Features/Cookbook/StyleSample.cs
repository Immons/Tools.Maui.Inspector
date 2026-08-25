namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>An instance of the style's TargetType wearing the style; Span styles ride inside a Label.</summary>
internal static class StyleSample
{
    public static bool CanCreate(Style style) =>
        style.TargetType == typeof(Span) || ControlSample.CanCreate(style.TargetType);

    public static View? Create(Style style)
    {
        if (style.TargetType == typeof(Span))
        {
            var formatted = new FormattedString();
            formatted.Spans.Add(new Span { Text = "Plain, then a " });
            formatted.Spans.Add(new Span { Text = SampleContent.SpanText, Style = style });
            return new Label { FormattedText = formatted };
        }

        var view = ControlSample.Create(style.TargetType);
        if (view != null)
            view.Style = style;
        return view;
    }
}
