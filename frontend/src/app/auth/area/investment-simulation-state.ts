import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { LocalSessionRequiredError, ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  InvestmentSimulationApi,
  InvestmentSimulationApiConfigurationError,
  InvestmentSimulationInput,
  InvestmentSimulationPayloadError,
  InvestmentSimulationResult,
} from './investment-simulation-api';

const DEBOUNCE_DELAY_MS = 300;
const MAXIMUM_SAVED_SCENARIOS = 5;
const MAXIMUM_INITIAL_AMOUNT = '10000000.00';

/** Estados públicos não preservam resposta anterior enquanto as entradas atuais aguardam confirmação. */
export type InvestmentSimulationStatus = 'idle' | 'debouncing' | 'loading' | 'confirmed' | 'error';

/** Falhas permitidas na view; corpo remoto, URL e detalhes de rede não entram no estado. */
export type InvestmentSimulationFailure =
  | 'forbidden'
  | 'network'
  | 'unavailable'
  | 'request-failed'
  | 'invalid-response'
  | 'configuration'
  | 'stale';

/** O resultado confirmado sempre pertence às entradas e à geração visíveis no mesmo snapshot. */
export interface InvestmentSimulationCalculationState {
  readonly status: InvestmentSimulationStatus;
  readonly generation: number | null;
  readonly input: InvestmentSimulationInput | null;
  readonly result: InvestmentSimulationResult | null;
  readonly failure: InvestmentSimulationFailure | null;
}

/** Cópia efêmera de uma projeção confirmada, com entradas e série completas para comparação. */
export interface SavedInvestmentSimulationScenario {
  readonly id: number;
  readonly input: InvestmentSimulationInput;
  readonly result: InvestmentSimulationResult;
}

/** Resultado da tentativa local de guardar; o limite nunca substitui cenário já salvo. */
export type SaveInvestmentSimulationScenarioResult = 'saved' | 'limit-reached' | 'not-confirmed';

/** Falhas seguras ao copiar a carteira para o campo inicial do simulador. */
export type InvestmentSimulationPortfolioValueFailure = 'stale' | 'over-limit';

/** Impede usar valor acima do teto ou resposta de leitura iniciada por outra edição/sessão. */
export class InvestmentSimulationPortfolioValueError extends Error {
  constructor(readonly reason: InvestmentSimulationPortfolioValueFailure) {
    super(
      reason === 'over-limit'
        ? 'O total da carteira excede o limite do valor inicial do simulador.'
        : 'A leitura da carteira ficou desatualizada; tente novamente.',
    );
    this.name = 'InvestmentSimulationPortfolioValueError';
  }
}

interface SavedScenarioCollection {
  readonly generation: number | null;
  readonly scenarios: readonly SavedInvestmentSimulationScenario[];
}

// Estado ocioso não carrega entradas, projeção ou erro de uma operação anterior.
function idleState(generation: number | null): InvestmentSimulationCalculationState {
  return {
    status: 'idle',
    generation,
    input: null,
    result: null,
    failure: null,
  };
}

// Debounce e resultado ficam vinculados à sessão; nenhuma projeção antiga atravessa gerações.
@Injectable({ providedIn: 'root' })
export class InvestmentSimulationStateService {
  private readonly api = inject(InvestmentSimulationApi);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly state = signal(idleState(null));
  private readonly scenarioCollection = signal<SavedScenarioCollection>({
    generation: null,
    scenarios: [],
  });
  private activeGeneration: number | null = null;
  private debounceTimer: ReturnType<typeof setTimeout> | null = null;
  private pendingRequest: Subscription | null = null;
  private requestSequence = 0;
  private inputRevision = 0;
  private portfolioReadSequence = 0;
  private scenarioSequence = 0;
  private readonly pendingPortfolioCancels = new Set<() => void>();

  // A página só observa snapshot da mesma geração que continua autenticada neste instante.
  readonly view = computed(() => this.forCurrentSession(this.state()));
  readonly savedScenarios = computed(() => {
    const generation = this.session.currentSession()?.generation ?? null;
    const collection = this.scenarioCollection();
    return generation !== null && collection.generation === generation ? collection.scenarios : [];
  });

