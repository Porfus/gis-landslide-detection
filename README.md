# gis-landslide-detection
GeoHike — GIS Landslide Detection

Real-Time Landslide Hazard Monitoring for Mountain Safety
GeoHike is a GIS-driven decision-support system that translates complex Earth Observation (EO) satellite data and meteorological forecasts into a real-time Danger Index for mountain hiking trails. Instead of raw, technical GIS output, hikers get a single color-coded map they can actually act on — before and during their journey.

**The Problem**

Mountain trails are regularly threatened by landslides triggered by soil saturation, heavy rainfall, and unstable geological zones. The data to predict these events already exists — satellite readings, weather forecasts, historical records — but it requires expert interpretation that the average hiker simply doesn't have.
GeoHike solves this by aggregating that data, running it through a risk engine, and surfacing the result as an intuitive visual safety guide.

**How It Works**

The system correlates three primary data sources to produce a composite Danger Index for each trail segment:
Meteorological Data — Real-time rainfall intensity and 24-hour forecasts
Earth Observation (EO) Data — Soil moisture levels and terrain saturation from satellite imagery
IFFI Records — Historical Italian Landslide Inventory data identifying high-risk geological zones

**Danger Index**

Each trail is assigned one of three states, updated in real time:

State  |  Indicator  |  Meaning

Safe   |  Green      | Standard conditions. Trail is clear.

Caution|  Orange     | Elevated risk due to recent rainfall or soil saturation.

Danger |  Red        |High-risk zone. Animated markers indicate immediate hazard.

Clicking any trail segment opens a popup with granular details: rainfall intensity, soil moisture readings, and the historical IFFI risk classification for that zone.

**Key Features**

Interactive Safety Map — Built with Leaflet.js for high-performance GeoJSON rendering of hiking paths
Dynamic Danger Index — Trail colors update in real time as conditions change
Hex-Pulse Alert System — CSS-animated hexagonal markers highlight exact landslide-risk coordinates
Granular Analytics Popups — Per-trail breakdowns of all contributing risk factors, accessible with a single tap
Mobile-First UI — Designed for field use where connectivity and screen space are limited

Contributors
Built for the Cassini Hackathon, a challenge focused on applying Earth Observation data to real-world problems.

    Name        |     GitHub       |         Role
    Luca Porfiri    |  @Porfus         |  Lead Developer / Project Architect
    Tommaso Raganini|  @TommasoRaganini|  Core Contributor/ Backend Developer
    Ardit Ceno      |  @ArditCeno      |  Frontend Developer & UI/UX Specialist

Link https: https://arditceno.github.io/gis-landslide-detection/
