import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { defer, map, Observable, throwError } from 'rxjs';
import { NORMALIZED_API_BASE_URL, NormalizedApiBaseUrl } from '../../api-base-url.token';
import { LocalSessionRequiredError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { SessionScopedResponse } from './card-connection-api';

/** Restringe a consulta aos seis filtros compartilhados com a lista, sem campos de paginação. */
export interface DashboardFilters {
  readonly cardId?: string;
  readonly categoryId?: string;
  readonly situation?: string;
  readonly from?: string;
  readonly toExclusive?: string;
  readonly search?: string;
}

/** Limita naturezas ao vocabulário publicado, inclusive a classificação explicitamente desconhecida. */
export type DashboardNature =
  | 'Purchase'
  | 'Refund'
  | 'BillPayment'
  | 'Fee'
  | 'Adjustment'
  | 'Unknown';

/** Mantém situação independente da natureza e não a interpreta como confirmação financeira. */
export type DashboardSituation = 'Effective' | 'Pending' | 'FutureInstallment' | 'Unknown';

/** Representa somente os estados de cobertura publicados pelo contrato do backend. */
export type DashboardCoverageState = 'Unknown' | 'Partial' | 'CompleteForRequestedWindow';

/** Representa somente os estados operacionais da execução mais recente. */
export type DashboardExecutionState = 'Pending' | 'Running' | 'Succeeded' | 'Failed';

/** Mantém a lista de códigos de falha limitada à allowlist pública da API. */
export type DashboardFailureCode =
  | 'PROVIDER_UNAVAILABLE'
  | 'RATE_LIMITED'
  | 'AUTH_REQUIRED'
  | 'INVALID_RESPONSE'
  | 'PROVIDER_TIMEOUT'
  | 'PERSISTENCE_FAILED'
  | 'CANCELLED'
  | 'UNKNOWN';

/** Uma faixa agrega somente quantos movimentos ocupam um código de natureza conhecido. */
export interface DashboardNatureCount {
  readonly nature: DashboardNature;
  readonly count: number;
}

/** Uma faixa agrega somente quantos movimentos ocupam um código de situação conhecido. */
export interface DashboardSituationCount {
  readonly situation: DashboardSituation;
  readonly count: number;
}

/** Descreve a janela técnica confirmada sem inferir cobertura fora do intervalo solicitado. */
export interface DashboardLastSuccessfulWindow {
  readonly requestedFrom: string;
  readonly requestedToExclusive: string | null;
  readonly observedFrom: string | null;
  readonly observedTo: string | null;
  readonly coverageState: DashboardCoverageState;
}

/** Mantém a execução recente separada do sucesso e da janela anteriormente confirmados. */
export interface DashboardLatestExecution {
  readonly state: DashboardExecutionState;
  readonly coverageState: DashboardCoverageState;
  readonly failureCode: DashboardFailureCode | null;
}

/** Reúne fatos independentes de sucesso, janela confirmada e execução mais recente. */
export interface DashboardCoverage {
  readonly lastSuccessfulImportAtUtc: string | null;
  readonly lastSuccessfulWindow: DashboardLastSuccessfulWindow | null;
  readonly latestExecution: DashboardLatestExecution | null;
}

/** Contrato completo do GET sem itens, filtros, valores individuais ou agregados monetários. */
export interface DashboardSummary {
  readonly filteredCount: number;
  readonly countsByNature: readonly DashboardNatureCount[];
  readonly countsBySituation: readonly DashboardSituationCount[];
  readonly coverage: DashboardCoverage;
  readonly dateBasis: 'TransactionDate';
}

/** Sinaliza ausência de configuração pública sem inferir uma origem alternativa para a API. */
export class DashboardApiConfigurationError extends Error {
  constructor() {
    super('A consulta está indisponível porque a API não está configurada neste ambiente.');
    this.name = 'DashboardApiConfigurationError';
  }
}

// Erro fixo para resposta incompatível; nenhuma mensagem ou corpo remoto é propagado à tela.
export class DashboardPayloadError extends Error {
  constructor() {
    super('A API retornou dados de resumo em formato não reconhecido.');
    this.name = 'DashboardPayloadError';
  }
}

// Mantém a leitura do resumo em uma rota única, protegida e sem qualquer intenção de sincronização.
@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(NORMALIZED_API_BASE_URL);
  private readonly session = inject(SessionService);

  // Envia somente filtros aplicados e decodifica o resumo antes de expô-lo ao estado visual.
  getDashboard(filters: DashboardFilters): Observable<SessionScopedResponse<DashboardSummary>> {
    return defer(() => {
      const activeSession = this.session.currentSession();
      if (activeSession === null) {
        return throwError(() => new LocalSessionRequiredError());
      }

      if (this.apiBase === null) {
        return throwError(() => new DashboardApiConfigurationError());
      }

      const url = buildApiUrl(this.apiBase, 'dashboard');
      return this.http.get<unknown>(url, { params: createDashboardParams(filters) }).pipe(
        map((payload) => ({
          generation: activeSession.generation,
          value: parseDashboardSummary(payload),
        })),
      );
    });
  }
}

