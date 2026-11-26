import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { defer, map, Observable, throwError } from 'rxjs';
import { NORMALIZED_API_BASE_URL, NormalizedApiBaseUrl } from '../../api-base-url.token';
import { LocalSessionRequiredError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { SessionScopedResponse } from './card-connection-api';

/** Reúne apenas os filtros e a paginação aceitos por GET /api/transactions. */
export interface TransactionListFilters {
  readonly cardId?: string;
  readonly categoryId?: string;
  readonly situation?: string;
  readonly from?: string;
  readonly toExclusive?: string;
  readonly search?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

/** Representa os campos próprios de cada movimento, sem valores derivados ou payload externo. */
export interface TransactionMovement {
  readonly id: string;
  readonly cardId: string;
  readonly description: string;
  readonly amount: number;
  readonly currencyCode: string;
  readonly originalDate: string;
  readonly transactionDate: string | null;
  readonly dateReason: string | null;
  readonly nature: string;
  readonly natureReason: string | null;
  readonly situation: string;
  readonly situationReason: string | null;
  readonly installmentNumber: number | null;
  readonly totalInstallments: number | null;
  readonly categoryId: string | null;
  readonly note: string | null;
}

/** Descreve exclusivamente a janela confirmada associada ao último sucesso correlato. */
export interface TransactionLastSuccessfulWindow {
  readonly requestedFrom: string;
  readonly requestedToExclusive: string | null;
  readonly observedFrom: string | null;
  readonly observedTo: string | null;
  readonly coverageState: string;
}

/** Preserva a execução recente separada do sucesso e da janela de cobertura. */
export interface TransactionLatestExecution {
  readonly state: string;
  readonly coverageState: string;
  readonly failureCode: string | null;
}

/** Mantém contagem, sucesso, janela confirmada e execução recente em campos distintos. */
export interface TransactionCoverage {
  readonly lastSuccessfulImportAtUtc: string | null;
  readonly lastSuccessfulWindow: TransactionLastSuccessfulWindow | null;
  readonly latestExecution: TransactionLatestExecution | null;
}

/** Contém a página atual e a contagem do conjunto inteiro filtrado pela API. */
export interface TransactionPage {
  readonly items: readonly TransactionMovement[];
  readonly page: number;
  readonly pageSize: number;
  readonly filteredCount: number;
  readonly coverage: TransactionCoverage;
}

/** Limita a origem visual da categoria aos três tipos publicados pelo catálogo. */
export type CategoryKind = 'system' | 'personal' | 'unassigned';

/** Opção allowlisted do catálogo, incluindo a representação virtual pelo ID nulo. */
export interface CategoryOption {
  readonly id: string | null;
  readonly name: string;
  readonly kind: CategoryKind;
}

/** PATCH parcial preserva a diferença entre propriedade omitida e valor nulo explícito. */
export interface TransactionAnnotationPatch {
  readonly categoryId?: string | null;
  readonly note?: string | null;
}

/** Resposta confirmada da anotação pessoal, sem dados do movimento ou da origem externa. */
export interface TransactionAnnotation {
  readonly categoryId: string | null;
  readonly note: string | null;
}

/** Limita a decisão do titular às quatro interpretações aceitas pelo contrato protegido. */
export type TransactionRecurrenceDecision =
  'Subscription' | 'Installment' | 'IndependentPurchases' | 'Rejected';

/** Projeta somente a referência interna e o estado da sugestão, sem fatos financeiros ou da origem. */
export interface TransactionRecurrenceCandidate {
  readonly candidateId: string;
  readonly policyVersion: string;
  readonly occurrenceTransactionIds: readonly string[];
  readonly isCurrent: boolean;
  readonly decision: TransactionRecurrenceDecision | null;
  readonly decisionUpdatedAtUtc: string | null;
}

/** Resposta confirmada de decisão/correção, sem repetir movimentos ou dados do titular. */
export interface TransactionRecurrenceDecisionResponse {
  readonly candidateId: string;
  readonly decision: TransactionRecurrenceDecision;
  readonly decisionUpdatedAtUtc: string;
}

// Erro fixo para resposta incompatível; o corpo remoto nunca é incluído na mensagem.
export class TransactionListPayloadError extends Error {
  constructor() {
    super('A API retornou dados de movimentos em formato não reconhecido.');
    this.name = 'TransactionListPayloadError';
  }
}

// Erro local seguro para impedir corpos de escrita vazios, inválidos ou com campos não permitidos.
export class TransactionListRequestError extends Error {
  constructor() {
    super('Os campos enviados para a organização do movimento não seguem o contrato esperado.');
    this.name = 'TransactionListRequestError';
  }
}

// Falha fechada quando o build não fornece uma origem pública/configurada para a API.
export class TransactionListApiConfigurationError extends Error {
  constructor() {
    super('A consulta está indisponível porque a API não está configurada neste ambiente.');
    this.name = 'TransactionListApiConfigurationError';
  }
}

// Centraliza contratos allowlist da lista, anotações e recorrência sem reutilizar rotas de sincronização.
@Injectable({ providedIn: 'root' })
export class TransactionListApi {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(NORMALIZED_API_BASE_URL);
  private readonly session = inject(SessionService);

  // Lê uma página e captura a geração que iniciou a consulta para descartar respostas de sessões antigas.
  getTransactions(
    filters: TransactionListFilters,
  ): Observable<SessionScopedResponse<TransactionPage>> {
    return this.get('transactions', parseTransactionPage, createQueryParams(filters));
  }

  // Lê categorias próprias e a opção virtual sem categoria, sem consultar dados da conexão financeira.
  getCategories(): Observable<SessionScopedResponse<readonly CategoryOption[]>> {
    return this.get('categories', parseCategoryOptions);
  }

  // Cria somente pelo nome; tipo e titularidade permanecem determinados pelo backend e pela sessão.
  createCategory(name: string): Observable<SessionScopedResponse<CategoryOption>> {
    if (typeof name !== 'string') {
      return throwError(() => new TransactionListRequestError());
    }

    return this.write('POST', 'categories', { name }, parseCreatedCategory, 201);
  }

  // Atualiza somente os campos próprios presentes no PATCH, preservando null como limpeza explícita.
  updateAnnotation(
    transactionId: string,
    patch: TransactionAnnotationPatch,
  ): Observable<SessionScopedResponse<TransactionAnnotation>> {
    if (!isNonEmptyGuid(transactionId)) {
      return throwError(() => new TransactionListRequestError());
    }

    let body: TransactionAnnotationPatch;
    try {
      body = projectAnnotationPatch(patch);
    } catch {
      return throwError(() => new TransactionListRequestError());
    }

    return this.write(
      'PATCH',
      `transactions/${transactionId}/annotation`,
      body,
      parseAnnotationResponse,
      200,
    );
  }

  // Consulta somente a projeção própria; GET não reavalia histórico nem dispara sincronização.
  getRecurrenceCandidates(): Observable<
    SessionScopedResponse<readonly TransactionRecurrenceCandidate[]>
  > {
    return this.get('transactions/recurrence-candidates', parseRecurrenceCandidates);
  }

  // Reavaliação é uma ação explícita e recebe a lista persistida atualizada pelo backend.
  evaluateRecurrenceCandidates(): Observable<
    SessionScopedResponse<readonly TransactionRecurrenceCandidate[]>
  > {
    return this.write(
      'POST',
      'transactions/recurrence-candidates/evaluate',
      null,
      parseRecurrenceCandidates,
      200,
    );
  }

  // A decisão usa PUT dedicado e aceita somente os quatro valores publicados pelo backend.
  updateRecurrenceDecision(
    candidateId: string,
    decision: TransactionRecurrenceDecision,
  ): Observable<SessionScopedResponse<TransactionRecurrenceDecisionResponse>> {
    if (!isNonEmptyGuid(candidateId) || !isRecurrenceDecision(decision)) {
      return throwError(() => new TransactionListRequestError());
    }

    return this.write(
      'PUT',
      `transactions/recurrence-candidates/${candidateId}/decision`,
      { decision },
      parseRecurrenceDecisionResponse,
      200,
    );
  }

  // Exige sessão local e base explícita antes de emitir GET; o interceptor anexa o Bearer permitido.
  private get<T>(
    route: string,
    decode: (payload: unknown) => T,
    params = new HttpParams(),
  ): Observable<SessionScopedResponse<T>> {
    return defer(() => {
      const activeSession = this.session.currentSession();
      if (activeSession === null) {
        return throwError(() => new LocalSessionRequiredError());
      }

      if (this.apiBase === null) {
        return throwError(() => new TransactionListApiConfigurationError());
      }

      const url = buildApiUrl(this.apiBase, route);
      return this.http.get<unknown>(url, { params }).pipe(
        map((payload) => ({
          generation: activeSession.generation,
          value: decode(payload),
        })),
      );
    });
  }

  // Exige o status explícito da rota e vincula a resposta allowlist à geração que iniciou a escrita.
  private write<T>(
    method: 'POST' | 'PATCH' | 'PUT',
    route: string,
    body: unknown,
    decode: (payload: unknown) => T,
    expectedStatus: number,
  ): Observable<SessionScopedResponse<T>> {
    return defer(() => {
      const activeSession = this.session.currentSession();
      if (activeSession === null) {
        return throwError(() => new LocalSessionRequiredError());
      }

      if (this.apiBase === null) {
        return throwError(() => new TransactionListApiConfigurationError());
      }

      const url = buildApiUrl(this.apiBase, route);
      const response = this.sendWrite(method, url, body);
      return response.pipe(
        map((result) => {
          if (result.status !== expectedStatus) {
            throw new TransactionListPayloadError();
          }

          return {
            generation: activeSession.generation,
            value: decode(result.body),
          };
        }),
      );
    });
  }

  // Mantém a escolha do verbo explícita para cada rota protegida sem ampliar o corpo permitido.
  private sendWrite(method: 'POST' | 'PATCH' | 'PUT', url: string, body: unknown) {
    switch (method) {
      case 'POST':
        return this.http.post<unknown>(url, body, { observe: 'response' });
      case 'PATCH':
        return this.http.patch<unknown>(url, body, { observe: 'response' });
      case 'PUT':
        return this.http.put<unknown>(url, body, { observe: 'response' });
    }
  }
}

// Serializa somente os filtros definidos e deixa a codificação literal a cargo do HttpParams do Angular.
function createQueryParams(filters: TransactionListFilters): HttpParams {
  let params = new HttpParams();
  for (const [name, value] of Object.entries(filters)) {
    if (value !== undefined) {
      params = params.set(name, String(value));
    }
  }

  return params;
}

// Usa a URL normalizada do build e preserva eventual prefixo de proxy sem fallback de origem.
function buildApiUrl(apiBase: NormalizedApiBaseUrl, route: string): string {
  const apiPath = apiBase.pathPrefix.toLowerCase().endsWith('/api')
    ? apiBase.pathPrefix
    : `${apiBase.pathPrefix}/api`;
  return new URL(`${apiPath}/${route}`, apiBase.origin).toString();
}

// Aceita objetos JSON simples e impede que arrays/null sejam interpretados como DTOs.
function asRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null;
  }

  return value as Record<string, unknown>;
}

