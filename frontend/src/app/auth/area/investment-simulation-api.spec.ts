import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import {
  apiAuthInterceptor,
  LocalSessionRequiredError,
  ProtectedApiRequestError,
} from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  InvestmentSimulationApi,
  InvestmentSimulationApiConfigurationError,
  InvestmentSimulationInput,
  InvestmentSimulationPayloadError,
} from './investment-simulation-api';

const API_BASE = 'https://api.example.test/proxy/';
const SIMULATION_URL = `${API_BASE}api/simulations/calculate`;
const PORTFOLIO_URL = `${API_BASE}api/investments`;
const INPUT: InvestmentSimulationInput = {
  initialAmount: '100.00',
  monthlyContribution: '10.00',
  annualRatePercent: '0.00',
  years: 1,
};

let backend: HttpTestingController;
let api: InvestmentSimulationApi;
let session: SessionService;

// O cliente e o interceptor reais usam API sintética e transporte HTTP controlado pelo teste.
function configureApi(apiBaseUrl: string | null = API_BASE): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: apiBaseUrl },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  api = TestBed.inject(InvestmentSimulationApi);
  session = TestBed.inject(SessionService);
}

// Sessões artificiais verificam geração/Bearer sem conta, token ou dado operacional.
function startSession(token = 'synthetic-simulation-token'): number {
  return session.startSession({
    accessToken: token,
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  })!;
}

// A fixture tem cardinalidade e propriedades de resposta reais, sem fazer cálculo monetário no cliente.
function simulationResponse(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    points: Array.from({ length: 13 }, (_, month) => ({
      month,
      totalContributed: month === 0 ? '100.00' : '110.00',
      projectedValue: '100.00',
    })),
    totalContributed: '220.00',
    projectedValue: '100.00',
    projectedReturn: '0.00',
    ...overrides,
  };
}

