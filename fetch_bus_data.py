"""
NavAid — LTA DataMall bus data fetcher
======================================

Fetches bus stop locations (and, optionally, bus route information) from the
LTA DataMall API and writes a single GeoJSON file for import by
import_data.py.

Run this once during setup. Bus stop locations change rarely, so the output
is committed alongside the other source datasets and the application never
calls DataMall at runtime — keeping the analysis deterministic and
reproducible.

SETUP
-----
1. Register for a free API key at https://datamall.lta.gov.sg/content/datamall/en/request-for-api.html

2. Install dependencies:

       pip install requests

3. Provide the key by either:

       export LTA_ACCOUNT_KEY=your_key_here      # Linux / macOS
       set LTA_ACCOUNT_KEY=your_key_here         # Windows cmd
       $env:LTA_ACCOUNT_KEY="your_key_here"      # Windows PowerShell

   or pass it directly:

       python fetch_bus_data.py --key your_key_here

USAGE
-----
    python fetch_bus_data.py                 # stops + route counts
    python fetch_bus_data.py --skip-routes   # stops only (much faster)
    python fetch_bus_data.py --out data/bus_stops.geojson

OUTPUT
------
A GeoJSON FeatureCollection of bus stops. Each feature carries:

    BusStopCode   5-digit identifier
    RoadName      road the stop is on
    Description   nearby landmark, used to identify the stop
    ServiceCount  number of distinct bus services calling at the stop
                  (null when --skip-routes is used)
    Services      sorted list of service numbers (omitted with --skip-routes)

ServiceCount is the analytically useful field. Distance to the nearest bus
stop barely varies across Singapore — the network is planned around roughly
a 400 m walk — so it cannot discriminate between areas. How well served a
stop is varies a great deal, and that is what ServiceCount captures.

NOTES
-----
  - Both endpoints are paginated at 500 records per call. Bus stops need
    around 10 calls; routes need around 55, which is why routes are
    separately skippable.
  - Stops with missing or out-of-range coordinates (decommissioned stops and
    some depot entries) are excluded and reported.
"""

import argparse
import json
import os
import sys
import time
from collections import defaultdict

import requests

# ---------------------------------------------------------------------------
# CONFIGURATION
# ---------------------------------------------------------------------------

BASE_URL = "https://datamall2.mytransport.sg/ltaodataservice"
BUS_STOPS_ENDPOINT = f"{BASE_URL}/BusStops"
BUS_ROUTES_ENDPOINT = f"{BASE_URL}/BusRoutes"

PAGE_SIZE = 500          # fixed by the API
REQUEST_DELAY = 0.2      # seconds between calls, to stay well under rate limits
MAX_RETRIES = 3
TIMEOUT = 30

DEFAULT_OUTPUT = "data/bus_stops.geojson"

# Rough bounding box for Singapore, used to discard placeholder coordinates.
SG_BOUNDS = {"min_lat": 1.15, "max_lat": 1.50,
             "min_lon": 103.55, "max_lon": 104.15}


# ---------------------------------------------------------------------------
# API ACCESS
# ---------------------------------------------------------------------------

def fetch_all(endpoint, account_key, label):
    """
    Page through a DataMall endpoint until it returns fewer than PAGE_SIZE
    records, collecting every result.
    """
    session = requests.Session()
    session.headers.update({
        "AccountKey": account_key,
        "accept": "application/json",
    })

    records, skip, page = [], 0, 0

    while True:
        page += 1
        params = {"$skip": skip} if skip else {}

        for attempt in range(1, MAX_RETRIES + 1):
            try:
                response = session.get(endpoint, params=params, timeout=TIMEOUT)

                if response.status_code == 401:
                    raise SystemExit(
                        "\nERROR: DataMall rejected the API key (401).\n"
                        "Check LTA_ACCOUNT_KEY, or pass --key explicitly."
                    )
                if response.status_code == 429:
                    wait = 5 * attempt
                    print(f"    rate limited, waiting {wait}s...")
                    time.sleep(wait)
                    continue

                response.raise_for_status()
                break

            except requests.exceptions.RequestException as exc:
                if attempt == MAX_RETRIES:
                    raise SystemExit(
                        f"\nERROR: {label} request failed after "
                        f"{MAX_RETRIES} attempts: {exc}"
                    )
                wait = 2 * attempt
                print(f"    request failed ({exc}), retrying in {wait}s...")
                time.sleep(wait)
        else:
            raise SystemExit(f"\nERROR: {label} request could not be completed.")

        batch = response.json().get("value", [])
        records.extend(batch)
        print(f"    page {page:>3}: {len(batch):>3} records "
              f"({len(records):,} total)", end="\r")

        if len(batch) < PAGE_SIZE:
            break

        skip += PAGE_SIZE
        time.sleep(REQUEST_DELAY)

    print(f"    {label}: {len(records):,} records fetched" + " " * 20)
    return records


# ---------------------------------------------------------------------------
# TRANSFORMATION
# ---------------------------------------------------------------------------

