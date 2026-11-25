import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { CardConnectionStateService } from './card-connection-state';
import { SyncIntentCoordinator } from './sync-intent-coordinator';

const API_BASE = 'https://api.example.test/proxy/';
const ON_ENTRY_URL = `${API_BASE}api/connections/current/sync/on-entry`;
const MANUAL_URL = `${API_BASE}api/connections/current/sync`;
const CARDS_URL = `${API_BASE}api/cards`;
const SYNC_STATE_URL = `${API_BASE}api/connections/current/sync-state`;

let backend: HttpTestingController;
let coordinator: SyncIntentCoordinator;
let connectionState: CardConnectionStateService;
let session: SessionService;

// Compõe os serviços reais do frontend com HttpTestingController, sem acessar API ou dados financeiros reais.
function configureCoordinator(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  coordinator = TestBed.inject(SyncIntentCoordinator);
  connectionState = TestBed.inject(CardConnectionStateService);
  session = TestBed.inject(SessionService);
}

// Inicia uma geração sintética com token não operacional para verificar o vínculo das intenções à sessão.
function startSession(): number {
  return session.startSession({
    accessToken: 'synthetic-sync-intent-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  })!;
}

// Confirma rota, método e chave ASCII visível exigida pelo contrato de idempotência do backend.
function expectOnEntryRequest(): TestRequest {
  const request = backend.expectOne(ON_ENTRY_URL);
  const key = request.request.headers.get('Idempotency-Key');
  expect(request.request.method).toBe('POST');
  expect(key).toMatch(/^[!-~]{1,128}$/);
  expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-sync-intent-token');
  return request;
}

// Valida o POST manual e devolve a chamada para conferir chave e resultado sem expor segredo real.
function expectManualRequest(): TestRequest {
  const request = backend.expectOne(MANUAL_URL);
  expect(request.request.method).toBe('POST');
  expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[!-~]{1,128}$/);
  expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-sync-intent-token');
  return request;
}

// Evita vazamento de requests entre casos e ignora somente chamadas explicitamente canceladas.
describe('Coordenador de intenção on-entry', () => {
  beforeEach(() => configureCoordinator());

  afterEach(() => backend.verify({ ignoreCancelled: true }));

  // Consultas GET e retorno à rota não repetem o POST já registrado na geração atual.
  it('avalia uma única vez por login mesmo após releituras e retorno à área', () => {
    startSession();
    coordinator.evaluateOnEntry();
    const onEntryRequest = expectOnEntryRequest();
    const key = onEntryRequest.request.headers.get('Idempotency-Key');

    connectionState.loadCards();
    connectionState.loadConnectionState();
    coordinator.evaluateOnEntry();
    coordinator.evaluateOnEntry();

    const cards = backend.expectOne(CARDS_URL);
    const syncState = backend.expectOne(SYNC_STATE_URL);
    expect(cards.request.method).toBe('GET');
    expect(syncState.request.method).toBe('GET');
    cards.flush([]);
    syncState.flush({
      updateNeeded: false,
      reauthorizationRequired: false,
      lastSuccessfulImportAtUtc: null,
      sourceUpdatedAtUtc: null,
      lastAttemptAtUtc: null,
      nextAllowedAtUtc: null,
      latestExecution: null,
    });

    onEntryRequest.flush({ decision: 'not_required' });

    expect(coordinator.state().onEntry).toEqual({
      status: 'not-required',
      syncRunId: null,
      nextAllowedAtUtc: null,
      retryAfterSeconds: null,
    });
    expect(key).toMatch(/^[!-~]{1,128}$/);
    backend.expectNone((request) => request.method === 'POST');
  });

  // A decisão de renovar acesso é exibível como estado fixo, sem propagar guidance do servidor.
  it('preserva a decisão de reautorização sem expor texto remoto', () => {
    startSession();
    coordinator.evaluateOnEntry();
    const request = expectOnEntryRequest();

    request.flush({
      decision: 'reauthorization_required',
      guidance: 'texto remoto não deve integrar o estado da interface',
    });

    expect(coordinator.state().onEntry.status).toBe('reauthorization-required');
    expect(JSON.stringify(coordinator.state())).not.toContain('texto remoto');
  });

  // 202 registra apenas aceite e identificador; não afirma que uma importação terminou.
  it('representa 202 como aceite pendente de acompanhamento', () => {
    startSession();
    coordinator.evaluateOnEntry();
    const request = expectOnEntryRequest();

    request.flush({
      syncRunId: '4b2eaef0-a819-45d9-b90f-dc61531985b1',
    });

    expect(coordinator.state().onEntry).toEqual({
      status: 'accepted',
      syncRunId: '4b2eaef0-a819-45d9-b90f-dc61531985b1',
      nextAllowedAtUtc: null,
      retryAfterSeconds: null,
    });
  });

  // Novo login recebe uma chave e uma avaliação novas; não reaproveita o estado do titular anterior.
  it('inicia uma nova intenção somente depois de uma nova geração de login', () => {
    startSession();
    coordinator.evaluateOnEntry();
    const firstRequest = expectOnEntryRequest();
    const firstKey = firstRequest.request.headers.get('Idempotency-Key');
    firstRequest.flush({ decision: 'not_required' });

    startSession();
    coordinator.evaluateOnEntry();
    const secondRequest = expectOnEntryRequest();
    const secondKey = secondRequest.request.headers.get('Idempotency-Key');
    secondRequest.flush({ decision: 'reauthorization_required' });

    expect(secondKey).not.toBe(firstKey);
    expect(coordinator.state().onEntry.status).toBe('reauthorization-required');
    backend.expectNone((request) => request.method === 'POST');
  });

  // A chave manual difere da automática e cliques concorrentes enquanto o POST aguarda não duplicam pedido.
  it('cria pedido manual distinto e impede um segundo POST concorrente', () => {
    startSession();
    coordinator.evaluateOnEntry();
    const entryRequest = expectOnEntryRequest();
    const entryKey = entryRequest.request.headers.get('Idempotency-Key');
    entryRequest.flush({ decision: 'not_required' });

    coordinator.requestManual();
    const manualRequest = expectManualRequest();
    const manualKey = manualRequest.request.headers.get('Idempotency-Key');
    coordinator.requestManual();

    expect(manualKey).not.toBe(entryKey);
    expect(coordinator.state().manual.status).toBe('submitting');
    backend.expectNone((request) => request.method === 'POST');
    manualRequest.flush({ syncRunId: '2eae7503-5513-487f-88ad-81a9636f912a' });

    expect(coordinator.state().manual.status).toBe('accepted');
    coordinator.requestManual();
    backend.expectNone((request) => request.method === 'POST');
  });

  // Timeout/rede isolam uma intenção indeterminada; só replay explícito reenvia a mesma chave e modo.
  it('reconcilia falha de rede com replay da mesma chave sem criar intenção paralela', () => {
    startSession();
    coordinator.evaluateOnEntry();
    expectOnEntryRequest().flush({ decision: 'not_required' });

    coordinator.requestManual();
    const firstRequest = expectManualRequest();
    const firstKey = firstRequest.request.headers.get('Idempotency-Key');
    firstRequest.error(new ProgressEvent('error'));

    expect(coordinator.state().manual.status).toBe('indeterminate');
    coordinator.requestManual();
    backend.expectNone((request) => request.method === 'POST');

    coordinator.retryIndeterminate('manual');
    const replay = expectManualRequest();
    expect(replay.request.headers.get('Idempotency-Key')).toBe(firstKey);
    replay.flush({ syncRunId: 'ad64ed6f-87a3-44f3-a110-49ece6f82468' });

    expect(coordinator.state().manual.status).toBe('accepted');
    expect(coordinator.state().manual.syncRunId).toBe('ad64ed6f-87a3-44f3-a110-49ece6f82468');
    backend.expectNone((request) => request.method === 'POST');
  });

  // Códigos 409/429/503 representam conflito, intervalo e incerteza; nenhum é apresentado como sucesso.
  it.each([
    [409, { code: 'SYNC_RUN_ALREADY_ACTIVE', syncRunId: 'd446e873-2d5e-4c3a-a1f6-2a3c72a6c164' }, 'active'],
    [429, { code: 'SYNC_RUN_MINIMUM_INTERVAL_ACTIVE', nextAllowedAtUtc: '2026-11-25T08:00:00Z' }, 'interval'],
    [503, { code: 'ACCEPTANCE_RESULT_INDETERMINATE' }, 'indeterminate'],
  ] as const)('não anuncia sucesso para resposta manual HTTP %s', (status, body, expectedStatus) => {
    startSession();
    coordinator.requestManual();
    expectManualRequest().flush(body, { status, statusText: 'Synthetic failure' });

    expect(coordinator.state().manual.status).toBe(expectedStatus);
    expect(coordinator.state().manual.status).not.toBe('accepted');
    expect(coordinator.state().manual.status).not.toBe('succeeded');
  });

  // Uma entrada indeterminada bloqueia pedido manual independente até que a mesma intenção seja repetida.
  it('bloqueia pedido manual enquanto a avaliação de entrada aguarda reconciliação', () => {
    startSession();
    coordinator.evaluateOnEntry();
    const firstRequest = expectOnEntryRequest();
    const firstKey = firstRequest.request.headers.get('Idempotency-Key');
    firstRequest.flush(
      { code: 'ACCEPTANCE_RESULT_INDETERMINATE' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(coordinator.state().onEntry.status).toBe('indeterminate');
    coordinator.requestManual();
    backend.expectNone((request) => request.method === 'POST');

    coordinator.retryIndeterminate('on-entry');
    const replay = expectOnEntryRequest();
    expect(replay.request.headers.get('Idempotency-Key')).toBe(firstKey);
    replay.flush({ decision: 'not_required' });
    coordinator.requestManual();

    expect(coordinator.state().manual.status).toBe('submitting');
    expectManualRequest().flush({ syncRunId: 'db19af40-d80a-414f-989b-b58ed20c19e7' });
  });
});
