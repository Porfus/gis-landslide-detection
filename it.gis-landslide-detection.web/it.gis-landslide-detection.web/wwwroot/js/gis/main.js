import { state } from './state.js';
import { apiFetch } from './api.js';
import { showToast, initClock, updateStats, renderFeatureList, renderLegend, highlightFeature } from './ui.js';
import { initMap, choroplethStyle, makePopup, clearMode, zoomToFeature } from './map.js';

/* ═══ LOAD DATA ═══ */
async function loadAll() {
    try {
        const minArea = getMinAreaFilter();
        const areaParam = minArea > 0 ? `?minArea=${minArea}` : '';
        const [pts, lns, pgs] = await Promise.all([
            apiFetch('/api/GisData/points'),
            apiFetch('/api/GisData/lines'),
            apiFetch(`/api/GisData/polygons${areaParam}`)
        ]);
        state.data.points = pts;
        state.data.lines = lns;
        state.data.polygons = pgs;
        renderLayers();
        updateStats(state);
        renderFeatureList(state);
        if (state.isEditMode) setTimeout(enableEditing, 500);
    } catch (err) {
        console.error('Errore caricamento dati:', err);
        showToast('Errore di connessione alle API', 'error');
    }
}

function getMinAreaFilter() {
    const el = document.getElementById('filter-min-area');
    return el ? parseFloat(el.value) || 0 : 0;
}

/* ═══ RENDER LAYERS ═══ */
function renderLayers() {
    const m = state.map;
    ['points', 'lines', 'polygons'].forEach(k => {
        if (state.layers[k]) { m.removeLayer(state.layers[k]); state.layers[k] = null; }
    });

    if (state.visible.points && state.data.points) {
        state.layers.points = L.geoJSON(state.data.points, {
            pointToLayer: (f, ll) => L.circleMarker(ll, {
                radius: 8, fillColor: '#38bdf8', color: '#0f172a', weight: 2, fillOpacity: 0.9
            }),
            onEachFeature: (f, layer) => {
                const p = f.properties;
                const fid = f.id || p.id;
                layer.bindPopup(makePopup('point', { ...p, id: fid }));
                layer.on('click', () => highlightFeature(fid, 'point'));
                layer.on('pm:dragend', () => onPointDragged(layer, fid));
            }
        }).addTo(m);
    }

    if (state.visible.lines && state.data.lines) {
        state.layers.lines = L.geoJSON(state.data.lines, {
            style: (f) => {
                const type = f.properties?.type || f.properties?.Type;
                if (type === 'Torrente') {
                    return { color: '#0284c7', weight: 3, opacity: 0.85 };
                } else if (type === 'Fiume') {
                    return { color: '#1e40af', weight: 4, opacity: 0.85 };
                } else if (type === 'Sentiero') {
                    return { color: '#a16207', weight: 2.5, opacity: 0.9, dashArray: '6,3' };
                }
                return { color: '#10ffb0', weight: 3, opacity: 0.8 };
            },
            onEachFeature: (f, layer) => {
                const p = f.properties;
                const fid = f.id || p.id;
                const type = p.type || p.Type;

                if (type === 'Sentiero') {
                    layer.bindPopup((lyr) => {
                        const popupId = `popup-sentiero-${fid}`;
                        setTimeout(() => fetchSentieroDanger(layer, fid, popupId), 10);
                        return `<div id="${popupId}" data-name="${p.name || ''}" style="width:250px; font-family:'DM Mono', monospace; font-size:11px; color:#cbd5e1;">
                                  <strong>${p.name || 'Sentiero'}</strong><br/>
                                  <span style="color:#64748b;">Analisi pericolo frana in corso... 🔄</span>
                                </div>`;
                    });
                } else {
                    layer.bindPopup(makePopup('line', { ...p, id: fid }));
                }

                layer.on('click', (e) => {
                    if (state.mode === 'route') { onMapClick(e); return; }
                    highlightFeature(fid, 'line');
                });
                layer.on('pm:edit', () => onLineEdited(layer, fid));
            }
        }).addTo(m);
    }

    if (state.visible.polygons && state.data.polygons) {
        state.layers.polygons = L.geoJSON(state.data.polygons, {
            style: f => choroplethStyle(f.properties.riskLevel ?? f.properties.population ?? 0),
            onEachFeature: (f, layer) => {
                const p = f.properties;
                const fid = f.id || p.id;
                layer.bindPopup(makePopup('polygon', { ...p, id: fid }));
                layer.on('click', () => highlightFeature(fid, 'polygon'));
                layer.on('pm:edit', () => onPolygonEdited(layer, fid));
            }
        }).addTo(m);
        renderLegend();
    }
}