// Vocabulários fechados conforme os mapeamentos HTTP, sem converter enums ou códigos novos.
const DASHBOARD_NATURES: readonly DashboardNature[] = [
  'Purchase',
  'Refund',
  'BillPayment',
  'Fee',
  'Adjustment',
  'Unknown',
];

const DASHBOARD_SITUATIONS: readonly DashboardSituation[] = [
  'Effective',
  'Pending',
  'FutureInstallment',
  'Unknown',
];

const DASHBOARD_COVERAGE_STATES: readonly DashboardCoverageState[] = [
  'Unknown',
  'Partial',
  'CompleteForRequestedWindow',
];

const DASHBOARD_EXECUTION_STATES: readonly DashboardExecutionState[] = [
  'Pending',
  'Running',
  'Succeeded',
  'Failed',
];

const DASHBOARD_FAILURE_CODES: readonly DashboardFailureCode[] = [
  'PROVIDER_UNAVAILABLE',
  'RATE_LIMITED',
  'AUTH_REQUIRED',
  'INVALID_RESPONSE',
  'PROVIDER_TIMEOUT',
  'PERSISTENCE_FAILED',
  'CANCELLED',
  'UNKNOWN',
];

// Serializa uma lista fixa de nomes para que campos extras nunca virem query parameters.
function createDashboardParams(filters: DashboardFilters): HttpParams {
  const allowedNames: readonly (keyof DashboardFilters)[] = [
    'cardId',
    'categoryId',
    'situation',
    'from',
    'toExclusive',
    'search',
  ];
  let params = new HttpParams();
  for (const name of allowedNames) {
    const value = filters[name];
    if (value !== undefined) {
      params = params.set(name, value);
    }
  }

  return params;
}

// Usa a base normalizada do build e preserva eventual prefixo de proxy sem fallback relativo.
function buildApiUrl(apiBase: NormalizedApiBaseUrl, route: string): string {
  const apiPath = apiBase.pathPrefix.toLowerCase().endsWith('/api')
    ? apiBase.pathPrefix
    : `${apiBase.pathPrefix}/api`;
  return new URL(`${apiPath}/${route}`, apiBase.origin).toString();
}

// Valida o envelope exato e exige que os dois eixos independentes totalizem o universo filtrado.
function parseDashboardSummary(payload: unknown): DashboardSummary {
  const summary = asRecord(payload);
  if (
    summary === null ||
    !hasExactKeys(summary, [
      'filteredCount',
      'countsByNature',
      'countsBySituation',
      'coverage',
      'dateBasis',
    ]) ||
    !isNonNegativeSafeInteger(summary['filteredCount']) ||
    !Array.isArray(summary['countsByNature']) ||
    !Array.isArray(summary['countsBySituation']) ||
    summary['dateBasis'] !== 'TransactionDate'
  ) {
    throw new DashboardPayloadError();
  }

  const countsByNature = parseNatureCounts(summary['countsByNature']);
  const countsBySituation = parseSituationCounts(summary['countsBySituation']);
  const natureTotal = sumCounts(countsByNature);
  const situationTotal = sumCounts(countsBySituation);
  if (
    natureTotal === null ||
    situationTotal === null ||
    natureTotal !== summary['filteredCount'] ||
    situationTotal !== summary['filteredCount']
  ) {
    throw new DashboardPayloadError();
  }

  return {
    filteredCount: summary['filteredCount'],
    countsByNature,
    countsBySituation,
    coverage: parseDashboardCoverage(summary['coverage']),
    dateBasis: 'TransactionDate',
  };
}

