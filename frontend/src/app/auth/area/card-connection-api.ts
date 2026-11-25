import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { defer, map, Observable, throwError } from 'rxjs';
import { NORMALIZED_API_BASE_URL, NormalizedApiBaseUrl } from '../../api-base-url.token';
import { LocalSessionRequiredError } from '../api-auth.interceptor';
import { SessionService } from '../session';

/** Identifica a geração local que iniciou a leitura para rejeitar respostas atrasadas na camada de estado. */
export interface SessionScopedResponse<T> {
  readonly generation: number;
  readonly value: T;
}

/** Contém somente os campos permitidos pelo DTO de cartões; dados de limite ou da origem são descartados. */
export interface CardSummary {
  readonly id: string;
  readonly name: string | null;
  readonly lastFour: string | null;
  readonly currencyCode: string;
}

/** Resume a execução mais recente sem assumir cobertura quando o backend a declara desconhecida. */
export interface ConnectionSyncExecution {
  readonly syncRunId: string;
  readonly state: string;
  readonly createdAtUtc: string;
  readonly lastAttemptAtUtc: string | null;
  readonly finishedAtUtc: string | null;
  readonly coverageState: string;
  readonly failureCode: string | null;
}

/** Mantém horários ausentes como null e não mistura atualização da origem com sucesso persistido no Finora. */
export interface ConnectionSyncState {
  readonly updateNeeded: boolean;
  readonly reauthorizationRequired: boolean;
  readonly lastSuccessfulImportAtUtc: string | null;
  readonly sourceUpdatedAtUtc: string | null;
  readonly lastAttemptAtUtc: string | null;
  readonly nextAllowedAtUtc: string | null;
  readonly latestExecution: ConnectionSyncExecution | null;
}

/** Projeta o estado operacional da SyncRun sem expor conteúdo financeiro ou os campos remotos completos. */
export interface SyncRunStatus {
  readonly syncRunId: string;
  readonly state: string;
  readonly finishedAtUtc: string | null;
  readonly coverageState: string;
  readonly failureCode: string | null;
}

/** Expõe somente o resultado contratual de uma intenção; respostas remotas arbitrárias são descartadas. */
export type SyncIntentResponse =
  | { readonly kind: 'decision'; readonly decision: 'not_required' | 'reauthorization_required' }
  | { readonly kind: 'accepted'; readonly syncRunId: string };

/** Erro fixo para respostas 200 incompatíveis com o contrato, sem incluir payload remoto. */
export class CardConnectionPayloadError extends Error {
  constructor() {
    super('A API retornou dados em formato não reconhecido.');
    this.name = 'CardConnectionPayloadError';
  }
}

/** Sinaliza falta de configuração pública da API sem tentar inferir uma origem alternativa. */
export class CardConnectionApiConfigurationError extends Error {
  constructor() {
    super('A consulta está indisponível porque a API não está configurada neste ambiente.');
    this.name = 'CardConnectionApiConfigurationError';
  }
}

// Centraliza somente leituras financeiras autenticadas e deixa o interceptor controlar o Bearer permitido.
@Injectable({ providedIn: 'root' })
export class CardConnectionApi {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(NORMALIZED_API_BASE_URL);
  private readonly session = inject(SessionService);

  // Consulta cartões sem criar valores substitutos quando a resposta é vazia ou contém campos opcionais nulos.
  getCards(): Observable<SessionScopedResponse<readonly CardSummary[]>> {
    return this.get('cards', parseCardsResponse);
  }

  // Consulta o estado persistido sem disparar intenção automática, chamada à origem ou outro POST.
  getSyncState(): Observable<SessionScopedResponse<ConnectionSyncState>> {
    return this.get('connections/current/sync-state', parseConnectionSyncState);
  }

  // Avalia a política do servidor uma vez por sessão sem decidir elegibilidade pelo relógio do navegador.
  acceptOnEntry(idempotencyKey: string): Observable<SessionScopedResponse<SyncIntentResponse>> {
    return this.post('connections/current/sync/on-entry', idempotencyKey);
  }

  // Solicita modo manual em rota própria; o chamador conserva a mesma chave para um replay explícito.
  acceptManual(idempotencyKey: string): Observable<SessionScopedResponse<SyncIntentResponse>> {
    return this.post('connections/current/sync', idempotencyKey);
  }

