import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import {
  apiAuthInterceptor,
  LocalSessionRequiredError,
} from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  DashboardApi,
  DashboardFilters,
  DashboardPayloadError,
  DashboardSummary,
} from './dashboard-api';

const API_BASE = 'https://api.example.test/proxy/';
const DASHBOARD_URL = `${API_BASE}api/dashboard`;

let backend: HttpTestingController;
let api: DashboardApi;
let session: SessionService;

// Configura transporte HTTP sintético com a mesma base e o interceptor de sessão da aplicação.
function configureApi(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  api = TestBed.inject(DashboardApi);
  session = TestBed.inject(SessionService);
}

// Inicia uma sessão artificial para conferir geração e Bearer sem usar credenciais reais.
function startSession(): number {
  return session.startSession({
    accessToken: 'synthetic-dashboard-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  })!;
}

// Impede que a consulta protegida passe a emitir POST ou alcance rotas de sincronização.
function expectNoSynchronizationRequests(): void {
  backend.expectNone((request) => request.method !== 'GET' || request.url.toLowerCase().includes('/sync'));
}

// Contrato consumido: seis naturezas e quatro situações únicas (incluindo Unknown e zeros),
// filteredCount coerente nos dois eixos, dateBasis TransactionDate e cobertura com sucesso/janela/execução separados.
function dashboardResponse(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    filteredCount: 3,
    countsByNature: [
      { nature: 'Purchase', count: 1 },
      { nature: 'Refund', count: 0 },
      { nature: 'BillPayment', count: 0 },
      { nature: 'Fee', count: 0 },
      { nature: 'Adjustment', count: 0 },
      { nature: 'Unknown', count: 2 },
    ],
    countsBySituation: [
      { situation: 'Effective', count: 1 },
      { situation: 'Pending', count: 1 },
      { situation: 'FutureInstallment', count: 0 },
      { situation: 'Unknown', count: 1 },
    ],
    coverage: {
      lastSuccessfulImportAtUtc: '2026-11-24T10:00:00Z',
      lastSuccessfulWindow: {
        requestedFrom: '2026-10-01',
        requestedToExclusive: '2026-12-01',
        observedFrom: '2026-10-02',
        observedTo: null,
        coverageState: 'Partial',
      },
      latestExecution: {
        state: 'Failed',
        coverageState: 'Unknown',
        failureCode: 'PROVIDER_UNAVAILABLE',
      },
    },
    dateBasis: 'TransactionDate',
    ...overrides,
  };
}

// Leva uma resposta sintética pelo mesmo decoder HTTP e captura somente resultado ou erro projetado.
function readDashboard(payload: Record<string, unknown>): {
  readonly result: { readonly generation: number; readonly value: DashboardSummary } | undefined;
  readonly error: unknown;
} {
  let result: { generation: number; value: DashboardSummary } | undefined;
  let receivedError: unknown;

  api.getDashboard({}).subscribe({
    next: (response) => (result = response),
    error: (error: unknown) => (receivedError = error),
  });
  backend.expectOne((candidate) => candidate.url === DASHBOARD_URL).flush(payload);

  return { result, error: receivedError };
}

// O cliente usa somente a rota allowlist do dashboard e nunca abre sockets reais.
describe('Cliente do resumo descritivo', () => {
  beforeEach(() => configureApi());

  afterEach(() => {
    expectNoSynchronizationRequests();
    backend.verify();
  });

  // A query contém exatamente os seis filtros aplicados, sem paginação nem propriedades extras.
  it('consulta o dashboard protegido com filtros allowlist e preserva a geração iniciadora', () => {
    const generation = startSession();
    const search = 'café 20%_\\literal+';
    const filters = {
      cardId: 'card-synthetic',
      categoryId: 'category-synthetic',
      situation: 'Pending',
      from: '2026-11-01',
      toExclusive: '2026-12-01',
      search,
      page: 4,
      pageSize: 100,
    } as DashboardFilters;
    const payload = dashboardResponse();
    let result: { generation: number; value: DashboardSummary } | undefined;

    api.getDashboard(filters).subscribe((response) => (result = response));
    const request = backend.expectOne((candidate) => candidate.url === DASHBOARD_URL);

    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-dashboard-token');
    expect(request.request.params.keys().sort()).toEqual(
      ['cardId', 'categoryId', 'situation', 'from', 'toExclusive', 'search'].sort(),
    );
    expect(request.request.params.get('search')).toBe(search);
    expect(request.request.params.has('page')).toBe(false);
    expect(request.request.params.has('pageSize')).toBe(false);
    for (const name of ['cardId', 'categoryId', 'situation', 'from', 'toExclusive'] as const) {
      expect(request.request.params.get(name)).toBe(filters[name]);
    }
    request.flush(payload);

    expect(result?.generation).toBe(generation);
    expect(result?.value.filteredCount).toBe(3);
    expect(result?.value.dateBasis).toBe('TransactionDate');
    expect(result?.value.countsByNature.find((bucket) => bucket.nature === 'Unknown')?.count).toBe(2);
    expect(result?.value.countsBySituation.find((bucket) => bucket.situation === 'Unknown')?.count).toBe(1);
    expect(result?.value.coverage.latestExecution?.state).toBe('Failed');
    expect(JSON.stringify(result)).not.toContain('amount');
  });

  // Sem sessão local, a pré-condição falha antes de produzir qualquer chamada HTTP.
  it('recusa a leitura sem sessão e não envia request', () => {
    let receivedError: unknown;

    api.getDashboard({}).subscribe({ error: (error: unknown) => (receivedError = error) });

    expect(receivedError).toBeInstanceOf(LocalSessionRequiredError);
    backend.expectNone(() => true);
  });

  // Grupos conhecidos com zero são válidos quando os dois eixos correspondem ao conjunto vazio.
  it('aceita todos os grupos zerados e cobertura ainda desconhecida', () => {
    startSession();
    const natureCodes = ['Purchase', 'Refund', 'BillPayment', 'Fee', 'Adjustment', 'Unknown'];
    const situationCodes = ['Effective', 'Pending', 'FutureInstallment', 'Unknown'];
    const payload = dashboardResponse({
      filteredCount: 0,
      countsByNature: natureCodes.map((nature) => ({ nature, count: 0 })),
      countsBySituation: situationCodes.map((situation) => ({ situation, count: 0 })),
      coverage: {
        lastSuccessfulImportAtUtc: null,
        lastSuccessfulWindow: null,
        latestExecution: null,
      },
    });

    const { result, error } = readDashboard(payload);

    expect(error).toBeUndefined();
    expect(result?.value.filteredCount).toBe(0);
    expect(result?.value.countsByNature).toHaveLength(6);
    expect(result?.value.countsBySituation).toHaveLength(4);
    expect(result?.value.coverage).toEqual({
      lastSuccessfulImportAtUtc: null,
      lastSuccessfulWindow: null,
      latestExecution: null,
    });
  });

  // Códigos de natureza/situação novos não são tratados como enum conhecido ou texto de apresentação.
  it('rejeita enum desconhecido em um eixo', () => {
    startSession();
    const payload = dashboardResponse();
    (payload['countsByNature'] as Array<Record<string, unknown>>)[0] = {
      nature: 'Withdrawal',
      count: 1,
    };

    const { result, error } = readDashboard(payload);

    expect(result).toBeUndefined();
    expect(error).toBeInstanceOf(DashboardPayloadError);
  });

  // O eixo temporal é sempre o dia técnico do movimento e não aceita um significado substituto.
  it('rejeita dateBasis diferente de TransactionDate', () => {
    startSession();
    const payload = dashboardResponse({ dateBasis: 'BillingCycle' });

    const { result, error } = readDashboard(payload);

    expect(result).toBeUndefined();
    expect(error).toBeInstanceOf(DashboardPayloadError);
  });

  // Propriedades adicionais, inclusive valores e payloads remotos, invalidam a allowlist sem ecoar conteúdo.
  it('rejeita campo monetário/desconhecido sem expor corpo sensível', () => {
    startSession();
    const privateValue = 'synthetic-private-provider-payload';
    const payload = dashboardResponse({ amount: 450.75, providerPayload: privateValue });

    const { result, error } = readDashboard(payload);

    expect(result).toBeUndefined();
    expect(error).toBeInstanceOf(DashboardPayloadError);
    expect((error as Error).message).not.toContain(privateValue);
    expect(JSON.stringify(error)).not.toContain(privateValue);
  });

  // As duas distribuições precisam totalizar o mesmo filteredCount do universo, não um eixo ou página.
  it('rejeita contagens incoerentes com filteredCount', () => {
    startSession();
    const payload = dashboardResponse({ filteredCount: 4 });

    const { result, error } = readDashboard(payload);

    expect(result).toBeUndefined();
    expect(error).toBeInstanceOf(DashboardPayloadError);
  });

  // Estados operacionais e de cobertura são fechados para evitar interpretar códigos futuros como sucesso.
  it('rejeita enum desconhecido na cobertura', () => {
    startSession();
    const payload = dashboardResponse();
    const coverage = payload['coverage'] as Record<string, unknown>;
    coverage['latestExecution'] = {
      state: 'Failed',
      coverageState: 'CompleteForever',
      failureCode: 'PROVIDER_UNAVAILABLE',
    };

    const { result, error } = readDashboard(payload);

    expect(result).toBeUndefined();
    expect(error).toBeInstanceOf(DashboardPayloadError);
  });
});
