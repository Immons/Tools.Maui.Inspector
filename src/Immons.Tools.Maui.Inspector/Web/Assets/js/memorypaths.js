// Reference chains from a heap dump. The report gives each path as type names from the leaked
// object up to its GC root — "A ← B" reads "A is held by B". The list shows a one-line preview;
// clicking it opens the chain as a vertical stack: app types marked, delegate hops explained as the
// event subscription they are, the root named. The step to fix is usually the first app type above
// the object — the one that still points at it.
const DELEGATE_STEP = /^System\.(EventHandler|Action|Func)(<|$)|EventHandler(<|$)|^System\.Delegate\[\]$/;

function pathEscape(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function isRootStep(t) { return t.startsWith('['); }
function isDelegateStep(t) { return DELEGATE_STEP.test(t); }

function reportAppTypes(report) {
  return new Set((report.types || []).filter(t => t.app).map(t => t.type));
}

// Third-party packages are not the framework, but they are not your code either — a chain that
// passes through CommunityToolkit is not "the place to look".
function reportPackageTypes(report) {
  return new Set((report.types || []).filter(t => t.package).map(t => t.type));
}

let pathPackageTypes = new Set();

// First three hops, a gap, the root — enough to recognise the chain without a horizontal scroll.
function pathPreviewSteps(path) {
  if (path.length <= 5) return path;
  return [...path.slice(0, 3), `… ${path.length - 4} more …`, path[path.length - 1]];
}

function stepClass(t, index, appTypes) {
  if (index === 0) return 'pstep held';
  if (isRootStep(t)) return 'pstep root';
  if (appTypes.has(t)) return 'pstep app';
  if (pathPackageTypes.has(t)) return 'pstep package';
  if (isDelegateStep(t)) return 'pstep delegate';
  return 'pstep';
}

function renderPathPreview(jobId, rootIndex, pathIndex, path, appTypes, kind) {
  const steps = pathPreviewSteps(path)
    .map((t, i) => `<span class="${t.startsWith('… ') ? 'pstep gap' : stepClass(t, i, appTypes)}">${pathEscape(t)}</span>`)
    .join('<span class="parrow">←</span>');
  const badge = kind ? `<span class="rootkind ${kind}">${kind}</span>` : '';
  return `<div class="rootpath" onclick="openPathPopup(${jobId}, ${rootIndex}, ${pathIndex})" title="Click for the whole chain as a stack">${badge}${steps}</div>`;
}

// What a step means for the reader — the root kind, the delegate hop, the app type to look at.
function stepNote(path, i, appTypes, state) {
  const t = path[i];
  if (i === 0) return 'the leaked object';
  if (isRootStep(t)) return rootMeaning(t);
  if (isDelegateStep(t)) {
    if (state.inDelegateRun) return '';
    state.inDelegateRun = true;
    // Down to the object that is actually held, past the closures the compiler put in between.
    let below = i - 1; while (below > 0 && (isDelegateStep(path[below]) || isClosure(path[below]))) below--;
    let above = i + 1;
    // Step over the closures too: the object that can actually unsubscribe is the first real type.
    while (above < path.length - 1 && (isDelegateStep(path[above]) || isClosure(path[above]))) above++;
    const owner = path[above] || 'the root', target = path[below];
    return isClosure(path[i + 1] || '')
      ? `a handler bound to a lambda: ${shortType(owner)} raises it, and the lambda's closure keeps ${shortType(target)} — the subscription has to be undone where it was made`
      : `an event subscription: ${shortType(owner)} keeps ${shortType(target)} through a handler — unsubscribe in ${shortType(target)}, or make the handler static / weak`;
  }
  state.inDelegateRun = false;
  if (closureNote(t)) return closureNote(t);
  if (pathPackageTypes.has(t) && !state.firstPackageSeen) {
    state.firstPackageSeen = true;
    return 'a third-party package, not your code — look at how your code hands objects to it, or at the package itself';
  }
  const singleton = isDiSingleton(t) ? ' · a DI singleton — it outlives every page it references' : '';
  if (appTypes.has(t) && !state.firstAppSeen) {
    state.firstAppSeen = true;
    return 'the first app type holding the chain — the place to look' + singleton;
  }
  return singleton ? 'a DI singleton — it outlives every page it references' : '';
}

// The root's kind decides where to look — and whether managed code can fix it at all.
function rootMeaning(t) {
  const root = t.toLowerCase();
  if (root.includes('strong handle')) return 'a strong GCHandle — an interop anchor (ObjC / Java peer). Managed code cannot release it: the native object has to go away (a UIViewController not dismissed, an Activity not finished, a native callback still registered)';
  if (root.includes('pinned handle')) return 'a pinned GCHandle — a buffer handed to native code and not freed';
  if (root.includes('finalizer')) return 'the finalizer queue — it is on its way out; snapshot again in a moment';
  if (root.includes('weak handle')) return 'a weak handle — not a leak by itself';
  if (root.includes('handle')) return 'a GC handle — alive until whoever created it frees it';
  if (t.startsWith('[static vars')) return 'the static fields — the root category';
  if (t.startsWith('[static var')) return 'a static field — your code, alive for the whole process';
  if (t.startsWith('[local vars')) return 'a local variable of a running method — transient, snapshot again';
  if (t.startsWith('[unreachable')) return 'not reachable from any root — garbage the GC had not collected yet';
  if (t.includes('COM') || t.includes('CCW')) return 'a COM/CCW wrapper — held from the native side';
  return 'GC root';
}

// The label the chain preview ends with, in one word, for the suspect line.
function rootKind(path) {
  const last = path[path.length - 1] || '';
  if (!isRootStep(last)) return '';
  const meaning = rootMeaning(last);
  return meaning.startsWith('a strong GCHandle') ? 'interop' : last.startsWith('[static') ? 'static' : last.includes('handle') ? 'handle' : 'root';
}

function isDiSingleton(typeName) {
  const singletons = memStats && memStats.singletons;
  if (!singletons || !singletons.length) return false;
  const bare = typeName.replace(/<.*$/, '');
  return singletons.includes(bare);
}

function openLargestPopup(jobId, index) {
  const job = memDumps.find(j => j.id === jobId);
  const o = job?.report?.largest?.[index];
  if (!o) return;
  const appTypes = reportAppTypes(job.report);
  pathPackageTypes = reportPackageTypes(job.report);
  document.getElementById('pathtitle').textContent = o.type;
  document.getElementById('pathhint').textContent = `${fmtBytes(o.bytes)} · retained ${fmtBytes(o.retained)} · dump #${job.id} · read top-down: each row is held by the one below`;
  document.getElementById('pathbody').innerHTML = renderPathStack(jobId, -1, index, o.path, appTypes, true).replace(/onclick="copyPath\([^)]*\)"/, 'hidden');
  document.getElementById('pathback').hidden = false;
}

// Compiler-generated names, decoded. A gcdump shows them as the runtime spells them, with <> as
// [] — "Foo.[]c__DisplayClass60_0" is the closure of a lambda written inside Foo's 60th method,
// holding every variable that lambda captured. Nobody wrote that class, so naming it as such saves
// the reader a search that ends in the C# spec.
const CLOSURE = /(^|\.)(\[\]|<>)c__DisplayClass(\d+)_\d+$/;
const LAMBDA_CACHE = /(^|\.)(\[\]|<>)c$/;

function closureNote(t) {
  const closure = CLOSURE.exec(t);
  if (closure) {
    const owner = shortType(t.replace(CLOSURE, ''));
    return `the captured variables of a lambda written in ${owner} (method #${closure[3]}) — whatever that lambda used is kept alive here`;
  }
  if (LAMBDA_CACHE.test(t))
    return `the cache of lambdas without captures in ${shortType(t.replace(LAMBDA_CACHE, ''))} — static by design, not a leak by itself`;
  if (t.includes('AsyncStateMachineBox') || t.includes('d__'))
    return 'the state machine of an async method — it holds everything the method had in scope until it finishes';
  return '';
}

function isClosure(t) { return CLOSURE.test(t) || LAMBDA_CACHE.test(t); }

function shortType(t) {
  const bare = t.replace(/<.*$/, '');
  return bare.slice(bare.lastIndexOf('.') + 1) || t;
}

function renderPathStack(jobId, rootIndex, pathIndex, path, appTypes, focus) {
  const state = { inDelegateRun: false, firstAppSeen: false, firstPackageSeen: false };
  let html = `<div class="pathstack${focus ? ' focus' : ''}" id="pathstack-${pathIndex}"><div class="pathstackhead">chain ${pathIndex + 1} · ${path.length} hops`
    + ` <button onclick="copyPath(${jobId}, ${rootIndex}, ${pathIndex})" title="Copy the chain as text">⧉ copy</button></div>`;
  path.forEach((t, i) => {
    const note = stepNote(path, i, appTypes, state);
    if (i > 0) html += '<div class="pathconn">held by</div>';
    html += `<div class="pathrow"><span class="pathnum">${i}</span><span class="pathtype ${stepClass(t, i, appTypes)}">${pathEscape(t)}</span>`
      + (note ? `<span class="pathnote${note.startsWith('the first app type') ? ' fix' : ''}">${pathEscape(note)}</span>` : '') + '</div>';
  });
  return html + '</div>';
}

function openPathPopup(jobId, rootIndex, pathIndex) {
  const job = memDumps.find(j => j.id === jobId);
  const root = job?.report?.roots?.[rootIndex];
  if (!root) return;
  const appTypes = reportAppTypes(job.report);
  pathPackageTypes = reportPackageTypes(job.report);
  document.getElementById('pathtitle').textContent = root.type;
  document.getElementById('pathhint').textContent = `${root.matched} instance${root.matched === 1 ? '' : 's'} in dump #${job.id} · ${root.paths.length} distinct chain${root.paths.length === 1 ? '' : 's'} · read top-down: each row is held by the one below`;
  document.getElementById('pathbody').innerHTML = root.paths.map((p, i) => renderPathStack(jobId, rootIndex, i, p, appTypes, i === pathIndex)).join('');
  document.getElementById('pathback').hidden = false;
  document.getElementById('pathstack-' + pathIndex)?.scrollIntoView({ block: 'nearest' });
}

function closePathPopup() {
  document.getElementById('pathback').hidden = true;
}

function copyPath(jobId, rootIndex, pathIndex) {
  const path = memDumps.find(j => j.id === jobId)?.report?.roots?.[rootIndex]?.paths?.[pathIndex];
  if (!path) return;
  const text = path.map((t, i) => (i === 0 ? '' : '  held by ') + t).join('\n');
  try { navigator.clipboard.writeText(text); } catch { /* no clipboard in this context */ }
}

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && !document.getElementById('pathback').hidden) closePathPopup();
});

