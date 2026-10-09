# Methodology

> Part of the reference docs. See [`README.md`](../README.md) for the index.

This is the report-facing document: what is computed, how, and why. Known
weaknesses are collected separately in [`08-limitations.md`](08-limitations.md).

## The claim

A route can be short and still be hard to follow. The number and difficulty of
the decisions along it, and whether anything along the way confirms the walker
is still right, matter more to a person whose orientation is unreliable than
the distance does. Duckham and Kulik (2003) argued this as *simplest paths*;
Zhou et al. (2019) extended it by identifying complicated decision points from
network structure and routing around them.

This project computes such routes for Singapore's public housing estates, where
repeated building form supplies few distinguishing cues, and phrases them using
landmarks rather than street names.

## Mobility profiles

Profiles are defined in `MobilityProfileCatalog` and exposed through the API, so
every parameter is visible and challengeable rather than buried in code.

| Profile | Speed | Steps | Unsignalised crossing | Unsheltered |
|---|---|---|---|---|
| `euclidean` | straight-line baseline | — | — | — |
| `default` | 1.10 m/s | allowed, small penalty | small penalty | none |
| `limited` | 0.80 m/s | impassable | large penalty | moderate penalty |

Speeds are taken from published reference values, not measured here. Yang et al.
(2024) report a median habitual gait speed of 1.08 m/s for Singapore adults aged
21–80, which is noticeably slower than the 1.3–1.4 m/s common in Western norms —
so the local figure is used for the default profile. The `limited` figure sits
below Bohannon's ~0.94 m/s for adults over eighty, representing a mobility-limited
rather than merely older walker.

Penalty magnitudes are initial estimates and are treated as tunable parameters
subject to sensitivity testing, not as established constants.

## Cost function

Traversal cost departs from shortest-path in three ways. All three are
precomputed into `way_profile_cost` at import.

**Decision-point cost.** Each vertex where a decision must be made adds a cost
weighted by how hard that decision is to get right. `branch_count` is cached at
import; a vertex with three or more outgoing ways at similar angles costs more
than a simple corner. Degree-2 vertices are not decision points at all.

**Landmark discount.** A segment passing within a landmark's `visibility_m`
is discounted, because a landmark confirms to the walker that they are still on
the right path. The discount scales with `salience`.

**Familiarity discount.** Segments belonging to a route in `recorded_routes` are
discounted heavily, so a known path is chosen over a shorter unknown one
wherever a known path exists. This is the strongest single term: for someone
with cognitive decline, familiarity beats optimality.

The resulting route is longer than the shortest path by construction. Measuring
that difference is the point of the evaluation, not a defect.

## Landmark model

Five sources, three roles.

| Kind | Source | Role |
|---|---|---|
| `gtp` | MOH Dementia Go-To Points | Destination **and** landmark |
| `caregiver` | Entered through the caregiver view | Landmark, highest salience |
| `route_derived` | Extracted from recorded routes | Landmark |
| `mrt_exit`, `bus_stop` | LTA | Landmark |
| `clinic` | MOH GP / polyclinic locations | Landmark |

Caregiver-entered landmarks are expected to outperform the published sets,
because a personally meaningful reference point ("the kopi shop where you always
sit") is recognised faster than a generic one. They also solve a data problem:
no dataset exists for the murals, colour-coded blocks and pillar signage
installed under the dementia-friendly community programme, and a caregiver can
record the ones that matter without a survey.

**Salience** is recorded 1–5 across visual distinctiveness, semantic meaning and
structural position, following the landmark-salience literature. It is a
judgement, recorded as such.

**Snapping.** Every landmark resolves to a `nearest_vertex` at import. A landmark
matters relative to a decision point, not as a free-floating coordinate, and
snapping absorbs a few metres of positional error — which matters because
Go-To Point coordinates are building-level and caregiver entries come from a
phone.

## Go-To Point deduplication

The published set has 831 rows but 717 distinct postal codes. Individual mall
tenants are each registered as a Go-To Point and geocoded to the mall's single
coordinate — twenty rows share one point at Waterway Point.

Routing therefore resolves to the **building**, not the shop unit. Tenants are
kept as an attribute array. Without this, "route to the nearest Go-To Point"
would compute twenty identical routes and offer the user a choice between Adidas
and Sushi Express.

## Destination fallback

Where a saved destination lies beyond a configurable walking threshold, the
system routes to the nearest Go-To Point instead of attempting a journey the
user cannot complete. This is what the national network exists for: a staffed
location where someone disoriented is helped to contact their caregiver.

At a median 210 m spacing between distinct buildings, a Go-To Point is typically
a two to four minute walk away in a built-up estate.

## AI constraint

The model appears at two points, each followed immediately by a deterministic
check.

**Input.** The transcript is passed to Gemini with a `responseSchema` returning
an action, a destination and an optional profile — nothing else. That object is
validated against known destinations and landmarks **before** the routing engine
is called. An intent that fails validation is refused and the user asked to
repeat, rather than answered with a guess.

**Output.** The model receives the computed route as an ordered list of segments
with the landmarks the engine attached to each, and phrases them as spoken
directions. Every landmark named in the generated text is checked back against
that list; any instruction containing something not supplied is discarded in
favour of a template.

The model never calculates a route and never selects a landmark. Current
autonomous-GIS agents report roughly 80–86% success at producing a correct
spatial workflow (Li & Ning, 2023) — a rate that cannot support an instruction a
disoriented person will act on immediately and without scrutiny.

## Evaluation

**T1 — Routing correctness.** Default-profile walking routes are compared against
the OneMap routing service across a sample of origin–destination pairs,
reporting agreement in distance and duration. This establishes that the network
import and cost model are sound before any profile-specific claim is made.

**T2 — Route comparison.** The headline measurement. For 20 journeys within the
study estate, the shortest and landmark-guided routes are compared on decision
points, turns, proportion of segments carrying a landmark, and additional
distance — establishing what ease of following costs in metres. A result showing
little divergence is still reportable: it would delimit when a shortest-path
approximation is adequate.

**T3 — Field verification.** A subset of those routes is walked with GPS trace
capture. Recorded tracks are compared against planned routes to test whether the
instructions could in fact be followed, and where they were ambiguous. The test
targets **path selection**, not speed — the researcher does not walk at the
`limited` profile's pace, and that distinction is stated rather than glossed.

**T4 — AI output validation.** The intent parser is tested on spoken requests
including deliberately malformed and out-of-range inputs, measuring correct
interpretation and correct refusal. Generated instructions are checked for
landmarks absent from the computed route, measuring the verifier's rejection
rate.
