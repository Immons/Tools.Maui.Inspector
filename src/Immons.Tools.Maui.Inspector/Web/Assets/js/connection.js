// Connection state of the panel ↔ app link. Without it the panel looks alive after the app
// goes away: buttons still click, nothing happens. Three states, because "reachable" and
// "able to act" are not the same thing — a backgrounded app still answers HTTP while its main
// thread is parked, so edits would queue up and appear to do nothing.
let connState = '';   // '' = not known yet, so the first result always renders
let connDevice = '';

function setConnected(up, foreground) {
  const state = !up ? 'down' : (foreground === false ? 'bg' : 'up');
  if (state === connState) return;
  connState = state;

  const host = document.getElementById('conn');
  const label = document.getElementById('win');
  if (!host || !label) return;

  host.classList.toggle('up', state === 'up');
  host.classList.toggle('bg', state === 'bg');
  host.classList.toggle('down', state === 'down');

  if (state === 'up') {
    host.title = 'Connected to the app';
    if (connDevice) { label.textContent = connDevice; connDevice = ''; }
  } else {
    if (!connDevice) connDevice = label.textContent;
    if (state === 'bg') {
      host.title = 'The app is not responding — most likely in the background (iOS suspends the whole process). '
        + 'Bring it back on screen; edits made now would not apply.';
      label.textContent = 'app in background';
    } else {
      host.title = 'No connection to the app — it may have been stopped, restarted on another port, or lost its adb forward';
      label.textContent = 'disconnected';
    }
  }
}

// One host port, two servers. An iOS simulator app binds the Mac's port directly; `adb forward
// tcp:P tcp:P` binds the same one for an Android app. Both listeners survive (one wildcard, one
// on 127.0.0.1) and the kernel hands them connections in turn — so the panel shows one app's
// header over another app's data and nothing adds up. The nonce in every /api/selection answer
// is what gives it away; say so plainly instead of letting it read as a bug in the app.
let connInstance = '';

function checkInstance(instance) {
  if (!instance) return;
  if (!connInstance) { connInstance = instance; return; }
  if (instance === connInstance) return;

  const host = document.getElementById('conn');
  const label = document.getElementById('win');
  if (host) {
    host.classList.remove('up', 'bg');
    host.classList.add('down');
    host.title = 'Two different apps are answering on this port. On a Mac an iOS simulator app listens on the '
      + 'host port itself, and `adb forward tcp:P tcp:P` puts an Android app on the same one — both bind, and '
      + 'requests alternate between them. Run maui-inspector-sync — it maps every device onto a free host port '
      + 'by itself; by hand, `adb forward --remove tcp:P` and then a shifted port, `adb forward tcp:1P tcp:P`.';
  }
  if (label) label.textContent = 'two apps on this port';
}
