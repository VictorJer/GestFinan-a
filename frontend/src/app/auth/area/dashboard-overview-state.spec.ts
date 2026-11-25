import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable, Subscription } from 'rxjs';
import { vi } from 'vitest';
import { API_BASE_URL } from '../../api-base-url.token';
import { SessionScopedResponse } from './card-connection-api';
import { DashboardSummary } from './dashboard-api';
import { DashboardOverviewQueryService } from './dashboard-overview-query';
import { DashboardOverviewStateService } from './dashboard-overview-state';
import { TransactionListApi } from './transaction-list-api';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { TransactionListStateService } from './transaction-list-state';

const API_BASE = 'https://api.example.test/proxy/';
const TRANSACTIONS_URL = `${API_BASE}api/transactions`;
const DASHBOARD_URL = `${API_BASE}api/dashboard`;
const TRANSACTION_ID = '5b76333c-6d8e-4d9e-aac6-3a6096f97ab1';
const CARD_ID = '2a990192-cb59-43eb-8520-cfef6a0e3e40';
const CATEGORY_ID = '6e209084-7488-4d97-930f-10f52df4b116';

let backend: HttpTestingController;
let state: DashboardOverviewStateService;
let session: SessionService;
let listState: TransactionListStateService;

// Configura estado real da lista, cliente do resumo e transporte protegido com backend HTTP sintético.
function configureState(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  state = TestBed.inject(DashboardOverviewStateService);
  session = TestBed.inject(SessionService);
  listState = TestBed.inject(TransactionListStateService);
}

// Instala uma sessão artificial e retorna sua geração para validar propriedade de cada snapshot.
function startSession(
  expiresAtUtc = new Date(Date.now() + 30 * 60 * 1000).toISOString(),
): number {
  return session.startSession({
    accessToken: 'synthetic-overview-state-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc,
  })!;
}

