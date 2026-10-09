import api from '../services/api';

export type ExplanationMode = 'Area' | 'Rank' | 'Compare' | 'Sensitivity';

export interface ExplainRequest {
  planningAreaId: number;
  mode: ExplanationMode;
  comparisonAreaId?: number | null;
  weights?: Record<string, number> | null;
}

export interface ExplanationFact {
  id: string;
  label: string;
  value: string;
  kind: string;
}

export interface ExplanationSection {
  heading: string;
  body: string;
  citedFactIds: string[];
  isVerbatim: boolean;
}

export interface VerificationFinding {
  severity: 'Error' | 'Warning';
  kind: string;
  text: string;
  message: string;
  start?: number;
  length?: number;
}

export interface VerificationReport {
  outcome: 'Verified' | 'Flagged';
  figuresChecked: number;
  figuresMatched: number;
  ledgerSize: number;
  findings: VerificationFinding[];
  summary: string;
}

export interface ExplanationPayload {
  mode: string;
  subject: {
    name: string;
    region: string;
    isScored: boolean;
    exclusionReason: string | null;
  };
  facts: ExplanationFact[];
  weights: ExplanationFact[];
  method: {
    formula: string;
    normalisation: string;
    scoreDirection: string;
    scoredAreaCount: number;
    sensitivitySamples: number | null;
    sensitivitySeed: number | null;
  };
  caveats: string[];
}

export interface ExplanationResult {
  source: 'Model' | 'Template';
  sections: ExplanationSection[];
  payload: ExplanationPayload;
  verification: VerificationReport | null;
  fallbackReason: string | null;
}

export async function explain(
  request: ExplainRequest,
  signal?: AbortSignal,
): Promise<ExplanationResult> {
  try {
    const response = await api.post<ExplanationResult>('/api/explain/area', request, { signal });
    return response.data;
  } catch (err) {
    const message = (err as { response?: { data?: { message?: string } } })
      ?.response?.data?.message;
    throw new Error(message ?? 'The explanation could not be written.');
  }
}