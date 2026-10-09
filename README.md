# Landmark-Guided Walking Navigation

A web-based walking navigation aid for older adults at risk of disorientation in
Singapore's public housing estates. Routes are optimised for ease of following
rather than length — minimising decision points, preferring paths the user
already knows, and describing the way by physical landmarks rather than street
names. An LLM handles speech in and speech out; it performs none of the routing
and every landmark it names is checked against the computed route before it is
spoken.

CEG3001 Capstone Project — Singapore Institute of Technology.

## Status

| Module | State |
|---|---|
| Pedestrian network import (OSM → osm2pgrouting) | Not started |
| Go-To Point import and deduplication | Not started |
| Baseline shortest-path routing | Not started |
| Landmark store and caregiver entry | Not started |
| Decision-point and landmark cost function | Not started |
| Intent parsing and validation | Not started |
| Instruction generation and verification | Not started |
| Navigation and caregiver views | Not started |
| Field verification with GPS traces | Not started |

Carried over from the previous direction and still usable: PostGIS database and
ETL scaffolding, Leaflet map shell, backend project structure, Gemini API
client.

## Stack

- **Frontend** React + TypeScript (Vite), Leaflet via react-leaflet
- **Backend** ASP.NET Core Web API (.NET 10), controllers-based, `GeoDSS.Api`
- **Database** PostgreSQL 18 + PostGIS 3.6 + **pgRouting**, EF Core + NetTopologySuite
- **Routing data** OpenStreetMap Singapore extract via `osm2pgrouting`
- **ETL** Python + psycopg2
- **Desktop GIS** QGIS (visual verification of imported geometry)
- **AI** Google Gemini API — language only, never computation
- **Speech** Web Speech API (browser-native transcription and synthesis)
- **Validation baseline** OneMap Routing API (SLA)

## Quick start

```bash
createdb navaid_db
psql navaid_db -c "CREATE EXTENSION postgis; CREATE EXTENSION pgrouting;"

pip install psycopg2-binary requests
python import_osm.py              # OSM extract -> routable network
python import_data.py             # Go-To Points, boundaries, landmark sources

cd backend  && dotnet run                      # http://localhost:5170
cd frontend && npm install && npm run dev
```

Full instructions: [`docs/01-setup.md`](docs/01-setup.md).

## Documentation

| Doc | What's in it |
|---|---|
| [`01-setup.md`](docs/01-setup.md) | Fresh clone to running app |
| [`02-data-sources.md`](docs/02-data-sources.md) | Datasets, source quirks, what was rejected and why |
| [`03-database-schema.md`](docs/03-database-schema.md) | Tables, columns, indexes |
| [`04-architecture.md`](docs/04-architecture.md) | Repository layout, what each file does |
| [`05-methodology.md`](docs/05-methodology.md) | Cost function, landmark model, evaluation — the report-facing document |
| [`06-decisions-and-gotchas.md`](docs/06-decisions-and-gotchas.md) | Decisions taken, and things that cost time once |
| [`07-roadmap.md`](docs/07-roadmap.md) | Milestones and what's outstanding |
| [`08-limitations.md`](docs/08-limitations.md) | Known weaknesses, recorded as encountered |

## Design principle

All spatial computation is deterministic and reproducible. The LLM appears at
two points only — converting a spoken request into a structured query, and
phrasing a supplied list of facts as spoken directions — and each is followed
immediately by a deterministic check. An intent that names an unknown
destination is rejected rather than guessed at; an instruction naming a landmark
absent from the computed route is discarded in favour of a template.

The model is therefore swappable without touching the routing engine or
weakening any guarantee the system makes.

## Scope

Walking routes only, used in the foreground while the device is held. The system
routes anywhere the OpenStreetMap pedestrian network covers; the landmark survey
and field evaluation are confined to one estate. Where a destination lies beyond
a configurable walking threshold, the system routes to the nearest Dementia
Go-To Point instead of attempting a journey the user cannot complete.

Out of scope: vehicle and public transport routing, background location
tracking, credentialed account management, and any clinical or diagnostic claim.
