const map = L.map('map').setView([43.095, 13.001], 14);

L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '© OpenStreetMap contributors',
    maxZoom: 18
}).addTo(map);

const API_URL = 'https://gis-landslide-detection-eegmbphwg2gdgrfn.switzerlandnorth-01.azurewebsites.net/api/landslide'; 
const SUPABASE_URL = 'https://mvxcotqzxxxknszflxic.supabase.co';
const SUPABASE_KEY = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im12eGNvdHF6eHh4a25zemZseGljIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzM0OTUyMzEsImV4cCI6MjA4OTA3MTIzMX0.__hQw4vr8ewv63Xm3XKXS7FZS2UygoknDgQruGTsTak';

const DANGER_HIGH_STYLE = { color: 'red', weight: 6, className: 'flashing-danger' };
const DANGER_MED_STYLE = { color: '#FF8C00', weight: 5, dashArray: '8, 8' };
const DANGER_LOW_STYLE = { color: '#28a745', weight: 4 };
const HEX_ALERT_ICON = L.divIcon({
    className: 'hex-alert-container',
    html: '<div class="hex-pulse"></div><div class="hex-solid"></div>',
    iconSize: [26, 30],
    iconAnchor: [13, 15]
});
const trailLayers = {};

fetch(`${SUPABASE_URL}/rest/v1/landslide_zones?select=id,nome_tipo,geom`, {
    headers: { 'apikey': SUPABASE_KEY, 'Authorization': 'Bearer ' + SUPABASE_KEY }
})
.then(r => r.json())
.then(zones => {
    zones.forEach(zone => {
        const colors = { 'P4': '#C0392B', 'P3': '#E67E22', 'P2': '#F1C40F', 'P1': '#27AE60' };
        const fill = colors[zone.nome_tipo] || '#888888';

        try {
            L.geoJSON(JSON.parse(zone.geom), {
                style: { color: fill, fillColor: fill, fillOpacity: 0.35, weight: 2 }
            })
            .addTo(map)
            .bindPopup(`<b>Landslide Risk Zone</b><br>Type: <b>${zone.nome_tipo}</b>`);
        } catch(e) {
            console.error("Error parsing geometry data:", e);
        }
    });
})
.catch(err => console.error('Error loading landslide data from Supabase:', err));

function getStyleByDangerIndex(index) {
    if (index >= 80) return DANGER_HIGH_STYLE; 
    if (index >= 50) return DANGER_MED_STYLE; 
    return DANGER_LOW_STYLE; 
}

function buildPopup(data) {
    const riskLevel = data.riskLevel || 'UNKNOWN';
    const riskColors = { 'HIGH': '#C0392B', 'MEDIUM': '#E67E22', 'LOW': '#27AE60', 'CRITICAL': '#C0392B' };
    const headerColor = riskColors[riskLevel] || '#888';
    const riskScore = data.riskScore || 0;

    return `
    <div class="geo-popup" style='font-family:Arial; min-width:260px;'>
        <div class="popup-header" style='background:${headerColor};color:white;padding:10px 12px; border-radius:4px 4px 0 0;font-weight:bold;font-size:14px;'>
            GEOHIKE ALERT — ${riskLevel}
        </div>
        <div class="popup-body" style='padding:10px 12px;'>
            <div style='margin-bottom:8px;'>
                <b>Risk Score:</b> <span style="color:${headerColor};font-size:18px;font-weight:bold;">${riskScore}/100</span>
                <div style='background:#eee;border-radius:4px;height:8px;margin-top:4px;'>
                    <div style='background:${headerColor};width:${riskScore}%;height:8px;border-radius:4px;'></div>
                </div>
            </div>
            <hr style='margin:8px 0;'>
            <b>Soil Moisture:</b> <span style="color:${headerColor};">${data.soilMoisture}%</span><br>
            <b>Precipitation:</b> ${data.precipitationMmh} mm/h<br>
            <b>VV Backscatter:</b> ${data.vvMeanDb} dB<br>
            <hr style='margin:8px 0;'>
            <small>
                Historical Risk: <b>${data.historicalRisk ? 'Yes' : 'No'}</b><br>
                IFFI Level: <b>${data.iffiLevel || 'N/A'}</b>
            </small>
            <br><br>
            <div style='background:${headerColor}22;border-left:3px solid ${headerColor};padding:8px 10px;border-radius:0 4px 4px 0;'>
                <span style='color:${headerColor};font-weight:bold;'>${data.message || ''}</span>
            </div>
            <br>
            <span style='font-size:11px;color:#888;'>Source: Copernicus Sentinel-1 & ECMWF</span>
        </div>
    </div>`;
}

