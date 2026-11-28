import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import {
  InvestmentClass,
  InvestmentInput,
  InvestmentItem,
  InvestmentMoney,
} from './investment-portfolio-api';
import {
  InvestmentPortfolioFailure,
  InvestmentPortfolioStateService,
} from './investment-portfolio-state';

/** Opções públicas do formulário; o código enviado continua estável e o rótulo é apresentado em português. */
const INVESTMENT_CLASS_OPTIONS: readonly {
  readonly value: InvestmentClass;
  readonly label: string;
}[] = [
  { value: 'fixed_income', label: 'Renda fixa' },
  { value: 'variable_income', label: 'Renda variável' },
  { value: 'real_estate_funds', label: 'Fundos imobiliários' },
];

/** Os formulários exigem a mesma forma decimal canônica enviada à API, sem campo do tipo number. */
const ITEM_MONEY_PATTERN = '^(?:0|[1-9]\\d{0,16})\\.\\d{2}$';

// Apresenta CRUD e resumo do backend sem converter valores monetários para precisão binária.
@Component({
  selector: 'app-investment-portfolio-page',
  templateUrl: './investment-portfolio-page.html',
})
export class InvestmentPortfolioPage implements OnInit, OnDestroy {
  private readonly portfolioState = inject(InvestmentPortfolioStateService);
  private mutationSubscription: Subscription | null = null;

  readonly state = this.portfolioState.view;
  readonly classOptions = INVESTMENT_CLASS_OPTIONS;
  readonly moneyPattern = ITEM_MONEY_PATTERN;
  readonly editingId = signal<string | null>(null);
  readonly deleteConfirmationId = signal<string | null>(null);
  readonly operationMessage = signal<string | null>(null);
  readonly draft = signal<InvestmentInput>({
    name: '',
    class: 'fixed_income',
    appliedAmount: '',
    currentAmount: '',
  });

  // A entrada da rota consulta somente a carteira e nunca passa pela inicialização da área de cartão.
  ngOnInit(): void {
    this.portfolioState.load();
  }

  // Ao sair, remove observadores da página e cancela eventual GET, sem relançar ações financeiras.
  ngOnDestroy(): void {
    this.mutationSubscription?.unsubscribe();
    this.portfolioState.cancelPendingRead();
  }

  // Mantém os campos de texto como strings, sem parseFloat ou normalização binária de centavos.
  updateTextField(field: 'name' | 'appliedAmount' | 'currentAmount', event: Event): void {
    const target = event.target as { value?: unknown } | null;
    if (target === null || typeof target.value !== 'string') {
      return;
    }

    this.draft.update((draft) => ({ ...draft, [field]: target.value as string }));
  }

  // Só códigos de classe conhecidos podem sair do controle nativo do formulário.
  updateClass(event: Event): void {
    const target = event.target as { value?: unknown } | null;
    if (
      target === null ||
      typeof target.value !== 'string' ||
      !this.classOptions.some((option) => option.value === target.value)
    ) {
      return;
    }

    this.draft.update((draft) => ({ ...draft, class: target.value as InvestmentClass }));
  }

  // Usa a validação nativa acessível antes de iniciar criação ou edição na API protegida.
  submitInvestment(event: Event): void {
    event.preventDefault();
    const form = event.target as HTMLFormElement | null;
    if (form === null || !form.reportValidity()) {
      return;
    }

    const id = this.editingId();
    this.operationMessage.set(null);
    const request =
      id === null
        ? this.portfolioState.createInvestment(this.draft())
        : this.portfolioState.updateInvestment(id, this.draft());
    const successMessage =
      id === null
        ? 'Cadastro confirmado pelo servidor. A carteira está sendo atualizada.'
        : 'Edição confirmada pelo servidor. A carteira está sendo atualizada.';

    this.observeMutation(request, successMessage, () => this.resetForm());
  }

  // Preenche os controles com as strings originais para que uma edição preserve cada centavo.
  editInvestment(item: InvestmentItem): void {
    if (this.isSaving()) {
      return;
    }

    this.deleteConfirmationId.set(null);
    this.operationMessage.set(null);
    this.editingId.set(item.id);
    this.draft.set({
      name: item.name,
      class: item.class,
      appliedAmount: item.appliedAmount,
      currentAmount: item.currentAmount,
    });
  }

  // Cancela apenas a edição ainda não enviada e restaura o formulário vazio padrão.
  cancelEdit(): void {
    if (this.isSaving()) {
      return;
    }

    this.operationMessage.set(null);
    this.resetForm();
  }

  // A confirmação permanece junto à linha da posição e funciona com controles nativos de teclado.
  requestDelete(itemId: string): void {
    if (!this.isSaving()) {
      this.operationMessage.set(null);
      this.deleteConfirmationId.update((current) => (current === itemId ? null : itemId));
    }
  }