// Projeta todas as naturezas conhecidas, sem aceitar códigos repetidos, ausentes ou futuros.
function parseNatureCounts(payload: readonly unknown[]): readonly DashboardNatureCount[] {
  const counts = payload.map((item) => {
    const bucket = asRecord(item);
    if (
      bucket === null ||
      !hasExactKeys(bucket, ['nature', 'count']) ||
      !isDashboardNature(bucket['nature']) ||
      !isNonNegativeSafeInteger(bucket['count'])
    ) {
      throw new DashboardPayloadError();
    }

    return { nature: bucket['nature'], count: bucket['count'] };
  });

  if (!hasExactlyCodes(counts.map((bucket) => bucket.nature), DASHBOARD_NATURES)) {
    throw new DashboardPayloadError();
  }
  return counts;
}

// Projeta todas as situações separadas da natureza e não atribui significado financeiro aos códigos.
function parseSituationCounts(payload: readonly unknown[]): readonly DashboardSituationCount[] {
  const counts = payload.map((item) => {
    const bucket = asRecord(item);
    if (
      bucket === null ||
      !hasExactKeys(bucket, ['situation', 'count']) ||
      !isDashboardSituation(bucket['situation']) ||
      !isNonNegativeSafeInteger(bucket['count'])
    ) {
      throw new DashboardPayloadError();
    }

    return { situation: bucket['situation'], count: bucket['count'] };
  });

  if (!hasExactlyCodes(counts.map((bucket) => bucket.situation), DASHBOARD_SITUATIONS)) {
    throw new DashboardPayloadError();
  }
  return counts;
}

// Rejeita estrutura adicional e estados desconhecidos em cada dimensão da cobertura.
function parseDashboardCoverage(payload: unknown): DashboardCoverage {
  const coverage = asRecord(payload);
  if (
    coverage === null ||
    !hasExactKeys(coverage, [
      'lastSuccessfulImportAtUtc',
      'lastSuccessfulWindow',
      'latestExecution',
    ]) ||
    !isNullableTimestamp(coverage['lastSuccessfulImportAtUtc'])
  ) {
    throw new DashboardPayloadError();
  }

  return {
    lastSuccessfulImportAtUtc: coverage['lastSuccessfulImportAtUtc'],
    lastSuccessfulWindow:
      coverage['lastSuccessfulWindow'] === null
        ? null
        : parseLastSuccessfulWindow(coverage['lastSuccessfulWindow']),
    latestExecution:
      coverage['latestExecution'] === null
        ? null
        : parseLatestExecution(coverage['latestExecution']),
  };
}

// Preserva datas solicitadas/observadas sem normalizar valores ausentes ou estados de cobertura.
function parseLastSuccessfulWindow(payload: unknown): DashboardLastSuccessfulWindow {
  const window = asRecord(payload);
  if (
    window === null ||
    !hasExactKeys(window, [
      'requestedFrom',
      'requestedToExclusive',
      'observedFrom',
      'observedTo',
      'coverageState',
    ]) ||
    !isDateOnly(window['requestedFrom']) ||
    !isNullableDateOnly(window['requestedToExclusive']) ||
    !isNullableDateOnly(window['observedFrom']) ||
    !isNullableDateOnly(window['observedTo']) ||
    !isDashboardCoverageState(window['coverageState'])
  ) {
    throw new DashboardPayloadError();
  }

  return {
    requestedFrom: window['requestedFrom'],
    requestedToExclusive: window['requestedToExclusive'],
    observedFrom: window['observedFrom'],
    observedTo: window['observedTo'],
    coverageState: window['coverageState'],
  };
}

// Mantém apenas estado operacional e código de falha conhecidos da execução mais recente.
function parseLatestExecution(payload: unknown): DashboardLatestExecution {
  const execution = asRecord(payload);
  if (
    execution === null ||
    !hasExactKeys(execution, ['state', 'coverageState', 'failureCode']) ||
    !isDashboardExecutionState(execution['state']) ||
    !isDashboardCoverageState(execution['coverageState']) ||
    !isNullableFailureCode(execution['failureCode'])
  ) {
    throw new DashboardPayloadError();
  }

  return {
    state: execution['state'],
    coverageState: execution['coverageState'],
    failureCode: execution['failureCode'],
  };
}

