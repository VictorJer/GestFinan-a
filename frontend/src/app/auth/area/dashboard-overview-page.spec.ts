import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, Subscription } from 'rxjs';
import { vi } from 'vitest';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { SessionScopedResponse } from './card-connection-api';
import { DashboardSummary } from './dashboard-api';
import { DashboardOverviewPage } from './dashboard-overview-page';
import { DashboardOverviewQueryService } from './dashboard-overview-query';
import {
  DashboardOverviewReadState,
  DashboardOverviewStateService,
} from './dashboard-overview-state';
import { TransactionListStateService } from './transaction-list-state';

const GENERATION = 7;
const API_BASE = 'https://api.example.test';
const TRANSACTIONS_URL = `${API_BASE}/api/transactions`;
const DASHBOARD_URL = `${API_BASE}/api/dashboard`;
const CARD_ID = '2a990192-cb59-43eb-8520-cfef6a0e3e40';

// Mantém uma resposta sintética coerente nos dois eixos e com fatos operacionais independentes.
function summary(filteredCount: number, coverage: DashboardSummary['coverage']): DashboardSummary {
  // Distribui sobras de modo não negativo para cobrir tanto o conjunto vazio quanto leituras pequenas.
  const purchaseCount = Math.max(0, filteredCount - 2);
  const effectiveCount = Math.max(0, filteredCount - 3);
  const pendingCount = Math.min(1, filteredCount - effectiveCount);
  const futureCount = Math.min(1, filteredCount - effectiveCount - pendingCount);
  return {
    filteredCount,
    countsByNature: [
      { nature: 'Purchase', count: purchaseCount },
      { nature: 'Refund', count: 0 },
      { nature: 'BillPayment', count: 0 },
      { nature: 'Fee', count: 0 },
      { nature: 'Adjustment', count: 0 },
      { nature: 'Unknown', count: filteredCount - purchaseCount },
    ],
    countsBySituation: [
      { situation: 'Effective', count: effectiveCount },
      { situation: 'Pending', count: pendingCount },
      { situation: 'FutureInstallment', count: futureCount },
      { situation: 'Unknown', count: filteredCount - effectiveCount - pendingCount - futureCount },
    ],
    coverage,
    dateBasis: 'TransactionDate',
  };
}

// Instala somente a página e um estado sintético, isolando a apresentação da API real.
function createPage(value: DashboardSummary): {
  readonly fixture: ComponentFixture<DashboardOverviewPage>;
  readonly state: DashboardOverviewReadState;
  readonly load: ReturnType<typeof vi.fn>;
} {
  const state: DashboardOverviewReadState = {
    status: value.filteredCount === 0 ? 'empty' : 'loaded',
    generation: GENERATION,
    filters: {},
    snapshot: { generation: GENERATION, filters: {}, summary: value },
    snapshotIsPrevious: false,
    failure: null,
  };
  const view = signal(state);
  const load = vi.fn();
  const stateService = {
    view,
    load,
    cancelPendingRead: vi.fn(),
  };

  TestBed.configureTestingModule({
    imports: [DashboardOverviewPage],
    providers: [
      provideRouter([]),
      { provide: DashboardOverviewStateService, useValue: stateService },
    ],
  });
  const fixture = TestBed.createComponent(DashboardOverviewPage);
  fixture.detectChanges();
  return { fixture, state, load };
}

// Gera movimentos isolados para representar uma página da lista sem reutilizar dados financeiros reais.
function transaction(index: number): Record<string, unknown> {
  return {
    id: `${String(index + 1).padStart(8, '0')}-6d8e-4d9e-aac6-3a6096f97ab1`,
    cardId: CARD_ID,
    description: `Movimento sintético ${index + 1}`,
    amount: 1,
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
  };
}