// Projeta somente os campos públicos do movimento e preserva os metadados opcionais como null.
function parseTransactionMovement(payload: unknown): TransactionMovement {
  const movement = asRecord(payload);
  if (
    movement === null ||
    !isNonEmptyGuid(movement['id']) ||
    !isNonEmptyGuid(movement['cardId']) ||
    typeof movement['description'] !== 'string' ||
    !isFiniteNumber(movement['amount']) ||
    typeof movement['currencyCode'] !== 'string' ||
    typeof movement['originalDate'] !== 'string' ||
    !isNullableString(movement['transactionDate']) ||
    !isNullableString(movement['dateReason']) ||
    typeof movement['nature'] !== 'string' ||
    !isNullableString(movement['natureReason']) ||
    typeof movement['situation'] !== 'string' ||
    !isNullableString(movement['situationReason']) ||
    !isNullableInteger(movement['installmentNumber']) ||
    !isNullableInteger(movement['totalInstallments']) ||
    !isNullableGuid(movement['categoryId']) ||
    !isNullableString(movement['note'])
  ) {
    throw new TransactionListPayloadError();
  }

  return {
    id: movement['id'],
    cardId: movement['cardId'],
    description: movement['description'],
    amount: movement['amount'],
    currencyCode: movement['currencyCode'],
    originalDate: movement['originalDate'],
    transactionDate: movement['transactionDate'],
    dateReason: movement['dateReason'],
    nature: movement['nature'],
    natureReason: movement['natureReason'],
    situation: movement['situation'],
    situationReason: movement['situationReason'],
    installmentNumber: movement['installmentNumber'],
    totalInstallments: movement['totalInstallments'],
    categoryId: movement['categoryId'],
    note: movement['note'],
  };
}

