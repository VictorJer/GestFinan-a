import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { Observable, ReplaySubject, Subscription, throwError } from 'rxjs';
import { LocalSessionRequiredError, ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  InvestmentInput,
  InvestmentItem,
  InvestmentPortfolio,
  InvestmentPortfolioApi,
  InvestmentPortfolioApiConfigurationError,
  InvestmentPortfolioPayloadError,
} from './investment-portfolio-api';
import { SessionScopedResponse } from './card-connection-api';

/** Estados de leitura públicos; erros brutos e respostas HTTP não são armazenados na view. */
export type InvestmentPortfolioReadStatus = 'idle' | 'loading' | 'loaded' | 'empty' | 'error';

/** Categorias locais fixas para mensagens seguras de falha de leitura ou mutação. */
export type InvestmentPortfolioFailure =
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'network'
  | 'unavailable'
  | 'request-failed'
  | 'invalid-response'
  | 'configuration'
  | 'stale';

/** Ações de gravação permanecem distintas da leitura e não insinuam sucesso antes da resposta HTTP. */
export type InvestmentPortfolioMutationStatus = 'idle' | 'saving' | 'error';

/** Snapshot e falhas pertencem a uma geração local de sessão e preservam o formato monetário textual. */
export interface InvestmentPortfolioReadState {
  readonly status: InvestmentPortfolioReadStatus;
  readonly generation: number | null;
  readonly snapshot: InvestmentPortfolio | null;
  readonly failure: InvestmentPortfolioFailure | null;
  readonly mutationStatus: InvestmentPortfolioMutationStatus;
  readonly mutationFailure: InvestmentPortfolioFailure | null;
}

interface PendingMutation {
  readonly subscription: Subscription;
  readonly result: ReplaySubject<InvestmentItem | null>;
}

// Cada sessão começa sem dados e sem erro herdado de outra geração.
function idleState(generation: number | null): InvestmentPortfolioReadState {
  return {
    status: 'idle',
    generation,
    snapshot: null,
    failure: null,
    mutationStatus: 'idle',
    mutationFailure: null,
  };
}

// Coordena leitura e CRUD sem permitir que respostas privadas ultrapassem a geração iniciadora.
@Injectable({ providedIn: 'root' })
export class InvestmentPortfolioStateService {
  private readonly api = inject(InvestmentPortfolioApi);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly state = signal<InvestmentPortfolioReadState>(idleState(null));
  private activeGeneration: number | null = null;
  private pendingRead: Subscription | null = null;
  private pendingMutation: PendingMutation | null = null;
  private readSequence = 0;
  private mutationSequence = 0;

  // A página observa somente estado autorizado pela sessão local ainda ativa.
  readonly view = computed(() => this.forCurrentSession(this.state()));

