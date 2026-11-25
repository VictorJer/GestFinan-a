import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { CardConnectionStateService } from './card-connection-state';
import { vi } from 'vitest';

const API_BASE = 'https://api.example.test/proxy/';
const CARDS_URL = `${API_BASE}api/cards`;
const SYNC_STATE_URL = `${API_BASE}api/connections/current/sync-state`;

let backend: HttpTestingController;
let state: CardConnectionStateService;
let session: SessionService;

// Isola a state machine da qualquer origem financeira real e mantém o mesmo interceptor da aplicação.
function configureState(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  state = TestBed.inject(CardConnectionStateService);
  session = TestBed.inject(SessionService);
}

// Cria uma sessão sintética com expiração controlada para exercitar isolamento entre gerações.
function startSession(expiresAtUtc = new Date(Date.now() + 30 * 60 * 1000).toISOString()): number {
  return session.startSession({
    accessToken: 'synthetic-card-state-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc,
  })!;
}

// Mantém o DTO de teste restrito aos valores de apresentação permitidos pela API.
function cardResponse(name = 'Cartão sintético'): object {
  return {
    id: 'a5a35003-cd81-4f07-9563-5710a034b78a',
    name,
    lastFour: '1234',
    currencyCode: 'BRL',
  };
}

// Confere os estados e limites de sessão sem expor erros brutos ou apagar dados em uma falha transitória.
describe('Estado de leituras de cartão e conexão', () => {
  beforeEach(() => configureState());

  afterEach(() => {
    backend.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  // A leitura progride de loading para empty sem tratar a lista vazia como erro de conexão.
  it('distingue carregamento de uma resposta válida sem cartões', () => {
    startSession();
    state.loadCards();

    expect(state.cards().status).toBe('loading');
    backend.expectOne(CARDS_URL).flush([]);

    expect(state.cards().status).toBe('empty');
    expect(state.cards().snapshot).toEqual([]);
    expect(state.cards().failure).toBeNull();
  });

  // Um GET posterior que falha preserva o último snapshot confirmado e apresenta somente a categoria segura.
  it('retém cartões confirmados durante uma falha transitória de rede', () => {
    startSession();
    state.loadCards();
    backend.expectOne(CARDS_URL).flush([cardResponse()]);
    const confirmedSnapshot = state.cards().snapshot;

    state.loadCards();
    expect(state.cards().status).toBe('loading');
    expect(state.cards().snapshot).toEqual(confirmedSnapshot);
    backend.expectOne(CARDS_URL).error(new ProgressEvent('error'));

    expect(state.cards().status).toBe('error');
    expect(state.cards().failure).toBe('network');
    expect(state.cards().snapshot).toEqual(confirmedSnapshot);
  });

  // Status 404 e 409 são ausência/ambiguidade, não estados conectados nem falhas globais de autenticação.
  it('modela conexão ausente e conexão ambígua sem inventar snapshots', () => {
    startSession();
    state.loadCards();
    backend.expectOne(CARDS_URL).flush(
      { title: 'synthetic not found' },
      { status: 404, statusText: 'Not Found' },
    );
    state.loadConnectionState();
    backend.expectOne(SYNC_STATE_URL).flush(
      { title: 'synthetic conflict' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(state.cards().status).toBe('not-found');
    expect(state.cards().snapshot).toBeNull();
    expect(state.connection().status).toBe('conflict');
    expect(state.connection().snapshot).toBeNull();
    expect(session.notice()).toBeNull();
  });

  // Uma navegação que cancela a leitura não converte resposta pendente em erro nem perde snapshot anterior.
  it('cancela leitura explícita e restaura o último estado confirmado', () => {
    startSession();
    state.loadCards();
    backend.expectOne(CARDS_URL).flush([cardResponse()]);
    state.loadCards();
    const pending = backend.expectOne(CARDS_URL);

    state.cancelPendingReads();

    expect(pending.cancelled).toBe(true);
    expect(state.cards().status).toBe('loaded');
    expect(state.cards().snapshot).toEqual([cardResponse()]);
  });

  // A resposta antiga é descartada mesmo se chegar antes de o efeito terminar a limpeza da geração anterior.
  it('não permite que resposta atrasada de login anterior repovoe o estado', () => {
    startSession();
    state.loadCards();
    const oldRequest = backend.expectOne(CARDS_URL);
    const previousGeneration = state.cards().generation;
    startSession();

    oldRequest.flush([cardResponse('Cartão da sessão anterior')]);
    TestBed.tick();

    expect(state.cards().status).toBe('idle');
    expect(state.cards().snapshot).toBeNull();
    expect(state.cards().generation).not.toBe(previousGeneration);
  });

  // Logout cancela requests protegidos e torna o snapshot invisível assim que a geração termina.
  it('cancela leitura pendente e limpa cartões no logout', () => {
    const generation = startSession();
    state.loadCards();
    const pending = backend.expectOne(CARDS_URL);

    session.endSessionForGeneration(generation);
    TestBed.tick();

    expect(pending.cancelled).toBe(true);
    expect(state.cards().status).toBe('idle');
    expect(state.cards().snapshot).toBeNull();
  });

  // Expiração detectada pelo próprio serviço de sessão aplica a mesma limpeza e cancelamento do logout.
  it('cancela leitura pendente e limpa estado quando a sessão expira', () => {
    vi.useFakeTimers();
    const startedAt = Date.now();
    startSession(new Date(startedAt + 1000).toISOString());
    state.loadConnectionState();
    const pending = backend.expectOne(SYNC_STATE_URL);
    vi.setSystemTime(startedAt + 1001);

    expect(session.currentSession()).toBeNull();
    TestBed.tick();

    expect(pending.cancelled).toBe(true);
    expect(state.connection().status).toBe('idle');
    expect(state.connection().snapshot).toBeNull();
  });
});