def valid_coordinates(lat, lon):
    """Reject placeholder (0,0) and anything outside Singapore."""
    try:
        lat, lon = float(lat), float(lon)
    except (TypeError, ValueError):
        return False
    return (SG_BOUNDS["min_lat"] <= lat <= SG_BOUNDS["max_lat"]
            and SG_BOUNDS["min_lon"] <= lon <= SG_BOUNDS["max_lon"])


def count_services_per_stop(routes):
    """
    Reduce the route table to distinct services per stop.

    BusRoutes lists one row per service-direction-stop combination, so a
    service appears more than once per stop when it runs in both directions.
    A set collapses these.
    """
    services = defaultdict(set)
    for row in routes:
        code = row.get("BusStopCode")
        service = row.get("ServiceNo")
        if code and service:
            services[str(code).strip()].add(str(service).strip())
    return services


def build_geojson(stops, services_by_stop=None):
    features, skipped, duplicates = [], [], 0
    seen = set()

    for stop in stops:
        code = str(stop.get("BusStopCode", "")).strip()
        lat, lon = stop.get("Latitude"), stop.get("Longitude")

        if not code:
            skipped.append(("(no code)", "missing BusStopCode"))
            continue

        if code in seen:
            duplicates += 1
            continue

        if not valid_coordinates(lat, lon):
            skipped.append((code, f"invalid coordinates ({lat}, {lon})"))
            continue

        seen.add(code)

        properties = {
            "BusStopCode": code,
            "RoadName": (stop.get("RoadName") or "").strip() or None,
            "Description": (stop.get("Description") or "").strip() or None,
        }

        if services_by_stop is not None:
            found = sorted(services_by_stop.get(code, set()))
            properties["ServiceCount"] = len(found)
            properties["Services"] = found
        else:
            properties["ServiceCount"] = None

        features.append({
            "type": "Feature",
            "geometry": {
                "type": "Point",
                "coordinates": [float(lon), float(lat)],   # GeoJSON: lon, lat
            },
            "properties": properties,
        })

    return {"type": "FeatureCollection", "features": features}, skipped, duplicates


# ---------------------------------------------------------------------------
# REPORTING
# ---------------------------------------------------------------------------

def summarise(geojson, skipped, duplicates, with_routes):
    features = geojson["features"]
    print(f"\n  {len(features):,} bus stops written")

    if duplicates:
        print(f"  {duplicates} duplicate stop codes ignored")

    if skipped:
        print(f"  {len(skipped)} stops excluded:")
        for code, reason in skipped[:5]:
            print(f"    {code}: {reason}")
        if len(skipped) > 5:
            print(f"    ... and {len(skipped) - 5} more")

    if not with_routes:
        return

    counts = [f["properties"]["ServiceCount"] for f in features]
    unserved = sum(1 for c in counts if c == 0)
    served = [c for c in counts if c > 0]

    if served:
        print(f"\n  services per stop: min {min(served)}, "
              f"median {sorted(served)[len(served) // 2]}, max {max(served)}")
    if unserved:
        print(f"  {unserved} stops have no services in the route data "
              f"(likely not currently in operation)")

    busiest = sorted(features,
                     key=lambda f: f["properties"]["ServiceCount"],
                     reverse=True)[:5]
    print("\n  busiest stops:")
    for f in busiest:
        p = f["properties"]
        label = p["Description"] or p["RoadName"] or p["BusStopCode"]
        print(f"    {p['ServiceCount']:>3} services  {label}")


# ---------------------------------------------------------------------------
# MAIN
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description="Fetch bus stop data from LTA DataMall and write GeoJSON."
    )
    parser.add_argument("--key", default=os.getenv("LTA_ACCOUNT_KEY"),
                        help="DataMall AccountKey (or set LTA_ACCOUNT_KEY)")
    parser.add_argument("--out", default=DEFAULT_OUTPUT,
                        help=f"output path (default: {DEFAULT_OUTPUT})")
    parser.add_argument("--skip-routes", action="store_true",
                        help="skip route data; faster, but no ServiceCount")
    args = parser.parse_args()

    if not args.key:
        raise SystemExit(
            "ERROR: no API key.\n"
            "  Set LTA_ACCOUNT_KEY or pass --key.\n"
            "  Register free at "
            "https://datamall.lta.gov.sg/content/datamall/en/request-for-api.html"
        )

    out_dir = os.path.dirname(args.out)
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    print("Fetching bus stops...")
    stops = fetch_all(BUS_STOPS_ENDPOINT, args.key, "bus stops")
    if not stops:
        raise SystemExit("ERROR: no bus stops returned; aborting.")

    services_by_stop = None
    if not args.skip_routes:
        print("\nFetching bus routes (this takes a minute)...")
        routes = fetch_all(BUS_ROUTES_ENDPOINT, args.key, "bus routes")
        services_by_stop = count_services_per_stop(routes)
        print(f"    {len(services_by_stop):,} stops have at least one service")

    geojson, skipped, duplicates = build_geojson(stops, services_by_stop)

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(geojson, f, ensure_ascii=False)

    summarise(geojson, skipped, duplicates, with_routes=not args.skip_routes)

    size_mb = os.path.getsize(args.out) / 1_000_000
    print(f"\nWrote {args.out} ({size_mb:.1f} MB)")
    print("Now run: python import_data.py")


if __name__ == "__main__":
    main()