  constructor() {
    // Logout, expiração e login novo cancelam trabalho antigo e apagam a carteira anterior.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      this.activateGeneration(generation);
    });
    this.destroyRef.onDestroy(() => this.cancelAllRequests());
  }

  // Carrega lista e resumo de uma única rota protegida, sem tocar em dados ou sincronização de cartão.
  load(): void {
    this.startRead();
  }

  // Retry inicia exatamente uma nova leitura e nunca reconstrói o resumo a partir dos itens no browser.
  retry(): void {
    this.startRead();
  }

  // Cria a posição; a leitura só é repetida depois que a API confirma a gravação com 201.
  createInvestment(input: InvestmentInput): Observable<InvestmentItem | null> {
    return this.startMutation(() => this.api.createInvestment(input));
  }

  // Atualiza a posição e atualiza o snapshot somente após resposta 200 confirmada.
  updateInvestment(id: string, input: InvestmentInput): Observable<InvestmentItem | null> {
    return this.startMutation(() => this.api.updateInvestment(id, input));
  }

  // Remove a posição e relê lista/resumo somente após o 204 confirmado pelo servidor.
  deleteInvestment(id: string): Observable<InvestmentItem | null> {
    return this.startMutation(() => this.api.deleteInvestment(id));
  }

  // Saída da página cancela apenas o GET e limpa seu snapshot, sem interromper uma gravação em trânsito.
  cancelPendingRead(): void {
    this.cancelReadRequest();
    const generation = this.session.currentSession()?.generation ?? null;
    const previous = this.state();
    const reset = idleState(generation);
    this.state.set({
      ...reset,
      mutationStatus:
        previous.generation === generation ? previous.mutationStatus : reset.mutationStatus,
      mutationFailure:
        previous.generation === generation ? previous.mutationFailure : reset.mutationFailure,
    });
  }

  // Inicia um GET da geração atual e publica a resposta apenas se request ID e sessão ainda coincidirem.
  private startRead(): void {
    const activeSession = this.session.currentSession();
    if (activeSession === null) {
      this.activateGeneration(null);
      return;
    }

    this.activateGeneration(activeSession.generation);
    this.cancelReadRequest();
    const requestId = ++this.readSequence;
    const previous = this.state();
    const retainedSnapshot =
      previous.generation === activeSession.generation ? previous.snapshot : null;
    this.state.set({
      ...previous,
      status: 'loading',
      generation: activeSession.generation,
      snapshot: retainedSnapshot,
      failure: null,
    });

    const request = this.api.getPortfolio().subscribe({
      next: (response) => {
        if (
          requestId !== this.readSequence ||
          !this.isCurrentGeneration(activeSession.generation)
        ) {
          return;
        }

        this.pendingRead = null;
        if (response.generation !== activeSession.generation) {
          this.setReadFailure(activeSession.generation, 'stale');
          return;
        }

        this.state.update((state) => ({
          ...state,
          status: response.value.items.length === 0 ? 'empty' : 'loaded',
          generation: activeSession.generation,
          snapshot: response.value,
          failure: null,
        }));
      },
      error: (error: unknown) => {
        if (
          requestId !== this.readSequence ||
          !this.isCurrentGeneration(activeSession.generation)
        ) {
          return;
        }

        this.pendingRead = null;
        this.setReadFailure(activeSession.generation, classifyFailure(error));
      },
    });

    if (!request.closed && requestId === this.readSequence) {
      this.pendingRead = request;
    }
  }

  // Uma gravação ativa invalida GETs anteriores e inicia nova leitura só quando a API a confirma.
  private startMutation(
    createRequest: () => Observable<SessionScopedResponse<InvestmentItem | null>>,
  ): Observable<InvestmentItem | null> {
    const activeSession = this.session.currentSession();
    if (activeSession === null) {
      return throwError(() => new LocalSessionRequiredError());
    }

    this.activateGeneration(activeSession.generation);
    if (this.state().mutationStatus === 'saving') {
      return throwError(() => new InvestmentPortfolioMutationInProgressError());
    }

    this.cancelReadRequest();
    const mutationId = ++this.mutationSequence;
    const previous = this.state();
    this.state.set({
      ...previous,
      status: statusForSnapshot(previous.snapshot),
      generation: activeSession.generation,
      failure: null,
      mutationStatus: 'saving',
      mutationFailure: null,
    });

    const result = new ReplaySubject<InvestmentItem | null>(1);
    const request = createRequest().subscribe({
      next: (response) => {
        if (
          mutationId !== this.mutationSequence ||
          !this.isCurrentGeneration(activeSession.generation)
        ) {
          result.complete();
          return;
        }

        this.pendingMutation = null;
        if (response.generation !== activeSession.generation) {
          const failure = 'stale';
          this.state.update((state) => ({
            ...state,
            mutationStatus: 'error',
            mutationFailure: failure,
          }));
          result.error(new InvestmentPortfolioPayloadError());
          return;
        }

        this.state.update((state) => ({
          ...state,
          mutationStatus: 'idle',
          mutationFailure: null,
        }));
        this.startRead();
        result.next(response.value);
        result.complete();
      },
      error: (error: unknown) => {
        if (
          mutationId !== this.mutationSequence ||
          !this.isCurrentGeneration(activeSession.generation)
        ) {
          result.complete();
          return;
        }

        this.pendingMutation = null;
        this.state.update((state) => ({
          ...state,
          mutationStatus: 'error',
          mutationFailure: classifyFailure(error),
        }));
        result.error(error);
      },
    });

    if (!request.closed && mutationId === this.mutationSequence) {
      this.pendingMutation = { subscription: request, result };
    }

    return result.asObservable();
  }

  // Alterações de sessão cancelam leitura e escrita pendentes antes de limpar snapshots privados.
  private activateGeneration(generation: number | null): void {
    if (generation === this.activeGeneration) {
      return;
    }

    this.cancelReadRequest();
    this.cancelMutationRequest();
    this.activeGeneration = generation;
    this.state.set(idleState(generation));
  }

  // Request IDs invalidam callbacks mesmo se um transporte ou adapter não cancelar fisicamente a resposta.
  private cancelReadRequest(): void {
    this.readSequence += 1;
    this.pendingRead?.unsubscribe();
    this.pendingRead = null;
  }

  // Uma mutação interrompida por troca de titular termina sem publicar sucesso ou disparar nova leitura.
  private cancelMutationRequest(): void {
    this.mutationSequence += 1;
    this.pendingMutation?.subscription.unsubscribe();
    this.pendingMutation?.result.complete();
    this.pendingMutation = null;
  }

  // O teardown do serviço encerra ambas as operações sem tocar em outras sessões ou recursos.
  private cancelAllRequests(): void {
    this.cancelReadRequest();
    this.cancelMutationRequest();
  }

  // Apenas uma sessão idêntica à que iniciou a operação pode receber seus dados ou sua falha.
  private isCurrentGeneration(expectedGeneration: number): boolean {
    return this.session.currentSession()?.generation === expectedGeneration;
  }

  // Falha de uma leitura mantém somente snapshot já confirmado da mesma geração.
  private setReadFailure(generation: number, failure: InvestmentPortfolioFailure): void {
    this.state.update((state) => ({
      ...state,
      status: 'error',
      generation,
      failure,
    }));
  }

  // A view redige dados assim que a sessão expira, mesmo antes do efeito de limpeza do estado.
  private forCurrentSession(state: InvestmentPortfolioReadState): InvestmentPortfolioReadState {
    const generation = this.session.currentSession()?.generation ?? null;
    if (generation === null || state.generation !== generation) {
      return idleState(generation);
    }

    return state;
  }
}

