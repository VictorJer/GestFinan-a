import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NORMALIZED_API_BASE_URL, NormalizedApiBaseUrl } from '../api-base-url.token';
import { SessionService } from './session';

export type ProtectedApiErrorKind =
  | 'unauthorized'
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'sync-active'
  | 'sync-interval'
  | 'acceptance-indeterminate'
  | 'network'
  | 'unavailable'
  | 'request-failed'
  | 'stale';

export interface SyncAcceptanceErrorMetadata {
  readonly syncRunId: string | null;
  readonly nextAllowedAtUtc: string | null;
  readonly retryAfterSeconds: number | null;
}

// Converte falhas protegidas em estados seguros, sem repassar payloads potencialmente sensíveis.
export class ProtectedApiRequestError extends Error {
  constructor(
    readonly kind: ProtectedApiErrorKind,
    readonly syncMetadata: SyncAcceptanceErrorMetadata | null = null,
  ) {
    const messages: Record<ProtectedApiErrorKind, string> = {
      unauthorized: 'A sessão não está mais válida. Entre novamente.',
      forbidden: 'Sua sessão não tem permissão para esta operação.',
      'not-found': 'O recurso solicitado não está disponível.',
      conflict: 'O recurso solicitado não pode ser selecionado sem resolver uma ambiguidade.',
      'sync-active': 'Já existe uma atualização em andamento para esta conexão.',
      'sync-interval': 'A conexão recebeu uma solicitação recentemente; aguarde antes de tentar novamente.',
      'acceptance-indeterminate': 'Não foi possível confirmar se a solicitação foi aceita.',
      network: 'Não foi possível conectar à API.',
      unavailable: 'A API está temporariamente indisponível.',
      'request-failed': 'Não foi possível concluir a operação.',
      stale: 'A solicitação pertence a uma sessão anterior.',
    };
    super(messages[kind]);
    this.name = 'ProtectedApiRequestError';
  }
}

// Distingue uma chamada protegida sem sessão de falhas HTTP reais, sem expor dados do request.
export class LocalSessionRequiredError extends Error {
  constructor() {
    super('É necessário entrar novamente para acessar esta operação.');
    this.name = 'LocalSessionRequiredError';
  }
}

// O token só sai para rotas da API configurada e protegidas pela sessão local atual.
export const apiAuthInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBase = inject(NORMALIZED_API_BASE_URL);
  const target = parseAbsoluteHttpUrl(request.urlWithParams);
  if (apiBase === null || target === null || !isProtectedApiRequest(request, target, apiBase)) {
    return next(withoutBearerHeader(request));
  }

  const sessionService = inject(SessionService);
  const session = sessionService.currentSession();
  if (session === null) {
    return throwError(() => new LocalSessionRequiredError());
  }

  return next(
    request.clone({
      setHeaders: { Authorization: `Bearer ${session.accessToken}` },
    }),
  ).pipe(
    // Só classifica respostas da geração que enviou o request; falhas antigas não afetam sessão nova.
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const currentSession = sessionService.currentSession();
      if (currentSession === null || currentSession.generation !== session.generation) {
        return throwError(() => new ProtectedApiRequestError('stale'));
      }

      if (error.status === 401) {
        if (!sessionService.endSessionForGeneration(session.generation, 'unauthorized')) {
          return throwError(() => new ProtectedApiRequestError('stale'));
        }

        return throwError(() => new ProtectedApiRequestError('unauthorized'));
      }

      if (error.status === 403) {
        sessionService.setNoticeForGeneration(session.generation, 'forbidden');
        return throwError(() => new ProtectedApiRequestError('forbidden'));
      }

      const isSyncAcceptance = isSyncAcceptanceRequest(request, target, apiBase);

      // Uma falha de transporte após POST não prova se o commit da intenção ocorreu; preserve a chave para replay.
      if (isSyncAcceptance && error.status === 0) {
        return throwError(() => new ProtectedApiRequestError('acceptance-indeterminate'));
      }

      // Só o código allowlisted distingue execução ativa de conexão ambígua, e o ID é validado antes da projeção.
      if (isSyncAcceptance && error.status === 409) {
        const problem = asRecord(error.error);
        if (problem?.['code'] === 'SYNC_RUN_ALREADY_ACTIVE') {
          return throwError(
            () =>
              new ProtectedApiRequestError('sync-active', {
                syncRunId: isGuid(problem['syncRunId']) ? problem['syncRunId'] : null,
                nextAllowedAtUtc: null,
                retryAfterSeconds: null,
              }),
          );
        }
      }

      // 429 na rota de intenção significa intervalo; campos auxiliares só são usados com o código esperado.
      if (isSyncAcceptance && error.status === 429) {
        const problem = asRecord(error.error);
        const hasExpectedCode = problem?.['code'] === 'SYNC_RUN_MINIMUM_INTERVAL_ACTIVE';
        return throwError(
          () =>
            new ProtectedApiRequestError(
              'sync-interval',
              hasExpectedCode
                ? {
                    syncRunId: null,
                    nextAllowedAtUtc: parseUtcTimestamp(problem['nextAllowedAtUtc']),
                    retryAfterSeconds: parseRetryAfter(error.headers.get('Retry-After')),
                  }
                : { syncRunId: null, nextAllowedAtUtc: null, retryAfterSeconds: null },
            ),
        );
      }

      // Qualquer 503 na aceitação permanece incerto, inclusive sem código; disponibilidade não comprova rollback.
      if (isSyncAcceptance && error.status === 503) {
        return throwError(() => new ProtectedApiRequestError('acceptance-indeterminate'));
      }

      // Ausência e ambiguidade são condições de domínio para a tela, não avisos globais de sessão.
      if (error.status === 404) {
        return throwError(() => new ProtectedApiRequestError('not-found'));
      }

      if (error.status === 409) {
        return throwError(() => new ProtectedApiRequestError('conflict'));
      }

      if (error.status === 0) {
        sessionService.setNoticeForGeneration(session.generation, 'network');
        return throwError(() => new ProtectedApiRequestError('network'));
      }

      if (error.status >= 500) {
        sessionService.setNoticeForGeneration(session.generation, 'unavailable');
        return throwError(() => new ProtectedApiRequestError('unavailable'));
      }

      sessionService.setNoticeForGeneration(session.generation, 'request-failed');
      return throwError(() => new ProtectedApiRequestError('request-failed'));
    }),
  );
};