  // Consulta uma execução somente pelo GUID validado e mantém a resposta vinculada à geração atual.
  getSyncRunStatus(syncRunId: string): Observable<SessionScopedResponse<SyncRunStatus>> {
    if (!isGuid(syncRunId)) {
      return throwError(() => new CardConnectionPayloadError());
    }

    return this.get(`sync-runs/${syncRunId}`, (payload) => parseSyncRunStatus(payload, syncRunId));
  }

  // Captura a geração no momento da assinatura e devolve-a junto do resultado para a UI descartar respostas antigas.
  private get<T>(
    route: string,
    decode: (payload: unknown) => T,
  ): Observable<SessionScopedResponse<T>> {
    return defer(() => {
      const activeSession = this.session.currentSession();
      if (activeSession === null) {
        return throwError(() => new LocalSessionRequiredError());
      }

      if (this.apiBase === null) {
        return throwError(() => new CardConnectionApiConfigurationError());
      }

      const url = buildApiUrl(this.apiBase, route);
      return this.http.get<unknown>(url).pipe(
        map((payload) => ({
          generation: activeSession.generation,
          value: decode(payload),
        })),
      );
    });
  }

  // Envia POST somente com sessão atual, chave ASCII visível e origem de API configurada.
  private post(
    route: string,
    idempotencyKey: string,
  ): Observable<SessionScopedResponse<SyncIntentResponse>> {
    return defer(() => {
      const activeSession = this.session.currentSession();
      if (activeSession === null) {
        return throwError(() => new LocalSessionRequiredError());
      }

      if (this.apiBase === null) {
        return throwError(() => new CardConnectionApiConfigurationError());
      }

      if (!isValidIdempotencyKey(idempotencyKey)) {
        return throwError(() => new CardConnectionPayloadError());
      }

      const url = buildApiUrl(this.apiBase, route);
      return this.http
        .post<unknown>(url, null, { headers: { 'Idempotency-Key': idempotencyKey } })
        .pipe(
          map((payload) => ({
            generation: activeSession.generation,
            value: parseSyncIntentResponse(payload),
          })),
        );
    });
  }
}

// Usa a base configurada e normalizada, sem fallback para origem relativa ou host inferido.
function buildApiUrl(apiBase: NormalizedApiBaseUrl, route: string): string {
  const apiPath = apiBase.pathPrefix.toLowerCase().endsWith('/api')
    ? apiBase.pathPrefix
    : `${apiBase.pathPrefix}/api`;
  return new URL(`${apiPath}/${route}`, apiBase.origin).toString();
}

// Aceita somente objetos JSON simples, evitando tratar arrays e null como DTOs válidos.
function asRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null;
  }

  return value as Record<string, unknown>;
}

// Projeta a allowlist do endpoint e conserva null nos campos opcionais sem inventar apresentação.
function parseCardsResponse(payload: unknown): readonly CardSummary[] {
  if (!Array.isArray(payload)) {
    throw new CardConnectionPayloadError();
  }

  return payload.map((item) => {
    const card = asRecord(item);
    if (
      card === null ||
      typeof card['id'] !== 'string' ||
      card['id'].length === 0 ||
      (card['name'] !== null && typeof card['name'] !== 'string') ||
      (card['lastFour'] !== null && typeof card['lastFour'] !== 'string') ||
      typeof card['currencyCode'] !== 'string'
    ) {
      throw new CardConnectionPayloadError();
    }

    return {
      id: card['id'],
      name: card['name'],
      lastFour: card['lastFour'],
      currencyCode: card['currencyCode'],
    };
  });
}

