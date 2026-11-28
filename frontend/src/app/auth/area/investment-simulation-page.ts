import { Component, computed, effect, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { SessionService } from '../session';
import { InvestmentSimulationInput, InvestmentSimulationResult } from './investment-simulation-api';
import {
  InvestmentSimulationCalculationState,
  InvestmentSimulationFailure,
  InvestmentSimulationPortfolioValueError,
  InvestmentSimulationStateService,
  SavedInvestmentSimulationScenario,
} from './investment-simulation-state';

type SimulationDraftField = 'initialAmount' | 'monthlyContribution' | 'annualRatePercent' | 'years';

interface SimulationDraft {
  readonly initialAmount: string;
  readonly monthlyContribution: string;
  readonly annualRatePercent: string;
  readonly years: string;
}

type SimulationFieldErrors = Readonly<Record<SimulationDraftField, string | null>>;

interface SimulationChartGeometry {
  readonly contributions: string;
  readonly projectedValue: string;
  readonly maximumMonth: number;
}

const INITIAL_DRAFT: SimulationDraft = {
  initialAmount: '10000.00',
  monthlyContribution: '500.00',
  annualRatePercent: '10.00',
  years: '5',
};

const INITIAL_AMOUNT_LIMIT = '10000000.00';
const MONTHLY_CONTRIBUTION_LIMIT = '1000000.00';
const ANNUAL_RATE_LIMIT = '50.00';
const INITIAL_AMOUNT_PATTERN = '^(?:0|[1-9]\\d{0,7})\\.\\d{2}$';
const MONTHLY_CONTRIBUTION_PATTERN = '^(?:0|[1-9]\\d{0,6})\\.\\d{2}$';
const ANNUAL_RATE_PATTERN = '^(?:0|[1-9]\\d?)\\.\\d{2}$';
const YEARS_PATTERN = '^(?:[1-9]|[12]\\d|30)$';
const DECIMAL_PATTERN = /^(0|[1-9]\d*)\.(\d{2})$/;
const CHART = { left: 18, right: 622, top: 18, bottom: 208 } as const;

// Apresenta projeções confirmadas pelo backend e mantém o formulário/cenários voláteis na sessão.
@Component({
  selector: 'app-investment-simulation-page',
  templateUrl: './investment-simulation-page.html',
})
export class InvestmentSimulationPage implements OnInit, OnDestroy {
  private readonly simulationState = inject(InvestmentSimulationStateService);
  private readonly session = inject(SessionService);
  private portfolioReadSubscription: Subscription | null = null;
  private activeSessionGeneration = this.session.currentSession()?.generation ?? null;

  readonly state = this.simulationState.view;
  readonly savedScenarios = this.simulationState.savedScenarios;
  readonly draft = signal<SimulationDraft>({ ...INITIAL_DRAFT });
  readonly fieldErrors = computed(() => validateDraft(this.draft()));
  readonly chartGeometry = computed(() => {
    const current = this.state();
    return current.status === 'confirmed' && current.result !== null
      ? createChartGeometry(current.result)
      : null;
  });
  readonly initialAmountPattern = INITIAL_AMOUNT_PATTERN;
  readonly monthlyContributionPattern = MONTHLY_CONTRIBUTION_PATTERN;
  readonly annualRatePattern = ANNUAL_RATE_PATTERN;
  readonly yearsPattern = YEARS_PATTERN;
  readonly portfolioReadStatus = signal<'idle' | 'loading' | 'success' | 'error'>('idle');
  readonly portfolioReadMessage = signal<string | null>(null);
  readonly scenarioMessage = signal<string | null>(null);

  constructor() {
    // Mudança de geração remove entradas locais além da projeção/cópias que o serviço já descarta.
    effect(() => {
      const generation = this.session.currentSession()?.generation ?? null;
      if (generation !== this.activeSessionGeneration) {
        this.activeSessionGeneration = generation;
        this.clearForSessionChange();
      }
    });
  }

  // Os valores ilustrativos são identificados como exemplos e a projeção inicial continua vindo da API.
  ngOnInit(): void {
    this.publishDraftIfValid();
  }

  // Sair da página cancela a leitura opcional e remove a projeção atual do estado volátil.
  ngOnDestroy(): void {
    this.portfolioReadSubscription?.unsubscribe();
    this.simulationState.cancelPendingCalculation();
  }

  // Preserva cada caractere digitado e delega ao backend todo cálculo de valores financeiros.
  updateTextField(field: SimulationDraftField, event: Event): void {
    const target = event.target as { value?: unknown } | null;
    if (target === null || typeof target.value !== 'string') {
      return;
    }

    this.updateDraftValue(field, target.value);
  }

  // Um formulário automático não deve navegar/recarregar caso Enter seja pressionado num campo.
  preventSubmission(event: Event): void {
    event.preventDefault();
  }

  // A ação é habilitada somente para resultado confirmado e grava apenas uma cópia em memória.
  saveCurrentScenario(): void {
    const outcome = this.simulationState.saveCurrentScenario();
    switch (outcome) {
      case 'saved':
        this.scenarioMessage.set('Cenário guardado somente nesta sessão.');
        break;
      case 'limit-reached':
        this.scenarioMessage.set(
          'Já há cinco cenários guardados. Remova um cenário ou limpe a lista antes de guardar outro.',
        );
        break;
      default:
        this.scenarioMessage.set('Confirme uma simulação válida antes de guardar o cenário.');
        break;
    }
  }

  // Remove somente a cópia escolhida e anuncia a alteração sem tocar nos dados da carteira.
  removeScenario(scenario: SavedInvestmentSimulationScenario): void {
    this.simulationState.removeSavedScenario(scenario.id);
    this.scenarioMessage.set('Cenário removido desta sessão.');
  }

  // Limpa todas as cópias voláteis sem alterar a entrada ou a projeção atual.
  clearScenarios(): void {
    this.simulationState.clearSavedScenarios();
    this.scenarioMessage.set('Todos os cenários guardados foram removidos desta sessão.');
  }

  // Só o clique inicia GET protegido; uma resposta válida preenche o texto inicial sem conversão.
  copyPortfolioValue(): void {
    if (this.portfolioReadStatus() === 'loading') {
      return;
    }

    this.portfolioReadSubscription?.unsubscribe();
    this.portfolioReadStatus.set('loading');
    this.portfolioReadMessage.set(null);
    this.portfolioReadSubscription = this.simulationState.copyPortfolioValue().subscribe({
      next: (amount) => {
        this.updateDraftValue('initialAmount', amount, true);
        this.portfolioReadStatus.set('success');
        this.portfolioReadMessage.set(
          'Valor atual da carteira copiado para o valor inicial; a carteira não foi alterada.',
        );
      },
      error: (error: unknown) => {
        this.portfolioReadStatus.set('error');
        this.portfolioReadMessage.set(this.portfolioFailureMessage(error));
      },
      complete: () => {
        if (this.portfolioReadStatus() === 'loading') {
          this.portfolioReadStatus.set('error');
          this.portfolioReadMessage.set(
            'A leitura da carteira foi interrompida antes de confirmar o valor. Tente novamente.',
          );
        }
      },
    });
  }

  // Formata somente strings decimais canônicas recebidas ou mantidas pelo formulário.
  formatBrl(value: string): string {
    const match = DECIMAL_PATTERN.exec(value);
    if (match === null) {
      return 'Valor não disponível';
    }

    const groupedInteger = match[1].replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    return `R$\u00a0${groupedInteger},${match[2]}`;
  }

  // Identifica cada cópia pelas entradas originais sem produzir qualquer total derivado no navegador.
  scenarioDescription(scenario: SavedInvestmentSimulationScenario): string {
    const yearsLabel = scenario.input.years === 1 ? 'ano' : 'anos';
    return `Valor inicial ${this.formatBrl(scenario.input.initialAmount)}; aporte mensal ${this.formatBrl(scenario.input.monthlyContribution)}; taxa anual ${scenario.input.annualRatePercent}%; prazo ${scenario.input.years} ${yearsLabel}.`;
  }

  // Mensagens fixas mantêm falhas remotas, corpo HTTP e dados técnicos fora da interface.
  failureMessage(failure: InvestmentSimulationFailure | null): string {
    switch (failure) {
      case 'forbidden':
        return 'Sua sessão não tem permissão para calcular esta projeção.';
      case 'network':
        return 'Não foi possível conectar à API. Verifique a conexão e altere uma entrada válida para tentar novamente.';
      case 'unavailable':
        return 'A API está temporariamente indisponível. Tente novamente mais tarde.';
      case 'invalid-response':
        return 'A resposta não corresponde ao formato esperado; nenhum resultado incompatível foi apresentado.';
      case 'configuration':
        return 'O simulador não está configurado neste ambiente.';
      case 'stale':
        return 'A sessão mudou antes da conclusão. Entre novamente para continuar.';
      default:
        return 'Não foi possível calcular a projeção. Altere uma entrada válida para tentar novamente.';
    }
  }

  // Distingue teto e mudança de sessão sem exibir erro ou resposta da API da carteira.
  private portfolioFailureMessage(error: unknown): string {
    if (error instanceof InvestmentSimulationPortfolioValueError) {
      return error.reason === 'over-limit'
        ? 'O total atual da carteira ultrapassa R$\u00a010.000.000,00 e não foi copiado.'
        : 'A sessão ou o formulário mudou durante a leitura; nenhum valor foi copiado. Tente novamente.';
    }

    return 'Não foi possível consultar o resumo da carteira. Nenhum valor foi copiado; tente novamente.';
  }

  // Atualizações manuais invalidam feedback de cópia anterior e sempre recalculam os erros por campo.
  private updateDraftValue(
    field: SimulationDraftField,
    value: string,
    fromPortfolio = false,
  ): void {
    this.draft.update((draft) => ({ ...draft, [field]: value }));
    if (field === 'initialAmount' && !fromPortfolio && this.portfolioReadStatus() === 'success') {
      this.portfolioReadStatus.set('idle');
      this.portfolioReadMessage.set(null);
    }

    this.scenarioMessage.set(null);
    this.publishDraftIfValid();
  }

  // Entradas inválidas limpam a projeção atual; entradas válidas permanecem textuais até a API.
  private publishDraftIfValid(): void {
    const draft = this.draft();
    const errors = validateDraft(draft);
    if (Object.values(errors).some((message) => message !== null)) {
      this.simulationState.updateInput(null);
      return;
    }

    this.simulationState.updateInput({
      initialAmount: draft.initialAmount,
      monthlyContribution: draft.monthlyContribution,
      annualRatePercent: draft.annualRatePercent,
      years: Number(draft.years),
    });
  }

  // Logout, expiração ou sessão substituta encerram leituras e apagam todo dado volátil da página.
  private clearForSessionChange(): void {
    this.portfolioReadSubscription?.unsubscribe();
    this.portfolioReadSubscription = null;
    this.draft.set({
      initialAmount: '',
      monthlyContribution: '',
      annualRatePercent: '',
      years: '',
    });
    this.portfolioReadStatus.set('idle');
    this.portfolioReadMessage.set(null);
    this.scenarioMessage.set(null);
    this.simulationState.updateInput(null);
  }
}

// Validação textual replica os limites do contrato e nunca converte montantes em Number.
function validateDraft(draft: SimulationDraft): SimulationFieldErrors {
  return {
    initialAmount: validateMoneyField(
      draft.initialAmount,
      'valor inicial',
      INITIAL_AMOUNT_LIMIT,
      'currency',
    ),
    monthlyContribution: validateMoneyField(
      draft.monthlyContribution,
      'aporte mensal',
      MONTHLY_CONTRIBUTION_LIMIT,
      'currency',
    ),
    annualRatePercent: validateMoneyField(
      draft.annualRatePercent,
      'taxa anual',
      ANNUAL_RATE_LIMIT,
      'percentage',
    ),
    years: validateYearsField(draft.years),
  };
}

// Cada campo informa formato ou teto próprio para permitir correção acessível sem resultado antigo.
function validateMoneyField(
  value: string,
  label: string,
  maximum: string,
  unit: 'currency' | 'percentage',
): string | null {
  if (value.length === 0) {
    return `Informe o ${label}.`;
  }

  if (!DECIMAL_PATTERN.test(value)) {
    return 'Use ponto e exatamente duas casas decimais, sem separador de milhar.';
  }

  if (!isAtMostDecimal(value, maximum)) {
    const formattedMaximum = formatDecimalForValidation(maximum);
    return unit === 'currency'
      ? `O limite para este campo é ${formattedMaximum}.`
      : `O limite para este campo é ${formattedMaximum}%.`;
  }

  return null;
}

// Prazo é a única entrada numérica; permanece texto no controle até passar pelo intervalo inteiro.
function validateYearsField(value: string): string | null {
  if (value.length === 0) {
    return 'Informe o prazo em anos.';
  }

  if (!/^(?:[1-9]|[12]\d|30)$/.test(value)) {
    return 'Informe um número inteiro de 1 a 30 anos.';
  }

  return null;
}

// Compara decimais canônicos por comprimento e ordem lexical, sem arredondamento binário.
function isAtMostDecimal(value: string, maximum: string): boolean {
  const valueParts = DECIMAL_PATTERN.exec(value);
  const maximumParts = DECIMAL_PATTERN.exec(maximum);
  if (valueParts === null || maximumParts === null) {
    return false;
  }

  if (valueParts[1].length !== maximumParts[1].length) {
    return valueParts[1].length < maximumParts[1].length;
  }

  return (
    valueParts[1] < maximumParts[1] ||
    (valueParts[1] === maximumParts[1] && valueParts[2] <= maximumParts[2])
  );
}

// Mensagens de validação compartilham a formatação BRL sem dependência de locale do navegador.
function formatDecimalForValidation(value: string): string {
  const match = DECIMAL_PATTERN.exec(value);
  if (match === null) {
    return 'valor não disponível';
  }

  const groupedInteger = match[1].replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return `R$\u00a0${groupedInteger},${match[2]}`;
}

// A série exata continua no estado da API; esta projeção numérica isolada só define coordenadas SVG.
function createChartGeometry(result: InvestmentSimulationResult): SimulationChartGeometry {
  const maximumMonth = result.points[result.points.length - 1]?.month ?? 0;
  const values = result.points.flatMap((point) => [
    decimalToCentsForGeometry(point.totalContributed),
    decimalToCentsForGeometry(point.projectedValue),
  ]);
  const maximumCents = values.reduce((maximum, value) => (value > maximum ? value : maximum), 1n);
  const scale = Number(maximumCents);

  // Conversão monetária para Number existe somente neste mapeamento visual e nunca volta à interface.
  const coordinates = (value: string, month: number): string => {
    const x =
      maximumMonth === 0
        ? CHART.left
        : CHART.left + (month / maximumMonth) * (CHART.right - CHART.left);
    const ratio = Number(decimalToCentsForGeometry(value)) / scale;
    const y = CHART.bottom - ratio * (CHART.bottom - CHART.top);
    return `${x.toFixed(2)},${y.toFixed(2)}`;
  };

  return {
    contributions: result.points
      .map((point) => coordinates(point.totalContributed, point.month))
      .join(' '),
    projectedValue: result.points
      .map((point) => coordinates(point.projectedValue, point.month))
      .join(' '),
    maximumMonth,
  };
}

// O inteiro de centavos é usado apenas na normalização do gráfico, não em cálculos ou totais apresentados.
function decimalToCentsForGeometry(value: string): bigint {
  const match = DECIMAL_PATTERN.exec(value);
  return match === null ? 0n : BigInt(match[1]) * 100n + BigInt(match[2]);
}
