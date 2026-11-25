import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from './session';

const INITIAL_TIME = Date.parse('2026-11-25T12:00:00.000Z');
const TOKEN_LIFETIME_MS = 30 * 60 * 1000;

// Fornece um contrato sintético estável para testar sessão sem HTTP, dados pessoais ou backend.
function validLoginResponse(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    accessToken: 'synthetic-access-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + TOKEN_LIFETIME_MS).toISOString(),
    ...overrides,
  };
}

// Controla relógio e temporizadores para provar expiração sem esperar tempo real.
describe('Sessão volátil', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(INITIAL_TIME);
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  // Valida Bearer e expiração futura antes de disponibilizar uma geração para chamadas protegidas.
  it('inicia uma sessão válida somente com resposta Bearer coerente', () => {
    const service = TestBed.inject(SessionService);

    const generation = service.startSession(validLoginResponse());

    expect(generation).toBe(1);
    expect(service.isAuthenticated()).toBe(true);
    expect(service.currentSession()).toEqual({
      accessToken: 'synthetic-access-token',
      expiresAtUtc: new Date(INITIAL_TIME + TOKEN_LIFETIME_MS).toISOString(),
      generation: 1,
    });
  });

  // Respostas malformadas, vencidas ou incompatíveis com o contrato não criam sessão nem timer.
  it.each([
    ['token ausente', { accessToken: '' }],
    ['token com espaços', { accessToken: ' token ' }],
    ['tipo de token incorreto', { tokenType: 'Basic' }],
    ['duração incompatível', { expiresIn: 900 }],
    ['expiração malformada', { expiresAtUtc: 'not-a-timestamp' }],
    ['expiração sem fuso UTC', { expiresAtUtc: '2026-11-25T12:30:00' }],
    ['expiração já vencida', { expiresAtUtc: new Date(INITIAL_TIME - 1).toISOString() }],
    ['expiração distante demais', { expiresAtUtc: new Date(INITIAL_TIME + TOKEN_LIFETIME_MS * 2).toISOString() }],
  ])('não inicia sessão quando a resposta tem %s', (_reason, override) => {
    const service = TestBed.inject(SessionService);

    expect(service.startSession(validLoginResponse(override))).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentSession()).toBeNull();
    expect(vi.getTimerCount()).toBe(0);
  });

  // Uma falha de validação posterior não substitui a sessão que já estava ativa.
  it('preserva a sessão atual quando uma resposta nova é inválida', () => {
    const service = TestBed.inject(SessionService);
    service.startSession(validLoginResponse());
    const original = service.currentSession();

    expect(service.startSession(validLoginResponse({ tokenType: 'Basic' }))).toBeNull();
    expect(service.currentSession()).toEqual(original);
  });

  // O temporizador encerra a sessão no instante informado pelo servidor sem aguardar outra chamada.
  it('encerra passivamente a sessão quando a expiração chega', () => {
    const service = TestBed.inject(SessionService);
    service.startSession(validLoginResponse());
    expect(service.isAuthenticated()).toBe(true);

    vi.advanceTimersByTime(TOKEN_LIFETIME_MS);

    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentSession()).toBeNull();
    expect(service.expiresAtUtc()).toBeNull();
  });

  // Um injector novo simula reload: como nada foi persistido, ele não restaura a sessão anterior.
  it('não restaura a sessão depois de recriar o injector da aplicação', () => {
    const original = TestBed.inject(SessionService);
    original.startSession(validLoginResponse());
    expect(original.isAuthenticated()).toBe(true);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const reloaded = TestBed.inject(SessionService);

    expect(reloaded.isAuthenticated()).toBe(false);
    expect(reloaded.currentSession()).toBeNull();
  });

  // Uma resposta ligada à geração anterior não consegue apagar uma sessão iniciada depois.
  it('ignora encerramento tardio de uma geração antiga', () => {
    const service = TestBed.inject(SessionService);
    const oldGeneration = service.startSession(validLoginResponse());
    const newGeneration = service.startSession(
      validLoginResponse({ accessToken: 'new-synthetic-access-token' }),
    );

    expect(oldGeneration).not.toBeNull();
    expect(newGeneration).toBeGreaterThan(oldGeneration!);
    expect(service.endSessionForGeneration(oldGeneration!)).toBe(false);
    expect(service.currentSession()?.accessToken).toBe('new-synthetic-access-token');
    expect(service.isAuthenticated()).toBe(true);
  });
});
