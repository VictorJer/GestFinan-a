import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { InvestmentSimulationInput, InvestmentSimulationResult } from './investment-simulation-api';
import { InvestmentSimulationPage } from './investment-simulation-page';
import {
  InvestmentSimulationCalculationState,
  InvestmentSimulationPortfolioValueError,
  InvestmentSimulationStateService,
  SavedInvestmentSimulationScenario,
} from './investment-simulation-state';

const EXAMPLE_INPUT: InvestmentSimulationInput = {
  initialAmount: '10000.00',
  monthlyContribution: '500.00',
  annualRatePercent: '10.00',
  years: 5,
};

// Os pontos sintéticos têm o tamanho do contrato por prazo e valores textuais conhecidos.
function simulationResult(years = 5, multiplier = 1): InvestmentSimulationResult {
  const points = Array.from({ length: years * 12 + 1 }, (_, month) => ({
    month,
    totalContributed: `${100 + month * 10 * multiplier}.00`,
    projectedValue: `${100 + month * 12 * multiplier}.00`,
  }));
  const finalPoint = points[points.length - 1]!;

  return {
    points,
    totalContributed: finalPoint.totalContributed,
    projectedValue: finalPoint.projectedValue,
    projectedReturn: `${(years * 12 * 2 * multiplier).toFixed(2)}`,
  };
}

// Snapshots descrevem apenas o estado que a página deve apresentar, sem iniciar API real.
function calculationState(
  status: InvestmentSimulationCalculationState['status'] = 'confirmed',
  result: InvestmentSimulationResult | null = simulationResult(),
): InvestmentSimulationCalculationState {
  const isConfirmed = status === 'confirmed';
  return {
    status,
    generation: 4,
    input: isConfirmed ? EXAMPLE_INPUT : null,
    result: isConfirmed ? result : null,
    failure: status === 'error' ? 'network' : null,
  };
}

// Cenários salvos mantêm input, totais e série independentes do resultado atual.
function savedScenario(id: number, years = 1): SavedInvestmentSimulationScenario {
  return {
    id,
    input: {
      initialAmount: '250.00',
      monthlyContribution: '25.00',
      annualRatePercent: '5.00',
      years,
    },
    result: simulationResult(years, 2),
  };
}

// Fornece o estado e operações simulados para isolar o contrato visual da rota e do transporte.
function createPage(
  initialState = calculationState(),
  initialScenarios: readonly SavedInvestmentSimulationScenario[] = [],
  portfolioRead: Observable<string> = of('125.75'),
): ComponentFixture<InvestmentSimulationPage> & {
  readonly page: InvestmentSimulationPage;
  readonly view: ReturnType<typeof signal<InvestmentSimulationCalculationState>>;
  readonly scenarios: ReturnType<typeof signal<readonly SavedInvestmentSimulationScenario[]>>;
  readonly state: {
    readonly updateInput: ReturnType<typeof vi.fn>;
    readonly cancelPendingCalculation: ReturnType<typeof vi.fn>;
    readonly saveCurrentScenario: ReturnType<typeof vi.fn>;
    readonly removeSavedScenario: ReturnType<typeof vi.fn>;
    readonly clearSavedScenarios: ReturnType<typeof vi.fn>;
    readonly copyPortfolioValue: ReturnType<typeof vi.fn>;
  };
} {
  const view = signal(initialState);
  const scenarios = signal(initialScenarios);
  const state = {
    view,
    savedScenarios: scenarios,
    updateInput: vi.fn(),
    cancelPendingCalculation: vi.fn(),
    saveCurrentScenario: vi.fn().mockReturnValue('saved'),
    removeSavedScenario: vi.fn(),
    clearSavedScenarios: vi.fn(),
    copyPortfolioValue: vi.fn().mockReturnValue(portfolioRead),
  };

  TestBed.configureTestingModule({
    imports: [InvestmentSimulationPage],
    providers: [{ provide: InvestmentSimulationStateService, useValue: state }],
  });
  const fixture = TestBed.createComponent(InvestmentSimulationPage);
  fixture.detectChanges();
  return Object.assign(fixture, {
    page: fixture.componentInstance,
    view,
    scenarios,
    state,
  });
}

