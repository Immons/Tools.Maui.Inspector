using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

internal static partial class ImageAssetCatalog
{
    /// <summary>AndroidX / Material drawables share the app's resource table — skipped by prefix.</summary>
    static readonly string[] LibraryPrefixes =
    [
        "abc_", "mtrl_", "material_", "m3_", "ic_m3_", "ic_mtrl_", "notification_", "design_",
        "navigation_", "avd_", "btn_", "common_google", "googleg_", "ic_clock_", "ic_keyboard_",
        "ic_calendar_", "ic_arrow_", "ic_call_", "tooltip_", "test_", "indeterminate_", "notify_",
        "ic_launcher", "mtrl", "maui_", "splash",
    ];

    // MauiImage outputs become drawable resources; the generated Resource.Drawable class lists
    // them as int constants. FileImageSource resolves "name.png" by that resource name.
    private static partial IEnumerable<string> BundledImagesPlatform()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name ?? "";
            if (assemblyName != "_Microsoft.Android.Resource.Designer" && AppAssemblies.IsFramework(assemblyName))
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.Name != "Drawable" || type.DeclaringType is not { Name: "Resource" or "ResourceConstant" })
                    continue;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.FieldType != typeof(int) || IsLibraryDrawable(field.Name))
                        continue;
                    yield return field.Name + ".png";
                }
            }
        }
    }

    static bool IsLibraryDrawable(string name) =>
        LibraryPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}
