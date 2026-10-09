# Status and roadmap

> Part of the Landmark-Guided Walking Navigation reference docs. See [`README.md`](README.md) for the index.

The scope changed in October 2026. Nothing from the previous project's feature
roadmap carries forward — the database, backend scaffolding, Leaflet map shell
and Gemini client wiring do, but every milestone below is new work.

**Current status: nothing started.** See the status table in
[`README.md`](README.md).

---

## Month by month

### October 2026 — routing foundation *(critical month)*

- Extract and clip the OpenStreetMap pedestrian network to the study area
- `import_osm.py`: `osm2pgrouting` into `ways` / `ways_vertices_pgr`
- Compute `branch_count` per vertex; populate `way_profile_cost` for all three
  profiles
- Import Dementia Go-To Points, deduplicating 831 records to 717 buildings
- Baseline `pgr_dijkstra` routing on plain length, end to end through the API
- Validate against OneMap walking directions on a sample of routes (T1)

This is the month that decides whether the project is buildable. Everything
after it assumes a routable network exists. **Fallback if the OSM import proves
unworkable:** use the OneMap routing API for the path geometry and apply the
landmark and familiarity layers on top of it. This loses control of the cost
function — which is the research contribution — so it is a genuine fallback, not
an equivalent option. Decide by the end of October either way.

### November 2026 — landmarks and the cost function

- `landmarks` table populated from Go-To Points, MRT exits, bus stops, clinics
- Nearest-vertex snapping for every landmark
- Caregiver landmark entry (map click + name + salience)
- Decision-point cost term, landmark discount term
- Side-by-side comparison of a landmark-guided route against the distance-
  optimal route for the same origin–destination pair

### December 2026 — speech and the AI layer

- Web Speech recognition in the navigation view
- Intent Parser (Gemini, structured output) + Intent Validator
- Instruction Builder: route segmentation, landmark per segment, ordered fact
  list
- Instruction Generator (Gemini) + Instruction Verifier + template fallback
- Speech synthesis out, with the acronym handling from G15

### January 2027 — familiarity, deviation, and the navigation UI

- Route recording in the caregiver view; `recorded_routes` populated
- Familiarity discount term in the cost function
- Deviation detection against the active route, with recompute
- Navigation view proper: next instruction, next landmark, directional
  indicator, minimal chrome
- Field testing on a phone over a tunnel

### February 2027 — buffer

Deliberately unallocated. Every month above has a dependency on the one before
it, so one month of slack is the only thing standing between an October slip and
a March crisis. If October and November go cleanly, this month absorbs the
evaluation work early instead.

### March 2027 — testing, evaluation, report

- T2: 20-route comparison (decision points, landmarks per instruction, length
  penalty vs. distance-optimal)
- T3: GPS field verification — walk the routes, record traces, check the
  instructions match what is actually visible
- T4: AI validation — intent parsing accuracy on a prepared transcript set,
  instruction verifier rejection rate
- Final Technical Report

### April 2027 — final refinements and presentation

---

## Deliverables

- Browser-based walking navigation application (React/TypeScript frontend,
  ASP.NET Core backend)
- PostgreSQL / PostGIS / pgRouting spatial database with the routable pedestrian
  network and landmark model
- Routing engine implementing the three-term cost function over three mobility
  profiles
- Speech input and output with deterministic validation at both ends
- Caregiver interface: landmark entry, saved destinations, route recording,
  deviation alert
- Python ETL pipeline for the network and landmark imports
- Evaluation results for T1–T4
- Final Technical Report (requirements, design, implementation, testing,
  evaluation)
- Setup and user guide (the docs in this project, consolidated)

---

## Carried over from the previous project

Reusable without redesign: PostGIS database and connection setup, EF Core +
NetTopologySuite configuration, the controller/service layout, the Gemini HTTP
client and its hard-won header and timeout handling, the React + Vite + Leaflet
shell, and the Python ETL scaffolding.

Not reusable: the scoring and sensitivity engine, the population dataset and all
per-capita metrics, the weights and ranking UI, and the bus construct-validity
work. Removed from the docs rather than archived — see
[`02-data-sources.md`](02-data-sources.md).
