import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { InvestmentSimulationInput } from './investment-simulation-api';
import {
  InvestmentSimulationPortfolioValueError,
  InvestmentSimulationStateService,
} from './investment-simulation-state';

const API_BASE = 'https://api.example.test/proxy/';
const SIMULATION_URL = `${API_BASE}api/simulations/calculate`;
const PORTFOLIO_URL = `${API_BASE}api/investments`;
const DEBOUNCE_DELAY_MS = 300;
const INPUT: InvestmentSimulationInput = {
  initialAmount: '100.00',
  monthlyContribution: '10.00',
  annualRatePercent: '0.00',
  years: 1,
};

let backend: HttpTestingController;
let state: InvestmentSimulationStateService;
let session: SessionService;

// O serviço real usa interceptor Bearer e transporte HTTP sintético, sem conta ou API remota.
function configureState(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  state = TestBed.inject(InvestmentSimulationStateService);
  session = TestBed.inject(SessionService);
}

// A expiração configurável permite testar isolamento sem esperar o relógio de produção.
function startSession(expiresAtUtc = new Date(Date.now() + 30 * 60 * 1000).toISOString()): number {
  return session.startSession({
    accessToken: 'synthetic-simulation-state-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc,
  })!;
}

// Fixture estrutural de uma resposta confirmada; os pontos vêm do servidor e não são calculados pelo estado.
function resultResponse(): Record<string, unknown> {
  return {
    points: Array.from({ length: 13 }, (_, month) => ({
      month,
      totalContributed: `${100 + month * 10}.00`,
      projectedValue: `${100 + month * 10}.00`,
    })),
    totalContributed: '220.00',
    projectedValue: '220.00',
    projectedReturn: '0.00',
  };
}

