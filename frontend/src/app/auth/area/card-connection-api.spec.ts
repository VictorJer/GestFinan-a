import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor, ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  CardConnectionApi,
  CardConnectionPayloadError,
  CardSummary,
  ConnectionSyncState,
} from './card-connection-api';

const API_BASE = 'https://api.example.test/proxy/';
const CARDS_URL = `${API_BASE}api/cards`;
const SYNC_STATE_URL = `${API_BASE}api/connections/current/sync-state`;
const SYNC_RUN_ID = '4b2eaef0-a819-45d9-b90f-dc61531985b1';
const SYNC_RUN_URL = `${API_BASE}api/sync-runs/${SYNC_RUN_ID}`;

let backend: HttpTestingController;
let api: CardConnectionApi;
let session: SessionService;

// Configura o cliente Angular com a mesma base/interceptor da aplicação e um backend HTTP totalmente sintético.
function configureApi(apiBaseUrl: string | null = API_BASE): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: apiBaseUrl },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  api = TestBed.inject(CardConnectionApi);
  session = TestBed.inject(SessionService);
}

// Cria uma sessão artificial com token não operacional para verificar geração e fronteira Bearer.
function startSession(token = 'synthetic-card-read-token'): number {
  return session.startSession({
    accessToken: token,
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  })!;
}

// Impede que uma leitura GET passe a disparar intenção de sincronização por efeito colateral.
function expectNoPostRequests(): void {
  backend.expectNone((request) => request.method === 'POST');
}

