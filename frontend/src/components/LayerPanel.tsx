import { LAYER_META, type LayerKey } from '../hooks/useMapLayers';
import { floatingCard, railButton, chevronStyle, surface, type PanelTheme } from './panelStyles';
import { BASEMAPS, BASEMAP_ORDER, type BasemapKey } from './basemaps';
import { useState } from 'react';

interface Props {
  visible: Record<LayerKey, boolean>;
  onToggle: (key: LayerKey) => void;
  counts?: Partial<Record<LayerKey, number>>;
  loading?: boolean;
  collapsed: boolean;
  onToggleCollapse: () => void;
  basemap: BasemapKey;
  onBasemapChange: (key: BasemapKey) => void;
  theme: PanelTheme;
  colours: Record<LayerKey, string>;
  onColourChange: (key: LayerKey, colour: string) => void;
  onResetColours: () => void;
  coloursCustomised: boolean;
}

export default function LayerPanel({
  visible, onToggle, counts, loading, collapsed, onToggleCollapse,
  basemap, onBasemapChange, theme,
  colours, onColourChange, onResetColours, coloursCustomised,
}: Props) {
  const c = surface(theme);
  const [draft, setDraft] = useState<Partial<Record<LayerKey, string>>>({});

  if (collapsed) {
    return (
      <button onClick={onToggleCollapse} style={railButton(theme)} title="Show layers" aria-label="Show layers">
        ▦
      </button>
    );
  }

  return (
    <aside style={{ ...floatingCard(theme), width: 214, padding: '12px 14px' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
        <h3 style={{ margin: 0, fontSize: 14 }}>Layers</h3>
        <button onClick={onToggleCollapse} style={chevronStyle(theme)} title="Hide layers">«</button>
      </div>

      {/* --- basemap --- */}
      <div style={{ display: 'flex', gap: 4, marginBottom: 12 }}>
        {BASEMAP_ORDER.map(key => {
          const active = key === basemap;
          return (
            <button
              key={key}
              onClick={() => onBasemapChange(key)}
              title={BASEMAPS[key].label}
              style={{
                flex: 1,
                padding: '5px 0',
                fontSize: 10.5,
                cursor: 'pointer',
                borderRadius: 5,
                border: `1px solid ${active ? 'var(--accent)' : c.border}`,
                background: active ? 'var(--accent-bg, rgba(120,120,255,0.14))' : 'transparent',
                color: active ? c.text : c.textMuted,
                fontWeight: active ? 600 : 400,
              }}
            >
              {BASEMAPS[key].label}
            </button>
          );
        })}
      </div>

      {loading && <p style={{ fontSize: 12, color: c.textMuted }}>Loading…</p>}

      {(Object.keys(LAYER_META) as LayerKey[]).map(key => {
        const meta = LAYER_META[key];
        return (
          <div key={key} style={{
            display: 'flex', alignItems: 'center', gap: 8,
            marginBottom: 7, fontSize: 13,
          }}>
            <input
              type="checkbox"
              checked={visible[key]}
              onChange={() => onToggle(key)}
              id={`layer-${key}`}
              style={{ accentColor: colours[key], cursor: 'pointer' }}
            />
            <input
              type="color"
              className="layer-swatch"
              value={draft[key] ?? colours[key]}
              // The picker fires `input` continuously while dragging; committing each
              // one rebuilds every marker in that layer. Commit on release instead.
              onChange={e => setDraft(d => ({ ...d, [key]: e.target.value }))}
              onBlur={() => {
                const v = draft[key];
                setDraft(d => { const { [key]: _, ...rest } = d; return rest; });
                if (v && v !== colours[key]) onColourChange(key, v);
              }}
              title={`Change ${meta.label} colour`}
            />
            <label htmlFor={`layer-${key}`} style={{ flex: 1, cursor: 'pointer' }}>
              {meta.label}
            </label>
            {counts?.[key] !== undefined && (
              <span style={{ fontSize: 11, color: c.textMuted }}>{counts[key]}</span>
            )}
          </div>
        );
      })}

      {coloursCustomised && (
        <div style={{ marginTop: 12, paddingTop: 10, borderTop: `1px solid ${c.border}` }}>
          <button
            onClick={onResetColours}
            style={{
              background: 'none', border: 'none',
              color: c.textMuted, cursor: 'pointer', fontSize: 11.5, padding: 0,
            }}
          >
            Reset colours
          </button>
        </div>
      )}
    </aside>
  );
}