  constructor() {
    // Logout, expiração ou login novo cancelam debounce/rede e descartam entradas e resultados da geração anterior.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      this.activateGeneration(generation);
    });
    this.destroyRef.onDestroy(() => this.cancelPendingCalculation());
  }

  // Entradas inválidas limpam resultado atual; valores válidos substituem qualquer timer/request pendente.
  updateInput(input: InvestmentSimulationInput | null): void {
    // Cada edição invalida leituras da carteira iniciadas antes da mudança do formulário.
    this.inputRevision += 1;
    this.portfolioReadSequence += 1;
    const activeSession = this.session.currentSession();
    if (activeSession === null) {
      this.activateGeneration(null);
      return;
    }

    this.activateGeneration(activeSession.generation);
    this.invalidatePendingOperation();
    const requestId = this.requestSequence;
    if (input === null) {
      this.state.set(idleState(activeSession.generation));
      return;
    }

    // Copiar campos evita que mutação do formulário altere o snapshot já associado ao request.
    const requestInput: InvestmentSimulationInput = {
      initialAmount: input.initialAmount,
      monthlyContribution: input.monthlyContribution,
      annualRatePercent: input.annualRatePercent,
      years: input.years,
    };
    this.state.set({
      status: 'debouncing',
      generation: activeSession.generation,
      input: requestInput,
      result: null,
      failure: null,
    });

    // O timer só dispara a última entrada válida e confirma novamente a sessão antes de enviar.
    this.debounceTimer = setTimeout(() => {
      this.debounceTimer = null;
      if (!this.isCurrentRequest(requestId, activeSession.generation)) {
        return;
      }

      this.state.set({
        status: 'loading',
        generation: activeSession.generation,
        input: requestInput,
        result: null,
        failure: null,
      });
      const request = this.api.calculate(requestInput).subscribe({
        next: (response) => {
          if (!this.isCurrentRequest(requestId, activeSession.generation)) {
            return;
          }

          this.pendingRequest = null;
          if (response.generation !== activeSession.generation) {
            this.setFailure(activeSession.generation, requestInput, 'stale');
            return;
          }

          // Somente resposta da entrada e geração atuais pode virar o cenário confirmado da página.
          this.state.set({
            status: 'confirmed',
            generation: activeSession.generation,
            input: requestInput,
            result: response.value,
            failure: null,
          });
        },
        error: (error: unknown) => {
          if (!this.isCurrentRequest(requestId, activeSession.generation)) {
            return;
          }

          this.pendingRequest = null;
          this.setFailure(activeSession.generation, requestInput, classifyFailure(error));
        },
      });

      if (!request.closed && this.isCurrentRequest(requestId, activeSession.generation)) {
        this.pendingRequest = request;
      }
    }, DEBOUNCE_DELAY_MS);
  }

  // Sair da página ou invalidar o formulário cancela trabalho pendente e apaga projeção não mais aplicável.
  cancelPendingCalculation(): void {
    this.invalidatePendingOperation();
    this.cancelPortfolioReads();
    this.portfolioReadSequence += 1;
    const generation = this.session.currentSession()?.generation ?? null;
    this.activeGeneration = generation;
    this.state.set(idleState(generation));
  }

  // Guarda apenas cálculo confirmado da sessão atual e nunca descarta silenciosamente um cenário anterior.
  saveCurrentScenario(): SaveInvestmentSimulationScenarioResult {
    const current = this.view();
    if (
      current.status !== 'confirmed' ||
      current.input === null ||
      current.result === null ||
      current.generation === null
    ) {
      return 'not-confirmed';
    }

    const existing = this.savedScenarios();
    if (existing.length >= MAXIMUM_SAVED_SCENARIOS) {
      return 'limit-reached';
    }

    const scenario: SavedInvestmentSimulationScenario = {
      id: ++this.scenarioSequence,
      input: { ...current.input },
      result: cloneResult(current.result),
    };
    this.scenarioCollection.set({
      generation: current.generation,
      scenarios: [...existing, scenario],
    });
    return 'saved';
  }

  // Remove somente o ID solicitado e preserva todos os demais cenários da sessão.
  removeSavedScenario(id: number): void {
    const generation = this.session.currentSession()?.generation ?? null;
    const collection = this.scenarioCollection();
    if (generation === null || collection.generation !== generation) {
      return;
    }

    this.scenarioCollection.set({
      generation,
      scenarios: collection.scenarios.filter((scenario) => scenario.id !== id),
    });
  }

  // Limpa todas as cópias voláteis sem alterar entradas, resultado atual ou carteira real.
  clearSavedScenarios(): void {
    const generation = this.session.currentSession()?.generation ?? null;
    if (generation !== null) {
      this.scenarioCollection.set({ generation, scenarios: [] });
    }
  }

  // O GET protegido só é iniciado ao assinar esta ação explícita; edição concorrente invalida a cópia.
  copyPortfolioValue(): Observable<string> {
    return new Observable<string>((observer) => {
      const activeSession = this.session.currentSession();
      if (activeSession === null) {
        observer.error(new LocalSessionRequiredError());
        return;
      }

      this.activateGeneration(activeSession.generation);
      const requestId = ++this.portfolioReadSequence;
      const inputRevision = this.inputRevision;
      let isActive = true;
      let request: Subscription | null = null;
      let cancel = (): void => undefined;
      const finishWithError = (error: unknown): void => {
        if (!isActive) {
          return;
        }

        isActive = false;
        this.pendingPortfolioCancels.delete(cancel);
        observer.error(error);
      };
      cancel = (): void => {
        if (!isActive) {
          return;
        }

        isActive = false;
        request?.unsubscribe();
        this.pendingPortfolioCancels.delete(cancel);
        observer.complete();
      };
      this.pendingPortfolioCancels.add(cancel);

      // A resposta é allowlisted pelo cliente de carteira; geração e revisão do formulário são conferidas antes do uso.
      request = this.api.getPortfolio().subscribe({
        next: (response) => {
          const currentSession = this.session.currentSession();
          if (
            requestId !== this.portfolioReadSequence ||
            inputRevision !== this.inputRevision ||
            currentSession?.generation !== activeSession.generation ||
            response.generation !== activeSession.generation
          ) {
            finishWithError(new InvestmentSimulationPortfolioValueError('stale'));
            return;
          }

          const totalCurrentAmount = response.value.summary.totalCurrentAmount;
          if (!isAtMostInitialLimit(totalCurrentAmount)) {
            finishWithError(new InvestmentSimulationPortfolioValueError('over-limit'));
            return;
          }

          isActive = false;
          this.pendingPortfolioCancels.delete(cancel);
          observer.next(totalCurrentAmount);
          observer.complete();
        },
        error: finishWithError,
        complete: () => {
          if (!isActive) {
            return;
          }

          isActive = false;
          this.pendingPortfolioCancels.delete(cancel);
          observer.complete();
        },
      });

      return () => {
        if (isActive) {
          isActive = false;
          request?.unsubscribe();
        }
        this.pendingPortfolioCancels.delete(cancel);
      };
    });
  }

  // A troca de geração cancela o timer e a inscrição HTTP antes de limpar o snapshot financeiro.
  private activateGeneration(generation: number | null): void {
    if (generation === this.activeGeneration) {
      return;
    }

    this.invalidatePendingOperation();
    this.cancelPortfolioReads();
    this.portfolioReadSequence += 1;
    this.activeGeneration = generation;
    this.state.set(idleState(generation));
    this.scenarioCollection.set({ generation, scenarios: [] });
  }

  // Incrementar sequência invalida callbacks antigos mesmo que algum transporte ignore cancelamento físico.
  private invalidatePendingOperation(): void {
    this.requestSequence += 1;
    if (this.debounceTimer !== null) {
      clearTimeout(this.debounceTimer);
      this.debounceTimer = null;
    }

    this.pendingRequest?.unsubscribe();
    this.pendingRequest = null;
  }

  // Sessão encerrada ou rota destruída invalida os observables ativos de leitura da carteira.
  private cancelPortfolioReads(): void {
    for (const cancel of [...this.pendingPortfolioCancels]) {
      cancel();
    }
  }

  // Uma operação só é atual enquanto request ID e geração ainda correspondem à sessão local.
  private isCurrentRequest(requestId: number, generation: number): boolean {
    return (
      requestId === this.requestSequence && this.session.currentSession()?.generation === generation
    );
  }

  // Falhas mantêm o input atual para permitir correção/retentativa, mas nunca mantêm resultado antigo.
  private setFailure(
    generation: number,
    input: InvestmentSimulationInput,
    failure: InvestmentSimulationFailure,
  ): void {
    this.state.set({ status: 'error', generation, input, result: null, failure });
  }

  // A view redige imediatamente qualquer valor sem sessão ativa da mesma geração.
  private forCurrentSession(
    state: InvestmentSimulationCalculationState,
  ): InvestmentSimulationCalculationState {
    const generation = this.session.currentSession()?.generation ?? null;
    if (generation === null || state.generation !== generation) {
      return idleState(generation);
    }

    return state;
  }
}