// Parents of a snapshot suspect: the logical tree it still sits in, up to the oldest ancestor —
// the top of the subtree that was dropped as a whole. The heap dump's root path then says who
// holds that top; the "who holds" button jumps there when a report has it.
function openParentsPopup(groupKey) {
  const g = memSuspectGroups.get(groupKey);
  if (!g) return;
  const chains = [...g.chains.entries()].sort((a, b) => b[1] - a[1]);
  document.getElementById('pathtitle').textContent = g.name;
  const what = `${g.count} detached ${g.kind === 'Element' ? 'element' : g.kind === 'BindingContext' ? 'view model' : g.kind.toLowerCase()}${g.count === 1 ? '' : 's'}`;
  document.getElementById('pathhint').textContent = chains.length
    ? `${what} · ${chains.length} distinct parent chain${chains.length === 1 ? '' : 's'} · read top-down: each row sits inside the one below`
    : `${what} · the top of its own subtree (no parent) — what holds it is listed below`;
  const held = g.holders && g.holders.size
    ? `<div class="pathstack holders"><div class="pathstackhead">held by — found in-process, no dump needed</div>${[...g.holders].map(h => `<div class="pathrow"><span class="pathnum">⛓</span><span class="pathtype pstep app">${pathEscape(h)}</span><span class="pathnote fix">${holderAdvice(h)}</span></div>`).join('')}</div>`
    : '';
  document.getElementById('pathbody').innerHTML = held + chains.map(([key, count], i) => renderParentStack(g, key.split('|'), count, i)).join('');
  document.getElementById('pathback').hidden = false;
}

