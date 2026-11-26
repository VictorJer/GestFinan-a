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
  CategoryOption,
  TransactionListApi,
  TransactionListApiConfigurationError,
  TransactionListPayloadError,
  TransactionListRequestError,
  TransactionPage,
  TransactionRecurrenceCandidate,
} from './transaction-list-api';

const API_BASE = 'https://api.example.test/proxy/';
const TRANSACTIONS_URL = `${API_BASE}api/transactions`;
const CATEGORIES_URL = `${API_BASE}api/categories`;
const TRANSACTION_ID = '5b76333c-6d8e-4d9e-aac6-3a6096f97ab1';
const CARD_ID = '2a990192-cb59-43eb-8520-cfef6a0e3e40';
const CATEGORY_ID = '6e209084-7488-4d97-930f-10f52df4b116';
const ANNOTATION_URL = `${TRANSACTIONS_URL}/${TRANSACTION_ID}/annotation`;
const RECURRENCE_CANDIDATES_URL = `${TRANSACTIONS_URL}/recurrence-candidates`;
const RECURRENCE_EVALUATE_URL = `${RECURRENCE_CANDIDATES_URL}/evaluate`;
const RECURRENCE_CANDIDATE_ID = 'f2ea634d-9f08-4bc0-9319-521b773844e3';
const SECOND_TRANSACTION_ID = '64965e38-e2e3-4b52-afd2-6bb1cfcf2450';
const THIRD_TRANSACTION_ID = '9a0cc19d-c360-4a93-8ba1-6f5353c8871d';

let backend: HttpTestingController;
let api: TransactionListApi;
let session: SessionService;

// Compõe cliente, interceptor e backend HTTP de teste sem alcançar API ou dados reais.
function configureApi(apiBaseUrl: string | null = API_BASE): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: apiBaseUrl },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  api = TestBed.inject(TransactionListApi);
  session = TestBed.inject(SessionService);
}

// Cria uma sessão sintética para verificar a captura da geração e a fronteira Bearer.
function startSession(): number {
  return session.startSession({
    accessToken: 'synthetic-transaction-list-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  })!;
}

// Localiza a rota sem query para permitir conferir cada parâmetro decodificado em separado.
function expectTransactionsRequest() {
  return backend.expectOne((request) => request.url === TRANSACTIONS_URL);
}

// Fornece um movimento contratualmente completo e permite variar campos para testes de allowlist.
function movement(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: TRANSACTION_ID,
    cardId: CARD_ID,
    description: 'Movimento sintético',
    amount: 17.25,
    currencyCode: 'BRL',
    originalDate: '2026-11-24T00:00:00Z',
    transactionDate: '2026-11-24',
    dateReason: null,
    nature: 'Unknown',
    natureReason: 'UNKNOWN_OPERATION_TYPE',
    situation: 'Pending',
    situationReason: null,
    installmentNumber: null,
    totalInstallments: null,
    categoryId: null,
    note: null,
    ...overrides,
  };
}

// Mantém a cobertura separada da contagem e permite páginas vazias sem inventar sucesso ou janela.
function coverage(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    lastSuccessfulImportAtUtc: null,
    lastSuccessfulWindow: null,
    latestExecution: null,
    ...overrides,
  };
}

// Cria um envelope paginado sintético com a mesma estrutura publicada pelo backend.
function pageResponse(
  items: readonly Record<string, unknown>[] = [movement()],
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    items,
    page: 1,
    pageSize: 25,
    filteredCount: items.length,
    coverage: coverage(),
    ...overrides,
  };
}

// Mantém a projeção sintética sem fatos financeiros e permite injetar campos que a allowlist deve ignorar.
function recurrenceCandidate(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    candidateId: RECURRENCE_CANDIDATE_ID,
    policyVersion: 'recurrence-v1',
    occurrenceTransactionIds: [TRANSACTION_ID, SECOND_TRANSACTION_ID, THIRD_TRANSACTION_ID],
    isCurrent: true,
    decision: null,
    decisionUpdatedAtUtc: null,
    ...overrides,
  };
}

