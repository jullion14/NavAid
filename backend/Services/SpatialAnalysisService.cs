using NavAid.Api.Models;
using Microsoft.EntityFrameworkCore;

public class SpatialAnalysisService
{
    private readonly NavAidDbContext _db;
    public SpatialAnalysisService(NavAidDbContext db) => _db = db;

    private const string MetricsSql = @"
WITH a AS (
    SELECT pa.id, pa.name, pa.region, pa.geom,
           ST_PointOnSurface(pa.geom) AS rep_point
    FROM planning_areas pa
    {0}
)
SELECT
    a.id                                        AS ""PlanningAreaId"",
    a.name                                      AS ""Name"",
    a.region                                    AS ""Region"",

    ST_Y(a.rep_point)                            AS ""RepPointLat"",
    ST_X(a.rep_point)                            AS ""RepPointLng"",

    p.total_population                          AS ""Population"",
    ST_Area(a.geom::geography) / 1000000.0       AS ""AreaSqKm"",
    CASE WHEN p.total_population IS NOT NULL
         THEN p.total_population / NULLIF(ST_Area(a.geom::geography) / 1000000.0, 0)
    END                                          AS ""PopulationDensity"",

    gp.cnt                                       AS ""GpCount"",
    pc.cnt                                       AS ""PolyclinicCount"",
    (gp.cnt + pc.cnt)                            AS ""TotalFacilities"",
    CASE WHEN p.total_population > 0
         THEN (gp.cnt + pc.cnt) * 10000.0 / p.total_population
    END                                          AS ""FacilitiesPer10k"",

    nf.dist                                      AS ""NearestFacilityMeters"",
    nf.name                                      AS ""NearestFacilityName"",
    nf.facility_type                             AS ""NearestFacilityType"",

    nf.lat                                       AS ""NearestFacilityLat"",
    nf.lng                                       AS ""NearestFacilityLng"",

    mx.cnt                                       AS ""MrtExitCount"",
    nm.dist                                      AS ""NearestMrtMeters"",
    nm.station_name                              AS ""NearestMrtStation"",

    nm.lat                                       AS ""NearestMrtLat"",
    nm.lng                                       AS ""NearestMrtLng"",

    bus.cnt                                      AS ""BusStopCount"",
    bus.well_served                              AS ""WellServedBusStops"",
    bus.busiest                                  AS ""BusiestStopServices""
FROM a
LEFT JOIN population p ON p.planning_area_id = a.id

CROSS JOIN LATERAL (
    SELECT COUNT(*)::int AS cnt FROM healthcare_facilities h
    WHERE h.planning_area_id = a.id AND h.facility_type = 'GP'
) gp
CROSS JOIN LATERAL (
    SELECT COUNT(*)::int AS cnt FROM healthcare_facilities h
    WHERE h.planning_area_id = a.id AND h.facility_type = 'Polyclinic'
) pc
CROSS JOIN LATERAL (
    SELECT COUNT(*)::int AS cnt FROM transit_exits t
    WHERE t.planning_area_id = a.id
) mx
CROSS JOIN LATERAL (
    SELECT COUNT(*)::int AS cnt,
           COUNT(*) FILTER (WHERE b.service_count >= 10)::int AS well_served,
           MAX(b.service_count)::int AS busiest
    FROM bus_stops b
    WHERE b.planning_area_id = a.id
) bus
LEFT JOIN LATERAL (
    SELECT h.name, h.facility_type,
           ST_Distance(a.rep_point::geography, h.geom::geography) AS dist,
           ST_Y(h.geom) AS lat, ST_X(h.geom) AS lng
    FROM healthcare_facilities h
    ORDER BY a.rep_point <-> h.geom
    LIMIT 1
) nf ON TRUE

LEFT JOIN LATERAL (
    SELECT t.station_name,
           ST_Distance(a.rep_point::geography, t.geom::geography) AS dist,
           ST_Y(t.geom) AS lat, ST_X(t.geom) AS lng
    FROM transit_exits t
    ORDER BY a.rep_point <-> t.geom
    LIMIT 1
) nm ON TRUE
";

    private const string PointSql = @"
WITH pt AS (
    SELECT ST_SetSRID(ST_MakePoint({1}, {0}), 4326) AS geom
)
SELECT
    {0}::double precision                        AS ""Lat"",
    {1}::double precision                        AS ""Lng"",

