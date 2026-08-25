namespace Immons.Tools.Maui.Inspector;

/// <summary>Tuning of the design cookbook (the gallery of the app's styles, controls and resources).</summary>
public sealed class CookbookOptions
{
    /// <summary>
    /// When not empty, only these controls appear in the cookbook's Controls section: prefixes of
    /// full type names ("MyApp.Controls.New.") or of the control's XAML file path ("Views/New/").
    /// Keep the current design system in one namespace or folder and list it here — the legacy
    /// controls stay out without naming each one.
    /// </summary>
    public IList<string> IncludedControls { get; } = new List<string>();

    /// <summary>
    /// Controls left out of the Controls section — the same prefixes as <see cref="IncludedControls"/>
    /// (namespace, full type name or XAML folder), vetoing even an included one. A control whose
    /// constructor starts hardware, timers or network has no business being instantiated by a gallery.
    /// </summary>
    public IList<string> ExcludedControls { get; } = new List<string>();

    /// <summary>
    /// When not empty, only matching resources appear in the other sections — colors, typography,
    /// styles, templates, images, scalars, recipes. Prefixes of a resource key ("Brand."), of its
    /// dictionary file ("Resources/Styles/DesignSystem/"), of an image or font file name, or of a
    /// style's target type. "styles:Resources/Styles/Legacy/" scopes an entry to one section id.
    /// </summary>
    public IList<string> IncludedResources { get; } = new List<string>();

    /// <summary>Resources left out — the same prefixes as <see cref="IncludedResources"/>, vetoing even an included one.</summary>
    public IList<string> ExcludedResources { get; } = new List<string>();

    /// <summary>
    /// Creates the data context every sample gets — what the app's screens would give a control:
    /// localized strings, theme colors, the services its bindings reach for. Called once per
    /// cookbook page. Null (default) leaves bindings unresolved, which is what a bare control shows.
    /// </summary>
    public Func<object?>? BindingContext { get; set; }

    /// <summary>
    /// Backdrop painted behind the samples in the light theme — the page background the app's
    /// screens use. Default: the app's implicit ContentPage style background, else white.
    /// </summary>
    public Color? LightBackground { get; set; }

    /// <summary>The dark-theme counterpart of <see cref="LightBackground"/>. Default: the implicit page style, else #121212.</summary>
    public Color? DarkBackground { get; set; }

    /// <summary>A brush backdrop (a gradient, say) used in both themes; outranks the colors.</summary>
    public Brush? Background { get; set; }
}
