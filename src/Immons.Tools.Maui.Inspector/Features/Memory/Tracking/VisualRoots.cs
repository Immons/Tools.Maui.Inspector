using System.Runtime.CompilerServices;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>
/// The tables map a root to nothing but its presence, so they never keep it alive; the lists are
/// weak too and only exist to enumerate.
/// </summary>
internal sealed class VisualRoots : IVisualRoots
{
    static readonly object Present = new();

    readonly object _gate = new();
    readonly ConditionalWeakTable<Element, object> _explicit = new();
    readonly List<WeakReference<Element>> _explicitList = [];
    readonly ConditionalWeakTable<Element, object> _discovered = new();
    readonly List<WeakReference<Element>> _shown = [];
    volatile int _explicitCount;
    int _version;

    public bool HasExplicit => _explicitCount > 0;

    public int Version => Volatile.Read(ref _version);

    public event Action? Changed;

    public bool AddExplicit(Element root)
    {
        lock (_gate)
        {
            if (_explicit.TryGetValue(root, out _))
                return false;
            _explicit.Add(root, Present);
            _explicitList.RemoveAll(w => !w.TryGetTarget(out _));
            _explicitList.Add(new WeakReference<Element>(root));
            _explicitCount = _explicitList.Count;
        }
        OnChanged();
        return true;
    }

    public bool RemoveExplicit(Element root)
    {
        lock (_gate)
        {
            if (!_explicit.Remove(root))
                return false;
            _explicitList.RemoveAll(w => !w.TryGetTarget(out var target) || ReferenceEquals(target, root));
            _explicitCount = _explicitList.Count;
        }
        OnChanged();
        return true;
    }

    public bool IsExplicit(Element element)
    {
        if (_explicitCount == 0)
            return false;
        lock (_gate)
        {
            return _explicit.TryGetValue(element, out _);
        }
    }

    public IReadOnlyList<Element> Explicit()
    {
        lock (_gate)
        {
            return Alive(_explicitList);
        }
    }

    public bool IsDiscovered(Element element)
    {
        lock (_gate)
        {
            return _discovered.TryGetValue(element, out _);
        }
    }

    public (List<Element> Appeared, List<Element> Vanished) Scanned(IReadOnlyList<Element> found)
    {
        List<Element> appeared, vanished;
        lock (_gate)
        {
            var before = Alive(_shown);
            appeared = found.Where(f => !before.Contains(f, ReferenceEqualityComparer.Instance)).ToList();
            vanished = before.Where(b => !found.Contains(b, ReferenceEqualityComparer.Instance)).ToList();
            foreach (var root in appeared)
                _discovered.AddOrUpdate(root, Present);
            _shown.Clear();
            _shown.AddRange(found.Select(f => new WeakReference<Element>(f)));
        }
        if (appeared.Count > 0 || vanished.Count > 0)
            OnChanged();
        return (appeared, vanished);
    }

    public IReadOnlyList<Element> Shown()
    {
        lock (_gate)
        {
            return Alive(_shown);
        }
    }

    void OnChanged()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }

    static List<Element> Alive(List<WeakReference<Element>> list) =>
        list.Select(w => w.TryGetTarget(out var target) ? target : null).OfType<Element>().ToList();
}
