import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { TransactionListStateService } from './transaction-list-state';
import { vi } from 'vitest';

const API_BASE = 'https://api.example.test/proxy/';
const TRANSACTIONS_URL = `${API_BASE}api/transactions`;
const CARD_ID = '2a990192-cb59-43eb-8520-cfef6a0e3e40';
const CATEGORY_ID = '6e209084-7488-4d97-930f-10f52df4b116';

let backend: HttpTestingController;
let state: TransactionListStateService;
let session: SessionService;

// Usa o cliente e interceptor reais com backend sintético para conferir filtros e encerramento de requests.
function configureState(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  state = TestBed.inject(TransactionListStateService);
  session = TestBed.inject(SessionService);
}

// Instala credenciais sintéticas com expiração configurável para identificar a geração do request.
function startSession(
  expiresAtUtc = new Date(Date.now() + 30 * 60 * 1000).toISOString(),
): number {
  return session.startSession({
    accessToken: 'synthetic-transaction-state-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc,
  })!;
}

// Mantém o envelope de teste fiel ao backend sem produzir cobertura ou contagens derivadas no cliente.
function pageResponse(
  options: {
    page?: number;
    pageSize?: number;
    filteredCount?: number;
    items?: readonly object[];
    coverageState?: string;
  } = {},
): object {
  const page = options.page ?? 1;
  const items = options.items ?? [
    {
      id: '5b76333c-6d8e-4d9e-aac6-3a6096f97ab1',
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
      categoryId: null,
      note: null,
    },
  ];

  return {
    items,
    page,
    pageSize: options.pageSize ?? 25,
    filteredCount: options.filteredCount ?? items.length,
    coverage: {
      lastSuccessfulImportAtUtc: '2026-11-24T10:00:00Z',
      lastSuccessfulWindow: {
        requestedFrom: '2026-11-01',
        requestedToExclusive: null,
        observedFrom: '2026-11-01',
        observedTo: '2026-11-24',
        coverageState: options.coverageState ?? 'Partial',
      },
      latestExecution: { state: 'Succeeded', coverageState: 'Partial', failureCode: null },
    },
  };
}