async function analyzeTrail(lat, lng, targetLayer) {
    try {
        targetLayer.bindPopup('⏳ Analyzing satellite data...').openPopup();
        const res = await fetch(`${API_URL}?lat=${lat}&lng=${lng}`);
        if (!res.ok) throw new Error("Network or API issue");
        const data = await res.json();
        targetLayer.bindPopup(buildPopup(data), { maxWidth: 300 }).openPopup();
    } catch (err) {
        console.error('API Error:', err);
        targetLayer.bindPopup('<b>Backend API unreachable</b><br>Check if API is deployed.').openPopup();
    }
}

function loadTrails() {
    const trailsData = [
        { id: 1, name: "Lower Trail", coords: [[43.085, 12.990], [43.090, 12.995]], dangerIndex: 20 },
        { id: 2, name: "Middle Ridge", coords: [[43.090, 12.995], [43.093, 13.000]], dangerIndex: 65 },
        { id: 3, name: "Lame Rosse North", coords: [[43.093, 13.000], [43.098, 13.005], [43.102, 13.002]], dangerIndex: 92 }
    ];

    trailsData.forEach(trail => {
        const pathLine = L.polyline(trail.coords, getStyleByDangerIndex(trail.dangerIndex)).addTo(map);
        trailLayers[trail.id] = pathLine;

        pathLine.on('click', (e) => {
            analyzeTrail(e.latlng.lat, e.latlng.lng, pathLine);
        });

        if (trail.dangerIndex >= 80) {
            const middleIndex = Math.floor(trail.coords.length / 2);
            const middlePoint = trail.coords[middleIndex];
            const hexMarker = L.marker(middlePoint, { icon: HEX_ALERT_ICON }).addTo(map);
            hexMarker.on('click', () => {
                analyzeTrail(middlePoint[0], middlePoint[1], hexMarker);
            });
        }
    });
}

loadTrails();

const darkModeToggle = document.getElementById('darkModeToggle');
const body = document.body;

let currentTileLayer = null;
map.eachLayer((layer) => {
    if (layer instanceof L.TileLayer) {
        currentTileLayer = layer;
    }
});

