namespace Immons.Tools.Maui.Inspector.Features.Memory.Watch;

internal sealed class NavigationLedger : INavigationLedger
{
    const int Limit = 100;

    readonly object _gate = new();
    readonly List<NavigationEntry> _entries = [];
    int _next;
    int _cycles;

    public IReadOnlyList<NavigationEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public void Pushed(object screen, string? name = null, bool reported = false)
    {
        lock (_gate)
        {
            if (_entries.FirstOrDefault(e => e.Verdict != PageVerdict.Open && e.Is(screen)) is { } cached)
            {
                cached.Verdict = PageVerdict.Reattached;
                return;
            }
            if (_entries.Any(e => e.Verdict == PageVerdict.Open && e.Is(screen)))
                return;

            var sample = MemoryMetrics.Sample();
            _entries.Insert(0, new NavigationEntry(++_next, screen, TypeNames.Full(screen.GetType()), Label(screen, name),
                DateTime.Now, sample.ManagedBytes, sample.Platform.ProcessBytes) { Reported = reported });
            if (_entries.Count > Limit)
                _entries.RemoveRange(Limit, _entries.Count - Limit);
        }
    }

    /// <summary>
    /// The name the app gave it, else the element's own label — with what it shows appended when the
    /// two say different things. A popup host is called PopupPage no matter which popup is inside,
    /// so a ledger of them is unreadable; "PopupPage · VisitPreview" is not.
    /// </summary>
    static string Label(object screen, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name!;
        if (screen is not Element element)
            return TypeNames.Short(screen.GetType());
        var label = ParentChain.Label(element);
        return Qualifier(element) is { } qualifier ? $"{label} · {qualifier}" : label;
    }

    /// <summary>
    /// What the screen shows, when its own name does not say it: the content it was handed, or —
    /// for a host that is not even the app's own type, like a popup library's page — its view
    /// model. A page named after its own screen gets nothing appended; it needs nothing.
    /// </summary>
    static string? Qualifier(Element element)
    {
        var own = Stem(TypeNames.Short(element.GetType()));
        if (element is ContentPage { Content: { } content } && Describes(content.GetType(), own) is { } fromContent)
            return fromContent;
        if (TypeNames.IsApp(element.GetType()))
            return null;
        if (element is Page { Title: { Length: > 0 } title } && !string.Equals(Stem(title), own, StringComparison.OrdinalIgnoreCase))
            return title;
        return element.BindingContext is { } context && context is not Element ? Describes(context.GetType(), own) : null;
    }

    /// <summary>The type's name when it is the app's own and says something the host's name does not.</summary>
    static string? Describes(Type type, string ownStem) =>
        TypeNames.IsApp(type) && !string.Equals(Stem(TypeNames.Short(type)), ownStem, StringComparison.OrdinalIgnoreCase)
            ? TypeNames.Short(type)
            : null;

    /// <summary>"InVisitPage", "InVisitViewModel" and "InVisitView" all stand for the same screen.</summary>
    static string Stem(string name)
    {
        foreach (var suffix in (string[])["ViewModel", "PageModel", "Popup", "Page", "View", "Model"])
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return Stem(name[..^suffix.Length]);
        }
        return name;
    }

    public void Popped(object screen)
    {
        lock (_gate)
        {
            if (_entries.FirstOrDefault(e => e.Verdict is PageVerdict.Open or PageVerdict.Reattached && e.Is(screen)) is not { } entry)
                return;
            var sample = MemoryMetrics.Sample();
            entry.PoppedAt = DateTime.Now;
            entry.ManagedAtPop = sample.ManagedBytes;
            entry.ProcessAtPop = sample.Platform.ProcessBytes;
            entry.Verdict = PageVerdict.Pending;
            entry.Survived = 0;
            _cycles++;
        }
    }

    public void Judge()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Where(e => e.Verdict is PageVerdict.Pending or PageVerdict.Alive))
            {
                if (entry.IsAlive)
                {
                    entry.Verdict = PageVerdict.Alive;
                    entry.Survived++;
                }
                else
                {
                    entry.Verdict = PageVerdict.Collected;
                }
            }
        }
    }

    public (int Open, int Pending, int Alive, int Cycles) Counts
    {
        get
        {
            lock (_gate)
            {
                return (_entries.Count(e => e.Verdict == PageVerdict.Open), _entries.Count(e => e.Verdict == PageVerdict.Pending),
                    _entries.Count(e => e.Verdict == PageVerdict.Alive), _cycles);
            }
        }
    }
}
