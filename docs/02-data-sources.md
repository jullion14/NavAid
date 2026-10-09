# Data sources

> Part of the reference docs. See [`README.md`](../README.md) for the index.

All spatial data WGS84 / EPSG:4326 — no reprojection. Go-To Points also carry
SVY21 coordinates, which are kept but not currently used.

```
--- Routing network ----------------------------------------------
OpenStreetMap Singapore extract (.osm.pbf)         [ODbL]
  Pedestrian ways only: highway=footway|path|steps|pedestrian|
  living_street|residential|service, plus crossings.
  Imported with osm2pgrouting into ways / ways_vertices_pgr.

  Attributes the cost function depends on:
    covered=yes|arcade        sheltered linkways
    highway=steps             treated as impassable for the limited profile
    highway=crossing          + crossing=traffic_signals|marked|unmarked
    wheelchair=yes|no|limited
    surface, incline          where surveyed

--- Destinations and landmarks -----------------------------------
Dementia Friendly Go-To Points        831 rows -> 717 buildings   [MOH]
  Designated safe-return points. Both a destination class and a
  landmark class.

LTA MRT Station Exit                  613 points                  [LTA]
  Landmark only. Exit-level, which the Go-To Point data is not.

Bus stops (via LTA DataMall API)      ~5,000 points               [LTA]
  Landmark only. Named and numbered, so usable in an instruction.

GP Locations                          237 points                  [MOH]
Polyclinics                            26 points                  [MOH]
  Landmark only, not routing destinations. Clinics that serve as
  safe-return points already appear in the Go-To Point set.

Caregiver-entered landmarks           user-generated
  The most useful class, because they are already personally
  meaningful to the walker.

--- Study area ---------------------------------------------------
Master Plan 2019 Planning Area Boundary (No Sea)   55 polygons    [URA]
  Used to delimit the study estate for the landmark survey and
  field evaluation. Not a scoring input.

--- Validation baseline ------------------------------------------
OneMap Routing API                                                [SLA]
  Independent check on computed walking routes. Never used at
  runtime, so the system carries no live dependency on it.
```

## Go-To Point source quirks

Inspected 2026-10; all figures from that snapshot.

```
831 features, all Point, one consistent property set, no missing values in
NAME / ADDRESS / POSTAL_CODE / DESCRIPTION. Unusually clean for open data.

- 831 rows are NOT 831 destinations. 717 distinct postal codes, 707 distinct
  coordinates. Individual mall tenants are each registered but geocoded to
  the mall's single point — 20 rows share one coordinate at Waterway Point,
  19 at another, 17 at a third. DEDUPLICATE BY POSTAL CODE at import and
  route to the building, keeping the tenant list as an attribute.

- Opening hours are embedded in free text in DESCRIPTION alongside a phone
  number, separated by <BR>:
      "Monday to Friday: 9am - 6pm<BR>6251 5010"
      "Daily: 24 hours<BR>6335 0606"
  514 of 831 contain an am/pm time; 644 mention a day or "24 hours".
  Format is inconsistent ("9am" vs "9 am", "8.30 am", "Closed on PHs").
  MRT station rows carry a URL to the operator's first/last train page
  instead of hours, which cannot be resolved offline.

- 110 rows are named only "NTUC FairPrice" and 66 only "Sheng Siong
  Supermarket". Disambiguate by postal code, never by name.

- Transit rows are station-level, not exit-level: 195 transit-named rows
  resolve to 169 distinct stations, one row per station per line (Dhoby
  Ghaut appears three times). The LTA exit dataset is the finer-grained
  source and should be joined to it.

- 22 NAME values and 10 DESCRIPTION values contain U+FFFD replacement
  characters where en-dashes and apostrophes should be. The source was
  mangled before publication, so this is not a decode setting — clean with
  a substitution map at import.

- Mixed vintage: 654 of 831 last updated September 2024, the remainder
  spread to August 2026. Roughly four-fifths of the hours data is about
  two years old.

- Density: median nearest-neighbour distance between distinct buildings is
  210 m (p25 112 m, p75 336 m). A Go-To Point is typically a two to four
  minute walk away in a built-up estate.

- Composition skews to retail: ~317 supermarket and mall entries, ~195
  transit, ~140 eldercare and active ageing centres. Many are retail
  counters rather than staffed care facilities, which affects how a
  destination should be described to the user.
```

## Datasets removed with the scope change

```
Resident Population by Planning Area (SingStat) — REMOVED.
  The previous direction used it as the denominator for per-capita
  accessibility rates. Navigation has no denominator.

Master Plan 2019 Subzone Boundaries — REMOVED.
  Only ever relevant to choosing a unit of analysis for area scoring.

HDB Existing Building — REMOVED.
  Rejected previously on methodological grounds; no longer relevant either
  way.

Master Plan 2014 Rail Lines — REMOVED. Visual only, 2014 vintage.
```
