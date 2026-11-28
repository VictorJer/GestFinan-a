import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { AuthenticatedAreaPage } from './auth/area/authenticated-area';
import { apiAuthInterceptor } from './auth/api-auth.interceptor';
import { LoginPage } from './auth/login/login';
import { SessionService } from './auth/session';
import { TransactionListPage } from './auth/area/transaction-list-page';
import { InvestmentPortfolioPage } from './auth/area/investment-portfolio-page';
import { API_BASE_URL } from './api-base-url.token';
import { routes } from './app.routes';

const API_BASE = 'https://api.example.test';

// Cria uma sessão sintética cuja duração pode ser controlada por cada cenário.
function startSession(expiresAtUtc = new Date(Date.now() + 30 * 60 * 1000).toISOString()): void {
  TestBed.inject(SessionService).startSession({
    accessToken: 'synthetic-area-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc,
  });
}

// Responde uma consulta vazia sem inferir cobertura ou ausência de movimentos na origem.
function emptyTransactionPage(): object {
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

// Mantém a resposta da carteira vazia no contrato e confirma zeros sem dados de demonstração.
function emptyInvestmentPortfolio(): object {
  return {
    items: [],
    summary: {
      totalAppliedAmount: '0.00',
      totalCurrentAmount: '0.00',
      difference: '0.00',
      currentAmountsByClass: [
        { class: 'fixed_income', label: 'Renda fixa', currentAmount: '0.00' },
        { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
    },
  };
}

// Testa os limites entre rotas públicas, guarda local e leituras protegidas de cartão/conexão.
describe('Rotas da sessão no navegador', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_BASE_URL, useValue: API_BASE },
        provideRouter(routes),
        provideHttpClient(withInterceptors([apiAuthInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    vi.useRealTimers();
  });

  // Login permanece público e navegável sem inicializar sessão nem consultar a API.
  it('permite abrir /login sem sessão', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/login', LoginPage);

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'Entrar no Finora',
    );
    backend.expectNone(() => true);
  });

  // A URL protegida direta redireciona visitantes ao login sem renderizar a área.
  it('redireciona para login ao abrir /area sem sessão local', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/area', LoginPage);

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(harness.routeNativeElement?.textContent).toContain('Entrar no Finora');
    expect(harness.routeNativeElement?.textContent).not.toContain(
      'Não há cartões disponíveis nos dados persistidos',
    );
    backend.expectNone(() => true);
  });

  // O destino de movimentos também exige sessão local e não consulta dados antes do login.
  it('redireciona para login ao abrir /area/movimentos sem sessão', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/area/movimentos', LoginPage);

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(harness.routeNativeElement?.textContent).toContain('Entrar no Finora');
    expect(harness.routeNativeElement?.textContent).not.toContain('Movimentos');
    backend.expectNone(() => true);
  });

  // A carteira usa o mesmo guard e não envia requisições financeiras para visitante anônimo.
  it('redireciona para login ao abrir /area/investimentos sem sessão', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/area/investimentos', LoginPage);

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(harness.routeNativeElement?.textContent).toContain('Entrar no Finora');
    expect(harness.routeNativeElement?.textContent).not.toContain('Carteira de investimentos');
    backend.expectNone(() => true);
  });

  // A lista lê dados e opções protegidos, mas nunca cria intenção de atualização ao abrir a rota.
  it('abre /area/movimentos com sessão usando somente leituras', async () => {
    startSession();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/area/movimentos', TransactionListPage);

    expect(TestBed.inject(Router).url).toBe('/area/movimentos');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain('Movimentos');
    const cardsRequest = backend.expectOne(`${API_BASE}/api/cards`);
    const categoriesRequest = backend.expectOne(`${API_BASE}/api/categories`);
    const transactionsRequest = backend.expectOne(
      (request) => request.url === `${API_BASE}/api/transactions`,
    );
    const recurrenceRequest = backend.expectOne(
      `${API_BASE}/api/transactions/recurrence-candidates`,
    );
    expect(cardsRequest.request.method).toBe('GET');
    expect(categoriesRequest.request.method).toBe('GET');
    expect(transactionsRequest.request.method).toBe('GET');
    expect(recurrenceRequest.request.method).toBe('GET');
    backend.expectNone(
      (request) => request.method === 'POST' && request.url.toLowerCase().includes('/sync'),
    );
    cardsRequest.flush([]);
    categoriesRequest.flush([]);
    transactionsRequest.flush(emptyTransactionPage());
    recurrenceRequest.flush([]);
    backend.expectNone(() => true);
  });

  // A carteira protegida consulta somente seu endpoint e não dispara cartão, conexão ou sync.
  it('abre /area/investimentos com sessão usando somente GET da carteira', async () => {
    startSession();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/area/investimentos', InvestmentPortfolioPage);

    expect(TestBed.inject(Router).url).toBe('/area/investimentos');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'Carteira de investimentos',
    );
    const portfolioRequest = backend.expectOne(`${API_BASE}/api/investments`);
    expect(portfolioRequest.request.method).toBe('GET');
    expect(portfolioRequest.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-area-token',
    );
    backend.expectNone(() => true);
    portfolioRequest.flush(emptyInvestmentPortfolio());
    harness.fixture.detectChanges();

    expect(harness.routeNativeElement?.textContent).toContain('A carteira está vazia');
    backend.expectNone(() => true);
  });

  // Voltar entre as duas rotas protegidas não repete o POST de avaliação já enviado na entrada.
  it('navega entre cartões e movimentos sem nova intenção de entrada', async () => {
    startSession();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/area', AuthenticatedAreaPage);
    backend.expectOne(`${API_BASE}/api/cards`).flush([]);
    backend.expectOne(`${API_BASE}/api/connections/current/sync-state`).flush({
      updateNeeded: false,
      reauthorizationRequired: false,
      lastSuccessfulImportAtUtc: null,
      sourceUpdatedAtUtc: null,
      lastAttemptAtUtc: null,
      nextAllowedAtUtc: null,
      latestExecution: null,
    });
    backend.expectOne(`${API_BASE}/api/connections/current/sync/on-entry`).flush({
      decision: 'not_required',
    });

    await harness.navigateByUrl('/area/movimentos', TransactionListPage);
    const listCardsRequest = backend.expectOne(`${API_BASE}/api/cards`);
    const categoriesRequest = backend.expectOne(`${API_BASE}/api/categories`);
    const transactionsRequest = backend.expectOne(
      (request) => request.url === `${API_BASE}/api/transactions`,
    );
    const recurrenceRequest = backend.expectOne(
      `${API_BASE}/api/transactions/recurrence-candidates`,
    );
    expect(listCardsRequest.request.method).toBe('GET');
    expect(categoriesRequest.request.method).toBe('GET');
    expect(transactionsRequest.request.method).toBe('GET');
    expect(recurrenceRequest.request.method).toBe('GET');
    backend.expectNone(
      (request) => request.method === 'POST' && request.url.toLowerCase().includes('/sync'),
    );
    listCardsRequest.flush([]);
    categoriesRequest.flush([]);
    transactionsRequest.flush(emptyTransactionPage());
    recurrenceRequest.flush([]);
    await harness.navigateByUrl('/area', AuthenticatedAreaPage);

    const cardsRequest = backend.expectOne(`${API_BASE}/api/cards`);
    const stateRequest = backend.expectOne(`${API_BASE}/api/connections/current/sync-state`);
    expect(cardsRequest.request.method).toBe('GET');
    expect(stateRequest.request.method).toBe('GET');
    backend.expectNone(
      (request) => request.method === 'POST' && request.url.toLowerCase().includes('/sync'),
    );
    cardsRequest.flush([]);
    stateRequest.flush({
      updateNeeded: false,
      reauthorizationRequired: false,
      lastSuccessfulImportAtUtc: null,
      sourceUpdatedAtUtc: null,
      lastAttemptAtUtc: null,
      nextAllowedAtUtc: null,
      latestExecution: null,
    });
  });

  // Expiração do timer local torna o guard fechado mesmo sem depender de uma resposta HTTP.
  it('redireciona sessão expirada ao tentar abrir /area', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-25T12:00:00.000Z'));
    startSession(new Date(Date.now() + 1000).toISOString());
    vi.advanceTimersByTime(1000);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/area', LoginPage);

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(TestBed.inject(SessionService).currentSession()).toBeNull();
    backend.expectNone(() => true);
  });

  // Sessão válida permite a visão protegida e envia apenas as leituras/avaliação previstas com Bearer.
  it('carrega cartões e conexão de /area com a sessão atual', async () => {
    startSession();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/area', AuthenticatedAreaPage);
    const cardsRequest = backend.expectOne(`${API_BASE}/api/cards`);
    const stateRequest = backend.expectOne(`${API_BASE}/api/connections/current/sync-state`);
    const entryRequest = backend.expectOne(`${API_BASE}/api/connections/current/sync/on-entry`);

    expect(cardsRequest.request.method).toBe('GET');
    expect(stateRequest.request.method).toBe('GET');
    expect(entryRequest.request.method).toBe('POST');
    expect(cardsRequest.request.headers.get('Authorization')).toBe('Bearer synthetic-area-token');
    expect(stateRequest.request.headers.get('Authorization')).toBe('Bearer synthetic-area-token');
    expect(entryRequest.request.headers.get('Authorization')).toBe('Bearer synthetic-area-token');
    cardsRequest.flush([]);
    stateRequest.flush({
      updateNeeded: false,
      reauthorizationRequired: false,
      lastSuccessfulImportAtUtc: null,
      sourceUpdatedAtUtc: null,
      lastAttemptAtUtc: null,
      nextAllowedAtUtc: null,
      latestExecution: null,
    });
    entryRequest.flush({ decision: 'not_required' });
    harness.fixture.detectChanges();
    const content = harness.routeNativeElement?.textContent?.toLocaleLowerCase('pt-BR') ?? '';

    expect(TestBed.inject(Router).url).toBe('/area');
    expect(content).toContain('cartões e conexão');
    expect(content).toContain('não há cartões disponíveis nos dados persistidos');
    expect(content).not.toMatch(/r\$\s*[\d.,]+|\b\d{1,3}(?:\.\d{3})*(?:,\d{2})\b/);
    expect(content).not.toMatch(/saldo|fatura|investimento|movimento/);
    backend.expectNone(() => true);
  });
});
