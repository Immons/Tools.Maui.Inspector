# Web panel smoke tests

Drives the embedded web panel of a **running** app in headless Chromium over the DevTools
protocol — no npm dependencies, only Node 22+ (built-in `WebSocket`, `fetch`) and a Chromium
binary (Playwright's cached one works: `~/Library/Caches/ms-playwright/chromium_headless_shell-*/…/chrome-headless-shell`).

```bash
# app running on the simulator, panel on http://localhost:9296
node tests/WebPanelSmoke/cdp.mjs "$CHROME" "http://localhost:9296/#cookbook" tests/WebPanelSmoke/cookbook.smoke.mjs
```

`cdp.mjs` starts the browser, opens the URL and hands a tiny page object (`evaluate`, `screenshot`,
`wait`, `log`) to the script's `run(page)`. Console errors and uncaught exceptions of the page are
echoed. `cookbook.smoke.mjs` walks the Cookbook view: catalog and chips, previews of the Styles
section, baseline → a `Primary` color edit through the API → the tiles marked changed, theme
buttons, the Inspect and Edit actions. Screenshots land in `SMOKE_OUT` (default: current directory).
