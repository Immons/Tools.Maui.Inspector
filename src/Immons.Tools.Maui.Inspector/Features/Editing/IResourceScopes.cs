namespace Immons.Tools.Maui.Inspector.Features.Editing;

/// <summary>Enumerates every resource dictionary the running app can reach, with its source file.</summary>
internal interface IResourceScopes
{
    /// <summary>
    /// The application dictionary, every merged dictionary (recursively — Source-loaded ones
    /// and their own merges included), then the presented pages' inline dictionaries.
    /// </summary>
    IReadOnlyList<ResourceScope> All();
}
