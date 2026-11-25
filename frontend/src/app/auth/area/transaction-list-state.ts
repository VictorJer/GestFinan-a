import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { LocalSessionRequiredError, ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  TransactionCoverage,
  TransactionListApi,
  TransactionListApiConfigurationError,
  TransactionListFilters,
  TransactionListPayloadError,
  TransactionPage,
} from './transaction-list-api';

const DEFAULT_PAGE_SIZE = 25;
const MAX_SEARCH_LENGTH = 200;
const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

export interface TransactionListFilterDraft {
  readonly cardId: string;
  readonly categoryId: string;
  readonly situation: string;
  readonly from: string;
  readonly toExclusive: string;
  readonly search: string;
}

export interface TransactionListSnapshot {
  readonly generation: number;
  readonly filters: TransactionListFilters;
  readonly page: number;
  readonly pageSize: number;
  readonly response: TransactionPage;
}

export type TransactionListReadStatus = 'idle' | 'loading' | 'loaded' | 'empty' | 'error';

export type TransactionListReadFailure =
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'network'
  | 'unavailable'
  | 'request-failed'
  | 'invalid-response'
  | 'configuration'
  | 'stale';

export interface TransactionListReadState {
  readonly status: TransactionListReadStatus;
  readonly generation: number | null;
  readonly filters: TransactionListFilters;
  readonly page: number;
  readonly pageSize: number;
  readonly snapshot: TransactionListSnapshot | null;
  readonly snapshotIsPrevious: boolean;
  readonly failure: TransactionListReadFailure | null;
  readonly validationMessage: string | null;
}

// Mantém filtros independentes do texto digitado e não reutiliza estado privado entre sessões.
function emptyDraft(): TransactionListFilterDraft {
  return {
    cardId: '',
    categoryId: '',
    situation: '',
    from: '',
    toExclusive: '',
    search: '',
  };
}

// Estado inicial sem resposta mantém página e tamanho explícitos sem fabricar movimentos ou cobertura.
function idleState(generation: number | null): TransactionListReadState {
  return {
    status: 'idle',
    generation,
    filters: {},
    page: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    snapshot: null,
    snapshotIsPrevious: false,
    failure: null,
    validationMessage: null,
  };
}

// Coordena filtros, páginas e snapshots confirmados vinculados a uma única geração de sessão.
@Injectable({ providedIn: 'root' })
export class TransactionListStateService {
  private readonly api = inject(TransactionListApi);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly draftState = signal(emptyDraft());
  private readonly listState = signal(idleState(null));
  private activeGeneration: number | null = null;
  private pendingRequest: Subscription | null = null;
  private requestSequence = 0;

  // A view redige snapshots antigos assim que a sessão muda, antes de a limpeza do effect ser executada.
  readonly view = computed(() => this.forCurrentSession(this.listState()));
  readonly draft = computed(() => this.draftState());
  readonly filteredCount = computed(() => this.view().snapshot?.response.filteredCount ?? null);
  readonly coverage = computed(() => this.view().snapshot?.response.coverage ?? null);

