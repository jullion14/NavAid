# Key decisions and gotchas

> Part of the Landmark-Guided Walking Navigation reference docs. See [`README.md`](README.md) for the index.

Two things live here. **Decisions** are positions taken deliberately, with the
reasoning, so they are not relitigated every month. **Gotchas** are things that
cost time once and should not cost it twice.

---

## Decisions

### D1. No background location tracking, and therefore no mobile app

This is the decision the whole scope hangs on. Continuous location in the
background is the one capability a browser cannot provide, and the moment it is
required the project becomes a native iOS/Android build — app store accounts,
platform permission flows, two codebases, and a safety claim that cannot be
validated in eight months by one student.

Cutting it keeps everything else browser-based: `watchPosition` works fine while
the page is in the foreground, which is exactly the situation the application is
designed for (the walker is holding the phone and following instructions).

Consequence: the application guides a journey the user starts. It does not watch
over them when they are not using it. This is stated plainly in the proposal
rather than hedged.

### D2. No "wandering detection" or outlier alerting

Considered and rejected on method grounds, not just scope. A radius or
distance-from-home model is miscalibrated against the actual failure mode:
disorientation in HDB estates characteristically happens *close* to home,
because void decks and block faces look alike. A radius model would miss the
common case and fire on harmless travel. Base rates then guarantee alarm
fatigue.

Deviation alerting on an **active route** is kept (D3) because it has a
well-defined trigger — the walker left the polyline the system itself computed —
and makes no claim about intent.

### D3. Deviation alerting is scoped to an active route only

Trigger: current position more than a threshold distance from the computed route
polyline, sustained across several fixes. Response: alert on the walker's screen,
recompute from current position, and notify the caregiver view if it is open.
No inference about whether the walker is "lost".

### D4. No public transport or vehicle routing

Walking only. Multi-modal routing would mean fare data, live arrivals, transfer
logic, and the much harder instruction problem of boarding and alighting the
right service — a project in itself. Journeys longer than a comfortable walk are
handled by D5 instead.

### D5. Go-To Point fallback instead of long walking routes

If the computed route exceeds a walkable threshold, the application does not
produce a 40-minute walking instruction set. It routes to the nearest Dementia
Go-To Point and says so: a staffed place where someone can help. This turns the
out-of-range case from a failure into the intended behaviour, and it is the
reason the Go-To Point dataset is load-bearing rather than decorative.

### D6. Gemini stays, and every AI stage is paired with a validator

Gemini is used for two things only: parsing a speech transcript into a
structured request, and phrasing supplied facts as a spoken instruction. It
performs no calculation, holds no database access, and never chooses a route.

Each stage is followed by a deterministic check:

| Stage | AI | Validator |
|---|---|---|
| Speech in | Intent Parser | Intent Validator — destination must resolve against known saved destinations or landmarks, else the request is refused and the user is asked to repeat |
| Speech out | Instruction Generator | Instruction Verifier — every landmark named in the generated sentence must appear in the route's fact list, else fall back to a deterministic template |

A local model was considered. Gemini was kept because the cloud model gives
better phrasing quality per hour of work, and the architecture is what makes the
choice defensible: because the model only ever sees a closed fact list and its
output is verified, **the model is swappable**. Replacing Gemini with a local
model is a configuration change, not a redesign. That point is worth stating in
the report explicitly — it is the answer to "what if the API is unavailable or
the data cannot leave the device".

### D7. Foreground, single-session use — no credentialed accounts

The proposal excludes account management systems. Saved destinations and
recorded routes are persisted server-side against a device or household
identifier, not behind a username and password.

### D8. Routing resolves to buildings, not to Go-To Point records

See G1. A consequence worth flagging as a decision: "route me to the nearest
Go-To Point" must return one route per building, not one per registered
participating outlet.

### D9. GP clinics and polyclinics are landmarks, not destinations

They are high-salience, publicly recognisable buildings and are therefore useful
to name in instructions ("past the polyclinic"). They are not offered as
destinations — doing so edges toward a clinical or care-navigation claim the
project does not make.

### D10. Three mobility profiles, not a continuous model

`euclidean` (baseline, for comparison against distance-optimal routing),
`default` (1.10 m/s), `limited` (0.80 m/s). A continuous speed parameter would
imply measurement precision that does not exist — see
[`08-limitations.md`](08-limitations.md).

---

## Open decisions

### O1. How caregiver-entered landmarks and destinations reach the walker's device

The caregiver enters landmarks, saves destinations and records familiar routes.
The walker's device must then read them. With no credentialed accounts (D7), the
link between the two devices is unresolved.

Proposed, **not yet adopted**: a short pairing code generated in the caregiver
view and entered once on the walker's device, which issues a pseudonymous
household identifier stored locally. No name, no password, no personal data.

Not adopted because the proposal as submitted excludes account management, and a
pairing flow sits on the boundary of that exclusion. Resolve before building the
destinations and landmark-write endpoints — the schema already carries the
columns either way.

---

## Gotchas

### Data and ETL

**G1. 831 Go-To Point records collapse to 717 buildings.** Large malls register
many tenant outlets individually and every one carries the mall's coordinates,
so the raw table has stacks of points at an identical location (110 records are
named "NTUC FairPrice"). Deduplicate by postal code / building before using the
set for routing, or "nearest Go-To Point" returns twenty identical routes to one
mall.

**G2. Opening hours are free text, not structured.** Strings like "Mon-Fri
9am-6pm, Sat 9am-1pm" and "24 hours" sit in one column. Do not attempt
open-now filtering; display the string and say in the report that temporal
filtering was out of scope.

