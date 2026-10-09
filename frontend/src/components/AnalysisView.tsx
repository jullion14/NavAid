import { useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import type {
  AreaPriorityScore, MetricDescriptor, PriorityScoreResponse, WeightMap,
} from '../services/priorityApi';
import type { RankStability } from '../services/sensitivityApi';
import { colourFor, sectionStyle, surface, scoreColour, type PanelTheme } from './panelStyles';
import StabilityBar from './StabilityBar';
import { InfoTip, Callout, HELP } from './InfoTip';
import ExplanationPanel from './ExplanationPanel';

interface Props {
  data: PriorityScoreResponse | null;
  metrics: MetricDescriptor[];
  notes: string[];
  weights: WeightMap;
  onWeightChange: (key: string, value: number) => void;
  onResetWeights: () => void;
  isDefault: boolean;
  loading: boolean;
  error: string | null;
  selectedAreaId: number | null;
  onSelectArea: (id: number) => void;
  theme: PanelTheme;
  stabilityById: Map<number, RankStability>;
  onShowOnMap: (id: number) => void;
}

/**
 * The analysis surface: weights and the full ranked table.
 *
 * Split out of the map view because these need width and are read
 * deliberately rather than glanced at. State is shared with the map, so a
 * selection made here is already highlighted when the user switches back.
 */
export default function AnalysisView({
  data, metrics, notes, weights, onWeightChange, onResetWeights, isDefault,
  loading, error, selectedAreaId, onSelectArea, theme,
  stabilityById, onShowOnMap,
}: Props) {
  const c = surface(theme);
  const s = mk(c, theme);
  const [sortKey, setSortKey] = useState<string>('rank');
  const [sortAsc, setSortAsc] = useState(true);

  const weightSum = useMemo(() => Object.values(weights).reduce((a, b) => a + b, 0), [weights]);

  const scoreRange = useMemo(() => {
    if (!data || data.results.length === 0) return { min: 0, max: 1 };
    const v = data.results.map(r => r.totalScore);
    return { min: Math.min(...v), max: Math.max(...v) };
  }, [data]);

  const rows = useMemo(() => {
    if (!data) return [];
    const sorted = [...data.results];
    sorted.sort((a, b) => {
      let d = 0;
      if (sortKey === 'rank') d = a.rank - b.rank;
      else if (sortKey === 'name') d = a.name.localeCompare(b.name);
      else d = rawOf(a, sortKey) - rawOf(b, sortKey);
      return sortAsc ? d : -d;
    });
    return sorted;
  }, [data, sortKey, sortAsc]);

  const maxScore = useMemo(
    () => (data ? Math.max(...data.results.map(r => r.totalScore), 1e-4) : 1),
    [data],
  );

  const toggleSort = (key: string) => {
    if (key === sortKey) setSortAsc(v => !v);
    else { setSortKey(key); setSortAsc(key === 'rank' || key === 'name'); }
  };

  return (
    <div style={s.page}>
      <header style={s.header}>
        <h2 style={s.title}>Priority analysis</h2>
        <p style={s.subtitle}>
          {data
            ? `${data.areaCount} planning areas with resident population data, ranked by priority for intervention.`
            : 'Loading…'}
        </p>
      </header>

      {error && <p style={s.error}>{error}</p>}

      {/* ---- weights ---- */}
      <section style={s.card}>
        <div style={s.cardHead}>
          <h3 style={s.cardTitle}>
            Weights
            <InfoTip text={HELP.weights} theme={theme} />
          </h3>
          {!isDefault && (
            <button onClick={onResetWeights} style={s.linkButton}>Reset to defaults</button>
          )}
        </div>

        <Callout theme={theme}>{HELP.direction}</Callout>

        <div style={s.weightGrid}>
          {metrics.map(m => (
            <div key={m.key} style={s.weightCell}>
              <div style={s.weightTop}>
                <span style={s.weightLabel}>
                  <span style={{ ...s.swatch, background: colourFor(m.key) }} />
                  {m.label}
                </span>
                <span style={s.weightValue}>{(weights[m.key] ?? 0).toFixed(2)}</span>
              </div>
              <input
                type="range" min={0} max={1} step={0.05}
                value={weights[m.key] ?? 0}
                onChange={e => onWeightChange(m.key, Number(e.target.value))}
                style={{ width: '100%', accentColor: colourFor(m.key) }}
                aria-label={`Weight for ${m.label}`}
              />
              <p style={s.weightHint}>
                {m.direction === 'cost'
                  ? 'Higher value lowers priority. '
                  : 'Higher value raises priority. '}
                {m.rationale}
              </p>
              <p style={s.rangeHint}>
                Observed range {fmt(m.observedMin)}–{fmt(m.observedMax)} {m.unit}
              </p>
            </div>
          ))}
        </div>

        <p style={s.sumNote}>
          Weights total {weightSum.toFixed(2)}
          {Math.abs(weightSum - 1) > 0.001 && ' — rescaled to 1 before scoring, so only the relative sizes matter.'}
        </p>

        {data && (
          <div style={s.formulaBox}>
            <span style={s.formulaLabel}>
              Formula in use
              <InfoTip text={HELP.score} theme={theme} />
            </span>
            <code style={s.formula}>{data.formula}</code>
            <p style={s.formulaNote}>{data.normalisation}. {HELP.normalised}</p>
          </div>
        )}

        {data?.warnings.map(w => <p key={w} style={s.warning}>{w}</p>)}
      </section>

      {/* ---- explanation ---- */}
      <ExplanationPanel
        planningAreaId={selectedAreaId}
        areaName={data?.results.find(r => r.planningAreaId === selectedAreaId)?.name ?? null}
        weights={weights}
        theme={theme}
        comparableAreas={data?.results.map(r => ({ id: r.planningAreaId, name: r.name })) ?? []}
      />

      {/* ---- ranked table ---- */}
      <section style={s.card}>
        <div style={s.cardHead}>
          <h3 style={s.cardTitle}>Ranked comparison</h3>
          <span style={s.muted}>Click a row to select · sort by any column</span>
        </div>

        <div style={{ ...s.tableWrap, opacity: loading ? 0.55 : 1 }}>
          <table style={s.table}>
            <thead>
              <tr>
                <th style={s.th} onClick={() => toggleSort('rank')}>#{ind(sortKey === 'rank', sortAsc)}</th>
                <th style={{ ...s.th, textAlign: 'left' }} onClick={() => toggleSort('name')}>
                  Planning area{ind(sortKey === 'name', sortAsc)}
                </th>
                <th style={s.th}>
                  Score<InfoTip text={HELP.score} theme={theme} />
                </th>
                <th style={{ ...s.th, textAlign: 'left', minWidth: 120 }}>Contributions</th>
                <th style={{ ...s.th, textAlign: 'left', minWidth: 170 }}>
                  If weighted differently<InfoTip text={HELP.stability} theme={theme} />
                </th>
                {metrics.map(m => (
                  <th key={m.key} style={s.th} title={`${m.label} (${m.unit})`}
                      onClick={() => toggleSort(m.key)}>
                    {short(m.key)}{ind(sortKey === m.key, sortAsc)}
                  </th>
                ))}
                <th style={s.th} />
              </tr>
            </thead>
            <tbody>
              {rows.map(row => (
                <tr
                  key={row.planningAreaId}
                  onClick={() => onSelectArea(row.planningAreaId)}
                  style={{
                    ...s.tr,
                    background: row.planningAreaId === selectedAreaId ? ACCENT_BG : 'transparent',
                  }}
                >
                  <td style={s.tdRank}>{row.rank}</td>
                  <td style={s.tdName}>
                    <span style={{
                      ...s.dot,
                      background: scoreColour(row.totalScore, scoreRange.min, scoreRange.max),
                    }} />
                    {row.name}
                  </td>
                  <td style={s.tdScore}>{row.totalScore.toFixed(3)}</td>
                  <td style={s.td}>
                    <div style={s.bar}>
                      {row.components.map(comp => (
                        <span key={comp.key} style={{
                          width: `${(comp.contribution / maxScore) * 100}%`,
                          background: colourFor(comp.key),
                          opacity: comp.isImputed ? 0.35 : 1,
                        }} />
                      ))}
                    </div>
                  </td>
                  <td style={{ ...s.td, textAlign: 'left' }}>
                    <StabilityBar
                      stability={stabilityById.get(row.planningAreaId)}
                      totalAreas={data?.areaCount ?? 0}
                      theme={theme}
                    />
                  </td>
                  {metrics.map(m => {
                    const comp = row.components.find(x => x.key === m.key);
                    return (
                      <td key={m.key} style={s.td}>
                        {comp?.rawValue == null
                          ? <span style={s.missing}>—</span>
                          : fmt(comp.rawValue)}
                      </td>
                    );
                  })}
                  <td style={s.td}>
                    <button
                      onClick={e => { e.stopPropagation(); onShowOnMap(row.planningAreaId); }}
                      style={s.mapButton}
                      title={`Show ${row.name} on the map`}
                    >
                      map
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {notes.length > 0 && (
        <section style={s.card}>
          <h3 style={s.cardTitle}>About this score</h3>
          <ul style={s.notesList}>{notes.map(n => <li key={n}>{n}</li>)}</ul>
        </section>
      )}
      <div style={{ height: 40, flexShrink: 0 }} aria-hidden />
    </div>
  );
}

// --- helpers ---------------------------------------------------------------

function rawOf(a: AreaPriorityScore, key: string) {
  return a.components.find(comp => comp.key === key)?.rawValue ?? Number.NEGATIVE_INFINITY;
}

function short(key: string) {
  return ({
    dist_healthcare: 'Clinic (m)',
    pop_density: 'Density',
    facilities_per_10k: 'Per 10k',
    dist_mrt: 'MRT (m)',
  } as Record<string, string>)[key] ?? key;
}

function fmt(v: number | null | undefined) {
  if (v == null) return '—';
  if (Math.abs(v) >= 1000) return Math.round(v).toLocaleString();
  if (Math.abs(v) >= 10) return v.toFixed(1);
  return v.toFixed(2);
}

function ind(active: boolean, asc: boolean) {
  return active ? <span style={{ opacity: 0.6 }}>{asc ? ' ▲' : ' ▼'}</span> : null;
}

const ACCENT = '#4c9fe0';
const ACCENT_BG = 'rgba(76, 159, 224, 0.16)';

const mk = (c: ReturnType<typeof surface>, theme: PanelTheme): Record<string, CSSProperties> => ({
  page: {
    height: '100%', overflowY: 'auto', padding: '20px 24px 40px',
    color: c.text, textAlign: 'left',
    display: 'flex', flexDirection: 'column', gap: 16,
    maxWidth: 1400, margin: '0 auto', width: '100%',
  },
  header: { display: 'flex', flexDirection: 'column', gap: 4, flexShrink: 0 },
  title: { margin: 0, fontSize: 20, fontWeight: 500 },
  subtitle: { margin: 0, fontSize: 13, color: c.textMuted },
  card: {
    background: c.panel, border: `1px solid ${c.border}`,
    borderRadius: 10, padding: '14px 16px',
    display: 'flex', flexDirection: 'column', gap: 10,
    flexShrink:0,
  },
  cardHead: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 },
  cardTitle: { margin: 0, fontSize: 14, fontWeight: 500, display: 'flex', alignItems: 'center' },
  linkButton: {
    background: 'none', border: 'none', color: ACCENT,
    cursor: 'pointer', fontSize: 12, padding: 0,
  },
  muted: { margin: 0, fontSize: 12, color: c.textMuted, lineHeight: 1.55 },
  weightGrid: {
    display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 18,
  },
  weightCell: { display: 'flex', flexDirection: 'column', gap: 5 },
  weightTop: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 },
  weightLabel: { display: 'flex', alignItems: 'center', gap: 7, fontSize: 13 },
  weightValue: { fontSize: 13, fontVariantNumeric: 'tabular-nums' },
  weightHint: { margin: 0, fontSize: 11.5, color: c.textMuted, lineHeight: 1.45 },
  rangeHint: { margin: 0, fontSize: 11, color: c.textMuted, opacity: 0.75 },
  swatch: { width: 9, height: 9, borderRadius: 2, display: 'inline-block', flexShrink: 0 },
  sumNote: { margin: 0, fontSize: 11.5, color: c.textMuted },
  formulaBox: {
    background: c.sunken, borderRadius: 8, padding: 12,
    display: 'flex', flexDirection: 'column', gap: 6,
  },
  formulaLabel: {
    ...sectionStyle(theme), margin: 0, display: 'flex', alignItems: 'center',
  },
  formula: {
    display: 'block',
    fontFamily: 'ui-monospace, Consolas, monospace',
    fontSize: 12,
    lineHeight: 1.7,
    color: c.text,
    background: 'transparent',
    padding: 0,
    wordBreak: 'break-word',
  },
  formulaNote: { margin: 0, fontSize: 11.5, color: c.textMuted, lineHeight: 1.5 },
  headline: { margin: 0, fontSize: 13, lineHeight: 1.55 },
  seedNote: { margin: 0, fontSize: 11, color: c.textMuted, display: 'flex', alignItems: 'center' },
  warning: { margin: 0, fontSize: 11.5, color: '#c9821f' },
  error: { margin: 0, fontSize: 13, color: '#e05252' },
  tableWrap: { overflowX: 'auto', transition: 'opacity 120ms ease' },
  table: { width: '100%', borderCollapse: 'collapse', fontSize: 13 },
  th: {
    textAlign: 'right', padding: '7px 10px', borderBottom: `1px solid ${c.border}`,
    color: c.textMuted, fontWeight: 500, fontSize: 11.5,
    whiteSpace: 'nowrap', cursor: 'pointer', userSelect: 'none',
  },
  tr: { cursor: 'pointer' },
  td: {
    textAlign: 'right', padding: '7px 10px',
    borderBottom: `1px solid ${c.borderSubtle}`,
    fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap',
  },
  tdRank: {
    textAlign: 'right', padding: '7px 10px',
    borderBottom: `1px solid ${c.borderSubtle}`,
    color: c.textMuted, fontVariantNumeric: 'tabular-nums',
  },
  tdName: {
    padding: '7px 10px', borderBottom: `1px solid ${c.borderSubtle}`,
    minWidth: 150, display: 'flex', alignItems: 'center', gap: 8,
  },
  tdScore: {
    textAlign: 'right', padding: '7px 10px',
    borderBottom: `1px solid ${c.borderSubtle}`,
    fontVariantNumeric: 'tabular-nums', fontWeight: 500,
  },
  dot: { width: 8, height: 8, borderRadius: '50%', flexShrink: 0 },
  bar: {
    display: 'flex', height: 4, borderRadius: 2, overflow: 'hidden',
    background: c.sunken, minWidth: 110,
  },
  missing: { color: c.textMuted },
  mapButton: {
    background: 'none', border: `1px solid ${c.border}`, borderRadius: 5,
    color: c.textMuted, cursor: 'pointer', fontSize: 11, padding: '2px 7px',
  },
  notesList: { margin: 0, paddingLeft: 18, lineHeight: 1.7, fontSize: 12.5, color: c.textMuted },
});