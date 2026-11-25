import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import {
  LocalSessionRequiredError,
  ProtectedApiRequestError,
} from '../api-auth.interceptor';
import { SessionService } from '../session';
import { DashboardFilters, DashboardSummary, DashboardApiConfigurationError, DashboardPayloadError } from './dashboard-api';
import { DashboardOverviewQueryService } from './dashboard-overview-query';

/** Um snapshot do resumo pertence a uma única geração e ao filtro capturado na consulta. */
export interface DashboardOverviewSnapshot {
  readonly generation: number;
  readonly filters: DashboardFilters;
  readonly summary: DashboardSummary;
}

/** Estados seguros que a página pode apresentar sem conservar qualquer corpo HTTP remoto. */
export type DashboardOverviewReadStatus = 'idle' | 'loading' | 'loaded' | 'empty' | 'error';

/** Categorias fixas de falha usadas pela UI para mensagens sem detalhes do servidor. */
export type DashboardOverviewReadFailure =
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'network'
  | 'unavailable'
  | 'request-failed'
  | 'invalid-response'
  | 'configuration'
  | 'stale';

/** Mantém filtros, snapshot e falha separados e vinculados à sessão local atual. */
export interface DashboardOverviewReadState {
  readonly status: DashboardOverviewReadStatus;
  readonly generation: number | null;
  readonly filters: DashboardFilters;
  readonly snapshot: DashboardOverviewSnapshot | null;
  readonly snapshotIsPrevious: boolean;
  readonly failure: DashboardOverviewReadFailure | null;
}

// Inicia sem resumo nem cobertura fabricados; filtros voltam ao estado não aplicado entre sessões.
function idleState(generation: number | null): DashboardOverviewReadState {
  return {
    status: 'idle',
    generation,
    filters: {},
    snapshot: null,
    snapshotIsPrevious: false,
    failure: null,
  };
}

// Coordena uma consulta de cada vez e impede que snapshots privados atravessem gerações de sessão.
@Injectable({ providedIn: 'root' })
export class DashboardOverviewStateService {
  private readonly query = inject(DashboardOverviewQueryService);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly state = signal<DashboardOverviewReadState>(idleState(null));
  private activeGeneration: number | null = null;
  private pendingRequest: Subscription | null = null;
  private requestSequence = 0;

  // A UI observa somente estado cuja geração ainda está autorizada pela sessão local atual.
  readonly view = computed(() => this.forCurrentSession(this.state()));

