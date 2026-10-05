// Receives { tracks: [{ id, name, color, points: [[lat, lon], ...oldest first] }], labels, fit } from the host app.
const map = L.map('map', { zoomControl: true, worldCopyJump: true }).setView([46.8, 8.2], 7);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
  maxZoom: 19,
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
    L.polyline(pts, { color: track.color, weight: 4, opacity: 0.8 }).addTo(layer);
    L.circleMarker(pts[0], { radius: 4, color: track.color, weight: 2, fillColor: '#fff', fillOpacity: 1 }).addTo(layer);
    const marker = L.circleMarker(pts[pts.length - 1], {
      radius: 9, color: '#fff', weight: 3, fillColor: track.color, fillOpacity: 1
    }).addTo(layer);
    if (msg.labels) {
      // The name is chosen by the user in the Husqvarna account: Leaflet would parse a string as HTML.
      const label = document.createElement('span');
      label.textContent = track.name;
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
