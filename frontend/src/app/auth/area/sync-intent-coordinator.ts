import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  CardConnectionApi,
  CardConnectionApiConfigurationError,
  CardConnectionPayloadError,
  SyncIntentResponse,
} from './card-connection-api';

export type SyncIntentStatus =
  | 'idle'
  | 'submitting'
  | 'not-required'
  | 'reauthorization-required'
  | 'accepted'
  | 'active'
  | 'interval'
  | 'indeterminate'
  | 'failed'
  | 'tracking'
  | 'succeeded'
  | 'execution-failed'
  | 'unconfirmed';

export interface SyncIntentAttemptState {
  readonly status: SyncIntentStatus;
  readonly syncRunId: string | null;
  readonly nextAllowedAtUtc: string | null;
  readonly retryAfterSeconds: number | null;
}

export interface SyncIntentCoordinatorState {
  readonly generation: number | null;
  readonly onEntry: SyncIntentAttemptState;
  readonly manual: SyncIntentAttemptState;
}

export type SyncIntentMode = 'on-entry' | 'manual';

interface PendingIntent {
  readonly generation: number;
  readonly mode: SyncIntentMode;
  readonly idempotencyKey: string;
}

const IDLE_ATTEMPT: SyncIntentAttemptState = {
  status: 'idle',
  syncRunId: null,
  nextAllowedAtUtc: null,
  retryAfterSeconds: null,
};

// Cria snapshots sem chave idempotente, mantendo credenciais e identificadores fora do estado visual.
function emptyCoordinatorState(generation: number | null): SyncIntentCoordinatorState {
  return {
    generation,
    onEntry: IDLE_ATTEMPT,
    manual: IDLE_ATTEMPT,
  };
}

// Coordena intenções somente em memória e associa cada tentativa à geração de login que a iniciou.
@Injectable({ providedIn: 'root' })
export class SyncIntentCoordinator {
  private readonly api = inject(CardConnectionApi);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly coordinatorState = signal(emptyCoordinatorState(null));
  private readonly pendingRequests = new Map<SyncIntentMode, Subscription>();
  private activeGeneration: number | null = null;
  private onEntryIntent: PendingIntent | null = null;
  private manualIntent: PendingIntent | null = null;

  // Oculta imediatamente intenções de outra sessão, inclusive antes da limpeza do efeito.
  readonly state = computed(() => this.stateForCurrentSession());

  constructor() {
    // Uma nova geração limpa as chaves e cancela POSTs antigos sem afetar a sessão que veio depois.
    effect(() => {
      this.activateGeneration(this.session.currentSession()?.generation ?? null);
    });
    this.destroyRef.onDestroy(() => this.cancelPendingRequests());
  }

  // Garante uma única avaliação no-entry por geração, mesmo que /area seja aberta novamente.
  evaluateOnEntry(): void {
    const activeSession = this.session.currentSession();
    if (activeSession === null) {
      return;
    }

    this.activateGeneration(activeSession.generation);
    if (this.onEntryIntent?.generation === activeSession.generation) {
      return;
    }

    const idempotencyKey = createIdempotencyKey();
    if (idempotencyKey === null) {
      this.updateAttempt('on-entry', { ...IDLE_ATTEMPT, status: 'failed' });
      return;
    }

    const intent: PendingIntent = {
      generation: activeSession.generation,
      mode: 'on-entry',
      idempotencyKey,
    };
    this.onEntryIntent = intent;
    this.updateAttempt('on-entry', { ...IDLE_ATTEMPT, status: 'submitting' });
    this.submit(intent);
  }

