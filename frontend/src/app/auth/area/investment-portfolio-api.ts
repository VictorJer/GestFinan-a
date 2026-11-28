import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { defer, map, Observable, throwError } from 'rxjs';
import { NORMALIZED_API_BASE_URL, NormalizedApiBaseUrl } from '../../api-base-url.token';
import { LocalSessionRequiredError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { SessionScopedResponse } from './card-connection-api';

/** Códigos estáveis de classe publicados pelo domínio da carteira manual. */
export type InvestmentClass = 'fixed_income' | 'variable_income' | 'real_estate_funds';

/** Valores monetários trafegam como strings para não passar por aritmética binária do navegador. */
export type InvestmentMoney = string;

/** Campos editáveis da posição; o identificador e o titular nunca são enviados pelo formulário. */
export interface InvestmentInput {
  readonly name: string;
  readonly class: InvestmentClass;
  readonly appliedAmount: InvestmentMoney;
  readonly currentAmount: InvestmentMoney;
}

/** Projeção allowlisted de uma posição manual persistida para o titular autenticado. */
export interface InvestmentItem extends InvestmentInput {
  readonly id: string;
}

/** Valor atual agrupado por classe, sem percentuais ou cálculo de rentabilidade no cliente. */
export interface InvestmentClassValue {
  readonly class: InvestmentClass;
  readonly label: string;
  readonly currentAmount: InvestmentMoney;
}

/** Resumo conciliado no backend; a diferença simples pode ser negativa. */
export interface InvestmentPortfolioSummary {
  readonly totalAppliedAmount: InvestmentMoney;
  readonly totalCurrentAmount: InvestmentMoney;
  readonly difference: InvestmentMoney;
  readonly currentAmountsByClass: readonly InvestmentClassValue[];
}

/** Lista e resumo chegam do mesmo contrato e não são reconstruídos a partir de movimentos do cartão. */
export interface InvestmentPortfolio {
  readonly items: readonly InvestmentItem[];
  readonly summary: InvestmentPortfolioSummary;
}

/** Erro fixo para entrada local ou resposta incompatível, sem propagar corpo remoto à interface. */
export class InvestmentPortfolioPayloadError extends Error {
  constructor() {
    super('A API retornou dados da carteira em formato não reconhecido.');
    this.name = 'InvestmentPortfolioPayloadError';
  }
}

/** Sinaliza ausência de URL pública da API sem inferir host ou usar rota relativa. */
export class InvestmentPortfolioApiConfigurationError extends Error {
  constructor() {
    super('A carteira está indisponível porque a API não está configurada neste ambiente.');
    this.name = 'InvestmentPortfolioApiConfigurationError';
  }
}

// Gramáticas espelham o contrato HTTP canônico; apenas os campos individuais têm faixa numeric(19,2).
const ITEM_MONEY_PATTERN = /^(?:0|[1-9]\d{0,16})\.\d{2}$/;
const SUMMARY_MONEY_PATTERN = /^(?:0|[1-9]\d*)\.\d{2}$/;
const SIGNED_SUMMARY_MONEY_PATTERN = /^(?:(?:0|[1-9]\d*)\.\d{2}|-(?:[1-9]\d*)\.\d{2})$/;
const INVESTMENT_CLASSES: readonly InvestmentClass[] = [
  'fixed_income',
  'variable_income',
  'real_estate_funds',
];

// Centraliza os verbos CRUD da carteira e captura a geração no momento da assinatura HTTP.
@Injectable({ providedIn: 'root' })
export class InvestmentPortfolioApi {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(NORMALIZED_API_BASE_URL);
  private readonly session = inject(SessionService);

  // Uma leitura única recebe posições e resumo já conciliados, sem consultar APIs de cartão.
  getPortfolio(): Observable<SessionScopedResponse<InvestmentPortfolio>> {
    return this.request('GET', 'investments', 200, parsePortfolio);
  }

  // A criação só é considerada confirmada com 201 e uma posição que respeita o contrato público.
  createInvestment(input: InvestmentInput): Observable<SessionScopedResponse<InvestmentItem>> {
    return this.request('POST', 'investments', 201, parseInvestmentItem, input);
  }

  // A atualização mantém o identificador validado na rota e os valores monetários como strings.
  updateInvestment(
    id: string,
    input: InvestmentInput,
  ): Observable<SessionScopedResponse<InvestmentItem>> {
    if (!isGuid(id)) {
      return throwError(() => new InvestmentPortfolioPayloadError());
    }

    return this.request('PUT', `investments/${id}`, 200, parseInvestmentItem, input);
  }

  // A exclusão só confirma sucesso com o 204 definido no contrato, sem aguardar corpo JSON.
  deleteInvestment(id: string): Observable<SessionScopedResponse<null>> {
    if (!isGuid(id)) {
      return throwError(() => new InvestmentPortfolioPayloadError());
    }

    return this.request('DELETE', `investments/${id}`, 204, parseEmptyResponse);
  }

  // Todas as chamadas usam URL configurada, sessão ativa, status esperado e decoder allowlisted.
  private request<T>(
    method: 'GET' | 'POST' | 'PUT' | 'DELETE',
    route: string,
    expectedStatus: number,
    decode: (payload: unknown) => T,
    input?: InvestmentInput,
  ): Observable<SessionScopedResponse<T>> {
    return defer(() => {
      const activeSession = this.session.currentSession();
      if (activeSession === null) {
        return throwError(() => new LocalSessionRequiredError());
      }

      if (this.apiBase === null) {
        return throwError(() => new InvestmentPortfolioApiConfigurationError());
      }

      let body: InvestmentInput | undefined;
      if (input !== undefined) {
        body = validateInvestmentInput(input);
      }

      const url = buildApiUrl(this.apiBase, route);
      return this.http.request<unknown>(method, url, { body, observe: 'response' }).pipe(
        map((response) => {
          if (response.status !== expectedStatus) {
            throw new InvestmentPortfolioPayloadError();
          }

          return {
            generation: activeSession.generation,
            value: decode(response.body),
          };
        }),
      );
    });
  }
}

// Usa a origem normalizada do build e mantém qualquer prefixo de proxy sem fallback implícito.
function buildApiUrl(apiBase: NormalizedApiBaseUrl, route: string): string {
  const apiPath = apiBase.pathPrefix.toLowerCase().endsWith('/api')
    ? apiBase.pathPrefix
    : `${apiBase.pathPrefix}/api`;
  return new URL(`${apiPath}/${route}`, apiBase.origin).toString();
}

// Aceita apenas objeto JSON, nunca arrays ou null como DTOs da API.
function asRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null;
  }

  return value as Record<string, unknown>;
}

