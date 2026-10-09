import { useCallback, useEffect, useMemo, useState } from 'react';
import { LAYER_META, type LayerKey } from './useMapLayers';

const STORAGE_KEY = 'navaid.layerColours';

export const DEFAULT_COLOURS = Object.fromEntries(
  (Object.keys(LAYER_META) as LayerKey[]).map(k => [k, LAYER_META[k].color]),
) as Record<LayerKey, string>;

/**
 * Layer colours, overridable by the user. Every place that draws a layer —
 * map dots, panel swatches, measurement legs — reads from here, so a leg
 * always matches the feature it points at.
 */
export function useLayerColours() {
  const [overrides, setOverrides] = useState<Partial<Record<LayerKey, string>>>(() => {
    try {
      return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}');
    } catch {
      return {};
    }
  });

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(overrides));
  }, [overrides]);

  const colours = useMemo(
    () => ({ ...DEFAULT_COLOURS, ...overrides }),
    [overrides],
  );

  const setColour = useCallback((key: LayerKey, colour: string) => {
    setOverrides(prev => ({ ...prev, [key]: colour }));
  }, []);

  const resetColours = useCallback(() => setOverrides({}), []);

  const isCustomised = Object.keys(overrides).length > 0;

  return { colours, setColour, resetColours, isCustomised };
}