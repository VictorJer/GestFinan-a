import { Component, computed, effect, inject, OnDestroy, OnInit } from '@angular/core';
import { CardConnectionReadFailure, CardConnectionStateService } from './card-connection-state';
import { SessionService } from '../session';
import { SyncIntentCoordinator, SyncIntentMode } from './sync-intent-coordinator';
import { SyncRunTracker } from './sync-run-tracker';

// Inicializa leituras/avaliação apenas dentro da rota protegida e cancela acompanhamento ao sair da visão.
@Component({
  selector: 'app-authenticated-area',
  templateUrl: './authenticated-area.html',
})
export class AuthenticatedAreaPage implements OnInit, OnDestroy {
  private readonly connectionState = inject(CardConnectionStateService);
  private readonly intentCoordinator = inject(SyncIntentCoordinator);
  private readonly syncRunTracker = inject(SyncRunTracker);
  private readonly session = inject(SessionService);

  readonly cards = this.connectionState.cards;
  readonly connection = this.connectionState.connection;
  readonly tracking = this.syncRunTracker.state;
  readonly updateMessage = computed(() => this.describeCurrentUpdate());
  readonly canRequestManual = computed(() => {
    const currentSession = this.session.currentSession();
    const state = this.intentCoordinator.state();
    const tracking = this.syncRunTracker.state().status;
    return (
      currentSession !== null &&
      !blocksAnotherIntent(state.onEntry.status) &&
      !blocksAnotherIntent(state.manual.status) &&
      tracking !== 'tracking' &&
      tracking !== 'unconfirmed'
    );
  });
  readonly canRetryPending = computed(() => {
    const state = this.intentCoordinator.state();
    return state.manual.status === 'indeterminate' || state.onEntry.status === 'indeterminate';
  });

  constructor() {
    // Aceite ou conflito ativo retoma GET limitado enquanto a área protegida continua montada.
    effect(() => {
      const state = this.intentCoordinator.state();
      const intent = [state.manual, state.onEntry].find(
        (attempt) =>
          (attempt.status === 'accepted' || attempt.status === 'active' || attempt.status === 'tracking') &&
          attempt.syncRunId !== null,
      );
      if (intent?.syncRunId !== null && intent?.syncRunId !== undefined) {
        this.syncRunTracker.start(intent.syncRunId);
      }
    });
  }

  // A rota protegida é a única origem de GETs financeiros e da intenção automática de entrada.
  ngOnInit(): void {
    this.connectionState.loadCards();
    this.connectionState.loadConnectionState();
    this.intentCoordinator.evaluateOnEntry();
  }

  // Ao sair, interrompe requests da tela sem alterar a execução persistida ou sua chave de replay.
  ngOnDestroy(): void {
    this.connectionState.cancelPendingReads();
    this.syncRunTracker.stop();
  }

  // Botão manual inicia uma nova intenção somente quando o coordenador confirma que não há outra pendente.
  requestManualUpdate(): void {
    this.intentCoordinator.requestManual();
  }

  // Replay explícito mantém modo e chave da intenção cujo aceite ainda não foi reconciliado.
  retryPendingIntent(): void {
    const state = this.intentCoordinator.state();
    const mode: SyncIntentMode = state.manual.status === 'indeterminate' ? 'manual' : 'on-entry';
    this.intentCoordinator.retryIndeterminate(mode);
  }

  // Ação de recuperação relê cartões e conexão, sem enviar intenção ou consultar o provedor.
  refreshSnapshots(): void {
    this.connectionState.loadCards();
    this.connectionState.loadConnectionState();
  }

  // Formata somente timestamps válidos em UTC e mantém ausência explícita em vez de usar o relógio local.
  formatTimestamp(value: string | null, absentLabel = 'Não disponível'): string {
    if (value === null || !Number.isFinite(Date.parse(value))) {
      return absentLabel;
    }

    return `${new Intl.DateTimeFormat('pt-BR', {
      dateStyle: 'medium',
      timeStyle: 'short',
      timeZone: 'UTC',
    }).format(new Date(value))} UTC`;
  }

