import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { apiAuthInterceptor } from './api-auth.interceptor';
import { LogoutService } from './logout';
import { SessionService } from './session';
import { vi } from 'vitest';

const API_BASE = 'https://api.example.test/proxy';
const LOGOUT_URL = `${API_BASE}/api/auth/logout`;

let backend: HttpTestingController;
let logout: LogoutService;
let session: SessionService;

// Isola o logout remoto e a fronteira Bearer sem consultar qualquer API real.
function configureLogout(apiBaseUrl: string | null = API_BASE): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: apiBaseUrl },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  logout = TestBed.inject(LogoutService);
  session = TestBed.inject(SessionService);
}

// Cria uma geração de sessão sintética para verificar limpeza e corridas de respostas.
function startSession(token = 'synthetic-logout-token'): void {
  session.startSession({
    accessToken: token,
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  });
}

// Garante confirmação estrita, limpeza local após tentativa e proteção da geração posterior.
describe('Logout remoto', () => {
  beforeEach(() => configureLogout());

  afterEach(() => {
    backend.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  // Somente 204 confirma a revogação e o request leva o Bearer da sessão iniciadora.
  it('confirma exclusivamente resposta 204 e encerra a sessão local', () => {
    startSession();
    logout.logout();
    const request = backend.expectOne(LOGOUT_URL);

    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-logout-token');
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(logout.outcome()).toBe('confirmed');
    expect(logout.isLoggingOut()).toBe(false);
    expect(session.currentSession()).toBeNull();
  });

  // Mesmo um sucesso HTTP convencional não substitui a confirmação contratual 204.
  it('não considera 200 uma confirmação de logout', () => {
    startSession();
    logout.logout();
    backend.expectOne(LOGOUT_URL).flush(null, { status: 200, statusText: 'OK' });

    expect(logout.outcome()).toBe('uncertain');
    expect(session.currentSession()).toBeNull();
  });

  // 401 não prova confirmação; a limpeza pode ocorrer no interceptor, mas o resultado é incerto.
  it('trata 401 como saída remota não confirmada e não repete o request', () => {
    startSession();
    logout.logout();
    const request = backend.expectOne(LOGOUT_URL);
    request.flush({ detail: 'synthetic-server-detail' }, { status: 401, statusText: 'Unauthorized' });

    expect(logout.outcome()).toBe('uncertain');
    expect(session.currentSession()).toBeNull();
    backend.expectNone(LOGOUT_URL);
  });

  // Status HTTP diferente de 204 limpa localmente, sem afirmar que houve rollback no servidor.
  it('trata 503 como confirmação incerta e limpa a sessão', () => {
    startSession();
    logout.logout();
    backend.expectOne(LOGOUT_URL).flush(
      { detail: 'synthetic-server-detail' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(logout.outcome()).toBe('uncertain');
    expect(session.currentSession()).toBeNull();
  });

  // Erro de rede encerra somente o acesso local e não é apresentado como revogação confirmada.
  it('trata falha de rede como saída não confirmada', () => {
    startSession();
    logout.logout();
    backend.expectOne(LOGOUT_URL).error(new ProgressEvent('error'));

    expect(logout.outcome()).toBe('uncertain');
    expect(session.currentSession()).toBeNull();
  });

  // Timeout cancela a espera, limpa a sessão e mantém a confirmação remota desconhecida.
  it('trata timeout como saída não confirmada', () => {
    vi.useFakeTimers();
    startSession();
    logout.logout();
    const request = backend.expectOne(LOGOUT_URL);
    vi.advanceTimersByTime(15_000);

    expect(request.cancelled).toBe(true);
    expect(logout.outcome()).toBe('uncertain');
    expect(session.currentSession()).toBeNull();
  });

  // Clique duplicado durante a tentativa não envia outra solicitação de revogação.
  it('bloqueia logout duplicado enquanto a tentativa está pendente', () => {
    startSession();
    logout.logout();
    logout.logout();
    const requests = backend.match(LOGOUT_URL);

    expect(requests).toHaveLength(1);
    expect(logout.isLoggingOut()).toBe(true);
    requests[0].flush(null, { status: 204, statusText: 'No Content' });
  });

  // A resposta de logout anterior pode confirmar sua chamada, mas não altera a sessão nova.
  it('preserva novo login quando logout antigo termina depois', () => {
    startSession('synthetic-old-logout-token');
    logout.logout();
    const request = backend.expectOne(LOGOUT_URL);
    startSession('synthetic-new-login-token');
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(session.currentSession()?.accessToken).toBe('synthetic-new-login-token');
    expect(logout.outcome()).toBeNull();
    expect(logout.isLoggingOut()).toBe(false);
  });

  // Sem URL confiável, encerra a memória local e informa que o servidor não foi confirmado.
  it('limpa localmente e informa incerteza quando não há URL de API', () => {
    TestBed.resetTestingModule();
    configureLogout(null);
    startSession();

    logout.logout();

    expect(logout.outcome()).toBe('uncertain');
    expect(session.currentSession()).toBeNull();
    backend.expectNone(() => true);
  });
});