// A allowlist exata impede que campos extras remotos sejam incorporados ao estado da interface.
function hasExactKeys(record: Record<string, unknown>, allowed: readonly string[]): boolean {
  const keys = Object.keys(record);
  return keys.length === allowed.length && allowed.every((key) => Object.hasOwn(record, key));
}

// Valida a estrutura completa do envelope antes de publicar qualquer posição ou agregado.
function parsePortfolio(payload: unknown): InvestmentPortfolio {
  const portfolio = asRecord(payload);
  if (
    portfolio === null ||
    !hasExactKeys(portfolio, ['items', 'summary']) ||
    !Array.isArray(portfolio['items'])
  ) {
    throw new InvestmentPortfolioPayloadError();
  }

  const items = portfolio['items'].map(parseInvestmentItem);
  const summary = parsePortfolioSummary(portfolio['summary']);
  return { items, summary };
}

// Aceita somente a projeção de item publicada e nunca converte centavos para Number.
function parseInvestmentItem(payload: unknown): InvestmentItem {
  const item = asRecord(payload);
  if (
    item === null ||
    !hasExactKeys(item, ['id', 'name', 'class', 'appliedAmount', 'currentAmount']) ||
    !isGuid(item['id']) ||
    typeof item['name'] !== 'string' ||
    item['name'].trim().length === 0 ||
    !isInvestmentClass(item['class']) ||
    !isItemMoney(item['appliedAmount']) ||
    !isItemMoney(item['currentAmount'])
  ) {
    throw new InvestmentPortfolioPayloadError();
  }

  return {
    id: item['id'],
    name: item['name'],
    class: item['class'],
    appliedAmount: item['appliedAmount'],
    currentAmount: item['currentAmount'],
  };
}

