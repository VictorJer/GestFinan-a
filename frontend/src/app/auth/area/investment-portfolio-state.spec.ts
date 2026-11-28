import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable, Subscription } from 'rxjs';
import { vi } from 'vitest';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor, ProtectedApiRequestError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { InvestmentPortfolio, InvestmentPortfolioApi } from './investment-portfolio-api';
import {
  InvestmentPortfolioMutationInProgressError,
  InvestmentPortfolioStateService,
} from './investment-portfolio-state';
import { SessionScopedResponse } from './card-connection-api';

const API_BASE = 'https://api.example.test/proxy/';
const INVESTMENTS_URL = `${API_BASE}api/investments`;
const INVESTMENT_ID = '87b65749-c927-431f-8d51-35fdf3106336';
const MAXIMUM_ITEM_AMOUNT = '99999999999999999.99';

let backend: HttpTestingController;
let state: InvestmentPortfolioStateService;
let session: SessionService;

// Configura o estado real e a API protegida sobre transporte HTTP sintético, sem dados externos.
function configureState(): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  state = TestBed.inject(InvestmentPortfolioStateService);
  session = TestBed.inject(SessionService);
}

// Instala uma sessão artificial cuja geração pode ser substituída ou expirada pelo teste.
function startSession(expiresAtUtc = new Date(Date.now() + 30 * 60 * 1000).toISOString()): number {
  return session.startSession({
    accessToken: 'synthetic-investment-state-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc,
  })!;
}

// Mantém a resposta sintética limitada aos campos do contrato e aos valores monetários como texto.
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