  // Fecha a confirmação sem tocar na API nem alterar o snapshot da carteira.
  cancelDelete(): void {
    this.deleteConfirmationId.set(null);
  }

  // Só remove a posição do estado visível depois de receber 204 e iniciar a releitura conciliada.
  confirmDelete(itemId: string): void {
    if (this.deleteConfirmationId() !== itemId || this.isSaving()) {
      return;
    }

    this.operationMessage.set(null);
    this.observeMutation(
      this.portfolioState.deleteInvestment(itemId),
      'Exclusão confirmada pelo servidor. A carteira está sendo atualizada.',
      () => {
        this.deleteConfirmationId.set(null);
        if (this.editingId() === itemId) {
          this.resetForm();
        }
      },
    );
  }

  // Repetição inicia somente o GET da carteira e mantém valores anteriores vinculados à mesma sessão.
  retryRead(): void {
    this.operationMessage.set(null);
    this.portfolioState.retry();
  }

  // Traduz valores válidos com agrupamento brasileiro operando diretamente sobre os dígitos da string.
  formatBrl(value: InvestmentMoney): string {
    const parts = /^(-?)(0|[1-9]\d*)\.(\d{2})$/.exec(value);
    if (parts === null) {
      return 'Valor não disponível';
    }

    const groupedInteger = parts[2].replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    const sign = parts[1] === '-' ? '-' : '';
    return `${sign}R$\u00a0${groupedInteger},${parts[3]}`;
  }

  // Traduz somente os códigos fechados do catálogo e mantém saída fixa para qualquer valor inesperado.
  classLabel(value: InvestmentClass): string {
    return (
      this.classOptions.find((option) => option.value === value)?.label ?? 'Classe não reconhecida'
    );
  }

  // Reduz falhas de leitura a mensagens fixas e não exibe payload, URL ou detalhe remoto.
  readFailureMessage(failure: InvestmentPortfolioFailure | null): string {
    switch (failure) {
      case 'forbidden':
        return 'Sua sessão não tem permissão para consultar esta carteira.';
      case 'not-found':
        return 'A carteira solicitada não está disponível.';
      case 'conflict':
        return 'Não foi possível consultar a carteira neste momento.';
      case 'network':
        return 'Não foi possível conectar à API. Tente novamente.';
      case 'unavailable':
        return 'A API está temporariamente indisponível. Tente novamente mais tarde.';
      case 'invalid-response':
        return 'A resposta não corresponde ao formato esperado; nenhum dado incompatível foi apresentado.';
      case 'configuration':
        return 'A consulta da carteira não está configurada neste ambiente.';
      case 'stale':
        return 'A sessão mudou antes da conclusão. Entre novamente para continuar.';
      default:
        return 'Não foi possível concluir a consulta da carteira.';
    }
  }

  // Traduz falhas de gravação sem refletir mensagens ou corpos recebidos do servidor.
  mutationFailureMessage(failure: InvestmentPortfolioFailure | null): string {
    switch (failure) {
      case 'forbidden':
        return 'Sua sessão não tem permissão para alterar esta carteira.';
      case 'not-found':
        return 'A posição não está mais disponível. Atualize a carteira antes de tentar novamente.';
      case 'conflict':
        return 'A alteração não pôde ser concluída agora. Recarregue a carteira e tente novamente.';
      case 'network':
        return 'Não foi possível conectar à API; a gravação não foi confirmada.';
      case 'unavailable':
        return 'A API está temporariamente indisponível; a gravação não foi confirmada.';
      case 'invalid-response':
        return 'A resposta da gravação não corresponde ao formato esperado.';
      case 'configuration':
        return 'A gravação da carteira não está configurada neste ambiente.';
      case 'stale':
        return 'A sessão mudou antes da confirmação da gravação.';
      default:
        return 'Não foi possível confirmar a alteração da carteira.';
    }
  }

  // Os controles de gravação e confirmação ficam indisponíveis enquanto há uma única mutação ativa.
  isSaving(): boolean {
    return this.state().mutationStatus === 'saving';
  }

  // Compartilha o tratamento visual de operações, liberando o formulário só após resposta confirmada.
  private observeMutation(
    request: Observable<InvestmentItem | null>,
    successMessage: string,
    onSuccess: () => void,
  ): void {
    this.mutationSubscription?.unsubscribe();
    this.mutationSubscription = request.subscribe({
      next: () => {
        this.operationMessage.set(successMessage);
        onSuccess();
      },
      error: () => this.operationMessage.set(null),
    });
  }

  // Limpa o identificador editado e os valores para impedir reaproveitamento involuntário do formulário.
  private resetForm(): void {
    this.editingId.set(null);
    this.draft.set({
      name: '',
      class: 'fixed_income',
      appliedAmount: '',
      currentAmount: '',
    });
  }
}
