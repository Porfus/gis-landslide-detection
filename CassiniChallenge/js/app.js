const map = L.map('map').setView([43.095, 13.001], 14);

L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '© OpenStreetMap contributors',
    maxZoom: 18
}).addTo(map);

function getStyleByDangerIndex(index) {
    if (index >= 80) {
        return { color: 'red', weight: 6, className: 'flashing-danger' }; 
    } else if (index >= 50) {
        return { color: '#FF8C00', weight: 5, dashArray: '8, 8' }; 
    } else {
        return { color: '#28a745', weight: 4 }; 
    }
}

const zonaGialla = [[43.102, 12.993], [43.105, 13.005], [43.102, 13.012], [43.093, 13.010], [43.091, 12.996]];
const zonaRossa = [[43.098, 12.997], [43.101, 13.002], [43.099, 13.008], [43.096, 13.006], [43.095, 13.000]];

L.polygon(zonaGialla, { color: '#FF8C00', fillColor: '#FFD700', fillOpacity: 0.35, weight: 2 })
    .addTo(map).bindPopup('<b>⚡ Moderate Risk Zone</b><br>High soil moisture detected.');

L.polygon(zonaRossa, { color: '#C0392B', fillColor: '#E74C3C', fillOpacity: 0.45, weight: 2 })
    .addTo(map).bindPopup('<b>🔴 CRITICAL ZONE</b><br>High landslide probability.');

const mockApiData = [
    { id: 1, name: "Lower Trail", coords: [[43.085, 12.990], [43.090, 12.995]], dangerIndex: 20, condition: "Safe 🟢" },
    { id: 2, name: "Middle Ridge", coords: [[43.090, 12.995], [43.093, 13.000]], dangerIndex: 65, condition: "Wet soil 🟡" },
    { id: 3, name: "Lame Rosse North", coords: [[43.093, 13.000], [43.098, 13.005], [43.102, 13.002]], dangerIndex: 92, moisture: "85%", rain: "47 mm/h" }
];

mockApiData.forEach(trail => {
    const pathLine = L.polyline(trail.coords, getStyleByDangerIndex(trail.dangerIndex)).addTo(map);

    if (trail.dangerIndex >= 80) {
        const middleIndex = Math.floor(trail.coords.length / 2);
        const middlePoint = trail.coords[middleIndex];

        const hexIcon = L.divIcon({
            className: 'hex-alert-container',
            html: '<div class="hex-pulse"></div><div class="hex-solid"></div>',
            iconSize: [26, 30], 
            iconAnchor: [13, 15] 
        });

        const hexMarker = L.marker(middlePoint, { icon: hexIcon }).addTo(map);

        const alertHTML = `
            <div style="font-family: Arial, sans-serif;">
                <div style="background: #C0392B; color: white; padding: 12px; font-weight: bold; text-align: center; font-size: 16px;">
                    CRITICAL LANDSLIDE ALERT
                </div>
                <div style="padding: 15px; background: #fff;">
                    <p style="margin: 0 0 10px 0; color: #333; font-size: 15px;"><b>Path Segment:</b> ${trail.name}</p>
                    <p style="margin: 0 0 6px 0; font-size: 14px; color: #555;">
                        Danger Index: <b style="color: #C0392B; font-size: 16px;">${trail.dangerIndex}/100</b>
                    </p>
                    <p style="margin: 0 0 6px 0; font-size: 14px; color: #555;">
                        Soil Moisture: <b>${trail.moisture || "N/A"}</b>
                    </p>
                    <p style="margin: 0 0 6px 0; font-size: 14px; color: #555;">
                        Rainfall: <b>${trail.rain || "N/A"}</b>
                    </p>
                    <hr style="border: 0; border-top: 1px solid #eee; margin: 12px 0;">
                    <p style="margin: 0; font-size: 11px; color: #888; text-align: center;">
                        Data Source: Sentinel-1 & Copernicus ECMWF
                    </p>
                </div>
            </div>
        `;

        hexMarker.bindPopup(alertHTML, { className: 'alert-popup' });
        setTimeout(() => hexMarker.openPopup(), 1000);
    } else {
        pathLine.bindPopup(`<b>${trail.name}</b><br>Conditions: ${trail.condition}`);
    }
});