function holderAdvice(holder) {
  if (holder.startsWith('static event') || holder.startsWith('event')) return 'unsubscribe when the page goes away (OnNavigatedFrom / OnDisappearing), or use WeakEventManager';
  if (holder.includes('collection')) return 'remove it from the collection when done — or hold weakly';
  if (holder.startsWith('static field') || holder.startsWith('field')) return 'clear the reference when the page goes away';
  return '';
}

function renderParentStack(g, parents, count, index) {
  const rows = [g.name, ...parents];
  const top = rows[rows.length - 1];
  const holder = latestRootFor(top) ?? latestRootFor(g.name);
  let html = `<div class="pathstack${index === 0 ? ' focus' : ''}"><div class="pathstackhead">×${count} · ${parents.length} level${parents.length === 1 ? '' : 's'} up`
    + (holder ? ` <button onclick="openPathPopup(${holder.jobId}, ${holder.rootIndex}, 0)" title="The heap dump's chain from ${pathEscape(holder.type)} to its GC root">🧬 who holds ${pathEscape(shortType(holder.type))}?</button>` : '')
    + '</div>';
  rows.forEach((label, i) => {
    const last = i === rows.length - 1;
    const note = i === 0 ? (g.kind === 'BindingContext' ? 'the element bound to the view model' : 'the detached object')
      : last ? 'the oldest ancestor — no window above it; whoever holds this holds the whole subtree' : '';
    if (i > 0) html += '<div class="pathconn inside">inside</div>';
    html += `<div class="pathrow"><span class="pathnum">${i}</span><span class="pathtype ${i === 0 ? 'pstep held' : last ? 'pstep root' : 'pstep'}">${pathEscape(label)}</span>`
      + (note ? `<span class="pathnote${last ? ' fix' : ''}">${pathEscape(note)}</span>` : '') + '</div>';
  });
  return html + '</div>';
}

// The newest done dump's root entry whose type ends with this label's type name (labels carry @x:Name / #AutomationId).
// What a leaked type costs: the retained size of its instances in the newest heap dump — every
// byte that would go away with them. The spanning tree attributes a shared object to one owner, so
// the number is the standard estimate, not a sum of overlapping subtrees.
function retainedFor(typeName) {
  const root = latestRootFor(typeName);
  if (!root) return null;
  const entry = memDumps.find(j => j.id === root.jobId)?.report?.roots?.[root.rootIndex];
  return entry && entry.retained ? { bytes: entry.retained, matched: entry.matched, jobId: root.jobId } : null;
}

function latestRootFor(label) {
  const name = label.split(' ')[0];
  for (const job of memDumps) {
    if (job.phase !== 'done' || !job.report?.roots) continue;
    const rootIndex = job.report.roots.findIndex(r => r.type === name || r.type.endsWith('.' + name) || r.type.endsWith('+' + name));
    if (rootIndex >= 0) return { jobId: job.id, rootIndex: rootIndex, type: job.report.roots[rootIndex].type };
  }
  return null;
}