  constructor() {
    // Logout, expiração ou novo login cancelam a leitura e apagam filtros/snapshots da geração anterior.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      this.activateGeneration(generation);
    });
    this.destroyRef.onDestroy(() => this.cancelPendingRead());
  }

  // Atualiza somente o rascunho; nenhuma chamada HTTP ocorre antes da aplicação explícita dos filtros.
  updateDraft(patch: Partial<TransactionListFilterDraft>): void {
    this.draftState.update((current) => ({ ...current, ...patch }));
  }

  // Valida datas/busca, converte campos vazios em filtros ausentes e reinicia a página se o filtro mudar.
  applyDraftFilters(): boolean {
    const draft = this.draftState();
    const validationMessage = validateDraft(draft);
    if (validationMessage !== null) {
      this.listState.update((current) => ({ ...current, validationMessage }));
      return false;
    }

    const current = this.view();
    const filters = filtersFromDraft(draft);
    const filtersChanged = !sameFilters(current.filters, filters);
    const page = filtersChanged ? 1 : current.page;
    this.startRead(filters, page);
    return true;
  }

  // Abre/reabre a página aplicada sem alterar o rascunho ou reaplicar intenção de sincronização.
  load(): void {
    const current = this.view();
    this.startRead(current.filters, current.page);
  }

  // Repete explicitamente apenas o GET atual, sem repetir automaticamente qualquer escrita ou sincronização.
  retry(): void {
    const current = this.view();
    this.startRead(current.filters, current.page);
  }

  // Navega com os mesmos filtros aplicados; páginas vazias são válidas e continuam trazendo a contagem da API.
  goToPage(page: number): boolean {
    if (!Number.isInteger(page) || page < 1) {
      return false;
    }

    const current = this.view();
    if (current.generation === null || page === current.page) {
      return false;
    }

    this.startRead(current.filters, page);
    return true;
  }

  // Cancela a leitura atual; quando existe snapshot, restaura exatamente seus filtros e sua página confirmados.
  cancelPendingRead(): void {
    this.cancelRequest();
    const current = this.listState();
    if (current.status !== 'loading') {
      return;
    }

    if (current.snapshot !== null && current.snapshot.generation === current.generation) {
      const snapshot = current.snapshot;
      this.listState.set({
        ...current,
        status: snapshot.response.items.length === 0 ? 'empty' : 'loaded',
        filters: snapshot.filters,
        page: snapshot.page,
        pageSize: snapshot.pageSize,
        snapshotIsPrevious: false,
        failure: null,
        validationMessage: null,
      });
      return;
    }

    this.listState.set(idleState(this.activeGeneration));
  }

  // Inicia a consulta e aceita a resposta somente se request, sessão e geração ainda forem atuais.
  private startRead(filters: TransactionListFilters, page: number): void {
    const activeSession = this.session.currentSession();
    if (activeSession === null) {
      this.activateGeneration(null);
      return;
    }

    this.activateGeneration(activeSession.generation);
    this.cancelRequest();
    const requestId = ++this.requestSequence;
    const previous = this.listState();
    const snapshot =
      previous.generation === activeSession.generation ? previous.snapshot : null;
    const query = { ...filters, page, pageSize: DEFAULT_PAGE_SIZE };

    this.listState.set({
      status: 'loading',
      generation: activeSession.generation,
      filters,
      page,
      pageSize: DEFAULT_PAGE_SIZE,
      snapshot,
      snapshotIsPrevious: snapshot !== null,
      failure: null,
      validationMessage: null,
    });

    const request = this.api.getTransactions(query).subscribe({
      next: (response) => {
        if (requestId !== this.requestSequence || !this.isCurrentGeneration(activeSession.generation)) {
          return;
        }

        this.pendingRequest = null;
        if (response.generation !== activeSession.generation) {
          this.listState.set({
            ...this.listState(),
            status: 'error',
            snapshot: null,
            snapshotIsPrevious: false,
            failure: 'stale',
          });
          return;
        }

        const nextSnapshot: TransactionListSnapshot = {
          generation: activeSession.generation,
          filters,
          page,
          pageSize: DEFAULT_PAGE_SIZE,
          response: response.value,
        };
        this.listState.set({
          status: response.value.items.length === 0 ? 'empty' : 'loaded',
          generation: activeSession.generation,
          filters,
          page,
          pageSize: DEFAULT_PAGE_SIZE,
          snapshot: nextSnapshot,
          snapshotIsPrevious: false,
          failure: null,
          validationMessage: null,
        });
      },
      error: (error: unknown) => {
        if (requestId !== this.requestSequence || !this.isCurrentGeneration(activeSession.generation)) {
          return;
        }

        this.pendingRequest = null;
        const failure = classifyReadFailure(error);
        const retainPrevious = isTransientFailure(failure) && snapshot !== null;
        this.listState.set({
          status: 'error',
          generation: activeSession.generation,
          filters,
          page,
          pageSize: DEFAULT_PAGE_SIZE,
          snapshot: retainPrevious ? snapshot : null,
          snapshotIsPrevious: retainPrevious,
          failure,
          validationMessage: null,
        });
      },
    });

    if (!request.closed && requestId === this.requestSequence) {
      this.pendingRequest = request;
    }
  }

  // Uma nova geração invalida qualquer request e remove filtros/snapshots antes de outra consulta.
  private activateGeneration(generation: number | null): void {
    if (generation === this.activeGeneration) {
      return;
    }

    this.cancelRequest();
    this.activeGeneration = generation;
    this.draftState.set(emptyDraft());
    this.listState.set(idleState(generation));
  }

  // Incrementa a sequência mesmo se o transporte não puder interromper fisicamente o request antigo.
  private cancelRequest(): void {
    this.requestSequence += 1;
    this.pendingRequest?.unsubscribe();
    this.pendingRequest = null;
  }

  // Confirma a geração atual em cada callback, inclusive quando a resposta chega após logout/expiração.
  private isCurrentGeneration(expectedGeneration: number): boolean {
    return this.session.currentSession()?.generation === expectedGeneration;
  }

  // A sessão atual precisa corresponder ao snapshot antes que a UI possa observar qualquer dado privado.
  private forCurrentSession(state: TransactionListReadState): TransactionListReadState {
    const generation = this.session.currentSession()?.generation ?? null;
    if (generation === null || state.generation !== generation) {
      return idleState(generation);
    }

    return state;
  }
}

