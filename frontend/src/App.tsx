import { useCallback, useRef, useEffect, useState } from 'react';
import MapView from './components/MapView';
import LayerPanel from './components/LayerPanel';
import { BASEMAPS, type BasemapKey } from './components/basemaps';
import { surface, railButton, floatingCard } from './components/panelStyles';
import { useMapLayers } from './hooks/useMapLayers';
import { useLayerColours } from './hooks/useLayerColours';
import { useGeolocation } from './hooks/useGeolocation';
import { flyTarget, type FlyTarget } from './types/map';

/**
 * Caregiver map shell.
 *
 * This is deliberately thin. The scoring and sensitivity views were removed
 * with the scope change, and the views that replace them — NavigationView for
 * the walker, CaregiverView for landmark entry, saved destinations and route
 * recording — are not built yet. What remains is the map surface, the basemap
 * and layer controls, and live position, which are the pieces both new views
 * need and which were worth keeping rather than rewriting.
 *
 * See 04-architecture.md and 07-roadmap.md.
 */
export default function App() {
  const [selectedAreaId, setSelectedAreaId] = useState<number | null>(null);
  const [layersCollapsed, setLayersCollapsed] = useState(false);
  const [basemap, setBasemap] = useState<BasemapKey>('light');
  const [flyTo, setFlyTo] = useState<FlyTarget | null>(null);

  const theme = BASEMAPS[basemap].theme;
  const c = surface(theme);
  const layerColours = useLayerColours();

  const geo = useGeolocation();
  const flownToUser = useRef(false);
  const [centredOnUser, setCentredOnUser] = useState(false);
  const handleUserPanned = useCallback(() => setCentredOnUser(false), []);

  const { layers, visible, toggleLayer, counts, loading: layersLoading, error: layersError } =
    useMapLayers();

  // Fly to the first fix — a marker that appears off-screen reads as nothing
  // having happened.
  useEffect(() => {
    if (!geo.position || flownToUser.current) return;
    flownToUser.current = true;
    setFlyTo(flyTarget(geo.position.lat, geo.position.lng, 16));
    setCentredOnUser(true);
  }, [geo.position]);

  const handleLocate = () => {
    if (geo.status !== 'tracking' || !geo.position) {
      flownToUser.current = false;
      setCentredOnUser(false);
      geo.toggle();
      return;
    }
    if (centredOnUser) {
      flownToUser.current = false;
      setCentredOnUser(false);
      geo.stop();
    } else {
      setFlyTo(flyTarget(geo.position.lat, geo.position.lng, 16));
      setCentredOnUser(true);
    }
  };

  const handleSelectArea = useCallback((id: number | null) => setSelectedAreaId(id), []);

  if (layersError) {
    return <div style={{ padding: 16 }}>Error loading map data: {layersError}</div>;
  }

  return (
    <div style={{ ...styles.shell, background: theme === 'dark' ? '#0d1116' : '#f2f2f0' }}>
      <div style={styles.pane}>
        <MapView
          layers={layers}
          visible={visible}
          selectedAreaId={selectedAreaId}
          onSelectArea={handleSelectArea}
          basemap={basemap}
          colours={layerColours.colours}
          userPosition={geo.position}
          flyTo={flyTo}
          onUserPanned={handleUserPanned}
        />

        <div style={styles.topLeft}>
          <LayerPanel
            visible={visible}
            onToggle={toggleLayer}
            counts={counts}
            loading={layersLoading}
            collapsed={layersCollapsed}
            onToggleCollapse={() => setLayersCollapsed(v => !v)}
            basemap={basemap}
            onBasemapChange={setBasemap}
            theme={theme}
            colours={layerColours.colours}
            onColourChange={layerColours.setColour}
            onResetColours={layerColours.resetColours}
            coloursCustomised={layerColours.isCustomised}
          />
        </div>

        <div style={styles.locateControl}>
          <button
            onClick={handleLocate}
            style={{
              ...railButton(theme),
              color: geo.status === 'tracking' ? '#2f80ed' : c.textMuted,
            }}
            title={
              geo.status === 'locating' ? 'Locating…'
              : geo.status !== 'tracking' ? 'Show my location'
              : centredOnUser ? 'Hide my location'
              : 'Centre on my location'
            }
            aria-label={
              geo.status === 'locating' ? 'Locating'
              : geo.status !== 'tracking' ? 'Show my location'
              : centredOnUser ? 'Hide my location'
              : 'Centre on my location'
            }
          >
            {geo.status === 'locating' ? '◌' : '◎'}
          </button>

          {geo.message && (
            <div style={{
              ...floatingCard(theme),
              padding: '6px 10px', fontSize: 11.5, maxWidth: 220, lineHeight: 1.45,
            }}>
              {geo.message}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

const styles = {
  shell: { position: 'relative' as const, height: '100vh', width: '100%', overflow: 'hidden' },
  pane: { position: 'absolute' as const, inset: 0 },
  topLeft: {
    position: 'absolute' as const, top: 12, left: 12, zIndex: 1000,
    display: 'flex', flexDirection: 'column' as const, gap: 10,
  },
  locateControl: {
    position: 'absolute' as const, right: 12, bottom: 16, zIndex: 1000,
    display: 'flex', flexDirection: 'column' as const, alignItems: 'flex-end', gap: 6,
  },
};
