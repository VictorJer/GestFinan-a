import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { CardConnectionApi, CardConnectionPayloadError, SyncRunStatus } from './card-connection-api';
import { CardConnectionStateService } from './card-connection-state';
import { SyncIntentCoordinator } from './sync-intent-coordinator';

const POLL_INTERVAL_MS = 2000;
const MAX_TRACKING_DURATION_MS = 60_000;

export type SyncRunTrackingStatus = 'idle' | 'tracking' | 'succeeded' | 'failed' | 'unconfirmed';
export type SyncRunTrackingFailure = 'timeout' | 'not-found' | 'invalid-response' | 'unavailable';

export interface SyncRunTrackingState {
  readonly status: SyncRunTrackingStatus;
  readonly syncRunId: string | null;
  readonly failure: SyncRunTrackingFailure | null;
}

interface StoredTrackingState extends SyncRunTrackingState {
  readonly generation: number | null;
}

const IDLE_TRACKING_STATE: StoredTrackingState = {
  status: 'idle',
  syncRunId: null,
  failure: null,
  generation: null,
};

// Acompanha uma execução sem manter polling em background nem reter dados de outra geração de login.
@Injectable({ providedIn: 'root' })
export class SyncRunTracker {
  private readonly api = inject(CardConnectionApi);
  private readonly connectionState = inject(CardConnectionStateService);
  private readonly coordinator = inject(SyncIntentCoordinator);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly storedState = signal(IDLE_TRACKING_STATE);
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private deadlineTimer: ReturnType<typeof setTimeout> | null = null;
  private request: Subscription | null = null;
  private snapshotRequest: Subscription | null = null;
  private activeGeneration: number | null = null;
  private activeSyncRunId: string | null = null;

  // Só publica o acompanhamento se a sessão atual ainda for a geração que o iniciou.
  readonly state = computed(() => {
    const generation = this.session.currentSession()?.generation ?? null;
    const state = this.storedState();
    if (generation === null || state.generation !== generation) {
      return { status: 'idle', syncRunId: null, failure: null } satisfies SyncRunTrackingState;
    }

    return {
      status: state.status,
      syncRunId: state.syncRunId,
      failure: state.failure,
    };
  });

