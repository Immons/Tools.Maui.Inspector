# Changelog

## Unreleased (proposed 0.9.22)

- Web server: survives the app going to the background. iOS reclaims a suspended app's listening sockets — afterwards the port still completed TCP handshakes (`nc -z` said open) but HttpListener's internal accept never fired again, so every HTTP request timed out until the app was restarted. The listening socket is now rebuilt on the same port when the app returns to the foreground (iOS: always; Android: when a loopback self-probe gets no answer) and when the network comes back or switches interfaces, with retries while the port is still held (auto-assigned servers move to another free port of the range as a last resort). A watchdog probes `/api/ping?probe=1` through loopback every 15 s while the app is in the foreground and rebuilds the socket after two misses in a row. The accept loop no longer dies silently on an exception: it logs, backs off and rebuilds the socket after repeated failures. `/api/ping` reports `listenerRestarts`.

## 0.9.21

- Memory: Watch mode is now on by default (web panel button and on-device 🧠 Mem pane show it on after connecting). Switch it off with the button or `options.Memory.WatchNavigation = false`. Release builds are unaffected as long as the package is referenced Debug-only.