// Converte texto vazio em ausência e conserva literalmente busca, IDs e filtros temporais.
function filtersFromDraft(draft: TransactionListFilterDraft): TransactionListFilters {
  const filters: {
    cardId?: string;
    categoryId?: string;
    situation?: string;
    from?: string;
    toExclusive?: string;
    search?: string;
  } = {};
  if (draft.cardId !== '') filters.cardId = draft.cardId;
  if (draft.categoryId !== '') filters.categoryId = draft.categoryId;
  if (draft.situation !== '') filters.situation = draft.situation;
  if (draft.from !== '') filters.from = draft.from;
  if (draft.toExclusive !== '') filters.toExclusive = draft.toExclusive;
  if (draft.search !== '') filters.search = draft.search;
  return filters;
}

// Compara filtros do contrato sem depender da ordem das propriedades do objeto.
function sameFilters(left: TransactionListFilters, right: TransactionListFilters): boolean {
  return (
    left.cardId === right.cardId &&
    left.categoryId === right.categoryId &&
    left.situation === right.situation &&
    left.from === right.from &&
    left.toExclusive === right.toExclusive &&
    left.search === right.search
  );
}

// Rejeita datas inexistentes, fim não posterior ao início e buscas acima do limite HTTP documentado.
function validateDraft(draft: TransactionListFilterDraft): string | null {
  if (draft.search.length > MAX_SEARCH_LENGTH) {
    return 'A busca deve ter no máximo 200 caracteres.';
  }

  if (draft.from !== '' && !isDateOnly(draft.from)) {
    return 'Informe uma data inicial válida no formato AAAA-MM-DD.';
  }

  if (draft.toExclusive !== '' && !isDateOnly(draft.toExclusive)) {
    return 'Informe uma data final válida no formato AAAA-MM-DD.';
  }

  if (draft.from !== '' && draft.toExclusive !== '' && draft.from >= draft.toExclusive) {
    return 'A data final exclusiva deve ser posterior à data inicial.';
  }

  return null;
}

// Confere o calendário antes de enviar a data técnica, evitando normalização de dias impossíveis.
function isDateOnly(value: string): boolean {
  const parts = DATE_ONLY_PATTERN.exec(value);
  if (parts === null) {
    return false;
  }

  const [, year, month, day] = parts;
  // DateOnly do servidor começa no ano 0001, enquanto Date do JavaScript aceita o ano zero.
  if (Number(year) < 1) {
    return false;
  }

  const timestamp = Date.parse(`${value}T00:00:00Z`);
  if (!Number.isFinite(timestamp)) {
    return false;
  }

  const parsed = new Date(timestamp);
  return (
    parsed.getUTCFullYear() === Number(year) &&
    parsed.getUTCMonth() + 1 === Number(month) &&
    parsed.getUTCDate() === Number(day)
  );
}

// Mantém códigos de falha fixos e nunca guarda ProblemDetails ou mensagem remota no estado visual.
function classifyReadFailure(error: unknown): TransactionListReadFailure {
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

  if (error instanceof TransactionListPayloadError) {
    return 'invalid-response';
  }
  if (error instanceof TransactionListApiConfigurationError) {
    return 'configuration';
  }
  if (error instanceof LocalSessionRequiredError) {
    return 'stale';
  }

  return 'request-failed';
}

// Só falhas transitórias de rede/serviço podem conservar o último snapshot da mesma sessão.
function isTransientFailure(failure: TransactionListReadFailure): boolean {
  return failure === 'network' || failure === 'unavailable';
}