  // Cria pedido manual distinto, sem concorrer com aceite, execução ativa ou resultado incerto da sessão.
  requestManual(): void {
    const activeSession = this.session.currentSession();
    if (activeSession === null) {
      return;
    }

    this.activateGeneration(activeSession.generation);
    if (
      this.pendingRequests.has('manual') ||
      isUnresolvedAttempt(this.coordinatorState().onEntry) ||
      isUnresolvedAttempt(this.coordinatorState().manual)
    ) {
      return;
    }

    const previousKeys = new Set(
      [this.onEntryIntent?.idempotencyKey, this.manualIntent?.idempotencyKey].filter(
        (key): key is string => key !== undefined,
      ),
    );
    const idempotencyKey = createIdempotencyKey(previousKeys);
    if (idempotencyKey === null) {
      this.updateAttempt('manual', { ...IDLE_ATTEMPT, status: 'failed' });
      return;
    }

    const intent: PendingIntent = {
      generation: activeSession.generation,
      mode: 'manual',
      idempotencyKey,
    };
    this.manualIntent = intent;
    this.updateAttempt('manual', { ...IDLE_ATTEMPT, status: 'submitting' });
    this.submit(intent);
  }

  // Replay explícito reaproveita modo e chave originais e só é possível após resultado indeterminado.
  retryIndeterminate(mode: SyncIntentMode): void {
    const activeSession = this.session.currentSession();
    const intent = mode === 'on-entry' ? this.onEntryIntent : this.manualIntent;
    if (
      activeSession === null ||
      intent === null ||
      intent.generation !== activeSession.generation ||
      this.pendingRequests.has(mode) ||
      this.attemptFor(mode).status !== 'indeterminate'
    ) {
      return;
    }

    this.updateAttempt(mode, { ...this.attemptFor(mode), status: 'submitting' });
    this.submit(intent);
  }

  // Libera novas intenções somente depois do estado terminal observado para a execução correspondente.
  resolveTrackedRun(
    syncRunId: string,
    outcome: 'succeeded' | 'execution-failed' | 'unconfirmed',
  ): void {
    this.coordinatorState.update((state) => ({
      ...state,
      onEntry: resolveAttemptForRun(state.onEntry, syncRunId, outcome),
      manual: resolveAttemptForRun(state.manual, syncRunId, outcome),
    }));
  }

  // Traduz apenas os resultados tipados da API para estado observável, sem repassar respostas brutas.
  private submit(intent: PendingIntent): void {
    const request = (intent.mode === 'on-entry'
      ? this.api.acceptOnEntry(intent.idempotencyKey)
      : this.api.acceptManual(intent.idempotencyKey)
    ).subscribe({
      next: (response) => {
        if (!this.isCurrentIntent(intent, response.generation)) {
          return;
        }

        this.pendingRequests.delete(intent.mode);
        this.updateAttempt(intent.mode, stateFromResponse(response.value));
      },
      error: (_error: unknown) => {
        if (!this.isCurrentIntent(intent, intent.generation)) {
          return;
        }

        this.pendingRequests.delete(intent.mode);
        this.updateAttempt(intent.mode, attemptFromError(_error));
      },
    });

    if (!request.closed) {
      this.pendingRequests.set(intent.mode, request);
    }
  }

  // Respostas só pertencem ao estado se o POST e a sessão ainda corresponderem à mesma geração.
  private isCurrentIntent(intent: PendingIntent, responseGeneration: number): boolean {
    return (
      responseGeneration === intent.generation &&
      this.session.currentSession()?.generation === intent.generation
    );
  }

  // Limpa chaves/respostas pendentes ao trocar de sessão e mantém a geração nova como único escopo válido.
  private activateGeneration(generation: number | null): void {
    if (generation === this.activeGeneration) {
      return;
    }

    this.cancelPendingRequests();
    this.activeGeneration = generation;
    this.onEntryIntent = null;
    this.manualIntent = null;
    this.coordinatorState.set(emptyCoordinatorState(generation));
  }

  // Cancela POSTs associados à geração antiga antes de descartar suas referências voláteis.
  private cancelPendingRequests(): void {
    for (const request of this.pendingRequests.values()) {
      request.unsubscribe();
    }
    this.pendingRequests.clear();
  }

  // Atualiza somente o ramo da intenção solicitado, preservando o estado independente do outro modo.
  private updateAttempt(mode: SyncIntentMode, attempt: SyncIntentAttemptState): void {
    this.coordinatorState.update((state) => ({
      ...state,
      [mode === 'on-entry' ? 'onEntry' : 'manual']: attempt,
    }));
  }

