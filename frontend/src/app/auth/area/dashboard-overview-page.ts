import { Component, inject, OnDestroy, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DashboardAppliedFiltersComponent } from './dashboard-applied-filters';
import {
  DashboardCoverageState,
  DashboardExecutionState,
  DashboardFailureCode,
  DashboardNature,
  DashboardSituation,
  DashboardLastSuccessfulWindow,
} from './dashboard-api';
import {
  DashboardOverviewReadFailure,
  DashboardOverviewStateService,
} from './dashboard-overview-state';

// Traduções fixas tornam Unknown explícito e impedem que códigos remotos virem rótulos arbitrários.
const NATURE_LABELS: Record<DashboardNature, string> = {
  Purchase: 'Compra',
  Refund: 'Estorno',
  BillPayment: 'Pagamento de fatura',
  Fee: 'Tarifa',
  Adjustment: 'Ajuste',
  Unknown: 'Desconhecida (Unknown)',
};

// Situação não é interpretada como natureza nem como confirmação de uma compra.
const SITUATION_LABELS: Record<DashboardSituation, string> = {
  Effective: 'Efetivada',
  Pending: 'Pendente',
  FutureInstallment: 'Parcela futura',
  Unknown: 'Desconhecida (Unknown)',
};

// Estados de cobertura são rotulados sem ampliar completude além da janela solicitada.
const COVERAGE_LABELS: Record<DashboardCoverageState, string> = {
  Unknown: 'Desconhecida',
  Partial: 'Parcial',
  CompleteForRequestedWindow: 'Completa somente para a janela solicitada',
};

// Estados da execução recente descrevem operação, não resultado financeiro dos movimentos.
const EXECUTION_LABELS: Record<DashboardExecutionState, string> = {
  Pending: 'Pendente',
  Running: 'Em andamento',
  Succeeded: 'Concluída',
  Failed: 'Falhou',
};

// Motivos são apresentados em português por uma allowlist; mensagem remota não é refletida na tela.
const FAILURE_LABELS: Record<DashboardFailureCode, string> = {
  PROVIDER_UNAVAILABLE: 'Serviço do provedor indisponível',
  RATE_LIMITED: 'Limite de consultas atingido',
  AUTH_REQUIRED: 'Autorização necessária',
  INVALID_RESPONSE: 'Resposta do provedor incompatível',
  PROVIDER_TIMEOUT: 'Tempo de resposta do provedor excedido',
  PERSISTENCE_FAILED: 'Falha ao guardar os dados',
  CANCELLED: 'Execução cancelada',
  UNKNOWN: 'Motivo não especificado',
};

// Mensagens fixas orientam cada falha sem interpolar corpo ou detalhe recebido da API.
const READ_FAILURE_MESSAGES: Record<DashboardOverviewReadFailure, string> = {
  forbidden: 'Sua sessão não tem permissão para consultar este resumo.',
  'not-found': 'O resumo solicitado não está disponível.',
  conflict: 'Não foi possível selecionar o resumo neste momento.',
  network: 'Não foi possível conectar à API. Verifique a conexão e tente novamente.',
  unavailable: 'A API está temporariamente indisponível. Tente novamente mais tarde.',
  'request-failed': 'Não foi possível concluir a leitura do resumo.',
  'invalid-response':
    'A resposta não corresponde ao formato esperado; nenhum dado foi apresentado.',
  configuration: 'A leitura não está configurada neste ambiente.',
  stale: 'A sessão mudou antes da conclusão da leitura. Entre novamente para continuar.',
};

// Apresenta a visão protegida com filtros compartilhados e cancela sua leitura ao sair da rota.
@Component({
  imports: [DashboardAppliedFiltersComponent, RouterLink],
  selector: 'app-dashboard-overview-page',
  templateUrl: './dashboard-overview-page.html',
})
export class DashboardOverviewPage implements OnInit, OnDestroy {
  private readonly overviewState = inject(DashboardOverviewStateService);
  readonly state = this.overviewState.view;

  // Expõe rótulos de contrato por funções estáveis para manter a tradução fora do markup estrutural.
  readonly natureLabel = (nature: DashboardNature): string => NATURE_LABELS[nature];
  readonly situationLabel = (situation: DashboardSituation): string => SITUATION_LABELS[situation];
  readonly coverageLabel = (coverage: DashboardCoverageState): string => COVERAGE_LABELS[coverage];
  readonly executionLabel = (execution: DashboardExecutionState): string =>
    EXECUTION_LABELS[execution];
  readonly failureLabel = (failure: DashboardFailureCode): string => FAILURE_LABELS[failure];
  readonly readFailureMessage = (failure: DashboardOverviewReadFailure | null): string =>
    failure === null
      ? 'Não foi possível carregar o resumo. Nenhum corpo ou detalhe remoto é apresentado.'
      : READ_FAILURE_MESSAGES[failure];

  // A entrada faz somente GET do resumo; não percorre a área que avalia atualização automática.
  ngOnInit(): void {
    this.overviewState.load();
  }

  // Repetir cria apenas uma nova leitura e deixa o serviço invalidar o request anterior.
  retry(): void {
    this.overviewState.retry();
  }

  // A saída invalida resposta tardia e não mantém snapshot privado como cache da próxima entrada.
  ngOnDestroy(): void {
    this.overviewState.cancelPendingRead();
  }

  // Converte o instante para UTC de modo explícito e determinístico, sem depender do fuso do navegador.
  timestampLabel(value: string): string {
    const formatted = new Intl.DateTimeFormat('pt-BR', {
      dateStyle: 'medium',
      timeStyle: 'medium',
      timeZone: 'UTC',
    }).format(new Date(value));
    return `${formatted} UTC`;
  }

  // Preserva o limite final exclusivo e não inventa uma data quando o contrato a deixa ausente.
  requestedWindowLabel(window: DashboardLastSuccessfulWindow): string {
    const end = window.requestedToExclusive ?? 'data final não informada';
    return `${window.requestedFrom} até ${end} (final exclusivo)`;
  }

  // Exibe somente os limites observados que a API informou, sem deduzir extremos desconhecidos.
  observedWindowLabel(window: DashboardLastSuccessfulWindow): string {
    const start = window.observedFrom ?? 'início não informado';
    const end = window.observedTo ?? 'fim não informado';
    return `${start} até ${end}`;
  }
}
