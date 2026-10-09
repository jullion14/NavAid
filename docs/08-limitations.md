# Limitations

> Part of the Landmark-Guided Walking Navigation reference docs. See [`README.md`](README.md) for the index.

Known weaknesses, recorded as they are encountered rather than reconstructed at
the end. Each is a deliberate position, not an oversight — the report should
state them rather than let an examiner find them.

## Data

**1. OpenStreetMap pedestrian coverage is uneven for exactly the attributes the
cost function needs.** Footway geometry in Singapore HDB estates is good, but the
tags the routing depends on — `covered` for sheltered walkways, `surface`,
`incline`, `wheelchair`, kerb-ramp presence at crossings — are tagged
inconsistently and in places not at all. An untagged sheltered link is
indistinguishable from an uncovered one, so the shelter preference is applied
only where the data supports it. Coverage should be measured and reported per
tag rather than assumed.

**2. Go-To Point coverage is retail-skewed.** Median spacing is roughly 210 m,
but the points cluster along shop frontages and are thin inside residential
blocks — which is where disorientation actually happens. The fallback in
[`06-decisions-and-gotchas.md`](06-decisions-and-gotchas.md) (D5) is therefore
better in town centres than in the places it would most help.

**3. Go-To Point opening hours are free text.** "Mon-Fri 9am-6pm, Sat 9am-1pm"
and "24 hours" sit in one string column. No open-now filtering is performed, so
the application can route someone to a shuttered shopfront. Temporal filtering
is out of scope and stated as such.

**4. The Go-To Point set is a 2024 snapshot.** Roughly four-fifths of records
were last updated September 2024, a minority earlier. Participating businesses
close and change hands, so some proportion of the set is stale and the project
has no way to measure how much.

**5. Some Go-To Point records are station-level rather than shop-level**, so
their coordinate can be tens of metres from where help would actually be found.

**6. Landmark salience is judgement-based.** The salience values that drive the
landmark discount were assigned by reasoning about the domain — a polyclinic is
more recognisable than a bus stop — not derived from any measurement of what
older adults in these estates actually notice or remember. Salience research
exists; this project does not replicate it.

## Method

**7. Gait speeds come from the literature, not from measurement.** The `default`
profile (1.10 m/s) and `limited` profile (0.80 m/s) are anchored to published
figures for older Singaporean adults. No one in the target population was timed
for this project. The profiles describe plausible walkers, not these walkers.

**8. The researcher does not match the `limited` profile.** Field verification
is walked by a 20-something student. Timing, fatigue, and the subjective
difficulty of a staircase or an unsheltered stretch are therefore not observed
as the intended user would experience them. The GPS traces verify that the route
is walkable and the instructions match what is visible; they say nothing about
whether it is comfortable.

**9. Cost-function penalty weights are judgement-based.** The decision-point
penalty, the landmark discount, and the familiarity discount are set by
reasoning about their relative importance — familiarity strongest, then
landmarks, then decision points — not calibrated against observed wayfinding
performance. Defensible for a prototype, unvalidated.

**10. Three discrete profiles, not a continuous model.** A continuous speed or
ability parameter would imply precision the underlying data does not have (see
7). The trade-off is that a walker who falls between profiles gets the nearer
one.

**11. The cost function is compensatory.** A route can accumulate a large
landmark and familiarity discount and so be preferred despite having more
decision points than an alternative. This is intended — the discounts are the
mechanism — but it means no single term can veto a route, and a pathological
case (a very familiar route that is also very complex) is possible in principle.

**12. GPS accuracy degrades under exactly the cover the system prefers.**
Sheltered walkways, void decks and linkways are the routes the cost function
favours, and they are also where consumer GPS is worst. Deviation detection and
field verification are both least reliable in the conditions the application
most often creates. Threshold tuning can mitigate this; it cannot remove it.

## Scope

**13. Walking only.** No public transport or vehicle routing. Journeys beyond a
walkable threshold are redirected to the nearest Go-To Point rather than routed.
A walker who needs to cross town is therefore handed to a person, not guided
home — which is the intended behaviour, but it is a boundary, not a feature.

**14. Foreground use only.** The application guides a journey the user starts
and has open. It does not monitor location in the background, does not detect
wandering, and cannot alert anyone to a journey the walker did not initiate in
the app. Browser background location is the hard constraint; the alternative was
a native mobile app, which was out of scope for a single-student eight-month
project.

**15. Deviation alerting makes no claim about intent.** It fires when the walker
leaves the computed route, not when the walker is lost. These are different
events and the report should not conflate them.

**16. The landmark survey covers a single estate.** Caregiver-entered landmarks
and the quality check on route-derived landmarks are done for one study area.
Nothing demonstrates that the approach generalises to estates with different
built form.

**17. Web Speech recognition support is uneven.** It works well in Chrome on
Android and acceptably in Safari on iOS, but iOS behaviour around continuous
recognition and permission re-prompting is less reliable. A text-entry path
exists as a fallback, which partly defeats the purpose for a user who cannot
read a screen comfortably.

**18. No clinical claim and no validation with the target population.** The
project is a navigation aid, not a diagnostic or care tool. No one with dementia
or at risk of disorientation participated in its design or testing — ethics
approval and recruitment for that are beyond the project's scope and timeline.
Every usability claim is therefore inferred from the literature and from the
researcher's own walking, and should be read that way.

**19. Evaluation is researcher-walked, not user-tested.** T2 compares routes
quantitatively and T3 verifies them in the field, but neither measures whether
an older adult at risk of disorientation follows them more successfully than a
conventional route. That is the claim the project is built around and the one it
cannot test. It is the single most important limitation in this document and the
clearest line of future work.

**20. No credentialed accounts, and the device-pairing question is open.** Saved
destinations and recorded routes persist server-side against a household
identifier. How the caregiver's device and the walker's device come to share
that identifier is unresolved — see O1 in
[`06-decisions-and-gotchas.md`](06-decisions-and-gotchas.md). A production
deployment would need real authentication; this prototype does not have it.

**21. GP clinics and polyclinics are landmarks only, never destinations.** They
are named in instructions because they are recognisable buildings. Offering them
as destinations would edge the project toward a care-navigation claim it does
not make.