// Abre a página com sessão e transporte sintéticos para conferir códigos HTTP no fluxo do interceptor.
function createAuthenticatedApiPage(): {
  readonly fixture: ComponentFixture<DashboardOverviewPage>;
  readonly http: HttpTestingController;
  readonly session: SessionService;
} {
  TestBed.configureTestingModule({
    imports: [DashboardOverviewPage],
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
      provideRouter([]),
    ],
  });
  const session = TestBed.inject(SessionService);
  session.startSession({
    accessToken: 'synthetic-dashboard-page-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(DashboardOverviewPage);
  fixture.detectChanges();
  return { fixture, http, session };
}

// A cobertura anterior e a falha recente aparecem lado a lado sem substituir sucesso por execução.
describe('Apresentação da visão descritiva', () => {
  // Isola módulos e sessões sintéticas para que um cenário de apresentação não afete o seguinte.
  afterEach(() => TestBed.resetTestingModule());

  // Um resumo de 57 itens, sem itens de página, demonstra que a contagem vem do universo da API.
  it('apresenta o universo completo, os dois eixos, a data técnica e sucesso separado da falha', () => {
    const { fixture, load } = createPage(
      summary(57, {
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
      }),
    );
    const page = fixture.nativeElement as HTMLElement;
    const text = page.textContent ?? '';

    expect(load).toHaveBeenCalledOnce();
    expect(text).toContain('57 movimentos no conjunto filtrado');
    expect(text).toContain(
      'A lista e a visão são consultas separadas; se os dados mudarem entre as leituras, as contagens podem diferir.',
    );
    expect(page.querySelector('#dashboard-overview-nature-title')?.textContent).toContain(
      'Natureza',
    );
    expect(page.querySelector('#dashboard-overview-situation-title')?.textContent).toContain(
      'Situação',
    );
    expect(text).toContain('Desconhecida (Unknown)');
    expect(text).toContain('Data de referência: TransactionDate');
    expect(text).toContain('não competência nem ciclo de fatura');
    expect(text).toContain('Último sucesso confirmado');
    expect(text).toContain('24 de nov. de 2026');
    expect(text).toContain('2026-10-01 até 2026-12-01 (final exclusivo)');
    expect(text).toContain('2026-10-02 até fim não informado');
    expect(text).toContain('Parcial');
    expect(text).toContain('Execução mais recente');
    expect(text).toContain('Estado: Falhou');
    expect(text).toContain('Cobertura informada nesta execução: Desconhecida');
    expect(text).toContain('Serviço do provedor indisponível');
    expect(text).not.toMatch(/R\$|\b(?:saldo|gasto|receita|subtotal|média)\b/i);
    expect(page.querySelector('svg, canvas')).toBeNull();
    fixture.destroy();
  });

  // A sequência dos títulos permanece compreensível quando o grid visual passa a ter colunas.
  it('mantém ordem semântica do cabeçalho aos filtros, resumo, cobertura e lista única', () => {
    const { fixture } = createPage(
      summary(57, {
        lastSuccessfulImportAtUtc: null,
        lastSuccessfulWindow: null,
        latestExecution: null,
      }),
    );
    const page = fixture.nativeElement as HTMLElement;
    const headings = Array.from(page.querySelectorAll<HTMLElement>('h1, h2, h3')).map((heading) => [
      heading.tagName,
      heading.textContent?.trim(),
    ]);

    expect(headings).toEqual([
      ['H1', 'Visão descritiva'],
      ['H2', 'Filtros aplicados'],
      ['H2', 'Conjunto filtrado'],
      ['H2', 'Distribuições independentes'],
      ['H3', 'Natureza do movimento'],
      ['H3', 'Situação do movimento'],
      ['H2', 'Cobertura e execução informadas'],
    ]);
    expect(page.querySelector('a[href="/area/movimentos"]')?.textContent).toContain(
      'Abrir lista de movimentos',
    );
    expect(page.textContent).not.toContain('Movimento sintético');
    expect(page.textContent).not.toMatch(/R\$|\b(?:saldo|gasto|receita|subtotal|média)\b/i);
    fixture.destroy();
  });

  // Contagem vazia não implica cobertura completa: sem sucesso e janela, a cobertura continua desconhecida.
  it('apresenta conjunto vazio com cobertura desconhecida sem fabricar janela ou execução', () => {
    const { fixture } = createPage(
      summary(0, {
        lastSuccessfulImportAtUtc: null,
        lastSuccessfulWindow: null,
        latestExecution: null,
      }),
    );
    const page = fixture.nativeElement as HTMLElement;
    const text = page.textContent ?? '';

    expect(text).toContain('0 movimentos no conjunto filtrado');
    expect(text).toContain('Não há movimentos persistidos para estes filtros');
    expect(text).toContain('Nenhuma importação bem-sucedida foi confirmada');
    expect(text).toContain(
      'Nenhuma janela de sucesso é conhecida; a cobertura permanece desconhecida',
    );
    expect(text).toContain('Nenhuma execução recente foi informada');
    expect(page.querySelector('#dashboard-overview-nature-title')).not.toBeNull();
    expect(page.querySelector('#dashboard-overview-situation-title')).not.toBeNull();
    fixture.destroy();
  });

  // Compara a página de 25 itens com filteredCount 57 e confirma a passagem do filtro aplicado.
  it('mostra a contagem do universo quando a lista contém somente uma página', () => {
    TestBed.configureTestingModule({
      imports: [DashboardOverviewPage],
      providers: [
        { provide: API_BASE_URL, useValue: API_BASE },
        provideHttpClient(withInterceptors([apiAuthInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    const session = TestBed.inject(SessionService);
    session.startSession({
      accessToken: 'synthetic-page-count-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const http = TestBed.inject(HttpTestingController);
    const listState = TestBed.inject(TransactionListStateService);
    const appliedSearch = 'filtro sintético';
    listState.updateDraft({ search: appliedSearch });
    expect(listState.applyDraftFilters()).toBe(true);

    const listRequest = http.expectOne((request) => request.url === TRANSACTIONS_URL);
    expect(listRequest.request.params.get('search')).toBe(appliedSearch);
    expect(listRequest.request.params.get('pageSize')).toBe('25');
    listRequest.flush({
      items: Array.from({ length: 25 }, (_, index) => transaction(index)),
      page: 1,
      pageSize: 25,
      filteredCount: 57,
      coverage: {
        lastSuccessfulImportAtUtc: null,
        lastSuccessfulWindow: null,
        latestExecution: null,
      },
    });

    const fixture = TestBed.createComponent(DashboardOverviewPage);
    fixture.detectChanges();
    const dashboardRequest = http.expectOne((request) => request.url === DASHBOARD_URL);
    expect(dashboardRequest.request.params.get('search')).toBe(appliedSearch);
    expect(dashboardRequest.request.params.has('page')).toBe(false);
    expect(dashboardRequest.request.params.has('pageSize')).toBe(false);
    dashboardRequest.flush(
      summary(57, {
        lastSuccessfulImportAtUtc: null,
        lastSuccessfulWindow: null,
        latestExecution: null,
      }),
    );
    fixture.detectChanges();

    expect(listState.view().snapshot?.response.items).toHaveLength(25);
    expect(listState.view().snapshot?.response.filteredCount).toBe(57);
    expect(fixture.nativeElement.textContent).toContain('57 movimentos no conjunto filtrado');
    expect(fixture.nativeElement.textContent).not.toContain('25 movimentos no conjunto filtrado');
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Busca literal');
    expect(page.textContent).toContain(appliedSearch);
    expect(listState.view().filters).toEqual({ search: appliedSearch });
    expect(page.querySelectorAll('a[href="/area/movimentos"]')).toHaveLength(1);
    expect(page.querySelector('a[href="/area/movimentos"]')?.textContent).toContain(
      'Abrir lista de movimentos',
    );
    fixture.destroy();
    http.verify({ ignoreCancelled: true });
  });

  // 403 permanece na sessão, oferece ação acessível e repete estritamente uma leitura GET.
  it('informa falta de permissão e permite repetir somente o GET com teclado', () => {
    const { fixture, http, session } = createAuthenticatedApiPage();
    const initialRequest = http.expectOne((request) => request.url === DASHBOARD_URL);
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain(
      'Carregando resumo',
    );
    initialRequest.flush(
      { detail: 'synthetic-private-forbidden-body' },
      { status: 403, statusText: 'Forbidden' },
    );
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    const alert = page.querySelector<HTMLElement>('[role="alert"]');
    const retryButton = page.querySelector<HTMLButtonElement>('button[type="button"]');
    expect(alert?.textContent).toContain('não tem permissão');
    expect(page.textContent).not.toContain('synthetic-private-forbidden-body');
    expect(session.currentSession()).not.toBeNull();
    expect(retryButton?.textContent).toContain('Tentar carregar novamente');
    expect(retryButton?.tabIndex).toBe(0);
    expect(retryButton?.classList.contains('min-h-11')).toBe(true);
    expect(retryButton?.classList.contains('w-full')).toBe(true);
    expect(retryButton?.classList.contains('focus-visible:outline-solid')).toBe(true);
    expect(retryButton?.classList.contains('outline-finora-brand')).toBe(true);
    retryButton?.focus();
    expect(document.activeElement).toBe(retryButton);

    retryButton?.click();
    fixture.detectChanges();
    const retries = http.match((request) => request.url === DASHBOARD_URL);
    expect(retries).toHaveLength(1);
    expect(retries[0].request.method).toBe('GET');
    expect(retries[0].request.headers.get('Authorization')).toMatch(/^Bearer /);
    retries[0].flush(
      summary(3, {
        lastSuccessfulImportAtUtc: null,
        lastSuccessfulWindow: null,
        latestExecution: null,
      }),
    );
    fixture.detectChanges();

    expect(page.textContent).toContain('3 movimentos no conjunto filtrado');
    expect(page.querySelector('[role="alert"]')).toBeNull();
    const listLink = page.querySelector<HTMLAnchorElement>('a[href="/area/movimentos"]');
    expect(listLink?.tabIndex).toBe(0);
    expect(listLink?.classList.contains('focus-visible:outline-solid')).toBe(true);
    http.verify({ ignoreCancelled: true });
    fixture.destroy();
  });

  // Uma falha de retry identifica o resumo anterior da mesma sessão sem confundi-lo com sucesso novo.
  it('diferencia resposta anterior de falha e repete apenas o GET do resumo', () => {
    const { fixture, http } = createAuthenticatedApiPage();
    http.expectOne((request) => request.url === DASHBOARD_URL).flush(
      summary(12, {
        lastSuccessfulImportAtUtc: '2026-11-24T10:00:00Z',
        lastSuccessfulWindow: {
          requestedFrom: '2026-11-01',
          requestedToExclusive: '2026-12-01',
          observedFrom: '2026-11-01',
          observedTo: '2026-11-24',
          coverageState: 'Partial',
        },
        latestExecution: null,
      }),
    );
    fixture.detectChanges();

    fixture.componentInstance.retry();
    fixture.detectChanges();
    const retry = http.expectOne((request) => request.url === DASHBOARD_URL);
    expect(retry.request.method).toBe('GET');
    retry.flush(
      { detail: 'synthetic-private-retry-failure' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('[role="status"]')?.textContent).toContain(
      'resposta anterior da mesma sessão e dos mesmos filtros',
    );
    expect(page.querySelector('[role="alert"]')?.textContent).toContain(
      'temporariamente indisponível',
    );
    expect(page.textContent).toContain('12 movimentos no conjunto filtrado');
    expect(page.textContent).toContain('2026-11-01 até 2026-12-01 (final exclusivo)');
    expect(page.textContent).not.toContain('synthetic-private-retry-failure');
    expect(page.textContent).not.toMatch(/R\$|\b(?:saldo|gasto|receita|subtotal|média)\b/i);
    expect(page.querySelectorAll('a[href="/area/movimentos"]')).toHaveLength(1);
    http.verify({ ignoreCancelled: true });
    fixture.destroy();
  });

  // Indisponibilidade apresenta orientação fixa e nunca ecoa o corpo potencialmente sensível.
  it('apresenta falha de indisponibilidade com mensagem segura', () => {
    const { fixture, http } = createAuthenticatedApiPage();
    http
      .expectOne((request) => request.url === DASHBOARD_URL)
      .flush(
        { detail: 'synthetic-private-unavailable-body' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('[role="alert"]')?.textContent).toContain(
      'temporariamente indisponível',
    );
    expect(page.textContent).not.toContain('synthetic-private-unavailable-body');
    expect(page.querySelector('button[type="button"]')?.textContent).toContain(
      'Tentar carregar novamente',
    );
    http.verify({ ignoreCancelled: true });
    fixture.destroy();
  });

  // Resposta incompatível não apresenta dados parciais nem texto livre devolvido pela API.
  it('apresenta resposta inválida com erro seguro e sem payload remoto', () => {
    const { fixture, http } = createAuthenticatedApiPage();
    http
      .expectOne((request) => request.url === DASHBOARD_URL)
      .flush({
        filteredCount: 42,
        detail: 'synthetic-private-invalid-body',
      });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('formato esperado');
    expect(page.textContent).not.toContain('42 movimentos no conjunto filtrado');
    expect(page.textContent).not.toContain('synthetic-private-invalid-body');
    expect(page.querySelector('#dashboard-overview-distributions-title')).toBeNull();
    http.verify({ ignoreCancelled: true });
    fixture.destroy();
  });

  // Um 401 depois de snapshot confirmado encerra a sessão e remove contagens, janela e cobertura privadas.
  it('remove dados privados após 401 mesmo quando havia resumo confirmado', () => {
    const { fixture, http, session } = createAuthenticatedApiPage();
    http
      .expectOne((request) => request.url === DASHBOARD_URL)
      .flush(
        summary(57, {
          lastSuccessfulImportAtUtc: '2026-11-24T10:00:00Z',
          lastSuccessfulWindow: {
            requestedFrom: '2026-10-01',
            requestedToExclusive: '2026-12-01',
            observedFrom: '2026-10-02',
            observedTo: '2026-11-24',
            coverageState: 'Partial',
          },
          latestExecution: null,
        }),
      );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('57 movimentos no conjunto filtrado');

    fixture.componentInstance.retry();
    fixture.detectChanges();
    http
      .expectOne((request) => request.url === DASHBOARD_URL)
      .flush(
        { detail: 'synthetic-private-unauthorized-body' },
        { status: 401, statusText: 'Unauthorized' },
      );
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(session.currentSession()).toBeNull();
    expect(page.textContent).not.toContain('57 movimentos no conjunto filtrado');
    expect(page.textContent).not.toContain('Último sucesso confirmado');
    expect(page.textContent).not.toContain('2026-11-24');
    expect(page.textContent).not.toContain('synthetic-private-unauthorized-body');
    expect(page.querySelector('#dashboard-overview-distributions-title')).toBeNull();
    expect(page.querySelector('#dashboard-overview-coverage-title')).toBeNull();
    expect(page.querySelector('[role="alert"]')).toBeNull();
    http.verify({ ignoreCancelled: true });
    fixture.destroy();
  });

  // A resposta de um transporte não cancelável não pode reexibir o resumo depois do logout.
  it('descarta resposta tardia e mantém a tela sem dados após encerrar a sessão', () => {
    const observers: Array<{
      next?: (response: SessionScopedResponse<DashboardSummary>) => void;
    }> = [];
    const queryStub = {
      getAppliedFilters: () => ({}),
      getSummaryForFilters: () =>
        ({
          subscribe: (observer: {
            next?: (response: SessionScopedResponse<DashboardSummary>) => void;
          }) => {
            observers.push(observer);
            return new Subscription();
          },
        }) as unknown as Observable<SessionScopedResponse<DashboardSummary>>,
    };
    TestBed.configureTestingModule({
      imports: [DashboardOverviewPage],
      providers: [
        provideRouter([]),
        { provide: DashboardOverviewQueryService, useValue: queryStub },
      ],
    });
    const session = TestBed.inject(SessionService);
    const generation = session.startSession({
      accessToken: 'synthetic-late-response-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    })!;
    const fixture = TestBed.createComponent(DashboardOverviewPage);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain(
      'Carregando resumo',
    );

    session.endSessionForGeneration(generation);
    TestBed.tick();
    fixture.detectChanges();
    observers[0].next?.({
      generation,
      value: summary(3, {
        lastSuccessfulImportAtUtc: '2026-11-24T10:00:00Z',
        lastSuccessfulWindow: null,
        latestExecution: null,
      }),
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).not.toContain('3 movimentos no conjunto filtrado');
    expect(page.textContent).not.toContain('2026-11-24');
    expect(page.querySelector('#dashboard-overview-distributions-title')).toBeNull();
    expect(page.querySelector('#dashboard-overview-coverage-title')).toBeNull();
    fixture.destroy();
  });
});
