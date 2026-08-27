namespace Immons.Tools.Maui.Inspector.Inspector.Panel;

/// <summary>
/// The Memory view for the phone-in-hand case: a snapshot button, watch mode, the suspects
/// grouped by type with their holders, and the navigation ledger's verdicts.
/// </summary>
internal sealed class MemoryPane : Grid
{
    const int MaxGroups = 25;
    const int MaxLedgerRows = 15;

    readonly Button _snapshot = Theme.MakeButton("📸 Snapshot");
    readonly Button _watch = Theme.MakeButton("👁 Watch");
    readonly Label _summary = Theme.MakeLabel("", Theme.TextSecondary, Theme.FontSizeSmall);
    readonly VerticalStackLayout _list = new() { Spacing = 4, Padding = new Thickness(10, 4) };

    public MemoryPane()
    {
        _snapshot.Clicked += async (_, _) => await Refresh();
        _watch.Clicked += (_, _) =>
        {
            var options = MauiInspector.Options.Memory;
            options.WatchNavigation = !options.WatchNavigation;
            Paint();
        };

        var bar = new HorizontalStackLayout { Spacing = 6, Padding = new Thickness(10, 4) };
        bar.Add(_snapshot);
        bar.Add(_watch);
        bar.Add(_summary);
        _summary.VerticalOptions = LayoutOptions.Center;

        RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)];
        this.Add(bar, 0, 0);
        this.Add(new ScrollView { Content = _list }, 0, 1);
        BackgroundColor = Theme.PanelBg;
        Paint();
    }

    /// <summary>Runs a snapshot and shows what it found.</summary>
    public async Task Refresh()
    {
        _snapshot.IsEnabled = false;
        _summary.Text = "collecting…";
        try
        {
            await InspectorServices.Current.Snapshots.RunAsync();
        }
        catch (Exception ex)
        {
            _summary.Text = ex.Message;
        }
        finally
        {
            _snapshot.IsEnabled = true;
        }
        Render();
    }

    /// <summary>Shows the latest snapshot and ledger without running anything.</summary>
    public void Render()
    {
        _list.Clear();
        Paint();
        var snapshot = InspectorServices.Current.Snapshots.Latest;
        var sample = MemoryMetrics.Sample();
        _summary.Text = snapshot == null
            ? $"managed {sample.ManagedBytes / 1024 / 1024} MB"
            : $"{snapshot.Totals.Detached} detached of {snapshot.Totals.Alive} · managed {sample.ManagedBytes / 1024 / 1024} MB";

        if (snapshot != null)
        {
            _list.Add(Theme.MakeLabel("Leak suspects", Theme.TextPrimary, Theme.FontSize, bold: true));
            var groups = snapshot.Suspects.Where(s => s.App).GroupBy(s => (s.Name, s.Kind)).OrderByDescending(g => g.Count()).Take(MaxGroups).ToList();
            if (groups.Count == 0)
                _list.Add(Theme.MakeLabel("no detached app objects", Theme.TextSecondary, Theme.FontSizeSmall));
            foreach (var group in groups)
            {
                var hints = group.SelectMany(s => s.Holders.Select(h => "⛓ " + h).Concat(s.Hints)).Distinct().Take(3);
                _list.Add(Theme.MakeLabel($"{group.Key.Name} ×{group.Count()}  {group.Key.Kind}", Theme.TextPrimary, Theme.FontSizeSmall, bold: true));
                foreach (var hint in hints)
                    _list.Add(Theme.MakeLabel("   " + hint, Theme.MeasureAccent, Theme.FontSizeSmall));
            }
        }

        var entries = InspectorServices.Current.Ledger.Entries.Take(MaxLedgerRows).ToList();
        if (entries.Count == 0)
            return;
        _list.Add(Theme.MakeLabel("Navigation ledger", Theme.TextPrimary, Theme.FontSize, bold: true));
        foreach (var entry in entries)
        {
            var verdict = entry.Verdict switch
            {
                PageVerdict.Alive => $"✗ still alive (×{entry.Survived})",
                PageVerdict.Collected => "✓ collected",
                PageVerdict.Pending => "… pending",
                PageVerdict.Reattached => "↩ reattached",
                _ => "open",
            };
            var color = entry.Verdict == PageVerdict.Alive ? Theme.Outline : Theme.TextSecondary;
            _list.Add(Theme.MakeLabel($"{entry.Label}  {entry.PushedAt:HH:mm:ss}  {verdict}", color, Theme.FontSizeSmall));
        }
    }

    void Paint()
    {
        var on = MauiInspector.Options.Memory.WatchNavigation;
        _watch.BackgroundColor = on ? Theme.MeasureAccent : Theme.PanelBg2;
        _watch.TextColor = on ? Colors.White : Theme.TextPrimary;
        foreach (var button in new[] { _snapshot, _watch })
            button.FontSize = Theme.FontSizeSmall;
    }
}