// Recusa URLs relativas, esquemas não HTTP e destinos com credenciais embutidas.
function parseAbsoluteHttpUrl(value: string): URL | null {
  try {
    const url = new URL(value);
    if (
      (url.protocol !== 'http:' && url.protocol !== 'https:') ||
      url.username.length > 0 ||
      url.password.length > 0
    ) {
      return null;
    }

    return url;
  } catch {
    return null;
  }
}

// Compara origem e fronteira de segmento; um prefixo textual parecido não representa a mesma base.
function isProtectedApiRequest(
  request: HttpRequest<unknown>,
  target: URL,
  base: NormalizedApiBaseUrl,
): boolean {
  if (target.origin !== base.origin || request.method.toUpperCase() === 'OPTIONS') {
    return false;
  }

  const apiPath = base.pathPrefix.endsWith('/api')
    ? base.pathPrefix
    : `${base.pathPrefix}/api`;
  const protectedPathPrefix = `${apiPath}/`;
  if (!target.pathname.startsWith(protectedPathPrefix)) {
    return false;
  }

  const normalizedPath = target.pathname.replace(/\/+$/, '').toLowerCase();
  return normalizedPath !== `${apiPath}/auth/login`.toLowerCase();
}

// Restringe a leitura de metadados de aceite aos dois POSTs próprios da API configurada.
function isSyncAcceptanceRequest(
  request: HttpRequest<unknown>,
  target: URL,
  base: NormalizedApiBaseUrl,
): boolean {
  if (request.method.toUpperCase() !== 'POST' || target.origin !== base.origin) {
    return false;
  }

  const apiPath = base.pathPrefix.endsWith('/api')
    ? base.pathPrefix
    : `${base.pathPrefix}/api`;
  const normalizedApiPath = apiPath.replace(/\/+$/, '').toLowerCase();
  const normalizedRequestPath = target.pathname.replace(/\/+$/, '').toLowerCase();
  return [
    `${normalizedApiPath}/connections/current/sync`,
    `${normalizedApiPath}/connections/current/sync/on-entry`,
  ].includes(normalizedRequestPath);
}

// Lê somente objetos simples de ProblemDetails e ignora string, array e payloads inesperados.
function asRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null;
  }

  return value as Record<string, unknown>;
}

// Limita IDs encaminhados para acompanhamento ao formato GUID; não armazena texto livre do problema.
function isGuid(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  );
}

// Preserva somente timestamps ISO válidos com fuso explícito para apresentar a próxima elegibilidade.
function parseUtcTimestamp(value: unknown): string | null {
  if (
    typeof value !== 'string' ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ||
    !Number.isFinite(Date.parse(value))
  ) {
    return null;
  }

  return value;
}

// Aceita Retry-After somente como segundos decimais não negativos e limita valores absurdos da resposta.
function parseRetryAfter(value: string | null): number | null {
  if (value === null || !/^(0|[1-9]\d*)$/.test(value)) {
    return null;
  }

  const seconds = Number(value);
  return Number.isSafeInteger(seconds) && seconds <= 86_400 ? seconds : null;
}

// Impede que um Bearer previamente definido pelo chamador escape para login, URL externa ou página pública.
function withoutBearerHeader(request: HttpRequest<unknown>): HttpRequest<unknown> {
  const authorization = request.headers.get('Authorization');
  if (authorization === null || !/^Bearer(?:\s|$)/i.test(authorization)) {
    return request;
  }

  return request.clone({ headers: request.headers.delete('Authorization') });
}
