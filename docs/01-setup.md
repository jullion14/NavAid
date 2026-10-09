# Setup (fresh clone / tester)

> Part of the reference docs. See [`README.md`](../README.md) for the index.

```
  CREATE DATABASE navaid_db;
  \c navaid_db
  CREATE EXTENSION postgis;
  CREATE EXTENSION pgrouting;

  pip install psycopg2-binary requests

  # 1. Routable pedestrian network (slow — minutes, not seconds)
  python import_osm.py --extract data/singapore.osm.pbf

  # 2. Everything else
  python import_data.py --dry-run     # validate files, no DB changes
  python import_data.py               # schema + Go-To Points, landmarks,
                                      # boundaries, profile costs
```

## Two-stage import, on purpose

`import_osm.py` runs `osm2pgrouting` over the Singapore extract and then builds
the derived columns the cost function needs — `covered`, `is_steps`,
`crossing_kind`, vertex `branch_count`. It is slow and it is idempotent, so it
is kept separate from the rest and should not be re-run casually.

`import_data.py` drops and recreates everything except the network tables. All
data derives from source files, so re-running gives identical counts.

Bus stops remain **optional**: an absent `bus_stops.geojson` prints a note and
everything else still imports, so a tester without an LTA key gets a working
database with a slightly thinner landmark set.

`fetch_bus_data.py` regenerates `bus_stops.geojson`; needs `LTA_ACCOUNT_KEY`
(env var or `--key`). Committed for provenance — it documents which endpoints
the GeoJSON came from.

## Running the app

```
cd backend  && dotnet run          # http://localhost:5170
cd frontend && npm install && npm run dev
```

`frontend/.env` needs `VITE_API_URL=http://localhost:5170` (copy
`.env.example`). Backend secrets go in `dotnet user-secrets` — Gemini and
OneMap keys. `LTA_ACCOUNT_KEY` is an env var.

## Testing on a phone

Geolocation and the Web Speech API both require a **secure context**. `localhost`
is exempt; a LAN IP over plain HTTP is not, so `http://192.168.x.x:5173` will
silently fail to return a position or a transcript.

Use a tunnel instead:

```
cloudflared tunnel --url http://localhost:5173
# or VS Code Dev Tunnels, or ngrok
```

Configure Vite's dev proxy so `/api` forwards to `localhost:5170` and call
relative paths from the frontend. Tunnelling one port avoids a second URL and
removes CORS from the picture entirely. Vite also rejects unfamiliar Host
headers, so add the tunnel domain to `server.allowedHosts`.

Before a field session: confirm on the device that the microphone prompt
appears, that a position fix returns, and that one full route completes. Request
a Screen Wake Lock when a route starts, or the screen sleeps and `watchPosition`
stops firing mid-walk.