// Valida envelope, paginação e cobertura sem recalcular contagens ou completar dados ausentes.
function parseTransactionPage(payload: unknown): TransactionPage {
  const page = asRecord(payload);
  if (
    page === null ||
    !Array.isArray(page['items']) ||
    !isPositiveInteger(page['page']) ||
    !isPageSize(page['pageSize']) ||
    !isNonNegativeInteger(page['filteredCount'])
  ) {
    throw new TransactionListPayloadError();
  }

  return {
    items: page['items'].map(parseTransactionMovement),
    page: page['page'],
    pageSize: page['pageSize'],
    filteredCount: page['filteredCount'],
    coverage: parseTransactionCoverage(page['coverage']),
  };
}

// Mantém sucesso, janela confirmada e execução recente como fontes independentes de cobertura.
function parseTransactionCoverage(payload: unknown): TransactionCoverage {
  const coverage = asRecord(payload);
  if (coverage === null || !isNullableString(coverage['lastSuccessfulImportAtUtc'])) {
    throw new TransactionListPayloadError();
  }

  const lastSuccessfulWindow = coverage['lastSuccessfulWindow'];
  const latestExecution = coverage['latestExecution'];

  return {
    lastSuccessfulImportAtUtc: coverage['lastSuccessfulImportAtUtc'],
    lastSuccessfulWindow:
      lastSuccessfulWindow === null ? null : parseLastSuccessfulWindow(lastSuccessfulWindow),
    latestExecution: latestExecution === null ? null : parseLatestExecution(latestExecution),
  };
}

