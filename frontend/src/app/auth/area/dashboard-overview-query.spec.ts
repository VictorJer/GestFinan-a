import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { DashboardOverviewQueryService } from './dashboard-overview-query';
import { TransactionListStateService } from './transaction-list-state';

const API_BASE = 'https://api.example.test/proxy/';
const TRANSACTIONS_URL = `${API_BASE}api/transactions`;
const DASHBOARD_URL = `${API_BASE}api/dashboard`;

let backend: HttpTestingController;
let query: DashboardOverviewQueryService;
let listState: TransactionListStateService;
let session: SessionService;

// Compõe os serviços reais e o interceptor com respostas HTTP sintéticas para não alcançar API externa.
function configureQuery(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  query = TestBed.inject(DashboardOverviewQueryService);
  listState = TestBed.inject(TransactionListStateService);
  session = TestBed.inject(SessionService);
}

// Cria uma geração de sessão fictícia para que lista e resumo atravessem o mesmo interceptor protegido.
function startSession(): void {
  session.startSession({
    accessToken: 'synthetic-overview-query-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  });
}

// Resposta mínima válida da lista mantém filtros aplicados sem depender de movimentos reais.
function transactionPageResponse(): Record<string, unknown> {
  return {
    items: [],
    page: 1,
    pageSize: 25,
    filteredCount: 0,
    coverage: {
      lastSuccessfulImportAtUtc: null,
      lastSuccessfulWindow: null,
      latestExecution: null,
    },
  };
}

// Resposta allowlisted com grupos zero confirma que um acesso direto sem filtros não inventa dados.
function emptyDashboardResponse(): Record<string, unknown> {
  return {
    filteredCount: 0,
    countsByNature: [
      { nature: 'Purchase', count: 0 },
      { nature: 'Refund', count: 0 },
      { nature: 'BillPayment', count: 0 },
      { nature: 'Fee', count: 0 },
      { nature: 'Adjustment', count: 0 },
      { nature: 'Unknown', count: 0 },
    ],
    countsBySituation: [
      { situation: 'Effective', count: 0 },
      { situation: 'Pending', count: 0 },
      { situation: 'FutureInstallment', count: 0 },
      { situation: 'Unknown', count: 0 },
    ],
    coverage: {
      lastSuccessfulImportAtUtc: null,
      lastSuccessfulWindow: null,
      latestExecution: null,
    },
    dateBasis: 'TransactionDate',
  };
}

// Cada cenário limita o cliente à leitura GET e exclui qualquer rota/ação de sincronização.
describe('Consulta da visão com filtros aplicados da lista', () => {
  beforeEach(() => configureQuery());

  afterEach(() => {
    backend.expectNone(
      (request) => request.method !== 'GET' || request.url.toLowerCase().includes('/sync'),
    );
    backend.verify({ ignoreCancelled: true });
  });

  // Abertura direta usa conjunto sem filtros, mesmo se houver um rascunho que ainda não foi aplicado.
  it('consulta sem parâmetros quando nenhum filtro foi confirmado na sessão', () => {
    startSession();
    listState.updateDraft({ search: 'rascunho ainda não aplicado' });
    let result: unknown;

    query.getSummaryForAppliedFilters().subscribe((response) => (result = response));
    const request = backend.expectOne((candidate) => candidate.url === DASHBOARD_URL);

    expect(request.request.params.keys()).toEqual([]);
    expect(request.request.method).toBe('GET');
    request.flush(emptyDashboardResponse());

    expect(result).toBeDefined();
  });

  // Busca literal e intervalo [from, toExclusive) chegam iguais à lista, ignorando rascunho e página.
  it('reutiliza somente os filtros aplicados ao ler o dashboard', () => {
    startSession();
    const search = 'café 20%_\\literal+';
    const applied = {
      cardId: 'card-synthetic',
      categoryId: 'category-synthetic',
      situation: 'Pending',
      from: '2026-11-01',
      toExclusive: '2026-12-01',
      search,
    };
    listState.updateDraft(applied);
    expect(listState.applyDraftFilters()).toBe(true);

    const listRequest = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    expect(listRequest.request.params.get('from')).toBe('2026-11-01');
    expect(listRequest.request.params.get('toExclusive')).toBe('2026-12-01');
    expect(listRequest.request.params.get('search')).toBe(search);
    listRequest.flush(transactionPageResponse());

    listState.updateDraft({ search: 'rascunho diferente' });
    let result: unknown;
    query.getSummaryForAppliedFilters().subscribe((response) => (result = response));
    const dashboardRequest = backend.expectOne((candidate) => candidate.url === DASHBOARD_URL);

    expect(dashboardRequest.request.method).toBe('GET');
    expect(dashboardRequest.request.params.keys().sort()).toEqual(
      ['cardId', 'categoryId', 'situation', 'from', 'toExclusive', 'search'].sort(),
    );
    expect(dashboardRequest.request.params.get('cardId')).toBe(applied.cardId);
    expect(dashboardRequest.request.params.get('categoryId')).toBe(applied.categoryId);
    expect(dashboardRequest.request.params.get('situation')).toBe(applied.situation);
    expect(dashboardRequest.request.params.get('from')).toBe(applied.from);
    expect(dashboardRequest.request.params.get('toExclusive')).toBe(applied.toExclusive);
    expect(dashboardRequest.request.params.get('search')).toBe(search);
    expect(dashboardRequest.request.params.has('page')).toBe(false);
    expect(dashboardRequest.request.params.has('pageSize')).toBe(false);
    dashboardRequest.flush(emptyDashboardResponse());

    expect(result).toBeDefined();
    expect(listState.view().filters).toEqual(applied);
    expect(listState.draft().search).toBe('rascunho diferente');
  });
});
