import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { InvestmentSimulationInput } from './investment-simulation-api';
import { InvestmentSimulationPage } from './investment-simulation-page';
import { InvestmentSimulationStateService } from './investment-simulation-state';

const API_BASE = 'https://api.example.test';
const CALCULATION_URL = `${API_BASE}/api/simulations/calculate`;
const PORTFOLIO_URL = `${API_BASE}/api/investments`;
const DEBOUNCE_DELAY_MS = 300;
const DEFAULT_INPUT: InvestmentSimulationInput = {
  initialAmount: '10000.00',
  monthlyContribution: '500.00',
  annualRatePercent: '10.00',
  years: 5,
};

let backend: HttpTestingController;
let session: SessionService;
let simulationState: InvestmentSimulationStateService;

// A resposta é uma fixture local allowlisted; ela não representa cálculo ou serviço hospedado.
function simulationResponse(years = 5, offset = 0): Record<string, unknown> {
  const startingAmount = 10000 + offset * 1000;
  const points = Array.from({ length: years * 12 + 1 }, (_, month) => ({
    month,
    totalContributed: `${startingAmount + month * 500}.00`,
    projectedValue: `${startingAmount + month * 520}.00`,
  }));
  const finalPoint = points[points.length - 1]!;

  return {
    points,
    totalContributed: finalPoint.totalContributed,
    projectedValue: finalPoint.projectedValue,
    projectedReturn: `${years * 240}.00`,
  };
}