// Rejeita janelas parciais no shape, mas preserva literalmente o estado de cobertura retornado.
function parseLastSuccessfulWindow(payload: unknown): TransactionLastSuccessfulWindow {
  const window = asRecord(payload);
  if (
    window === null ||
    typeof window['requestedFrom'] !== 'string' ||
    !isNullableString(window['requestedToExclusive']) ||
    !isNullableString(window['observedFrom']) ||
    !isNullableString(window['observedTo']) ||
    typeof window['coverageState'] !== 'string'
  ) {
    throw new TransactionListPayloadError();
  }

  return {
    requestedFrom: window['requestedFrom'],
    requestedToExclusive: window['requestedToExclusive'],
    observedFrom: window['observedFrom'],
    observedTo: window['observedTo'],
    coverageState: window['coverageState'],
  };
}

// A execução recente continua separada do sucesso anterior e nunca é traduzida em sucesso presumido.
function parseLatestExecution(payload: unknown): TransactionLatestExecution {
  const execution = asRecord(payload);
  if (
    execution === null ||
    typeof execution['state'] !== 'string' ||
    typeof execution['coverageState'] !== 'string' ||
    !isNullableString(execution['failureCode'])
  ) {
    throw new TransactionListPayloadError();
  }

  return {
    state: execution['state'],
    coverageState: execution['coverageState'],
    failureCode: execution['failureCode'],
  };
}

// Projeta somente opções do catálogo e mantém a categoria virtual pelo ID nulo do contrato.
function parseCategoryOptions(payload: unknown): readonly CategoryOption[] {
  if (!Array.isArray(payload)) {
    throw new TransactionListPayloadError();
  }

  return payload.map(parseCategoryOption);
}

// Criação só é confirmada com ID próprio e kind pessoal retornados pelo servidor.
function parseCreatedCategory(payload: unknown): CategoryOption {
  const category = parseCategoryOption(payload);
  if (category.id === null || category.kind !== 'personal') {
    throw new TransactionListPayloadError();
  }

  return category;
}

// Projeta uma categoria isolada e exige coerência entre o tipo e o ID virtual nulo.
function parseCategoryOption(payload: unknown): CategoryOption {
  const option = asRecord(payload);
  const id = option?.['id'];
  const kind = option?.['kind'];
  if (
    option === null ||
    !isNullableGuid(id) ||
    typeof option['name'] !== 'string' ||
    !isCategoryKind(kind) ||
    (kind === 'unassigned') !== (id === null)
  ) {
    throw new TransactionListPayloadError();
  }

  return { id, name: option['name'], kind };
}

// A resposta PATCH confirma apenas categoria e observação persistidas.
function parseAnnotationResponse(payload: unknown): TransactionAnnotation {
  const annotation = asRecord(payload);
  if (
    annotation === null ||
    !isNullableGuid(annotation['categoryId']) ||
    !isNullableString(annotation['note'])
  ) {
    throw new TransactionListPayloadError();
  }

  return {
    categoryId: annotation['categoryId'],
    note: annotation['note'],
  };
}

// Valida a lista sem copiar propriedades desconhecidas ou detalhes internos para o estado da tela.
function parseRecurrenceCandidates(payload: unknown): readonly TransactionRecurrenceCandidate[] {
  if (!Array.isArray(payload)) {
    throw new TransactionListPayloadError();
  }

  return payload.map(parseRecurrenceCandidate);
}

