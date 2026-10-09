// Receives { tracks: [{ id, name, color, status, offline, points: [[lat, lon], ...oldest first] }], labels, fit }
// from the host app.
// The OpenStreetMap tiles stop at zoom 19 (about 0.3 m per pixel): beyond it the map enlarges the level-19 tiles
// (maxNativeZoom), which is blurrier but lets one place the mower precisely.
const map = L.map('map', { zoomControl: true, worldCopyJump: true, maxZoom: 22 }).setView([46.8, 8.2], 7);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
  maxNativeZoom: 19,
  maxZoom: 22,
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
}).addTo(map);

const layer = L.layerGroup().addTo(map);
let fitted = false;

function render(msg) {
  layer.clearLayers();
  const all = [];
  for (const track of msg.tracks) {
    const pts = track.points;
    if (!pts.length) continue;
    // A mower out of reach left its last positions long ago: dashed and faded, and grey where it was last seen.
    L.polyline(pts, {
      color: track.color, weight: 4, opacity: track.offline ? 0.45 : 0.8, dashArray: track.offline ? '4 8' : null
    }).addTo(layer);
    L.circleMarker(pts[0], { radius: 4, color: track.color, weight: 2, fillColor: '#fff', fillOpacity: 1 }).addTo(layer);
    const marker = L.circleMarker(pts[pts.length - 1], {
      radius: 9, color: '#fff', weight: 3, fillColor: track.offline ? '#9e9e9e' : track.color, fillOpacity: 1
    }).addTo(layer);
    if (msg.labels) {
      // The name is chosen by the user in the Husqvarna account: it goes in as text, not as HTML.
      const label = document.createElement('span');
      const name = document.createElement('strong');
      name.textContent = track.name;
      label.append(name);
      if (track.status) {
        const status = document.createElement('span');
        status.className = track.offline ? 'mower-status offline' : 'mower-status';
        status.textContent = ' · ' + track.status;
        label.append(status);
      }
      marker.bindTooltip(label, { permanent: true, direction: 'right', offset: [10, 0], className: 'mower-label' });
    }
    all.push(...pts);
  }
  if (all.length && (msg.fit || !fitted)) {
    map.fitBounds(L.latLngBounds(all).pad(0.25), { maxZoom: 18 });
    fitted = true;
  }
}

window.chrome.webview.addEventListener('message', e => render(e.data));
window.chrome.webview.postMessage('ready');
