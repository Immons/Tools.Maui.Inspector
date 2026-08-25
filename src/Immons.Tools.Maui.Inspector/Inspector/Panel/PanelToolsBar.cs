namespace Immons.Tools.Maui.Inspector.Inspector.Panel;

/// <summary>
/// Second toolbar row (behind "⋯"): the toggles that the web panel keeps in its global bar —
/// debug paint, XAML write-back, frame stats and slow animations.
/// </summary>
internal sealed class PanelToolsBar : Grid
{
    readonly Button _guides;
    readonly Button _xaml;
    readonly Button _perf;
    readonly Button _slow;
    readonly Button _cookbook;
    readonly Label _perfOut;

    IDispatcher? _dispatcher;
    bool _guidesOn;

    /// <summary>Debug paint lives on the window inspector, so it is raised as an event.</summary>
    public event Action<bool>? DebugPaintToggled;

    /// <summary>The design cookbook page was requested; the window inspector hides the panel and opens it.</summary>
    public event Action? CookbookRequested;

    public PanelToolsBar()
    {
        _guides = Theme.MakeButton("▦︎ Guides");
        _guides.Clicked += (_, _) =>
        {
            _guidesOn = !_guidesOn;
            DebugPaintToggled?.Invoke(_guidesOn);
            UpdateVisuals();
        };

        _xaml = Theme.MakeButton("✎︎ XAML");
        _xaml.Clicked += (_, _) =>
        {
            InspectorServices.Current.XamlChanges.Enabled = !InspectorServices.Current.XamlChanges.Enabled;
            UpdateVisuals();
        };

        _perf = Theme.MakeButton("⏱︎ Perf");
        _perf.Clicked += (_, _) =>
        {
            FrameStats.SetEnabled(FrameStats.Current == null);
            UpdateVisuals();
            SchedulePerfRefresh();
        };

        _slow = Theme.MakeButton("🐢 Slow");
        _slow.Clicked += (_, _) =>
        {
            SlowAnimations.Set(!SlowAnimations.Enabled);
            UpdateVisuals();
        };

        _cookbook = Theme.MakeButton("📚 Cookbook");
        _cookbook.Clicked += (_, _) => CookbookRequested?.Invoke();

        _perfOut = Theme.MakeLabel("", Theme.TextSecondary, Theme.FontSizeSmall);
        _perfOut.VerticalOptions = LayoutOptions.Center;

        ColumnDefinitions =
        [
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Star),
        ];
        ColumnSpacing = 6;
        Padding = new Thickness(10, 0, 10, 6);
        BackgroundColor = Theme.PanelBg;
        this.NoSafeArea();

        this.Add(_guides, 0);
        this.Add(_xaml, 1);
        this.Add(_perf, 2);
        this.Add(_slow, 3);
        this.Add(_cookbook, 4);
        this.Add(_perfOut, 5);

        foreach (var button in new[] { _guides, _xaml, _perf, _slow, _cookbook })
            button.FontSize = Theme.FontSizeSmall;

        UpdateVisuals();
    }

    /// <summary>Set by the panel so the fps readout can refresh itself while Perf is on.</summary>
    public IDispatcher? Dispatcher
    {
        set => _dispatcher = value;
    }

    void UpdateVisuals()
    {
        Paint(_guides, _guidesOn);
        Paint(_xaml, InspectorServices.Current.XamlChanges.Enabled);
        Paint(_perf, FrameStats.Current != null);
        Paint(_slow, SlowAnimations.Enabled);
        _perfOut.Text = FrameStats.Current is { } f
            ? $"{f.Fps:F0} fps · {f.AverageMs:F1} ms"
            : "";
    }

    static void Paint(Button button, bool on)
    {
        button.BackgroundColor = on ? Theme.MeasureAccent : Theme.PanelBg2;
        button.TextColor = on ? Colors.White : Theme.TextPrimary;
    }

    void SchedulePerfRefresh()
    {
        if (_dispatcher == null || FrameStats.Current == null)
            return;

        _dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
        {
            if (!IsVisible)
                return;
            UpdateVisuals();
            SchedulePerfRefresh();
        });
    }
}
