import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { defer, map, Observable, throwError } from 'rxjs';
import { NORMALIZED_API_BASE_URL, NormalizedApiBaseUrl } from '../../api-base-url.token';
import { LocalSessionRequiredError } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { InvestmentPortfolio, InvestmentPortfolioApi } from './investment-portfolio-api';
import { SessionScopedResponse } from './card-connection-api';

/** Entradas textuais do cálculo; dinheiro e taxa não são convertidos para Number no navegador. */
export interface InvestmentSimulationInput {
  readonly initialAmount: string;
  readonly monthlyContribution: string;
  readonly annualRatePercent: string;
  readonly years: number;
}

/** Ponto mensal allowlisted recebido da API, com valores monetários preservados como texto. */
export interface InvestmentSimulationPoint {
  readonly month: number;
  readonly totalContributed: string;
  readonly projectedValue: string;
}

/** Resultado completo do servidor; a interface não recalcula nem completa propriedades ausentes. */
export interface InvestmentSimulationResult {
  readonly points: readonly InvestmentSimulationPoint[];
  readonly totalContributed: string;
  readonly projectedValue: string;
  readonly projectedReturn: string;
}

/** Erro genérico para input ou resposta incompatível, sem preservar body HTTP ou valor recebido. */
export class InvestmentSimulationPayloadError extends Error {
  constructor() {
    super('A API retornou dados do simulador em formato não reconhecido.');
    this.name = 'InvestmentSimulationPayloadError';
  }
}

/** Sinaliza base ausente/inválida sem inferir host ou emitir rota relativa. */
export class InvestmentSimulationApiConfigurationError extends Error {
  constructor() {
    super('O simulador está indisponível porque a API não está configurada neste ambiente.');
    this.name = 'InvestmentSimulationApiConfigurationError';
  }
}

const SIMULATION_MONEY_PATTERN = /^(?:0|[1-9]\d*)\.\d{2}$/;
const MAXIMUM_INITIAL_AMOUNT = '10000000.00';
const MAXIMUM_MONTHLY_CONTRIBUTION = '1000000.00';
const MAXIMUM_ANNUAL_RATE_PERCENT = '50.00';

// Só publica o cliente com URL/configuração válidas e geração de sessão capturada ao iniciar o request.
@Injectable({ providedIn: 'root' })
export class InvestmentSimulationApi {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(NORMALIZED_API_BASE_URL);
  private readonly session = inject(SessionService);
  private readonly portfolioApi = inject(InvestmentPortfolioApi);

  // A validação local é estrutural; a API continua sendo a única responsável pela aritmética financeira.
  calculate(
    input: InvestmentSimulationInput,
  ): Observable<SessionScopedResponse<InvestmentSimulationResult>> {
    return defer(() => {
      const activeSession = this.session.currentSession();
      if (activeSession === null) {
        return throwError(() => new LocalSessionRequiredError());
      }

      if (this.apiBase === null) {
        return throwError(() => new InvestmentSimulationApiConfigurationError());
      }

      const body = validateSimulationInput(input);
      const url = buildSimulationUrl(this.apiBase);
      return this.http.post<unknown>(url, body, { observe: 'response' }).pipe(
        map((response) => {
          if (response.status !== 200) {
            throw new InvestmentSimulationPayloadError();
          }

          return {
            generation: activeSession.generation,
            value: parseSimulationResult(response.body, body.years),
          };
        }),
      );
    });
  }

  // A leitura reutiliza cliente/decoder da carteira e só ocorre quando a ação chamadora é explicitamente assinada.
  getPortfolio(): Observable<SessionScopedResponse<InvestmentPortfolio>> {
    return this.portfolioApi.getPortfolio();
  }
}

// Monta a rota sob o prefixo configurado, preservando proxy e sem acrescentar origens alternativas.
function buildSimulationUrl(apiBase: NormalizedApiBaseUrl): string {
  const apiPath = apiBase.pathPrefix.toLowerCase().endsWith('/api')
    ? apiBase.pathPrefix
    : `${apiBase.pathPrefix}/api`;
  return new URL(`${apiPath}/simulations/calculate`, apiBase.origin).toString();
}

// Restringe objetos recebidos ao contrato exato antes de ler qualquer propriedade tipada.
function asRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null;
  }

  return value as Record<string, unknown>;
}