/* ═══ EDIT HANDLERS ═══ */
async function onPointDragged(layer, id) {
    if (!id || id === 'undefined') return;
    const ll = layer.getLatLng();
    const f = findFeature(id, 'point');
    if (!f) return;
    try {
        await apiFetch(`/api/GisData/points/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: f.name, type: f.type, lat: ll.lat, lng: ll.lng })
        });
        showToast('Posizione aggiornata!', 'success');
        await loadAll();
    } catch (err) { showToast('Errore aggiornamento: ' + err.message, 'error'); }
}

async function onLineEdited(layer, id) {
    if (!id || id === 'undefined') return;
    const coords = layer.getLatLngs().map(ll => [ll.lng, ll.lat]);
    const f = findFeature(id, 'line');
    if (!f) return;
    try {
        await apiFetch(`/api/GisData/lines/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: f.name, type: f.type, coordinates: coords })
        });
        showToast('Sentiero aggiornato!', 'success');
        await loadAll();
    } catch (err) { showToast('Errore: ' + err.message, 'error'); }
}

async function onPolygonEdited(layer, id) {
    if (!id || id === 'undefined') return;
    const coords = layer.getLatLngs()[0].map(ll => [ll.lng, ll.lat]);
    coords.push(coords[0]);
    const f = findFeature(id, 'polygon');
    if (!f) return;
    try {
        await apiFetch(`/api/GisData/polygons/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: f.name, population: f.riskLevel ?? f.population ?? 0, coordinates: coords })
        });
        showToast('Zona di rischio aggiornata!', 'success');
        await loadAll();
    } catch (err) { showToast('Errore: ' + err.message, 'error'); }
}

function findFeature(id, type) {
    const src = type === 'point' ? state.data.points : type === 'line' ? state.data.lines : state.data.polygons;
    const f = src?.features?.find(f => (f.id || f.properties?.id) === id);
    return f?.properties;
}

/* ═══ LAYER VISIBILITY ═══ */
function toggleLayer(name, btn) {
    state.visible[name] = !state.visible[name];
    btn.classList.toggle('active');
    renderLayers();
}

/* ═══ FILTER ═══ */
function applyFilter() {
    const typeFilter = document.getElementById('filter-type').value;
    if (!typeFilter) { loadAll(); return; }

    // Linee: Sentiero, Torrente, Fiume
    const isLineType = ['Sentiero', 'Torrente', 'Fiume'].includes(typeFilter);

    if (isLineType) {
        apiFetch(`/api/GisData/lines?type=${typeFilter}`).then(d => {
            state.data.lines = d;
            state.data.points = { type: 'FeatureCollection', features: [] };
            renderLayers(); updateStats(state); renderFeatureList(state);
        });
    } else {
        // Punti: Baita, PuntoRistoro
        apiFetch(`/api/GisData/points?type=${typeFilter}`).then(d => {
            state.data.points = d;
            state.data.lines = { type: 'FeatureCollection', features: [] };
            renderLayers(); updateStats(state); renderFeatureList(state);
        });
    }
}

/* ═══ FILTRO SUPERFICIE POLIGONI ═══ */
function applyAreaFilter() {
    loadAll(); // loadAll legge già il valore da #filter-min-area
}

/* ═══ DRAW TOOLS ═══ */
function disableEditing() {
    ['points', 'lines', 'polygons'].forEach(k => {
        if (state.layers[k]) {
            state.layers[k].eachLayer(layer => {
                if (layer.pm) layer.pm.disable();
            });
        }
    });
}

window.showCustomModal = (title, fields, onConfirm, onCancel) => {
  const titleEl = document.getElementById('custom-modal-title');
  const container = document.getElementById('custom-modal-fields-container');
  const btnConfirm = document.getElementById('custom-modal-btn-confirm');
  const btnCancel = document.getElementById('custom-modal-btn-cancel');

  titleEl.textContent = title;
  container.innerHTML = '';

  const inputs = {};
  fields.forEach(f => {
    const fieldDiv = document.createElement('div');
    fieldDiv.className = 'custom-modal-field';
    
    const label = document.createElement('label');
    label.className = 'custom-modal-label';
    label.textContent = f.label;
    
    let input;
    if (f.options) {
        input = document.createElement('select');
        input.className = 'custom-modal-input';
        f.options.forEach(opt => {
            const o = document.createElement('option');
            o.value = opt.value;
            o.textContent = opt.label;
            if (opt.value === f.default) o.selected = true;
            input.appendChild(o);
        });
    } else {
        input = document.createElement('input');
        input.className = 'custom-modal-input';
        input.type = f.type || 'text';
        input.value = f.default || '';
    }
    input.id = 'modal-input-' + f.name;
    
    fieldDiv.appendChild(label);
    fieldDiv.appendChild(input);
    container.appendChild(fieldDiv);
    inputs[f.name] = input;
  });

  const overlay = document.getElementById('custom-modal-overlay');
  overlay.classList.add('active');
  if (fields.length > 0) setTimeout(() => inputs[fields[0].name].focus(), 50);

  const cleanUp = () => {
    overlay.classList.remove('active');
    btnConfirm.onclick = null;
    btnCancel.onclick = null;
  };

  btnConfirm.onclick = () => {
    const data = {};
    fields.forEach(f => { data[f.name] = inputs[f.name].value; });
    cleanUp();
    if (onConfirm) onConfirm(data);
  };

  btnCancel.onclick = () => {
    cleanUp();
    if (onCancel) onCancel();
  };
};

window.showCustomConfirm = (message, onConfirm, onCancel) => {
  const titleEl = document.getElementById('custom-modal-title');
  const container = document.getElementById('custom-modal-fields-container');
  const btnConfirm = document.getElementById('custom-modal-btn-confirm');
  const btnCancel = document.getElementById('custom-modal-btn-cancel');

  titleEl.textContent = 'Eliminazione ⚠️';
  container.innerHTML = `<div style="font-size:0.95rem;line-height:1.4;margin-bottom:12px;color:#cbd5e1;">${message}</div>`;

  const overlay = document.getElementById('custom-modal-overlay');
  overlay.classList.add('active');

  const cleanUp = () => {
    overlay.classList.remove('active');
    btnConfirm.onclick = null;
    btnCancel.onclick = null;
  };

  btnConfirm.onclick = () => {
    cleanUp();
    if (onConfirm) onConfirm();
  };

  btnCancel.onclick = () => {
    cleanUp();
    if (onCancel) onCancel();
  };
};

function startDrawPoint() {
    clearMode();
    window.showCustomModal('Nuovo Punto 📍', [
        { name: 'name', label: 'Nome', default: 'Nuovo Punto' },
        { 
            name: 'type', 
            label: 'Tipo', 
            options: [
                { value: 'Baita', label: '🏔️ Baita' },
                { value: 'PuntoRistoro', label: '☕ Punto Ristoro' }
            ],
            default: 'Baita'
        }
    ], (data) => {
        if (!data.name || !data.type) return;
        state.drawMeta = { name: data.name, type: data.type };
        disableEditing();
        state.map.pm.enableDraw('Marker');
        showToast('Clicca sulla mappa per posizionare il punto', 'success');
    });
}

function startDrawLine() {
    clearMode();
    window.showCustomModal('Nuova Linea 📏', [
        { name: 'name', label: 'Nome', default: 'Nuovo Sentiero' },
        {
            name: 'type',
            label: 'Tipo',
            options: [
                { value: 'Sentiero', label: '🥾 Sentiero' },
                { value: 'Torrente', label: '🌊 Torrente' },
                { value: 'Fiume', label: '🏞️ Fiume' }
            ],
            default: 'Sentiero'
        }
    ], (data) => {
        if (!data.name || !data.type) return;
        state.drawMeta = { name: data.name, type: data.type };
        disableEditing();
        state.map.pm.enableDraw('Line');
        showToast('Clicca per disegnare i vertici, doppio-click per terminare', 'success');
    });
}

function startDrawPolygon() {
    clearMode();
    window.showCustomModal('Nuova Zona di Rischio ⬡', [
        { name: 'name', label: 'Nome della zona', default: 'Nuova Zona Rischio' },
        { 
            name: 'population',
            label: 'Livello di rischio (0-100)',
            type: 'number',
            default: '40',
            // Suggerimento: 100=ColamentoRapido, 80=Crollo, 60=Scivolamento, 40=Complesso, 20=Basso
        }
    ], (data) => {
        if (!data.name) return;
        const risk = Math.max(0, Math.min(100, parseInt(data.population || '40')));
        state.drawMeta = { name: data.name, population: risk };
        disableEditing();
        state.map.pm.enableDraw('Polygon');
        showToast('Clicca per disegnare i vertici, clicca sul primo per chiudere', 'success');
    });
}

async function onDrawCreated(e) {
    const layer = e.layer;
    const shape = e.shape;

    // ── Within mode: handle the search polygon ──
    if (state.mode === 'within' && shape === 'Polygon') {
        const coords = layer.getLatLngs()[0].map(ll => [ll.lng, ll.lat]);
        coords.push(coords[0]);
        state.searchAreaLayer = layer;
        layer.setStyle({ color: '#38bdf8', fillColor: '#38bdf8', fillOpacity: 0.1, dashArray: '6,4' });
        layer.bringToBack();

        try {
            const filterType = document.getElementById('filter-type')?.value;
            const bodyPayload = { name: 'search', coordinates: coords };
            if (filterType) bodyPayload.type = filterType;
            const result = await apiFetch('/api/GisData/points/within', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(bodyPayload)
            });
            if (state.withinLayer) state.map.removeLayer(state.withinLayer);
            state.withinLayer = L.geoJSON(result, {
                pointToLayer: (f, ll) => L.circleMarker(ll, {
                    radius: 9, fillColor: '#ffea00', color: '#0f172a', weight: 2, fillOpacity: 0.9
                }),
                onEachFeature: (f, layer) => layer.bindPopup(makePopup('point', f.properties))
            }).addTo(state.map);
            const count = result?.features?.length || 0;
            showToast(`Trovati ${count} punti nell'area selezionata`, 'success');
        } catch (err) { showToast('Errore: ' + err.message, 'error'); }
        state.mode = null;
        document.getElementById('btn-within').classList.remove('active');
        return;
    }

    // ── Intersection mode: handle the search polygon ──
    if (state.mode === 'intersection' && shape === 'Polygon') {
        const coords = layer.getLatLngs()[0].map(ll => [ll.lng, ll.lat]);
        coords.push(coords[0]);
        state.searchAreaLayer = layer;
        layer.setStyle({ color: '#a855f7', fillColor: '#a855f7', fillOpacity: 0.1, dashArray: '6,4' });
        layer.bringToBack();

        try {
            const result = await apiFetch('/api/GisData/intersections', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: 'intersection-search', coordinates: coords })
            });
            if (state.intersectionLayer) state.map.removeLayer(state.intersectionLayer);
            state.intersectionLayer = L.geoJSON(result, {
                style: { color: '#a855f7', weight: 5, opacity: 1 },
                onEachFeature: (f, layer) => layer.bindPopup(makePopup('line', f.properties))
            }).addTo(state.map);
            const count = result?.features?.length || 0;
            showToast(`${count} sentieri intersecano l'area`, 'success');
        } catch (err) { showToast('Errore: ' + err.message, 'error'); }
        state.mode = null;
        document.getElementById('btn-intersection').classList.remove('active');
        return;
    }

    // ── Draw mode: create new features ──
    if (!state.drawMeta) return;
    const meta = state.drawMeta;
    state.map.removeLayer(layer);

    try {
        if (shape === 'Marker') {
            const ll = layer.getLatLng();
            await apiFetch('/api/GisData/points', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: meta.name, type: meta.type, lat: ll.lat, lng: ll.lng })
            });
        } else if (shape === 'Line') {
            const coords = layer.getLatLngs().map(ll => [ll.lng, ll.lat]);
            await apiFetch('/api/GisData/lines', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: meta.name, type: meta.type, coordinates: coords })
            });
        } else if (shape === 'Polygon') {
            const coords = layer.getLatLngs()[0].map(ll => [ll.lng, ll.lat]);
            coords.push(coords[0]);
            await apiFetch('/api/GisData/polygons', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: meta.name, population: meta.population || 0, coordinates: coords })
            });
        }
        showToast('Elemento creato con successo!', 'success');
        await loadAll();
    } catch (err) {
        showToast('Errore: ' + err.message, 'error');
    }
    state.drawMeta = null;
}

