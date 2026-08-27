using Immons.Tools.Maui.Inspector.Features.Memory.Holders;
using Immons.Tools.Maui.Inspector.Features.Memory.Metrics;
using Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>
/// Sorts the survivors of a collection round into attached (a window still uses them) and detached
/// (nothing does — the leak suspects). Elements are judged by their parent chain; handlers by their
/// view; platform views by the handlers that own them; view models by the elements bound to them —
/// in that order, because each set feeds the next.
/// </summary>
internal static class InstanceClassifier
{
    public static MemorySnapshot Classify(
        IReadOnlyList<TrackedInstance> live, Dictionary<string, int> collected, IReadOnlyDictionary<string, int> cumulative,
        BaselineComparison? baseline, DateTime now, TimeSpan elapsed, int rounds, IHolderScanner holders)
    {
        var attachedContexts = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var attachedElements = new List<Element>();
        var attachedPlatformViews = new HashSet<object>(ReferenceEqualityComparer.Instance);
        // A detached view model's chain is its element's — the first detached element bound to it.
        var contextOwners = new Dictionary<object, Element>(ReferenceEqualityComparer.Instance);
        var verdicts = new List<(TrackedInstance Record, object Target, bool Attached)>(live.Count);

        foreach (var record in live.Where(r => r.Kind == TrackedKind.Element))
        {
            if (record.Target is not Element element)
                continue;
            var attached = ElementAttachment.IsAttached(element);
            if (attached)
            {
                attachedElements.Add(element);
                if (element.BindingContext is { } context)
                    attachedContexts.Add(context);
                if (element.Handler?.PlatformView is { } platformView)
                    attachedPlatformViews.Add(platformView);
            }
            else if (element.BindingContext is { } context)
            {
                contextOwners.TryAdd(context, element);
            }
            verdicts.Add((record, element, attached));
        }

        foreach (var record in live.Where(r => r.Kind == TrackedKind.Handler))
        {
            if (record.Target is not IElementHandler handler)
                continue;
            var attached = handler.VirtualView is Element view && ElementAttachment.IsAttached(view);
            if (attached && handler.PlatformView is { } platformView)
                attachedPlatformViews.Add(platformView);
            verdicts.Add((record, handler, attached));
        }

        foreach (var record in live.Where(r => r.Kind == TrackedKind.PlatformView))
        {
            if (record.Target is { } target)
                verdicts.Add((record, target, attachedPlatformViews.Contains(target)));
        }

        // State the live screens still reach is in use, not leaked — the filter built on one page and
        // read by the popup that opens next would otherwise be reported every time it is idle.
        var reachable = LiveReachability.From(attachedContexts, attachedElements);
        foreach (var record in live.Where(r => r.Kind == TrackedKind.BindingContext))
        {
            if (record.Target is { } target)
                verdicts.Add((record, target, attachedContexts.Contains(target) || reachable.Reaches(target)));
        }

        return Build(verdicts, contextOwners, collected, cumulative, baseline, now, elapsed, rounds, live.Count, holders);
    }

    static MemorySnapshot Build(
        List<(TrackedInstance Record, object Target, bool Attached)> verdicts, Dictionary<object, Element> contextOwners,
        Dictionary<string, int> collected, IReadOnlyDictionary<string, int> cumulative, BaselineComparison? baseline,
        DateTime now, TimeSpan elapsed, int rounds, int tracked, IHolderScanner scanner)
    {
        var rows = new Dictionary<(string, TrackedKind), TypeRow>();
        var suspects = new List<Suspect>();
        var holders = scanner.Scan(verdicts.Where(v => !v.Attached).Select(v => (v.Record.Id, v.Target)).ToList());

        foreach (var (record, target, attached) in verdicts)
        {
            var type = TypeNames.Full(record.Type);
            var key = (type, record.Kind);
            var row = rows.GetValueOrDefault(key)
                ?? new TypeRow(type, TypeNames.Short(record.Type), record.Kind, TypeNames.IsApp(record.Type), 0, 0, 0, collected.GetValueOrDefault(type))
                {
                    CollectedSinceBaseline = cumulative.GetValueOrDefault(type),
                };

            if (attached)
            {
                record.DetachedAt = null;
                record.DetachedSnapshots = 0;
                rows[key] = row with { Alive = row.Alive + 1, Attached = row.Attached + 1 };
                continue;
            }

            record.DetachedAt ??= now;
            record.DetachedSnapshots++;
            rows[key] = row with { Alive = row.Alive + 1, Detached = row.Detached + 1 };
            suspects.Add(new Suspect(record.Id, type, row.Name, record.Kind, row.App,
                now - record.DetachedAt.Value, record.DetachedSnapshots, record.Owner, SuspectHints.For(record.Kind, target),
                Parents(record.Kind, target, contextOwners), holders.GetValueOrDefault(record.Id) ?? []));
        }

        foreach (var (type, count) in collected.Where(kv => !rows.Keys.Any(k => k.Item1 == kv.Key)))
        {
            rows[(type, TrackedKind.Element)] = new TypeRow(type, type[(type.LastIndexOf('.') + 1)..], TrackedKind.Element,
                AppAssemblies.IsOwn(type.Split('.')[0]), 0, 0, 0, count) { CollectedSinceBaseline = cumulative.GetValueOrDefault(type) };
        }

        // Against a baseline every row also carries how much it grew — that is the leak signal.
        if (baseline != null)
        {
            var byType = rows.Values.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Sum(r => r.Alive));
            foreach (var key in rows.Keys.ToList())
            {
                var row = rows[key];
                rows[key] = row with { BaselineDelta = byType[row.Type] - baseline.AliveByType.GetValueOrDefault(row.Type) };
            }
        }

        var ordered = rows.Values
            .OrderByDescending(r => r.App).ThenByDescending(r => r.Detached).ThenByDescending(r => r.Alive).ThenBy(r => r.Name)
            .ToList();
        var totals = new SnapshotTotals(tracked, ordered.Sum(r => r.Alive), ordered.Sum(r => r.Attached), ordered.Sum(r => r.Detached),
            collected.Values.Sum(), cumulative.Values.Sum());
        var orderedSuspects = suspects.OrderByDescending(s => s.App).ThenByDescending(s => s.Survived).ThenBy(s => s.Name).ToList();
        return new MemorySnapshot(now, elapsed, rounds, totals, ordered, orderedSuspects, MemoryMetrics.Sample()) { Baseline = baseline };
    }

    /// <summary>Where the object sits: an element's ancestors, a handler's view with its ancestors, a view model's element.</summary>
    static IReadOnlyList<string> Parents(TrackedKind kind, object target, Dictionary<object, Element> contextOwners) => kind switch
    {
        TrackedKind.Element when target is Element element => ParentChain.Of(element, includeSelf: false),
        TrackedKind.Handler when target is IElementHandler { VirtualView: Element view } => ParentChain.Of(view, includeSelf: true),
        TrackedKind.BindingContext when contextOwners.TryGetValue(target, out var owner) => ParentChain.Of(owner, includeSelf: true),
        _ => [],
    };
}