// O resumo usa os três grupos obrigatórios e permanece separado da rota de cálculo.
function portfolioResponse(): Record<string, unknown> {
  return {
    items: [],
    summary: {
      totalAppliedAmount: '0.00',
      totalCurrentAmount: '0.00',
      difference: '0.00',
      currentAmountsByClass: [
        { class: 'fixed_income', label: 'Renda fixa', currentAmount: '0.00' },
        { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
    },
  };
}

// Contrato HTTP do cálculo e leitura explícita opcional da carteira manual.
describe('Cliente HTTP do simulador de aportes', () => {
  beforeEach(() => configureApi());

  afterEach(() => backend.verify());

  // A rota usa a base configurada, preserva proxy, envia texto decimal e recebe resultado allowlisted.
  it('envia cálculo com Bearer e associa resposta válida à geração iniciadora', () => {
    const generation = startSession();
    let result: unknown;

    api.calculate(INPUT).subscribe((response) => (result = response));
    const request = backend.expectOne(SIMULATION_URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-simulation-token');
    expect(request.request.body).toEqual(INPUT);
    expect(typeof request.request.body['initialAmount']).toBe('string');
    expect(typeof request.request.body['annualRatePercent']).toBe('string');
    request.flush(simulationResponse());

    expect(result).toEqual({
      generation,
      value: {
        points: Array.from({ length: 13 }, (_, month) => ({
          month,
          totalContributed: month === 0 ? '100.00' : '110.00',
          projectedValue: '100.00',
        })),
        totalContributed: '220.00',
        projectedValue: '100.00',
        projectedReturn: '0.00',
      },
    });
  });

  // A carteira só é lida quando o chamador aciona este método; o serviço reaproveita seu decoder existente.
  it('faz apenas GET protegido da carteira quando solicitado explicitamente', () => {
    const generation = startSession();
    let result: unknown;

    api.getPortfolio().subscribe((response) => (result = response));
    const request = backend.expectOne(PORTFOLIO_URL);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-simulation-token');
    request.flush(portfolioResponse());

    expect(result).toEqual({
      generation,
      value: {
        items: [],
        summary: {
          totalAppliedAmount: '0.00',
          totalCurrentAmount: '0.00',
          difference: '0.00',
          currentAmountsByClass: [
            { class: 'fixed_income', label: 'Renda fixa', currentAmount: '0.00' },
            { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
            { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
          ],
        },
      },
    });
    backend.expectNone(SIMULATION_URL);
  });

  // Limites inclusivos aceitam os tetos textuais sem conversão ou arredondamento no navegador.
  it('aceita tetos monetários textuais e prazo máximo como entrada HTTP', () => {
    startSession();
    const maximum: InvestmentSimulationInput = {
      initialAmount: '10000000.00',
      monthlyContribution: '1000000.00',
      annualRatePercent: '50.00',
      years: 30,
    };
    api.calculate(maximum).subscribe();

    const request = backend.expectOne(SIMULATION_URL);
    expect(request.request.body).toEqual(maximum);
    request.flush(
      simulationResponse({
        points: Array.from({ length: 361 }, (_, month) => ({
          month,
          totalContributed: '10000000.00',
          projectedValue: '10000000.00',
        })),
      }),
    );
  });

  // Formas não canônicas e faixas excedidas falham localmente antes de emitir POST.
  it.each([
    { ...INPUT, initialAmount: '1.0' },
    { ...INPUT, initialAmount: '01.00' },
    { ...INPUT, initialAmount: '10000000.01' },
    { ...INPUT, monthlyContribution: '1000000.01' },
    { ...INPUT, annualRatePercent: '50.01' },
    { ...INPUT, years: 1.5 },
    { ...INPUT, extra: 'unexpected' },
  ])('recusa entrada decimal não canônica ou fora do contrato', (invalid) => {
    startSession();
    let receivedError: unknown;

    api.calculate(invalid as unknown as InvestmentSimulationInput).subscribe({
      error: (error: unknown) => (receivedError = error),
    });

    expect(receivedError).toBeInstanceOf(InvestmentSimulationPayloadError);
    backend.expectNone(() => true);
  });

  // Campos extras, dinheiro numérico ou meses fora da ordem são recusados antes de chegar ao estado.
  it.each([
    simulationResponse({ privateField: 'must-not-escape' }),
    simulationResponse({ totalContributed: 220 }),
    simulationResponse({ projectedReturn: '01.00' }),
    simulationResponse({ points: [] }),
    simulationResponse({
      points: Array.from({ length: 13 }, (_, month) => ({
        month: month === 4 ? 3 : month,
        totalContributed: '100.00',
        projectedValue: '100.00',
      })),
    }),
    simulationResponse({
      points: Array.from({ length: 13 }, (_, month) => ({
        month,
        totalContributed: '100.00',
        projectedValue: month === 4 ? 100 : '100.00',
      })),
    }),
  ])('rejeita resposta remota fora da allowlist ou da gramática textual', (payload) => {
    startSession();
    let receivedError: unknown;

    api.calculate(INPUT).subscribe({ error: (error: unknown) => (receivedError = error) });
    backend.expectOne(SIMULATION_URL).flush(payload);

    expect(receivedError).toBeInstanceOf(InvestmentSimulationPayloadError);
  });

  // 401 encerra apenas a sessão iniciadora pelo interceptor e nunca devolve corpo remoto à camada de estado.
  it('classifica 401 e encerra a sessão atual', () => {
    startSession();
    let receivedError: unknown;

    api.calculate(INPUT).subscribe({ error: (error: unknown) => (receivedError = error) });
    backend
      .expectOne(SIMULATION_URL)
      .flush(
        { detail: 'private-simulation-sentinel' },
        { status: 401, statusText: 'Unauthorized' },
      );

    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect((receivedError as ProtectedApiRequestError).kind).toBe('unauthorized');
    expect(session.currentSession()).toBeNull();
  });

  // Sem sessão não há POST nem GET; uma origem ausente também não gera URL relativa ou fallback de host.
  it('não envia chamadas sem sessão ou sem base de API configurada', () => {
    let receivedError: unknown;
    api.calculate(INPUT).subscribe({ error: (error: unknown) => (receivedError = error) });
    expect(receivedError).toBeInstanceOf(LocalSessionRequiredError);
    api.getPortfolio().subscribe({ error: (error: unknown) => (receivedError = error) });
    expect(receivedError).toBeInstanceOf(LocalSessionRequiredError);
    backend.expectNone(() => true);

    TestBed.resetTestingModule();
    configureApi(null);
    startSession();
    api.calculate(INPUT).subscribe({ error: (error: unknown) => (receivedError = error) });
    expect(receivedError).toBeInstanceOf(InvestmentSimulationApiConfigurationError);
    backend.expectNone(() => true);
  });
});
