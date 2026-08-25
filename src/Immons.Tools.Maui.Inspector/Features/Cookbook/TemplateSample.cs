namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Live content of ControlTemplate / DataTemplate resources (recipes included).</summary>
internal static class TemplateSample
{
    public static View FromControlTemplate(ControlTemplate template) => new ContentView
    {
        ControlTemplate = template,
        Content = new Label { Text = "Content" },
    };

    /// <summary>Instantiates the template without a data context — bindings resolve to nothing, the layout still shows.</summary>
    public static View? FromDataTemplate(DataTemplate template) => template.CreateContent() switch
    {
        View view => view,
        ViewCell cell => cell.View,
        _ => null,
    };
}