// Copia pontos e totais allowlisted para que cenários salvos não compartilhem referências mutáveis da view atual.
function cloneResult(result: InvestmentSimulationResult): InvestmentSimulationResult {
  return {
    points: result.points.map((point) => ({ ...point })),
    totalContributed: result.totalContributed,
    projectedValue: result.projectedValue,
    projectedReturn: result.projectedReturn,
  };
}

// O total recebido já é decimal canônico; comparação textual evita Number, arredondamento ou truncamento.
function isAtMostInitialLimit(value: string): boolean {
  if (!/^(?:0|[1-9]\d*)\.\d{2}$/.test(value)) {
    return false;
  }

  const [integerPart, fractionalPart] = value.split('.');
  const [maximumInteger, maximumFraction] = MAXIMUM_INITIAL_AMOUNT.split('.');
  if (integerPart.length !== maximumInteger.length) {
    return integerPart.length < maximumInteger.length;
  }

  if (integerPart !== maximumInteger) {
    return integerPart < maximumInteger;
  }

  return fractionalPart <= maximumFraction;
}

// Converte falhas HTTP/parser em categorias finitas sem armazenar corpo, token ou texto do servidor.
function classifyFailure(error: unknown): InvestmentSimulationFailure {
  if (error instanceof ProtectedApiRequestError) {
    switch (error.kind) {
      case 'forbidden':
        return 'forbidden';
      case 'network':
        return 'network';
      case 'unavailable':
        return 'unavailable';
      case 'stale':
        return 'stale';
      default:
        return 'request-failed';
    }
  }

  if (error instanceof InvestmentSimulationPayloadError) {
    return 'invalid-response';
  }

  if (error instanceof InvestmentSimulationApiConfigurationError) {
    return 'configuration';
  }

  if (error instanceof LocalSessionRequiredError) {
    return 'stale';
  }

  return 'request-failed';
}