    pa.id                                        AS ""PlanningAreaId"",
    pa.name                                      AS ""PlanningAreaName"",
    pa.region                                    AS ""Region"",
    CASE WHEN pa.id IS NOT NULL
         THEN ST_Distance(pt.geom::geography,
                          ST_PointOnSurface(pa.geom)::geography)
    END                                          AS ""MetresFromAreaRepPoint"",

    nf.dist                                      AS ""NearestFacilityMeters"",
    nf.name                                      AS ""NearestFacilityName"",
    nf.facility_type                             AS ""NearestFacilityType"",
    nf.lat                                       AS ""NearestFacilityLat"",
    nf.lng                                       AS ""NearestFacilityLng"",

    nm.dist                                      AS ""NearestMrtMeters"",
    nm.station_name                              AS ""NearestMrtStation"",
    nm.lat                                       AS ""NearestMrtLat"",
    nm.lng                                       AS ""NearestMrtLng"",

    nb.dist                                      AS ""NearestBusStopMeters"",
    nb.description                               AS ""NearestBusStopDescription"",
    nb.service_count                             AS ""NearestBusStopServices"",
    nb.lat                                       AS ""NearestBusStopLat"",
    nb.lng                                       AS ""NearestBusStopLng""
FROM pt
LEFT JOIN planning_areas pa ON ST_Contains(pa.geom, pt.geom)

LEFT JOIN LATERAL (
    SELECT name, facility_type, dist, lat, lng FROM (
        SELECT h.name, h.facility_type,
               ST_Distance(pt.geom::geography, h.geom::geography) AS dist,
               ST_Y(h.geom) AS lat, ST_X(h.geom) AS lng
        FROM healthcare_facilities h
        ORDER BY pt.geom <-> h.geom
        LIMIT 20
    ) c ORDER BY c.dist LIMIT 1
) nf ON TRUE

LEFT JOIN LATERAL (
    SELECT station_name, dist, lat, lng FROM (
        SELECT t.station_name,
               ST_Distance(pt.geom::geography, t.geom::geography) AS dist,
               ST_Y(t.geom) AS lat, ST_X(t.geom) AS lng
        FROM transit_exits t
        ORDER BY pt.geom <-> t.geom
        LIMIT 20
    ) c ORDER BY c.dist LIMIT 1
) nm ON TRUE

LEFT JOIN LATERAL (
    SELECT description, service_count, dist, lat, lng FROM (
        SELECT b.description, b.service_count,
               ST_Distance(pt.geom::geography, b.geom::geography) AS dist,
               ST_Y(b.geom) AS lat, ST_X(b.geom) AS lng
        FROM bus_stops b
        ORDER BY pt.geom <-> b.geom
        LIMIT 20
    ) c ORDER BY c.dist LIMIT 1
) nb ON TRUE
";

    public async Task<AccessibilityMetrics?> GetForAreaAsync(int planningAreaId)
    {
        var sql = string.Format(MetricsSql, "WHERE pa.id = {0}");
        return await _db.AccessibilityMetrics
            .FromSqlRaw(sql, planningAreaId)
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    public async Task<List<AccessibilityMetrics>> GetAllAsync(int minPopulation = 1000)
    {
        // Areas below the floor produce unstable per-capita rates (1 clinic in
        // Tuas, pop 80, = 125 per 10k) which distort min-max normalisation for
        // every other area. Documented as an explicit inclusion criterion.
        var sql = string.Format(MetricsSql,
            "WHERE EXISTS (SELECT 1 FROM population pp WHERE pp.planning_area_id = pa.id " +
            "AND pp.total_population >= {0})");
        return await _db.AccessibilityMetrics
            .FromSqlRaw(sql, minPopulation)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<PointAccessibility?> GetForPointAsync(double lat, double lng)
    {
        // Reject anything outside a generous box around Singapore before
        // touching the database: a stray click on the world map would otherwise
        // return the nearest clinic to somewhere in the Atlantic.
        if (lat < 1.10 || lat > 1.52 || lng < 103.55 || lng > 104.15)
        {
            return null;
        }

        var sql = string.Format(PointSql, "{0}", "{1}");

        return await _db.Set<PointAccessibility>()
            .FromSqlRaw(sql, lat, lng)
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }
}