// A resposta protegida preserva somente o resumo da carteira manual e aceita totais acima do teto do simulador.
function portfolioResponse(totalCurrentAmount = '125.75'): Record<string, unknown> {
  return {
    items: [],
    summary: {
      totalAppliedAmount: '0.00',
      totalCurrentAmount,
      difference: totalCurrentAmount,
      currentAmountsByClass: [
        { class: 'fixed_income', label: 'Renda fixa', currentAmount: totalCurrentAmount },
        { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
    },
  };
}

// O estado confirma somente respostas atuais e limpa qualquer chamada financeira fora da rota de cálculo.
describe('Estado do cálculo do simulador', () => {
  beforeEach(() => configureState());

  afterEach(() => {
    backend.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  // Alterações dentro do debounce substituem a entrada anterior e enviam somente o cenário final.
  it('debounce mudanças rápidas e confirma apenas a última entrada', () => {
    vi.useFakeTimers();
    const generation = startSession();
    const latestInput: InvestmentSimulationInput = { ...INPUT, monthlyContribution: '25.00' };

    state.updateInput(INPUT);
    vi.advanceTimersByTime(150);
    state.updateInput(latestInput);
    expect(state.view().status).toBe('debouncing');
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS - 1);
    backend.expectNone(SIMULATION_URL);
    vi.advanceTimersByTime(1);

    const request = backend.expectOne(SIMULATION_URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(latestInput);
    expect(request.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-simulation-state-token',
    );
    request.flush(resultResponse());

    expect(state.view()).toEqual({
      status: 'confirmed',
      generation,
      input: latestInput,
      result: {
        points: Array.from({ length: 13 }, (_, month) => ({
          month,
          totalContributed: `${100 + month * 10}.00`,
          projectedValue: `${100 + month * 10}.00`,
        })),
        totalContributed: '220.00',
        projectedValue: '220.00',
        projectedReturn: '0.00',
      },
      failure: null,
    });
  });

  // Uma entrada nova cancela a requisição HTTP anterior e deixa somente a resposta do input mais recente.
  it('cancela request em trânsito quando as entradas mudam', () => {
    vi.useFakeTimers();
    startSession();
    state.updateInput(INPUT);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const previousRequest = backend.expectOne(SIMULATION_URL);

    const latestInput = { ...INPUT, annualRatePercent: '5.00' };
    state.updateInput(latestInput);
    expect(previousRequest.cancelled).toBe(true);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const latestRequest = backend.expectOne(SIMULATION_URL);
    expect(latestRequest.request.body).toEqual(latestInput);
    latestRequest.flush(resultResponse());

    expect(state.view().status).toBe('confirmed');
    expect(state.view().input).toEqual(latestInput);
  });

  // Formulário inválido cancela a resposta pendente e não deixa resultado anterior aparecer como atual.
  it('limpa resultado confirmado e request pendente quando a entrada fica inválida', () => {
    vi.useFakeTimers();
    const generation = startSession();
    state.updateInput(INPUT);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    backend.expectOne(SIMULATION_URL).flush(resultResponse());
    expect(state.view().status).toBe('confirmed');

    state.updateInput({ ...INPUT, monthlyContribution: '15.00' });
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const pendingRequest = backend.expectOne(SIMULATION_URL);
    state.updateInput(null);

    expect(pendingRequest.cancelled).toBe(true);
    expect(state.view()).toEqual({
      status: 'idle',
      generation,
      input: null,
      result: null,
      failure: null,
    });
    backend.expectNone(() => true);
  });

  // Uma falha de rede vira categoria fixa recuperável e não mantém dados antigos nem detalhe do transporte.
  it('publica falha de rede sem incluir detalhes da resposta', () => {
    vi.useFakeTimers();
    const generation = startSession();
    state.updateInput(INPUT);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    backend.expectOne(SIMULATION_URL).error(new ProgressEvent('error'));

    expect(state.view()).toEqual({
      status: 'error',
      generation,
      input: INPUT,
      result: null,
      failure: 'network',
    });
    expect(JSON.stringify(state.view())).not.toContain('ProgressEvent');
  });

  // Cenários salvos são cópias da mesma sessão, preservam série completa e nunca excedem o limite de cinco.
  it('guarda no máximo cinco cenários confirmados e permite remover um ou limpar todos', () => {
    vi.useFakeTimers();
    const generation = startSession();
    expect(state.saveCurrentScenario()).toBe('not-confirmed');
    state.updateInput(INPUT);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    backend.expectOne(SIMULATION_URL).flush(resultResponse());

    for (let index = 0; index < 5; index += 1) {
      expect(state.saveCurrentScenario()).toBe('saved');
    }
    const saved = state.savedScenarios();
    expect(saved).toHaveLength(5);
    expect(saved.every((scenario) => scenario.input.initialAmount === INPUT.initialAmount)).toBe(
      true,
    );
    expect(saved[0]?.input).toEqual(INPUT);
    expect(saved[0]?.input).not.toBe(INPUT);
    expect(saved[0]?.result.totalContributed).toBe('220.00');
    expect(saved[0]?.result).not.toBe(state.view().result);
    expect(saved[0]?.result.points).not.toBe(state.view().result?.points);
    expect(saved[0]?.result.points).toHaveLength(13);
    expect(saved[0]?.id).not.toBe(saved[1]?.id);
    expect(state.saveCurrentScenario()).toBe('limit-reached');
    expect(state.savedScenarios()).toEqual(saved);
    expect(state.view().generation).toBe(generation);

    state.removeSavedScenario(saved[1]!.id);
    expect(state.savedScenarios()).toHaveLength(4);
    expect(state.savedScenarios().some((scenario) => scenario.id === saved[1]!.id)).toBe(false);
    state.clearSavedScenarios();
    expect(state.savedScenarios()).toEqual([]);
    backend.expectNone(PORTFOLIO_URL);
  });

  // O resumo só é lido quando a ação é chamada e somente GET protegido pode preencher o texto inicial.
  it('lê valor da carteira sob ação explícita e não dispara mutação', () => {
    const generation = startSession();
    let copiedAmount: string | undefined;
    state.savedScenarios();
    backend.expectNone(PORTFOLIO_URL);

    state.copyPortfolioValue().subscribe((value) => (copiedAmount = value));
    const request = backend.expectOne(PORTFOLIO_URL);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-simulation-state-token',
    );
    backend.expectNone((candidate) => candidate.method === 'POST');
    request.flush(portfolioResponse());

    expect(copiedAmount).toBe('125.75');
    expect(state.view().generation).toBe(generation);
    backend.expectNone((candidate) => candidate.method === 'POST');
  });

  // Total acima do limite é devolvido como erro específico, sem truncar o valor nem iniciar outro request.
  it('recusa total da carteira acima do teto sem alterar formulário ou fazer POST', () => {
    startSession();
    let receivedError: unknown;
    state.copyPortfolioValue().subscribe({ error: (error: unknown) => (receivedError = error) });
    backend.expectOne(PORTFOLIO_URL).flush(portfolioResponse('10000000.01'));

    expect(receivedError).toBeInstanceOf(InvestmentSimulationPortfolioValueError);
    expect((receivedError as InvestmentSimulationPortfolioValueError).reason).toBe('over-limit');
    expect(state.view().input).toBeNull();
    backend.expectNone((candidate) => candidate.method === 'POST');
  });

  // Uma edição feita durante o GET invalida o valor obtido, mesmo se o resumo chegar depois da mudança.
  it('não aplica leitura de carteira quando o formulário muda antes da resposta', () => {
    vi.useFakeTimers();
    startSession();
    let copiedAmount: string | undefined;
    let receivedError: unknown;
    state.copyPortfolioValue().subscribe({
      next: (value) => (copiedAmount = value),
      error: (error: unknown) => (receivedError = error),
    });
    const request = backend.expectOne(PORTFOLIO_URL);
    const editedInput = { ...INPUT, initialAmount: '999.00' };
    state.updateInput(editedInput);
    request.flush(portfolioResponse());

    expect(copiedAmount).toBeUndefined();
    expect(receivedError).toBeInstanceOf(InvestmentSimulationPortfolioValueError);
    expect((receivedError as InvestmentSimulationPortfolioValueError).reason).toBe('stale');
    expect(state.view().input).toEqual(editedInput);
    expect(state.view().status).toBe('debouncing');
    state.cancelPendingCalculation();
    backend.expectNone((candidate) => candidate.method === 'POST');
  });

  // Troca de sessão apaga cópias salvas e cancela leitura pendente, sem disponibilizar o resultado antigo.
  it('isola cenários e leitura da carteira ao sair ou iniciar sessão nova', () => {
    vi.useFakeTimers();
    const previousGeneration = startSession();
    state.updateInput(INPUT);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    backend.expectOne(SIMULATION_URL).flush(resultResponse());
    expect(state.saveCurrentScenario()).toBe('saved');

    let copiedAmount: string | undefined;
    state.copyPortfolioValue().subscribe((value) => (copiedAmount = value));
    const pendingPortfolioRead = backend.expectOne(PORTFOLIO_URL);
    const currentGeneration = startSession();
    expect(currentGeneration).not.toBe(previousGeneration);
    expect(state.savedScenarios()).toEqual([]);
    TestBed.tick();

    expect(pendingPortfolioRead.cancelled).toBe(true);
    expect(copiedAmount).toBeUndefined();
    expect(state.savedScenarios()).toEqual([]);
    expect(state.view().result).toBeNull();
    backend.expectNone((candidate) => candidate.method === 'POST');

    // Uma cópia na sessão seguinte existe antes do reset e deve desaparecer com o injector da aplicação.
    state.updateInput(INPUT);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    backend.expectOne(SIMULATION_URL).flush(resultResponse());
    expect(state.saveCurrentScenario()).toBe('saved');
    expect(state.savedScenarios()).toHaveLength(1);

    // Um injector novo representa reload completo: nenhum cenário da instância anterior é persistido.
    backend.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    configureState();
    expect(state.savedScenarios()).toEqual([]);
    expect(session.currentSession()).toBeNull();
  });

  // Expiração enquanto a API está pendente cancela o transporte e redige entrada e resultado da view.
  it('limpa operação e projeção quando a sessão expira', () => {
    vi.useFakeTimers();
    const expiresAt = new Date(Date.now() + DEBOUNCE_DELAY_MS + 700).toISOString();
    const generation = startSession(expiresAt);
    state.updateInput(INPUT);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const pendingRequest = backend.expectOne(SIMULATION_URL);

    vi.advanceTimersByTime(700);
    TestBed.tick();

    expect(session.currentSession()).toBeNull();
    expect(pendingRequest.cancelled).toBe(true);
    expect(state.view()).toEqual({
      status: 'idle',
      generation: null,
      input: null,
      result: null,
      failure: null,
    });
    expect(generation).toBeGreaterThan(0);
  });

  // Callback anterior à execução do effect ainda é barrado ao consultar a geração atual da sessão.
  it('não publica resposta tardia na geração de uma nova sessão', () => {
    vi.useFakeTimers();
    const previousGeneration = startSession();
    state.updateInput(INPUT);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const previousRequest = backend.expectOne(SIMULATION_URL);

    const currentGeneration = startSession();
    expect(currentGeneration).not.toBe(previousGeneration);
    previousRequest.flush(resultResponse());

    expect(state.view()).toEqual({
      status: 'idle',
      generation: currentGeneration,
      input: null,
      result: null,
      failure: null,
    });
    TestBed.tick();
    expect(state.view().generation).toBe(currentGeneration);
    expect(state.view().result).toBeNull();
  });
});