// Valida timestamps apenas como texto/null e preserva a cobertura desconhecida literalmente.
function parseConnectionSyncState(payload: unknown): ConnectionSyncState {
  const state = asRecord(payload);
  if (
    state === null ||
    typeof state['updateNeeded'] !== 'boolean' ||
    typeof state['reauthorizationRequired'] !== 'boolean' ||
    !isNullableString(state['lastSuccessfulImportAtUtc']) ||
    !isNullableString(state['sourceUpdatedAtUtc']) ||
    !isNullableString(state['lastAttemptAtUtc']) ||
    !isNullableString(state['nextAllowedAtUtc'])
  ) {
    throw new CardConnectionPayloadError();
  }

  const execution = state['latestExecution'];
  if (execution === null) {
    return {
      updateNeeded: state['updateNeeded'],
      reauthorizationRequired: state['reauthorizationRequired'],
      lastSuccessfulImportAtUtc: state['lastSuccessfulImportAtUtc'],
      sourceUpdatedAtUtc: state['sourceUpdatedAtUtc'],
      lastAttemptAtUtc: state['lastAttemptAtUtc'],
      nextAllowedAtUtc: state['nextAllowedAtUtc'],
      latestExecution: null,
    };
  }

  const latest = asRecord(execution);
  if (
    latest === null ||
    typeof latest['syncRunId'] !== 'string' ||
    latest['syncRunId'].length === 0 ||
    typeof latest['state'] !== 'string' ||
    typeof latest['createdAtUtc'] !== 'string' ||
    !isNullableString(latest['lastAttemptAtUtc']) ||
    !isNullableString(latest['finishedAtUtc']) ||
    typeof latest['coverageState'] !== 'string' ||
    !isNullableString(latest['failureCode'])
  ) {
    throw new CardConnectionPayloadError();
  }

  return {
    updateNeeded: state['updateNeeded'],
    reauthorizationRequired: state['reauthorizationRequired'],
    lastSuccessfulImportAtUtc: state['lastSuccessfulImportAtUtc'],
    sourceUpdatedAtUtc: state['sourceUpdatedAtUtc'],
    lastAttemptAtUtc: state['lastAttemptAtUtc'],
    nextAllowedAtUtc: state['nextAllowedAtUtc'],
    latestExecution: {
      syncRunId: latest['syncRunId'],
      state: latest['state'],
      createdAtUtc: latest['createdAtUtc'],
      lastAttemptAtUtc: latest['lastAttemptAtUtc'],
      finishedAtUtc: latest['finishedAtUtc'],
      coverageState: latest['coverageState'],
      failureCode: latest['failureCode'],
    },
  };
}

// Valida o formato de chave exigido pela API sem permitir espaços, controles ou caracteres não ASCII.
function isValidIdempotencyKey(value: string): boolean {
  return value.length > 0 && value.length <= 128 && /^[!-~]+$/.test(value);
}

// Distingue decisões 200 de aceitação 202 sem confiar em guidance ou campos adicionais do servidor.
function parseSyncIntentResponse(payload: unknown): SyncIntentResponse {
  const response = asRecord(payload);
  if (response === null) {
    throw new CardConnectionPayloadError();
  }

  if (
    response['decision'] === 'not_required' ||
    response['decision'] === 'reauthorization_required'
  ) {
    return { kind: 'decision', decision: response['decision'] };
  }

  if (typeof response['syncRunId'] === 'string' && isGuid(response['syncRunId'])) {
    return { kind: 'accepted', syncRunId: response['syncRunId'] };
  }

  throw new CardConnectionPayloadError();
}

// Projeta a resposta da SyncRun e rejeita ID divergente para impedir consulta/associação de outra execução.
function parseSyncRunStatus(payload: unknown, expectedSyncRunId: string): SyncRunStatus {
  const status = asRecord(payload);
  const responseSyncRunId = status?.['syncRunId'];
  if (
    status === null ||
    !isGuid(responseSyncRunId) ||
    responseSyncRunId.toLowerCase() !== expectedSyncRunId.toLowerCase() ||
    typeof status['state'] !== 'string' ||
    !isNullableString(status['finishedAtUtc']) ||
    typeof status['coverageState'] !== 'string' ||
    !isNullableString(status['failureCode'])
  ) {
    throw new CardConnectionPayloadError();
  }

  return {
    syncRunId: responseSyncRunId,
    state: status['state'],
    finishedAtUtc: status['finishedAtUtc'],
    coverageState: status['coverageState'],
    failureCode: status['failureCode'],
  };
}

// Aceita somente GUIDs no formato canônico para impedir que texto remoto seja usado como rota de acompanhamento.
function isGuid(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  );
}

// Ausência é explícita no contrato; strings arbitrárias não são substituídas por data/hora do cliente.
function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}