// Lista e resumo são controlados juntos, sem recompor qualquer total no teste do estado.
function portfolioResponse(
  items: readonly Record<string, unknown>[] = [investmentResponse()],
  summaryOverrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    items,
    summary: {
      totalAppliedAmount: items.length === 0 ? '0.00' : '120.50',
      totalCurrentAmount: items.length === 0 ? '0.00' : '125.75',
      difference: items.length === 0 ? '0.00' : '5.25',
      currentAmountsByClass: [
        {
          class: 'fixed_income',
          label: 'Renda fixa',
          currentAmount: items.length === 0 ? '0.00' : '125.75',
        },
        { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
      ...summaryOverrides,
    },
  };
}

// Evita regressão para sincronização ou leitura de cartão ao entrar na carteira.
function expectNoOtherFinancialRequests(): void {
  backend.expectNone((request) => !request.url.startsWith(INVESTMENTS_URL));
}

// O estado preserva precisão, protege gerações e coordena releituras após CRUD confirmado.
describe('Estado da carteira manual', () => {
  beforeEach(() => configureState());

  afterEach(() => {
    expectNoOtherFinancialRequests();
    backend.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  // A leitura usa somente GET de carteira e mantém valores máximos como strings até a view.
  it('publica lista e resumo de uma leitura protegida sem sincronizar cartões', () => {
    const generation = startSession();
    state.load();

    expect(state.view().status).toBe('loading');
    const request = backend.expectOne(INVESTMENTS_URL);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-investment-state-token',
    );
    request.flush(
      portfolioResponse(
        [investmentResponse({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '0.00' })],
        {
          totalAppliedAmount: MAXIMUM_ITEM_AMOUNT,
          totalCurrentAmount: '0.00',
          difference: `-${MAXIMUM_ITEM_AMOUNT}`,
          currentAmountsByClass: [
            { class: 'fixed_income', label: 'Renda fixa', currentAmount: '0.00' },
            { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
            { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
          ],
        },
      ),
    );

    expect(state.view().status).toBe('loaded');
    expect(state.view().generation).toBe(generation);
    expect(state.view().snapshot?.items[0].appliedAmount).toBe(MAXIMUM_ITEM_AMOUNT);
    expect(state.view().snapshot?.summary.difference).toBe(`-${MAXIMUM_ITEM_AMOUNT}`);
    expect(typeof state.view().snapshot?.items[0].appliedAmount).toBe('string');
  });

  // Uma carteira vazia é sucesso do domínio e seus zeros continuam strings de duas casas.
  it('representa carteira vazia como estado válido sem preencher posições artificiais', () => {
    startSession();
    state.load();
    backend.expectOne(INVESTMENTS_URL).flush(portfolioResponse([]));

    expect(state.view().status).toBe('empty');
    expect(state.view().snapshot?.items).toEqual([]);
    expect(state.view().snapshot?.summary.totalAppliedAmount).toBe('0.00');
    expect(state.view().snapshot?.summary.totalCurrentAmount).toBe('0.00');
  });

  // Cada mutação espera o status de sucesso e dispara um GET novo antes de atualizar o snapshot.
  it('relê carteira após criação, edição e exclusão confirmadas', () => {
    startSession();
    const input = {
      name: 'Reserva sintética',
      class: 'fixed_income' as const,
      appliedAmount: MAXIMUM_ITEM_AMOUNT,
      currentAmount: '0.01',
    };
    let created: unknown;

    state.createInvestment(input).subscribe((item) => (created = item));
    expect(state.view().mutationStatus).toBe('saving');
    const createRequest = backend.expectOne(INVESTMENTS_URL);
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toEqual(input);
    expect(typeof createRequest.request.body['appliedAmount']).toBe('string');
    createRequest.flush(
      investmentResponse({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '0.01' }),
      { status: 201, statusText: 'Created' },
    );
    expect(created).toMatchObject({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '0.01' });

    const afterCreate = backend.expectOne(INVESTMENTS_URL);
    expect(afterCreate.request.method).toBe('GET');
    afterCreate.flush(
      portfolioResponse([
        investmentResponse({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '0.01' }),
      ]),
    );
    expect(state.view().snapshot?.items[0].currentAmount).toBe('0.01');

    let updated: unknown;
    state
      .updateInvestment(INVESTMENT_ID, { ...input, currentAmount: '50.00' })
      .subscribe((item) => (updated = item));
    const updateRequest = backend.expectOne(`${INVESTMENTS_URL}/${INVESTMENT_ID}`);
    expect(updateRequest.request.method).toBe('PUT');
    updateRequest.flush(
      investmentResponse({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '50.00' }),
      { status: 200, statusText: 'OK' },
    );
    expect(updated).toMatchObject({ currentAmount: '50.00' });

    const afterUpdate = backend.expectOne(INVESTMENTS_URL);
    afterUpdate.flush(
      portfolioResponse([
        investmentResponse({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '50.00' }),
      ]),
    );
    expect(state.view().snapshot?.items[0].currentAmount).toBe('50.00');

    let deleted: unknown = 'not-completed';
    state.deleteInvestment(INVESTMENT_ID).subscribe((item) => (deleted = item));
    const deleteRequest = backend.expectOne(`${INVESTMENTS_URL}/${INVESTMENT_ID}`);
    expect(deleteRequest.request.method).toBe('DELETE');
    deleteRequest.flush(null, { status: 204, statusText: 'No Content' });
    expect(deleted).toBeNull();

    const afterDelete = backend.expectOne(INVESTMENTS_URL);
    expect(afterDelete.request.method).toBe('GET');
    afterDelete.flush(portfolioResponse([]));
    expect(state.view().status).toBe('empty');
    expect(state.view().snapshot?.items).toEqual([]);
    expect(state.view().mutationStatus).toBe('idle');
  });

  // Falha de rede vira categoria segura e conserva apenas dados confirmados da mesma sessão.
  it('classifica falha de leitura sem armazenar corpo de erro e permite retry', () => {
    startSession();
    state.load();
    backend
      .expectOne(INVESTMENTS_URL)
      .flush(
        { detail: 'synthetic-private-error-body' },
        { status: 503, statusText: 'Service Unavailable' },
      );

    expect(state.view().status).toBe('error');
    expect(state.view().failure).toBe('unavailable');
    expect(JSON.stringify(state.view())).not.toContain('synthetic-private-error-body');

    state.retry();
    backend.expectOne(INVESTMENTS_URL).flush(portfolioResponse());
    expect(state.view().status).toBe('loaded');
    expect(state.view().failure).toBeNull();
  });

  // Erro de mutação não dispara uma releitura que poderia mascarar ausência de confirmação HTTP.
  it('não relê após falha de gravação e conserva somente a categoria segura', () => {
    startSession();
    let receivedError: unknown;
    state
      .createInvestment({
        name: 'Reserva sintética',
        class: 'fixed_income',
        appliedAmount: '1.00',
        currentAmount: '1.00',
      })
      .subscribe({ error: (error: unknown) => (receivedError = error) });
    backend
      .expectOne(INVESTMENTS_URL)
      .flush(
        { detail: 'synthetic-private-mutation-error' },
        { status: 400, statusText: 'Bad Request' },
      );

    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect(state.view().mutationStatus).toBe('error');
    expect(state.view().mutationFailure).toBe('request-failed');
    expect(state.view().snapshot).toBeNull();
    backend.expectNone((request) => request.method === 'GET');
    expect(JSON.stringify(state.view())).not.toContain('synthetic-private-mutation-error');
  });

  // Logout cancela a leitura em curso e a view não expõe snapshot privado após encerrar a sessão.
  it('cancela e limpa dados no logout', () => {
    const generation = startSession();
    state.load();
    const pending = backend.expectOne(INVESTMENTS_URL);

    session.endSessionForGeneration(generation);
    TestBed.tick();

    expect(pending.cancelled).toBe(true);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });

  // Expiração local aplica a mesma limpeza do logout, inclusive enquanto há uma leitura pendente.
  it('cancela e redige snapshot quando a sessão expira', () => {
    vi.useFakeTimers();
    const startedAt = Date.now();
    startSession(new Date(startedAt + 1000).toISOString());
    state.load();
    backend.expectOne(INVESTMENTS_URL).flush(portfolioResponse());
    state.retry();
    const pending = backend.expectOne(INVESTMENTS_URL);
    vi.setSystemTime(startedAt + 1001);

    expect(session.currentSession()).toBeNull();
    TestBed.tick();

    expect(pending.cancelled).toBe(true);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });

  // Uma geração nova nunca recebe a posição mantida pela sessão que acabou de ser substituída.
  it('descarta snapshot confirmado ao iniciar outra sessão', () => {
    const previousGeneration = startSession();
    state.load();
    backend.expectOne(INVESTMENTS_URL).flush(portfolioResponse());
    const currentGeneration = startSession();
    TestBed.tick();

    expect(currentGeneration).not.toBe(previousGeneration);
    expect(state.view().generation).toBe(currentGeneration);
    expect(state.view().status).toBe('idle');
    expect(state.view().snapshot).toBeNull();
  });

  // Uma mutação pendente termina sem GET subsequente se a geração que a iniciou fizer logout.
  it('cancela gravação pendente no logout sem publicar sucesso ou reler', () => {
    const generation = startSession();
    let completed = false;
    state
      .createInvestment({
        name: 'Reserva sintética',
        class: 'fixed_income',
        appliedAmount: '1.00',
        currentAmount: '1.00',
      })
      .subscribe({ complete: () => (completed = true) });
    const pending = backend.expectOne(INVESTMENTS_URL);

    session.endSessionForGeneration(generation);
    TestBed.tick();

    expect(pending.cancelled).toBe(true);
    expect(completed).toBe(true);
    expect(state.view().status).toBe('idle');
    expect(state.view().mutationStatus).toBe('idle');
    backend.expectNone((request) => request.method === 'GET');
  });

  // Sair da rota cancela apenas a leitura, mantendo o bloqueio enquanto a gravação confirmável segue ativa.
  it('preserva o estado salvando ao sair da página e bloqueia uma segunda mutação', () => {
    startSession();
    state
      .createInvestment({
        name: 'Reserva sintética',
        class: 'fixed_income',
        appliedAmount: '1.00',
        currentAmount: '1.00',
      })
      .subscribe();
    const createRequest = backend.expectOne(INVESTMENTS_URL);
    expect(createRequest.request.method).toBe('POST');

    state.cancelPendingRead();

    expect(state.view().mutationStatus).toBe('saving');
    let concurrentError: unknown;
    state
      .createInvestment({
        name: 'Outra posição',
        class: 'fixed_income',
        appliedAmount: '2.00',
        currentAmount: '2.00',
      })
      .subscribe({ error: (error: unknown) => (concurrentError = error) });
    expect(concurrentError).toBeInstanceOf(InvestmentPortfolioMutationInProgressError);

    createRequest.flush(investmentResponse(), { status: 201, statusText: 'Created' });
    const refreshRequest = backend.expectOne(INVESTMENTS_URL);
    expect(refreshRequest.request.method).toBe('GET');
    refreshRequest.flush(portfolioResponse());
    expect(state.view().status).toBe('loaded');
    expect(state.view().mutationStatus).toBe('idle');
  });
});

// Um adapter que não cancela callbacks permite testar a proteção por generation/request ID.
describe('Respostas tardias da carteira', () => {
  // Uma segunda leitura da mesma geração invalida a primeira pelo identificador local da requisição.
  it('ignora resposta anterior quando uma leitura mais recente da mesma sessão já foi aceita', () => {
    const observers: Array<{
      next?: (response: SessionScopedResponse<InvestmentPortfolio>) => void;
    }> = [];
    const apiStub = {
      getPortfolio: () =>
        ({
          subscribe: (observer: {
            next?: (response: SessionScopedResponse<InvestmentPortfolio>) => void;
          }) => {
            observers.push(observer);
            return new Subscription();
          },
        }) as unknown as Observable<SessionScopedResponse<InvestmentPortfolio>>,
    };
    TestBed.configureTestingModule({
      providers: [{ provide: InvestmentPortfolioApi, useValue: apiStub }],
    });
    state = TestBed.inject(InvestmentPortfolioStateService);
    session = TestBed.inject(SessionService);
    const generation = startSession();

    state.load();
    state.load();
    observers[1].next?.({
      generation,
      value: {
        items: [
          {
            id: INVESTMENT_ID,
            name: 'Leitura mais recente',
            class: 'fixed_income',
            appliedAmount: '20.00',
            currentAmount: '20.00',
          },
        ],
        summary: {
          totalAppliedAmount: '20.00',
          totalCurrentAmount: '20.00',
          difference: '0.00',
          currentAmountsByClass: [
            { class: 'fixed_income', label: 'Renda fixa', currentAmount: '20.00' },
            { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
            { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
          ],
        },
      },
    });
    observers[0].next?.({
      generation,
      value: {
        items: [
          {
            id: INVESTMENT_ID,
            name: 'Resposta tardia',
            class: 'fixed_income',
            appliedAmount: '900.00',
            currentAmount: '900.00',
          },
        ],
        summary: {
          totalAppliedAmount: '900.00',
          totalCurrentAmount: '900.00',
          difference: '0.00',
          currentAmountsByClass: [
            { class: 'fixed_income', label: 'Renda fixa', currentAmount: '900.00' },
            { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
            { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
          ],
        },
      },
    });

    expect(state.view().status).toBe('loaded');
    expect(state.view().snapshot?.items[0].name).toBe('Leitura mais recente');
  });

  it('ignora resposta antiga mesmo se chegar depois da resposta da nova geração', () => {
    const observers: Array<{
      next?: (response: SessionScopedResponse<InvestmentPortfolio>) => void;
    }> = [];
    const apiStub = {
      getPortfolio: () =>
        ({
          subscribe: (observer: {
            next?: (response: SessionScopedResponse<InvestmentPortfolio>) => void;
          }) => {
            observers.push(observer);
            return new Subscription();
          },
        }) as unknown as Observable<SessionScopedResponse<InvestmentPortfolio>>,
    };
    TestBed.configureTestingModule({
      providers: [{ provide: InvestmentPortfolioApi, useValue: apiStub }],
    });
    state = TestBed.inject(InvestmentPortfolioStateService);
    session = TestBed.inject(SessionService);
    const oldGeneration = startSession();

    state.load();
    const currentGeneration = startSession();
    state.load();
    observers[1].next?.({
      generation: currentGeneration,
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
    observers[0].next?.({
      generation: oldGeneration,
      value: {
        items: [
          {
            id: INVESTMENT_ID,
            name: 'Posição da sessão anterior',
            class: 'fixed_income',
            appliedAmount: '900.00',
            currentAmount: '900.00',
          },
        ],
        summary: {
          totalAppliedAmount: '900.00',
          totalCurrentAmount: '900.00',
          difference: '0.00',
          currentAmountsByClass: [
            { class: 'fixed_income', label: 'Renda fixa', currentAmount: '900.00' },
            { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
            { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
          ],
        },
      },
    });

    expect(state.view().generation).toBe(currentGeneration);
    expect(state.view().status).toBe('empty');
    expect(state.view().snapshot?.items).toEqual([]);
  });
});