// Cada caso deve permanecer somente em GET e não pode alcançar nenhuma rota de sincronização.
describe('Cliente da lista de movimentos', () => {
  beforeEach(() => configureApi());

  afterEach(() => {
    backend.expectNone((request) => request.url.toLowerCase().includes('/sync'));
    backend.verify();
  });

  // Os filtros combinados mantêm nomes/valores literais e a resposta fica vinculada à sessão iniciadora.
  it('consulta movimentos com filtros opcionais, paginação e allowlist da resposta', () => {
    const generation = startSession();
    const search = 'café 20%_\\literal+';
    const filters = {
      cardId: CARD_ID,
      categoryId: CATEGORY_ID,
      situation: 'FutureInstallment',
      from: '2026-01-01',
      toExclusive: '2026-02-01',
      search,
      page: 3,
      pageSize: 50,
    };
    let result: { generation: number; value: TransactionPage } | undefined;

    api.getTransactions(filters).subscribe((response) => (result = response));
    const request = expectTransactionsRequest();

    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-transaction-list-token',
    );
    expect(request.request.params.keys().sort()).toEqual(
      [
        'cardId',
        'categoryId',
        'situation',
        'from',
        'toExclusive',
        'search',
        'page',
        'pageSize',
      ].sort(),
    );
    for (const [name, value] of Object.entries(filters)) {
      expect(request.request.params.get(name)).toBe(String(value));
    }

    request.flush(
      pageResponse(
        [
          movement({
            externalTransactionId: 'must-not-escape',
            providerPayload: { secret: 'synthetic' },
          }),
        ],
        {
          page: 3,
          pageSize: 50,
          filteredCount: 127,
          coverage: coverage({
            lastSuccessfulImportAtUtc: '2026-11-24T10:00:00Z',
            lastSuccessfulWindow: {
              requestedFrom: '2026-01-01',
              requestedToExclusive: null,
              observedFrom: '2026-01-02',
              observedTo: null,
              coverageState: 'Partial',
            },
            latestExecution: {
              state: 'Failed',
              coverageState: 'Unknown',
              failureCode: 'PROVIDER_UNAVAILABLE',
            },
          }),
        },
      ),
    );

    expect(result?.generation).toBe(generation);
    expect(result?.value.page).toBe(3);
    expect(result?.value.pageSize).toBe(50);
    expect(result?.value.filteredCount).toBe(127);
    expect(result?.value.coverage.lastSuccessfulWindow?.coverageState).toBe('Partial');
    expect(result?.value.coverage.latestExecution?.state).toBe('Failed');
    expect(result?.value.items[0]).not.toHaveProperty('externalTransactionId');
    expect(JSON.stringify(result)).not.toContain('providerPayload');
  });

  // O catálogo mantém IDs internos e a opção virtual nula, descartando propriedades fora do contrato.
  it('consulta categorias próprias e preserva a opção virtual sem categoria', () => {
    const generation = startSession();
    let result: { generation: number; value: readonly CategoryOption[] } | undefined;

    api.getCategories().subscribe((response) => (result = response));
    const request = backend.expectOne(CATEGORIES_URL);
    expect(request.request.method).toBe('GET');
    request.flush([
      { id: CATEGORY_ID, name: 'Projetos', kind: 'personal', userId: 'must-not-escape' },
      { id: null, name: 'Sem categoria', kind: 'unassigned', scopeId: 'must-not-escape' },
    ]);

    expect(result).toEqual({
      generation,
      value: [
        { id: CATEGORY_ID, name: 'Projetos', kind: 'personal' },
        { id: null, name: 'Sem categoria', kind: 'unassigned' },
      ],
    });
  });

  // Somente o nome segue no POST; a categoria passa a existir no cliente após o 201 confirmado.
  it('cria categoria pessoal pelo POST estrito e projeta somente a resposta confirmada', () => {
    const generation = startSession();
    let result: unknown;

    api.createCategory('Projetos').subscribe((response) => (result = response));
    const request = backend.expectOne(CATEGORIES_URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-transaction-list-token',
    );
    expect(request.request.body).toEqual({ name: 'Projetos' });
    expect(Object.keys(request.request.body)).toEqual(['name']);
    request.flush(
      {
        id: CATEGORY_ID,
        name: 'Projetos',
        kind: 'personal',
        userId: 'must-not-escape',
        externalCategoryId: 'must-not-escape',
      },
      { status: 201, statusText: 'Created' },
    );

    expect(result).toEqual({
      generation,
      value: { id: CATEGORY_ID, name: 'Projetos', kind: 'personal' },
    });
    expect(JSON.stringify(result)).not.toContain('userId');
  });

  // PATCH envia apenas o campo editado, e null continua distinto de uma propriedade omitida.
  it('preserva omissão e null em PATCH parciais de categoria e observação', () => {
    const generation = startSession();
    const cases = [
      {
        patch: { categoryId: null },
        expectedBody: { categoryId: null },
        response: { categoryId: null, note: 'observação preservada' },
      },
      {
        patch: { note: null },
        expectedBody: { note: null },
        response: { categoryId: CATEGORY_ID, note: null },
      },
      {
        patch: { categoryId: CATEGORY_ID, note: 'texto simples' },
        expectedBody: { categoryId: CATEGORY_ID, note: 'texto simples' },
        response: { categoryId: CATEGORY_ID, note: 'texto simples' },
      },
    ];
    const results: unknown[] = [];

    for (const current of cases) {
      api.updateAnnotation(TRANSACTION_ID, current.patch).subscribe((value) => results.push(value));
      const request = backend.expectOne(ANNOTATION_URL);
      expect(request.request.method).toBe('PATCH');
      expect(request.request.headers.get('Authorization')).toBe(
        'Bearer synthetic-transaction-list-token',
      );
      expect(request.request.body).toEqual(current.expectedBody);
      expect(Object.keys(request.request.body).sort()).toEqual(
        Object.keys(current.expectedBody).sort(),
      );
      request.flush(
        {
          ...current.response,
          transactionId: 'must-not-escape',
          ownerId: 'must-not-escape',
        },
        { status: 200, statusText: 'OK' },
      );
    }

    expect(results).toEqual(cases.map((current) => ({ generation, value: current.response })));
    expect(JSON.stringify(results)).not.toContain('must-not-escape');
  });

  // GET permanece somente leitura; a avaliação exige POST explícito e projeta apenas IDs internos.
  it('consulta e avalia candidatos por operações separadas com resposta sanitizada', () => {
    const generation = startSession();
    let readResult: unknown;
    api.getRecurrenceCandidates().subscribe((response) => (readResult = response));
    const readRequest = backend.expectOne(RECURRENCE_CANDIDATES_URL);
    expect(readRequest.request.method).toBe('GET');
    expect(readRequest.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-transaction-list-token',
    );
    readRequest.flush([
      recurrenceCandidate({
        ownerId: 'must-not-escape',
        transactionDescription: 'private synthetic description',
      }),
    ]);

    expect(readResult).toEqual({
      generation,
      value: [
        {
          candidateId: RECURRENCE_CANDIDATE_ID,
          policyVersion: 'recurrence-v1',
          occurrenceTransactionIds: [TRANSACTION_ID, SECOND_TRANSACTION_ID, THIRD_TRANSACTION_ID],
          isCurrent: true,
          decision: null,
          decisionUpdatedAtUtc: null,
        } satisfies TransactionRecurrenceCandidate,
      ],
    });
    expect(JSON.stringify(readResult)).not.toContain('private synthetic description');

    let evaluationResult: unknown;
    api.evaluateRecurrenceCandidates().subscribe((response) => (evaluationResult = response));
    const evaluationRequest = backend.expectOne(RECURRENCE_EVALUATE_URL);
    expect(evaluationRequest.request.method).toBe('POST');
    expect(evaluationRequest.request.body).toBeNull();
    evaluationRequest.flush([recurrenceCandidate({ decision: 'IndependentPurchases' })], {
      status: 200,
      statusText: 'OK',
    });

    expect(evaluationResult).toEqual({
      generation,
      value: [
        expect.objectContaining({
          candidateId: RECURRENCE_CANDIDATE_ID,
          decision: 'IndependentPurchases',
        }),
      ],
    });
  });

  // PUT permite confirmar, rejeitar e corrigir sem reutilizar PATCH de anotação ou enviar fatos financeiros.
  it('registra e corrige decisão somente com corpo allowlisted e resposta confirmada', () => {
    const generation = startSession();
    const updatedAt = '2026-11-25T12:30:00Z';
    let firstResult: unknown;
    api
      .updateRecurrenceDecision(RECURRENCE_CANDIDATE_ID, 'Subscription')
      .subscribe((response) => (firstResult = response));
    const firstRequest = backend.expectOne(
      `${RECURRENCE_CANDIDATES_URL}/${RECURRENCE_CANDIDATE_ID}/decision`,
    );
    expect(firstRequest.request.method).toBe('PUT');
    expect(firstRequest.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-transaction-list-token',
    );
    expect(firstRequest.request.body).toEqual({ decision: 'Subscription' });
    expect(Object.keys(firstRequest.request.body)).toEqual(['decision']);
    firstRequest.flush(
      {
        candidateId: RECURRENCE_CANDIDATE_ID,
        decision: 'Subscription',
        decisionUpdatedAtUtc: updatedAt,
        ownerId: 'must-not-escape',
      },
      { status: 200, statusText: 'OK' },
    );
    expect(firstResult).toEqual({
      generation,
      value: {
        candidateId: RECURRENCE_CANDIDATE_ID,
        decision: 'Subscription',
        decisionUpdatedAtUtc: updatedAt,
      },
    });

    api.updateRecurrenceDecision(RECURRENCE_CANDIDATE_ID, 'Rejected').subscribe();
    const correctionRequest = backend.expectOne(
      `${RECURRENCE_CANDIDATES_URL}/${RECURRENCE_CANDIDATE_ID}/decision`,
    );
    expect(correctionRequest.request.method).toBe('PUT');
    expect(correctionRequest.request.body).toEqual({ decision: 'Rejected' });
    correctionRequest.flush(
      {
        candidateId: RECURRENCE_CANDIDATE_ID,
        decision: 'Rejected',
        decisionUpdatedAtUtc: updatedAt,
      },
      { status: 200, statusText: 'OK' },
    );
  });

  // IDs e decisões fora da allowlist falham antes do transporte, e trios incompletos falham fechados.
  it('recusa requisições inválidas e candidatos com ocorrências incompletas', () => {
    startSession();
    const errors: unknown[] = [];
    api.updateRecurrenceDecision('../candidate', 'Rejected').subscribe({
      error: (error: unknown) => errors.push(error),
    });
    api
      .updateRecurrenceDecision(RECURRENCE_CANDIDATE_ID, 'Unclassified' as never)
      .subscribe({ error: (error: unknown) => errors.push(error) });
    expect(errors).toHaveLength(2);
    expect(errors.every((error) => error instanceof TransactionListRequestError)).toBe(true);
    backend.expectNone((request) => request.url.startsWith(RECURRENCE_CANDIDATES_URL));

    let receivedError: unknown;
    api.getRecurrenceCandidates().subscribe({ error: (error: unknown) => (receivedError = error) });
    backend
      .expectOne(RECURRENCE_CANDIDATES_URL)
      .flush([
        recurrenceCandidate({ occurrenceTransactionIds: [TRANSACTION_ID, SECOND_TRANSACTION_ID] }),
      ]);
    expect(receivedError).toBeInstanceOf(TransactionListPayloadError);
  });

  // IDs/corpos inválidos ou propriedades extras são recusados antes de qualquer escrita HTTP.
  it('recusa PATCH vazio, ID inválido e campos externos sem enviar request', () => {
    startSession();
    const errors: unknown[] = [];

    api
      .updateAnnotation(TRANSACTION_ID, {})
      .subscribe({ error: (error: unknown) => errors.push(error) });
    api.updateAnnotation('../external', { note: 'texto' }).subscribe({
      error: (error: unknown) => errors.push(error),
    });
    api.updateAnnotation(TRANSACTION_ID, { note: 'texto', userId: 'external' } as never).subscribe({
      error: (error: unknown) => errors.push(error),
    });

    expect(errors).toHaveLength(3);
    expect(errors.every((error) => error instanceof TransactionListRequestError)).toBe(true);
    backend.expectNone((request) => request.url.includes('/annotation'));
  });

  // Um item incompleto falha fechado sem propagar detalhes ou valores brutos da resposta remota.
  it('rejeita DTO de movimento incompleto com mensagem sanitizada', () => {
    startSession();
    let receivedError: unknown;

    api.getTransactions({}).subscribe({ error: (error: unknown) => (receivedError = error) });
    expectTransactionsRequest().flush(
      pageResponse([movement({ amount: undefined, providerPayload: 'private-synthetic-value' })]),
    );

    expect(receivedError).toBeInstanceOf(TransactionListPayloadError);
    expect((receivedError as Error).message).not.toContain('private-synthetic-value');
  });

  // Uma página vazia não vira quantidade monetária nem altera a cobertura ausente.
  it('preserva página vazia, contagem zero e cobertura desconhecida', () => {
    const generation = startSession();
    let result: { generation: number; value: TransactionPage } | undefined;

    api.getTransactions({ page: 1, pageSize: 25 }).subscribe((response) => (result = response));
    const request = expectTransactionsRequest();
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    request.flush(pageResponse([], { filteredCount: 0 }));

    expect(result).toEqual({
      generation,
      value: {
        items: [],
        page: 1,
        pageSize: 25,
        filteredCount: 0,
        coverage: {
          lastSuccessfulImportAtUtc: null,
          lastSuccessfulWindow: null,
          latestExecution: null,
        },
      },
    });
  });

  // Filtro indefinido não é enviado, e a base nula impede qualquer fallback para origem relativa.
  it('envia somente filtros opcionais definidos', () => {
    startSession();
    api.getTransactions({ cardId: CARD_ID }).subscribe();
    const request = expectTransactionsRequest();

    expect(request.request.params.keys()).toEqual(['cardId']);
    request.flush(pageResponse());
  });

  // Campos adicionais ou tipo incompatível em respostas de escrita falham sem refletir payload remoto.
  it('rejeita respostas de escrita não reconhecidas sem expor os campos recebidos', () => {
    startSession();
    let receivedError: unknown;

    api
      .createCategory('Projetos')
      .subscribe({ error: (error: unknown) => (receivedError = error) });
    backend.expectOne(CATEGORIES_URL).flush({
      id: CATEGORY_ID,
      name: 'Projetos',
      kind: 'system',
      externalValue: 'synthetic-private-value',
    });

    expect(receivedError).toBeInstanceOf(TransactionListPayloadError);
    expect((receivedError as Error).message).not.toContain('synthetic-private-value');
  });

  // Só 201 confirma criação e só 200 confirma PATCH; outro sucesso HTTP não vira escrita reconhecida.
  it('rejeita status de sucesso incompatível com o contrato de categoria e anotação', () => {
    startSession();
    const errors: unknown[] = [];

    api.createCategory('Projetos').subscribe({ error: (error: unknown) => errors.push(error) });
    backend
      .expectOne(CATEGORIES_URL)
      .flush(
        { id: CATEGORY_ID, name: 'Projetos', kind: 'personal' },
        { status: 200, statusText: 'OK' },
      );
    api.updateAnnotation(TRANSACTION_ID, { note: 'texto' }).subscribe({
      error: (error: unknown) => errors.push(error),
    });
    backend
      .expectOne(ANNOTATION_URL)
      .flush({ categoryId: null, note: 'texto' }, { status: 201, statusText: 'Created' });

    expect(errors).toHaveLength(2);
    expect(errors.every((error) => error instanceof TransactionListPayloadError)).toBe(true);
  });

  // A matriz de escrita preserva códigos seguros para sessão, propriedade, conflito e indisponibilidade.
  it.each([
    { status: 400, statusText: 'Bad Request', operation: 'create', kind: 'request-failed' },
    { status: 401, statusText: 'Unauthorized', operation: 'create', kind: 'unauthorized' },
    { status: 403, statusText: 'Forbidden', operation: 'patch', kind: 'forbidden' },
    { status: 404, statusText: 'Not Found', operation: 'patch', kind: 'not-found' },
    { status: 409, statusText: 'Conflict', operation: 'create', kind: 'conflict' },
    { status: 429, statusText: 'Too Many Requests', operation: 'patch', kind: 'request-failed' },
    { status: 503, statusText: 'Service Unavailable', operation: 'create', kind: 'unavailable' },
  ])(
    'sanitiza falha HTTP $status em $operation sem refletir o corpo remoto',
    ({ status, statusText, operation, kind }) => {
      startSession();
      let receivedError: unknown;
      const isPatch = operation === 'patch';
      if (isPatch) {
        api.updateAnnotation(TRANSACTION_ID, { note: 'texto sintético' }).subscribe({
          error: (error: unknown) => (receivedError = error),
        });
      } else {
        api.createCategory('Projetos').subscribe({
          error: (error: unknown) => (receivedError = error),
        });
      }
      const request = backend.expectOne(isPatch ? ANNOTATION_URL : CATEGORIES_URL);

      request.flush(
        { detail: 'external-sensitive-message', traceId: 'synthetic-trace' },
        { status, statusText },
      );

      expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
      expect((receivedError as ProtectedApiRequestError).kind).toBe(kind);
      expect((receivedError as Error).message).not.toContain('external-sensitive-message');
    },
  );

  // Falha de transporte é distinguida de resposta HTTP sem repassar o evento bruto à interface.
  it('sanitiza falha de rede durante criação de categoria', () => {
    startSession();
    let receivedError: unknown;

    api
      .createCategory('Projetos')
      .subscribe({ error: (error: unknown) => (receivedError = error) });
    backend.expectOne(CATEGORIES_URL).error(new ProgressEvent('error'));

    expect(receivedError).toBeInstanceOf(ProtectedApiRequestError);
    expect((receivedError as ProtectedApiRequestError).kind).toBe('network');
    expect((receivedError as Error).message).toBe('Não foi possível conectar à API.');
  });
});

