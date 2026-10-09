import { useEffect, useState } from 'react';
import { MapContainer, TileLayer, GeoJSON, CircleMarker,
  Popup, useMap, useMapEvents, Circle } from 'react-leaflet';
import type { FeatureCollection } from 'geojson';
import 'leaflet/dist/leaflet.css';
import type { LayerKey } from '../hooks/useMapLayers';
import { areaStroke } from './panelStyles';
import { BASEMAPS, type BasemapKey } from './basemaps';
import type { UserPosition } from '../hooks/useGeolocation';
import L from 'leaflet';
import type { FlyTarget } from '../types/map';

// Must live outside the component: a new renderer each render detaches the
// previous layer group.
const svgRenderer = L.svg();

interface Props {
  layers: Record<LayerKey, FeatureCollection | null>;
  visible: Record<LayerKey, boolean>;
  selectedAreaId: number | null;
  onSelectArea: (id: number | null) => void;
  basemap: BasemapKey;
  colours: Record<LayerKey, string>;
  userPosition: UserPosition | null;
  flyTo: FlyTarget | null;
  onUserPanned: () => void;
}

/** The map, and only the map. Panels are siblings rendered over it by App. */
export default function MapView({
  layers, visible, selectedAreaId, onSelectArea, basemap, colours,
  userPosition, flyTo, onUserPanned,
}: Props) {
  const base = BASEMAPS[basemap];
  const stroke = areaStroke(base.theme);
  const [highlight, setHighlight] = useState<{ lat: number; lng: number } | null>(null);

  const USER_COLOUR = '#2f80ed';

  const areaStyle = (feature: any) => {
    const isSelected = feature.properties.id === selectedAreaId;
    return {
      fillColor: isSelected ? '#f39c12' : colours.planningAreas,
      weight: isSelected ? 3 : 1,
      opacity: 1,
      color: isSelected ? '#e67e22' : stroke,
      // Imagery needs a lighter touch or the polygons bury it entirely.
      fillOpacity: isSelected ? 0.7 : (basemap === 'satellite' ? 0.22 : 0.35),
    };
  };

  const onEachArea = (feature: any, layer: any) => {
    layer.on({ click: () => onSelectArea(feature.properties.id) });
    layer.bindTooltip(feature.properties.name, { sticky: true });
  };

  // The ring is temporary on purpose: it answers "which one did I just fly to"
  // and then gets out of the way, rather than becoming a second kind of marker.
  useEffect(() => {
    if (!flyTo) return;
    setHighlight({ lat: flyTo.lat, lng: flyTo.lng });
    const t = setTimeout(() => setHighlight(null), 4000);
    return () => clearTimeout(t);
  }, [flyTo?.nonce]);

  return (
    <MapContainer
      center={[1.3521, 103.8198]}
      zoom={12}
      zoomControl={false}
      style={{ position: 'absolute', inset: 0,
        background: base.theme === 'dark' ? '#0d1116' : '#e8e8e8' }}
    >
      <InvalidateOnResize />
      <DragWatch onDrag={onUserPanned} />
      <FlyToTarget target={flyTo} />

      {/* key forces a fresh tile layer when the basemap changes */}
      <TileLayer
        key={base.key}
        attribution={base.attribution}
        url={base.url}
        maxZoom={base.maxZoom ?? 19}
      />
      {base.labelUrl && (
        <TileLayer key={`${base.key}-labels`} url={base.labelUrl} maxZoom={base.maxZoom ?? 19} />
      )}

      {visible.planningAreas && layers.planningAreas && (
        <GeoJSON
          key={`areas-${selectedAreaId}-${basemap}-${colours.planningAreas}`}
          data={layers.planningAreas}
          style={areaStyle}
          onEachFeature={onEachArea}
        />
      )}

      {userPosition && (
        <>
          {/* Accuracy matters here: a 2 km fix would otherwise imply a precision
              the reading doesn't have. */}
          <Circle
            center={[userPosition.lat, userPosition.lng]}
            radius={userPosition.accuracy}
            pathOptions={{ color: USER_COLOUR, weight: 1, fillColor: USER_COLOUR, fillOpacity: 0.12 }}
          />
          <CircleMarker
            center={[userPosition.lat, userPosition.lng]}
            radius={6}
            pathOptions={{ color: '#ffffff', weight: 2, fillColor: USER_COLOUR, fillOpacity: 1 }}
          >
            <Popup>
              <strong>Your location</strong><br />
              Accurate to about {Math.round(userPosition.accuracy)} m
            </Popup>
          </CircleMarker>
        </>
      )}

      {highlight && (
        <CircleMarker
          center={[highlight.lat, highlight.lng]}
          radius={13}
          className="search-pulse"
          interactive={false}
          pathOptions={{ color: '#FF2D95', weight: 2.5, fill: false, opacity: 0.95, renderer: svgRenderer }}
        />
      )}
    </MapContainer>
  );
}

/** A panel opening and closing changes the visible map area. */
function InvalidateOnResize() {
  const map = useMap();
  useEffect(() => {
    const ro = new ResizeObserver(() => map.invalidateSize());
    ro.observe(map.getContainer());
    return () => ro.disconnect();
  }, [map]);
  return null;
}

/** Targets are usually off-screen, so this flies rather than pans. */
function FlyToTarget({ target }: { target: FlyTarget | null }) {
  const map = useMap();
  useEffect(() => {
    if (!target) return;
    map.flyTo([target.lat, target.lng], target.zoom, { duration: 0.8 });
  }, [target?.nonce, map]);
  return null;
}

function DragWatch({ onDrag }: { onDrag: () => void }) {
  useMapEvents({ dragstart: onDrag });
  return null;
}