**G3. Some rows are station-level, not shop-level.** Transit entries point at a
station rather than a specific unit, so their coordinate may be tens of metres
from where a person would actually find help.

**G4. The file contains U+FFFD replacement characters** — the source was
re-encoded somewhere upstream. Sanitise names on import rather than at display
time.

**G5. Roughly four-fifths of records were last updated September 2024**, with a
minority much older. Treat the set as a 2024 snapshot.

**G6. Median spacing between points is about 210 m, but coverage is retail-
skewed** — dense along shop frontages, thin inside residential blocks, which is
precisely where disorientation happens. This is a limitation, not a bug, but it
shapes what the fallback in D5 can promise.

**G7. `ST_Distance` on SRID 4326 returns degrees.** Cast to `::geography`.
Values like 0.0043 instead of 478 are the tell. (Carried over from the previous
project and still true.)

**G8. `ST_PointOnSurface`, not `ST_Centroid`**, for a representative point
inside a polygon — a centroid can fall outside a concave shape.

**G9. The OSM import is slow and belongs in its own script.** `import_osm.py`
runs `osm2pgrouting` and takes long enough that bundling it into the general
import makes iteration painful. Keep them separate, and keep the extracted
`.pbf` clipped to the study area before importing.

**G10. `import_data.py` uses CASCADE** and will silently drop views,
materialised views and functions built on these tables. Nothing currently
depends on them; if a view is added, its definition has to live in the script.

### Backend

**G11. Gemini rejects `charset=utf-8` on the request body.** `StringContent`
adds it automatically and `generateContent` returns an error. Overwrite
`Content-Type` with a plain `MediaTypeHeaderValue("application/json")`.

**G12. Set the HTTP timeout to at least 60 seconds** for thinking models. The
default client timeout fires first and looks like a network fault.

**G13. `thinkingBudget = 0` is rejected by 3.x models.** Omit the field rather
than setting it to zero.

**G14. `responseSchema` rejects `propertyOrdering`.** Valid in some schema
dialects, not in this one.

**G15. `ToLowerInvariant` mangles acronyms.** "MRT" becomes "mrt" and then gets
read aloud as a word. Use a `Lower()` helper that restores a known acronym list
before the string reaches speech synthesis.

**G16. A 405 on a route that clearly exists is usually HTTPS redirection
middleware**, not the route table. "Failed to determine the https port for
redirect" in the startup log is the tell. Wrap `UseHttpsRedirection` in
`if (!app.Environment.IsDevelopment())`.

**G17. The .NET webapi template uses minimal APIs by default.** A manually
created `Controllers/` folder requires both `AddControllers()` and
`MapControllers()` in `Program.cs`, or every route 404s.

**G18. Registering a CORS policy is not enough** — `app.UseCors("FrontendDev")`
must actually be called.

**G19. `NetTopologySuite.IO.GeoJSON4STJ` + `GeoJsonConverterFactory`** must be
registered in `AddJsonOptions`, or NTS geometries will not serialise to GeoJSON.

**G20. Lateral subquery aliases are not visible to the outer SELECT.** Computing
`ST_Y(h.geom)` in the outer list fails with "missing FROM-clause entry for table
h" — the value must be produced inside the lateral and read back through its
alias.

**G21. `FromSqlRaw` column lists are unforgiving.** A missing comma before a new
block and a trailing comma before `FROM` both parse as syntax errors, and the
only symptom is a 500. Paste the generated SQL into `psql` before assuming the
C# is at fault.

### Frontend

**G22. React props declared in the interface but not destructured in the
function signature are silently `undefined` inside JSX** — not a compile error.
This caused two separate "renders fine but does nothing" bugs. Check the
signature first when a feature does nothing at all.

**G23. Watch for render blocks nested inside conditional guards.** A section
written inside an early-return branch never renders in the normal path, and the
component still compiles.

**G24. Stale closures in callbacks** — a handler registered once captures the
first render's state. Hold the live value in a ref and read it inside the
handler. This bites hardest on `watchPosition`, which registers once and fires
for the whole journey.

**G25. Do not construct `L.svg()` inside the component body.** A new renderer
instance every render detaches the previous layer group. Build it once outside
the component, or memoise it.

**G26. `preferCanvas` is incompatible with pane-ordering for click precedence.**
The canvas renderer lets vector layers intercept clicks before the map-level
handler sees them. Pick one: canvas for marker volume, or SVG panes for reliable
click ordering. The navigation view needs very few markers, so SVG is the
default here.

**G27. React `<Popup>` escapes HTML strings**, unlike Leaflet's `bindPopup`
which parses them. Pass JSX, not `"<b>name</b>"`.

**G28. Leaflet caches container size.** A panel that resizes the map needs
`invalidateSize()` — handled by a `ResizeObserver`.

**G29. `CircleMarker` over default Leaflet markers** — the default pin icons
break under Vite because of asset path resolution.

**G30. Vite's react-ts template pins `#root` to a fixed width.** Override it for
a full-bleed map layout.

### Field testing

**G31. Web Speech and Geolocation require a secure context.** `http://` on a
phone's LAN address will not prompt for microphone or location. Use a tunnel
(cloudflared, VS Code Dev Tunnels, ngrok) so the phone loads the app over
HTTPS — see [`01-setup.md`](01-setup.md).

**G32. Proxy the API through Vite rather than fighting CORS** once the frontend
is on a tunnel domain. Also set `server.allowedHosts`, or Vite refuses the
tunnel hostname.

**G33. Request a screen wake lock during navigation.** The screen sleeping
mid-journey is a usability failure for exactly the user the project is for.
