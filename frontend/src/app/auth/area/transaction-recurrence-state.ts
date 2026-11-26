import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { LocalSessionRequiredError, ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  TransactionListApi,
  TransactionListApiConfigurationError,
  TransactionListPayloadError,
  TransactionRecurrenceCandidate,
  TransactionRecurrenceDecision,
  TransactionRecurrenceDecisionResponse,
} from './transaction-list-api';

export type RecurrenceCandidateReadStatus = 'idle' | 'loading' | 'loaded' | 'error';
export type RecurrenceEvaluationStatus = 'idle' | 'evaluating' | 'complete' | 'error';
export type RecurrenceDecisionEditStatus = 'idle' | 'dirty' | 'saving' | 'saved' | 'error';
export type RecurrenceRequestFailure =
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'network'
  | 'unavailable'
  | 'request-failed'
  | 'invalid-response'
  | 'configuration'
  | 'stale';

/** Mantém o valor escolhido separado da decisão já confirmada pela API. */
export interface RecurrenceCandidateReview {
  readonly candidate: TransactionRecurrenceCandidate;
  readonly selectedDecision: TransactionRecurrenceDecision | '';
  readonly decisionStatus: RecurrenceDecisionEditStatus;
  readonly decisionMessage: string | null;
}

/** Agrupa carga, avaliação explícita e decisões sem misturá-las ao snapshot financeiro da lista. */
export interface TransactionRecurrenceReviewState {
  readonly generation: number | null;
  readonly candidatesStatus: RecurrenceCandidateReadStatus;
  readonly candidates: readonly RecurrenceCandidateReview[];
  readonly candidatesFailure: RecurrenceRequestFailure | null;
  readonly evaluationStatus: RecurrenceEvaluationStatus;
  readonly evaluationFailure: RecurrenceRequestFailure | null;
  readonly evaluationMessage: string | null;
}

// A sessão sem resultado começa vazia; nenhuma sugestão ou decisão é fabricada localmente.
function emptyState(generation: number | null): TransactionRecurrenceReviewState {
  return {
    generation,
    candidatesStatus: 'idle',
    candidates: [],
    candidatesFailure: null,
    evaluationStatus: 'idle',
    evaluationFailure: null,
    evaluationMessage: null,
  };
}

// Projeta cada resposta confirmada em um rascunho corrigível, mantendo o dado da API intacto.
function reviewsFromCandidates(
  candidates: readonly TransactionRecurrenceCandidate[],
): readonly RecurrenceCandidateReview[] {
  return candidates.map((candidate) => ({
    candidate,
    selectedDecision: candidate.decision ?? '',
    decisionStatus: 'idle',
    decisionMessage: null,
  }));
}

// Coordena operações de recorrência e descarta estado/respostas fora da geração autenticada atual.
@Injectable({ providedIn: 'root' })
export class TransactionRecurrenceStateService {
  private readonly api = inject(TransactionListApi);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly state = signal(emptyState(null));
  private activeGeneration: number | null | undefined;
  private candidateRequest: Subscription | null = null;
  private candidateRequestId = 0;
  private readonly decisionRequests = new Map<string, Subscription>();
  private readonly decisionRequestIds = new Map<string, number>();
  private nextDecisionRequestId = 0;

  // A view redige decisões antigas imediatamente, antes que o effect limpe o estado no logout.
  readonly view = computed(() => this.forCurrentSession(this.state()));

  constructor() {
    // Uma nova sessão limpa dados anteriores e carrega apenas sua projeção; avaliação segue explícita.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      if (generation === this.activeGeneration) {
        return;
      }

