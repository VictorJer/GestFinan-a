import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import {
  apiAuthInterceptor,
  LocalSessionRequiredError,
  ProtectedApiRequestError,
} from './api-auth.interceptor';
import { SessionService } from './session';

const API_BASE = 'https://api.example.test/proxy/';

// Fornece sessão sintética sem credenciais pessoais e configura o transporte isolado para cada teste.
function configureHttp(apiBaseUrl: string | null = API_BASE): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: apiBaseUrl },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
}

// Inicia uma concessão local com o formato exato esperado do backend autenticado.
function startSession(token = 'synthetic-interceptor-token'): void {
  TestBed.inject(SessionService).startSession({
    accessToken: token,
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  });
}

// Prova o isolamento de destino e a anexação de token sem abrir sockets ou acessar a API real.
describe('Interceptor de sessão da API', () => {
  beforeEach(() => configureHttp());

  // Só a URL dentro da origem e do prefixo configurados recebe o token da sessão válida.
  it('anexa Bearer à rota protegida da base autorizada', () => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);

    client.get(`${API_BASE}api/auth/logout`).subscribe();
    const request = backend.expectOne(`${API_BASE}api/auth/logout`);

    expect(request.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-interceptor-token',
    );
    request.flush(null);
  });

  // Subdomínio, esquema, porta e prefixo textual divergentes nunca recebem o token local.
  it.each([
    ['prefixo semelhante', 'https://api.example.test/proxy-extra/api/auth/logout'],
    ['subdomínio', 'https://sub.api.example.test/proxy/api/auth/logout'],
    ['esquema diferente', 'http://api.example.test/proxy/api/auth/logout'],
    ['porta diferente', 'https://api.example.test:8443/proxy/api/auth/logout'],
  ])('não encaminha Bearer para destino com %s', (_reason, url) => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);

    client.get(url, { headers: { Authorization: 'Bearer stale-sentinel' } }).subscribe();
    const request = backend.expectOne(url);

    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  // O login e endpoints fora do namespace protegido permanecem sem Bearer, mesmo com header antigo.
  it('remove Bearer de login público e de páginas fora da API protegida', () => {
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const loginUrl = `${API_BASE}api/auth/login`;
    const healthUrl = `${API_BASE}health`;

    client.post(loginUrl, {}, { headers: { Authorization: 'Bearer stale-sentinel' } }).subscribe();
    client.get(healthUrl, { headers: { Authorization: 'Bearer stale-sentinel' } }).subscribe();

    for (const url of [loginUrl, healthUrl]) {
      const request = backend.expectOne(url);
      expect(request.request.headers.has('Authorization')).toBe(false);
      request.flush({});
    }
  });

  // Sem sessão válida, uma chamada protegida falha localmente e não alcança nenhum servidor.
  it('não envia chamada protegida quando não há sessão local', () => {
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    let receivedError: unknown;

    client.get(`${API_BASE}api/auth/logout`).subscribe({
      error: (error: unknown) => {
        receivedError = error;
      },
    });

    expect(receivedError).toBeInstanceOf(LocalSessionRequiredError);
    backend.expectNone(`${API_BASE}api/auth/logout`);
  });

  // 401 da geração atual encerra a sessão e fornece um erro seguro para orientar novo login.
  it('encerra a sessão atual em 401 protegido sem expor o corpo da API', () => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    let receivedError: unknown;

    client.get(`${API_BASE}api/private/resource`).subscribe({
      error: (error: unknown) => {
        receivedError = error;
      },
    });
    backend.expectOne(`${API_BASE}api/private/resource`).flush(
      { message: 'sensitive-server-detail' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect((receivedError as ProtectedApiRequestError).kind).toBe('unauthorized');
    expect((receivedError as Error).message).not.toContain('sensitive-server-detail');
    expect(session.currentSession()).toBeNull();
    expect(session.notice()).toBe('unauthorized');
  });

  // 403 não encerra uma sessão válida e permanece distinto de credencial expirada.
  it('preserva a sessão atual em 403 protegido', () => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    let receivedError: unknown;

    client.get(`${API_BASE}api/private/resource`).subscribe({
      error: (error: unknown) => {
        receivedError = error;
      },
    });
    backend.expectOne(`${API_BASE}api/private/resource`).flush(
      { message: 'sensitive-server-detail' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect((receivedError as ProtectedApiRequestError).kind).toBe('forbidden');
    expect(session.currentSession()?.accessToken).toBe('synthetic-interceptor-token');
    expect(session.notice()).toBe('forbidden');
  });

  // Falha de rede e indisponibilidade HTTP têm categorias próprias e não invalidam a sessão.
  it.each([
    ['rede', 'network'],
    ['503', 'unavailable'],
  ] as const)('distingue falha de %s de credencial inválida', (failure, expectedKind) => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    let receivedError: unknown;

    client.get(`${API_BASE}api/private/resource`).subscribe({
      error: (error: unknown) => {
        receivedError = error;
      },
    });
    const request = backend.expectOne(`${API_BASE}api/private/resource`);
    if (failure === 'rede') {
      request.error(new ProgressEvent('error'));
    } else {
      request.flush({}, { status: 503, statusText: 'Service Unavailable' });
    }

    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect((receivedError as ProtectedApiRequestError).kind).toBe(expectedKind);
    expect(session.currentSession()?.accessToken).toBe('synthetic-interceptor-token');
    expect(session.notice()).toBe(expectedKind);
  });

  // Resposta de uma chamada antiga é classificada como obsoleta e não encerra login posterior.
  it('ignora 401 atrasado de uma geração anterior', () => {
    startSession('synthetic-old-token');
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    let receivedError: unknown;

    client.get(`${API_BASE}api/private/resource`).subscribe({
      error: (error: unknown) => {
        receivedError = error;
      },
    });
    const request = backend.expectOne(`${API_BASE}api/private/resource`);
    startSession('synthetic-new-token');
    request.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect((receivedError as ProtectedApiRequestError).kind).toBe('stale');
    expect(session.currentSession()?.accessToken).toBe('synthetic-new-token');
    expect(session.notice()).toBeNull();
  });

  // O 401 de login público pertence ao formulário e não encerra uma sessão anterior.
  it('não reclassifica nem encerra sessão por 401 do login público', () => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    let receivedError: unknown;

    client.post(`${API_BASE}api/auth/login`, {}).subscribe({
      error: (error: unknown) => {
        receivedError = error;
      },
    });
    backend.expectOne(`${API_BASE}api/auth/login`).flush(
      {},
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(receivedError).toBeInstanceOf(HttpErrorResponse);
    expect((receivedError as HttpErrorResponse).status).toBe(401);
    expect(receivedError).not.toBeInstanceOf(ProtectedApiRequestError);
    expect(session.currentSession()?.accessToken).toBe('synthetic-interceptor-token');
  });

  // Configuração ausente ou inválida desabilita o uso do Bearer em vez de inferir outro destino.
  it.each([null, 'ftp://api.example.test/proxy'])('não anexa token com base %s', (apiBaseUrl) => {
    TestBed.resetTestingModule();
    configureHttp(apiBaseUrl);
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const url = 'https://api.example.test/proxy/api/auth/logout';

    client.get(url, { headers: { Authorization: 'Bearer stale-sentinel' } }).subscribe();
    const request = backend.expectOne(url);

    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });
});
