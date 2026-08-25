namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

// The cookbook's own UI is built from these subclasses: the app's implicit styles match exact
// types only, so the chrome around the samples is never restyled by them (every look-affecting
// property is also set explicitly, for styles that opt into ApplyToDerivedTypes).

internal sealed class ChromeLabel : Label;

internal sealed class ChromeBorder : Border;

internal sealed class ChromeStack : VerticalStackLayout;

internal sealed class ChromeRow : HorizontalStackLayout;

internal sealed class ChromeGrid : Grid;

internal sealed class ChromeFlex : FlexLayout;

internal sealed class ChromeScroll : ScrollView;

internal sealed class ChromeButton : Button;

internal sealed class ChromeImage : Image;

internal sealed class ChromeCollection : CollectionView;

/// <summary>The element a tile's web preview captures: the sample(s), none of the captions.</summary>
internal sealed class SampleHost : Grid;
