// Drives the web panel's SignalR view against a running app (the sample's "📡 SignalR demo" page
// connected to samples/Immons.Tools.Maui.Inspector.SignalRServer) and screenshots what it shows.
//   SMOKE_TAG     file-name prefix for this run (e.g. connected / reconnecting / closed)
//   SMOKE_DETAIL  method whose first row gets expanded (pretty JSON + facts)
//   SMOKE_FILTER  text typed into the method filter
//   SMOKE_INJECT  "Method|[json args]" — sent through the inject form
const OUT = process.env.SMOKE_OUT || './';
const TAG = process.env.SMOKE_TAG || 'signalr';

async function until(page, expression, timeoutMs, label) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    if (await page.evaluate(expression)) return true;
    await page.wait(250);
  }
  page.log('  TIMEOUT waiting for', label);
  return false;
}

export async function run(page) {
  await until(page, 'activeView === "signalr" && srUi.hubs.length > 0 && srUi.events.length > 0', 10000, 'hubs and events');
  page.log('hubs:', await page.evaluate('JSON.stringify(srUi.hubs.map(h => [h.name, h.state, "reconnects=" + h.reconnects, h.transport, h.protocol]))'));
  page.log('events cached:', await page.evaluate('srUi.events.length'), 'rows:', await page.evaluate('document.querySelectorAll("#srevents .srrow:not(.nethead)").length'));
  page.log('badge:', await page.evaluate('document.getElementById("srbadge").hidden ? "(hidden)" : document.getElementById("srbadge").textContent'));
  await page.screenshot(`${OUT}${TAG}-overview.png`);

  if (process.env.SMOKE_FILTER) {
    await page.evaluate(`(() => { const i = document.getElementById("srmethod"); i.value = ${JSON.stringify(process.env.SMOKE_FILTER)}; srFilterChanged(); })()`);
    page.log('filtered rows:', await page.evaluate('document.querySelectorAll("#srevents .srrow:not(.nethead)").length'));
    await page.screenshot(`${OUT}${TAG}-filter.png`);
    await page.evaluate('(() => { document.getElementById("srmethod").value = ""; srFilterChanged(); })()');
  }

  if (process.env.SMOKE_DETAIL) {
    const opened = await page.evaluate(`(() => {
      const row = [...document.querySelectorAll("#srevents .srrow:not(.nethead)")]
        .find(r => r.querySelector(".srmethod")?.textContent === ${JSON.stringify(process.env.SMOKE_DETAIL)});
      if (!row) return false; row.click(); return true; })()`);
    page.log('detail opened:', opened);
    if (opened) {
      await until(page, '!!document.querySelector("#srevents .netdetail")', 4000, 'detail');
      page.log('detail facts:', await page.evaluate('document.querySelector("#srevents .netdetail .srfacts")?.innerText.replace(/\\n/g, " | ")'));
      page.log('detail payload:', await page.evaluate('document.querySelector("#srevents .netdetail .srpre")?.textContent.slice(0, 300)'));
      await page.screenshot(`${OUT}${TAG}-detail.png`);
    }
  }

  if (process.env.SMOKE_INJECT) {
    const [method, args] = process.env.SMOKE_INJECT.split('|');
    await page.evaluate(`openSrInject(srUi.hubs[0].name, ${JSON.stringify(method)}, ${JSON.stringify(args || '[]')})`);
    await page.evaluate('[...document.querySelectorAll("#srinject button")].find(b => b.textContent.includes("Inject")).click()');
    await until(page, 'document.getElementById("srinjectresult").textContent.length > 2', 4000, 'inject result');
    page.log('inject result:', await page.evaluate('document.getElementById("srinjectresult").textContent'));
    await until(page, 'srUi.events.some(e => e.injected)', 4000, 'injected event');
    await page.wait(1200);
    await page.screenshot(`${OUT}${TAG}-inject.png`);
  }
}
