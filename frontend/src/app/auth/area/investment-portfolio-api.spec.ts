import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor, LocalSessionRequiredError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import {
  InvestmentInput,
  InvestmentPortfolioApi,
  InvestmentPortfolioApiConfigurationError,
  InvestmentPortfolioPayloadError,
} from './investment-portfolio-api';

const API_BASE = 'https://api.example.test/proxy/';
const INVESTMENTS_URL = `${API_BASE}api/investments`;
const INVESTMENT_ID = '87b65749-c927-431f-8d51-35fdf3106336';
const MAXIMUM_ITEM_AMOUNT = '99999999999999999.99';

let backend: HttpTestingController;
let api: InvestmentPortfolioApi;
let session: SessionService;

// Usa o cliente e interceptor reais com origem sintética e transporte Angular controlado pelo teste.
function configureApi(apiBaseUrl: string | null = API_BASE): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: apiBaseUrl },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  api = TestBed.inject(InvestmentPortfolioApi);
  session = TestBed.inject(SessionService);
}

// A sessão sintética confirma geração e Bearer sem utilizar conta ou token operacional.
function startSession(token = 'synthetic-investment-token'): number {
  return session.startSession({
    accessToken: token,
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  })!;
}

// O item inclui somente as propriedades publicadas e mantém cada valor monetário como string.
function investmentResponse(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: INVESTMENT_ID,
    name: 'Reserva sintética',
    class: 'fixed_income',
    appliedAmount: '120.50',
    currentAmount: '125.75',
    ...overrides,
  };
}

