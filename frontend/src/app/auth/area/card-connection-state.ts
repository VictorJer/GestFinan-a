import { computed, DestroyRef, effect, inject, Injectable, signal, WritableSignal } from '@angular/core';
import { Observable, ReplaySubject, Subscription, throwError } from 'rxjs';
import {
  CardConnectionApi,
  CardConnectionApiConfigurationError,
  CardConnectionPayloadError,
  CardSummary,
  ConnectionSyncState,
  SessionScopedResponse,
} from './card-connection-api';
import { LocalSessionRequiredError, ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';

export type CardConnectionReadStatus =
  | 'idle'
  | 'loading'
  | 'loaded'
  | 'empty'
  | 'not-found'
  | 'conflict'
  | 'error';

export type CardConnectionReadFailure =
  | 'not-found'
  | 'conflict'
  | 'forbidden'
  | 'network'
  | 'unavailable'
  | 'request-failed'
  | 'invalid-response'
  | 'configuration';

export interface CardConnectionReadState<T> {
  readonly status: CardConnectionReadStatus;
  readonly generation: number | null;
  readonly snapshot: T | null;
  readonly failure: CardConnectionReadFailure | null;
}

type ReadResource = 'cards' | 'sync-state';

interface PendingRead {
  readonly subscription: Subscription;
  readonly completeCancelledResult: () => void;
}

// Concentra estados públicos seguros; o estado vazio nunca reutiliza snapshot de outra sessão.
function idleState<T>(): CardConnectionReadState<T> {
  return {
    status: 'idle',
    generation: null,
    snapshot: null,
    failure: null,
  };
}

// Coordena leituras independentes, retém apenas snapshots confirmados e apaga tudo na troca de geração.
@Injectable({ providedIn: 'root' })
export class CardConnectionStateService {
  private readonly api = inject(CardConnectionApi);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cardsState = signal(idleState<readonly CardSummary[]>());
  private readonly syncState = signal(idleState<ConnectionSyncState>());
  private readonly pendingReads = new Map<ReadResource, PendingRead>();
  private activeGeneration: number | null = null;

  // Computed views redactam imediatamente qualquer snapshot sem uma sessão da mesma geração.
  readonly cards = computed(() => this.forCurrentSession(this.cardsState));
  readonly connection = computed(() => this.forCurrentSession(this.syncState));

  constructor() {
    // A mudança de login/logout/expiração cancela requests pendentes e elimina snapshots privados.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      this.activateGeneration(generation);
    });
    this.destroyRef.onDestroy(() => this.cancelPendingReads());
  }

  // Carrega cartões sem bloquear a leitura independente do estado da conexão.
  loadCards(): void {
    this.startRead('cards', () => this.api.getCards(), this.cardsState, (cards) => cards.length === 0);
  }

  // Carrega somente o estado persistido; não usa GET para criar intenção de atualização.
  loadConnectionState(): void {
    this.startRead('sync-state', () => this.api.getSyncState(), this.syncState);
  }

  // Atualiza o mesmo snapshot visual e devolve a leitura para correlacionar uma conclusão persistida.
  refreshConnectionState(): Observable<SessionScopedResponse<ConnectionSyncState>> {
    return (
      this.startRead('sync-state', () => this.api.getSyncState(), this.syncState) ??
      throwError(() => new LocalSessionRequiredError())
    );
  }

  // Permite cancelar leituras quando a visão deixa de estar ativa, preservando o último snapshot confirmado.
  cancelPendingReads(): void {
    for (const request of this.pendingReads.values()) {
      request.subscription.unsubscribe();
      request.completeCancelledResult();
    }
    this.pendingReads.clear();

    this.cardsState.update((state) => this.finishCancelledRead(state, state.snapshot?.length === 0));
    this.syncState.update((state) => this.finishCancelledRead(state, false));
  }

  // Inicia uma leitura da geração atual e publica somente respostas que ainda pertencem à mesma sessão.
  private startRead<T>(
    resource: ReadResource,
    createRequest: () => Observable<SessionScopedResponse<T>>,
    target: WritableSignal<CardConnectionReadState<T>>,
    isEmpty?: (snapshot: T) => boolean,
  ): Observable<SessionScopedResponse<T>> | null {
    const activeSession = this.session.currentSession();
    if (activeSession === null) {
      return null;
    }

    this.activateGeneration(activeSession.generation);
    this.cancelResourceRead(resource);
    const previous = target();
    const retainedSnapshot =
      previous.generation === activeSession.generation ? previous.snapshot : null;
    target.set({
      status: 'loading',
      generation: activeSession.generation,
      snapshot: retainedSnapshot,
      failure: null,
    });

    const result = new ReplaySubject<SessionScopedResponse<T>>(1);
    const request = createRequest().subscribe({
      next: (response) => {
        if (
          response.generation !== activeSession.generation ||
          !this.isCurrentGeneration(activeSession.generation)
        ) {
          this.pendingReads.delete(resource);
          result.complete();
          return;
        }

        this.pendingReads.delete(resource);
        target.set({
          status: isEmpty?.(response.value) === true ? 'empty' : 'loaded',
          generation: activeSession.generation,
          snapshot: response.value,
          failure: null,
        });
        result.next(response);
        result.complete();
      },
      error: (error: unknown) => {
        if (!this.isCurrentGeneration(activeSession.generation)) {
          this.pendingReads.delete(resource);
          result.complete();
          return;
        }

        this.pendingReads.delete(resource);
        const failure = classifyReadFailure(error);
        target.set({
          status: failure === 'not-found' || failure === 'conflict' ? failure : 'error',
          generation: activeSession.generation,
          snapshot: target().snapshot,
          failure,
        });
        result.error(error);
      },
    });

    if (!request.closed) {
      this.pendingReads.set(resource, {
        subscription: request,
        completeCancelledResult: () => result.complete(),
      });
    }

    return result.asObservable();
  }

  // Troca de geração invalida dados e cancela ambas as leituras antes de qualquer resposta atrasada.
  private activateGeneration(generation: number | null): void {
    if (generation === this.activeGeneration) {
      return;
    }

    this.cancelPendingReads();
    this.activeGeneration = generation;
    this.cardsState.set(idleState<readonly CardSummary[]>());
    this.syncState.set(idleState<ConnectionSyncState>());
  }

  // Remove uma requisição substituída sem afetar a chamada independente do outro recurso.
  private cancelResourceRead(resource: ReadResource): void {
    const request = this.pendingReads.get(resource);
    request?.subscription.unsubscribe();
    request?.completeCancelledResult();
    this.pendingReads.delete(resource);
  }

  // Oculta estados antigos imediatamente, mesmo antes de o efeito de limpeza terminar sua execução.
  private forCurrentSession<T>(
    state: WritableSignal<CardConnectionReadState<T>>,
  ): CardConnectionReadState<T> {
    const activeSession = this.session.currentSession();
    const snapshot = state();
    if (activeSession === null || snapshot.generation !== activeSession.generation) {
      return idleState<T>();
    }

    return snapshot;
  }

  // Resposta tardia de outra geração não pode atualizar cartões nem estado de conexão.
  private isCurrentGeneration(expectedGeneration: number): boolean {
    return this.session.currentSession()?.generation === expectedGeneration;
  }

  // Uma navegação que cancela uma leitura mantém o estado confirmado ou volta a idle se nada foi carregado.
  private finishCancelledRead<T>(
    state: CardConnectionReadState<T>,
    hasEmptySnapshot: boolean,
  ): CardConnectionReadState<T> {
    if (state.status !== 'loading') {
      return state;
    }

    if (state.snapshot === null) {
      return idleState<T>();
    }

    return {
      ...state,
      status: hasEmptySnapshot ? 'empty' : 'loaded',
      failure: null,
    };
  }
}

// Reduz erros HTTP/payload a uma categoria fechada, sem manter corpo, URL ou texto remoto no estado.
function classifyReadFailure(error: unknown): CardConnectionReadFailure {
  if (error instanceof ProtectedApiRequestError) {
    switch (error.kind) {
      case 'not-found':
        return 'not-found';
      case 'conflict':
        return 'conflict';
      case 'forbidden':
        return 'forbidden';
      case 'network':
        return 'network';
      case 'unavailable':
        return 'unavailable';
      default:
        return 'request-failed';
    }
  }

  if (error instanceof CardConnectionPayloadError) {
    return 'invalid-response';
  }

  if (error instanceof CardConnectionApiConfigurationError) {
    return 'configuration';
  }

  return 'request-failed';
}
