// Drives the web panel's Cookbook view against the app on localhost:9296 and reports what it sees.
const OUT = process.env.SMOKE_OUT || './';

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
  page.log('view:', await page.evaluate('activeView'));
  await until(page, 'cookbook != null && document.querySelectorAll("#cookgrid .cookcard").length > 0', 10000, 'catalog');
  page.log('sections:', await page.evaluate('cookbook.sections.map(s => s.id + ":" + s.items.length).join(", ")'));
  page.log('open on device:', await page.evaluate('cookbook.open'), ' section filter:', await page.evaluate('cookbookSection'),
    ' device view:', await page.evaluate('JSON.stringify(cookbook.view || null)'));
  page.log('chips:', await page.evaluate('[...document.querySelectorAll("#cookchips button")].map(b => b.textContent + (b.classList.contains("active") ? "*" : "")).join(" | ")'));

  page.log('headless: open =', await page.evaluate('cookbook.open'), '(must stay false — the web never opens the device page by itself)');

  // Switch to the Styles section and wait for its previews.
  await page.evaluate('[...document.querySelectorAll("#cookchips button")].find(b => b.textContent.startsWith("Styles")).click()');
  await until(page, '[...document.querySelectorAll("#cookgrid .cookcard[data-preview]")].every(c => c.dataset.loaded === "1")', 30000, 'styles previews');
  const cards = await page.evaluate('[...document.querySelectorAll("#cookgrid .cookcard")].filter(c => !c.classList.contains("hiddenbyfilter")).map(c => c.dataset.id + (c.querySelector("img")?.naturalWidth ? " img" + c.querySelector("img").naturalWidth : "") + (c.querySelector(".cookerror:not([hidden])") ? " ERR:" + c.querySelector(".cookerror").textContent : ""))');
  page.log('styles cards:', cards.join(' · '));
  page.log('hint:', await page.evaluate('document.getElementById("cookhint").textContent'));
  page.log('state pickers:', await page.evaluate('[...document.querySelectorAll("#cookgrid select.cookstates")].filter(s => !s.hidden).map(s => s.closest(".cookcard").dataset.id + "[" + [...s.options].map(o => o.value).filter(Boolean).join(",") + "]").join(" ")'));
  await page.screenshot(OUT + 'web-styles.png', true);

  // Baseline, then change the Primary color through the API (what the Resources popup does) and re-capture.
  await page.evaluate('cookbookBaseline()');
  page.log('baseline size:', await page.evaluate('cookbookBaselineHashes.size'));
  const set = await page.evaluate('fetch("/api/resources/set", { method: "POST", body: JSON.stringify({ key: "Primary", value: "#FFE53935" }) }).then(r => r.json())');
  page.log('set Primary:', JSON.stringify(set));
  // What cookbookOnEdit() schedules (debounced) — awaited directly so the check is deterministic.
  await page.evaluate('loadCookbookPreviews(true)');
  page.log('changed after color edit:', await page.evaluate('[...document.querySelectorAll("#cookgrid .cookcard.changed")].map(c => c.dataset.id).join(", ")'));
  page.log('hint:', await page.evaluate('document.getElementById("cookhint").textContent'));
  await page.evaluate('toggleChangedOnly()');
  page.log('visible with Δ only:', await page.evaluate('[...document.querySelectorAll("#cookgrid .cookcard")].filter(c => !c.classList.contains("hiddenbyfilter")).length'));
  await page.screenshot(OUT + 'web-changed.png', true);
  await page.evaluate('toggleChangedOnly()');

  // Colors section: CSS swatches diff by value, no device round trip.
  await page.evaluate('[...document.querySelectorAll("#cookchips button")].find(b => b.textContent.startsWith("Colors")).click()');
  await page.wait(500);
  await page.evaluate('refreshCookbook(false)');
  await page.wait(1500);
  page.log('colors changed:', await page.evaluate('[...document.querySelectorAll("#cookgrid .cookcard.changed")].map(c => c.dataset.id).join(", ")'));

  // Restore the color.
  await page.evaluate('fetch("/api/resources/set", { method: "POST", body: JSON.stringify({ key: "Primary", value: "#FF512BD4" }) }).then(r => r.json())');
  await page.evaluate('loadCookbookPreviews(true)');

  // Theme buttons and the Inspect action.
  await page.evaluate('setAppTheme("dark")');
  await page.wait(1500);
  page.log('theme buttons:', await page.evaluate('["system","light","dark"].map(t => t + (document.getElementById("theme-" + t).classList.contains("active") ? "*" : "")).join(" ")'));
  await page.evaluate('setAppTheme("system")');
  await page.wait(800);

  await page.evaluate('[...document.querySelectorAll("#cookchips button")].find(b => b.textContent.startsWith("Styles")).click()');
  await until(page, '[...document.querySelectorAll("#cookgrid .cookcard[data-preview]")].every(c => c.dataset.loaded === "1")', 30000, 'styles previews again');
  // Inspect needs the gallery on the device — open it explicitly, as a user would.
  await page.evaluate('setCookbookOnDevice(true)');
  await until(page, 'cookbook && cookbook.open === true', 10000, 'device gallery');
  await page.evaluate('inspectCookbookItem(cookbook.sections.flatMap(s => s.items).find(i => i.id === "style-PrimaryButton"))');
  await page.wait(2500);
  page.log('after inspect: view =', await page.evaluate('activeView'), ' selectedId =', await page.evaluate('selectedId'),
    ' style row =', await page.evaluate('[...document.querySelectorAll("#props td.k")].find(td => td.textContent === "Style")?.nextElementSibling?.textContent?.trim()'));
  await page.screenshot(OUT + 'web-inspect.png', false);

  // Focus popup — headless again once the device gallery is closed: full width, the sheet on demand.
  await page.evaluate('showView("cookbook")');
  await page.wait(500);
  await page.evaluate('setCookbookOnDevice(false)');
  await until(page, 'cookbook && cookbook.open === false', 10000, 'device gallery closed');
  await page.evaluate('openCookbookFocus(cookbook.sections.flatMap(s => s.items).find(i => i.id === "custom-PillBadge"))');
  await until(page, 'document.getElementById("cookfocusimg").naturalWidth > 0', 20000, 'focus preview');
  page.log('sheet hidden by default:', await page.evaluate('document.getElementById("cookpropsbody").hidden'));
  await page.evaluate('toggleCookbookSheet()');
  await until(page, 'document.querySelectorAll("#cookpropsbody .section").length > 0', 20000, 'focus sheet');
  page.log('focus:', await page.evaluate('JSON.stringify(cookbook.focus || null)'), '(onDevice must be false)');
  page.log('popup sections:', await page.evaluate('[...document.querySelectorAll("#cookpropsbody .section")].map(s => (s.tagName === "DETAILS" ? (s.open ? "▾" : "▸") : "") + s.querySelector("h2").textContent).join(" | ")'));
  await until(page, 'document.getElementById("cookfocusimg").naturalWidth > 0', 15000, 'focus preview');
  page.log('focus preview width:', await page.evaluate('document.getElementById("cookfocusimg").naturalWidth'));
  // An edit made in the sheet must show up in the capture without any device page.
  const before = await page.evaluate('document.getElementById("cookfocusimg").dataset.hash');
  await page.evaluate('apply(cookbookFocusElement, "PillBadge properties", "Text", "Edited from the sheet", document.createElement("input"), false)');
  await until(page, 'document.getElementById("cookfocusimg").dataset.hash !== ' + JSON.stringify(before), 8000, 'focus preview refresh after edit');
  page.log('focus preview refreshed after edit:', await page.evaluate('document.getElementById("cookfocusimg").dataset.hash') !== before);
  const w100 = await page.evaluate('setCookbookZoom(1); parseInt(document.getElementById("cookfocusimg").style.width)');
  const w200 = await page.evaluate('setCookbookZoom(2); parseInt(document.getElementById("cookfocusimg").style.width)');
  page.log('zoom 100% → 200%:', w100, '→', w200, w200 === w100 * 2 ? '(ok)' : '(BAD)');
  await page.evaluate('toggleCookbookMax()');
  page.log('maximized:', await page.evaluate('document.getElementById("cookpropspanel").classList.contains("maximized")'));
  await page.screenshot(OUT + 'web-focus-zoom.png', false);
  await page.evaluate('toggleCookbookMax(); setCookbookZoom("fit")');
  await page.evaluate('closeCookbookProps()');
  await page.wait(600);

  // A XAML control bound to its own properties via x:Reference — the sheet edit must reach the capture.
  await page.evaluate('openCookbookFocus(cookbook.sections.flatMap(s => s.items).find(i => i.id === "custom-NoticeView"))');
  await until(page, 'document.getElementById("cookfocusimg").naturalWidth > 0', 20000, 'NoticeView preview');
  await page.evaluate('toggleCookbookSheet()');
  await until(page, 'document.querySelectorAll("#cookpropsbody .section").length > 0', 20000, 'NoticeView sheet');
  const noticeBefore = await page.evaluate('document.getElementById("cookfocusimg").dataset.hash');
  await page.evaluate('apply(cookbookFocusElement, "NoticeView properties", "Title", "Title edited in the sheet", document.createElement("input"), false)');
  await until(page, 'document.getElementById("cookfocusimg").dataset.hash !== ' + JSON.stringify(noticeBefore), 8000, 'NoticeView preview refresh');
  page.log('NoticeView preview refreshed after Title edit:', await page.evaluate('document.getElementById("cookfocusimg").dataset.hash') !== noticeBefore);
  await page.evaluate('setCookbookZoom(2)');
  await page.wait(300);
  await page.screenshot(OUT + 'web-focus-xaml.png', false);
  await page.screenshot(OUT + 'web-focus.png', false);
  await page.evaluate('closeCookbookProps()');
  await page.wait(800);
  page.log('focus after close:', await page.evaluate('fetch("/api/cookbook").then(r => r.json()).then(d => JSON.stringify(d.focus || null))'));

  // Edit action opens the Resources popup on the key.
  await page.evaluate('showView("cookbook")');
  await page.wait(800);
  await page.evaluate('openResourcesFor("PrimaryButton")');
  await page.wait(1500);
  page.log('resources popup hidden:', await page.evaluate('document.getElementById("resback").hidden'), ' visible rows:',
    await page.evaluate('[...document.querySelectorAll("#resourcelist .resrow")].filter(r => !r.classList.contains("hiddenbyfilter")).length'));
  await page.screenshot(OUT + 'web-edit.png', false);
}