// Valida totais assinados e exige uma única entrada para cada classe publicada pelo domínio.
function parsePortfolioSummary(payload: unknown): InvestmentPortfolioSummary {
  const summary = asRecord(payload);
  if (
    summary === null ||
    !hasExactKeys(summary, [
      'totalAppliedAmount',
      'totalCurrentAmount',
      'difference',
      'currentAmountsByClass',
    ]) ||
    !isSummaryMoney(summary['totalAppliedAmount']) ||
    !isSummaryMoney(summary['totalCurrentAmount']) ||
    !isSignedSummaryMoney(summary['difference']) ||
    !Array.isArray(summary['currentAmountsByClass'])
  ) {
    throw new InvestmentPortfolioPayloadError();
  }

  const currentAmountsByClass = summary['currentAmountsByClass'].map(parseClassValue);
  if (
    currentAmountsByClass.length !== INVESTMENT_CLASSES.length ||
    new Set(currentAmountsByClass.map((value) => value.class)).size !== INVESTMENT_CLASSES.length ||
    !INVESTMENT_CLASSES.every((investmentClass) =>
      currentAmountsByClass.some((value) => value.class === investmentClass),
    )
  ) {
    throw new InvestmentPortfolioPayloadError();
  }

  return {
    totalAppliedAmount: summary['totalAppliedAmount'],
    totalCurrentAmount: summary['totalCurrentAmount'],
    difference: summary['difference'],
    currentAmountsByClass,
  };
}

// Projeta cada classe sem percentuais e preserva como texto até mesmo totais acima da precisão binária.
function parseClassValue(payload: unknown): InvestmentClassValue {
  const value = asRecord(payload);
  if (
    value === null ||
    !hasExactKeys(value, ['class', 'label', 'currentAmount']) ||
    !isInvestmentClass(value['class']) ||
    typeof value['label'] !== 'string' ||
    value['label'].trim().length === 0 ||
    !isSummaryMoney(value['currentAmount'])
  ) {
    throw new InvestmentPortfolioPayloadError();
  }

  return {
    class: value['class'],
    label: value['label'],
    currentAmount: value['currentAmount'],
  };
}

// Exige as quatro propriedades editáveis, códigos de classe conhecidos e valores em faixa persistível.
function validateInvestmentInput(input: InvestmentInput): InvestmentInput {
  const candidate = asRecord(input);
  if (
    candidate === null ||
    !hasExactKeys(candidate, ['name', 'class', 'appliedAmount', 'currentAmount']) ||
    typeof candidate['name'] !== 'string' ||
    candidate['name'].trim().length === 0 ||
    candidate['name'].length > 120 ||
    !isInvestmentClass(candidate['class']) ||
    !isItemMoney(candidate['appliedAmount']) ||
    !isItemMoney(candidate['currentAmount'])
  ) {
    throw new InvestmentPortfolioPayloadError();
  }

  return {
    name: candidate['name'],
    class: candidate['class'],
    appliedAmount: candidate['appliedAmount'],
    currentAmount: candidate['currentAmount'],
  };
}

// A resposta DELETE deve ser vazia, como definido pelo status 204 do contrato HTTP.
function parseEmptyResponse(payload: unknown): null {
  if (payload !== null) {
    throw new InvestmentPortfolioPayloadError();
  }

  return null;
}

// Valida gramática decimal invariável sem usar parseFloat, Number ou conversão localizada.
function isItemMoney(value: unknown): value is InvestmentMoney {
  return typeof value === 'string' && ITEM_MONEY_PATTERN.test(value);
}

// Totais não negativos não têm limite binário do JavaScript e continuam strings decimais fixas.
function isSummaryMoney(value: unknown): value is InvestmentMoney {
  return typeof value === 'string' && SUMMARY_MONEY_PATTERN.test(value);
}

// A diferença aceita sinal de menos somente para valores efetivamente negativos.
function isSignedSummaryMoney(value: unknown): value is InvestmentMoney {
  return typeof value === 'string' && SIGNED_SUMMARY_MONEY_PATTERN.test(value);
}

// Mantém fechado o vocabulário confirmado para não apresentar classes desconhecidas como válidas.
function isInvestmentClass(value: unknown): value is InvestmentClass {
  return typeof value === 'string' && INVESTMENT_CLASSES.includes(value as InvestmentClass);
}

// IDs só podem ser GUIDs canônicos antes de serem interpolados em uma rota protegida.
function isGuid(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  );
}