      this.activateGeneration(generation);
      if (generation !== null) {
        this.load();
      }
    });
    this.destroyRef.onDestroy(() => this.cancelPendingRequests());
  }

  // Consulta candidatos sem efeitos colaterais e mantém resultados da mesma sessão durante a carga.
  load(): void {
    const generation = this.currentGeneration();
    if (generation === null) {
      this.activateGeneration(null);
      return;
    }
    if (!this.canLoad()) {
      return;
    }

    this.cancelCandidateRequest();
    const requestId = ++this.candidateRequestId;
    this.state.update((current) => ({
      ...current,
      generation,
      candidatesStatus: 'loading',
      candidatesFailure: null,
      evaluationStatus: 'idle',
      evaluationFailure: null,
      evaluationMessage: null,
    }));

    const request = this.api.getRecurrenceCandidates().subscribe({
      next: (response) => {
        if (!this.isCurrentCandidateRequest(generation, requestId)) {
          return;
        }
        this.candidateRequest = null;
        if (response.generation !== generation) {
          this.state.update((current) => ({
            ...current,
            candidatesStatus: 'error',
            candidatesFailure: 'stale',
          }));
          return;
        }

        this.state.update((current) => ({
          ...current,
          candidatesStatus: 'loaded',
          candidates: reviewsFromCandidates(response.value),
          candidatesFailure: null,
        }));
      },
      error: (error: unknown) => {
        if (!this.isCurrentCandidateRequest(generation, requestId)) {
          return;
        }
        this.candidateRequest = null;
        this.state.update((current) => ({
          ...current,
          candidatesStatus: 'error',
          candidatesFailure: classifyRecurrenceFailure(error),
        }));
      },
    });

    if (!request.closed && this.isCurrentCandidateRequest(generation, requestId)) {
      this.candidateRequest = request;
    }
  }

  // Reavalia somente após ação explícita e preserva a projeção anterior se a operação falhar.
  evaluate(): void {
    const generation = this.currentGeneration();
    if (generation === null || !this.canEvaluate()) {
      return;
    }

    this.cancelCandidateRequest();
    const requestId = ++this.candidateRequestId;
    this.state.update((state) => ({
      ...state,
      generation,
      evaluationStatus: 'evaluating',
      evaluationFailure: null,
      evaluationMessage: null,
    }));

    const request = this.api.evaluateRecurrenceCandidates().subscribe({
      next: (response) => {
        if (!this.isCurrentCandidateRequest(generation, requestId)) {
          return;
        }
        this.candidateRequest = null;
        if (response.generation !== generation) {
          this.state.update((state) => ({
            ...state,
            evaluationStatus: 'error',
            evaluationFailure: 'stale',
          }));
          return;
        }

        this.state.update((state) => ({
          ...state,
          candidatesStatus: 'loaded',
          candidates: reviewsFromCandidates(response.value),
          candidatesFailure: null,
          evaluationStatus: 'complete',
          evaluationFailure: null,
          evaluationMessage:
            'Avaliação concluída. As sugestões foram atualizadas sem alterar os movimentos.',
        }));
      },
      error: (error: unknown) => {
        if (!this.isCurrentCandidateRequest(generation, requestId)) {
          return;
        }
        this.candidateRequest = null;
        this.state.update((state) => ({
          ...state,
          evaluationStatus: 'error',
          evaluationFailure: classifyRecurrenceFailure(error),
        }));
      },
    });

    if (!request.closed && this.isCurrentCandidateRequest(generation, requestId)) {
      this.candidateRequest = request;
    }
  }

  // Atualiza apenas o rascunho local e reconhece somente decisões publicadas no contrato.
  updateDecision(candidateId: string, decision: string): void {
    if (decision !== '' && !isRecurrenceDecision(decision)) {
      return;
    }

    this.state.update((current) => ({
      ...current,
      candidates: current.candidates.map((review) => {
        if (review.candidate.candidateId !== candidateId || review.decisionStatus === 'saving') {
          return review;
        }

        return {
          ...review,
          selectedDecision: decision,
          decisionStatus: decision === (review.candidate.decision ?? '') ? 'idle' : 'dirty',
          decisionMessage: null,
        };
      }),
    }));
  }

  // Informa se a escolha difere da API e não existe gravação concorrente para o candidato.
  canSaveDecision(review: RecurrenceCandidateReview): boolean {
    const current = this.view();
    return (
      current.generation !== null &&
      current.candidatesStatus !== 'loading' &&
      current.evaluationStatus !== 'evaluating' &&
      review.selectedDecision !== '' &&
      review.selectedDecision !== review.candidate.decision &&
      review.decisionStatus !== 'saving'
    );
  }

  // Leitura e avaliação aguardam decisões pendentes para não substituir uma confirmação recente.
  canLoad(): boolean {
    const current = this.view();
    return (
      current.generation !== null &&
      current.candidatesStatus !== 'loading' &&
      current.evaluationStatus !== 'evaluating' &&
      !current.candidates.some((review) => review.decisionStatus === 'saving')
    );
  }

  // Evita reavaliar enquanto uma leitura ou gravação concorrente ainda está em andamento.
  canEvaluate(): boolean {
    const current = this.view();
    return (
      current.generation !== null &&
      current.candidatesStatus !== 'loading' &&
      current.evaluationStatus !== 'evaluating' &&
      !current.candidates.some((review) => review.decisionStatus === 'saving')
    );
  }

  // Registra ou corrige a escolha sem enviar nenhum fato financeiro ou modificar o movimento.
  saveDecision(candidateId: string): void {
    const generation = this.currentGeneration();
    const review = this.view().candidates.find(
      (candidate) => candidate.candidate.candidateId === candidateId,
    );
    if (
      generation === null ||
      review === undefined ||
      !this.canSaveDecision(review) ||
      review.selectedDecision === ''
    ) {
      return;
    }

    this.decisionRequests.get(candidateId)?.unsubscribe();
    const requestId = ++this.nextDecisionRequestId;
    const decision = review.selectedDecision;
    this.decisionRequestIds.set(candidateId, requestId);
    this.updateReview(candidateId, (current) => ({
      ...current,
      decisionStatus: 'saving',
      decisionMessage: null,
    }));

    const request = this.api.updateRecurrenceDecision(candidateId, decision).subscribe({
      next: (response) => {
        if (!this.isCurrentDecisionRequest(candidateId, generation, requestId)) {
          return;
        }
        this.finishDecisionRequest(candidateId, requestId);
        if (
          response.generation !== generation ||
          !matchesDecisionResponse(response.value, candidateId, decision)
        ) {
          this.updateReview(candidateId, (current) => ({
            ...current,
            decisionStatus: 'error',
            decisionMessage: 'A resposta da API não confirmou esta decisão. Tente novamente.',
          }));
          return;
        }

        this.applyDecisionResponse(response.value);
      },
      error: (error: unknown) => {
        if (!this.isCurrentDecisionRequest(candidateId, generation, requestId)) {
          return;
        }
        this.finishDecisionRequest(candidateId, requestId);
        this.updateReview(candidateId, (current) => ({
          ...current,
          decisionStatus: 'error',
          decisionMessage: decisionFailureMessage(error),
        }));
      },
    });

    if (!request.closed && this.isCurrentDecisionRequest(candidateId, generation, requestId)) {
      this.decisionRequests.set(candidateId, request);
    }
  }

  // Uma geração nova invalida tanto a leitura quanto cada escrita antes de limpar o estado privado.
  private activateGeneration(generation: number | null): void {
    this.cancelPendingRequests();
    this.activeGeneration = generation;
    this.state.set(emptyState(generation));
  }

  // Cancela a consulta substituída e avança seu identificador para rejeitar callbacks tardios.
  private cancelCandidateRequest(): void {
    this.candidateRequestId += 1;
    this.candidateRequest?.unsubscribe();
    this.candidateRequest = null;
  }

  // Desconecta todas as operações pendentes em logout, troca de titular ou destruição do serviço.
  private cancelPendingRequests(): void {
    this.cancelCandidateRequest();
    for (const request of this.decisionRequests.values()) {
      request.unsubscribe();
    }
    this.decisionRequests.clear();
    this.decisionRequestIds.clear();
  }

  // A resposta só é atual para a mesma geração, sessão ativa e chamada que iniciou a leitura.
  private isCurrentCandidateRequest(generation: number, requestId: number): boolean {
    return (
      requestId === this.candidateRequestId &&
      this.activeGeneration === generation &&
      this.session.currentSession()?.generation === generation
    );
  }

  // Confere também o ID por candidato para permitir decisões paralelas sem cruzar suas respostas.
  private isCurrentDecisionRequest(
    candidateId: string,
    generation: number,
    requestId: number,
  ): boolean {
    return (
      this.decisionRequestIds.get(candidateId) === requestId &&
      this.activeGeneration === generation &&
      this.session.currentSession()?.generation === generation
    );
  }

  // Remove somente a inscrição concluída pela operação que ainda detém o ID do candidato.
  private finishDecisionRequest(candidateId: string, requestId: number): void {
    if (this.decisionRequestIds.get(candidateId) !== requestId) {
      return;
    }
    this.decisionRequestIds.delete(candidateId);
    this.decisionRequests.delete(candidateId);
  }

  // Atualiza um rascunho apenas se o candidato ainda pertence à projeção da sessão ativa.
  private updateReview(
    candidateId: string,
    update: (review: RecurrenceCandidateReview) => RecurrenceCandidateReview,
  ): void {
    this.state.update((current) => ({
      ...current,
      candidates: current.candidates.map((review) =>
        review.candidate.candidateId === candidateId ? update(review) : review,
      ),
    }));
  }

  // Incorpora a decisão confirmada mantendo versão, ocorrências e vigência da sugestão inalteradas.
  private applyDecisionResponse(response: TransactionRecurrenceDecisionResponse): void {
    this.updateReview(response.candidateId, (review) => ({
      ...review,
      candidate: {
        ...review.candidate,
        decision: response.decision,
        decisionUpdatedAtUtc: response.decisionUpdatedAtUtc,
      },
      selectedDecision: response.decision,
      decisionStatus: 'saved',
      decisionMessage: 'Decisão salva. Os movimentos originais permanecem inalterados.',
    }));
  }

  // Esconde qualquer resposta cuja geração não corresponda à sessão observada pela view.
  private forCurrentSession(
    state: TransactionRecurrenceReviewState,
  ): TransactionRecurrenceReviewState {
    const generation = this.currentGeneration();
    return generation === null || state.generation !== generation ? emptyState(generation) : state;
  }

  // Centraliza a geração presente para que cada callback e projeção aplique a mesma verificação.
  private currentGeneration(): number | null {
    return this.session.currentSession()?.generation ?? null;
  }
}