/* ═══ DELETE TOOL ═══ */
async function deleteFeature(id, type) {
    window.showCustomConfirm('Sei sicuro di voler eliminare definitivamente questo elemento? Questa azione non può essere annullata.', async () => {
        const endpoint = type === 'point' ? 'points' : type === 'line' ? 'lines' : 'polygons';
        try {
            await apiFetch(`/api/GisData/${endpoint}/${id}`, { method: 'DELETE' });
            showToast('Elemento eliminato!', 'success');
            await loadAll();
        } catch (err) { showToast('Errore: ' + err.message, 'error'); }
    });
}

/* ═══ SPATIAL TOOLS ═══ */
function toggleNearest() {
    if (state.mode === 'nearest') { clearMode(); return; }
    clearMode();
    state.mode = 'nearest';
    document.getElementById('btn-nearest').classList.add('active');
    const sel = document.getElementById('nearest-type');
    const t = sel ? sel.value : '';
    const label = t === 'Baita' ? 'baite' : t === 'PuntoRistoro' ? 'punti ristoro' : 'POI';
    showToast(`Clicca sulla mappa per trovare i 5 ${label} più vicini`, 'success');
}

function toggleWithin() {
    if (state.mode === 'within') { clearMode(); return; }
    clearMode();
    state.mode = 'within';
    document.getElementById('btn-within').classList.add('active');
    state.map.pm.enableDraw('Polygon');
    showToast('Disegna un\'area per trovare i punti al suo interno', 'success');
}

