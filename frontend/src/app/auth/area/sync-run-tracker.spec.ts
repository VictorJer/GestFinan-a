import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { CardConnectionStateService } from './card-connection-state';
import { SyncRunTracker } from './sync-run-tracker';

const API_BASE = 'https://api.example.test/proxy/';
const SYNC_RUN_ID = '4b2eaef0-a819-45d9-b90f-dc61531985b1';
const SYNC_RUN_URL = `${API_BASE}api/sync-runs/${SYNC_RUN_ID}`;
const CARDS_URL = `${API_BASE}api/cards`;
const SYNC_STATE_URL = `${API_BASE}api/connections/current/sync-state`;

let backend: HttpTestingController;
let tracker: SyncRunTracker;
let connectionState: CardConnectionStateService;
let session: SessionService;

// Compõe tracker, API e estado da tela com transporte sintético, sem abrir sockets ou ler dados reais.
function configureTracker(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  tracker = TestBed.inject(SyncRunTracker);
  connectionState = TestBed.inject(CardConnectionStateService);
  session = TestBed.inject(SessionService);
}

// Cria uma sessão sintética para exercitar cancelamento, geração e Bearer de cada GET.
function startSession(): number {
  return session.startSession({
    accessToken: 'synthetic-sync-run-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  })!;
}

// Representa somente o DTO operacional necessário para determinar se a execução ainda está ativa.
function runResponse(state: string): object {
  return {
    syncRunId: SYNC_RUN_ID,
    state,
    finishedAtUtc: state === 'Succeeded' || state === 'Failed' ? '2026-11-25T10:05:00Z' : null,
    coverageState: 'Unknown',
    failureCode: state === 'Failed' ? 'PROVIDER_UNAVAILABLE' : null,
  };
}

// Monta o snapshot mínimo do endpoint persistido, preservando cobertura/horários sem presumir sucesso.
function syncStateResponse(
  syncRunId: string,
  state: string,
  lastSuccessfulImportAtUtc: string | null,
): object {
  return {
    updateNeeded: false,
    reauthorizationRequired: false,
    lastSuccessfulImportAtUtc,
    sourceUpdatedAtUtc: null,
    lastAttemptAtUtc: '2026-11-25T10:05:00Z',
    nextAllowedAtUtc: null,
    latestExecution: {
      syncRunId,
      state,
      createdAtUtc: '2026-11-25T10:00:00Z',
      lastAttemptAtUtc: '2026-11-25T10:05:00Z',
      finishedAtUtc: '2026-11-25T10:05:00Z',
      coverageState: 'Unknown',
      failureCode: null,
    },
  };
}

// Mantém o transporte limpo e evita deixar timers globais alterados entre provas.
describe('Acompanhamento limitado de SyncRun', () => {
  beforeEach(() => configureTracker());

  afterEach(() => {
    backend.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  // Execução ativa é consultada após intervalo fixo; estado terminal provoca apenas novas leituras GET.
  it('aguarda estado ativo, lê snapshots ao terminar e não cria nova intenção', () => {
    vi.useFakeTimers();
    startSession();
    tracker.start(SYNC_RUN_ID);
    const firstStatus = backend.expectOne(SYNC_RUN_URL);
    expect(firstStatus.request.method).toBe('GET');
    firstStatus.flush(runResponse('Running'));

    expect(tracker.state().status).toBe('tracking');
    vi.advanceTimersByTime(2000);
    const terminalStatus = backend.expectOne(SYNC_RUN_URL);
    terminalStatus.flush(runResponse('Failed'));

    const cards = backend.expectOne(CARDS_URL);
    const syncState = backend.expectOne(SYNC_STATE_URL);
    expect(cards.request.method).toBe('GET');
    expect(syncState.request.method).toBe('GET');
    cards.flush([]);
    syncState.flush(syncStateResponse(SYNC_RUN_ID, 'Failed', null));

    expect(tracker.state().status).toBe('failed');
    expect(connectionState.connection().snapshot?.lastSuccessfulImportAtUtc).toBeNull();
    backend.expectNone((request) => request.method === 'POST');
  });

  // Sucesso só aparece quando o snapshot novo aponta para a mesma execução e confirma horário persistido.
  it('só confirma sucesso após correlação com o estado persistido atualizado', () => {
    startSession();
    tracker.start(SYNC_RUN_ID);
    backend.expectOne(SYNC_RUN_URL).flush(runResponse('Succeeded'));

    const cards = backend.expectOne(CARDS_URL);
    const syncState = backend.expectOne(SYNC_STATE_URL);
    cards.flush([
      {
        id: 'a5a35003-cd81-4f07-9563-5710a034b78a',
        name: 'Cartão sintético',
        lastFour: '1234',
        currencyCode: 'BRL',
      },
    ]);
    syncState.flush(syncStateResponse(SYNC_RUN_ID, 'Succeeded', '2026-11-25T10:05:00Z'));

    expect(tracker.state().status).toBe('succeeded');
    expect(connectionState.cards().status).toBe('loaded');
    backend.expectNone((request) => request.method === 'POST');
  });

  // Um sucesso isolado da SyncRun não prevalece quando o novo snapshot não a associa ao último sucesso.
  it('marca confirmação pendente se o snapshot não corresponde à execução observada', () => {
    startSession();
    tracker.start(SYNC_RUN_ID);
    backend.expectOne(SYNC_RUN_URL).flush(runResponse('Succeeded'));

    backend.expectOne(CARDS_URL).flush([]);
    backend.expectOne(SYNC_STATE_URL).flush(
      syncStateResponse('d446e873-2d5e-4c3a-a1f6-2a3c72a6c164', 'Succeeded', null),
    );

    expect(tracker.state().status).toBe('unconfirmed');
    expect(tracker.state().failure).toBe('unavailable');
    backend.expectNone((request) => request.method === 'POST');
  });

  // Sair da área cancela o GET em curso; reabrir retoma o mesmo SyncRunId sem repetir aceitação.
  it('cancela na saída e retoma a mesma execução na entrada seguinte', () => {
    startSession();
    tracker.start(SYNC_RUN_ID);
    const firstRequest = backend.expectOne(SYNC_RUN_URL);
    tracker.stop();

    expect(firstRequest.cancelled).toBe(true);
    expect(tracker.state().status).toBe('idle');
    tracker.start(SYNC_RUN_ID);
    backend.expectOne(SYNC_RUN_URL).flush(runResponse('Failed'));
    backend.expectOne(CARDS_URL).flush([]);
    backend.expectOne(SYNC_STATE_URL).flush(syncStateResponse(SYNC_RUN_ID, 'Failed', null));

    expect(tracker.state().status).toBe('failed');
    backend.expectNone((request) => request.method === 'POST');
  });

  // Deadline absoluto cancela request pendente em vez de deixar polling ou espera sem limite.
  it('encerra em 60 segundos com estado não confirmado e sem nova consulta', () => {
    vi.useFakeTimers();
    startSession();
    tracker.start(SYNC_RUN_ID);
    const pending = backend.expectOne(SYNC_RUN_URL);

    vi.advanceTimersByTime(60_000);

    expect(pending.cancelled).toBe(true);
    expect(tracker.state()).toEqual({
      status: 'unconfirmed',
      syncRunId: SYNC_RUN_ID,
      failure: 'timeout',
    });
    backend.expectNone(SYNC_RUN_URL);
    backend.expectNone((request) => request.method === 'POST');
  });

  // Logout termina a geração e cancela imediatamente consulta de execução e estado visível.
  it('cancela acompanhamento e oculta estado no logout', () => {
    const generation = startSession();
    tracker.start(SYNC_RUN_ID);
    const pending = backend.expectOne(SYNC_RUN_URL);

    session.endSessionForGeneration(generation);
    TestBed.tick();

    expect(pending.cancelled).toBe(true);
    expect(tracker.state().status).toBe('idle');
  });
});