// Aceita somente objetos simples para impedir que arrays ou null sejam tratados como contratos.
function asRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null;
  }
  return value as Record<string, unknown>;
}

// A allowlist de propriedades rejeita campos financeiros ou versões de contrato ainda desconhecidas.
function hasExactKeys(record: Record<string, unknown>, allowed: readonly string[]): boolean {
  const keys = Object.keys(record);
  return (
    keys.length === allowed.length &&
    allowed.every((key) => Object.prototype.hasOwnProperty.call(record, key))
  );
}

// Exige um item único para cada código publicado, inclusive os grupos válidos com contagem zero.
function hasExactlyCodes(actual: readonly string[], expected: readonly string[]): boolean {
  return (
    actual.length === expected.length &&
    new Set(actual).size === expected.length &&
    expected.every((code) => actual.includes(code))
  );
}

// Soma contagens sem permitir overflow nem usar aritmética insegura para validar coerência.
function sumCounts(counts: readonly { readonly count: number }[]): number | null {
  let total = 0;
  for (const bucket of counts) {
    const next = total + bucket.count;
    if (!Number.isSafeInteger(next)) {
      return null;
    }
    total = next;
  }
  return total;
}

// Limita cada código de enum aos valores textuais expressamente mapeados pelo backend.
function isDashboardNature(value: unknown): value is DashboardNature {
  return typeof value === 'string' && DASHBOARD_NATURES.includes(value as DashboardNature);
}

// Mantém o eixo de situação fechado para que um enum futuro não seja exibido como classificação conhecida.
function isDashboardSituation(value: unknown): value is DashboardSituation {
  return typeof value === 'string' && DASHBOARD_SITUATIONS.includes(value as DashboardSituation);
}

// Não presume que um novo estado de cobertura equivale a sucesso completo.
function isDashboardCoverageState(value: unknown): value is DashboardCoverageState {
  return (
    typeof value === 'string' &&
    DASHBOARD_COVERAGE_STATES.includes(value as DashboardCoverageState)
  );
}

// Limita a execução aos estados operacionais publicados sem traduzir enums numéricos.
function isDashboardExecutionState(value: unknown): value is DashboardExecutionState {
  return (
    typeof value === 'string' &&
    DASHBOARD_EXECUTION_STATES.includes(value as DashboardExecutionState)
  );
}

// O código nulo é permitido; qualquer código presente precisa pertencer à allowlist do backend.
function isNullableFailureCode(value: unknown): value is DashboardFailureCode | null {
  return (
    value === null ||
    (typeof value === 'string' && DASHBOARD_FAILURE_CODES.includes(value as DashboardFailureCode))
  );
}

// Datas de cobertura são strings explícitas de ISO 8601 com fuso, sem completar o relógio no cliente.
function isNullableTimestamp(value: unknown): value is string | null {
  return (
    value === null ||
    (typeof value === 'string' &&
      /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/i.test(value) &&
      Number.isFinite(Date.parse(value)))
  );
}

// Datas DateOnly devem existir no calendário e manter literalmente o formato AAAA-MM-DD.
function isDateOnly(value: unknown): value is string {
  if (typeof value !== 'string') {
    return false;
  }

  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (parts === null || Number(parts[1]) < 1) {
    return false;
  }

  const timestamp = Date.parse(`${value}T00:00:00Z`);
  if (!Number.isFinite(timestamp)) {
    return false;
  }

  const parsed = new Date(timestamp);
  return (
    parsed.getUTCFullYear() === Number(parts[1]) &&
    parsed.getUTCMonth() + 1 === Number(parts[2]) &&
    parsed.getUTCDate() === Number(parts[3])
  );
}

// Janela ausente é nula; quando presente, deve manter o formato DateOnly válido do endpoint.
function isNullableDateOnly(value: unknown): value is string | null {
  return value === null || isDateOnly(value);
}

// Contagens e totais precisam ser inteiros não negativos que não percam precisão no JavaScript.
function isNonNegativeSafeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