// Campos extras são inválidos, não ignorados, para impedir mistura de versões ou dados inesperados.
function hasExactKeys(record: Record<string, unknown>, allowed: readonly string[]): boolean {
  const keys = Object.keys(record);
  return keys.length === allowed.length && allowed.every((key) => Object.hasOwn(record, key));
}

// O corpo é recriado allowlisted e mantém as quatro propriedades na forma HTTP aprovada.
function validateSimulationInput(input: InvestmentSimulationInput): InvestmentSimulationInput {
  const candidate = asRecord(input);
  if (
    candidate === null ||
    !hasExactKeys(candidate, [
      'initialAmount',
      'monthlyContribution',
      'annualRatePercent',
      'years',
    ]) ||
    !isWithinMaximum(candidate['initialAmount'], MAXIMUM_INITIAL_AMOUNT) ||
    !isWithinMaximum(candidate['monthlyContribution'], MAXIMUM_MONTHLY_CONTRIBUTION) ||
    !isWithinMaximum(candidate['annualRatePercent'], MAXIMUM_ANNUAL_RATE_PERCENT) ||
    typeof candidate['years'] !== 'number' ||
    !Number.isInteger(candidate['years']) ||
    candidate['years'] < 1 ||
    candidate['years'] > 30
  ) {
    throw new InvestmentSimulationPayloadError();
  }

  return {
    initialAmount: candidate['initialAmount'],
    monthlyContribution: candidate['monthlyContribution'],
    annualRatePercent: candidate['annualRatePercent'],
    years: candidate['years'],
  };
}

// Compara campos decimais como texto e limita os valores sem conversão binária nem arredondamento local.
function isWithinMaximum(value: unknown, maximum: string): value is string {
  if (typeof value !== 'string' || !SIMULATION_MONEY_PATTERN.test(value)) {
    return false;
  }

  const [integerPart, fractionalPart] = value.split('.');
  const [maximumInteger, maximumFraction] = maximum.split('.');
  if (integerPart.length !== maximumInteger.length) {
    return integerPart.length < maximumInteger.length;
  }

  if (integerPart !== maximumInteger) {
    return integerPart < maximumInteger;
  }

  return fractionalPart <= maximumFraction;
}

// Verifica objeto, cardinalidade e meses em sequência sem derivar valores monetários no browser.
function parseSimulationResult(payload: unknown, years: number): InvestmentSimulationResult {
  const result = asRecord(payload);
  if (
    result === null ||
    !hasExactKeys(result, ['points', 'totalContributed', 'projectedValue', 'projectedReturn']) ||
    !isSimulationMoney(result['totalContributed']) ||
    !isSimulationMoney(result['projectedValue']) ||
    !isSimulationMoney(result['projectedReturn']) ||
    !Array.isArray(result['points']) ||
    result['points'].length !== years * 12 + 1
  ) {
    throw new InvestmentSimulationPayloadError();
  }

  const points = result['points'].map((point, index) => parseSimulationPoint(point, index));
  return {
    points,
    totalContributed: result['totalContributed'],
    projectedValue: result['projectedValue'],
    projectedReturn: result['projectedReturn'],
  };
}

// O ponto precisa corresponder ao mês esperado e publicar somente os dois valores textuais permitidos.
function parseSimulationPoint(payload: unknown, expectedMonth: number): InvestmentSimulationPoint {
  const point = asRecord(payload);
  if (
    point === null ||
    !hasExactKeys(point, ['month', 'totalContributed', 'projectedValue']) ||
    typeof point['month'] !== 'number' ||
    !Number.isInteger(point['month']) ||
    point['month'] !== expectedMonth ||
    !isSimulationMoney(point['totalContributed']) ||
    !isSimulationMoney(point['projectedValue'])
  ) {
    throw new InvestmentSimulationPayloadError();
  }

  return {
    month: point['month'],
    totalContributed: point['totalContributed'],
    projectedValue: point['projectedValue'],
  };
}

// Valores HTTP seguem string decimal não negativa com ponto e exatamente duas casas.
function isSimulationMoney(value: unknown): value is string {
  return typeof value === 'string' && SIMULATION_MONEY_PATTERN.test(value);
}