  constructor() {
    // Logout, expiração e troca de titular cancelam a leitura e apagam o snapshot anterior.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      this.activateGeneration(generation);
    });
    this.destroyRef.onDestroy(() => this.cancelRequest());
  }

  // Toda entrada ou atualização explícita inicia somente o GET do resumo para filtros já aplicados.
  load(): void {
    this.startRead();
  }

  // Repetir equivale a uma única nova leitura; não reutiliza intenção nem endpoint de sincronização.
  retry(): void {
    this.startRead();
  }

  // Saída da página invalida callbacks tardios e remove o resumo em memória antes de outra navegação.
  cancelPendingRead(): void {
    this.cancelRequest();
    const generation = this.session.currentSession()?.generation ?? null;
    this.state.set(idleState(generation));
  }

  // Captura sessão/filtros, mantém snapshot anterior somente se filtro e geração ainda forem iguais.
  private startRead(): void {
    const activeSession = this.session.currentSession();
    if (activeSession === null) {
      this.activateGeneration(null);
      return;
    }

    this.activateGeneration(activeSession.generation);
    this.cancelRequest();
    const requestId = ++this.requestSequence;
    const filters = this.query.getAppliedFilters();
    const previous = this.state();
    const snapshot =
      previous.generation === activeSession.generation &&
      sameFilters(previous.filters, filters) &&
      previous.snapshot?.generation === activeSession.generation
        ? previous.snapshot
        : null;

    this.state.set({
      status: 'loading',
      generation: activeSession.generation,
      filters,
      snapshot,
      snapshotIsPrevious: snapshot !== null,
      failure: null,
    });

    const request = this.query.getSummaryForFilters(filters).subscribe({
      next: (response) => {
        if (requestId !== this.requestSequence || !this.isCurrentGeneration(activeSession.generation)) {
          return;
        }

        this.pendingRequest = null;
        if (response.generation !== activeSession.generation) {
          this.state.set({
            ...this.state(),
            status: 'error',
            snapshot: null,
            snapshotIsPrevious: false,
            failure: 'stale',
          });
          return;
        }

        const acceptedSnapshot: DashboardOverviewSnapshot = {
          generation: activeSession.generation,
          filters,
          summary: response.value,
        };
        this.state.set({
          status: response.value.filteredCount === 0 ? 'empty' : 'loaded',
          generation: activeSession.generation,
          filters,
          snapshot: acceptedSnapshot,
          snapshotIsPrevious: false,
          failure: null,
        });
      },
      error: (error: unknown) => {
        if (requestId !== this.requestSequence || !this.isCurrentGeneration(activeSession.generation)) {
          return;
        }

        this.pendingRequest = null;
        const failure = classifyReadFailure(error);
        const retainPrevious =
          isTransientFailure(failure) &&
          snapshot !== null &&
          snapshot.generation === activeSession.generation &&
          sameFilters(snapshot.filters, filters);
        this.state.set({
          status: 'error',
          generation: activeSession.generation,
          filters,
          snapshot: retainPrevious ? snapshot : null,
          snapshotIsPrevious: retainPrevious,
          failure,
        });
      },
    });

    if (!request.closed && requestId === this.requestSequence) {
      this.pendingRequest = request;
    }
  }

  // Qualquer nova geração cancela a leitura e elimina estado privado da sessão anterior.
  private activateGeneration(generation: number | null): void {
    if (generation === this.activeGeneration) {
      return;
    }

    this.cancelRequest();
    this.activeGeneration = generation;
    this.state.set(idleState(generation));
  }

  // Invalida request IDs mesmo quando a camada de transporte não interrompe fisicamente a resposta.
  private cancelRequest(): void {
    this.requestSequence += 1;
    this.pendingRequest?.unsubscribe();
    this.pendingRequest = null;
  }

  // Confirma a sessão a cada callback antes de publicar dados ou falhas de uma leitura protegida.
  private isCurrentGeneration(expectedGeneration: number): boolean {
    return this.session.currentSession()?.generation === expectedGeneration;
  }

  // Oculta snapshots assim que a sessão deixa de corresponder à geração que os autorizou.
  private forCurrentSession(state: DashboardOverviewReadState): DashboardOverviewReadState {
    const generation = this.session.currentSession()?.generation ?? null;
    if (generation === null || state.generation !== generation) {
      return idleState(generation);
    }
    return state;
  }
}

// Compara somente os seis filtros funcionais, sem página ou ordem de propriedades do objeto.
function sameFilters(left: DashboardFilters, right: DashboardFilters): boolean {
  return (
    left.cardId === right.cardId &&
    left.categoryId === right.categoryId &&
    left.situation === right.situation &&
    left.from === right.from &&
    left.toExclusive === right.toExclusive &&
    left.search === right.search
  );
}

// Converte erros tipados em categorias seguras e descarta mensagens/corpos externos.
function classifyReadFailure(error: unknown): DashboardOverviewReadFailure {
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

  if (error instanceof DashboardPayloadError) {
    return 'invalid-response';
  }
  if (error instanceof DashboardApiConfigurationError) {
    return 'configuration';
  }
  if (error instanceof LocalSessionRequiredError) {
    return 'stale';
  }
  return 'request-failed';
}

// Só rede/indisponibilidade podem manter temporariamente snapshot da mesma sessão e dos mesmos filtros.
function isTransientFailure(failure: DashboardOverviewReadFailure): boolean {
  return failure === 'network' || failure === 'unavailable';
}