// O resumo representa todos os grupos confirmados, inclusive classes cujo valor atual é zero.
function portfolioResponse(
  item: Record<string, unknown> = investmentResponse(),
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    items: [item],
    summary: {
      totalAppliedAmount: '120.50',
      totalCurrentAmount: '125.75',
      difference: '5.25',
      currentAmountsByClass: [
        { class: 'fixed_income', label: 'Renda fixa', currentAmount: '125.75' },
        { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
    },
    ...overrides,
  };
}

// Nenhuma leitura da carteira pode iniciar sincronização ou consultar cartões/movimentos.
function expectNoOtherFinancialRequests(): void {
  backend.expectNone((request) => !request.url.startsWith(INVESTMENTS_URL));
}

// Confirma a fronteira de rede exclusiva da carteira e a gramática monetária sem Number JavaScript.
describe('Cliente HTTP da carteira manual', () => {
  beforeEach(() => configureApi());

  afterEach(() => backend.verify());

  // GET retorna o resumo do backend sem alterar precisão do máximo ou aceitar agregados numéricos.
  it('consulta a carteira protegida e preserva strings decimais em toda a faixa individual', () => {
    const generation = startSession();
    let result: unknown;
    const payload = portfolioResponse(
      investmentResponse({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '0.00' }),
      {
        summary: {
          totalAppliedAmount: MAXIMUM_ITEM_AMOUNT,
          totalCurrentAmount: '0.00',
          difference: `-${MAXIMUM_ITEM_AMOUNT}`,
          currentAmountsByClass: [
            { class: 'fixed_income', label: 'Renda fixa', currentAmount: '0.00' },
            { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
            { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
          ],
        },
      },
    );

    api.getPortfolio().subscribe((response) => (result = response));
    const request = backend.expectOne(INVESTMENTS_URL);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer synthetic-investment-token');
    request.flush(payload);

    expect(result).toEqual({
      generation,
      value: {
        items: [
          {
            id: INVESTMENT_ID,
            name: 'Reserva sintética',
            class: 'fixed_income',
            appliedAmount: MAXIMUM_ITEM_AMOUNT,
            currentAmount: '0.00',
          },
        ],
        summary: {
          totalAppliedAmount: MAXIMUM_ITEM_AMOUNT,
          totalCurrentAmount: '0.00',
          difference: `-${MAXIMUM_ITEM_AMOUNT}`,
          currentAmountsByClass: [
            { class: 'fixed_income', label: 'Renda fixa', currentAmount: '0.00' },
            { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
            { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
          ],
        },
      },
    });
    expect(JSON.stringify(result)).toContain(MAXIMUM_ITEM_AMOUNT);
    expectNoOtherFinancialRequests();
  });

  // POST, PUT e DELETE respeitam verbos, rotas, corpos textuais e status de sucesso contratados.
  it('envia CRUD protegido sem converter valores monetários em números JSON', () => {
    startSession();
    const input: InvestmentInput = {
      name: 'Reserva sintética',
      class: 'fixed_income',
      appliedAmount: MAXIMUM_ITEM_AMOUNT,
      currentAmount: '0.01',
    };
    const results: unknown[] = [];

    api.createInvestment(input).subscribe((response) => results.push(response));
    const createRequest = backend.expectOne(INVESTMENTS_URL);
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toEqual(input);
    expect(typeof createRequest.request.body['appliedAmount']).toBe('string');
    createRequest.flush(
      investmentResponse({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '0.01' }),
      {
        status: 201,
        statusText: 'Created',
      },
    );

    api.updateInvestment(INVESTMENT_ID, input).subscribe((response) => results.push(response));
    const updateRequest = backend.expectOne(`${INVESTMENTS_URL}/${INVESTMENT_ID}`);
    expect(updateRequest.request.method).toBe('PUT');
    expect(updateRequest.request.body).toEqual(input);
    updateRequest.flush(
      investmentResponse({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '0.01' }),
      {
        status: 200,
        statusText: 'OK',
      },
    );

    api.deleteInvestment(INVESTMENT_ID).subscribe((response) => results.push(response));
    const deleteRequest = backend.expectOne(`${INVESTMENTS_URL}/${INVESTMENT_ID}`);
    expect(deleteRequest.request.method).toBe('DELETE');
    expect(deleteRequest.request.body).toBeNull();
    deleteRequest.flush(null, { status: 204, statusText: 'No Content' });

    expect(results).toHaveLength(3);
    expect((results[0] as { value: { appliedAmount: string } }).value.appliedAmount).toBe(
      MAXIMUM_ITEM_AMOUNT,
    );
    expect((results[1] as { value: { appliedAmount: string } }).value.appliedAmount).toBe(
      MAXIMUM_ITEM_AMOUNT,
    );
    expect((results[2] as { value: null }).value).toBeNull();
    expectNoOtherFinancialRequests();
  });

  // Tipos JSON numéricos, casas variáveis, zeros à esquerda e overflow violam o contrato canônico.
  it.each([1.25, '1.0', '01.00', '1e2', '1,00', '100000000000000000.00'])(
    'rejeita valor monetário HTTP não canônico (%s)',
    (invalidAmount) => {
      startSession();
      let receivedError: unknown;
      const payload = portfolioResponse(investmentResponse({ appliedAmount: invalidAmount }));

      api.getPortfolio().subscribe({ error: (error: unknown) => (receivedError = error) });
      backend.expectOne(INVESTMENTS_URL).flush(payload);

      expect(receivedError).toBeInstanceOf(InvestmentPortfolioPayloadError);
      expectNoOtherFinancialRequests();
    },
  );

  // Campos extras, classes repetidas e diferença negativa zero não são respostas canônicas.
  it.each([
    portfolioResponse(investmentResponse({ userId: 'must-not-escape' })),
    portfolioResponse(investmentResponse(), {
      summary: {
        totalAppliedAmount: '120.50',
        totalCurrentAmount: '125.75',
        difference: '-0.00',
        currentAmountsByClass: [
          { class: 'fixed_income', label: 'Renda fixa', currentAmount: '125.75' },
          { class: 'fixed_income', label: 'Duplicada', currentAmount: '0.00' },
          { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
        ],
      },
    }),
  ])('recusa campos não publicados ou classes inconsistentes', (payload) => {
    startSession();
    let receivedError: unknown;

    api.getPortfolio().subscribe({ error: (error: unknown) => (receivedError = error) });
    backend.expectOne(INVESTMENTS_URL).flush(payload);

    expect(receivedError).toBeInstanceOf(InvestmentPortfolioPayloadError);
    expectNoOtherFinancialRequests();
  });

  // Entrada local malformada e ID inválido falham antes de construir um request protegido.
  it('recusa valor JavaScript e identificador inválido antes de enviar HTTP', () => {
    startSession();
    let receivedError: unknown;
    const invalidInput = {
      name: 'Reserva sintética',
      class: 'fixed_income',
      appliedAmount: 1.25,
      currentAmount: '1.00',
    } as unknown as InvestmentInput;

    api
      .createInvestment(invalidInput)
      .subscribe({ error: (error: unknown) => (receivedError = error) });
    expect(receivedError).toBeInstanceOf(InvestmentPortfolioPayloadError);
    api.updateInvestment('../foreign', invalidInput).subscribe({
      error: (error: unknown) => (receivedError = error),
    });
    expect(receivedError).toBeInstanceOf(InvestmentPortfolioPayloadError);
    backend.expectNone(() => true);
  });

  // Sem sessão ou URL pública configurada, os serviços não recorrem a origem implícita.
  it('não envia requisição sem sessão ou sem configuração da API', () => {
    let receivedError: unknown;
    api.getPortfolio().subscribe({ error: (error: unknown) => (receivedError = error) });
    expect(receivedError).toBeInstanceOf(LocalSessionRequiredError);
    backend.expectNone(() => true);

    TestBed.resetTestingModule();
    configureApi(null);
    startSession();
    api.getPortfolio().subscribe({ error: (error: unknown) => (receivedError = error) });
    expect(receivedError).toBeInstanceOf(InvestmentPortfolioApiConfigurationError);
    backend.expectNone(() => true);
  });
});