// Contagens mantêm os dois eixos coerentes e cobertura ausente permanece explicitamente desconhecida.
function dashboardResponse(filteredCount = 3): DashboardSummary {
  return {
    filteredCount,
    countsByNature: [
      { nature: 'Purchase', count: filteredCount },
      { nature: 'Refund', count: 0 },
      { nature: 'BillPayment', count: 0 },
      { nature: 'Fee', count: 0 },
      { nature: 'Adjustment', count: 0 },
      { nature: 'Unknown', count: 0 },
    ],
    countsBySituation: [
      { situation: 'Effective', count: filteredCount },
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

// Movimento sintético completo permite confirmar uma anotação sem depender de dados ou APIs reais.
function movement(categoryId: string | null): Record<string, unknown> {
  return {
    id: TRANSACTION_ID,
    cardId: CARD_ID,
    description: 'Movimento sintético',
    amount: 17.25,
    currencyCode: 'BRL',
    originalDate: '2026-11-24T00:00:00Z',
    transactionDate: '2026-11-24',
    dateReason: null,
    nature: 'Purchase',
    natureReason: null,
    situation: 'Effective',
    situationReason: null,
    installmentNumber: null,
    totalInstallments: null,
    categoryId,
    note: null,
  };
}

// A consulta da lista confirma filtros e categoria sem recalcular o resumo pela página materializada.
function transactionPageResponse(
  items: readonly Record<string, unknown>[] = [],
): Record<string, unknown> {
  return {
    items,
    page: 1,
    pageSize: 25,
    filteredCount: items.length,
    coverage: {
      lastSuccessfulImportAtUtc: null,
      lastSuccessfulWindow: null,
      latestExecution: null,
    },
  };
}

// Protege a suíte contra qualquer POST ou rota de sincronização introduzidos pelo estado da visão.
function expectNoSynchronizationRequests(): void {
  backend.expectNone(
    (request) => request.method !== 'GET' || request.url.toLowerCase().includes('/sync'),
  );
}

// Exercita geração, request ID, retries e cancelamentos com respostas isoladas da API real.
describe('Estado da visão descritiva', () => {
  beforeEach(() => configureState());

  afterEach(() => {
    expectNoSynchronizationRequests();
    backend.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  // A resposta aceita fica vinculada à geração e ao filtro capturado no início da leitura.
  it('publica snapshot autorizado e refaz somente um GET no retry', () => {
    const generation = startSession();
    state.load();
    const initialRequest = backend.expectOne((request) => request.url === DASHBOARD_URL);
    expect(initialRequest.request.method).toBe('GET');
    initialRequest.flush(dashboardResponse(3));

    expect(state.view().status).toBe('loaded');
    expect(state.view().snapshot?.generation).toBe(generation);
    expect(state.view().snapshot?.summary.filteredCount).toBe(3);

    state.retry();
    const retryRequests = backend.match((request) => request.url === DASHBOARD_URL);
    expect(retryRequests).toHaveLength(1);
    expect(retryRequests[0].request.method).toBe('GET');
    expect(state.view().status).toBe('loading');
    expect(state.view().snapshotIsPrevious).toBe(true);
    retryRequests[0].flush(dashboardResponse(4));

    expect(state.view().status).toBe('loaded');
    expect(state.view().snapshot?.summary.filteredCount).toBe(4);
    expect(state.view().snapshotIsPrevious).toBe(false);
  });

  // Falha transitória só preserva um resumo quando geração e filtros ainda são os mesmos.
  it('preserva resumo anterior da mesma geração/filtro, mas o descarta ao mudar filtro', () => {
    startSession();
    state.load();
    backend.expectOne((request) => request.url === DASHBOARD_URL).flush(dashboardResponse(3));

    state.retry();
    backend.expectOne((request) => request.url === DASHBOARD_URL).flush(
      { title: 'synthetic unavailable body' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    expect(state.view().status).toBe('error');
    expect(state.view().snapshotIsPrevious).toBe(true);
    expect(state.view().snapshot?.summary.filteredCount).toBe(3);

    listState.updateDraft({ search: 'filtro novo' });
    expect(listState.applyDraftFilters()).toBe(true);
    backend.expectOne((request) => request.url === TRANSACTIONS_URL).flush(transactionPageResponse());

    state.load();
    const changedFilterRequest = backend.expectOne((request) => request.url === DASHBOARD_URL);
    expect(changedFilterRequest.request.params.get('search')).toBe('filtro novo');
    expect(state.view().snapshot).toBeNull();
    changedFilterRequest.flush(dashboardResponse(1));
    expect(state.view().snapshot?.summary.filteredCount).toBe(1);
  });

  // A ida ao resumo mostra filtros aplicados; o rascunho permanece separado e não vira estado persistido.
  it('mantém resumo/lista coerentes e relê contagens após categoria confirmada', () => {
    startSession();
    const appliedSearch = 'filtro aplicado literal';
    listState.updateDraft({ search: appliedSearch });
    expect(listState.applyDraftFilters()).toBe(true);
    const initialListRead = backend.expectOne((request) => request.url === TRANSACTIONS_URL);
    expect(initialListRead.request.params.get('search')).toBe(appliedSearch);
    initialListRead.flush(transactionPageResponse([movement(null)]));

    state.load();
    const initialSummaryRead = backend.expectOne((request) => request.url === DASHBOARD_URL);
    expect(initialSummaryRead.request.params.get('search')).toBe(appliedSearch);
    initialSummaryRead.flush(dashboardResponse(1));
    expect(state.view().filters).toEqual({ search: appliedSearch });

    listState.updateDraft({ search: 'rascunho ainda não aplicado' });
    expect(state.view().filters).toEqual({ search: appliedSearch });
    state.cancelPendingRead();
    expect(state.view().snapshot).toBeNull();
    expect(listState.draft().search).toBe('rascunho ainda não aplicado');

    // O retorno à lista relê os filtros confirmados e conserva o rascunho apenas nos controles da lista.
    listState.load();
    const returnedListRead = backend.expectOne((request) => request.url === TRANSACTIONS_URL);
    expect(returnedListRead.request.params.get('search')).toBe(appliedSearch);
    returnedListRead.flush(transactionPageResponse([movement(null)]));
    expect(listState.view().filters).toEqual({ search: appliedSearch });
    expect(listState.draft().search).toBe('rascunho ainda não aplicado');

    // A categoria só muda após PATCH 200; a releitura posterior atualiza lista e resumo pela API.
    TestBed.inject(TransactionListApi)
      .updateAnnotation(TRANSACTION_ID, { categoryId: CATEGORY_ID })
      .subscribe();
    const patch = backend.expectOne(`${TRANSACTIONS_URL}/${TRANSACTION_ID}/annotation`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ categoryId: CATEGORY_ID });
    patch.flush(
      { categoryId: CATEGORY_ID, note: null },
      { status: 200, statusText: 'OK' },
    );

    listState.load();
    const updatedListRead = backend.expectOne((request) => request.url === TRANSACTIONS_URL);
    expect(updatedListRead.request.params.get('search')).toBe(appliedSearch);
    updatedListRead.flush(transactionPageResponse([movement(CATEGORY_ID)]));

    // O filtro de categoria aplicado depois da confirmação inclui o movimento recém-organizado.
    listState.updateDraft({ search: appliedSearch, categoryId: CATEGORY_ID });
    expect(listState.applyDraftFilters()).toBe(true);
    const categoryFilteredListRead = backend.expectOne(
      (request) => request.url === TRANSACTIONS_URL,
    );
    expect(categoryFilteredListRead.request.params.get('categoryId')).toBe(CATEGORY_ID);
    categoryFilteredListRead.flush(transactionPageResponse([movement(CATEGORY_ID)]));

    state.load();
    const updatedSummaryRead = backend.expectOne((request) => request.url === DASHBOARD_URL);
    expect(updatedSummaryRead.request.params.get('search')).toBe(appliedSearch);
    expect(updatedSummaryRead.request.params.get('categoryId')).toBe(CATEGORY_ID);
    expect(updatedSummaryRead.request.params.has('page')).toBe(false);
    updatedSummaryRead.flush(dashboardResponse(1));

    expect(state.view().filters).toEqual({ search: appliedSearch, categoryId: CATEGORY_ID });
    expect(state.view().snapshot?.summary.filteredCount).toBe(1);
    expect(listState.draft().search).toBe(appliedSearch);
  });

  // Logout cancela a consulta em curso e remove qualquer estado privado ainda exposto pela geração.
  it('cancela e limpa resumo pendente ao encerrar a sessão', () => {
    const generation = startSession();
    state.load();
    const pendingRequest = backend.expectOne((request) => request.url === DASHBOARD_URL);

    session.endSessionForGeneration(generation);
    TestBed.tick();

    expect(pendingRequest.cancelled).toBe(true);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });

  // A expiração verificada pelo SessionService cancela a leitura e redige o resumo da mesma geração.
  it('cancela e limpa resumo após a expiração local', () => {
    vi.useFakeTimers();
    const startedAt = Date.now();
    startSession(new Date(startedAt + 1000).toISOString());
    state.load();
    backend.expectOne((request) => request.url === DASHBOARD_URL).flush(dashboardResponse(3));
    state.retry();
    const pendingRequest = backend.expectOne((request) => request.url === DASHBOARD_URL);
    vi.setSystemTime(startedAt + 1001);

    expect(session.currentSession()).toBeNull();
    TestBed.tick();

    expect(pendingRequest.cancelled).toBe(true);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });

  // Novo login cria outra geração, sem resgatar snapshot do titular ou token anterior.
  it('descarta snapshot confirmado ao trocar a geração da sessão', () => {
    const oldGeneration = startSession();
    state.load();
    backend.expectOne((request) => request.url === DASHBOARD_URL).flush(dashboardResponse(3));
    const newGeneration = startSession();
    TestBed.tick();

    expect(newGeneration).not.toBe(oldGeneration);
    expect(state.view().generation).toBe(newGeneration);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });

  // Ao sair da rota, o request é cancelado e nenhum snapshot fica como cache da próxima entrada.
  it('cancela e limpa resumo quando a página é destruída', () => {
    startSession();
    state.load();
    const pendingRequest = backend.expectOne((request) => request.url === DASHBOARD_URL);

    state.cancelPendingRead();

    expect(pendingRequest.cancelled).toBe(true);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });
});

// Um transporte deliberadamente não cancelável permite exercitar callbacks atrasados além do unsubscribe.
describe('Respostas fora de ordem da visão', () => {
  it('descarta callback antigo mesmo quando chega após a resposta mais recente', () => {
    const observers: Array<{
      next?: (response: SessionScopedResponse<DashboardSummary>) => void;
    }> = [];
    const queryStub = {
      getAppliedFilters: () => ({}),
      getSummaryForFilters: () =>
        ({
          subscribe: (observer: { next?: (response: SessionScopedResponse<DashboardSummary>) => void }) => {
            observers.push(observer);
            return new Subscription();
          },
        }) as unknown as Observable<SessionScopedResponse<DashboardSummary>>,
    };
    TestBed.configureTestingModule({
      providers: [{ provide: DashboardOverviewQueryService, useValue: queryStub }],
    });
    state = TestBed.inject(DashboardOverviewStateService);
    session = TestBed.inject(SessionService);
    const generation = startSession();

    state.load();
    state.load();
    observers[1].next?.({ generation, value: dashboardResponse(7) });
    observers[0].next?.({ generation, value: dashboardResponse(2) });

    expect(state.view().snapshot?.summary.filteredCount).toBe(7);
    expect(state.view().status).toBe('loaded');
  });
});