  // Lê o estado atual de um modo sem expor a chave privada mantida no registro interno.
  private attemptFor(mode: SyncIntentMode): SyncIntentAttemptState {
    return mode === 'on-entry' ? this.coordinatorState().onEntry : this.coordinatorState().manual;
  }

  // Mantém estado e identificadores de execução invisíveis quando a sessão termina ou muda.
  private stateForCurrentSession(): SyncIntentCoordinatorState {
    const generation = this.session.currentSession()?.generation ?? null;
    const state = this.coordinatorState();
    return generation !== null && state.generation === generation
      ? state
      : emptyCoordinatorState(null);
  }
}

// Converte somente os dois formatos de sucesso documentados em estados simples para a interface.
function stateFromResponse(response: SyncIntentResponse): SyncIntentAttemptState {
  if (response.kind === 'accepted') {
    return {
      ...IDLE_ATTEMPT,
      status: 'accepted',
      syncRunId: response.syncRunId,
    };
  }

  return {
    ...IDLE_ATTEMPT,
    status: response.decision === 'not_required' ? 'not-required' : 'reauthorization-required',
  };
}

// Bloqueia nova intenção enquanto existe POST em trânsito, aceite ativo ou aceitação não reconciliada.
function isUnresolvedAttempt(attempt: SyncIntentAttemptState): boolean {
  return [
    'submitting',
    'accepted',
    'active',
    'indeterminate',
    'tracking',
    'unconfirmed',
  ].includes(attempt.status);
}

// Só associa a conclusão ao ramo que contém o mesmo ID, preservando intenções de outra execução.
function resolveAttemptForRun(
  attempt: SyncIntentAttemptState,
  syncRunId: string,
  outcome: 'succeeded' | 'execution-failed' | 'unconfirmed',
): SyncIntentAttemptState {
  if (
    attempt.syncRunId !== syncRunId ||
    !['accepted', 'active', 'tracking', 'unconfirmed'].includes(attempt.status)
  ) {
    return attempt;
  }

  return { ...attempt, status: outcome };
}

// Converte somente códigos fechados do interceptor em estados; erros/payloads remotos nunca viram texto.
function attemptFromError(error: unknown): SyncIntentAttemptState {
  if (error instanceof ProtectedApiRequestError) {
    switch (error.kind) {
      case 'sync-active':
        return {
          ...IDLE_ATTEMPT,
          status: 'active',
          syncRunId: error.syncMetadata?.syncRunId ?? null,
        };
      case 'sync-interval':
        return {
          ...IDLE_ATTEMPT,
          status: 'interval',
          nextAllowedAtUtc: error.syncMetadata?.nextAllowedAtUtc ?? null,
          retryAfterSeconds: error.syncMetadata?.retryAfterSeconds ?? null,
        };
      case 'acceptance-indeterminate':
      case 'network':
      case 'unavailable':
        return { ...IDLE_ATTEMPT, status: 'indeterminate' };
      default:
        return { ...IDLE_ATTEMPT, status: 'failed' };
    }
  }

  // Uma resposta de sucesso inválida não comprova que a API recusou o pedido aceito.
  if (error instanceof CardConnectionPayloadError) {
    return { ...IDLE_ATTEMPT, status: 'indeterminate' };
  }

  if (error instanceof CardConnectionApiConfigurationError) {
    return { ...IDLE_ATTEMPT, status: 'failed' };
  }

  return { ...IDLE_ATTEMPT, status: 'failed' };
}

// Usa UUID criptográfico ASCII; sem fonte segura, não envia uma chave previsível ou inválida.
function createIdempotencyKey(excludedKeys: ReadonlySet<string> = new Set()): string | null {
  try {
    const key = globalThis.crypto?.randomUUID?.();
    return key !== undefined && !excludedKeys.has(key) && /^[!-~]{1,128}$/.test(key) ? key : null;
  } catch {
    return null;
  }
}