  // Converte estados/categorias fechados para textos em português sem exibir códigos internos de falha.
  readFailureMessage(
    failure: CardConnectionReadFailure | null,
    resource: 'cards' | 'connection' = 'cards',
  ): string {
    switch (failure) {
      case 'not-found':
        return resource === 'connection'
          ? 'Não há uma conexão persistida disponível para esta sessão.'
          : 'Não foi possível localizar dados de cartão para esta conexão.';
      case 'conflict':
        return 'A conexão não pode ser escolhida enquanto houver ambiguidade.';
      case 'forbidden':
        return 'Esta sessão não tem permissão para consultar estes dados.';
      case 'network':
        return 'Não foi possível conectar à API. Os dados confirmados anteriormente foram preservados.';
      case 'unavailable':
        return 'A API está temporariamente indisponível. Os dados confirmados anteriormente foram preservados.';
      case 'invalid-response':
        return 'A resposta da API não pôde ser reconhecida. Os dados anteriores foram preservados.';
      case 'configuration':
        return 'A API não está configurada neste ambiente.';
      case 'request-failed':
        return 'Não foi possível concluir a consulta. Os dados anteriores foram preservados.';
      default:
        return 'Não foi possível concluir a consulta.';
    }
  }

  // Traduz estados do backend conhecidos e não apresenta como confiáveis valores de enum desconhecidos.
  executionStateLabel(state: string): string {
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

  // Explicita o limite da cobertura reportada sem promover uma janela a histórico integral.
  coverageLabel(coverage: string): string {
    switch (coverage.toLowerCase()) {
      case 'partial':
        return 'Parcial';
      case 'completeforrequestedwindow':
        return 'Completa para a janela solicitada; não representa todo o histórico';
      case 'unknown':
      default:
        return 'Desconhecida';
    }
  }

  // Priorizamos o acompanhamento da execução e nunca chamamos 202 ou intenção indeterminada de sucesso.
  private describeCurrentUpdate(): string | null {
    const tracking = this.syncRunTracker.state();
    switch (tracking.status) {
      case 'tracking':
        return 'Solicitação aceita. A atualização está em acompanhamento; isso ainda não confirma importação concluída.';
      case 'succeeded':
        return 'Importação concluída e confirmada pelo estado persistido.';
      case 'failed':
        return 'A atualização terminou com falha. O último sucesso confirmado e os dados anteriores foram preservados.';
      case 'unconfirmed':
        return 'Não foi possível confirmar uma nova atualização. Os dados anteriores foram preservados.';
      default:
        return this.describeIntentState();
    }
  }

  // Mantém respostas de entrada/manual distintas e reduz todos os erros a mensagens fixas da interface.
  private describeIntentState(): string | null {
    const state = this.intentCoordinator.state();
    const manual = state.manual.status !== 'idle' ? state.manual : state.onEntry;
    switch (manual.status) {
      case 'submitting':
        return 'Enviando a solicitação de atualização ao servidor.';
      case 'not-required':
        return 'O servidor informou que não é necessária uma atualização automática agora.';
      case 'reauthorization-required':
        return 'Renove o acesso no provedor antes de solicitar uma nova atualização.';
      case 'accepted':
        return 'Solicitação aceita; a importação ainda não foi confirmada.';
      case 'active':
        return 'Existe uma atualização em andamento. Esta resposta não confirma que seu pedido a iniciou.';
      case 'interval':
        if (manual.nextAllowedAtUtc !== null) {
          return `Aguarde até ${this.formatTimestamp(manual.nextAllowedAtUtc)} para tentar novamente.`;
        }
        if (manual.retryAfterSeconds !== null) {
          return `Aguarde pelo menos ${manual.retryAfterSeconds} segundos antes de tentar novamente.`;
        }
        return 'Aguarde o intervalo indicado pelo servidor antes de tentar novamente.';
      case 'indeterminate':
        return 'O resultado da solicitação não foi confirmado. Repita a mesma solicitação para reconciliar.';
      case 'failed':
        return 'Não foi possível confirmar o resultado da solicitação.';
      default:
        return null;
    }
  }
}

// Bloqueia ações concorrentes enquanto uma intenção está em trânsito, aceita, ativa ou sem reconciliação.
function blocksAnotherIntent(status: string): boolean {
  return [
    'submitting',
    'accepted',
    'active',
    'indeterminate',
    'tracking',
    'unconfirmed',
  ].includes(status);
}