function toggleIntersection() {
    if (state.mode === 'intersection') { clearMode(); return; }
    clearMode();
    state.mode = 'intersection';
    document.getElementById('btn-intersection').classList.add('active');
    state.map.pm.enableDraw('Polygon');
    showToast('Disegna un\'area per trovare i sentieri che la attraversano', 'success');
}

function toggleRoute() {
    if (state.mode === 'route') { clearMode(); return; }
    clearMode();
    state.mode = 'route';
    state.routePoints = [];
    document.getElementById('btn-route').classList.add('active');
    const rs = document.getElementById('routing-status');
    if (rs) {
        rs.textContent = 'Clicca sulla mappa: imposta il punto di PARTENZA';
        rs.classList.add('active');
    }
}

function toggleTsp() {
    if (state.mode === 'tsp') {
        // Secondo click sul bottone = esegui calcolo se ci sono almeno 2 POI
        if (state.tspPoints.length >= 2) {
            computeTsp();
        } else {
            showToast('Servono almeno 2 punti per il TSP', 'error');
            clearMode();
        }
        return;
    }
    clearMode();
    state.mode = 'tsp';
    state.tspPoints = [];
    state.tspMarkers = [];
    document.getElementById('btn-tsp').classList.add('active');
    const rs = document.getElementById('routing-status');
    if (rs) {
        rs.textContent = 'TSP: clicca i POI da visitare (2-12). Riclicca 🔁 TSP per calcolare il tour.';
        rs.classList.add('active');
    }
}