// Exercita filtros combinados, página/contagem e cancelamento usando somente respostas sintéticas.
describe('Estado da lista de movimentos', () => {
  beforeEach(() => configureState());

  afterEach(() => {
    backend.expectNone((candidate) => candidate.url.toLowerCase().includes('/sync'));
    backend.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  // Os filtros chegam juntos, preservam busca literal e mantêm fim exclusivo sem traduzir data em competência.
  it('aplica filtros combinados e mantém filteredCount/coverage da resposta correspondente', () => {
    const generation = startSession();
    const search = 'café 20%_\\literal+';
    state.updateDraft({
      cardId: CARD_ID,
      categoryId: CATEGORY_ID,
      situation: 'Pending',
      from: '2026-11-01',
      toExclusive: '2026-12-01',
      search,
    });

    expect(state.applyDraftFilters()).toBe(true);
    const request = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('cardId')).toBe(CARD_ID);
    expect(request.request.params.get('categoryId')).toBe(CATEGORY_ID);
    expect(request.request.params.get('situation')).toBe('Pending');
    expect(request.request.params.get('from')).toBe('2026-11-01');
    expect(request.request.params.get('toExclusive')).toBe('2026-12-01');
    expect(request.request.params.get('search')).toBe(search);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('25');
    request.flush(pageResponse({ filteredCount: 42, coverageState: 'Partial' }));

    expect(state.view().generation).toBe(generation);
    expect(state.view().status).toBe('loaded');
    expect(state.view().filters).toEqual({
      cardId: CARD_ID,
      categoryId: CATEGORY_ID,
      situation: 'Pending',
      from: '2026-11-01',
      toExclusive: '2026-12-01',
      search,
    });
    expect(state.filteredCount()).toBe(42);
    expect(state.coverage()?.lastSuccessfulWindow?.coverageState).toBe('Partial');
  });

  // Paginação conserva o mesmo filtro e a página além do fim continua distinta de um conjunto sem registros.
  it('mantém filtros ao paginar e preserva contagem positiva numa página vazia', () => {
    startSession();
    state.updateDraft({ situation: 'Pending' });
    state.applyDraftFilters();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(
      pageResponse({ page: 1, filteredCount: 3 }),
    );

    expect(state.goToPage(8)).toBe(true);
    const request = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    expect(request.request.params.get('situation')).toBe('Pending');
    expect(request.request.params.get('page')).toBe('8');
    expect(request.request.params.get('pageSize')).toBe('25');
    request.flush(
      pageResponse({ page: 8, filteredCount: 3, items: [], coverageState: 'Unknown' }),
    );

    expect(state.view().status).toBe('empty');
    expect(state.view().page).toBe(8);
    expect(state.view().snapshot?.response.items).toEqual([]);
    expect(state.filteredCount()).toBe(3);
    expect(state.coverage()?.lastSuccessfulWindow?.coverageState).toBe('Unknown');
  });

  // A mudança de filtro sempre recomeça na página 1 e não envia uma faixa temporal impossível.
  it('reinicia em página 1 após alterar filtros e recusa intervalo inválido', () => {
    startSession();
    state.updateDraft({ search: 'primeira busca' });
    state.applyDraftFilters();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(
      pageResponse({ page: 1, filteredCount: 31 }),
    );
    state.goToPage(3);
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(
      pageResponse({ page: 3, filteredCount: 31 }),
    );

    state.updateDraft({ search: 'segunda busca' });
    state.applyDraftFilters();
    const request = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    expect(request.request.params.get('search')).toBe('segunda busca');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(pageResponse({ page: 1, filteredCount: 2 }));

    state.updateDraft({ from: '2026-11-25', toExclusive: '2026-11-25' });
    expect(state.applyDraftFilters()).toBe(false);
    expect(state.view().validationMessage).toContain('posterior');
    state.updateDraft({ from: '0000-01-01', toExclusive: '0000-01-02' });
    expect(state.applyDraftFilters()).toBe(false);
    expect(state.view().validationMessage).toContain('data inicial válida');
    backend.expectNone((candidate) => candidate.url === TRANSACTIONS_URL);
    expect(state.view().snapshot?.response.filteredCount).toBe(2);
  });

  // Uma consulta nova cancela a antiga; somente o filtro/página da última leitura pode publicar snapshot.
  it('cancela requisição substituída e não mistura respostas de filtros diferentes', () => {
    startSession();
    state.updateDraft({ search: 'primeira' });
    state.applyDraftFilters();
    const firstRequest = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);

    state.updateDraft({ search: 'mais recente' });
    state.applyDraftFilters();
    expect(firstRequest.cancelled).toBe(true);
    const currentRequest = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    expect(currentRequest.request.params.get('search')).toBe('mais recente');
    currentRequest.flush(pageResponse({ filteredCount: 7 }));

    expect(state.view().filters.search).toBe('mais recente');
    expect(state.view().snapshot?.filters.search).toBe('mais recente');
    expect(state.filteredCount()).toBe(7);

    state.retry();
    const pendingRetry = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    state.cancelPendingRead();
    expect(pendingRetry.cancelled).toBe(true);
    expect(state.view().status).toBe('loaded');
    expect(state.view().snapshot?.filters.search).toBe('mais recente');
  });

  // O limite de busca é conferido antes do GET e o texto literal não é aparado ou interpretado como curinga.
  it('recusa busca acima de 200 caracteres sem limpar o último snapshot confirmado', () => {
    startSession();
    state.updateDraft({ search: 'busca válida' });
    state.applyDraftFilters();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(
      pageResponse({ filteredCount: 5 }),
    );

    state.updateDraft({ search: 'x'.repeat(201) });
    expect(state.applyDraftFilters()).toBe(false);
    expect(state.view().validationMessage).toContain('200 caracteres');
    expect(state.view().snapshot?.response.filteredCount).toBe(5);
    backend.expectNone((candidate) => candidate.url === TRANSACTIONS_URL);
  });

  // Logout cancela o GET pendente e remove imediatamente a página privada já confirmada.
  it('cancela a leitura e limpa snapshot quando a sessão termina', () => {
    const generation = startSession();
    state.load();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(pageResponse());
    state.retry();
    const pendingRequest = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);

    session.endSessionForGeneration(generation);
    TestBed.tick();

    expect(pendingRequest.cancelled).toBe(true);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
    expect(state.filteredCount()).toBeNull();
    expect(state.coverage()).toBeNull();
  });

  // Uma resposta antiga não pode preencher a lista após uma nova geração de sessão.
  it('descarta resposta tardia quando outra sessão começa', () => {
    const previousGeneration = startSession();
    state.load();
    const previousRequest = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);

    const currentGeneration = startSession();
    previousRequest.flush(pageResponse({ items: [] }));
    TestBed.tick();

    expect(state.view().generation).toBe(currentGeneration);
    expect(state.view().generation).not.toBe(previousGeneration);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });

  // A expiração detectada pela sessão aplica a mesma limpeza e cancelamento do logout.
  it('cancela leitura e remove snapshot ao expirar a sessão', () => {
    vi.useFakeTimers();
    const startedAt = Date.now();
    startSession(new Date(startedAt + 1000).toISOString());
    state.load();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(pageResponse());
    state.retry();
    const pendingRequest = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    vi.setSystemTime(startedAt + 1001);

    expect(session.currentSession()).toBeNull();
    TestBed.tick();

    expect(pendingRequest.cancelled).toBe(true);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });

  // 401 encerra a geração e redige o snapshot; o interceptor mantém o aviso seguro de novo login.
  it('limpa a lista em 401 e exige nova sessão', () => {
    startSession();
    state.load();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(pageResponse());
    state.retry();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(
      { message: 'detalhe remoto sintético' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(session.currentSession()).toBeNull();
    expect(session.notice()).toBe('unauthorized');
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
    TestBed.tick();
    expect(state.view().snapshot).toBeNull();
  });

  // 403 é falta de permissão, não expiração: a sessão persiste, sem preservar a página como atual.
  it('distingue 403 de 401 sem manter snapshot como resposta atual', () => {
    const generation = startSession();
    state.load();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(pageResponse());
    state.retry();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(
      { message: 'detalhe remoto sintético' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(session.currentSession()?.generation).toBe(generation);
    expect(session.notice()).toBe('forbidden');
    expect(state.view().status).toBe('error');
    expect(state.view().failure).toBe('forbidden');
    expect(state.view().snapshot).toBeNull();
  });

  // Somente falha transitória conserva o snapshot marcado como anterior e requer retry manual.
  it('preserva snapshot anterior em falha transitória até uma nova leitura explícita', () => {
    startSession();
    state.updateDraft({ search: 'termo confirmado' });
    state.applyDraftFilters();
    backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL).flush(
      pageResponse({ filteredCount: 6 }),
    );
    const confirmedSnapshot = state.view().snapshot;

    state.retry();
    const failedRequest = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    expect(state.view().snapshotIsPrevious).toBe(true);
    failedRequest.error(new ProgressEvent('error'));

    expect(state.view().status).toBe('error');
    expect(state.view().failure).toBe('network');
    expect(state.view().snapshot).toEqual(confirmedSnapshot);
    expect(state.view().snapshotIsPrevious).toBe(true);
    expect(session.notice()).toBe('network');
    backend.expectNone((candidate) => candidate.url === TRANSACTIONS_URL);

    state.retry();
    const retryRequest = backend.expectOne((candidate) => candidate.url === TRANSACTIONS_URL);
    expect(retryRequest.request.method).toBe('GET');
    expect(retryRequest.request.params.get('search')).toBe('termo confirmado');
    retryRequest.flush(pageResponse({ filteredCount: 6 }));
    expect(state.view().snapshotIsPrevious).toBe(false);
    expect(state.view().status).toBe('loaded');
  });
});