  constructor() {
    // Logout, expiração ou novo login cancelam timer e requests antes de qualquer resposta tardia.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      if (this.activeGeneration !== null && generation !== this.activeGeneration) {
        this.stop();
      }
    });
    this.destroyRef.onDestroy(() => this.stop());
  }

  // Inicia ou retoma acompanhamento somente para um GUID próprio durante a sessão ativa.
  start(syncRunId: string): void {
    const activeSession = this.session.currentSession();
    if (activeSession === null || !isGuid(syncRunId)) {
      return;
    }

    if (
      this.activeGeneration === activeSession.generation &&
      this.activeSyncRunId === syncRunId &&
      this.storedState().status === 'tracking'
    ) {
      return;
    }

    this.stop();
    this.activeGeneration = activeSession.generation;
    this.activeSyncRunId = syncRunId;
    this.storedState.set({
      status: 'tracking',
      syncRunId,
      failure: null,
      generation: activeSession.generation,
    });
    this.deadlineTimer = setTimeout(
      () => this.finish('unconfirmed', 'timeout'),
      MAX_TRACKING_DURATION_MS,
    );
    this.poll();
  }

  // Cancela leitura/timers quando a pessoa sai da área; a próxima entrada pode retomar o mesmo ID.
  stop(): void {
    this.clearTimers();
    this.request?.unsubscribe();
    this.snapshotRequest?.unsubscribe();
    this.request = null;
    this.snapshotRequest = null;
    this.activeGeneration = null;
    this.activeSyncRunId = null;
    this.storedState.set(IDLE_TRACKING_STATE);
  }

  // Consulta o endpoint de status uma vez por vez e agenda nova leitura somente para estado não terminal.
  private poll(): void {
    const generation = this.activeGeneration;
    const syncRunId = this.activeSyncRunId;
    if (generation === null || syncRunId === null) {
      return;
    }

    if (!this.isCurrentGeneration(generation)) {
      this.stop();
      return;
    }

    this.pollTimer = null;
    const request = this.api.getSyncRunStatus(syncRunId).subscribe({
      next: (response) => {
        if (!this.isCurrentGeneration(generation) || response.generation !== generation) {
          this.stop();
          return;
        }

        if (isTerminal(response.value)) {
          this.request = null;
          this.refreshAfterTerminal(response.value, generation);
          return;
        }

        this.scheduleNextPoll(generation);
      },
      error: (error: unknown) => {
        if (!this.isCurrentGeneration(generation)) {
          this.stop();
          return;
        }

        this.finish('unconfirmed', classifyTrackingFailure(error));
      },
    });

    if (!request.closed) {
      this.request = request;
    }
  }

  // Usa intervalo fixo com deadline absoluto para limitar tanto quantidade quanto duração do polling.
  private scheduleNextPoll(generation: number): void {
    if (!this.isCurrentGeneration(generation)) {
      this.stop();
      return;
    }

    this.pollTimer = setTimeout(() => this.poll(), POLL_INTERVAL_MS);
  }

  // Após estado terminal relê cartões e conexão; sucesso exige correlação exata no snapshot persistido.
  private refreshAfterTerminal(run: SyncRunStatus, generation: number): void {
    this.request?.unsubscribe();
    this.request = null;
    this.pollTimer && clearTimeout(this.pollTimer);
    this.pollTimer = null;
    this.connectionState.loadCards();

    let receivedSnapshot = false;
    const refresh = this.connectionState.refreshConnectionState().subscribe({
      next: (response) => {
        receivedSnapshot = true;
        if (!this.isCurrentGeneration(generation) || response.generation !== generation) {
          this.stop();
          return;
        }

        if (run.state.toLowerCase() === 'failed') {
          this.finish('failed', null);
          return;
        }

        const latestExecution = response.value.latestExecution;
        const confirmed =
          run.state.toLowerCase() === 'succeeded' &&
          latestExecution?.syncRunId.toLowerCase() === run.syncRunId.toLowerCase() &&
          latestExecution.state.toLowerCase() === 'succeeded' &&
          response.value.lastSuccessfulImportAtUtc !== null;
        this.finish(confirmed ? 'succeeded' : 'unconfirmed', confirmed ? null : 'unavailable');
      },
      error: (error: unknown) => {
        if (!this.isCurrentGeneration(generation)) {
          this.stop();
          return;
        }

        this.finish(
          run.state.toLowerCase() === 'failed' ? 'failed' : 'unconfirmed',
          run.state.toLowerCase() === 'failed' ? null : classifyTrackingFailure(error),
        );
      },
      complete: () => {
        if (!receivedSnapshot && this.isCurrentGeneration(generation)) {
          this.finish('unconfirmed', 'unavailable');
        }
      },
    });

    if (!refresh.closed) {
      this.snapshotRequest = refresh;
    }
  }

  // Registra apenas estado terminal e libera uma nova intenção depois de resultado conhecido/reconciliado.
  private finish(status: Exclude<SyncRunTrackingStatus, 'idle' | 'tracking'>, failure: SyncRunTrackingFailure | null): void {
    const generation = this.activeGeneration;
    const syncRunId = this.activeSyncRunId;
    if (generation === null || syncRunId === null || !this.isCurrentGeneration(generation)) {
      this.stop();
      return;
    }

    this.clearTimers();
    this.request?.unsubscribe();
    this.snapshotRequest?.unsubscribe();
    this.request = null;
    this.snapshotRequest = null;
    this.storedState.set({ status, syncRunId, failure, generation });
    this.coordinator.resolveTrackedRun(
      syncRunId,
      status === 'failed' ? 'execution-failed' : status,
    );
  }

  // Cancela os dois timers sem alterar o snapshot final que permanece visível na rota atual.
  private clearTimers(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
    if (this.deadlineTimer !== null) {
      clearTimeout(this.deadlineTimer);
      this.deadlineTimer = null;
    }
  }

  // Respostas só podem alterar estado enquanto a mesma geração de login continua ativa.
  private isCurrentGeneration(generation: number): boolean {
    return this.session.currentSession()?.generation === generation;
  }
}

// Trata somente os estados terminais documentados como encerramento do acompanhamento.
function isTerminal(run: SyncRunStatus): boolean {
  const state = run.state.toLowerCase();
  return state === 'succeeded' || state === 'failed';
}

// Reduz falhas de consulta a categorias estáveis sem guardar corpo, URL ou texto da API.
function classifyTrackingFailure(error: unknown): SyncRunTrackingFailure {
  if (error instanceof CardConnectionPayloadError) {
    return 'invalid-response';
  }

  if (error instanceof ProtectedApiRequestError && error.kind === 'not-found') {
    return 'not-found';
  }

  return 'unavailable';
}

// Aceita GUID canônico para que apenas IDs de execução validados componham o destino do GET.
function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}
