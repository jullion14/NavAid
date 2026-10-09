import { useEffect, useMemo, useState } from 'react';
import type { FeatureCollection } from 'geojson';
import api from '../services/api';

/** Landmark layers arrive once LandmarksController exists. */
export type LayerKey = 'planningAreas';

export const LAYER_META: Record<LayerKey, { label: string; color: string }> = {
  planningAreas: { label: 'Planning Areas', color: '#3388ff' },
};

/** Layer data and visibility, lifted out of MapView so the map component only renders. */
export function useMapLayers() {
  const [layers, setLayers] = useState<Record<LayerKey, FeatureCollection | null>>({
    planningAreas: null,
  });
  const [visible, setVisible] = useState<Record<LayerKey, boolean>>({
    planningAreas: true,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.get('/api/planningareas/geojson')
      .then(pa => setLayers({ planningAreas: pa.data }))
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  const toggleLayer = (key: LayerKey) => setVisible(v => ({ ...v, [key]: !v[key] }));

  const counts = useMemo(
    () => Object.fromEntries(
      (Object.keys(layers) as LayerKey[]).map(k => [k, layers[k]?.features.length]),
    ) as Partial<Record<LayerKey, number>>,
    [layers],
  );

  return { layers, visible, toggleLayer, counts, loading, error };
}
