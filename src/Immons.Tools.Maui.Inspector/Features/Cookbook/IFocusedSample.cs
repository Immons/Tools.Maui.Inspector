using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>The one sample singled out — on its own page on the device, or headlessly on the stage.</summary>
internal interface IFocusedSample
{
    CookbookItem Item { get; }

    /// <summary>The element captures render: the sample alone on the backdrop.</summary>
    SampleHost Host { get; }

    View? Sample { get; }

    string? Error { get; }
}