// A ausência de sessão ou de origem configurada falha antes de criar qualquer request financeiro.
describe('Pré-condições do cliente da lista', () => {
  beforeEach(() => configureApi());

  afterEach(() => backend.verify());

  // Lista, catálogo e recorrência recusam leituras/escritas sem uma geração autenticada em memória.
  it('recusa operações financeiras e de recorrência quando não existe sessão local', () => {
    const errors: unknown[] = [];
    api.getTransactions({}).subscribe({ error: (error: unknown) => errors.push(error) });
    api.getCategories().subscribe({ error: (error: unknown) => errors.push(error) });
    api.createCategory('Projetos').subscribe({ error: (error: unknown) => errors.push(error) });
    api
      .updateAnnotation(TRANSACTION_ID, { note: 'texto' })
      .subscribe({ error: (error: unknown) => errors.push(error) });
    api.getRecurrenceCandidates().subscribe({ error: (error: unknown) => errors.push(error) });
    api.evaluateRecurrenceCandidates().subscribe({ error: (error: unknown) => errors.push(error) });
    api.updateRecurrenceDecision(RECURRENCE_CANDIDATE_ID, 'Rejected').subscribe({
      error: (error: unknown) => errors.push(error),
    });

    expect(errors).toHaveLength(7);
    expect(errors.every((error) => error instanceof LocalSessionRequiredError)).toBe(true);
    backend.expectNone(() => true);
  });
});

// A falta de base é configurada antes de instanciar os serviços, evitando reconfiguração do TestBed.
describe('Cliente da lista sem URL configurada', () => {
  beforeEach(() => configureApi(null));

  afterEach(() => backend.verify());

  // A URL não é inferida do navegador quando a configuração pública está ausente.
  it('recusa consulta com sessão ativa quando a URL da API não está configurada', () => {
    startSession();
    let receivedError: unknown;

    api.getTransactions({}).subscribe({ error: (error: unknown) => (receivedError = error) });

    expect(receivedError).toBeInstanceOf(TransactionListApiConfigurationError);
    backend.expectNone(() => true);
  });
});