// O resumo sintético segue o GET protegido existente sem acesso a conta, banco ou corretora.
function portfolioResponse(totalCurrentAmount = '125.75'): Record<string, unknown> {
  return {
    items: [],
    summary: {
      totalAppliedAmount: '0.00',
      totalCurrentAmount,
      difference: totalCurrentAmount,
      currentAmountsByClass: [
        { class: 'fixed_income', label: 'Renda fixa', currentAmount: totalCurrentAmount },
        { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
    },
  };
}

// A sessão local de teste usa token sentinela e expira após tempo controlado pelo fake timer.
function startSyntheticSession(lifetimeMs = 5000): number {
  const expiresAtUtc = new Date(Date.now() + lifetimeMs).toISOString();
  return session.startSession({
    accessToken: 'synthetic-investment-simulation-journey-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc,
  })!;
}

// Configura cliente e estado reais antes de instalar sessão ou criar a página de rota.
function configureJourney(): void {
  TestBed.configureTestingModule({
    imports: [InvestmentSimulationPage],
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  session = TestBed.inject(SessionService);
  simulationState = TestBed.inject(InvestmentSimulationStateService);
}

// Cria a página depois da sessão para testar a geração protegida que recebeu o token sintético.
function createJourney(): { readonly fixture: ComponentFixture<InvestmentSimulationPage> } {
  const fixture = TestBed.createComponent(InvestmentSimulationPage);
  fixture.detectChanges();
  return { fixture };
}

// A jornada integrada exercita UI, estado e clientes HTTP reais com respostas de servidor controladas.
describe('Jornada local do simulador de aportes', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    configureJourney();
  });

  afterEach(() => {
    backend.verify({ ignoreCancelled: true });
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  // Edição, confirmação, comparação, GET explícito da carteira e expiração permanecem na geração local.
  it('completa a jornada sintética e apaga formulário, projeção e cenários ao expirar', () => {
    const generation = startSyntheticSession();
    const { fixture } = createJourney();
    const root = fixture.nativeElement as HTMLElement;

    expect(simulationState.view().status).toBe('debouncing');
    expect(simulationState.view().generation).toBe(generation);
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const initialCalculation = backend.expectOne(CALCULATION_URL);
    expect(initialCalculation.request.method).toBe('POST');
    expect(initialCalculation.request.body).toEqual(DEFAULT_INPUT);
    expect(initialCalculation.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-investment-simulation-journey-token',
    );
    initialCalculation.flush(simulationResponse());
    fixture.detectChanges();
    expect(simulationState.view().status).toBe('confirmed');
    expect(root.textContent).toContain('R$\u00a041.200,00');

    // A edição real do formulário aciona o debounce e o cliente protegido com strings monetárias.
    const contribution = root.querySelector<HTMLInputElement>('#simulation-monthly-contribution')!;
    contribution.value = '600.00';
    contribution.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const editedCalculation = backend.expectOne(CALCULATION_URL);
    expect(editedCalculation.request.body).toEqual({
      ...DEFAULT_INPUT,
      monthlyContribution: '600.00',
    });
    editedCalculation.flush(simulationResponse(5, 1));
    fixture.detectChanges();

    // Guardar é ação explícita e a cópia confirmada conserva entradas e série da mesma geração.
    Array.from(root.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.includes('Guardar este cenário'))
      ?.click();
    fixture.detectChanges();
    const savedScenario = simulationState.savedScenarios()[0]!;
    expect(simulationState.savedScenarios()).toHaveLength(1);
    expect(savedScenario.input.monthlyContribution).toBe('600.00');
    expect(savedScenario.result.points).toHaveLength(61);

    // Uma nova taxa confirma outro resultado e permite comparar a série atual com a cópia guardada.
    const annualRate = root.querySelector<HTMLInputElement>('#simulation-annual-rate')!;
    annualRate.value = '12.00';
    annualRate.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const comparisonCalculation = backend.expectOne(CALCULATION_URL);
    expect(comparisonCalculation.request.body).toEqual({
      ...DEFAULT_INPUT,
      monthlyContribution: '600.00',
      annualRatePercent: '12.00',
    });
    comparisonCalculation.flush(simulationResponse(5, 2));
    fixture.detectChanges();
    expect(root.textContent).toContain('Comparação dos totais');
    expect(root.textContent).toContain('R$\u00a042.200,00');
    expect(root.textContent).toContain('R$\u00a043.200,00');

    // A carteira é lida somente pelo botão e o interceptor limita Authorization à sessão atual.
    backend.expectNone(PORTFOLIO_URL);
    Array.from(root.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.includes('Usar valor da carteira'))
      ?.click();
    fixture.detectChanges();
    const portfolioRead = backend.expectOne(PORTFOLIO_URL);
    expect(portfolioRead.request.method).toBe('GET');
    expect(portfolioRead.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-investment-simulation-journey-token',
    );
    portfolioRead.flush(portfolioResponse());
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('#simulation-initial-amount')?.value).toBe(
      '125.75',
    );
    expect(simulationState.savedScenarios()[0]?.input.initialAmount).toBe('10000.00');

    // A leitura copiada solicita novo cálculo, mas não altera a cópia anterior nem grava a carteira.
    vi.advanceTimersByTime(DEBOUNCE_DELAY_MS);
    const copiedValueCalculation = backend.expectOne(CALCULATION_URL);
    expect(copiedValueCalculation.request.body).toEqual({
      ...DEFAULT_INPUT,
      initialAmount: '125.75',
      monthlyContribution: '600.00',
      annualRatePercent: '12.00',
    });
    copiedValueCalculation.flush(simulationResponse(5, 3));
    fixture.detectChanges();
    expect(simulationState.savedScenarios()).toHaveLength(1);
    backend.expectNone(
      (request) => request.method === 'POST' && request.url.toLowerCase().includes('/sync'),
    );
    backend.expectNone((request) => request.method !== 'GET' && request.url === PORTFOLIO_URL);

    // Expiração automática encerra a geração e redige dados da página, não só o estado do serviço.
    vi.advanceTimersByTime(5000);
    TestBed.tick();
    fixture.detectChanges();
    expect(session.currentSession()).toBeNull();
    expect(simulationState.view()).toEqual({
      status: 'idle',
      generation: null,
      input: null,
      result: null,
      failure: null,
    });
    expect(simulationState.savedScenarios()).toEqual([]);
    expect(fixture.componentInstance.draft()).toEqual({
      initialAmount: '',
      monthlyContribution: '',
      annualRatePercent: '',
      years: '',
    });
    expect(root.querySelector('svg')).toBeNull();
    expect(root.textContent).not.toContain('R$\u00a043.200,00');
    expect(root.textContent).not.toContain('Cenário guardado 1');
    backend.expectNone(() => true);
    fixture.destroy();
  });
});
