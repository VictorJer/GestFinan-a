import { Component, computed, effect, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { SessionService } from '../session';
import { LocalSessionRequiredError, ProtectedApiRequestError } from '../api-auth.interceptor';
import { CardConnectionApi, CardSummary } from './card-connection-api';
import {
  CategoryOption,
  TransactionListApiConfigurationError,
  TransactionListPayloadError,
  TransactionCoverage,
  TransactionListApi,
  TransactionMovement,
} from './transaction-list-api';
import { TransactionListFilterDraft, TransactionListStateService } from './transaction-list-state';
import {
  RecurrenceCandidateReview,
  RecurrenceRequestFailure,
  TransactionRecurrenceStateService,
} from './transaction-recurrence-state';

type FilterOptionsStatus = 'idle' | 'loading' | 'loaded' | 'error';
type AnnotationEditStatus = 'dirty' | 'saving' | 'saved' | 'error';

// Guarda a escolha e a confirmação por movimento sem alterar o snapshot antes da releitura.
interface CategoryEditState {
  readonly selection: string;
  readonly categoryId: string | null;
  readonly status: AnnotationEditStatus;
  readonly message: string | null;
}

// Mantém o texto livre e a última anotação confirmada separados durante edição e releitura.
interface NoteEditState {
  readonly value: string;
  readonly confirmedNote: string | null;
  readonly status: AnnotationEditStatus;
  readonly message: string | null;
}

// Compoõe os filtros e a apresentação descritiva sem depender da intenção de atualizar a conexão.
@Component({
  selector: 'app-transaction-list-page',
  templateUrl: './transaction-list-page.html',
})
export class TransactionListPage implements OnInit, OnDestroy {
  private readonly session = inject(SessionService);
  private readonly cardsApi = inject(CardConnectionApi);
  private readonly listApi = inject(TransactionListApi);
  private readonly listState = inject(TransactionListStateService);
  private readonly recurrenceReviewState = inject(TransactionRecurrenceStateService);
  private optionRequests = new Subscription();
  private activeOptionsGeneration: number | null | undefined;
  private readonly cardOptionsState = signal<readonly CardSummary[]>([]);
  private readonly categoryOptionsState = signal<readonly CategoryOption[]>([]);
  private readonly categoryEdits = signal<Readonly<Record<string, CategoryEditState>>>({});
  private readonly noteEdits = signal<Readonly<Record<string, NoteEditState>>>({});

  readonly state = this.listState.view;
  // A projeção de recorrência é lida separadamente e vinculada à mesma geração de sessão.
  readonly recurrence = this.recurrenceReviewState.view;
  // Associa cada candidato ao primeiro movimento relacionado presente no snapshot visível.
  readonly recurrenceReviewsByMovement = computed(() => {
    const visibleIds = new Set(
      this.state().snapshot?.response.items.map((movement) => movement.id) ?? [],
    );
    const reviewsByMovement: Record<string, RecurrenceCandidateReview[]> = {};
    for (const review of this.recurrence().candidates) {
      const anchorId = review.candidate.occurrenceTransactionIds.find((id) => visibleIds.has(id));
      if (anchorId !== undefined) {
        (reviewsByMovement[anchorId] ??= []).push(review);
      }
    }
    return reviewsByMovement;
  });
  readonly draft = this.listState.draft;
  readonly categoryNameDraft = signal('');
  readonly categoryCreationStatus = signal<'idle' | 'saving' | 'saved' | 'error'>('idle');
  readonly categoryCreationMessage = signal<string | null>(null);
  readonly noCategorySelection = '__virtual_null_category__';
  readonly cardOptions = computed(() => this.optionsForCurrentSession(this.cardOptionsState()));
  readonly categoryOptions = computed(() =>
    this.optionsForCurrentSession(this.categoryOptionsState()),
  );
  readonly categoryFilterOptions = computed(() =>
    this.categoryOptions().filter(
      (category): category is CategoryOption & { readonly id: string } => category.id !== null,
    ),
  );
  readonly cardsStatus = signal<FilterOptionsStatus>('idle');
  readonly categoriesStatus = signal<FilterOptionsStatus>('idle');

  constructor() {
    // Troca de geração cancela catálogos antigos e remove opções privadas antes de carregar a nova sessão.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      if (generation === this.activeOptionsGeneration) {
        return;
      }

      this.activeOptionsGeneration = generation;
      this.cancelOptionReads();
      this.cardOptionsState.set([]);
      this.categoryOptionsState.set([]);
      this.categoryEdits.set({});
      this.noteEdits.set({});
      this.categoryNameDraft.set('');
      this.categoryCreationStatus.set('idle');
      this.categoryCreationMessage.set(null);
      this.cardsStatus.set(generation === null ? 'idle' : 'loading');
      this.categoriesStatus.set(generation === null ? 'idle' : 'loading');
      if (generation !== null) {
        this.loadFilterOptionsForGeneration(generation);
      }
    });

    // Descarta a confirmação local depois da releitura e esclarece quando a lista anterior foi mantida.
    effect(() => {
      const listState = this.listState.view();
      const edits = this.categoryEdits();
      const noteEdits = this.noteEdits();
      if (listState.status === 'loaded' || listState.status === 'empty') {
        const remaining = Object.fromEntries(
          Object.entries(edits).filter(([, edit]) => edit.status !== 'saved'),
        );
        if (Object.keys(remaining).length !== Object.keys(edits).length) {
          this.categoryEdits.set(remaining);
        }
        const remainingNotes = Object.fromEntries(
          Object.entries(noteEdits).filter(([, edit]) => edit.status !== 'saved'),
        );
        if (Object.keys(remainingNotes).length !== Object.keys(noteEdits).length) {
          this.noteEdits.set(remainingNotes);
        }
      } else if (listState.status === 'error' && listState.snapshotIsPrevious) {
        const failureMessage =
          'Categoria salva; a leitura falhou e os dados visíveis são anteriores.';
        const hasUnreportedReadFailure = Object.values(edits).some(
          (edit) => edit.status === 'saved' && edit.message !== failureMessage,
        );
        if (hasUnreportedReadFailure) {
          const updated = Object.fromEntries(
            Object.entries(edits).map(([id, edit]) => [
              id,
              edit.status === 'saved' ? { ...edit, message: failureMessage } : edit,
            ]),
          );
          this.categoryEdits.set(updated);
        }

        const noteFailureMessage =
          'Observação salva; a leitura falhou e os dados visíveis são anteriores.';
        const hasUnreportedNoteReadFailure = Object.values(noteEdits).some(
          (edit) => edit.status === 'saved' && edit.message !== noteFailureMessage,
        );
        if (hasUnreportedNoteReadFailure) {
          const updatedNotes = Object.fromEntries(
            Object.entries(noteEdits).map(([id, edit]) => [
              id,
              edit.status === 'saved' ? { ...edit, message: noteFailureMessage } : edit,
            ]),
          );
          this.noteEdits.set(updatedNotes);
        }
      }
    });
  }

  // A entrada da rota inicia somente leituras GET da lista e das opções de filtro.
  ngOnInit(): void {
    this.listState.load();
  }

  // Saída da rota cancela consultas pendentes sem iniciar qualquer intenção de sincronização.
  ngOnDestroy(): void {
    this.cancelOptionReads();
    this.listState.cancelPendingRead();
  }

  // Encaminha um único campo do formulário ao rascunho; a API só é consultada ao aplicar filtros.
  updateFilter(key: keyof TransactionListFilterDraft, event: Event): void {
    const target = event.target as { value?: unknown } | null;
    if (target === null || typeof target.value !== 'string') {
      return;
    }

    this.listState.updateDraft({ [key]: target.value } as Partial<TransactionListFilterDraft>);
  }

  // Impede o submit nativo de recarregar a aplicação e aplica o rascunho pela única ação explícita.
  submitFilters(event: Event): void {
    event.preventDefault();
    this.applyFilters();
  }

  // Aplica o conjunto validado e deixa o serviço reiniciar a página quando o filtro realmente muda.
  applyFilters(): void {
    this.listState.applyDraftFilters();
  }

  // Repete somente a leitura atual após erro ou pedido explícito da pessoa titular.
  retryListRead(): void {
    this.listState.retry();
  }

  // Repete somente a leitura de candidatos; não solicita reavaliação nem sincronização do histórico.
  retryRecurrenceRead(): void {
    this.recurrenceReviewState.load();
  }

  // Bloqueia a releitura enquanto avaliação ou decisão estiver pendente.
  canRetryRecurrenceRead(): boolean {
    return this.recurrenceReviewState.canLoad();
  }

  // Inicia a avaliação de recorrência somente após o acionamento explícito da pessoa titular.
  evaluateRecurrences(): void {
    this.recurrenceReviewState.evaluate();
  }

  // Evita avaliação concorrente com leitura ou gravação de decisão.
  canEvaluateRecurrences(): boolean {
    return this.recurrenceReviewState.canEvaluate();
  }

  // Coloca cada revisão junto ao primeiro movimento relacionado que está visível nesta página.
  recurrenceCandidatesForMovement(movementId: string): readonly RecurrenceCandidateReview[] {
    return this.recurrenceReviewsByMovement()[movementId] ?? [];
  }

  // Encaminha apenas escolhas que correspondem às opções nativas do controle de decisão.
  updateRecurrenceDecision(candidateId: string, event: Event): void {
    const target = event.target as { value?: unknown } | null;
    if (target !== null && typeof target.value === 'string') {
      this.recurrenceReviewState.updateDecision(candidateId, target.value);
    }
  }

  // Registra a escolha allowlisted no endpoint próprio de recorrência, sem reutilizar anotações.
  saveRecurrenceDecision(candidateId: string): void {
    this.recurrenceReviewState.saveDecision(candidateId);
  }

  // Expõe o bloqueio de gravação sem dar acesso do template ao serviço de estado interno.
  canSaveRecurrenceDecision(review: RecurrenceCandidateReview): boolean {
    return this.recurrenceReviewState.canSaveDecision(review);
  }

  // Traduz somente códigos fixos de falha e não expõe mensagens ou corpos remotos.
  recurrenceFailureMessage(failure: RecurrenceRequestFailure | null): string {
    switch (failure) {
      case 'forbidden':
        return 'Esta sessão não tem permissão para consultar ou decidir sugestões.';
      case 'not-found':
        return 'O candidato não está mais disponível. Recarregue as sugestões.';
      case 'conflict':
        return 'A operação não pode ser concluída agora. Recarregue as sugestões e tente novamente.';
      case 'network':
        return 'Não foi possível conectar à API.';
      case 'unavailable':
        return 'A API está temporariamente indisponível.';
      case 'invalid-response':
        return 'A resposta da API não pôde ser reconhecida.';
      case 'configuration':
        return 'A API não está configurada neste ambiente.';
      case 'stale':
        return 'A sessão mudou ou expirou. Entre novamente para consultar sugestões.';
      default:
        return 'Não foi possível concluir a operação. Tente novamente.';
    }
  }

  // Mantém os nomes das escolhas explícitos e não converte a decisão em natureza financeira.
  recurrenceDecisionLabel(decision: string | null): string {
    switch (decision) {
      case 'Subscription':
        return 'Assinatura';
      case 'Installment':
        return 'Parcelamento';
      case 'IndependentPurchases':
        return 'Compras independentes';
      case 'Rejected':
        return 'Padrão rejeitado';
      default:
        return 'Sem decisão do titular';
    }
  }

  // Separa sugestão atual de registro histórico sem ocultar decisões corrigíveis já confirmadas.
  recurrenceCandidateHeading(review: RecurrenceCandidateReview): string {
    return review.candidate.isCurrent
      ? 'Sugestão: possível recorrência'
      : 'Candidato histórico, não sugerido atualmente';
  }

  // O texto do botão diferencia a primeira confirmação de uma correção explícita posterior.
  recurrenceDecisionButtonLabel(review: RecurrenceCandidateReview): string {
    return review.candidate.decision === null ? 'Salvar decisão' : 'Corrigir decisão';
  }

  // Reconsulta as opções sem alterar filtros, lista ou estado de atualização da conexão.
  retryFilterOptions(): void {
    const generation = this.session.currentSession()?.generation ?? null;
    if (generation !== null) {
      this.loadFilterOptionsForGeneration(generation);
    }
  }

  // Mantém o nome digitado separado do catálogo até o envio explícito da criação.
  updateCategoryName(event: Event): void {
    const target = event.target as { value?: unknown } | null;
    if (target === null || typeof target.value !== 'string') {
      return;
    }

    this.categoryNameDraft.set(target.value);
    this.categoryCreationStatus.set('idle');
    this.categoryCreationMessage.set(null);
  }

  // Só adiciona ao catálogo local a categoria allowlisted que a API confirmou com status 201.
  submitCategoryCreation(event: Event): void {
    event.preventDefault();
    const name = this.categoryNameDraft().trim();
    if (name.length === 0 || name.length > 60) {
      this.categoryCreationStatus.set('error');
      this.categoryCreationMessage.set('Informe um nome de categoria entre 1 e 60 caracteres.');
      return;
    }

    const generation = this.session.currentSession()?.generation ?? null;
    if (generation === null || this.categoryCreationStatus() === 'saving') {
      return;
    }
    // Aguarda o GET inicial para que uma resposta antiga do catálogo não apague a categoria recém-criada.
    if (this.categoriesStatus() === 'loading') {
      return;
    }

    this.categoryCreationStatus.set('saving');
    this.categoryCreationMessage.set(null);
    this.listApi.createCategory(name).subscribe({
      next: (response) => {
        if (!this.isCurrentOptionsGeneration(generation) || response.generation !== generation) {
          return;
        }

        this.categoryOptionsState.update((current) =>
          current.some((category) => category.id === response.value.id)
            ? current
            : [...current, response.value],
        );
        this.categoryNameDraft.set('');
        this.categoryCreationStatus.set('saved');
        this.categoryCreationMessage.set(`Categoria “${response.value.name}” criada e disponível.`);
      },
      error: (error: unknown) => {
        if (!this.isCurrentOptionsGeneration(generation)) {
          return;
        }
        this.categoryCreationStatus.set('error');
        this.categoryCreationMessage.set(this.categoryWriteFailureMessage(error, 'create'));
      },
    });
  }

  // Mapeia o ID nulo do catálogo a um valor de formulário próprio, distinto de “nenhuma escolha”.
  categorySelectionValue(category: CategoryOption): string {
    return category.id ?? this.noCategorySelection;
  }

  // Mantém a associação confirmada visível enquanto a releitura atualiza filtros e contagens.
  movementCategoryLabel(movement: TransactionMovement): string {
    const edit = this.categoryEdits()[movement.id];
    return this.categoryLabel(edit?.status === 'saved' ? edit.categoryId : movement.categoryId);
  }

  // Conserva a escolha ainda não confirmada separada do valor salvo no último snapshot.
  movementCategorySelection(movement: TransactionMovement): string {
    return (
      this.categoryEdits()[movement.id]?.selection ??
      this.selectionForCategoryId(movement.categoryId)
    );
  }

  // Atualiza somente o rascunho do seletor e aceita valores realmente presentes no catálogo carregado.
  updateMovementCategory(movement: TransactionMovement, event: Event): void {
    const target = event.target as { value?: unknown } | null;
    if (target === null || typeof target.value !== 'string') {
      return;
    }

    const selection = target.value;
    if (
      !this.categoryOptions().some(
        (category) => this.categorySelectionValue(category) === selection,
      )
    ) {
      return;
    }

    this.categoryEdits.update((current) => ({
      ...current,
      [movement.id]: {
        selection,
        categoryId: movement.categoryId,
        status: 'dirty',
        message: null,
      },
    }));
  }

  // Bloqueia gravação sem catálogo confirmado, sem mudança real ou enquanto houver outra escrita.
  canSaveMovementCategory(movement: TransactionMovement): boolean {
    const edit = this.categoryEdits()[movement.id];
    if (
      this.categoriesStatus() !== 'loaded' ||
      edit === undefined ||
      edit.status === 'saving' ||
      edit.status === 'saved'
    ) {
      return false;
    }

    const selectedCategoryId = this.categoryIdFromSelection(edit.selection);
    return selectedCategoryId !== undefined && selectedCategoryId !== movement.categoryId;
  }

  // Retorna o feedback associado ao movimento sem incluir detalhes da resposta HTTP.
  movementCategoryMessage(movementId: string): string | null {
    const edit = this.categoryEdits()[movementId];
    if (edit?.status === 'saving') {
      return 'Salvando categoria…';
    }
    return edit?.message ?? null;
  }

  // Identifica recusas de escrita para leitores de tela sem colorir o estado como único sinal.
  movementCategoryHasError(movementId: string): boolean {
    return this.categoryEdits()[movementId]?.status === 'error';
  }

  // Evita nova seleção enquanto o PATCH confirmado aguarda que a leitura reflita filtros e contagens.
  isMovementCategoryLocked(movementId: string): boolean {
    const status = this.categoryEdits()[movementId]?.status;
    return status === 'saving' || status === 'saved';
  }

  // Mostra a anotação retornada pelo PATCH confirmado, sem alterar valores antes da resposta.
  movementNoteLabel(movement: TransactionMovement): string | null {
    const edit = this.noteEdits()[movement.id];
    return edit?.status === 'saved' ? edit.confirmedNote : movement.note;
  }

  // Preenche a edição com o valor persistido ou com o rascunho local ainda não confirmado.
  movementNoteValue(movement: TransactionMovement): string {
    return this.noteEdits()[movement.id]?.value ?? movement.note ?? '';
  }

  // Captura texto simples e mantém o último valor do servidor como base da comparação.
  updateMovementNote(movement: TransactionMovement, event: Event): void {
    const target = event.target as { value?: unknown } | null;
    if (target === null || typeof target.value !== 'string') {
      return;
    }

    const current = this.noteEdits()[movement.id];
    const value = target.value;
    this.noteEdits.update((edits) => ({
      ...edits,
      [movement.id]: {
        value,
        confirmedNote: current === undefined ? movement.note : current.confirmedNote,
        status: value.length > 500 ? 'error' : 'dirty',
        message: value.length > 500 ? 'A observação deve ter no máximo 500 caracteres.' : null,
      },
    }));
  }

  // Permite gravar somente uma alteração válida em relação à anotação confirmada.
  canSaveMovementNote(movement: TransactionMovement): boolean {
    const edit = this.noteEdits()[movement.id];
    if (
      edit === undefined ||
      edit.value.length > 500 ||
      edit.status === 'saving' ||
      edit.status === 'saved'
    ) {
      return false;
    }

    return edit.value !== (edit.confirmedNote ?? '');
  }

  // Expõe salvamento/erro por movimento em texto anunciado, sem incluir o corpo remoto.
  movementNoteMessage(movementId: string): string | null {
    const edit = this.noteEdits()[movementId];
    if (edit?.status === 'saving') {
      return 'Salvando observação…';
    }
    return edit?.message ?? null;
  }

  // Mantém recusas de edição identificáveis sem depender somente de cor.
  movementNoteHasError(movementId: string): boolean {
    return this.noteEdits()[movementId]?.status === 'error';
  }

  // Evita nova escrita enquanto a confirmação da observação está sendo relida.
  isMovementNoteLocked(movementId: string): boolean {
    const status = this.noteEdits()[movementId]?.status;
    return status === 'saving' || status === 'saved';
  }

  // Envia apenas note; string vazia torna-se null explícito e dispara releitura após confirmação.
  saveMovementNote(movement: TransactionMovement): void {
    const edit = this.noteEdits()[movement.id];
    if (edit === undefined || !this.canSaveMovementNote(movement)) {
      return;
    }

    const generation = this.session.currentSession()?.generation ?? null;
    if (generation === null) {
      return;
    }

    this.noteEdits.update((current) => ({
      ...current,
      [movement.id]: { ...edit, status: 'saving', message: null },
    }));
    this.listApi
      .updateAnnotation(movement.id, { note: edit.value === '' ? null : edit.value })
      .subscribe({
        next: (response) => {
          if (!this.isCurrentOptionsGeneration(generation) || response.generation !== generation) {
            return;
          }

          this.noteEdits.update((current) => ({
            ...current,
            [movement.id]: {
              value: response.value.note ?? '',
              confirmedNote: response.value.note,
              status: 'saved',
              message: 'Observação salva; atualizando movimentos e contagens.',
            },
          }));
          this.listState.load();
        },
        error: (error: unknown) => {
          if (!this.isCurrentOptionsGeneration(generation)) {
            return;
          }

          this.noteEdits.update((current) => ({
            ...current,
            [movement.id]: {
              ...edit,
              status: 'error',
              message: this.categoryWriteFailureMessage(error, 'edit'),
            },
          }));
        },
      });
  }

  // Envia somente categoryId e só atualiza a apresentação depois do PATCH confirmado e da releitura.
  saveMovementCategory(movement: TransactionMovement): void {
    const edit = this.categoryEdits()[movement.id];
    if (edit === undefined || !this.canSaveMovementCategory(movement)) {
      return;
    }

    const categoryId = this.categoryIdFromSelection(edit.selection);
    const generation = this.session.currentSession()?.generation ?? null;
    if (categoryId === undefined || generation === null) {
      return;
    }

    this.categoryEdits.update((current) => ({
      ...current,
      [movement.id]: { ...edit, status: 'saving', message: null },
    }));
    this.listApi.updateAnnotation(movement.id, { categoryId }).subscribe({
      next: (response) => {
        if (!this.isCurrentOptionsGeneration(generation) || response.generation !== generation) {
          return;
        }

        this.categoryEdits.update((current) => ({
          ...current,
          [movement.id]: {
            selection: this.selectionForCategoryId(response.value.categoryId),
            categoryId: response.value.categoryId,
            status: 'saved',
            message: 'Categoria salva; atualizando movimentos e contagens.',
          },
        }));
        this.listState.load();
      },
      error: (error: unknown) => {
        if (!this.isCurrentOptionsGeneration(generation)) {
          return;
        }

        this.categoryEdits.update((current) => ({
          ...current,
          [movement.id]: {
            ...edit,
            status: 'error',
            message: this.categoryWriteFailureMessage(error, 'edit'),
          },
        }));
      },
    });
  }

  // A paginação mantém filtros e evita novas leituras enquanto a resposta anterior está em recuperação.
  goToPage(page: number): void {
    this.listState.goToPage(page);
  }

  // Indica próxima página somente quando a contagem confirmada demonstra que ainda há itens.
  hasNextPage(): boolean {
    const snapshot = this.state().snapshot;
    return snapshot !== null && snapshot.page * snapshot.pageSize < snapshot.response.filteredCount;
  }

  // Traduz naturezas conhecidas sem promover valores desconhecidos a uma classificação inventada.
  natureLabel(nature: string): string {
    switch (nature.toLowerCase()) {
      case 'purchase':
        return 'Compra';
      case 'refund':
        return 'Estorno';
      case 'billpayment':
        return 'Pagamento de fatura';
      case 'fee':
        return 'Tarifa';
      case 'adjustment':
        return 'Ajuste';
      default:
        return 'Natureza não reconhecida';
    }
  }

  // Mantém situação pendente e parcela futura distintas de efetivação ou cobrança observada.
  situationLabel(situation: string): string {
    switch (situation.toLowerCase()) {
      case 'effective':
        return 'Efetivado';
      case 'pending':
        return 'Pendente';
      case 'futureinstallment':
        return 'Parcela futura';
      default:
        return 'Situação não reconhecida';
    }
  }

  // Formata apenas o valor individual e conserva um fallback quando o código de moeda não é reconhecido.
  amountLabel(movement: TransactionMovement): string {
    try {
      return new Intl.NumberFormat('pt-BR', {
        style: 'currency',
        currency: movement.currencyCode,
      }).format(movement.amount);
    } catch {
      const currency = movement.currencyCode.trim() || 'moeda não informada';
      return `${movement.amount} ${currency}`;
    }
  }

  // Explicita metadados parciais de parcelas sem substituir campos ausentes por zero.
  installmentLabel(movement: TransactionMovement): string {
    if (movement.installmentNumber === null && movement.totalInstallments === null) {
      return 'Não informadas';
    }

    const current = movement.installmentNumber?.toString() ?? 'não informada';
    const total = movement.totalInstallments?.toString() ?? 'não informado';
    return `Parcela ${current} de ${total}`;
  }

  // Mostra o nome conhecido da categoria sem revelar IDs ou supor associação fora do catálogo.
  categoryLabel(categoryId: string | null): string {
    if (categoryId === null) {
      return 'Sem categoria';
    }

    return (
      this.categoryOptions().find((category) => category.id === categoryId)?.name ??
      'Categoria não disponível'
    );
  }

  // Exibe cartões por nome ou final mascarado, sem inventar identificador quando ambos faltam.
  cardLabel(card: CardSummary): string {
    const name = card.name?.trim();
    if (name) {
      return name;
    }

    return card.lastFour?.trim() ? `Cartão final ${card.lastFour}` : 'Cartão sem identificação';
  }

  // Relaciona o cartão pelo catálogo carregado e não exibe o ID interno quando a opção está ausente.
  movementCardLabel(cardId: string): string {
    const card = this.cardOptions().find((option) => option.id === cardId);
    return card === undefined ? 'Cartão não disponível' : this.cardLabel(card);
  }

  // Converte motivos conhecidos em texto controlado e mantém códigos novos explicitamente não reconhecidos.
  reasonLabel(reason: string): string {
    switch (reason.toLowerCase()) {
      case 'missingoperationtype':
        return 'Tipo de operação ausente';
      case 'unknownoperationtype':
        return 'Tipo de operação não reconhecido';
      case 'contradictorytypeorsign':
        return 'Tipo e sinal contraditórios';
      case 'missingrequiredfield':
        return 'Campo necessário ausente';
      case 'unsupportedcurrency':
        return 'Moeda não compatível';
      case 'notpurchase':
        return 'Não classificado como compra';
      case 'noteffective':
        return 'Não efetivado';
      case 'unknownstatus':
        return 'Situação não reconhecida';
      case 'invalidinstallment':
        return 'Metadados de parcela inválidos';
      case 'futureinstallment':
        return 'Parcela futura';
      case 'invaliddate':
        return 'Data inválida';
      case 'missingdateoffset':
        return 'Fuso horário ausente';
      case 'timezoneunavailable':
        return 'Fuso horário indisponível';
      case 'unlinkedrefund':
        return 'Estorno sem vínculo';
      default:
        return 'Motivo não reconhecido';
    }
  }

  // Mantém o instante UTC distinto da data original e do dia técnico usado pelo filtro.
  timestampLabel(value: string | null): string {
    if (value === null || !Number.isFinite(Date.parse(value))) {
      return 'Não informada';
    }

    return `${new Intl.DateTimeFormat('pt-BR', {
      dateStyle: 'medium',
      timeStyle: 'short',
      timeZone: 'UTC',
    }).format(new Date(value))} UTC`;
  }

  // Traduz os estados fechados de cobertura sem interpretar CompleteForRequestedWindow como histórico integral.
  coverageLabel(coverageState: string): string {
    switch (coverageState.toLowerCase()) {
      case 'partial':
        return 'Parcial';
      case 'completeforrequestedwindow':
        return 'Completa somente para a janela solicitada';
      case 'unknown':
      default:
        return 'Desconhecida';
    }
  }

  // Traduz estados da execução recente em separado dos dados de cobertura confirmados.
  executionLabel(state: string): string {
    switch (state.toLowerCase()) {
      case 'pending':
        return 'Aguardando processamento';
      case 'running':
        return 'Em andamento';
      case 'succeeded':
        return 'Concluída';
      case 'failed':
        return 'Falhou';
      default:
        return 'Estado não reconhecido';
    }
  }

  // Usa mensagens fixas para filtros inválidos e categorias fechadas de falha, nunca corpo HTTP bruto.
  readFailureMessage(failure: string | null): string {
    switch (failure) {
      case 'forbidden':
        return 'Esta sessão não tem permissão para consultar estes movimentos.';
      case 'not-found':
        return 'Um dos filtros selecionados não está disponível. Revise os filtros e tente novamente.';
      case 'conflict':
        return 'A consulta não pode continuar enquanto a conexão estiver ambígua.';
      case 'network':
        return 'Não foi possível conectar à API.';
      case 'unavailable':
        return 'A API está temporariamente indisponível.';
      case 'invalid-response':
        return 'A resposta da API não pôde ser reconhecida.';
      case 'configuration':
        return 'A API não está configurada neste ambiente.';
      case 'stale':
        return 'A sessão mudou. Entre novamente para consultar seus movimentos.';
      default:
        return 'Não foi possível concluir a consulta.';
    }
  }

  // Descreve falhas transitórias com o aviso de que o snapshot exibido pertence a uma leitura anterior.
  previousSnapshotMessage(): string {
    return this.state().status === 'loading'
      ? 'A consulta está em andamento; os dados visíveis são a resposta confirmada anterior.'
      : 'A consulta falhou; os dados visíveis são a resposta confirmada anterior e podem não corresponder aos filtros atuais.';
  }

  // Converte o envelope de cobertura em mensagens explícitas sem inferir ausência na origem.
  coverageSummary(coverage: TransactionCoverage): string {
    if (coverage.lastSuccessfulImportAtUtc === null || coverage.lastSuccessfulWindow === null) {
      return 'Cobertura desconhecida; uma lista vazia não confirma ausência de movimentos na origem.';
    }

    return `Janela confirmada: ${coverage.lastSuccessfulWindow.requestedFrom} até ${coverage.lastSuccessfulWindow.requestedToExclusive ?? 'sem data final'}; cobertura ${this.coverageLabel(coverage.lastSuccessfulWindow.coverageState)}.`;
  }

  // Substitui leituras de opções ao repetir, mantendo seu resultado amarrado à geração que as iniciou.
  private loadFilterOptionsForGeneration(generation: number): void {
    this.cancelOptionReads();
    this.cardsStatus.set('loading');
    this.categoriesStatus.set('loading');

    this.optionRequests.add(
      this.cardsApi.getCards().subscribe({
        next: (response) => {
          if (!this.isCurrentOptionsGeneration(generation) || response.generation !== generation) {
            return;
          }
          this.cardOptionsState.set(response.value);
          this.cardsStatus.set('loaded');
        },
        error: (_error: unknown) => {
          if (this.isCurrentOptionsGeneration(generation)) {
            this.cardsStatus.set('error');
          }
        },
      }),
    );

    this.optionRequests.add(
      this.listApi.getCategories().subscribe({
        next: (response) => {
          if (!this.isCurrentOptionsGeneration(generation) || response.generation !== generation) {
            return;
          }
          this.categoryOptionsState.set(response.value);
          this.categoriesStatus.set('loaded');
        },
        error: (_error: unknown) => {
          if (this.isCurrentOptionsGeneration(generation)) {
            this.categoriesStatus.set('error');
          }
        },
      }),
    );
  }

  // Respostas de cartões/categorias só podem atualizar as opções enquanto a mesma sessão continua ativa.
  private isCurrentOptionsGeneration(generation: number): boolean {
    return (
      this.activeOptionsGeneration === generation &&
      this.session.currentSession()?.generation === generation
    );
  }

  // Resolve o valor do seletor apenas contra opções allowlisted e preserva null como limpeza válida.
  private categoryIdFromSelection(selection: string): string | null | undefined {
    const category = this.categoryOptions().find(
      (option) => this.categorySelectionValue(option) === selection,
    );
    return category === undefined ? undefined : category.id;
  }

  // Converte uma confirmação do servidor de volta ao valor correspondente no seletor.
  private selectionForCategoryId(categoryId: string | null): string {
    return categoryId ?? this.noCategorySelection;
  }

  // Mantém mensagens de criação/edição específicas sem revelar ProblemDetails nem conteúdo remoto.
  private categoryWriteFailureMessage(error: unknown, operation: 'create' | 'edit'): string {
    if (error instanceof ProtectedApiRequestError) {
      switch (error.kind) {
        case 'conflict':
          return operation === 'create'
            ? 'Já existe uma categoria com esse nome ou ele está reservado.'
            : 'A categoria mudou ou não está mais disponível. Releia os movimentos e tente novamente.';
        case 'forbidden':
          return 'Esta sessão não tem permissão para organizar este movimento.';
        case 'not-found':
          return 'O recurso necessário não está mais disponível. Releia o catálogo e a lista.';
        case 'network':
          return 'Não foi possível conectar à API; nenhuma alteração foi confirmada.';
        case 'unavailable':
          return 'A API está temporariamente indisponível; nenhuma alteração foi confirmada.';
        case 'unauthorized':
        case 'stale':
          return 'A sessão mudou ou expirou. Entre novamente antes de organizar movimentos.';
        case 'request-failed':
          return operation === 'create'
            ? 'A API recusou a criação. Revise o nome e tente novamente.'
            : 'A API recusou a alteração. Revise a seleção e tente novamente.';
        default:
          return 'Não foi possível confirmar a alteração. Tente novamente.';
      }
    }

    if (error instanceof TransactionListPayloadError) {
      return 'A resposta da API não pôde ser reconhecida; nenhuma opção foi adicionada.';
    }
    if (error instanceof TransactionListApiConfigurationError) {
      return 'A API não está configurada neste ambiente.';
    }
    if (error instanceof LocalSessionRequiredError) {
      return 'Entre novamente para organizar movimentos.';
    }

    return 'Não foi possível confirmar a alteração. Tente novamente.';
  }

  // Oculta listas de cartões/categorias imediatamente quando a sessão não corresponde à geração carregada.
  private optionsForCurrentSession<T>(options: readonly T[]): readonly T[] {
    const generation = this.session.currentSession()?.generation ?? null;
    return generation !== null && generation === this.activeOptionsGeneration ? options : [];
  }

  // Cancela os dois catálogos em logout, troca de geração, retry ou destruição da página.
  private cancelOptionReads(): void {
    this.optionRequests.unsubscribe();
    this.optionRequests = new Subscription();
  }
}