darkModeToggle.addEventListener('click', () => {
    body.classList.toggle('dark-mode');
    
    if (body.classList.contains('dark-mode')) {
        darkModeToggle.innerText = 'Light Mode';
    } else {
        darkModeToggle.innerText = 'Dark Mode';
    }
});
/*
const map = L.map('map').setView([43.095, 13.001], 14);

L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '© OpenStreetMap contributors',
    maxZoom: 18
}).addTo(map);

const API_BASE = 'https://tuoapp.azurewebsites.net';
const SUPABASE_URL = 'https://mvxcotqzxxxknszflxic.supabase.co';
const SUPABASE_KEY = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im12eGNvdHF6eHh4a25zemZseGljIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzM0OTUyMzEsImV4cCI6MjA4OTA3MTIzMX0.__hQw4vr8ewv63Xm3XKXS7FZS2UygoknDgQruGTsTak';

const STYLE_DEFAULT  = { color: '#888888', weight: 4, opacity: 0.7 };
const STYLE_LOADING  = { color: '#3498DB', weight: 4, opacity: 0.9, dashArray: '6,4' };
const STYLE_CRITICAL = { color: '#C0392B', weight: 6, opacity: 1.0 };
const STYLE_MEDIUM   = { color: '#E67E22', weight: 5, opacity: 1.0 };
const STYLE_LOW      = { color: '#27AE60', weight: 4, opacity: 1.0 };

const trailLayers = {};
let criticalMarker = null;

fetch(`${SUPABASE_URL}/rest/v1/landslide_zones?select=id,nome_tipo,geom`, {
    headers: { 'apikey': SUPABASE_KEY, 'Authorization': 'Bearer ' + SUPABASE_KEY }
})
.then(r => r.json())
.then(zones => {
    zones.forEach(zone => {
        const colors = { 'P4': '#C0392B', 'P3': '#E67E22', 'P2': '#F1C40F', 'P1': '#27AE60' };
        const fill = colors[zone.nome_tipo] || '#888888';
        try {
            L.geoJSON(JSON.parse(zone.geom), {
                style: { color: fill, fillColor: fill, fillOpacity: 0.35, weight: 2 }
            })
            .addTo(map)
            .bindPopup(`<b>Landslide Risk Zone</b><br>Type: <b>${zone.nome_tipo}</b>`);
        } catch(e) {
            console.error("Error parsing geometry data:", e);
        }
    });
})
.catch(err => console.error('Error loading landslide data from Supabase:', err));

async function loadTrails() {
    try {
        const res    = await fetch(`${API_BASE}/api/trails`);
        const trails = await res.json();

        trails.forEach(trail => {
            if (!trail.geom) return;
            const geomObj = JSON.parse(trail.geom);
            const latlngs = geomObj.coordinates.map(c => [c[1], c[0]]);
            const polyline = L.polyline(latlngs, STYLE_DEFAULT).addTo(map);
            if (trail.name) polyline.bindTooltip(trail.name, { permanent: false, sticky: true });
            polyline.on('click', () => analyzeTrail(trail.id, polyline));
            trailLayers[trail.id] = polyline;
        });

        console.log(`Loaded ${trails.length} trails`);

    } catch (err) {
        console.error('Error loading trails — using mock data:', err);

        const mockTrails = [
            { id: 1, name: "Lower Trail",      geom: '{"type":"LineString","coordinates":[[12.990,43.085],[12.995,43.090]]}' },
            { id: 2, name: "Middle Ridge",     geom: '{"type":"LineString","coordinates":[[12.995,43.090],[13.000,43.093]]}' },
            { id: 3, name: "Lame Rosse North", geom: '{"type":"LineString","coordinates":[[13.000,43.093],[13.005,43.098],[13.002,43.102]]}' }
        ];

        mockTrails.forEach(trail => {
            const geomObj = JSON.parse(trail.geom);
            const latlngs = geomObj.coordinates.map(c => [c[1], c[0]]);
            const polyline = L.polyline(latlngs, STYLE_DEFAULT).addTo(map);
            if (trail.name) polyline.bindTooltip(trail.name, { permanent: false, sticky: true });
            polyline.on('click', () => analyzeTrail(trail.id, polyline));
            trailLayers[trail.id] = polyline;
        });
    }
}

loadTrails();

async function analyzeTrail(trailId, polyline) {
    polyline.setStyle(STYLE_LOADING);
    polyline.bindPopup(' Analyzing risk data...').openPopup();

    try {
        const res  = await fetch(`${API_BASE}/api/trails/${trailId}/risk`);
        if (!res.ok) throw new Error('API error: ' + res.status);
        const data = await res.json();

        const styles = { 'CRITICAL': STYLE_CRITICAL, 'MEDIUM': STYLE_MEDIUM, 'LOW': STYLE_LOW };
        polyline.setStyle(styles[data.riskLevel] ?? STYLE_DEFAULT);

        if (criticalMarker) { map.removeLayer(criticalMarker); criticalMarker = null; }

        if (data.criticalPointLat && data.criticalPointLng) {
            criticalMarker = L.circleMarker(
                [data.criticalPointLat, data.criticalPointLng],
                { radius: 12, fillColor: '#C0392B', color: '#FFFFFF', weight: 3, fillOpacity: 1.0 }
            ).addTo(map);
        }

        polyline.bindPopup(buildPopup(data), { maxWidth: 320 }).openPopup();

    } catch (err) {
        console.error('API Error:', err);
        polyline.setStyle(STYLE_DEFAULT);
        polyline.bindPopup('<b>Backend API unreachable</b><br>Check if API is deployed.').openPopup();
    }
}

function buildPopup(data) {
    const headerColor = {
        'CRITICAL': '#C0392B', 'MEDIUM': '#E67E22', 'LOW': '#27AE60'
    }[data.riskLevel] ?? '#888888';

    const icon = {
        'CRITICAL': '🔴', 'MEDIUM': '🟡', 'LOW': '🟢'
    }[data.riskLevel] ?? '⚪';

    const barWidth = Math.round(data.riskScore ?? 0);

    return `
    <div style='font-family:Arial;min-width:280px;border-radius:6px;overflow:hidden;'>
      <div style='background:${headerColor};color:#fff;padding:10px 14px;font-size:15px;font-weight:bold;'>
        ${icon} GEOHIKE ALERT — ${data.riskLevel}
      </div>
      <div style='padding:12px 14px;border:1px solid #eee;border-top:none;'>
        <b style='font-size:14px;'>${data.trailName ?? 'Trail'}</b>
        <div style='margin:8px 0 4px;font-size:12px;color:#555;'>Risk Score: <b>${data.riskScore ?? 'N/A'}/100</b></div>
        <div style='background:#eee;border-radius:4px;height:8px;'>
          <div style='background:${headerColor};width:${barWidth}%;height:8px;border-radius:4px;'></div>
        </div>
        <hr style='margin:10px 0;border:none;border-top:1px solid #eee;'>
        <div style='margin-bottom:6px;font-size:12px;'>
          <b>Historical risk:</b>
          ${data.historicalRisk
            ? `<span style='color:#C0392B;'>${data.iffiTipo} (${data.iffiZoneCount} zones)</span>`
            : '<span style="color:#27AE60;">No IFFI zones</span>'}
        </div>
        <div style='margin-bottom:6px;font-size:12px;'>
           <b>Soil moisture:</b> ${data.soilMoisture ?? 'N/A'}/100
          <span style='color:#888;font-size:11px;'>(${data.vvMeanDb ?? 'N/A'} dB · ${data.sentinelSource ?? 'Sentinel-1'})</span>
        </div>
        <div style='margin-bottom:10px;font-size:12px;'>
           <b>Rainfall:</b> ${data.precipitationMmh ?? 'N/A'} mm/h
          <span style='color:#888;font-size:11px;'>(${data.weatherSource ?? 'Open-Meteo'})</span>
        </div>
        <div style='background:${headerColor}22;border-left:3px solid ${headerColor};padding:6px 10px;border-radius:0 4px 4px 0;font-size:12px;font-weight:bold;color:${headerColor};'>
          ${data.message ?? ''}
        </div>
      </div>
    </div>`;
}

const darkModeToggle = document.getElementById('darkModeToggle');
const body = document.body;

let currentTileLayer = null;
map.eachLayer((layer) => {
    if (layer instanceof L.TileLayer) { currentTileLayer = layer; }
});

darkModeToggle.addEventListener('click', () => {
    body.classList.toggle('dark-mode');
    darkModeToggle.innerText = body.classList.contains('dark-mode') ? 'Light Mode' : 'Dark Mode';
});
*/