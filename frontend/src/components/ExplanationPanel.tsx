import { useCallback, useEffect, useRef, useState } from 'react';
import {
  explain,
  type ExplanationFact,
  type ExplanationMode,
  type ExplanationResult,
  type ExplanationSection,
  type VerificationReport,
} from '../services/explainApi';
import { floatingCard, surface, type PanelTheme } from './panelStyles';

interface ExplanationPanelProps {
  planningAreaId: number | null;
  areaName: string | null;
  weights: Record<string, number> | null;
  theme: PanelTheme;
  comparableAreas: { id: number; name: string }[];
}

type PanelState = 'idle' | 'loading' | 'ready' | 'error';

/**
 * Plain-language explanation of a single area's results.
 *
 * The figures shown here are computed by the database and handed to the model
 * as a closed list; the model rephrases them and calculates nothing. The panel
 * makes that visible rather than asking to be trusted: every figure drawn from
 * that list is marked in the prose, and the full list can be opened underneath.
 */
export default function ExplanationPanel({
  planningAreaId,
  areaName,
  weights,
  theme,
  comparableAreas = [],
}: ExplanationPanelProps) {
  const c = surface(theme);
  const CHIPS: { mode: ExplanationMode; label: string }[] = [
  { mode: 'Area', label: 'Describe this area' },
  { mode: 'Rank', label: 'Why does it rank here?' },
  { mode: 'Sensitivity', label: 'How much do the weights matter?' },
  { mode: 'Compare', label: 'Compare with…' },
];
  const [state, setState] = useState<PanelState>('idle');
  const [result, setResult] = useState<ExplanationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showLedger, setShowLedger] = useState(false);
  const [mode, setMode] = useState<ExplanationMode>('Area');
  const [comparisonId, setComparisonId] = useState<number | null>(null);
  // The weights in force when the current explanation was written, so the panel
  // can mark itself stale rather than describing a scoring run since changed.
  const [writtenFor, setWrittenFor] = useState<string | null>(null);

  const abortRef = useRef<AbortController | null>(null);
  const weightsKey = weights ? JSON.stringify(weights) : 'default';
  const isStale = result !== null && writtenFor !== null && writtenFor !== weightsKey;
  const handleChip = useCallback((next: ExplanationMode) => {
    setMode(next);
    setResult(null);
    setState('idle');
    setError(null);
    if (next !== 'Compare') setComparisonId(null);
  }, []);

  const card = {
    background: c.panel,
    border: `1px solid ${c.border}`,
    borderRadius: 10,
    padding: '14px 16px',
    display: 'flex',
    flexDirection: 'column' as const,
    gap: 10,
    flexShrink: 0,
    textAlign: 'left' as const,
};

  useEffect(() => {
    setState('idle');
    setResult(null);
    setError(null);
    setWrittenFor(null);
  }, [planningAreaId]);

  useEffect(() => () => abortRef.current?.abort(), []);

  const canExplain = planningAreaId != null && (mode !== 'Compare' || comparisonId != null);

  const handleExplain = useCallback(async () => {
    if (planningAreaId == null || !canExplain) return;

    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setState('loading');
    setError(null);

    try {
      const explanation = await explain(
        { planningAreaId, mode, comparisonAreaId: comparisonId, weights },
        controller.signal,
      );
      setResult(explanation);
      setWrittenFor(weightsKey);
      setState('ready');
    } catch (err) {
      if (controller.signal.aborted) return;
      setError(err instanceof Error ? err.message : 'The explanation could not be written.');
      setState('error');
    }
  }, [planningAreaId, mode, comparisonId, weights, weightsKey, canExplain]);

  if (planningAreaId == null) {
    return (
      <div style={ card }>
        <div style={{ ...s.title, color: c.text }}>Explanation</div>
        <div style={{ ...s.muted, color: c.textMuted }}>
          Select an area to explain its results.
        </div>
      </div>
    );
  }

  return (
    <div style={ card }>
      <div style={s.header}>
        <div style={{ ...s.title, color: c.text }}>Explanation</div>
        <button
          onClick={handleExplain}
          disabled={state === 'loading' || !canExplain}
          style={{
            ...s.button,
            background: state === 'loading' ? c.textMuted : ACCENT,
            cursor: state === 'loading' ? 'default' : 'pointer',
          }}
        >
          {state === 'loading'
            ? 'Writing…'
            : result
              ? 'Write again'
              : mode === 'Compare' && comparisonId == null
                ? 'Choose an area'
                : 'Explain'}
        </button>
      </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
          {CHIPS.map(chip => {
            const active = chip.mode === mode;
            return (
              <button
                key={chip.mode}
                onClick={() => handleChip(chip.mode)}
                style={{
                  padding: '4px 10px',
                  borderRadius: 999,
                  border: `1px solid ${active ? ACCENT : c.border}`,
                  background: active ? ACCENT_BG : 'transparent',
                  color: active ? c.text : c.textMuted,
                  fontSize: 11.5,
                  cursor: 'pointer',
                }}
              >
                {chip.label}
              </button>
            );
          })}
      </div>

      {mode === 'Compare' && (
        <select
          value={comparisonId ?? ''}
          onChange={e => setComparisonId(e.target.value ? Number(e.target.value) : null)}
          style={{
            alignSelf: 'flex-start',
            padding: '4px 8px',
            borderRadius: 5,
            border: `1px solid ${c.border}`,
            background: c.panel,
            color: c.text,
            fontSize: 12,
          }}
        >
          <option value="">Compare with…</option>
          {comparableAreas
            .filter(a => a.id !== planningAreaId)
            .map(a => (
              <option key={a.id} value={a.id}>{a.name}</option>
            ))}
        </select>
      )}
      <div style={{ ...s.muted, color: c.textMuted }}>
        Every figure below is computed by the database. The model rephrases them
        and calculates nothing of its own.
      </div>

      {state === 'loading' && (
        <div style={{ ...s.body, color: c.textMuted }} role="status">
          Writing an explanation for {areaName ?? 'this area'}…
        </div>
      )}

      {state === 'error' && (
        <div style={{ ...s.notice, background: '#fdeaea', color: '#8a1c1c' }} role="alert">
          {error}
        </div>
      )}

      {state === 'ready' && result && (
        <>
          {isStale && (
            <div style={{ ...s.notice, background: '#fdf3d8', color: '#7a5a10' }}>
              The weights have changed since this was written. Write again to bring it up to date.
            </div>
          )}

          {result.source === 'Template' && (
            <div style={{ ...s.notice, background: '#e6ecfa', color: '#2a3d6b' }}>
              {result.fallbackReason ?? 'Written without the model. The figures are identical.'}
            </div>
          )}

          {result.verification && <Verification report={result.verification} />}

          {result.sections.map(section => (
            <Section
              key={section.heading}
              section={section}
              facts={result.payload.facts}
              theme={theme}
            />
          ))}

          <button
            onClick={() => setShowLedger(v => !v)}
            aria-expanded={showLedger}
            style={{ ...s.link, color: ACCENT }}
          >
            {showLedger
              ? 'Hide the figures'
              : `Show all ${result.payload.facts.length} figures the model was given`}
          </button>

          {showLedger && <Ledger result={result} theme={theme} />}
        </>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------

function Verification({ report }: { report: VerificationReport }) {
  const flagged = report.outcome === 'Flagged';

  return (
    <div
      style={{
        ...s.notice,
        background: flagged ? '#fdeaea' : '#e8f6ec',
        color: flagged ? '#8a1c1c' : '#1e5b32',
        borderLeft: `3px solid ${flagged ? '#c0392b' : '#2e9e51'}`,
      }}
    >
      <strong>{flagged ? 'Some figures could not be traced' : 'Figures checked'}</strong>
      <div style={{ marginTop: 2 }}>{report.summary}</div>

      {flagged && (
        <ul style={{ margin: '6px 0 0', paddingLeft: 16 }}>
          {report.findings.map((f, i) => (
            <li key={i}>{f.message}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

function Section({
  section,
  facts,
  theme,
}: {
  section: ExplanationSection;
  facts: ExplanationFact[];
  theme: 'light' | 'dark';
}) {
  const c = surface(theme);
  const cited = facts.filter(f => section.citedFactIds.includes(f.id));

  return (
    <div>
      <div style={{ ...s.heading, color: c.text }}>{section.heading}</div>
      <div style={{ ...s.body, color: c.text }}>{mark(section.body, cited, theme)}</div>
    </div>
  );
}

/**
 * Marks each supplied value where it appears in the prose. Longest first, so
 * "16 of 38" wins over "16".
 *
 * This is a reading aid, not the check: it matches literal strings, so a figure
 * the model reworded goes unmarked. Verification is the badge above.
 */
function mark(body: string, facts: ExplanationFact[], theme: 'light' | 'dark') {
  const values = facts
    .map(f => f.value)
    .filter(v => v.length > 0)
    .sort((a, b) => b.length - a.length);

  if (values.length === 0) return body;

  const pattern = new RegExp(
    `(${values.map(v => v.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`,
    'g',
  );

  return body.split(pattern).map((part, i) =>
    values.includes(part) ? (
      <span
        key={i}
        style={{
          background: theme === 'dark' ? '#1d3a5c' : '#dceaf9',
          borderRadius: 2,
          padding: '0 2px',
          fontVariantNumeric: 'tabular-nums',
        }}
      >
        {part}
      </span>
    ) : (
      part
    ),
  );
}

function Ledger({ result, theme }: { result: ExplanationResult; theme: 'light' | 'dark' }) {
  const c = surface(theme);
  const { payload } = result;

  return (
    <div style={{ ...s.muted, color: c.textMuted, display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div>Nothing outside this list can appear in the text above.</div>

      <Rows facts={payload.facts} theme={theme} />

      {payload.weights.length > 0 && (
        <>
          <div style={{ ...s.heading, color: c.text, fontSize: 11.5 }}>Weights in force</div>
          <Rows facts={payload.weights} theme={theme} />
        </>
      )}

        <div style={{
            background: c.sunken,
            borderRadius: 8,
            padding: 10,
            fontFamily: 'ui-monospace, Consolas, monospace',
            fontSize: 11,
            lineHeight: 1.6,
            color: c.text,
            wordBreak: 'break-word',
        }}>
        {payload.method.formula}
        {payload.method.sensitivitySeed != null &&
          ` · ${payload.method.sensitivitySamples?.toLocaleString()} samples, seed ${payload.method.sensitivitySeed}`}
      </div>
    </div>
  );
}

function Rows({ facts, theme }: { facts: ExplanationFact[]; theme: 'light' | 'dark' }) {
  const c = surface(theme);

  return (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: '2px 12px' }}>
      {facts.map(f => (
        <div key={f.id} style={{ display: 'contents' }}>
          <div>{f.label}</div>
          <div style={{ color: c.text, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
            {f.value}
          </div>
        </div>
      ))}
    </div>
  );
}

const ACCENT = '#4c9fe0';
const ACCENT_BG = 'rgba(76, 159, 224, 0.16)';

const s = {
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  title: { fontSize: 13, fontWeight: 600 },
  heading: { fontSize: 12.5, fontWeight: 600, marginBottom: 3 },
  body: { fontSize: 12.5, lineHeight: 1.6 },
  muted: { fontSize: 11, lineHeight: 1.45 },
  notice: { padding: '7px 9px', borderRadius: 4, fontSize: 11.5, lineHeight: 1.45 },
  button: {
    padding: '5px 10px',
    border: 0,
    borderRadius: 4,
    color: '#fff',
    fontSize: 11.5,
    fontWeight: 500,
  },
  link: {
    alignSelf: 'flex-start' as const,
    padding: 0,
    border: 0,
    background: 'none',
    fontSize: 11,
    textDecoration: 'underline',
    cursor: 'pointer',
  },
};