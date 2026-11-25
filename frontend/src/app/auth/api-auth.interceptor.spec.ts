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
const MANUAL_SYNC_URL = `${API_BASE}api/connections/current/sync`;

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

  // 409 só vira execução ativa com o código exato e GUID válido; texto externo nunca chega à aplicação.
  it('classifica execução ativa e permite somente o syncRunId validado', () => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    let receivedError: unknown;

    client.post(MANUAL_SYNC_URL, null, { headers: { 'Idempotency-Key': 'synthetic-intent-key' } }).subscribe({
      error: (error: unknown) => (receivedError = error),
    });
    backend.expectOne(MANUAL_SYNC_URL).flush(
      {
        code: 'SYNC_RUN_ALREADY_ACTIVE',
        syncRunId: '4b2eaef0-a819-45d9-b90f-dc61531985b1',
        title: 'sentinel de mensagem externa',
        traceId: 'synthetic-trace-secret',
      },
      { status: 409, statusText: 'Conflict' },
    );

    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect((receivedError as ProtectedApiRequestError).kind).toBe('sync-active');
    expect((receivedError as ProtectedApiRequestError).syncMetadata).toEqual({
      syncRunId: '4b2eaef0-a819-45d9-b90f-dc61531985b1',
      nextAllowedAtUtc: null,
      retryAfterSeconds: null,
    });
    expect((receivedError as Error).message).not.toContain('sentinel');
    expect(JSON.stringify(receivedError)).not.toContain('synthetic-trace-secret');
  });

  // 429 só expõe instante e Retry-After com formato permitido e coerente com o contrato.
  it('classifica intervalo e valida metadados opcionais', () => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    let receivedError: unknown;

    client.post(MANUAL_SYNC_URL, null, { headers: { 'Idempotency-Key': 'synthetic-intent-key' } }).subscribe({
      error: (error: unknown) => (receivedError = error),
    });
    backend.expectOne(MANUAL_SYNC_URL).flush(
      {
        code: 'SYNC_RUN_MINIMUM_INTERVAL_ACTIVE',
        nextAllowedAtUtc: '2026-11-25T07:10:00Z',
        syncRunId: 'https://external.example.test/private',
        title: 'sentinel de mensagem externa',
      },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '300' },
      },
    );

    expect((receivedError as ProtectedApiRequestError).kind).toBe('sync-interval');
    expect((receivedError as ProtectedApiRequestError).syncMetadata).toEqual({
      syncRunId: null,
      nextAllowedAtUtc: '2026-11-25T07:10:00Z',
      retryAfterSeconds: 300,
    });
    expect((receivedError as Error).message).not.toContain('sentinel');
  });

  // Códigos allowlisted não tornam válidos timestamp impossível nem cabeçalho fora do formato numérico.
  it('descarta metadados malformados e mantém apenas a categoria fixa do bloqueio', () => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    let receivedError: unknown;

    client.post(MANUAL_SYNC_URL, null, { headers: { 'Idempotency-Key': 'synthetic-intent-key' } }).subscribe({
      error: (error: unknown) => (receivedError = error),
    });
    backend.expectOne(MANUAL_SYNC_URL).flush(
      {
        code: 'SYNC_RUN_MINIMUM_INTERVAL_ACTIVE',
        nextAllowedAtUtc: '2026-99-99T99:99:99Z',
        title: 'malicious raw body sentinel',
      },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '999999999999999999999999' },
      },
    );

    expect((receivedError as ProtectedApiRequestError).syncMetadata).toEqual({
      syncRunId: null,
      nextAllowedAtUtc: null,
      retryAfterSeconds: null,
    });
    expect(JSON.stringify(receivedError)).not.toContain('malicious raw body sentinel');
  });

  // Uma extensão ausente não quebra a classificação contratual nem cria metadados presumidos.
  it.each([
    [409, { code: 'SYNC_RUN_ALREADY_ACTIVE' }, 'sync-active'],
    [429, {}, 'sync-interval'],
    [503, {}, 'acceptance-indeterminate'],
  ] as const)('mantém categoria segura para %s sem extensões completas', (status, body, kind) => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    let receivedError: unknown;

    client.post(MANUAL_SYNC_URL, null, { headers: { 'Idempotency-Key': 'synthetic-intent-key' } }).subscribe({
      error: (error: unknown) => (receivedError = error),
    });
    backend.expectOne(MANUAL_SYNC_URL).flush(body, { status, statusText: 'Synthetic failure' });

    expect((receivedError as ProtectedApiRequestError).kind).toBe(kind);
    expect((receivedError as ProtectedApiRequestError).syncMetadata).toEqual(
      kind === 'acceptance-indeterminate'
        ? null
        : { syncRunId: null, nextAllowedAtUtc: null, retryAfterSeconds: null },
    );
  });

  // O transporte mantém as reações de autenticação existentes nas duas rotas que criam intenção.
  it.each([
    [401, 'unauthorized'],
    [403, 'forbidden'],
  ] as const)('preserva a classificação de autenticação HTTP %s no POST de sincronização', (status, kind) => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    let receivedError: unknown;

    client.post(MANUAL_SYNC_URL, null, { headers: { 'Idempotency-Key': 'synthetic-intent-key' } }).subscribe({
      error: (error: unknown) => (receivedError = error),
    });
    backend.expectOne(MANUAL_SYNC_URL).flush(
      { code: 'external-auth-sentinel', title: 'private server text' },
      { status, statusText: 'Authentication failure' },
    );

    expect((receivedError as ProtectedApiRequestError).kind).toBe(kind);
    expect((receivedError as Error).message).not.toContain('private server text');
    expect(TestBed.inject(SessionService).isAuthenticated()).toBe(status === 403);
  });

  // Metadados de sync em URL externa não são interpretados e o Bearer local continua removido.
  it('não interpreta corpo de endpoint externo como falha de aceite', () => {
    startSession();
    const client = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const externalUrl = 'https://external.example.test/proxy/api/connections/current/sync';
    let receivedError: unknown;

    client
      .post(externalUrl, null, {
        headers: {
          Authorization: 'Bearer caller-supplied-stale-token',
          'Idempotency-Key': 'synthetic-intent-key',
        },
      })
      .subscribe({ error: (error: unknown) => (receivedError = error) });
    const request = backend.expectOne(externalUrl);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(
      { code: 'ACCEPTANCE_RESULT_INDETERMINATE', title: 'external response' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(receivedError).toBeInstanceOf(HttpErrorResponse);
    expect(receivedError).not.toBeInstanceOf(ProtectedApiRequestError);
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
