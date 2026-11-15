#!/usr/bin/env bash
# Drive a MAUI app with the inspector's web server from the Mac — simulator or physical device.
# Needs only curl and python3 (for JSON escaping). See docs/HTTP-API.md, "Remote control".
#
#   tools/inspector-remote.sh discover [subnet]   # find apps: localhost 9295-9309 (+ a /24 subnet, e.g. 192.168.1)
#   export INSPECTOR=http://192.168.1.23:9295     # pick one (default http://localhost:9295)
#   tools/inspector-remote.sh ping | page | shot out.jpg | tree
#   tools/inspector-remote.sh tap-text "Zaloguj"     tap-id LoginButton     tap 120 340
#   tools/inspector-remote.sh text-id EmailEntry "me@example.com"           text-ph "Hasło" "secret" submit
#   tools/inspector-remote.sh find-text "Czat"       wait-text "Panel" 15000   wait-gone "Ładowanie"
#   tools/inspector-remote.sh scroll down [amount]   scroll-to-text "Ustawienia"   swipe 200 600 200 200 [ms]
#   tools/inspector-remote.sh long-press-text "Wiadomość" [ms]                 back
#   tools/inspector-remote.sh post /api/tap '{"automationId":"X","index":1}'  # anything else
set -euo pipefail

BASE="${INSPECTOR:-http://localhost:9295}"

# json k1 v1 k2 v2 … → a JSON object; values that look like numbers/booleans stay unquoted.
json() {
  python3 -c '
import json, sys, re
a = sys.argv[1:]
o = {}
for k, v in zip(a[0::2], a[1::2]):
    if re.fullmatch(r"-?\d+(\.\d+)?", v): o[k] = float(v) if "." in v else int(v)
    elif v in ("true", "false"): o[k] = v == "true"
    else: o[k] = v
print(json.dumps(o, ensure_ascii=False))' "$@"
}

post() { curl -sS -X POST "$BASE$1" -H 'Content-Type: application/json' -d "$2"; echo; }
get() { curl -sS "$BASE$1"; echo; }
enc() { python3 -c 'import urllib.parse, sys; print(urllib.parse.quote(sys.argv[1]))' "$1"; }

discover() {
  local hosts=(localhost)
  if [[ -n "${1:-}" ]]; then for i in $(seq 1 254); do hosts+=("$1.$i"); done; fi
  for h in "${hosts[@]}"; do for p in $(seq 9295 9309); do echo "$h $p"; done; done |
    xargs -P 64 -n 2 sh -c 'r=$(curl -s -m 0.4 "http://$0:$1/api/ping") && case "$r" in *\"instance\"*) echo "http://$0:$1  $r";; esac; exit 0' 2>/dev/null |
    python3 -c '
import json, sys
for line in sys.stdin:
    url, _, body = line.strip().partition("  ")
    try:
        d = json.loads(body)
    except Exception:
        continue
    name = d.get("deviceName") or d.get("device", "?")
    print("%-28s %-16s %-24s %-8s %s v%s" % (url, d.get("app", "?"), name, d.get("platform", ""), d.get("appId", ""), d.get("version", "?")))'
}

cmd="${1:-help}"; shift || true
case "$cmd" in
  discover)        discover "${1:-}" ;;
  ping)            get /api/ping ;;
  page)            get /api/page ;;
  tree)            get /api/dump ;;
  shot)            curl -sS -o "${1:-screen.jpg}" "$BASE/api/screenshot" && echo "${1:-screen.jpg}" ;;
  tap)             post /api/tap "$(json x "$1" y "$2")" ;;
  tap-id)          post /api/tap "$(json automationId "$1" index "${2:-0}")" ;;
  tap-text)        post /api/tap "$(json text "$1" index "${2:-0}")" ;;
  long-press-id)   post /api/long-press "$(json automationId "$1" durationMs "${2:-800}")" ;;
  long-press-text) post /api/long-press "$(json text "$1" durationMs "${2:-800}")" ;;
  text-id)         post /api/text "$(json automationId "$1" value "$2" submit "$([[ "${3:-}" == submit ]] && echo true || echo false)")" ;;
  text-ph)         post /api/text "$(json placeholder "$1" value "$2" submit "$([[ "${3:-}" == submit ]] && echo true || echo false)")" ;;
  text-focused)    post /api/text "$(json value "$1")" ;;
  find-id)         get "/api/find?automationId=$(enc "$1")" ;;
  find-text)       get "/api/find?text=$(enc "$1")" ;;
  wait-id)         post /api/wait "$(json automationId "$1" timeoutMs "${2:-10000}")" ;;
  wait-text)       post /api/wait "$(json text "$1" timeoutMs "${2:-10000}")" ;;
  wait-gone)       post /api/wait "$(json text "$1" timeoutMs "${2:-10000}" gone true)" ;;
  scroll)          post /api/scroll "$(json direction "${1:-down}" amount "${2:-0.8}")" ;;
  scroll-to-text)  post /api/scroll-to "$(json text "$1" direction "${2:-down}")" ;;
  scroll-to-id)    post /api/scroll-to "$(json automationId "$1" direction "${2:-down}")" ;;
  swipe)           post /api/swipe "$(json x1 "$1" y1 "$2" x2 "$3" y2 "$4" durationMs "${5:-300}")" ;;
  back)            post /api/back '{}' ;;
  key)             post /api/key "$(json key "$1")" ;;
  post)            post "$1" "${2:-{\}}" ;;
  get)             get "$1" ;;
  *)               sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//' ;;
esac
