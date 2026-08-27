// Drives the web panel's Memory view against the app on localhost (launch the sample with
// HV_LEAK=3 so three leaky pages were pushed and popped) and reports what it sees. With
// maui-inspector-sync running and a diagnostics-enabled build it also orders a heap dump.
const OUT = process.env.SMOKE_OUT || './';
const DUMP_WAIT_MS = Number(process.env.SMOKE_DUMP_WAIT_MS || 240000);

async function until(page, expression, timeoutMs, label) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    if (await page.evaluate(expression)) return true;
    await page.wait(500);
  }
  page.log('  TIMEOUT waiting for', label);
  return false;
}

export async function run(page) {
  page.log('view:', await page.evaluate('activeView'));
  await until(page, 'memStats != null', 10000, 'memory stats');
  page.log('numbers:', await page.evaluate('document.getElementById("memnumbers").innerText.replace(/\\n/g, " | ")'));
  page.log('platform:', await page.evaluate('memStats.platform'), 'virtual:', await page.evaluate('memStats.virtual'),
    'diagnostics:', await page.evaluate('memStats.diagnostics'), 'syncTool:', await page.evaluate('memStats.syncTool'));

  // A snapshot: several GC rounds, then the survivors without a window.
  await page.evaluate('runSnapshot()');
  await until(page, 'memSnapshot != null && document.getElementById("memSnapBtn").disabled === false', 30000, 'snapshot');
  page.log('snapshot:', await page.evaluate('JSON.stringify(memSnapshot.totals)'), 'in', await page.evaluate('memSnapshot.elapsedMs'), 'ms');
  page.log('suspects (app):', await page.evaluate('memSnapshot.suspects.filter(s => s.app).map(s => s.name + "/" + s.kind + (s.hints.length ? " [" + s.hints.join("; ") + "]" : "")).join(" · ")'));
  page.log('leaky rows:', await page.evaluate('memSnapshot.rows.filter(r => r.type.includes("Leaky")).map(r => r.name + " " + r.kind + " alive=" + r.alive + " detached=" + r.detached).join(" · ")'));
  page.log('suspect lines rendered:', await page.evaluate('document.querySelectorAll("#memsuspects .suspect").length'));

  // The parents of a suspect as a stack: click the first clickable group, read the popup back.
  const parentsOpened = await page.evaluate('(() => { const s = document.querySelector("#memsuspects .suspect.clickable"); if (!s) return false; s.click(); return !document.getElementById("pathback").hidden; })()');
  page.log('parents popup opened:', parentsOpened);
  if (parentsOpened) {
    page.log('parents title:', await page.evaluate('document.getElementById("pathtitle").textContent'), '—', await page.evaluate('document.getElementById("pathhint").textContent'));
    page.log('parents rows:', await page.evaluate('[...document.querySelectorAll("#pathbody .pathstack.focus .pathrow .pathtype")].map(r => r.textContent).join(" ⇡ ")'));
    await page.screenshot(OUT + 'web-memory-parents.png', false);
    await page.evaluate('closePathPopup()');
  }

  // A second snapshot right away: nothing new should be detached, Δ column present.
  await page.evaluate('runSnapshot()');
  await until(page, 'document.getElementById("memSnapBtn").disabled === false', 30000, 'second snapshot');
  page.log('second snapshot Δ for leaky rows:', await page.evaluate('memSnapshot.rows.filter(r => r.type.includes("Leaky")).map(r => r.name + " Δ" + r.delta).join(" · ")'));

  // Watch mode, ledger, history, images, holders, bisection switches, exports.
  page.log('holders:', await page.evaluate('[...document.querySelectorAll("#memsuspects .sholder")].map(h => h.textContent).slice(0, 3).join(" | ")'));
  await page.evaluate('toggleWatch()');
  await page.wait(800);
  page.log('watch on:', await page.evaluate('memSettings && memSettings.watch'), 'button active:', await page.evaluate('document.getElementById("memWatchBtn").classList.contains("active")'));
  await page.evaluate('loadLedger()');
  await page.wait(600);
  page.log('ledger rows:', await page.evaluate('memLedger ? memLedger.entries.slice(0, 4).map(e => e.label + ":" + e.verdict).join(" · ") : "none"'), '| badge:', await page.evaluate('document.getElementById("membadge").hidden ? "hidden" : document.getElementById("membadge").textContent'));
  await page.evaluate('loadHistory()');
  await page.wait(500);
  page.log('history points:', await page.evaluate('memHistory.length'), 'chart drawn:', await page.evaluate('!!document.getElementById("memhist")'));
  await page.evaluate('loadImages()');
  await page.wait(800);
  page.log('images:', await page.evaluate('memImages ? memImages.total + " · " + fmtBytes(memImages.bytes) + " · " + memImages.images.slice(0, 2).map(i => i.owner + " " + i.width + "x" + i.height).join(", ") : "n/a"'));
  await page.evaluate('setBisection("disconnectHandlersOnPop", true)');
  await page.wait(500);
  page.log('bisection handlers on:', await page.evaluate('memSettings.disconnectHandlersOnPop'));
  await page.evaluate('setBisection("disconnectHandlersOnPop", false); toggleWatch()');
  await page.wait(500);
  page.log('markdown export length:', await page.evaluate('(() => { const orig = HTMLAnchorElement.prototype.click; let size = 0; HTMLAnchorElement.prototype.click = function () { size = this.href.length; }; exportMemoryMarkdown(); HTMLAnchorElement.prototype.click = orig; return size; })()'));
  await page.screenshot(OUT + 'web-memory-v2.png', true);

  await page.evaluate('forceGc()');
  await page.wait(1500);
  page.log('hint after GC:', await page.evaluate('document.getElementById("memhint").textContent'));
  page.log('peers:', await page.evaluate('memPeers ? (memPeers.supported ? memPeers.total + " surfaced, GREF " + memPeers.grefs + ", top: " + memPeers.types.slice(0, 5).map(t => t.name + "×" + t.count).join(", ") : "not supported here") : "n/a"'));
  await page.screenshot(OUT + 'web-memory.png', true);

  // The hand-off, when both halves are in place.
  const canDump = await page.evaluate('!!(memStats.diagnosticsAvailable && memStats.syncTool)');
  page.log('heap dump available:', canDump, '—', await page.evaluate('document.getElementById("memDumpBtn").title'));
  if (!canDump) return;

  await page.evaluate('requestHeapDump()');
  const done = await until(page, 'memDumps.some(j => j.phase === "done" || j.phase === "failed")', DUMP_WAIT_MS, 'heap dump');
  page.log('dump jobs:', await page.evaluate('memDumps.map(j => "#" + j.id + " " + j.phase + (j.message ? " (" + j.message + ")" : "")).join(" · ")'));
  if (done) {
    page.log('report:', await page.evaluate('(j => j && j.report ? j.report.totalObjects + " objects, " + j.report.typeCount + " types, " + j.report.types.filter(t => t.app).length + " app types, file " + j.file : "none")(memDumps.find(j => j.phase === "done"))'));
    page.log('roots:', await page.evaluate('(j => j && j.report ? j.report.roots.map(r => r.type + " ×" + r.matched + ": " + r.paths.map(p => p.join(" <- ")).join(" || ")).join("\\n") : "none")(memDumps.find(j => j.phase === "done"))'));
    await page.screenshot(OUT + 'web-memory-dump.png', true);

    // A chain as a stack: click the first path line, read the popup back.
    const opened = await page.evaluate('(() => { const p = document.querySelector("#memdumps .rootpath:not(.static)"); if (!p) return false; p.click(); return !document.getElementById("pathback").hidden; })()');
    page.log('path popup opened:', opened);
    if (opened) {
      page.log('popup title:', await page.evaluate('document.getElementById("pathtitle").textContent'), '—', await page.evaluate('document.getElementById("pathhint").textContent'));
      page.log('stack rows:', await page.evaluate('[...document.querySelectorAll("#pathbody .pathstack.focus .pathrow")].map(r => r.querySelector(".pathtype").textContent + (r.querySelector(".pathnote") ? " [" + r.querySelector(".pathnote").textContent + "]" : "")).join(" | ")'));
      await page.screenshot(OUT + 'web-memory-path.png', false);
      await page.evaluate('closePathPopup()');
      page.log('closed:', await page.evaluate('document.getElementById("pathback").hidden'));
    }
  }
}