// Confere que os dois métodos fazem somente leituras independentes e respeitam o contrato persistido.
describe('Cliente de leitura de cartões e conexão', () => {
  beforeEach(() => configureApi());

  afterEach(() => backend.verify());

  // Lista vazia continua vazia, usa a base configurada e anexa somente o Bearer da geração atual.
  it('consulta cartões com GET protegido e preserva uma resposta vazia', () => {
    const generation = startSession();
    let result: { generation: number; value: readonly CardSummary[] } | undefined;

    api.getCards().subscribe((value) => (result = value));
    const request = backend.expectOne(CARDS_URL);

    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-card-read-token');
    request.flush([]);

    expect(result).toEqual({ generation, value: [] });
    expectNoPostRequests();
  });

  // Campos opcionais nulos e campos extras não viram valores presumidos nem escapam da allowlist.
  it('preserva cartões incompletos sem fabricar final, bandeira ou limite', () => {
    startSession();
    let result: { generation: number; value: readonly CardSummary[] } | undefined;

    api.getCards().subscribe((value) => (result = value));
    backend.expectOne(CARDS_URL).flush([
      {
        id: 'a5a35003-cd81-4f07-9563-5710a034b78a',
        name: null,
        lastFour: null,
        currencyCode: 'USD',
        externalCardId: 'synthetic-external-card',
        availableCreditLimit: 900,
      },
    ]);

    expect(result?.value).toEqual([
      {
        id: 'a5a35003-cd81-4f07-9563-5710a034b78a',
        name: null,
        lastFour: null,
        currencyCode: 'USD',
      },
    ]);
    expect(Object.keys(result!.value[0])).toEqual(['id', 'name', 'lastFour', 'currencyCode']);
    expectNoPostRequests();
  });

  // 404 de conexão ausente e 409 de conexão ambígua permanecem erros, sem criar snapshots fictícios.
  it.each([
    { url: CARDS_URL, status: 404, statusText: 'Not Found' },
    { url: SYNC_STATE_URL, status: 409, statusText: 'Conflict' },
  ])('não converte HTTP $status de $url em dados de cartão/conexão', ({ url, status, statusText }) => {
    startSession();
    let result: unknown;
    let receivedError: unknown;
    const requestStream: Observable<unknown> =
      url === CARDS_URL ? api.getCards() : api.getSyncState();

    requestStream.subscribe({
      next: (value: unknown) => (result = value),
      error: (error: unknown) => (receivedError = error),
    });
    backend.expectOne(url).flush(
      { title: 'synthetic problem detail' },
      { status, statusText },
    );

    expect(result).toBeUndefined();
    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect((receivedError as ProtectedApiRequestError).kind).toBe(
      status === 404 ? 'not-found' : 'conflict',
    );
    expectNoPostRequests();
  });

  // Uma falha parcial afeta somente seu GET; a leitura bem-sucedida continua disponível sem sincronização.
  it('mantém independentes os GETs quando a consulta de estado falha parcialmente', () => {
    startSession();
    let cards: { generation: number; value: readonly CardSummary[] } | undefined;
    let state: { generation: number; value: ConnectionSyncState } | undefined;
    let stateError: unknown;

    api.getCards().subscribe((value) => (cards = value));
    api.getSyncState().subscribe({
      next: (value) => (state = value),
      error: (error: unknown) => (stateError = error),
    });

    const cardsRequest = backend.expectOne(CARDS_URL);
    const stateRequest = backend.expectOne(SYNC_STATE_URL);
    stateRequest.flush(
      { title: 'synthetic dependency failure' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    cardsRequest.flush([
      {
        id: 'a5a35003-cd81-4f07-9563-5710a034b78a',
        name: 'Cartão sintético',
        lastFour: '1234',
        currencyCode: 'BRL',
      },
    ]);

    expect(cards?.value).toHaveLength(1);
    expect(state).toBeUndefined();
    expect(stateError).toBeInstanceOf(ProtectedApiRequestError);
    expectNoPostRequests();
  });

  // Timestamps nulos e cobertura Unknown são fatos do backend, não campos para completar com o relógio local.
  it('preserva horários nulos e cobertura desconhecida no estado recebido', () => {
    const generation = startSession();
    let result: { generation: number; value: ConnectionSyncState } | undefined;
    const response: ConnectionSyncState = {
      updateNeeded: true,
      reauthorizationRequired: true,
      lastSuccessfulImportAtUtc: null,
      sourceUpdatedAtUtc: null,
      lastAttemptAtUtc: null,
      nextAllowedAtUtc: null,
      latestExecution: {
        syncRunId: '4b2eaef0-a819-45d9-b90f-dc61531985b1',
        state: 'Failed',
        createdAtUtc: '2026-11-25T10:00:00Z',
        lastAttemptAtUtc: null,
        finishedAtUtc: null,
        coverageState: 'Unknown',
        failureCode: 'AUTH_REQUIRED',
      },
    };

    api.getSyncState().subscribe((value) => (result = value));
    const request = backend.expectOne(SYNC_STATE_URL);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-card-read-token');
    request.flush(response);

    expect(result).toEqual({ generation, value: response });
    expect(result?.value.sourceUpdatedAtUtc).toBeNull();
    expect(result?.value.latestExecution?.coverageState).toBe('Unknown');
    expectNoPostRequests();
  });

  // A consulta de execução usa somente o GUID aceito e projeta os metadados necessários ao acompanhamento.
  it('consulta uma SyncRun própria por GET e descarta campos não previstos', () => {
    const generation = startSession();
    let result: unknown;

    api.getSyncRunStatus(SYNC_RUN_ID).subscribe((value) => (result = value));
    const request = backend.expectOne(SYNC_RUN_URL);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-card-read-token');
    request.flush({
      syncRunId: SYNC_RUN_ID,
      state: 'Running',
      finishedAtUtc: null,
      coverageState: 'Unknown',
      failureCode: null,
      requestedFrom: '2026-01-01',
      sensitiveExternalField: 'must-not-escape',
    });

    expect(result).toEqual({
      generation,
      value: {
        syncRunId: SYNC_RUN_ID,
        state: 'Running',
        finishedAtUtc: null,
        coverageState: 'Unknown',
        failureCode: null,
      },
    });
    expect(JSON.stringify(result)).not.toContain('must-not-escape');
    expectNoPostRequests();
  });

  // ID divergente e entrada que não é GUID não criam consulta de rota sob controle do servidor.
  it('rejeita SyncRunId divergente e não envia identificadores inválidos', () => {
    startSession();
    let receivedError: unknown;

    api.getSyncRunStatus(SYNC_RUN_ID).subscribe({ error: (error: unknown) => (receivedError = error) });
    backend.expectOne(SYNC_RUN_URL).flush({
      syncRunId: 'd446e873-2d5e-4c3a-a1f6-2a3c72a6c164',
      state: 'Succeeded',
      finishedAtUtc: '2026-11-25T10:00:00Z',
      coverageState: 'CompleteForRequestedWindow',
      failureCode: null,
    });

    expect(receivedError).toBeInstanceOf(CardConnectionPayloadError);
    api.getSyncRunStatus('../external').subscribe({ error: (error: unknown) => (receivedError = error) });
    expect(receivedError).toBeInstanceOf(CardConnectionPayloadError);
    backend.expectNone((request) => request.url.includes('sync-runs/../external'));
    expectNoPostRequests();
  });

  // Sem sessão local, nenhuma URL ou request financeiro é produzido pelo serviço.
  it('não envia GET quando a sessão local não existe', () => {
    let receivedError: unknown;

    api.getCards().subscribe({ error: (error: unknown) => (receivedError = error) });

    expect(receivedError).toBeTruthy();
    backend.expectNone(() => true);
  });
});