// Exige exatamente três referências distintas e estados coerentes com o DTO revisado da API.
function parseRecurrenceCandidate(payload: unknown): TransactionRecurrenceCandidate {
  const candidate = asRecord(payload);
  const occurrenceIds = candidate?.['occurrenceTransactionIds'];
  const decision = candidate?.['decision'];
  if (
    candidate === null ||
    !isNonEmptyGuid(candidate['candidateId']) ||
    typeof candidate['policyVersion'] !== 'string' ||
    candidate['policyVersion'].length === 0 ||
    !Array.isArray(occurrenceIds) ||
    occurrenceIds.length !== 3 ||
    !occurrenceIds.every(isNonEmptyGuid) ||
    new Set(occurrenceIds).size !== 3 ||
    typeof candidate['isCurrent'] !== 'boolean' ||
    !(decision === null || isRecurrenceDecision(decision)) ||
    !isNullableString(candidate['decisionUpdatedAtUtc'])
  ) {
    throw new TransactionListPayloadError();
  }

  return {
    candidateId: candidate['candidateId'],
    policyVersion: candidate['policyVersion'],
    occurrenceTransactionIds: occurrenceIds,
    isCurrent: candidate['isCurrent'],
    decision,
    decisionUpdatedAtUtc: candidate['decisionUpdatedAtUtc'],
  };
}

// A gravação só é refletida localmente quando ID, decisão allowlisted e instante são reconhecidos.
function parseRecurrenceDecisionResponse(payload: unknown): TransactionRecurrenceDecisionResponse {
  const response = asRecord(payload);
  if (
    response === null ||
    !isNonEmptyGuid(response['candidateId']) ||
    !isRecurrenceDecision(response['decision']) ||
    typeof response['decisionUpdatedAtUtc'] !== 'string' ||
    response['decisionUpdatedAtUtc'].length === 0
  ) {
    throw new TransactionListPayloadError();
  }

  return {
    candidateId: response['candidateId'],
    decision: response['decision'],
    decisionUpdatedAtUtc: response['decisionUpdatedAtUtc'],
  };
}

// Constrói corpo parcial estrito sem transformar propriedade ausente em null nem aceitar campos extras.
function projectAnnotationPatch(patch: TransactionAnnotationPatch): TransactionAnnotationPatch {
  const input = asRecord(patch);
  if (
    input === null ||
    Object.keys(input).length === 0 ||
    Object.keys(input).some((key) => key !== 'categoryId' && key !== 'note')
  ) {
    throw new TransactionListRequestError();
  }

  const body: { categoryId?: string | null; note?: string | null } = {};
  if (Object.prototype.hasOwnProperty.call(input, 'categoryId')) {
    const categoryId = input['categoryId'];
    if (categoryId !== null && !isNonEmptyGuid(categoryId)) {
      throw new TransactionListRequestError();
    }
    body.categoryId = categoryId;
  }

  if (Object.prototype.hasOwnProperty.call(input, 'note')) {
    const note = input['note'];
    if (note !== null && (typeof note !== 'string' || note.length > 500)) {
      throw new TransactionListRequestError();
    }
    body.note = note;
  }

  return body;
}

// Valida identificadores internos antes de os expor a rotas ou controles da interface.
function isGuid(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  );
}

// Recusa também o GUID vazio, que o backend não aceita como identificador de recurso.
function isNonEmptyGuid(value: unknown): value is string {
  return isGuid(value) && value.toLowerCase() !== '00000000-0000-0000-0000-000000000000';
}

// A categoria virtual é o único caso válido de ID nulo no catálogo e na anotação.
function isNullableGuid(value: unknown): value is string | null {
  return value === null || isNonEmptyGuid(value);
}

// Mantém valores null explícitos, sem substituir metadados ausentes por texto arbitrário.
function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

// Os números monetários permanecem individuais e precisam ser finitos para a tela formatar com segurança.
function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

// Parcelas opcionais são contagens inteiras quando presentes, sem preencher ausência com zero.
function isNullableInteger(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isInteger(value));
}

// Valida os limites declarados pelo contrato HTTP antes de expor o envelope paginado.
function isPositiveInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0;
}

// O tamanho de página aceito é o intervalo 1–100 publicado pela API.
function isPageSize(value: unknown): value is number {
  return isPositiveInteger(value) && value <= 100;
}

// A contagem do conjunto pode ser zero, mas nunca negativa ou fracionária.
function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}

// Limita o tipo do catálogo às três opções previstas, sem interpolar novos códigos como texto de UI.
function isCategoryKind(value: unknown): value is CategoryKind {
  return value === 'system' || value === 'personal' || value === 'unassigned';
}

// A allowlist impede que códigos futuros ou strings remotas apareçam como decisões válidas.
function isRecurrenceDecision(value: unknown): value is TransactionRecurrenceDecision {
  return (
    value === 'Subscription' ||
    value === 'Installment' ||
    value === 'IndependentPurchases' ||
    value === 'Rejected'
  );
}