async function computeTsp() {
    const rs = document.getElementById('routing-status');
    if (rs) rs.textContent = `Calcolo TSP su ${state.tspPoints.length} punti (NN + 2-opt)...`;
    try {
        const body = {
            points: state.tspPoints.map(ll => [ll.lng, ll.lat]),
            closed: true
        };
        const result = await apiFetch('/api/GisData/tsp', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (state.tspLayer) state.map.removeLayer(state.tspLayer);
        state.tspLayer = L.geoJSON(result, {
            style: { color: '#ff8c00', weight: 5, opacity: 0.95, dashArray: '10,5' }
        }).addTo(state.map);

        // Etichette ordine di visita
        const ordered = result?.properties?.orderedPoints || [];
        ordered.forEach((p, idx) => {
            const marker = L.marker([p[1], p[0]], {
                icon: L.divIcon({
                    className: 'tsp-order-label',
                    html: `<div style="background:#ff8c00;color:#0f172a;border:2px solid #0f172a;border-radius:50%;width:24px;height:24px;display:flex;align-items:center;justify-content:center;font-weight:700;font-family:'DM Mono',monospace;font-size:11px;">${idx + 1}</div>`,
                    iconSize: [24, 24],
                    iconAnchor: [12, 12]
                })
            }).addTo(state.map);
            state.tspMarkers.push(marker);
        });

        state.map.fitBounds(state.tspLayer.getBounds(), { padding: [60, 60] });
        if (rs) rs.textContent = `Tour TSP: ${state.tspPoints.length} POI, lunghezza ${formatRouteLength(result)}`;
        showToast('Tour TSP calcolato!', 'success');
    } catch (err) {
        if (rs) rs.textContent = 'TSP fallito: nessun percorso trovato';
        showToast('Errore TSP: ' + err.message, 'error');
    }
    state.mode = null;
    document.getElementById('btn-tsp').classList.remove('active');
}

function formatRouteLength(result) {
    const coords = result?.geometry?.coordinates;
    if (!coords || coords.length < 2) return '0 m';
    let totalMeters = 0;
    for (let i = 0; i < coords.length - 1; i++) {
        const p1 = L.latLng(coords[i][1], coords[i][0]);
        const p2 = L.latLng(coords[i+1][1], coords[i+1][0]);
        totalMeters += p1.distanceTo(p2);
    }
    if (totalMeters >= 1000) {
        return `${(totalMeters / 1000).toFixed(2)} km`;
    }
    return `${totalMeters.toFixed(0)} m`;
}

async function onMapClick(e) {
    // Skip map click when Geoman is actively drawing
    if (state.map.pm.globalDrawModeEnabled()) return;
    if (state.mode === 'nearest') {
        try {
            const sel = document.getElementById('nearest-type');
            const typeFilter = sel ? sel.value : '';
            let url = `/api/GisData/nearest?lat=${e.latlng.lat}&lng=${e.latlng.lng}&limit=5`;
            if (typeFilter) url += `&type=${encodeURIComponent(typeFilter)}`;
            const result = await apiFetch(url);
            if (state.nearestLayer) state.map.removeLayer(state.nearestLayer);
            state.nearestLayer = L.geoJSON(result, {
                pointToLayer: (f, ll) => L.circleMarker(ll, {
                    radius: 9, fillColor: '#ff8c00', color: '#0f172a', weight: 2, fillOpacity: 0.9
                }),
                onEachFeature: (f, layer) => layer.bindPopup(makePopup('point', f.properties))
            }).addTo(state.map);
            L.circleMarker(e.latlng, { radius: 5, fillColor: '#ff4060', color: '#fff', weight: 2, fillOpacity: 1 }).addTo(state.nearestLayer);
            const count = result?.features?.length || 0;
            const label = typeFilter === 'Baita' ? 'baite' : typeFilter === 'PuntoRistoro' ? 'punti ristoro' : 'punti';
            showToast(`${count} ${label} più vicini trovati!`, 'success');
        } catch (err) { showToast('Errore: ' + err.message, 'error'); }

    } else if (state.mode === 'tsp') {
        if (state.tspPoints.length >= 12) {
            showToast('Limite 12 punti raggiunto. Riclicca 🔁 TSP per calcolare.', 'info');
            return;
        }
        state.tspPoints.push(e.latlng);
        const idx = state.tspPoints.length;
        const marker = L.marker(e.latlng, {
            icon: L.divIcon({
                className: 'tsp-pick',
                html: `<div style="background:#38bdf8;color:#0f172a;border:2px solid #0f172a;border-radius:50%;width:22px;height:22px;display:flex;align-items:center;justify-content:center;font-weight:700;font-family:'DM Mono',monospace;font-size:10px;">${idx}</div>`,
                iconSize: [22, 22],
                iconAnchor: [11, 11]
            })
        }).addTo(state.map);
        state.tspMarkers.push(marker);
        const rs = document.getElementById('routing-status');
        if (rs) rs.textContent = `TSP: ${idx} POI selezionati. Aggiungi altri o riclicca 🔁 TSP per calcolare.`;

    } else if (state.mode === 'route') {
        state.routePoints.push(e.latlng);
        const marker = L.circleMarker(e.latlng, { radius: 8, fillColor: state.routePoints.length === 1 ? '#10ffb0' : '#ff4060',
            color: '#0f172a', weight: 2, fillOpacity: 1 }).addTo(state.map);
        state.routeMarkers.push(marker);

        const rs = document.getElementById('routing-status');
        if (state.routePoints.length === 1) {
            if (rs) rs.textContent = 'Ora clicca il punto di ARRIVO';
        } else if (state.routePoints.length === 2) {
            if (rs) rs.textContent = 'Calcolo percorso Dijkstra in corso...';
            const [start, end] = state.routePoints;
            try {
                const result = await apiFetch(`/api/GisData/route?startLat=${start.lat}&startLng=${start.lng}&endLat=${end.lat}&endLng=${end.lng}`);
                if (state.routeLayer) state.map.removeLayer(state.routeLayer);
                state.routeLayer = L.geoJSON(result, {
                    style: { color: '#38bdf8', weight: 5, opacity: 1, dashArray: '8,4' }
                }).addTo(state.map);
                
                const p = result.properties;
                if (p && p.snappedStartLat && p.snappedStartLng && p.snappedEndLat && p.snappedEndLng) {
                    if (state.routeMarkers[0]) {
                        state.routeMarkers[0].setLatLng([p.snappedStartLat, p.snappedStartLng]);
                    }
                    if (state.routeMarkers[1]) {
                        state.routeMarkers[1].setLatLng([p.snappedEndLat, p.snappedEndLng]);
                    }
                }

                state.map.fitBounds(state.routeLayer.getBounds(), { padding: [50, 50] });
                if (rs) rs.textContent = `Percorso trovato! Lunghezza: ${formatRouteLength(result)}`;
                showToast('Percorso calcolato con successo!', 'success');
            } catch (err) {
                if (rs) rs.textContent = 'Nessun percorso trovato';
                showToast('Nessun percorso trovato tra i punti', 'error');
            }
            state.mode = null;
            document.getElementById('btn-route').classList.remove('active');
        }
    }
}

function enableEditing() {
    showToast('Seleziona un elemento sulla mappa o dalla lista per modificarlo', 'info');
}

function startEditingLayer(layer) {
    if (state.currentlyEditingLayer && state.currentlyEditingLayer !== layer) {
        try { state.currentlyEditingLayer.pm.disable(); } catch(err){}
    }
    state.currentlyEditingLayer = layer;
    if (layer.pm) {
        layer.pm.enable({
            allowSelfIntersection: false,
            draggable: true
        });
        showToast('Modifica abilitata. Trascina i nodi per cambiare la forma/posizione.', 'success');
    }
}

// ─── INITIALIZATION ───
initMap(onMapClick, onDrawCreated);

// Gestione Modifica Dati On-Demand (Evita crash di performance con migliaia di record)
state.currentlyEditingLayer = null;
state.map.on('popupopen', (e) => {
    if (state.isEditMode && e.popup && e.popup._source) {
        startEditingLayer(e.popup._source);
    }
});
state.map.on('popupclose', (e) => {
    if (state.currentlyEditingLayer) {
        try { state.currentlyEditingLayer.pm.disable(); } catch(err){}
        state.currentlyEditingLayer = null;
    }
});

initClock();
loadAll();

function toggleEditMode() {
    state.isEditMode = !state.isEditMode;
    const btn = document.getElementById('btn-edit');
    if (state.isEditMode) {
        if (btn) { btn.textContent = '✏️ Modifica Dati: ON'; btn.classList.add('active'); }
        enableEditing();
        showToast('Modalità modifica attivata', 'success');
    } else {
        if (btn) { btn.textContent = '✏️ Modifica Dati: OFF'; btn.classList.remove('active'); }
        disableEditing();
        showToast('Modalità modifica disattivata', 'info');
    }
}

async function fetchSentieroDanger(layer, id, popupId) {
    const el = document.getElementById(popupId);
    if (!el) return;

    try {
        const center = layer.getBounds().getCenter();
        const data = await apiFetch(`/api/Landslide?lat=${center.lat}&lng=${center.lng}`);
        
        let riskColor;
        let riskLabel;
        if (data.hazardLevel === 'CRITICAL') {
            riskColor = 'var(--neon-crit)';
            riskLabel = '🔴 CRITICO';
        } else if (data.hazardLevel === 'HIGH') {
            riskColor = 'var(--neon-high)';
            riskLabel = '🟠 ALTO';
        } else if (data.hazardLevel === 'MEDIUM') {
            riskColor = 'var(--neon-warn)';
            riskLabel = '🟡 MEDIO';
        } else {
            riskColor = 'var(--neon-safe)';
            riskLabel = '🟢 BASSO';
        }

        const deleteBtn = `
          <div style="margin-top:10px;border-top:1px solid rgba(148,163,184,0.12);padding-top:8px;display:flex;justify-content:flex-end;">
            <button onclick="event.stopPropagation();GIS.deleteFeature(${id},'line')" 
                    title="Elimina sentiero" 
                    style="background:none;border:none;color:#ff4060;cursor:pointer;padding:2px 4px;font-size:10px;display:flex;align-items:center;gap:4px;font-family:inherit;">
              <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="stroke:#ff4060;">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
              Elimina Sentiero
            </button>
          </div>
        `;

        el.innerHTML = `
          <div style="font-family:'Syne', sans-serif; font-weight:700; font-size:13px; color:#f8fafc; margin-bottom:6px; border-bottom:1px solid rgba(148,163,184,0.12); padding-bottom:4px;">
            🥾 ${el.dataset.name || 'Sentiero'}
          </div>
          <div style="margin-bottom:8px;">
            Stato Pericolo: <strong style="color:${riskColor};">${riskLabel}</strong> (${data.hazardScore}/100)
          </div>
          <div style="font-size:10px; color:#94a3b8; display:grid; grid-template-columns: 1fr 1fr; gap:6px; background:rgba(255,255,255,0.03); border:1px solid rgba(148,163,184,0.08); border-radius:6px; padding:6px; margin-bottom:8px;">
            <div>💧 Umidità Suolo:<br/><strong style="color:#f8fafc;">${data.soilMoisture}%</strong></div>
            <div>🌧️ Pioggia Attuale:<br/><strong style="color:#f8fafc;">${data.precipitationMmh.toFixed(1)} mm/h</strong></div>
            <div style="grid-column: span 2;">🗺️ Catasto IFFI:<br/><strong style="color:#f8fafc;">${data.iffiLevel}</strong></div>
          </div>
          <div style="font-size:10px; line-height:1.4; color:#e2e8f0; background:rgba(56,189,248,0.06); border:1px solid rgba(56,189,248,0.15); border-radius:6px; padding:6px 8px;">
            ${data.message}
          </div>
          ${deleteBtn}
        `;
    } catch (err) {
        console.error('Errore analisi sentiero:', err);
        el.innerHTML = `
          <strong>${el.dataset.name || 'Sentiero'}</strong><br/>
          <span style="color:var(--neon-crit); font-size: 10px;">Impossibile calcolare la pericolosità in tempo reale. Verificare connessione API.</span>
        `;
    }
}

// Expose GIS to global scope to keep inline HTML event handlers (onclick="GIS.x()") working seamlessly
window.GIS = {
    toggleLayer, applyFilter, applyAreaFilter,
    startDrawPoint, startDrawLine, startDrawPolygon,
    toggleNearest, toggleWithin, toggleIntersection, toggleRoute, toggleTsp,
    deleteFeature, zoomTo: zoomToFeature, toggleEditMode
};
