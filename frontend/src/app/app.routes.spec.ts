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
