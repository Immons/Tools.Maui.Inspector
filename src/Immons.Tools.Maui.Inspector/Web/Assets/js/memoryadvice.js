// "How to fix it": the remedy for each kind of evidence a leak snapshot attaches to a suspect.
// MAUI leaks come in a handful of shapes, and each hint the classifier emits points at one of them.
const LEAK_ADVICE = [
  { when: (g) => g.kind === 'Element' && g.hints.has('page'),
    text: 'A popped page that stays alive is held by something that outlives it: a static or singleton event it subscribed to '
      + '(unsubscribe in OnNavigatedFrom / OnDisappearing, or publish through WeakEventManager), a messenger subscription '
      + '(WeakReferenceMessenger, or Unregister), a timer or Dispatcher.StartTimer callback, a long-running Task capturing `this`, '
      + 'or a singleton service given the page. The heap dump names the holder — the last app type on the root path.' },
  { when: (g) => [...g.hints].some(h => h.startsWith('handler still connected')),
    text: 'Its handlers were never disconnected. MAUI does that on pop and on window close; a page or view removed by hand '
      + '(Content = null, a custom navigation stack, a hidden tab) needs element.DisconnectHandlers() — a connected handler keeps '
      + 'the platform view, and on iOS/Mac Catalyst the native view keeps the page through its retain cycle.' },
  { when: (g) => [...g.hints].some(h => h.startsWith('inside ')),
    text: (g) => 'A child of the detached ' + [...g.hints].find(h => h.startsWith('inside ')).slice(7) + ' — it goes away once the page does; fix the page, not the child.' },
  { when: (g) => g.kind === 'BindingContext',
    text: 'The view model outlived its page: unsubscribe what it hooked (static events, PropertyChanged of singletons and services, '
      + 'a messenger), cancel timers and CancellationTokens (an OnNavigatedFrom / Dispose hook), and check DI lifetimes — a singleton '
      + 'service holding a transient view model is the usual culprit. If the page itself is listed too, the view model is just held with it.' },
  { when: (g) => g.kind === 'Handler',
    text: 'A handler without a live view: a custom handler must undo in DisconnectHandler everything ConnectHandler subscribed '
      + '(platform events, notifications, timers), and a mapper must not capture the handler in a static.' },
  { when: (g) => g.kind === 'PlatformView',
    text: 'A native view survives its element. iOS/Mac Catalyst: a C# event handler on a native subview (picker.ValueChanged += OnChanged) '
      + 'is a retain cycle — make the handler static or route it through a proxy object without NSObject ancestry. '
      + 'Android: a Java peer kept in a static field or a listener registered on the Activity/Window — clear it when the handler disconnects.' },
];

const LEAK_ADVICE_FOOTER = 'Repeat the navigation and snapshot again — a count that keeps growing is the leak; 🧬 Heap dump traces the suspects to their GC roots.';

function leakAdvice(group) {
  const seen = new Set();
  const lines = [];
  for (const rule of LEAK_ADVICE) {
    if (!rule.when(group)) continue;
    const text = typeof rule.text === 'function' ? rule.text(group) : rule.text;
    if (seen.has(text)) continue;
    seen.add(text);
    lines.push(text);
  }
  return lines;
}
