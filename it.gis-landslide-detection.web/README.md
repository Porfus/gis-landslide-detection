# 🌍 GeoSentinel Explorer — GIS Landslide Detection

**Branch:** `gis.exam.unicam.it`

Web application ASP.NET Core 8 per la visualizzazione e l'analisi di dati GIS legati al rischio frana nell'area Camerino / Monti Sibillini.
L'utente può esplorare sentieri, zone di rischio (classificazione IFFI), punti di interesse, calcolare percorsi ottimali (Dijkstra / TSP) e interrogare dati SAR Copernicus Sentinel-1 per l'umidità del suolo — il tutto su una mappa interattiva Leaflet.

---

## 📋 Prerequisiti

| Software | Versione minima | Note |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **8.0** | `dotnet --version` per verificare |
| [Docker Desktop](https://www.docker.com/products/docker-desktop) | qualsiasi recente | necessario per il database PostGIS locale |
| [Git](https://git-scm.com/) | qualsiasi | per clonare il repository |

> [!NOTE]
> Non è necessario installare PostgreSQL manualmente: il database viene eseguito interamente dentro un container Docker.

---

## 🚀 Guida al Primo Avvio

### 1. Clona il repository e posizionati sul branch

```bash
git clone <url-del-repository>
cd gis-landslide-detection
git checkout gis.exam.unicam.it
```

### 2. Avvia il database PostGIS + pgRouting con Docker

Dalla **root del repository** (dove si trova `docker-compose.yml`):

```bash
docker compose up -d
```

Questo comando:
- Scarica l'immagine `pgrouting/pgrouting:15-3.5-3.8` (PostgreSQL 15 + PostGIS + pgRouting)
- Crea il container `gis_local_db` in ascolto sulla **porta 5433**
- Esegue automaticamente gli script in `db-init/`:
  - **`init.sql.gz`** — crea le tabelle e popola i dati GIS di base (sentieri, punti, zone di rischio)
  - **`z_osm_trails.sql.gz`** — importa i sentieri OpenStreetMap dell'area di studio

> [!IMPORTANT]
> Al primo avvio Docker deve scaricare l'immagine (~500 MB) e inizializzare il database.
> Attendi che il container sia **healthy** prima di procedere:
> ```bash
> docker compose logs -f gis_db
> ```
> Quando vedi `database system is ready to accept connections`, puoi procedere.

### 3. Ripristina i pacchetti NuGet e avvia l'applicazione

```bash
cd it.gis-landslide-detection.web
dotnet restore
dotnet run --project it.gis-landslide-detection.web
```

Oppure, se usi **Visual Studio**:
1. Apri `it.gis-landslide-detection.web.sln`
2. Imposta `it.gis-landslide-detection.web` come progetto di avvio
3. Premi **F5** (o **Ctrl+F5** senza debugger)

### 4. Apri il browser

L'applicazione si avvia su:

| Profilo | URL |
|---|---|
| HTTP | [http://localhost:5140](http://localhost:5140) |
| HTTPS | [https://localhost:7224](https://localhost:7224) |
| Swagger API | [http://localhost:5140/swagger](http://localhost:5140/swagger) |

---

## ⚙️ Cosa succede all'avvio dell'applicazione

All'avvio, `Program.cs` esegue automaticamente queste operazioni (tutte **idempotenti**, sicure da ripetere):

1. **`EnsureCreated()`** — Crea le tabelle EF Core se non esistono (PostGIS abilitato)
2. **`GisDataSeeder.SeedAsync()`** — Popola punti (baite, ristori), linee (sentieri, torrenti) e poligoni (zone frana con indice IFFI 0–100) se le tabelle sono vuote
3. **Riallineamento sequenze IDENTITY** — Ricalcola `max(id)+1` per `gis_lines`, `gis_points`, `gis_polygons` (evita errori `duplicate key` dopo import SQL con id espliciti)
4. **Reimport sentieri OSM** — Se assenti, decomprime e importa `z_osm_trails.sql.gz`
5. **Setup topologia di routing** — Esegue `setup_dynamic_routing.sql`: crea la rete `routing_edges` con noding (ST_Node) delle geometrie e bridge per componenti isolate, abilitando il routing Dijkstra su pgRouting

> [!TIP]
> Controlla i log nella console: l'applicazione stampa lo stato di ogni passo (`Database: connessione OK`, `OSM trails: presenti`, `Routing: setup topologia eseguito`…).

---

## 🗂️ Struttura del Progetto

```
gis-landslide-detection/
├── docker-compose.yml              # Container PostGIS + pgRouting
├── db-init/
│   ├── init.sql.gz                 # Schema e dati GIS iniziali
│   └── z_osm_trails.sql.gz         # Sentieri OSM area Camerino
│
└── it.gis-landslide-detection.web/
    ├── it.gis-landslide-detection.web.sln
    └── it.gis-landslide-detection.web/
        ├── Program.cs              # Entry point, DI, bootstrap DB
        ├── appsettings.json        # Config produzione (Supabase)
        ├── appsettings.Development.json  # Config locale (Docker)
        ├── setup_dynamic_routing.sql     # Topologia pgRouting
        │
        ├── Controllers/
        │   ├── GisDataController.cs      # CRUD + Spatial queries
        │   ├── TrailsController.cs       # Sentieri + hazard score
        │   ├── LandslideController.cs    # Analisi frana + SAR
        │   ├── HikingPointsController.cs # Punti escursionistici
        │   └── IffiZonesController.cs    # Zone rischio IFFI
        │
        ├── Services/
        │   ├── RoutingService.cs         # Dijkstra su pgRouting
        │   ├── TspService.cs             # Travelling Salesman Problem
        │   ├── SentinelService.cs        # API Copernicus Sentinel-1
        │   ├── WeatherService.cs         # Open-Meteo API
        │   ├── HazardScoreEngine.cs      # Calcolo score pericolosità
        │   └── IffiService.cs            # Integrazione dati IFFI
        │
        ├── Data/
        │   ├── ApplicationDbContext.cs   # EF Core + NetTopologySuite
        │   └── GisDataSeeder.cs          # Seed dati di esempio
        │
        ├── Models/                # Entità GIS (Point, Line, Polygon, Trail…)
        ├── DTOs/                  # Data Transfer Objects
        ├── Repositories/          # Data access layer
        ├── Helpers/               # GeoJSON formatter
        │
        ├── Views/Home/
        │   └── Index.cshtml       # UI mappa GeoSentinel Explorer
        │
        └── wwwroot/
            ├── css/gis-app.css    # Stile dark UI
            ├── js/gis/            # Moduli JS (map, api, state, ui)
            └── data/              # JSON precomputed (soil moisture)
```

---

## 🔑 Configurazione

### Ambiente di Sviluppo (locale con Docker)

La configurazione in `appsettings.Development.json` punta al container Docker locale:

```json
{
  "ConnectionStrings": {
    "ApplicationDbContext": "Host=localhost;Port=5433;Database=gis_local_db;Username=postgres;Password=your_secret_password;Include Error Detail=true;"
  }
}
```

> [!WARNING]
> **Non modificare la porta 5433** a meno che tu non la cambi anche nel `docker-compose.yml`.
> La porta host è 5433 (non la 5432 standard) per evitare conflitti con un'eventuale installazione PostgreSQL locale.

### API esterne (opzionali, già configurate)

| Servizio | Scopo | Config in |
|---|---|---|
| **Copernicus Sentinel-1** | Dati SAR umidità del suolo | `appsettings.json` → `CopernicusApi` |
| **Open-Meteo** | Dati meteo (precipitazioni, temperatura) | Hardcoded in `Program.cs` |

---

## 🧪 Test delle API

Il file `test-api.http` (compatibile con l'estensione REST Client di VS Code o l'HTTP Client di Visual Studio) contiene richieste pronte:

```http
### Recupera tutti i Punti (GeoJSON)
GET http://localhost:5140/api/GisData/points

### Routing Dijkstra
GET http://localhost:5140/api/GisData/route?startLat=43.1&startLng=13.4&endLat=43.2&endLng=13.5

### Analisi rischio frana
GET http://localhost:5140/api/Landslide?lat=43.0805&lng=13.2384
```

Oppure usa Swagger UI: [http://localhost:5140/swagger](http://localhost:5140/swagger)

---

## 🛑 Troubleshooting

### Il database non si connette

```
Database: impossibile connettersi al database.
```

**Soluzioni:**
1. Verifica che Docker sia in esecuzione: `docker ps`
2. Controlla che il container `gis_local_db` esista e sia avviato: `docker compose ps`
3. Se il container non esiste, riesegui `docker compose up -d` dalla root del repo
4. Se hai cambiato la password, aggiornala sia in `docker-compose.yml` che in `appsettings.Development.json`

### Errore `duplicate key value violates unique constraint`

Questo errore è stato risolto: l'applicazione riallinea automaticamente le sequenze IDENTITY ad ogni avvio. Se persiste, riavvia l'app (`dotnet run`).

### I sentieri OSM non compaiono sulla mappa

Controlla il log all'avvio. Se vedi:
```
OSM trails: z_osm_trails.sql.gz non trovato in <path>, skip.
```
Assicurati che la cartella `db-init/` sia presente alla root del repository con il file `z_osm_trails.sql.gz`.

### Reset completo del database

Per ripartire da zero con un database pulito:

```bash
docker compose down -v     # Elimina container E volume dati
docker compose up -d       # Ricrea tutto da zero
dotnet run --project it.gis-landslide-detection.web/it.gis-landslide-detection.web
```

---

## 🧩 Funzionalità Principali

- **🗺️ Mappa interattiva** — Leaflet con layer punti, linee e poligoni attivabili
- **✏️ CRUD GIS** — Crea, modifica, elimina elementi geometrici direttamente sulla mappa
- **🔍 Query spaziali** — Nearest, Within, Intersection con geometrie PostGIS
- **🧭 Routing Dijkstra** — Percorso più breve tra due punti lungo i sentieri reali (pgRouting)
- **🔁 TSP Tour** — Calcolo del giro ottimale tra N punti di interesse
- **⚠️ Zone di rischio IFFI** — Classificazione frane con scala cromatica
- **🛰️ Sentinel-1 SAR** — Analisi umidità del suolo da immagini radar Copernicus
- **🌦️ Meteo in tempo reale** — Dati precipitazioni e temperatura da Open-Meteo
- **📊 Hazard Score** — Indice di pericolosità composito per ogni sentiero

---

## 📄 Licenza

Progetto accademico — Università di Camerino (UNICAM).
