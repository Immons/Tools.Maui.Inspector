namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>A fresh instance of a control type with sample content — its implicit look, once in the page.</summary>
internal static class ControlSample
{
    public static bool CanCreate(Type type) =>
        typeof(View).IsAssignableFrom(type)
        && !type.IsAbstract
        && !type.IsGenericTypeDefinition
        && type.GetConstructor(Type.EmptyTypes) != null;

    /// <summary>Null for types that cannot be instantiated; constructor exceptions propagate to the tile.</summary>
    public static View? Create(Type type)
    {
        if (!CanCreate(type))
            return null;
        var view = (View)Activator.CreateInstance(type)!;
        SampleContent.Configure(view);
        return view;
    }
}
