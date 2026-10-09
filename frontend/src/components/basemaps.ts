/**
 * Basemap options.
 *
 * Each entry carries a `theme` flag because the panels and the planning-area
 * outlines have to adapt: white-alpha borders vanish on a light basemap, and
 * dark panel surfaces look wrong floating over pale tiles.
 *
 * NOTE ON TRAFFIC: there is no free tile source for live traffic. Google Maps
 * and Mapbox both provide it, but each requires a billed API key and Google's
 * needs the Maps JS API rather than plain XYZ tiles, so it cannot be dropped
 * into react-leaflet as a TileLayer. Left out deliberately — worth a line in
 * the report as a data-availability constraint rather than a missing feature.
 */

export type BasemapKey = 'dark' | 'light' | 'satellite' | 'terrain';

export interface Basemap {
  key: BasemapKey;
  label: string;
  url: string;
  attribution: string;
  /** Drives panel surfaces and vector stroke colours. */
  theme: 'dark' | 'light';
  /** Overlaid on satellite so place names stay readable. */
  labelUrl?: string;
  maxZoom?: number;
}

export const BASEMAPS: Record<BasemapKey, Basemap> = {
  dark: {
    key: 'dark',
    label: 'Dark',
    url: 'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png?key=cb1_3of4_1_1ab8c1e21f7c5fe13b29e03a',
    attribution: '&copy; OpenStreetMap contributors &copy; CARTO',
    theme: 'dark',
    maxZoom: 20,
  },
  light: {
    key: 'light',
    label: 'Light',
    url: 'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png?key=cb1_3of4_1_1ab8c1e21f7c5fe13b29e03a',
    attribution: '&copy; OpenStreetMap contributors &copy; CARTO',
    theme: 'light',
    maxZoom: 20,
  },
  satellite: {
    key: 'satellite',
    label: 'Satellite',
    url: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
    attribution: 'Tiles &copy; Esri — Source: Esri, Maxar, Earthstar Geographics',
    // Imagery is dark enough overall that light text reads better on it.
    theme: 'dark',
    labelUrl: 'https://{s}.basemaps.cartocdn.com/dark_only_labels/{z}/{x}/{y}{r}.png',
    maxZoom: 19,
  },
  terrain: {
    key: 'terrain',
    label: 'Terrain',
    url: 'https://tile.opentopomap.org/{z}/{x}/{y}.png',
    attribution: '&copy; OpenStreetMap contributors, SRTM — style: OpenTopoMap (CC-BY-SA)',
    theme: 'light',
    maxZoom: 17,
  },
};

export const BASEMAP_ORDER: BasemapKey[] = ['dark', 'light', 'satellite', 'terrain'];