// Classifica falhas em códigos fixos; mensagens e corpos remotos nunca chegam ao estado da interface.
function classifyRecurrenceFailure(error: unknown): RecurrenceRequestFailure {
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
      case 'unauthorized':
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

// Converte falhas de escrita em orientação genérica sem ecoar detalhes privados da API.
function decisionFailureMessage(error: unknown): string {
  switch (classifyRecurrenceFailure(error)) {
    case 'forbidden':
      return 'Esta sessão não tem permissão para registrar a decisão.';
    case 'not-found':
      return 'O candidato não está mais disponível. Recarregue as sugestões.';
    case 'conflict':
      return 'A decisão não pôde ser atualizada. Recarregue as sugestões e tente novamente.';
    case 'network':
      return 'Não foi possível conectar à API; a decisão não foi confirmada.';
    case 'unavailable':
      return 'A API está temporariamente indisponível; a decisão não foi confirmada.';
    case 'invalid-response':
      return 'A resposta da API não pôde ser reconhecida; a decisão não foi atualizada.';
    case 'configuration':
      return 'A API não está configurada neste ambiente.';
    case 'stale':
      return 'A sessão mudou ou expirou. Entre novamente antes de decidir.';
    default:
      return 'Não foi possível confirmar a decisão. Tente novamente.';
  }
}

// O contrato de resposta deve confirmar o mesmo candidato e a escolha enviada antes da projeção local.
function matchesDecisionResponse(
  response: TransactionRecurrenceDecisionResponse,
  candidateId: string,
  decision: TransactionRecurrenceDecision,
): boolean {
  return response.candidateId === candidateId && response.decision === decision;
}

// Evita aceitar valores vindos de controles ou respostas fora da enumeração pública da API.
function isRecurrenceDecision(value: string): value is TransactionRecurrenceDecision {
  return (
    value === 'Subscription' ||
    value === 'Installment' ||
    value === 'IndependentPurchases' ||
    value === 'Rejected'
  );
}