/** Evita concorrência de cliques enquanto uma gravação anterior ainda aguarda confirmação HTTP. */
export class InvestmentPortfolioMutationInProgressError extends Error {
  constructor() {
    super('Já existe uma alteração da carteira em andamento.');
    this.name = 'InvestmentPortfolioMutationInProgressError';
  }
}

// Snapshot vazio continua como empty após cancelamento da leitura; dados carregados voltam a loaded.
function statusForSnapshot(snapshot: InvestmentPortfolio | null): InvestmentPortfolioReadStatus {
  if (snapshot === null) {
    return 'idle';
  }

  return snapshot.items.length === 0 ? 'empty' : 'loaded';
}

// Reduz erros HTTP e parsing a um vocabulário fechado sem conservar corpo, URL ou detalhe do servidor.
function classifyFailure(error: unknown): InvestmentPortfolioFailure {
  if (error instanceof ProtectedApiRequestError) {
    switch (error.kind) {
      case 'forbidden':
        return 'forbidden';
      case 'not-found':
        return 'not-found';
      case 'conflict':
        return 'conflict';
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

  if (error instanceof InvestmentPortfolioPayloadError) {
    return 'invalid-response';
  }

  if (error instanceof InvestmentPortfolioApiConfigurationError) {
    return 'configuration';
  }

  if (error instanceof LocalSessionRequiredError) {
    return 'stale';
  }

  return 'request-failed';
}
