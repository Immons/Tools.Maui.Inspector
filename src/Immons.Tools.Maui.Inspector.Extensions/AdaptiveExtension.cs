using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Microsoft.Maui.Controls.Xaml;

namespace Immons.Tools.Maui.Inspector.Extensions;

/// <summary>
/// A value chosen by idiom AND platform in one inline expression, for the cases
/// <c>OnIdiom</c> alone cannot express — different values on tablets, iOS phones and
/// Android phones without a dozen lines of nested element syntax:
/// <code>
/// xmlns:ins="clr-namespace:Immons.Tools.Maui.Inspector.Extensions;assembly=Immons.Tools.Maui.Inspector.Extensions"
/// Margin="{ins:Adaptive Default='0,0,32,20', Phone='0', PhoneIOS='0,0,0,-10'}"
/// </code>
/// Works for any property type (Thickness, double, Color, string, …): the literal is
/// converted to the target property's type the same way <c>OnPlatform</c> does it.
/// Resolution order: the idiom+platform value, then the idiom value, then
/// <see cref="Default"/>. The MAUI Inspector understands the expression — it shows it as
/// the value's origin and its ⋔ editor edits the entries per idiom/platform, live.
/// </summary>
[ContentProperty(nameof(Default))]
[AcceptEmptyServiceProvider]
public class AdaptiveExtension : IMarkupExtension
{
    public object? Default { get; set; }

    public object? Phone { get; set; }

    public object? Tablet { get; set; }

    public object? Desktop { get; set; }

    public object? PhoneIOS { get; set; }

    public object? PhoneAndroid { get; set; }

    public object? TabletIOS { get; set; }

    public object? TabletAndroid { get; set; }

    public object? ProvideValue(IServiceProvider serviceProvider)
    {
        var idiom = DeviceInfo.Idiom;
        var platform = DeviceInfo.Platform;

        object? forIdiom;
        object? forPlatform;

        if (idiom == DeviceIdiom.Phone)
        {
            forIdiom = Phone;
            forPlatform = platform == DevicePlatform.iOS ? PhoneIOS
                : platform == DevicePlatform.Android ? PhoneAndroid
                : null;
        }
        else if (idiom == DeviceIdiom.Tablet)
        {
            forIdiom = Tablet;
            forPlatform = platform == DevicePlatform.iOS ? TabletIOS
                : platform == DevicePlatform.Android ? TabletAndroid
                : null;
        }
        else
        {
            forIdiom = Desktop;
            forPlatform = null;
        }

        return Convert(forPlatform ?? forIdiom ?? Default, serviceProvider);
    }

    // The literals arrive as strings. MAUI's own converter provider is internal, so convert
    // through the TypeConverter the target type declares (Thickness, Color, LayoutOptions…
    // all carry one) and fall back to plain numeric/enum conversion.
    static object? Convert(object? value, IServiceProvider? serviceProvider)
    {
        if (value is not string text
            || serviceProvider?.GetService(typeof(IProvideValueTarget)) is not IProvideValueTarget target)
        {
            return value;
        }

        var propertyType = target.TargetProperty switch
        {
            BindableProperty bindable => bindable.ReturnType,
            PropertyInfo property => property.PropertyType,
            _ => null,
        };

        if (propertyType == null || propertyType == typeof(string) || propertyType == typeof(object))
        {
            return value;
        }

        var converter = TypeDescriptor.GetConverter(propertyType);
        if (converter?.CanConvertFrom(typeof(string)) == true)
        {
            return converter.ConvertFromInvariantString(text);
        }

        if (propertyType.IsEnum)
        {
            return Enum.Parse(propertyType, text, ignoreCase: true);
        }

        return System.Convert.ChangeType(text, propertyType, CultureInfo.InvariantCulture);
    }
}