// A página exibe valores confirmados e permite consultar graficamente e em texto toda a série.
describe('Página do simulador de aportes', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Exemplos são identificados, os valores confirmados vêm do estado e o SVG não substitui a tabela.
  it('apresenta os campos iniciais, totais da API e alternativa textual completa', () => {
    const fixture = createPage();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('h1')?.textContent).toContain('Simulador de aportes');
    expect(root.querySelector<HTMLInputElement>('#simulation-initial-amount')?.value).toBe(
      '10000.00',
    );
    expect(root.querySelector<HTMLInputElement>('#simulation-monthly-contribution')?.value).toBe(
      '500.00',
    );
    expect(root.querySelector<HTMLInputElement>('#simulation-annual-rate')?.value).toBe('10.00');
    expect(root.querySelector<HTMLInputElement>('#simulation-years')?.value).toBe('5');
    expect(fixture.state.updateInput).toHaveBeenCalledWith(EXAMPLE_INPUT);
    expect(typeof fixture.state.updateInput.mock.calls[0][0].initialAmount).toBe('string');
    expect(root.textContent).toContain('R$\u00a0700,00');
    expect(root.textContent).toContain('R$\u00a0820,00');
    expect(root.querySelector('svg[role="img"]')?.getAttribute('viewBox')).toBe('0 0 640 240');
    expect(root.querySelectorAll('svg polyline')).toHaveLength(2);
    expect(root.querySelector('svg polyline')?.getAttribute('points')).toContain(',');
    expect(root.querySelector('details table tbody')?.querySelectorAll('tr')).toHaveLength(61);
    expect(root.textContent).toContain(
      'Não é promessa, garantia nem recomendação de investimento.',
    );
    fixture.destroy();
    expect(fixture.state.cancelPendingCalculation).toHaveBeenCalledOnce();
  });

  // Formato e intervalo são anunciados junto ao campo e entrada inválida invalida o request atual.
  it('valida cada campo com mensagem associada e remove a projeção anterior', () => {
    const fixture = createPage();
    const root = fixture.nativeElement as HTMLElement;
    fixture.state.updateInput.mockImplementation((input: InvestmentSimulationInput | null) => {
      if (input === null) {
        fixture.view.set(calculationState('idle', null));
      }
    });
    const invalidCases = [
      ['simulation-initial-amount', '1,00', 'Use ponto e exatamente duas casas decimais'],
      ['simulation-monthly-contribution', '1000000.01', 'O limite para este campo'],
      ['simulation-annual-rate', '50.01', 'O limite para este campo'],
      ['simulation-years', '1.5', 'número inteiro de 1 a 30 anos'],
    ] as const;

    for (const [id, value, message] of invalidCases) {
      const field = root.querySelector<HTMLInputElement>(`#${id}`)!;
      field.value = value;
      field.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();

      expect(field.getAttribute('aria-invalid')).toBe('true');
      expect(root.textContent).toContain(message);
      expect(fixture.state.updateInput).toHaveBeenLastCalledWith(null);
    }

    expect(root.querySelector('svg')).toBeNull();
    expect(root.textContent).not.toContain('R$\u00a0820,00');
    fixture.destroy();
  });

  // A carteira só é consultada após o clique, e o texto copiado volta ao serviço sem parse financeiro.
  it('copia o valor inicial por ação explícita e não consulta a carteira ao abrir a página', () => {
    const fixture = createPage();
    const root = fixture.nativeElement as HTMLElement;
    const copyButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Usar valor da carteira'),
    )!;

    expect(fixture.state.copyPortfolioValue).not.toHaveBeenCalled();
    copyButton.click();
    fixture.detectChanges();

    expect(fixture.state.copyPortfolioValue).toHaveBeenCalledOnce();
    expect(root.querySelector<HTMLInputElement>('#simulation-initial-amount')?.value).toBe(
      '125.75',
    );
    expect(fixture.state.updateInput).toHaveBeenLastCalledWith({
      ...EXAMPLE_INPUT,
      initialAmount: '125.75',
    });
    expect(root.textContent).toContain('carteira copiado para o valor inicial');
    fixture.destroy();
  });

  // Total de carteira acima do teto é informado sem refletir valor remoto nem alterar o campo.
  it('apresenta falha sanitizada ao copiar valor acima do limite', () => {
    const fixture = createPage(
      calculationState(),
      [],
      throwError(() => new InvestmentSimulationPortfolioValueError('over-limit')),
    );
    const root = fixture.nativeElement as HTMLElement;
    const initial = root.querySelector<HTMLInputElement>('#simulation-initial-amount')!;
    const copyButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Usar valor da carteira'),
    )!;

    copyButton.click();
    fixture.detectChanges();

    expect(initial.value).toBe('10000.00');
    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'ultrapassa R$\u00a010.000.000,00 e não foi copiado',
    );
    expect(root.textContent).not.toContain('10000000.01');
    fixture.destroy();
  });

  // Comparação mostra os totais lado a lado e mantém a série exata de cada cenário percorrível.
  it('compara cenário atual e salvo e expõe remoção individual e limpeza total', () => {
    const saved = savedScenario(7);
    const fixture = createPage(calculationState(), [saved]);
    const root = fixture.nativeElement as HTMLElement;
    const comparison = Array.from(root.querySelectorAll('table caption')).find((caption) =>
      caption.textContent?.includes('Comparação dos totais'),
    );

    expect(comparison?.textContent).toContain('Comparação dos totais');
    expect(root.textContent).toContain('Guardado 1');
    expect(root.textContent).toContain('Cenário guardado 1');
    expect(root.textContent).toContain('R$\u00a0340,00');
    expect(root.querySelectorAll('details table')).toHaveLength(2);
    expect(root.textContent).toContain('Consultar série mensal do cenário guardado 1');

    root.querySelector<HTMLButtonElement>('[aria-label="Remover cenário guardado 1"]')!.click();
    expect(fixture.state.removeSavedScenario).toHaveBeenCalledWith(saved.id);
    Array.from(root.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.includes('Limpar cenários'))
      ?.click();
    expect(fixture.state.clearSavedScenarios).toHaveBeenCalledOnce();
    fixture.destroy();
  });

  // Campos, resumo e tabelas mantêm ordem de foco nativa e estilos fluidos para larguras menores.
  it('mantém associação de rótulos, teclado nativo e containers responsivos', () => {
    const fixture = createPage();
    const root = fixture.nativeElement as HTMLElement;
    const fields = [
      ['simulation-initial-amount', 'Valor inicial (BRL)', 'decimal'],
      ['simulation-monthly-contribution', 'Aporte mensal (BRL)', 'decimal'],
      ['simulation-annual-rate', 'Taxa anual efetiva (%)', 'decimal'],
      ['simulation-years', 'Prazo (anos)', 'numeric'],
    ] as const;

    for (const [id, labelText, inputMode] of fields) {
      const field = root.querySelector<HTMLInputElement>(`#${id}`)!;
      expect(root.querySelector<HTMLLabelElement>(`label[for="${id}"]`)?.textContent?.trim()).toBe(
        labelText,
      );
      expect(field.type).toBe('text');
      expect(field.inputMode).toBe(inputMode);
      expect(field.hasAttribute('tabindex')).toBe(false);
      expect(field.className).toContain('focus-visible:outline');
    }

    const focusedField = root.querySelector<HTMLInputElement>('#simulation-initial-amount')!;
    focusedField.focus();
    expect(document.activeElement).toBe(focusedField);
    expect(root.querySelector('form')?.className).toContain('sm:grid-cols-2');
    expect(root.querySelector('svg')?.getAttribute('class')).toContain('w-full');
    expect(root.querySelector('details table')?.parentElement?.className).toContain(
      'overflow-x-auto',
    );
    expect(root.querySelectorAll('button[tabindex]')).toHaveLength(0);
    fixture.destroy();
  });

  // Falha do cálculo apresenta uma orientação curta e nunca inclui detalhes técnicos ou corpo remoto.
  it('apresenta estado de falha recuperável sem refletir detalhes do transporte', () => {
    const fixture = createPage(calculationState('error', null));
    const root = fixture.nativeElement as HTMLElement;
    const alert = root.querySelector('[role="alert"]');

    expect(alert?.textContent).toContain('Não foi possível conectar à API');
    expect(alert?.textContent).not.toContain('ProgressEvent');
    expect(root.querySelector('svg')).toBeNull();
    expect(root.textContent).toContain('Premissas importantes');
    fixture.destroy();
  });
});
